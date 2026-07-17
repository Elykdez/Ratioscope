using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.InferenceEngine;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Local LLM chat runtime on Unity Inference Engine (Sentis). It prefers the fixed-shape
    /// one-token KV-cache decode export and falls back to the original fixed-window graph.
    ///
    /// Two generation surfaces: blocking Chat/GenerateTokens, and BeginChat/BeginStream which
    /// return an incremental ChatStream with forward-pass, graph, and per-token telemetry.
    /// </summary>
    public sealed class ChatService : IDisposable
    {
        const string thoughtPrefix = "<think>";
        const string thoughtSuffix = "</think>";

        readonly ChatServiceOptions options;

        Model model;
        Worker worker;
        string inputIdsName;
        string attentionMaskName;
        string positionIdsName;
        string modelSource;
        int contextLength;
        int transformerBlockCount;
        string[] layerOpNames;
        string[] cacheInputNames;
        string[] cacheOutputNames;
        Tensor<float>[] cacheInputs;
        int[] cacheAttentionMask;
        readonly List<int> cachedTokenIds = new();
        float[] cachedNextLogits;
        ChatStream activeStream;
        readonly List<IDisposable> retainedStreamInputs = new();
        Task<ChatRuntimeInfo> pendingStart;
        bool disposed;

        public bool IsReady { get; private set; }
        public LlmTokenizer Tokenizer { get; private set; }
        public ChatRuntimeInfo RuntimeInfo { get; private set; }

        internal int ContextLength => contextLength;
        internal int TransformerBlockCount => transformerBlockCount;
        internal string[] LayerOpNames => layerOpNames;
        internal bool UsesKvCache => cacheInputNames != null;
        internal int CachedPosition => cachedTokenIds.Count;
        internal bool SchedulesWholeGraph => options.Backend == BackendType.GPUCompute;

        public ChatService(ChatServiceOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ChatRuntimeInfo Start()
        {
            ThrowIfDisposed();
            if (IsReady)
                return RuntimeInfo;

            Stopwatch stopwatch = Stopwatch.StartNew();
            LoadArtifacts();
            return CompleteStart(stopwatch);
        }

        /// <summary>
        /// Loads the tokenizer and model on a worker thread so the main thread keeps rendering.
        /// Worker creation still happens on the calling thread;
        /// await this from the Unity main thread. The ModelAsset path loads synchronously.
        /// </summary>
        public Task<ChatRuntimeInfo> StartAsync()
        {
            ThrowIfDisposed();
            if (IsReady)
                return Task.FromResult(RuntimeInfo);
            pendingStart ??= StartAsyncCore();
            return pendingStart;
        }

        async Task<ChatRuntimeInfo> StartAsyncCore()
        {
            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                if (options.DecodeModelAsset != null || options.ModelAsset != null)
                    LoadArtifacts();
                else
                    await Task.Run(LoadArtifacts);
                return CompleteStart(stopwatch);
            }
            catch
            {
                pendingStart = null;
                throw;
            }
        }

        void LoadArtifacts()
        {
            if (!File.Exists(options.TokenizerPath))
                throw new FileNotFoundException(
                    "LLM tokenizer binary not found. Run Tools/LlmChat/export_llm_tokenizer.py.",
                    options.TokenizerPath
                );
            Tokenizer = new LlmTokenizer(options.TokenizerPath);

            if (options.DecodeModelAsset != null)
            {
                model = ModelLoader.Load(options.DecodeModelAsset);
                modelSource = options.DecodeModelAsset.name;
            }
            else if (
                !string.IsNullOrEmpty(options.DecodeModelPath)
                && File.Exists(options.DecodeModelPath)
            )
            {
                model = ModelLoader.Load(options.DecodeModelPath);
                modelSource = options.DecodeModelPath;
            }
            else if (options.ModelAsset != null)
            {
                model = ModelLoader.Load(options.ModelAsset);
                modelSource = options.ModelAsset.name;
            }
            else
            {
                if (!File.Exists(options.ModelPath))
                    throw new FileNotFoundException(
                        "LLM .sentis model not found. Export the ONNX model and convert it "
                            + "with Tools/Hypocycloid/Sentis/ONNX to Sentis.",
                        options.ModelPath
                    );
                model = ModelLoader.Load(options.ModelPath);
                modelSource = options.ModelPath;
            }
        }

        ChatRuntimeInfo CompleteStart(Stopwatch stopwatch)
        {
            ResolveModelInterface();
            worker = new Worker(model, options.Backend);
            if (UsesKvCache)
                InitializeKvCache();
            stopwatch.Stop();

            RuntimeInfo = new ChatRuntimeInfo
            {
                ModelSource = modelSource,
                Backend = options.Backend,
                ContextLength = contextLength,
                TransformerBlockCount = transformerBlockCount,
                VocabularySize = Tokenizer.VocabularySize,
                LoadSeconds = stopwatch.Elapsed.TotalSeconds,
            };
            IsReady = true;
            return RuntimeInfo;
        }

        public ChatResult Chat(
            IReadOnlyList<ChatMessage> messages,
            ChatGenerationOptions generation = null
        )
        {
            generation = PrepareChat(messages, generation);
            ChatContextSnapshot context = BuildContextSnapshot(messages, generation);

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<int> generated = GenerateTokens(context.PromptIds, generation);
            stopwatch.Stop();

            string rawText = Tokenizer.Decode(generated, skipSpecialTokens: false);
            ParseResponse(rawText, out string content, out string thinking);
            ChatFinishReason finishReason =
                generated.Count < Math.Min(generation.MaxNewTokens, context.AvailableReplyTokens)
                    ? ChatFinishReason.StopToken
                : context.PromptTokens + generated.Count >= context.WindowCapacity
                    ? ChatFinishReason.ContextLimit
                    : ChatFinishReason.TokenLimit;
            return new ChatResult(
                content,
                thinking,
                rawText,
                context.PromptTokens,
                generated.Count,
                stopwatch.Elapsed.TotalSeconds,
                finishReason
            );
        }

        /// <summary>
        /// Returns the exact prompt that would be used for the next turn, including any
        /// complete old turns that cannot fit in the model's static context window.
        /// </summary>
        public ChatContextSnapshot InspectContext(
            IReadOnlyList<ChatMessage> messages,
            ChatGenerationOptions generation = null
        )
        {
            generation = PrepareChat(messages, generation);
            return BuildContextSnapshot(messages, generation);
        }

        /// <summary>
        /// Starts incremental generation for a conversation. Oldest non-system turns are
        /// dropped until the prompt fits the context window. Drive the returned stream with
        /// ChatStream.Tick from the main thread.
        /// </summary>
        public ChatStream BeginChat(
            IReadOnlyList<ChatMessage> messages,
            ChatGenerationOptions generation = null
        )
        {
            generation = PrepareChat(messages, generation);
            ChatContextSnapshot context = BuildContextSnapshot(messages, generation);
            return BeginStream(context.PromptIds, generation);
        }

        /// <summary>Raw-id counterpart of BeginChat; null stopTokens means eos + end-of-turn,
        /// an empty collection disables early stopping.</summary>
        public ChatStream BeginStream(
            IReadOnlyList<int> promptIds,
            ChatGenerationOptions generation,
            IReadOnlyCollection<int> stopTokens = null
        )
        {
            ThrowIfDisposed();
            Start();
            EnsureWorkerIdle();
            ValidatePrompt(promptIds);
            ValidateGenerationOptions(generation);

            ChatStream stream = new(this, promptIds, generation, stopTokens);
            activeStream = stream;
            return stream;
        }

        /// <summary>
        /// Runs the raw decode loop. Stops at MaxNewTokens, a full context window, or any stop
        /// token (&lt;eos&gt;/&lt;turn|&gt; when stopTokens is null; pass an empty collection to
        /// disable early stopping). Stop tokens are not included in the returned list.
        /// </summary>
        public List<int> GenerateTokens(
            IReadOnlyList<int> promptIds,
            ChatGenerationOptions generation,
            IReadOnlyCollection<int> stopTokens = null
        )
        {
            ThrowIfDisposed();
            Start();
            EnsureWorkerIdle();
            ValidatePrompt(promptIds);

            if (UsesKvCache)
                return GenerateTokensCached(promptIds, generation, stopTokens);

            stopTokens ??= new[] { Tokenizer.EosTokenId, Tokenizer.EndOfTurnTokenId };
            int budget = Math.Min(generation.MaxNewTokens, contextLength - promptIds.Count);
            System.Random random = new();
            List<int> generated = new(budget);
            int[] window = new int[contextLength];
            int[] mask = new int[contextLength];

            for (int step = 0; step < budget; step++)
            {
                FillWindow(promptIds, generated, contextLength, window, mask);
                float[] logits = RunForward(window, mask);
                int next = SelectToken(logits, generation, random);
                if (stopTokens.Contains(next))
                    break;
                generated.Add(next);
            }

            return generated;
        }

        List<int> GenerateTokensCached(
            IReadOnlyList<int> promptIds,
            ChatGenerationOptions generation,
            IReadOnlyCollection<int> stopTokens
        )
        {
            stopTokens ??= new[] { Tokenizer.EosTokenId, Tokenizer.EndOfTurnTokenId };
            int budget = Math.Min(generation.MaxNewTokens, contextLength - promptIds.Count);
            int promptStart = PrepareCachedPrompt(promptIds);
            float[] logits = cachedNextLogits;
            for (int i = promptStart; i < promptIds.Count; i++)
                logits = RunCachedToken(promptIds[i]);
            if (logits == null)
                throw new InvalidOperationException("The KV cache has no logits for this prompt.");

            System.Random random = new();
            List<int> generated = new(budget);
            for (int step = 0; step < budget; step++)
            {
                int next = SelectToken(logits, generation, random);
                if (stopTokens.Contains(next))
                    break;
                generated.Add(next);
                if (generated.Count < budget)
                    logits = RunCachedToken(next);
            }
            return generated;
        }

        ChatGenerationOptions PrepareChat(
            IReadOnlyList<ChatMessage> messages,
            ChatGenerationOptions generation
        )
        {
            if (messages == null || messages.Count == 0)
                throw new ArgumentException(
                    "At least one chat message is required.",
                    nameof(messages)
                );
            foreach (ChatMessage message in messages)
            {
                if (message == null)
                    throw new ArgumentException(
                        "Chat messages cannot contain null.",
                        nameof(messages)
                    );
            }
            generation ??= new ChatGenerationOptions();
            ValidateGenerationOptions(generation);
            Start();
            return generation;
        }

        // Keep all history that fits. ChatStream shrinks the reply budget to the remaining
        // space; reserving MaxNewTokens here used to evict valid context prematurely.
        ChatContextSnapshot BuildContextSnapshot(
            IReadOnlyList<ChatMessage> messages,
            ChatGenerationOptions generation
        )
        {
            List<ChatMessage> working = messages.ToList();
            List<ChatMessage> removed = new();
            while (true)
            {
                string promptText = ChatTemplate.BuildPromptText(
                    working,
                    options.SupportsThinking && !generation.EnableThinking
                );
                List<int> promptIds = Tokenizer.EncodeWithSpecialTokens(promptText);
                if (promptIds.Count < contextLength)
                    return new ChatContextSnapshot(
                        working,
                        removed,
                        promptText,
                        promptIds,
                        contextLength,
                        generation.MaxNewTokens
                    );

                int oldest = working.FindIndex(message => message.Role != "system");
                int nonSystemCount = working.Count(message => message.Role != "system");
                if (oldest < 0 || nonSystemCount <= 1)
                    throw new ArgumentException(
                        $"Prompt needs {promptIds.Count} tokens; the {contextLength}-token "
                            + "window cannot fit it even after trimming history."
                    );
                removed.Add(working[oldest]);
                working.RemoveAt(oldest);
                if (oldest < working.Count && working[oldest].Role == "assistant")
                {
                    removed.Add(working[oldest]);
                    working.RemoveAt(oldest);
                }
            }
        }

        void ValidatePrompt(IReadOnlyList<int> promptIds)
        {
            if (promptIds == null || promptIds.Count == 0)
                throw new ArgumentException("Prompt ids are required.", nameof(promptIds));
            if (promptIds.Count >= contextLength)
                throw new ArgumentException(
                    $"Prompt is {promptIds.Count} tokens; the fixed context window holds "
                        + $"{contextLength}, leaving no room to generate.",
                    nameof(promptIds)
                );
            ValidatePromptVocabulary(promptIds);
        }

        // Sentis indexes the embedding constant with raw pointer arithmetic and never bounds-checks
        // the gathered index, so an id past the exported vocabulary reads unmapped memory and kills
        // the process instead of throwing. Sampled ids come from the model's own logits and are
        // always in range; the prompt is the only way an out-of-range id reaches the graph.
        void ValidatePromptVocabulary(IReadOnlyList<int> promptIds)
        {
            int vocabularySize = options.VocabularySize;
            if (vocabularySize <= 0)
                return;
            for (int i = 0; i < promptIds.Count; i++)
            {
                int id = promptIds[i];
                if (id >= 0 && id < vocabularySize)
                    continue;
                throw new ArgumentException(
                    $"Prompt token {id} at index {i} is outside the {vocabularySize}-token "
                        + $"vocabulary of {modelSource}. The loaded tokenizer "
                        + $"({Tokenizer.VocabularySize} tokens) does not match this artifact.",
                    nameof(promptIds)
                );
            }
        }

        void EnsureWorkerIdle()
        {
            if (activeStream != null && activeStream.State == ChatStreamState.Thinking)
                throw new InvalidOperationException(
                    "A ChatStream is already generating on this service; complete or cancel it first."
                );
        }

        internal void ReleaseStream(ChatStream stream)
        {
            if (activeStream == stream)
                activeStream = null;
        }

        // CPU ScheduleIterable can keep Burst jobs referencing an input after output
        // readback. Release streamed inputs only after the owning worker is gone.
        internal void RetainStreamInput(IDisposable tensor)
        {
            if (tensor != null)
                retainedStreamInputs.Add(tensor);
        }

        internal IEnumerator ScheduleForwardIterable(Tensor<int> ids, Tensor<int> mask)
        {
            worker.SetInput(inputIdsName, ids);
            worker.SetInput(attentionMaskName, mask);
            return worker.ScheduleIterable();
        }

        internal IEnumerator ScheduleCachedForwardIterable(
            Tensor<int> ids,
            Tensor<int> mask,
            Tensor<int> position
        )
        {
            worker.SetInput(inputIdsName, ids);
            worker.SetInput(attentionMaskName, mask);
            worker.SetInput(positionIdsName, position);
            for (int i = 0; i < cacheInputNames.Length; i++)
                worker.SetInput(cacheInputNames[i], cacheInputs[i]);
            return worker.ScheduleIterable();
        }

        internal void ScheduleForward(Tensor<int> ids, Tensor<int> mask)
        {
            worker.SetInput(inputIdsName, ids);
            worker.SetInput(attentionMaskName, mask);
            worker.Schedule();
        }

        internal void ScheduleCachedForward(Tensor<int> ids, Tensor<int> mask, Tensor<int> position)
        {
            worker.SetInput(inputIdsName, ids);
            worker.SetInput(attentionMaskName, mask);
            worker.SetInput(positionIdsName, position);
            for (int i = 0; i < cacheInputNames.Length; i++)
                worker.SetInput(cacheInputNames[i], cacheInputs[i]);
            worker.Schedule();
        }

        internal int PrepareCachedPrompt(IReadOnlyList<int> promptIds)
        {
            int common = 0;
            int limit = Math.Min(promptIds.Count, cachedTokenIds.Count);
            while (common < limit && promptIds[common] == cachedTokenIds[common])
                common++;
            if (common != cachedTokenIds.Count)
            {
                ResetKvCache();
                return 0;
            }
            // Replaying an identical prompt has no suffix to schedule. Rebuild so both
            // blocking and frame-budgeted paths obtain logits through the same flow.
            if (common == promptIds.Count)
            {
                ResetKvCache();
                return 0;
            }
            return common;
        }

        internal void FillCachedAttentionMask(int[] target)
        {
            Array.Copy(cacheAttentionMask, target, cacheAttentionMask.Length);
            target[^1] = 1;
        }

        internal void CommitCachedForward(int token, float[] logits)
        {
            for (int i = 0; i < cacheOutputNames.Length; i++)
            {
                Tensor destination = cacheInputs[i];
                worker.CopyOutput(cacheOutputNames[i], ref destination);
                if (destination is not Tensor<float> cache)
                    throw new InvalidDataException(
                        $"Missing float cache output {cacheOutputNames[i]}."
                    );
                cacheInputs[i] = cache;
            }
            Array.Copy(cacheAttentionMask, 1, cacheAttentionMask, 0, cacheAttentionMask.Length - 1);
            cacheAttentionMask[^1] = 1;
            cachedTokenIds.Add(token);
            cachedNextLogits = logits;
        }

        internal float[] ReadLogits()
        {
            Tensor<float> output = PeekLogits();
            using Tensor<float> cpu = output.ReadbackAndClone();
            return cpu.DownloadToArray();
        }

        internal void RequestLogitsReadback()
        {
            PeekLogits().ReadbackRequest();
        }

        internal bool AreLogitsReady()
        {
            return PeekLogits().IsReadbackRequestDone();
        }

        Tensor<float> PeekLogits()
        {
            Tensor<float> output = worker.PeekOutput() as Tensor<float>;
            if (output == null)
                throw new InvalidDataException("The model did not produce float logits.");
            return output;
        }

        internal static void FillWindow(
            IReadOnlyList<int> promptIds,
            IReadOnlyList<int> generated,
            int contextLength,
            int[] window,
            int[] mask
        )
        {
            int tokenCount = promptIds.Count + generated.Count;
            int offset = contextLength - tokenCount;
            Array.Clear(window, 0, offset);
            Array.Clear(mask, 0, offset);
            for (int i = 0; i < tokenCount; i++)
            {
                window[offset + i] =
                    i < promptIds.Count ? promptIds[i] : generated[i - promptIds.Count];
                mask[offset + i] = 1;
            }
        }

        void ResolveModelInterface()
        {
            bool cached = model.inputs.Any(input =>
                input.name.StartsWith("past_", StringComparison.Ordinal)
            );
            if (!cached && model.inputs.Count != 2)
                throw new InvalidDataException(
                    $"Expected 2 model inputs (input_ids, attention_mask), found {model.inputs.Count}."
                );

            foreach (Model.Input input in model.inputs)
            {
                if (!input.shape.IsStatic())
                    throw new InvalidDataException(
                        $"Model input {input.name} must have a static shape."
                    );
                if (input.name.StartsWith("past_", StringComparison.Ordinal))
                    continue;
                if (input.name.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0)
                    positionIdsName = input.name;
                else if (input.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                    attentionMaskName = input.name;
                else
                    inputIdsName = input.name;
            }
            if (inputIdsName == null || attentionMaskName == null)
                throw new InvalidDataException(
                    "Could not identify input_ids/attention_mask model inputs."
                );

            if (cached)
            {
                cacheInputNames = model
                    .inputs.Where(input => input.name.StartsWith("past_", StringComparison.Ordinal))
                    .Select(input => input.name)
                    .OrderBy(CacheNameOrder)
                    .ToArray();
                cacheOutputNames = cacheInputNames
                    .Select(name => "present_" + name.Substring("past_".Length))
                    .ToArray();
                contextLength = model
                    .inputs.First(input => input.name == attentionMaskName)
                    .shape.ToTensorShape()[1];
                if (positionIdsName == null || cacheInputNames.Length == 0)
                    throw new InvalidDataException("Incomplete KV-cache model interface.");
                if ((cacheInputNames.Length & 1) != 0)
                    throw new InvalidDataException(
                        $"Expected paired K/V cache inputs, found {cacheInputNames.Length}."
                    );
                transformerBlockCount = cacheInputNames.Length / 2;
            }
            else
            {
                contextLength = model.inputs[0].shape.ToTensorShape()[1];
                transformerBlockCount = options.TransformerBlockCount;
                if (transformerBlockCount <= 0)
                    throw new InvalidDataException(
                        "The uncached model does not expose its transformer block count. "
                            + "Set ChatServiceOptions.TransformerBlockCount."
                    );
            }
            if (contextLength < 8)
                throw new InvalidDataException($"Suspicious context length {contextLength}.");

            layerOpNames = model.layers.Select(layer => layer.opName).ToArray();
        }

        static int CacheNameOrder(string name)
        {
            string[] parts = name.Split('_');
            int layer = int.Parse(parts[1]);
            int kind = parts[2] == "key" ? 0 : 1;
            return layer * 2 + kind;
        }

        void InitializeKvCache()
        {
            cacheInputs = new Tensor<float>[cacheInputNames.Length];
            for (int i = 0; i < cacheInputNames.Length; i++)
            {
                TensorShape shape = model
                    .inputs.First(input => input.name == cacheInputNames[i])
                    .shape.ToTensorShape();
                cacheInputs[i] = new Tensor<float>(shape, new float[shape.length]);
            }
            cacheAttentionMask = new int[contextLength - 1];
            ResetKvCache();
        }

        void ResetKvCache()
        {
            for (int i = 0; i < cacheInputs.Length; i++)
            {
                TensorShape shape = cacheInputs[i].shape;
                cacheInputs[i].Dispose();
                cacheInputs[i] = new Tensor<float>(shape, new float[shape.length]);
            }
            Array.Clear(cacheAttentionMask, 0, cacheAttentionMask.Length);
            cachedTokenIds.Clear();
            cachedNextLogits = null;
        }

        float[] RunForward(int[] window, int[] mask)
        {
            TensorShape shape = new(1, contextLength);
            using Tensor<int> idsTensor = new(shape, window);
            using Tensor<int> maskTensor = new(shape, mask);
            worker.SetInput(inputIdsName, idsTensor);
            worker.SetInput(attentionMaskName, maskTensor);
            worker.Schedule();
            return ReadLogits();
        }

        float[] RunCachedToken(int token)
        {
            int[] mask = new int[contextLength];
            FillCachedAttentionMask(mask);
            using Tensor<int> idsTensor = new(new TensorShape(1, 1), new[] { token });
            using Tensor<int> maskTensor = new(new TensorShape(1, contextLength), mask);
            using Tensor<int> positionTensor =
                new(new TensorShape(1, 1), new[] { cachedTokenIds.Count });
            worker.SetInput(inputIdsName, idsTensor);
            worker.SetInput(attentionMaskName, maskTensor);
            worker.SetInput(positionIdsName, positionTensor);
            for (int i = 0; i < cacheInputNames.Length; i++)
                worker.SetInput(cacheInputNames[i], cacheInputs[i]);
            worker.Schedule();
            float[] logits = ReadLogits();
            CommitCachedForward(token, logits);
            return logits;
        }

        static int SelectToken(
            float[] logits,
            ChatGenerationOptions generation,
            System.Random random
        )
        {
            int k = Math.Min(generation.TopK, logits.Length);
            int[] ids = new int[k];
            float[] values = new float[k];
            int count = LogitMath.SelectTopK(logits, k, ids, values);
            if (generation.Temperature <= 0f)
                return ids[0];
            return LogitMath.SampleFromSortedTopK(
                ids,
                values,
                count,
                generation.Temperature,
                generation.TopP,
                random
            );
        }

        // ChatML response shape: an optional <think>...</think> block from capable
        // checkpoints, then the visible content, terminated by a ChatML end token.
        internal static void ParseResponse(string rawText, out string content, out string thinking)
        {
            ParseStreamingResponse(rawText, out content, out thinking, out _);
        }

        internal static void ParseStreamingResponse(
            string rawText,
            out string content,
            out string thinking,
            out bool thinkingStarted
        )
        {
            string text = rawText.Trim();
            foreach (string terminator in new[] { "<|im_end|>", "<|endoftext|>" })
            {
                int position = text.IndexOf(terminator, StringComparison.Ordinal);
                if (position >= 0)
                    text = text[..position].TrimEnd();
            }

            thinking = "";
            content = "";
            thinkingStarted = false;

            if (thoughtPrefix.StartsWith(text, StringComparison.Ordinal))
            {
                thinkingStarted = text.Length > 0;
                return;
            }
            if (!text.StartsWith(thoughtPrefix, StringComparison.Ordinal))
            {
                content = text;
                return;
            }

            thinkingStarted = true;
            int end = text.IndexOf(thoughtSuffix, thoughtPrefix.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                thinking = text[thoughtPrefix.Length..].Trim();
                return;
            }

            thinking = text[thoughtPrefix.Length..end].Trim();
            content = text[(end + thoughtSuffix.Length)..].Trim();
        }

        static void ValidateGenerationOptions(ChatGenerationOptions generation)
        {
            if (generation.MaxNewTokens < 1)
                throw new ArgumentOutOfRangeException(nameof(generation.MaxNewTokens));
            if (generation.TopP <= 0f || generation.TopP > 1f)
                throw new ArgumentOutOfRangeException(nameof(generation.TopP));
            if (generation.TopK < 1)
                throw new ArgumentOutOfRangeException(nameof(generation.TopK));
        }

        void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChatService));
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            IsReady = false;
            activeStream?.Cancel();
            worker?.Dispose();
            worker = null;
            if (cacheInputs != null)
                foreach (Tensor<float> tensor in cacheInputs)
                    tensor.Dispose();
            foreach (IDisposable tensor in retainedStreamInputs)
                tensor.Dispose();
            retainedStreamInputs.Clear();
            model = null;
        }
    }
}
