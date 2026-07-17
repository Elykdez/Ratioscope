#!/usr/bin/env python3
"""Export a byte-level-BPE LLM tokenizer as a compact binary for Unity.

Covers GPT-2-style byte-level BPE tokenizers (Qwen, Llama 3, Phi, SmolLM):
text is split by a pre-tokenization regex, each piece is mapped byte-by-byte
through the byte-to-unicode table, and BPE merges run inside each piece.
The binary carries the vocab, the merges (as id triples), the added/special
tokens, and the split regex.

Also emits reference fixtures (encoded ids produced by the Hugging Face
tokenizer) that the Unity editor tests replay against the C# implementation.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import struct

MAGIC = b"LTK1"
VERSION = 1

FIXTURE_TEXTS = [
    "Hello world",
    "Reply with exactly PONG.",
    "You are concise.",
    "Line one\nline two  with  spaces",
    "你好，世界！",
    "Emoji \U0001f642 test",
    "assistant\n",
    "It's 12345 numbers, don't merge them!",
    "  leading and trailing  ",
]

FIXTURE_CHAT = [
    {"role": "system", "content": "You are concise."},
    {"role": "user", "content": "Reply with exactly PONG."},
]


def find_split_regex(pre_tokenizer: dict) -> str:
    """Extract the Split pattern from the pre_tokenizer config."""
    if pre_tokenizer is None:
        raise ValueError("Tokenizer has no pre_tokenizer; expected byte-level BPE.")
    candidates = (
        pre_tokenizer.get("pretokenizers", [pre_tokenizer])
        if pre_tokenizer.get("type") == "Sequence"
        else [pre_tokenizer]
    )
    for entry in candidates:
        if entry.get("type") == "Split":
            pattern = entry["pattern"]
            return pattern["Regex"] if isinstance(pattern, dict) else pattern
    raise ValueError("No Split pre-tokenizer with a regex found.")


def export_binary(tokenizer_json: dict, output_path: Path) -> dict:
    model = tokenizer_json["model"]
    if model["type"] != "BPE":
        raise ValueError(f"Expected a BPE tokenizer, got {model['type']}.")
    if model.get("byte_fallback"):
        raise ValueError("Expected byte-level BPE, not byte-fallback (SentencePiece).")

    split_regex = find_split_regex(tokenizer_json.get("pre_tokenizer"))

    vocab: dict[str, int] = model["vocab"]
    vocab_count = max(vocab.values()) + 1
    id_to_token = [""] * vocab_count
    for token, token_id in vocab.items():
        id_to_token[token_id] = token

    merges = model["merges"]
    added_tokens = tokenizer_json["added_tokens"]

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("wb") as stream:
        stream.write(MAGIC)
        stream.write(struct.pack("<i", VERSION))

        raw_regex = split_regex.encode("utf-8")
        stream.write(struct.pack("<H", len(raw_regex)))
        stream.write(raw_regex)

        stream.write(struct.pack("<i", vocab_count))
        for token in id_to_token:
            raw = token.encode("utf-8")
            if len(raw) > 0xFFFF:
                raise ValueError("Token longer than 65535 UTF-8 bytes.")
            stream.write(struct.pack("<H", len(raw)))
            stream.write(raw)

        # Merges are stored as (left, right, merged) vocab ids in rank order.
        stream.write(struct.pack("<i", len(merges)))
        for merge in merges:
            left, right = merge if isinstance(merge, list) else merge.split(" ", 1)
            merged = left + right
            stream.write(
                struct.pack("<iii", vocab[left], vocab[right], vocab[merged])
            )

        stream.write(struct.pack("<i", len(added_tokens)))
        for token in added_tokens:
            raw = token["content"].encode("utf-8")
            stream.write(struct.pack("<i", token["id"]))
            stream.write(struct.pack("<H", len(raw)))
            stream.write(raw)
            stream.write(struct.pack("<B", 1 if token["special"] else 0))

    return {
        "binary": str(output_path),
        "bytes": output_path.stat().st_size,
        "vocab": vocab_count,
        "merges": len(merges),
        "added_tokens": len(added_tokens),
        "split_regex": split_regex,
    }


def export_fixtures(model_dir: Path, output_path: Path) -> dict:
    from transformers import AutoTokenizer

    tokenizer = AutoTokenizer.from_pretrained(model_dir, local_files_only=True)

    cases = []
    for text in FIXTURE_TEXTS:
        cases.append(
            {"text": text, "ids": tokenizer.encode(text, add_special_tokens=False)}
        )

    chat_ids = list(
        tokenizer.apply_chat_template(
            FIXTURE_CHAT, tokenize=True, add_generation_prompt=True
        )["input_ids"]
    )
    chat_text = tokenizer.apply_chat_template(
        FIXTURE_CHAT, tokenize=False, add_generation_prompt=True
    )

    fixtures = {
        "encodeCases": cases,
        "chatMessages": FIXTURE_CHAT,
        "chatIds": chat_ids,
        "chatText": chat_text,
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(fixtures, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return {"fixtures": str(output_path), "cases": len(cases), "chat_tokens": len(chat_ids)}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-dir", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--fixtures", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    tokenizer_json = json.loads(
        (args.model_dir / "tokenizer.json").read_text(encoding="utf-8")
    )
    report = export_binary(tokenizer_json, args.output)
    if args.fixtures:
        report.update(export_fixtures(args.model_dir, args.fixtures))
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
