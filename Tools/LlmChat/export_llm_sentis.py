#!/usr/bin/env python3
"""Export a causal-LM text decoder as a fixed-context ONNX graph for Sentis.

Works for standard dense transformers loadable via AutoModelForCausalLM
(current target: Qwen3-4B-Instruct-2507). Weights are exported as float32 so
the Unity-side converter can quantize them to uint8 predictably; initializers
above Sentis's 500 MB constant limit are split into offset-zero chunk files.
"""

from __future__ import annotations

import argparse
import contextlib
import copy
import json
import os
from pathlib import Path
import shutil
import sys
import time

import numpy as np
import torch
from torch import nn
from transformers import AutoConfig, AutoModelForCausalLM
from transformers.cache_utils import DynamicCache


if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


class LastTokenLogits(nn.Module):
    def __init__(self, model: nn.Module):
        super().__init__()
        self.model = model

    def forward(self, input_ids: torch.Tensor, attention_mask: torch.Tensor) -> torch.Tensor:
        output = self.model(
            input_ids=input_ids,
            attention_mask=attention_mask,
            use_cache=False,
            logits_to_keep=1,
            return_dict=True,
        )
        return output.logits


class PrefillWithCache(nn.Module):
    """Runs a fixed prompt and exports one owned K/V tensor pair per layer."""

    def __init__(self, model: nn.Module):
        super().__init__()
        self.model = model

    def forward(self, input_ids: torch.Tensor, attention_mask: torch.Tensor):
        output = self.model(
            input_ids=input_ids,
            attention_mask=attention_mask,
            use_cache=True,
            logits_to_keep=1,
            return_dict=True,
        )
        values = [output.logits]
        for layer in output.past_key_values.layers:
            values.extend((layer.keys, layer.values))
        return tuple(values)


class DecodeWithCache(nn.Module):
    """Decodes one token against a fixed-size rolling K/V cache."""

    def __init__(self, model: nn.Module, layer_count: int):
        super().__init__()
        self.model = model
        self.layer_count = layer_count

    def forward(
        self,
        input_ids: torch.Tensor,
        attention_mask: torch.Tensor,
        position_ids: torch.Tensor,
        *past: torch.Tensor,
    ):
        cache = DynamicCache(
            ddp_cache_data=[
                (past[layer * 2], past[layer * 2 + 1])
                for layer in range(self.layer_count)
            ]
        )
        output = self.model(
            input_ids=input_ids,
            attention_mask=attention_mask,
            position_ids=position_ids,
            past_key_values=cache,
            use_cache=True,
            logits_to_keep=1,
            return_dict=True,
        )
        values = [output.logits]
        for layer in output.past_key_values.layers:
            # Drop the oldest physical slot so every decode has the same input/output
            # cache shape. Attention-mask zeros occupy these slots until the window fills.
            values.extend((layer.keys[..., 1:, :], layer.values[..., 1:, :]))
        return tuple(values)


def create_tiny_model(model_dir: Path, context_length: int) -> nn.Module:
    """A 4-layer random-weight model with the source architecture, for fast tests."""
    source = AutoConfig.from_pretrained(model_dir, local_files_only=True)
    values = copy.deepcopy(source.to_dict())
    values.update(
        vocab_size=512,
        hidden_size=64,
        intermediate_size=128,
        num_hidden_layers=4,
        layer_types=["full_attention"] * 4,
        num_attention_heads=4,
        num_key_value_heads=2,
        head_dim=16,
        max_position_embeddings=context_length,
        use_cache=True,
        tie_word_embeddings=True,
        bos_token_id=None,
        eos_token_id=None,
        pad_token_id=None,
    )
    values.pop("torch_dtype", None)
    values.pop("dtype", None)
    config = type(source)(**values)
    model = AutoModelForCausalLM.from_config(config, attn_implementation="eager")
    return model.float().eval()


def load_text_model(model_dir: Path) -> nn.Module:
    config = AutoConfig.from_pretrained(model_dir, local_files_only=True)
    config.use_cache = True
    # Load in the checkpoint's native bfloat16 (plain mmap copy), then cast with a
    # torch op: converting per-shard during load (dtype=float32) crashes natively
    # (0xC0000005) in this environment.
    model = AutoModelForCausalLM.from_pretrained(
        model_dir,
        config=config,
        local_files_only=True,
        device_map="cpu",
        dtype=torch.bfloat16,
        attn_implementation="eager",
    )
    return model.float().eval()


