# Ratioscope Guide

## Introduction

Ratioscope is an artwork:

1. a visual study of machine language unfolding in real time.
1. It turns completed model forward passes and next-token probabilities into a living field of glowing cells.
1. Some displayed values are direct model telemetry, while their spatial composition is intentionally artistic rather than a map of biological neurons.

It is also a functional local, text-only chatbot:

- a live visualization of language-model inference.
- It runs quantized Qwen3 checkpoints through Unity Inference Engine.
- Tokenization, conversation history, generation, and the Cortex display all run on this computer; no 3rd party service or cloud model is required at runtime.

## Starting the model

Ratioscope selects a model profile from the available graphics memory, then starts loading it as soon as the application opens.

- On a high-VRAM system it selects the 4B model with a 4096-token window and the GPUCompute backend.
- On a mid-range GPU it selects the 1.7B model with a 2048-token window and GPUCompute.
- When GPU memory is limited or no GPU is available it selects the 1.7B model on the CPU. A 4B CPU profile is also available for manual configuration.

The loading overlay reports the selected model file, backend, file size, and elapsed time. **SEND** remains disabled until loading finishes. When the status begins with **ready**, it shows the active profile, model file, backend, transformer block count, context window, and load time. **FPS** and **VRAM** at the top report current rendering speed and graphics-memory use.

The generated model files are large, so startup can take time. A **model load failed** status usually means that the selected `.sentis` file is missing or unreadable, the settings profile does not match its filename, or the model cannot fit in available memory.

## Chatting

1. Wait until the status changes to **ready**.
2. Type a message in the field below the Cortex.
3. Press **Enter** or click **SEND**. Use **Shift+Enter** to insert a new line without sending.
4. Watch the reply appear token by token while the Cortex visualizes the work.

Only one reply or context-compaction pass runs at a time. Sending and compaction are disabled while the model is busy. The status line reports graph progress, generated-token count, seconds per token, context use, and entropy. After completion it reports the reply length and total generation time.

The current UI requests up to 512 new tokens per reply. The actual reply can be shorter when the model emits an end marker or when the remaining context window has less space. The 1.7B profile supports a separate thinking phase, controlled by the **Thinking** toggle under **Settings**. When enabled, reasoning streams into a muted **thinking** bubble while the visible answer streams into the **ai** bubble. Profiles without thinking support show only the answer.

The model can make factual mistakes. Ratioscope has no web access, tools, image input, audio input, or external knowledge lookup in the current version.

## Reading the Cortex

The Cortex has two data regions.

- **Transformer structure.** The 1.7B model shows all 28 blocks as 56 rows; the 4B model shows all 36 blocks as 72 rows. Each block has an attention row and an MLP row, subdivided into labelled architectural stages. The complete stack pulses when a real model forward finishes.
- **Token candidates.** After each generated token, the model's leading next-token candidates flare in the token strip. Brighter cells mean higher candidate probability.
- **Color: uncertainty.** Lower entropy keeps the field closer to green; higher entropy shifts it toward blue. High entropy means the model was considering a broader distribution of possible next tokens.
- **Decay: recent activity.** Heat fades over time so recent forward passes and candidates remain visible briefly.

Move the pointer over any Cortex cell to inspect it. A structural cell shows its transformer block, attention/MLP branch, architectural stage, and recent-participation heat. A token cell can show the decoded token and its probability. The tooltip also lists current entropy and up to five leading token candidates.

## What is real and what is artistic

The displayed block count comes from the loaded model's K/V-cache interface, with an explicit configured fallback for uncached graphs. Completed-forward events, token candidates, probabilities, entropy, elapsed time, and context occupancy come from the running model. The cached graph exposes one K/V pair per transformer block, allowing Ratioscope to distinguish all 28 or 36 blocks without reading hidden tensors.

The labelled stages within each attention/MLP row are a structural diagram. Structural brightness records recent participation by the dense transformer stack; it is not hidden-state or neuron activation strength, and it does not claim per-block timing. Token IDs are hashed to display positions, so nearby token cells do not imply semantic relation. These Qwen checkpoints are dense transformers and have no mixture-of-experts routing grid.

