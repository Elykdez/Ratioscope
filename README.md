# Ratioscope

[![Logo](./Assets/Bundles/Texture/2D/logo.png)](./)

## What is this?

1. An artwork: a visual study of machine language unfolding in real time.
2. A functional local chatbot built with Unity Inference Engine. As the model generates a reply, the Cortex turns the loaded transformer structure, completed forward passes, next-token candidates, probabilities, and uncertainty into a living field of light.
3. Inference, tokenization, chat history, and visualization stay on your machine; no Python service, 3rd party lib or cloud model is used at runtime.

Again, the work uses real model telemetry, but its visual composition is artistic.
It is not a view of hidden states or an actual claim that the model's thoughts can be read. The in-app Help panel explains exactly which data is measured and which parts are visual interpretation.

## User Guidance

[![Preview](./Docs/preview.png)](./)

Currently supported languages:

[English](./Assets/StreamingAssets/doc/desc_en.md) | [简体中文](./Assets/StreamingAssets/doc/desc_zh.md)

## Technical Information

### Why Art?

Running an LLM inside Unity Inference Engine is not the efficient way to run an LLM. A llama.cpp-style runtime keeps weights quantized through execution and would serve the same checkpoints faster in a fraction of the memory. This project pays that cost deliberately, because the artwork sets the requirements:

1. The Cortex renders real telemetry from inside the forward pass - graph progress, token candidates, probabilities, entropy - which is only observable by owning the inference graph itself instead of calling an opaque external runtime.
2. The piece must run as one self-contained offline Unity build, suitable for an installation: no Python service, no separate inference server, no cloud.
3. The engine's constraints (fp32-only kernels, fixed context windows, per-tensor uint8 weights) are accepted rather than fought, because chat throughput and answer quality are secondary to making the machinery visible.

As a chatbot, the tradeoffs look wrong; as an instrument for watching a transformer work, they are the point.

### Why Not Float16?

The converter actually offers `Float16` weight quantization; in current version of Sentis it is mechanically possible but practically pointless. Sentis pads 16-bit data into int32 buffers and casts weights to fp32 before every kernel, so a Float16 artifact serializes and resides at full fp32 size, larger on disk and in VRAM than `Uint8`, gaining only per-weight accuracy. Also this fp32 size does not actually improve KV Cache precision - it preserves attention history precisely, but the underlying weights have already been quantized.

True 16-bit (or 4-bit) execution residency would require a different runtime such as llama.cpp, which conflicts with the self-contained goals above.

See [Contribution.md](./CONTRIBUTION.md) for more technical details and [`Docs/Llm-Chat-Runtime.md`](./Docs/Llm-Chat-Runtime.md) for the concrete numbers behind these tradeoffs.

## Build On Your Own

- Desktop builds default to Windows with IL2CPP. You may adjust model loading configuration at `Assets/StreamingAssets/config/models.json`.

- For Android builds `.sentis` graphs must not be included in a standalone APK: they are too large for the package and are loaded through normal file IO, which cannot read a `StreamingAssets` entry directly. The mobile build filter therefore removes every `.sentis` file under `Assets/StreamingAssets/Sentis` while keeping the manifest and tokenizer in the APK. You can also side-load a graph straight to that folder over adb with `Tools\push-model.bat [model-file]` (defaults to `Llm_Decode_2048.sentis`). It auto-detects adb, pushes the file to the connected device's `persistentDataPath/Models` (you need to start the app once), and verifies the byte count.

On first launch, this app will try download model. You may select another CPU or GPU profile. When its artifact is missing, system waits and **Settings > Download Models** fetches that selected artifact instead. The download is streamed to a `.part` file, verified, then stored under `Application.persistentDataPath/Models`; it survives application updates but is removed when the application is uninstalled.

### Prerequisites