def export_onnx(
    wrapper: nn.Module,
    output_path: Path,
    context_length: int,
    opset: int,
    sample_token_ids: tuple[int, int],
) -> None:
    input_ids = torch.zeros((1, context_length), dtype=torch.int64)
    attention_mask = torch.zeros((1, context_length), dtype=torch.int64)
    input_ids[0, -2:] = torch.tensor(sample_token_ids, dtype=torch.int64)
    attention_mask[0, -2:] = 1

    output_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        wrapper,
        (input_ids, attention_mask),
        str(output_path),
        input_names=["input_ids", "attention_mask"],
        output_names=["logits"],
        opset_version=opset,
        dynamo=True,
        external_data=True,
        optimize=False,
    )


def cache_names(layer_count: int) -> list[str]:
    return [
        f"present_{layer}_{kind}"
        for layer in range(layer_count)
        for kind in ("key", "value")
    ]


def past_names(layer_count: int) -> list[str]:
    return [
        f"past_{layer}_{kind}"
        for layer in range(layer_count)
        for kind in ("key", "value")
    ]


def export_cached_onnx(
    model: nn.Module,
    prefill_path: Path,
    decode_path: Path,
    context_length: int,
    opset: int,
    sample_token_ids: tuple[int, int],
) -> None:
    export_prefill_onnx(
        model, prefill_path, context_length, opset, sample_token_ids
    )
    export_decode_onnx(
        model, decode_path, context_length, opset, sample_token_ids
    )


def export_prefill_onnx(
    model: nn.Module,
    prefill_path: Path,
    context_length: int,
    opset: int,
    sample_token_ids: tuple[int, int],
) -> None:
    cache_length = context_length - 1
    layer_count = model.config.num_hidden_layers
    prefill_ids = torch.zeros((1, cache_length), dtype=torch.int64)
    prefill_mask = torch.zeros((1, cache_length), dtype=torch.int64)
    prefill_ids[0, -2:] = torch.tensor(sample_token_ids, dtype=torch.int64)
    prefill_mask[0, -2:] = 1
    prefill_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        PrefillWithCache(model).eval(),
        (prefill_ids, prefill_mask),
        str(prefill_path),
        input_names=["input_ids", "attention_mask"],
        output_names=["logits", *cache_names(layer_count)],
        opset_version=opset,
        dynamo=True,
        external_data=True,
        optimize=False,
    )


def export_decode_onnx(
    model: nn.Module,
    decode_path: Path,
    context_length: int,
    opset: int,
    sample_token_ids: tuple[int, int],
) -> None:
    cache_length = context_length - 1
    layer_count = model.config.num_hidden_layers
    kv_heads = model.config.num_key_value_heads
    head_dim = getattr(
        model.config,
        "head_dim",
        model.config.hidden_size // model.config.num_attention_heads,
    )
    prefill_mask = torch.zeros((1, cache_length), dtype=torch.int64)
    prefill_mask[0, -2:] = 1
    decode_ids = torch.tensor([[sample_token_ids[-1]]], dtype=torch.int64)
    decode_mask = torch.cat(
        (prefill_mask, torch.ones((1, 1), dtype=torch.int64)), dim=1
    )
    position_ids = torch.tensor([[2]], dtype=torch.int64)
    empty_cache = tuple(
        torch.zeros((1, kv_heads, cache_length, head_dim), dtype=torch.float32)
        for _ in range(layer_count * 2)
    )
    decode_path.parent.mkdir(parents=True, exist_ok=True)
    torch.onnx.export(
        DecodeWithCache(model, layer_count).eval(),
        (decode_ids, decode_mask, position_ids, *empty_cache),
        str(decode_path),
        input_names=[
            "input_ids",
            "attention_mask",
            "position_ids",
            *past_names(layer_count),
        ],
        output_names=["logits", *cache_names(layer_count)],
        opset_version=opset,
        dynamo=True,
        external_data=True,
        optimize=False,
    )


MAX_SENTIS_CONSTANT_BYTES = 500_000_000


