using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hypocycloid.Utils;
using Unity.InferenceEngine;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Frame-budgeted incremental generation. Call Tick from the main thread each frame:
    /// GPUCompute schedules the graph in one batch to avoid thousands of command-buffer
    /// submissions per token. CPU execution remains frame-budgeted via ScheduleIterable.
    /// </summary>
    public sealed class ChatStream : IDisposable
    {
        readonly ChatService service;
        readonly ChatGenerationOptions generation;
        readonly IReadOnlyCollection<int> stopTokens;
        readonly IReadOnlyList<int> promptIds;
        readonly List<int> generated = new();
        readonly int budget;
        readonly System.Random random = new();
        readonly int[] window;
        readonly int[] mask;
        readonly Stopwatch tokenStopwatch = new();
        readonly Stopwatch totalStopwatch = new();
        readonly List<IDisposable> retainedInputs = new();

        IEnumerator forward;
        Tensor<int> idsTensor;
        Tensor<int> maskTensor;
        Tensor<int> positionTensor;
        int layerIndex;
        bool waitingForOutput;
        int promptIngestIndex;
        int processedGeneratedCount;
        int currentForwardToken;
        bool wholeGraphScheduled;

        public ChatStreamState State { get; private set; } = ChatStreamState.Thinking;
        public ChatResult Result { get; private set; }
        public IReadOnlyList<int> GeneratedIds => generated;
        public int PromptTokens => promptIds.Count;
        public int WindowCapacity { get; }
        public int LayerCount { get; }
        public int TransformerBlockCount { get; }

        /// <summary>
        /// Graph-operation scheduling telemetry. CPU reports every operation; batched GPU
        /// execution reports one completion marker without splitting the command buffer.
        /// </summary>
        public event Action<int, int, string> LayerExecuted;

        /// <summary>Raised once after each real model forward finishes and logits are readable.</summary>
        public event Action ForwardEvaluated;
        public event Action<TokenMetrics> TokenSampled;
        public event Action<ChatResult> Completed;

        internal ChatStream(
            ChatService service,
            IReadOnlyList<int> promptIds,
            ChatGenerationOptions generation,
            IReadOnlyCollection<int> stopTokens
        )
        {
            this.service = service;
            this.promptIds = promptIds;
            this.generation = generation;
            this.stopTokens =
                stopTokens
                ?? new[] { service.Tokenizer.EosTokenId, service.Tokenizer.EndOfTurnTokenId };

            WindowCapacity = service.ContextLength;
            LayerCount = service.LayerOpNames.Length;
            TransformerBlockCount = service.TransformerBlockCount;
            budget = Math.Min(generation.MaxNewTokens, WindowCapacity - promptIds.Count);
            window = new int[WindowCapacity];
            mask = new int[WindowCapacity];
            if (service.UsesKvCache)
                promptIngestIndex = service.PrepareCachedPrompt(promptIds);
        }

        /// <summary>
        /// Advances generation until roughly maxMilliseconds have elapsed.
        /// Returns true while more work remains.
        /// </summary>
        public bool Tick(double maxMilliseconds = 8)
        {
            if (State != ChatStreamState.Thinking)
                return false;

            totalStopwatch.Start();
            Stopwatch slice = Stopwatch.StartNew();
            try
            {
                do
                {
                    if (waitingForOutput)
                    {
                        if (!TryFinishForward())
                            break;
                        continue;
                    }

                    if (forward == null && !wholeGraphScheduled)
                        BeginForward();
                    if (wholeGraphScheduled)
                    {
                        PublishBatchedGraphTelemetry();
                        BeginOutputReadback();
                    }
                    else if (forward.MoveNext())
                    {
                        int index = Math.Min(layerIndex++, LayerCount - 1);
                        LayerExecuted?.Invoke(index, LayerCount, service.LayerOpNames[index]);
                    }
                    else
                    {
                        BeginOutputReadback();
                    }
                } while (
                    State == ChatStreamState.Thinking
                    && slice.Elapsed.TotalMilliseconds < maxMilliseconds
                );
            }
            catch
            {
                State = ChatStreamState.Faulted;
                RetainTensors();
                service.ReleaseStream(this);
                throw;
            }
            finally
            {
                totalStopwatch.Stop();
            }
            return State == ChatStreamState.Thinking;
        }

        public void Cancel()
        {
            if (State != ChatStreamState.Thinking)
                return;
            State = ChatStreamState.Cancelled;
            try
            {
                // Sentis schedules backend work asynchronously. Finish and synchronize the
                // current forward before releasing its input tensors.
                if (waitingForOutput)
                {
                    service.ReadLogits();
                }
                else if (forward != null)
                {
                    while (forward.MoveNext()) { }
                    service.ReadLogits();
                    (forward as IDisposable)?.Dispose();
                }
            }
            catch (Exception exception)
            {
                LogHelper.LogError(exception);
            }
            finally
            {
                forward = null;
                waitingForOutput = false;
                RetainTensors();
                service.ReleaseStream(this);
            }
        }

        /// <summary>
        /// Stops generation but finalizes a result from the tokens produced so far, so a
        /// user-cancelled reply can be kept like a truncated completion. If the stream already
        /// completed, its existing result is returned unchanged.
        /// </summary>
        public ChatResult CancelWithPartialResult()
        {
            Cancel();
            if (Result == null)
            {
                string rawText = service.Tokenizer.Decode(generated, skipSpecialTokens: false);
                ChatService.ParseResponse(rawText, out string content, out string thinking);
                Result = new ChatResult(
                    content,
                    thinking,
                    rawText,
                    promptIds.Count,
                    generated.Count,
                    totalStopwatch.Elapsed.TotalSeconds,
                    ChatFinishReason.Cancelled
                );
            }
            return Result;
        }

        public void Dispose() => Cancel();

        void BeginForward()
        {
            tokenStopwatch.Restart();
            if (service.UsesKvCache)
            {
                if (promptIngestIndex < promptIds.Count)
                    currentForwardToken = promptIds[promptIngestIndex++];
                else if (processedGeneratedCount < generated.Count)
                    currentForwardToken = generated[processedGeneratedCount++];
                else
                    throw new InvalidOperationException("No token is available for cached decode.");

                service.FillCachedAttentionMask(mask);
                idsTensor = new Tensor<int>(new TensorShape(1, 1), new[] { currentForwardToken });
                maskTensor = new Tensor<int>(new TensorShape(1, WindowCapacity), mask);
                positionTensor = new Tensor<int>(
                    new TensorShape(1, 1),
                    new[] { service.CachedPosition }
                );
                if (service.SchedulesWholeGraph)
                {
                    service.ScheduleCachedForward(idsTensor, maskTensor, positionTensor);
                    wholeGraphScheduled = true;
                }
                else
                {
                    forward = service.ScheduleCachedForwardIterable(
                        idsTensor,
                        maskTensor,
                        positionTensor
                    );
                }
            }
            else
            {
                ChatService.FillWindow(promptIds, generated, WindowCapacity, window, mask);
                TensorShape shape = new(1, WindowCapacity);
                idsTensor = new Tensor<int>(shape, window);
                maskTensor = new Tensor<int>(shape, mask);
                if (service.SchedulesWholeGraph)
                {
                    service.ScheduleForward(idsTensor, maskTensor);
                    wholeGraphScheduled = true;
                }
                else
                {
                    forward = service.ScheduleForwardIterable(idsTensor, maskTensor);
                }
            }
            layerIndex = 0;
        }

        void BeginOutputReadback()
        {
            (forward as IDisposable)?.Dispose();
            forward = null;
            wholeGraphScheduled = false;
            service.RequestLogitsReadback();
            waitingForOutput = true;
            RetainInputTensors();
        }

        void PublishBatchedGraphTelemetry()
        {
            int index = LayerCount - 1;
            LayerExecuted?.Invoke(index, LayerCount, service.LayerOpNames[index]);
        }

        bool TryFinishForward()
        {
            if (!service.AreLogitsReady())
                return false;

            float[] logits = service.ReadLogits();
            waitingForOutput = false;

            if (service.UsesKvCache)
            {
                service.CommitCachedForward(currentForwardToken, logits);
            }
            tokenStopwatch.Stop();
            ForwardEvaluated?.Invoke();
            if (service.UsesKvCache && promptIngestIndex < promptIds.Count)
                return true;

            TokenMetrics metrics = TokenMetrics.FromLogits(logits, generation.TopK);
            int next =
                generation.Temperature <= 0f
                    ? metrics.TopIds[0]
                    : LogitMath.SampleFromSortedTopK(
                        metrics.TopIds,
                        metrics.TopLogits,
                        metrics.TopCount,
                        generation.Temperature,
                        generation.TopP,
                        random
                    );

            bool isStop = stopTokens.Contains(next);
            metrics.Index = generated.Count;
            metrics.TokenId = next;
            metrics.Text = service.Tokenizer.Decode(new[] { next }, skipSpecialTokens: false);
            metrics.ElapsedSeconds = tokenStopwatch.Elapsed.TotalSeconds;
            metrics.WindowCapacity = WindowCapacity;
            metrics.IsStopToken = isStop;
            FillCandidateTexts(metrics);
            if (!isStop)
                generated.Add(next);
            metrics.WindowTokens = promptIds.Count + generated.Count;

            TokenSampled?.Invoke(metrics);
            if (isStop)
                Complete(ChatFinishReason.StopToken);
            else if (generated.Count >= budget)
                Complete(
                    promptIds.Count + generated.Count >= WindowCapacity
                        ? ChatFinishReason.ContextLimit
                        : ChatFinishReason.TokenLimit
                );
            return true;
        }

        void FillCandidateTexts(TokenMetrics metrics)
        {
            TokenCandidate[] candidates = (TokenCandidate[])metrics.TopCandidates;
            for (int i = 0; i < candidates.Length; i++)
            {
                candidates[i] = new TokenCandidate(
                    candidates[i].Id,
                    service.Tokenizer.Decode(new[] { candidates[i].Id }, skipSpecialTokens: false),
                    candidates[i].Probability
                );
            }
        }

        void Complete(ChatFinishReason finishReason)
        {
            string rawText = service.Tokenizer.Decode(generated, skipSpecialTokens: false);
            ChatService.ParseResponse(rawText, out string content, out string thinking);
            Result = new ChatResult(
                content,
                thinking,
                rawText,
                promptIds.Count,
                generated.Count,
                totalStopwatch.Elapsed.TotalSeconds,
                finishReason
            );
            State = ChatStreamState.Completed;
            RetainTensors();
            service.ReleaseStream(this);
            Completed?.Invoke(Result);
        }

        void RetainInputTensors()
        {
            if (idsTensor != null)
                retainedInputs.Add(idsTensor);
            idsTensor = null;
            if (maskTensor != null)
                retainedInputs.Add(maskTensor);
            maskTensor = null;
            if (positionTensor != null)
                retainedInputs.Add(positionTensor);
            positionTensor = null;
        }

        void RetainTensors()
        {
            RetainInputTensors();
            foreach (IDisposable tensor in retainedInputs)
                service.RetainStreamInput(tensor);
            retainedInputs.Clear();
        }
    }
}