The Cortex is therefore an honest structural map plus real output telemetry, not a microscope on hidden states or thoughts. Surfacing intermediate activations would require a separately instrumented model export.

## How the local model works

Ratioscope includes CPU and GPU profiles for two uint8 model artifacts: Qwen3-1.7B with a 2048-token window and Qwen3-4B-Instruct-2507 with a 4096-token window. One settings asset owns the model filenames, backend choices, VRAM selection policy, transformer fallback counts, and Cortex appearance.

Text is generated one token at a time. The fixed-shape K/V-cache graph retains earlier attention keys and values across generated tokens and chat rounds, so the model evaluates the new token without recomputing the entire window. The uncached graph remains a compatible fallback for the 1.7B profile. The context window is still fixed by the exported model; K/V caching improves reuse but does not increase its capacity.

## Model configuration

The runtime model manifest is `StreamingAssets/config/models.json`. It controls the downloadable model artifacts:

- `baseUrl` is the common download location. Each entry's `relativeUrl` is appended to it unless that entry already contains an absolute HTTP or HTTPS URL.
- `entries` declares each downloadable `.sentis` file through `fileName`, `byteSize`, `sha256`, and `relativeUrl`.

The Cortex system prompt is edited under **Settings** and saved with your other settings. The panel's **Reset to Defaults** restores that default, and a change applies when the next chat session starts.

Open **Settings** and choose **Download Models** to fetch manifest entries that are not already available. Downloads are written to the application's persistent `Models` directory through a temporary `.part` file, checked against the configured byte size and SHA-256 hash, and finalized only after verification. A downloaded artifact takes priority over the matching bundled file under `Assets/StreamingAssets/Sentis`. An empty `sha256` disables checksum verification; a positive `byteSize` is still checked.

## Context and working memory

The active model has a fixed 2048-token or 4096-token window shared by the system prompt, conversation history, current message, thinking text, and reply.

- The circular percentage control shows current context occupancy.
- Click the percentage to open **CONTEXT / WORKING MEMORY**.
- The panel shows prompt tokens, space available for the reply, the exact messages retained by the model, generated memory, and any messages outside the current window.
- **COMPACT** becomes available after at least one complete user-and-assistant turn exists.

Compaction is manual. Clicking **COMPACT** asks the active local model to summarize the oldest complete turn, together with any existing compacted memory, into a short memory message. The original turn is then removed from active history. This preserves useful facts longer, but the summary is model-generated and can lose or alter details.

Ratioscope never compacts automatically. If the prompt itself no longer fits, the runtime can remove the oldest complete turns. A system prompt plus newest message that is still too large cannot be sent. Use **COMPACT** before the percentage becomes full, or restart Ratioscope to begin a clean session.

Conversation history and compacted memory exist only for the current run. Closing the application clears them.

## Toolbar and updates

- **Help** opens this guide and shows the application version.
- **Settings** opens the current configuration panel, including update status, a manual update check, and the release-page action when a release endpoint is configured.

The updater only checks for a newer version and can open its release page. It does not download or install an update automatically.

## Privacy

Chat text, tokenization, model inference, context compaction, and Cortex telemetry stay on this machine. The separate update checker may contact the release endpoint configured by the build, but it does not send conversation content.

## Troubleshooting

- **The loading screen remains visible:** large model files take time to read and prepare. Check the elapsed timer and VRAM readout before restarting.
- **Model load failed:** confirm that the `.sentis` file named by the selected profile exists under `Assets/StreamingAssets/Sentis`, and that the selected backend has enough memory.
- **A reply is very short:** the model may have emitted an end marker, or the active context window may have left little reply space. Open the context panel and compact older turns.
- **Cannot start / prompt does not fit:** shorten the newest message, compact an older complete turn, or restart to clear the session.
- **COMPACT is disabled:** wait for the model to become idle and complete at least one user-and-assistant exchange.
- **Generation is slow:** local inference speed depends on the selected checkpoint, backend, and hardware. The status line's seconds-per-token value and the FPS/VRAM readout show whether work is still progressing.