def element_size(data_type: int) -> int:
    import onnx

    return {
        onnx.TensorProto.FLOAT: 4,
        onnx.TensorProto.FLOAT16: 2,
    }.get(data_type, 0)


def split_large_matrix_initializer(
    model,
    tensor,
    source_path: Path,
    source_offset: int,
    weights_dir: Path,
    external_count: int,
    chunk_dimension: int = 2048,
) -> tuple[int, int, int]:
    """Split a large float matrix and reconstruct its Gather/Transpose consumers."""
    import onnx

    stride = element_size(tensor.data_type)
    if stride == 0 or len(tensor.dims) != 2:
        raise RuntimeError(
            f"Cannot split oversized non-float-matrix initializer: {tensor.name}"
        )
    dtype = np.float32 if stride == 4 else np.float16

    consumers = [node for node in model.graph.node if tensor.name in node.input]
    if not consumers or any(node.op_type not in ("Gather", "Transpose") for node in consumers):
        raise RuntimeError(
            f"Oversized initializer has unsupported consumers: {tensor.name}"
        )

    rows, columns = tensor.dims
    column_ranges = [
        (start, min(start + chunk_dimension, columns))
        for start in range(0, columns, chunk_dimension)
    ]
    chunk_names = []
    target_paths = []
    chunk_bytes = 0
    for chunk_index, (start, end) in enumerate(column_ranges):
        length = rows * (end - start) * stride
        if length > MAX_SENTIS_CONSTANT_BYTES:
            raise RuntimeError(f"Split initializer is still too large: {tensor.name}")

        target_name = f"{external_count + chunk_index:04d}.bin"
        chunk_name = f"{tensor.name}.sentis_chunk_{chunk_index}"
        chunk_tensor = onnx.TensorProto()
        chunk_tensor.name = chunk_name
        chunk_tensor.data_type = tensor.data_type
        chunk_tensor.dims.extend([rows, end - start])
        chunk_tensor.data_location = onnx.TensorProto.EXTERNAL
        for key, value in (("location", f"{weights_dir.name}/{target_name}"), ("length", str(length))):
            entry = chunk_tensor.external_data.add()
            entry.key = key
            entry.value = value
        model.graph.initializer.append(chunk_tensor)
        chunk_names.append(chunk_name)
        target_paths.append(weights_dir / target_name)
        chunk_bytes += length

    with source_path.open("rb") as source, contextlib.ExitStack() as stack:
        targets = [stack.enter_context(path.open("wb")) for path in target_paths]
        source.seek(source_offset)
        rows_per_block = 4096
        for row_start in range(0, rows, rows_per_block):
            row_count = min(rows_per_block, rows - row_start)
            raw = source.read(row_count * columns * stride)
            if len(raw) != row_count * columns * stride:
                raise EOFError(f"Unexpected end of external data for {tensor.name}")
            values = np.frombuffer(raw, dtype=dtype).reshape(row_count, columns)
            for target, (start, end) in zip(targets, column_ranges):
                target.write(values[:, start:end].tobytes(order="C"))

    transpose_consumers = [node for node in consumers if node.op_type == "Transpose"]
    projection_names = []
    if transpose_consumers:
        projection_row_count = 16_384
        projection_ranges = [
            (start, min(start + projection_row_count, rows))
            for start in range(0, rows, projection_row_count)
        ]
        with source_path.open("rb") as source:
            for projection_index, (start, end) in enumerate(projection_ranges):
                row_count = end - start
                length = row_count * columns * stride
                target_name = (
                    f"{external_count + len(column_ranges) + projection_index:04d}.bin"
                )
                projection_name = (
                    f"{tensor.name}.sentis_projection_chunk_{projection_index}"
                )
                source.seek(source_offset + start * columns * stride)
                raw = source.read(length)
                if len(raw) != length:
                    raise EOFError(
                        f"Unexpected end of projection data for {tensor.name}"
                    )
                values = np.frombuffer(raw, dtype=dtype).reshape(
                    row_count, columns
                )
                (weights_dir / target_name).write_bytes(
                    values.transpose(1, 0).tobytes(order="C")
                )

                projection_tensor = onnx.TensorProto()
                projection_tensor.name = projection_name
                projection_tensor.data_type = tensor.data_type
                projection_tensor.dims.extend([columns, row_count])
                projection_tensor.data_location = onnx.TensorProto.EXTERNAL
                for key, value in (
                    ("location", f"{weights_dir.name}/{target_name}"),
                    ("length", str(length)),
                ):
                    entry = projection_tensor.external_data.add()
                    entry.key = key
                    entry.value = value
                model.graph.initializer.append(projection_tensor)
                projection_names.append(projection_name)
                chunk_bytes += length

    nodes = list(model.graph.node)
    for consumer in consumers:
        if consumer.op_type == "Transpose":
            matmul_consumers = [
                node
                for node in nodes
                if node.op_type == "MatMul" and consumer.output[0] in node.input
            ]
            if len(matmul_consumers) != 1:
                raise RuntimeError(
                    f"Large Transpose must feed one MatMul: {consumer.name}"
                )
            matmul = matmul_consumers[0]
            if len(matmul.input) != 2 or matmul.input[1] != consumer.output[0]:
                raise RuntimeError(
                    f"Large Transpose must be the right MatMul input: {consumer.name}"
                )

            projection_outputs = [
                f"{matmul.output[0]}.sentis_projection_chunk_{index}"
                for index in range(len(projection_names))
            ]
            replacement_nodes = []
            for projection_index, (projection_name, output_name) in enumerate(
                zip(projection_names, projection_outputs)
            ):
                node = onnx.helper.make_node(
                    "MatMul",
                    [matmul.input[0], projection_name],
                    [output_name],
                    name=f"{matmul.name}.sentis_projection_chunk_{projection_index}",
                )
                node.attribute.extend(copy.deepcopy(matmul.attribute))
                replacement_nodes.append(node)
            replacement_nodes.append(
                onnx.helper.make_node(
                    "Concat",
                    projection_outputs,
                    list(matmul.output),
                    name=f"{matmul.name}.sentis_projection_concat",
                    axis=-1,
                )
            )

            nodes.remove(consumer)
            matmul_index = nodes.index(matmul)
            nodes[matmul_index : matmul_index + 1] = replacement_nodes
            continue

        replacement_nodes = []
        consumer_outputs = [
            f"{consumer.output[0]}.sentis_chunk_{index}"
            for index in range(len(chunk_names))
        ]
        for chunk_index, (chunk_name, output_name) in enumerate(
            zip(chunk_names, consumer_outputs)
        ):
            node = onnx.helper.make_node(
                "Gather",
                [chunk_name, consumer.input[1]],
                [output_name],
                name=f"{consumer.name}.sentis_chunk_{chunk_index}",
            )
            node.attribute.extend(copy.deepcopy(consumer.attribute))
            replacement_nodes.append(node)

        replacement_nodes.append(
            onnx.helper.make_node(
                "Concat",
                consumer_outputs,
                list(consumer.output),
                name=f"{consumer.name}.sentis_concat",
                axis=-1,
            )
        )

        consumer_index = nodes.index(consumer)
        nodes[consumer_index : consumer_index + 1] = replacement_nodes

    del model.graph.node[:]
    model.graph.node.extend(nodes)
    model.graph.initializer.remove(tensor)
    files_written = len(column_ranges) + len(projection_names)
    return len(column_ranges), chunk_bytes, files_written