> Jump to [Build Sentis Graph](#build-sentis-graph) if you have knowledge in Unity and Sentis, or just want a quick start.

1. Download and install [Unity Hub](https://unity.com/download) and install Unity Engine with target version.
2. Clone the repository, or use GitHub's **Code > Download ZIP**, then add the project folder in Unity Hub and open it with that Editor version.

```powershell
git clone https://github.com/Elykdez/Ratioscope.git
cd Ratioscope
```

The source checkpoints, exported ONNX graphs, and generated `.sentis` files are large local build products and are intentionally excluded from Git. A fresh clone therefore needs at least one model prepared before the `Stage` scene can run.

After that, you may modify and build this project at your own favor.

### Project Dependencies

Unity Package Manager automatically restores the project's dependencies when the project opens (This includes Unity Inference Engine). Let Unity finish resolving packages and compiling scripts before preparing a model.

### ONNX Compatibility

The Unity Inference Engine, AKA "Sentis", **is not a general-purpose ONNX Runtime** that can execute every valid `.onnx` file unchanged. During import, each ONNX operator must map to a [supported Sentis operator](https://docs.unity.cn/Packages/com.unity.ai.inference%402.6/manual/supported-operators.html), data type, and execution backend; it supports most models using ONNX opsets 7 through 25, but unsupported operators, tensor types, or backend combinations can still make a model fail during import or execution.

This is especially relevant to large language models:

- ONNX control-flow and sequence operators such as `If`, `Loop`, and `Scan` are unsupported in Sentis 2.6.x.
- Fused operators such as ONNX `Attention` and `RotaryEmbedding` are unsupported and must be exported as compatible primitive operations.
- Standard ONNX quantization operators such as `QuantizeLinear`, `DequantizeLinear`, and `QLinearMatMul` are unsupported. This project therefore exports a floating-point ONNX graph and applies its own uint8 weight conversion inside Unity.
- Operator support varies by backend. A graph that imports successfully can still fail if one of its layers or data types is unavailable on the selected CPU or GPU backend.

This project adds stricter runtime requirements on top of Sentis: the graph must be a dense causal text decoder with static input shapes, a fixed context window, logits output, and the expected K/V-cache inputs and outputs. The tokenizer must also be compatible with the project's byte-level BPE pipeline. Hybrid architectures such as Gated DeltaNet or SSM/Mamba are not supported by the current exporter/runtime path.

For these reasons, do not substitute an arbitrary ONNX, pre-quantized ONNX, GGUF, or raw `safetensors` file. Use the provided export script and Unity conversion tool so the graph shape, operators, external weights, quantization, and `.sentis` serialization match the project's runtime contract. See Unity's [supported-model documentation](https://docs.unity.cn/Packages/com.unity.ai.inference%402.6/manual/supported-models.html) for the complete format-level limits.

### Build Sentis Graph

1. **Download Checkpoints**

   The configured profiles use the official [Qwen3-1.7B](https://huggingface.co/Qwen/Qwen3-1.7B) checkpoint for the standard model and [Qwen3-4B-Instruct-2507](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507) for the more-capable model. Start with 1.7B; it runs on an 8GB-VRAM GPU. The 4B artifact needs a 16GB-class GPU (a full-window session measures ~17.5GB GPU memory) or the much slower CPU backend.

   Checkpoints are read from `Tools\ImportedModel` by default, which is gitignored alongside the exported artifacts. Create the exporter environment under `Tools\LlmChat\.venv`, install the pinned dependencies, then download one or both checkpoints with the Hugging Face CLI.

   ```powershell
   python -m venv --system-site-packages Tools\LlmChat\.venv
   Tools\LlmChat\.venv\Scripts\python.exe -m pip install -r Tools\LlmChat\requirements.txt

   Tools\LlmChat\.venv\Scripts\hf.exe download Qwen/Qwen3-1.7B `
   --local-dir Tools\ImportedModel\Qwen3-1.7B

   # Optional 4B profile
   Tools\LlmChat\.venv\Scripts\hf.exe download Qwen/Qwen3-4B-Instruct-2507 `
   --local-dir Tools\ImportedModel\Qwen3-4B-Instruct-2507
   ```

   To keep checkpoints on another drive, put them anywhere you like and set a `MODEL_ROOT` environment variable pointing at the folder that contains the model directories. Every command below honours it.

2. **Export Onnx**

   Export the checkpoint to the fixed-shape KV-cache ONNX graph expected by this project.
   The committed tokenizer already matches both configured Qwen checkpoints.

   Close the Unity Editor first, then double-click:

   ```text
   Tools\LlmChat\build_llm_model.bat
   ```

   The script creates the exporter venv if it is missing. Expect several minutes per graph with no progress output, and transient disk use of about twice the fp32 weight size (~14 GB for 1.7B, ~32 GB for 4B) before it collapses to the final `.onnx` plus its `.weights` folder.

   The equivalent commands, if you prefer to drive the exporter yourself:

   ```powershell
   # Recommended 1.7B / 2048-token graph
   Tools\LlmChat\.venv\Scripts\python.exe Tools\LlmChat\export_llm_sentis.py `
   --model-dir Tools\ImportedModel\Qwen3-1.7B `
   --output Tools\ExportedModel\Llm_Decode_2048.onnx `
   --decode-only --context-length 2048

   # Optional 4B / 4096-token graph
   Tools\LlmChat\.venv\Scripts\python.exe Tools\LlmChat\export_llm_sentis.py `
   --model-dir Tools\ImportedModel\Qwen3-4B-Instruct-2507 `
   --output Tools\ExportedModel\Llm_Decode_4096.onnx `
   --decode-only --context-length 4096
   ```

3. **Sentis Integration**

   Then convert each exported graph in the Unity Editor:

   1. Open **Tools > Hypocycloid > Sentis > ONNX to Sentis**.
   2. Browse to `Tools/ExportedModel/Llm_Decode_2048.onnx` or `Tools/ExportedModel/Llm_Decode_4096.onnx`.
   3. Keep **Weight quantization** set to `Uint8` and **Skip graph optimization** enabled for these multi-gigabyte models, then click **Convert**.
   4. Confirm the matching file appears in `Assets/StreamingAssets/Sentis`. The tool preserves the base name, so `Llm_Decode_2048.onnx` becomes `Llm_Decode_2048.sentis`.
   5. Select `Assets/Bundles/Settings/LlmSystemSettings.asset` and confirm the profile's **Decode Model File Name**, backend, thinking support, and transformer block count. The included settings already map the filenames above to the 1.7B and 4B profiles.
   6. Confirm settings asset then open main scene and enter Play Mode. Wait for the status to change to **READY** before sending a message.

   Converting both graphs in one session is not recommended: import materializes the whole
   float32 graph on the managed heap, and Unity never returns those pages to the OS. The
   converter warns about this after any artifact of 512 MB or larger - restart the Editor
   before converting the second model.

Current exporter supports standard dense causal language models with a compatible tokenizer (GPT-2 style preferred; see [Sentis and ONNX Compatibility](#onnx-compatibility)). For a different checkpoint, regenerate both the tokenizer binary and the ONNX graph, then add or update its profile in `LlmSystemSettings.asset`.

## License

[MIT](./LICENSE)
