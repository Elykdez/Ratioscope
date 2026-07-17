using System;
using System.Collections.Generic;

namespace Hypocycloid.Ratioscope
{
    public readonly struct TokenCandidate
    {
        public readonly int Id;
        public readonly string Text;
        public readonly float Probability;

        public TokenCandidate(int id, string text, float probability)
        {
            Id = id;
            Text = text;
            Probability = probability;
        }
    }

    /// <summary>
    /// Per-token generation telemetry: the sampled token, the model's top candidates with
    /// full-softmax probabilities, and the entropy of the full output distribution (nats).
    /// Produced by ChatStream for every generated token; also computable from any raw
    /// logits row via FromLogits for tests and tools.
    /// </summary>
    public sealed class TokenMetrics
    {
        /// <summary>Zero-based position within the generated reply.</summary>
        public int Index { get; internal set; }
        public int TokenId { get; internal set; }
        public string Text { get; internal set; } = "";
        public IReadOnlyList<TokenCandidate> TopCandidates { get; internal set; }

        /// <summary>Shannon entropy of the full-vocabulary softmax, in nats.</summary>
        public float Entropy { get; internal set; }

        /// <summary>Wall time of the forward pass that produced this token.</summary>
        public double ElapsedSeconds { get; internal set; }
        public int WindowTokens { get; internal set; }
        public int WindowCapacity { get; internal set; }
        public bool IsStopToken { get; internal set; }

        // Raw sorted top-k retained so sampling reuses the same selection.
        internal int[] TopIds;
        internal float[] TopLogits;
        internal int TopCount;

        /// <summary>Computes entropy and the top-k candidates (ids and probabilities only;
        /// candidate texts are filled in by the caller that owns a tokenizer).</summary>
        public static TokenMetrics FromLogits(float[] logits, int topK)
        {
            if (logits == null)
                throw new ArgumentNullException(nameof(logits));
            if (logits.Length == 0)
                throw new ArgumentException("Logits cannot be empty.", nameof(logits));
            if (topK < 1)
                throw new ArgumentOutOfRangeException(nameof(topK));

            int k = Math.Min(topK, logits.Length);
            int[] ids = new int[k];
            float[] values = new float[k];
            int count = LogitMath.SelectTopK(logits, k, ids, values);
            LogitMath.SoftmaxStatistics(
                logits,
                out float max,
                out double sumExp,
                out float entropy
            );

            TokenCandidate[] candidates = new TokenCandidate[count];
            for (int i = 0; i < count; i++)
            {
                float probability = (float)(Math.Exp(values[i] - max) / sumExp);
                candidates[i] = new TokenCandidate(ids[i], "", probability);
            }

            return new TokenMetrics
            {
                TopCandidates = candidates,
                Entropy = entropy,
                TopIds = ids,
                TopLogits = values,
                TopCount = count,
            };
        }
    }
}