def fold_transposed_weight_initializers(model, base_dir: Path) -> int:
    """Fold Constant -> Transpose([1,0]) -> MatMul weights into pre-transposed data.

    The dynamo export leaves every linear weight behind a Transpose node. Folding it
    into the initializer removes ~250 runtime layers and halves the transient memory
    of the dequantize/transpose chain, which matters for GPU inference. Expects every
    external initializer to already live in its own offset-zero file (run after
    splitting). Returns the number of folded weights.
    """
    import onnx

    initializers = {t.name: t for t in model.graph.initializer}
    consumers: dict[str, list] = {}
    for node in model.graph.node:
        for name in node.input:
            consumers.setdefault(name, []).append(node)

    folded = 0
    for node in list(model.graph.node):
        if node.op_type != "Transpose":
            continue
        perm = next((list(a.ints) for a in node.attribute if a.name == "perm"), None)
        if perm != [1, 0]:
            continue
        tensor = initializers.get(node.input[0])
        if tensor is None or len(tensor.dims) != 2:
            continue
        if len(consumers.get(node.input[0], [])) != 1:
            continue
        stride = element_size(tensor.data_type)
        if stride == 0:
            continue
        values = {entry.key: entry.value for entry in tensor.external_data}
        location = values.get("location")
        if not location or int(values.get("offset", "0")) != 0:
            continue

        rows, columns = tensor.dims
        path = base_dir / location
        dtype = np.float32 if stride == 4 else np.float16
        data = np.fromfile(path, dtype=dtype)
        if data.size != rows * columns:
            raise RuntimeError(f"External data size mismatch for {tensor.name}")
        data.reshape(rows, columns).transpose(1, 0).copy().tofile(path)
        tensor.dims[:] = [columns, rows]

        transposed_output = node.output[0]
        for consumer in consumers.get(transposed_output, []):
            for i, name in enumerate(consumer.input):
                if name == transposed_output:
                    consumer.input[i] = tensor.name
        model.graph.node.remove(node)
        folded += 1

    return folded


def split_external_data_files(
    source_onnx: Path,
    output_path: Path,
    delete_source_data: bool = False,
) -> tuple[int, int, int]:
    """Give every external initializer an offset-zero file readable by Sentis 2.6.1."""
    import onnx

    model = onnx.load(str(source_onnx), load_external_data=False)
    weights_dir = output_path.with_suffix("").with_name(output_path.stem + ".weights")
    if weights_dir.exists():
        shutil.rmtree(weights_dir)
    weights_dir.mkdir(parents=True)

    source_locations: set[str] = set()
    external_count = 0
    external_bytes = 0
    embedding_chunks = 1
    for tensor in list(model.graph.initializer):
        values = {entry.key: entry.value for entry in tensor.external_data}
        location = values.get("location")
        if not location:
            continue

        source_locations.add(location)
        offset = int(values.get("offset", "0"))
        length = int(values.get("length", "0"))
        if length <= 0:
            raise RuntimeError(f"External initializer has no length: {tensor.name}")
        if element_size(tensor.data_type) > 0 and length > MAX_SENTIS_CONSTANT_BYTES:
            chunks, bytes_written, files_written = split_large_matrix_initializer(
                model,
                tensor,
                source_onnx.parent / location,
                offset,
                weights_dir,
                external_count,
                chunk_dimension=512,
            )
            embedding_chunks = chunks
            external_count += files_written
            external_bytes += bytes_written
            continue

        source_path = source_onnx.parent / location
        target_name = f"{external_count:04d}.bin"
        target_path = weights_dir / target_name
        with source_path.open("rb") as source, target_path.open("wb") as target:
            source.seek(offset)
            remaining = length
            while remaining:
                block = source.read(min(16 * 1024 * 1024, remaining))
                if not block:
                    raise EOFError(f"Unexpected end of external data for {tensor.name}")
                target.write(block)
                remaining -= len(block)

        del tensor.external_data[:]
        entry = tensor.external_data.add()
        entry.key = "location"
        entry.value = f"{weights_dir.name}/{target_name}"
        entry = tensor.external_data.add()
        entry.key = "length"
        entry.value = str(length)
        external_count += 1
        external_bytes += length

    fold_transposed_weight_initializers(model, output_path.parent)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save_model(model, str(output_path))
    if delete_source_data:
        for location in source_locations:
            source_path = source_onnx.parent / location
            if source_path.is_file() and source_path.parent != weights_dir:
                source_path.unlink()

    return external_count, external_bytes, embedding_chunks


def verify_onnx(
    output_path: Path,
    context_length: int,
    vocab_size: int,
    sample_token_ids: tuple[int, int],
) -> None:
    import onnx
    import onnxruntime as ort

    onnx.checker.check_model(str(output_path))
    session = ort.InferenceSession(str(output_path), providers=["CPUExecutionProvider"])
    input_ids = np.zeros((1, context_length), dtype=np.int64)
    attention_mask = np.zeros((1, context_length), dtype=np.int64)
    input_ids[0, -2:] = sample_token_ids
    attention_mask[0, -2:] = 1
    logits = session.run(
        ["logits"], {"input_ids": input_ids, "attention_mask": attention_mask}
    )[0]
    expected = (1, 1, vocab_size)
    if logits.shape != expected:
        raise RuntimeError(f"Expected logits {expected}, received {logits.shape}.")
    if not np.isfinite(logits).all():
        raise RuntimeError("ONNX logits contain NaN or infinity.")


def verify_cached_onnx(
    prefill_path: Path,
    decode_path: Path,
    context_length: int,
    vocab_size: int,
    layer_count: int,
    kv_heads: int,
    head_dim: int,
    sample_token_ids: tuple[int, int],
) -> None:
    import onnx
    import onnxruntime as ort

    onnx.checker.check_model(str(prefill_path))
    onnx.checker.check_model(str(decode_path))
    cache_length = context_length - 1
    ids = np.zeros((1, cache_length), dtype=np.int64)
    mask = np.zeros((1, cache_length), dtype=np.int64)
    ids[0, -2:] = sample_token_ids
    mask[0, -2:] = 1

    prefill = ort.InferenceSession(str(prefill_path), providers=["CPUExecutionProvider"])
    prefill_values = prefill.run(None, {"input_ids": ids, "attention_mask": mask})
    if prefill_values[0].shape != (1, 1, vocab_size):
        raise RuntimeError(f"Unexpected prefill logits: {prefill_values[0].shape}")

    expected_cache = (1, kv_heads, cache_length, head_dim)
    if len(prefill_values) != 1 + layer_count * 2:
        raise RuntimeError("Prefill did not return one K/V pair per transformer layer.")
    if any(value.shape != expected_cache for value in prefill_values[1:]):
        raise RuntimeError("Prefill returned an unexpected K/V cache shape.")

    decode_inputs = {
        "input_ids": np.array([[sample_token_ids[-1]]], dtype=np.int64),
        "attention_mask": np.concatenate(
            (mask, np.ones((1, 1), dtype=np.int64)), axis=1
        ),
        "position_ids": np.array([[2]], dtype=np.int64),
    }
    decode_inputs.update(zip(past_names(layer_count), prefill_values[1:]))
    decode = ort.InferenceSession(str(decode_path), providers=["CPUExecutionProvider"])
    decode_values = decode.run(None, decode_inputs)
    if decode_values[0].shape != (1, 1, vocab_size):
        raise RuntimeError(f"Unexpected decode logits: {decode_values[0].shape}")
    if any(value.shape != expected_cache for value in decode_values[1:]):
        raise RuntimeError("Decode returned an unexpected K/V cache shape.")
    if not all(np.isfinite(value).all() for value in decode_values):
        raise RuntimeError("Cached ONNX output contains NaN or infinity.")


def verify_decode_onnx(
    decode_path: Path,
    context_length: int,
    vocab_size: int,
    layer_count: int,
    kv_heads: int,
    head_dim: int,
    sample_token_id: int,
) -> None:
    import onnx
    import onnxruntime as ort

    onnx.checker.check_model(str(decode_path))
    cache_shape = (1, kv_heads, context_length - 1, head_dim)
    inputs = {
        "input_ids": np.array([[sample_token_id]], dtype=np.int64),
        "attention_mask": np.concatenate(
            (
                np.zeros((1, context_length - 1), dtype=np.int64),
                np.ones((1, 1), dtype=np.int64),
            ),
            axis=1,
        ),
        "position_ids": np.array([[0]], dtype=np.int64),
    }
    inputs.update(
        (name, np.zeros(cache_shape, dtype=np.float32))
        for name in past_names(layer_count)
    )
    session = ort.InferenceSession(str(decode_path), providers=["CPUExecutionProvider"])
    values = session.run(None, inputs)
    if values[0].shape != (1, 1, vocab_size):
        raise RuntimeError(f"Unexpected decode logits: {values[0].shape}")
    if any(value.shape != cache_shape for value in values[1:]):
        raise RuntimeError("Decode returned an unexpected K/V cache shape.")
    if not all(np.isfinite(value).all() for value in values):
        raise RuntimeError("Decode output contains NaN or infinity.")


def write_metadata(
    output_path: Path,
    model_dir: Path,
    context_length: int,
    vocab_size: int,
    tiny: bool,
) -> None:
    metadata = {
        "source_model": str(model_dir.resolve()),
        "context_length": context_length,
        "vocab_size": vocab_size,
        "inputs": {"input_ids": [1, context_length], "attention_mask": [1, context_length]},
        "output": {"logits": [1, 1, vocab_size]},
        "use_cache": False,
        "tiny_validation_model": tiny,
    }
    output_path.with_suffix(".json").write_text(
        json.dumps(metadata, indent=2), encoding="utf-8"
    )


def write_cache_metadata(
    prefill_path: Path,
    decode_path: Path,
    model_dir: Path,
    context_length: int,
    vocab_size: int,
    layer_count: int,
    kv_heads: int,
    head_dim: int,
    tiny: bool,
) -> None:
    common = {
        "source_model": str(model_dir.resolve()),
        "context_length": context_length,
        "cache_length": context_length - 1,
        "vocab_size": vocab_size,
        "layer_count": layer_count,
        "kv_heads": kv_heads,
        "head_dim": head_dim,
        "use_cache": True,
        "tiny_validation_model": tiny,
    }
    prefill = {
        **common,
        "kind": "prefill",
        "inputs": {
            "input_ids": [1, context_length - 1],
            "attention_mask": [1, context_length - 1],
        },
    }
    decode = {
        **common,
        "kind": "decode",
        "inputs": {
            "input_ids": [1, 1],
            "attention_mask": [1, context_length],
            "position_ids": [1, 1],
            "past_*": [1, kv_heads, context_length - 1, head_dim],
        },
    }
    prefill_path.with_suffix(".json").write_text(
        json.dumps(prefill, indent=2), encoding="utf-8"
    )
    decode_path.with_suffix(".json").write_text(
        json.dumps(decode, indent=2), encoding="utf-8"
    )


def write_decode_metadata(
    decode_path: Path,
    model_dir: Path,
    context_length: int,
    vocab_size: int,
    layer_count: int,
    kv_heads: int,
    head_dim: int,
    tiny: bool,
) -> None:
    metadata = {
        "source_model": str(model_dir.resolve()),
        "context_length": context_length,
        "cache_length": context_length - 1,
        "vocab_size": vocab_size,
        "layer_count": layer_count,
        "kv_heads": kv_heads,
        "head_dim": head_dim,
        "use_cache": True,
        "tiny_validation_model": tiny,
        "kind": "decode",
        "inputs": {
            "input_ids": [1, 1],
            "attention_mask": [1, context_length],
            "position_ids": [1, 1],
            "past_*": [1, kv_heads, context_length - 1, head_dim],
        },
    }
    decode_path.with_suffix(".json").write_text(
        json.dumps(metadata, indent=2), encoding="utf-8"
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", type=Path)
    parser.add_argument(
        "--source-onnx",
        type=Path,
        help="Prepare an existing ONNX export without loading the Hugging Face model.",
    )
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--decode-output",
        type=Path,
        help="Also export fixed-shape KV-cache prefill/decode graphs; --output is prefill.",
    )
    parser.add_argument(
        "--decode-only",
        action="store_true",
        help="Export only the persistent one-token KV decode graph to --output.",
    )
    parser.add_argument("--context-length", type=int, default=128)
    parser.add_argument("--opset", type=int, default=20)
    parser.add_argument("--tiny", action="store_true")
    parser.add_argument("--verify-onnxruntime", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    os.environ.setdefault("HF_HUB_OFFLINE", "1")
    os.environ.setdefault("TRANSFORMERS_OFFLINE", "1")
    os.environ.setdefault("TOKENIZERS_PARALLELISM", "false")

    if args.context_length < 8:
        raise ValueError("context-length must be at least 8.")
    if args.decode_only and args.decode_output:
        raise ValueError("--decode-only and --decode-output are mutually exclusive.")

    started = time.perf_counter()
    if args.source_onnx:
        external_file_count, external_size, embedding_chunks = split_external_data_files(
            args.source_onnx,
            args.output,
        )
        source_metadata_path = args.source_onnx.with_suffix(".json")
        source_metadata = None
        if source_metadata_path.is_file():
            source_metadata = json.loads(source_metadata_path.read_text(encoding="utf-8"))
            shutil.copy2(source_metadata_path, args.output.with_suffix(".json"))
        if args.verify_onnxruntime and source_metadata:
            verify_onnx(
                args.output,
                source_metadata["context_length"],
                source_metadata["vocab_size"],
                (1, 2),
            )
        print(
            json.dumps(
                {
                    "output": str(args.output.resolve()),
                    "onnx_bytes": args.output.stat().st_size,
                    "external_data_bytes": external_size,
                    "external_file_count": external_file_count,
                    "per_layer_embedding_chunks": embedding_chunks,
                    "elapsed_seconds": round(time.perf_counter() - started, 3),
                }
            )
        )
        return 0
    if not args.model_dir:
        raise ValueError("--model-dir is required unless --source-onnx is used.")

    model = (
        create_tiny_model(args.model_dir, args.context_length)
        if args.tiny
        else load_text_model(args.model_dir)
    )
    vocab_size = model.config.vocab_size
    # Any two in-vocab ids work for the export trace; 1/2 also fit the 512-token tiny vocab.
    sample_token_ids = (1, 2)
    if args.decode_only:
        export_decode_onnx(
            model,
            args.output,
            args.context_length,
            args.opset,
            sample_token_ids,
        )
        external_file_count, external_size, embedding_chunks = split_external_data_files(
            args.output, args.output, delete_source_data=True
        )
        layer_count = model.config.num_hidden_layers
        kv_heads = model.config.num_key_value_heads
        head_dim = getattr(
            model.config,
            "head_dim",
            model.config.hidden_size // model.config.num_attention_heads,
        )
        write_decode_metadata(
            args.output,
            args.model_dir,
            args.context_length,
            vocab_size,
            layer_count,
            kv_heads,
            head_dim,
            args.tiny,
        )
        if args.verify_onnxruntime:
            verify_decode_onnx(
                args.output,
                args.context_length,
                vocab_size,
                layer_count,
                kv_heads,
                head_dim,
                sample_token_ids[-1],
            )
    elif args.decode_output:
        export_cached_onnx(
            model,
            args.output,
            args.decode_output,
            args.context_length,
            args.opset,
            sample_token_ids,
        )
        prefill_files, prefill_size, embedding_chunks = split_external_data_files(
            args.output, args.output, delete_source_data=True
        )
        decode_files, decode_size, _ = split_external_data_files(
            args.decode_output, args.decode_output, delete_source_data=True
        )
        layer_count = model.config.num_hidden_layers
        kv_heads = model.config.num_key_value_heads
        head_dim = getattr(
            model.config,
            "head_dim",
            model.config.hidden_size // model.config.num_attention_heads,
        )
        write_cache_metadata(
            args.output,
            args.decode_output,
            args.model_dir,
            args.context_length,
            vocab_size,
            layer_count,
            kv_heads,
            head_dim,
            args.tiny,
        )
        if args.verify_onnxruntime:
            verify_cached_onnx(
                args.output,
                args.decode_output,
                args.context_length,
                vocab_size,
                layer_count,
                kv_heads,
                head_dim,
                sample_token_ids,
            )
        external_file_count = prefill_files + decode_files
        external_size = prefill_size + decode_size
    else:
        wrapper = LastTokenLogits(model).eval()
        export_onnx(wrapper, args.output, args.context_length, args.opset, sample_token_ids)
        external_file_count, external_size, embedding_chunks = split_external_data_files(
            args.output,
            args.output,
            delete_source_data=True,
        )
        write_metadata(
            args.output,
            args.model_dir,
            args.context_length,
            vocab_size,
            args.tiny,
        )

        if args.verify_onnxruntime:
            verify_onnx(args.output, args.context_length, vocab_size, sample_token_ids)

    size = args.output.stat().st_size
    print(
        json.dumps(
            {
                "output": str(args.output.resolve()),
                "decode_output": str(args.decode_output.resolve()) if args.decode_output else None,
                "onnx_bytes": size,
                "external_data_bytes": external_size,
                "external_file_count": external_file_count,
                "per_layer_embedding_chunks": embedding_chunks,
                "vocab_size": vocab_size,
                "context_length": args.context_length,
                "elapsed_seconds": round(time.perf_counter() - started, 3),
            }
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
