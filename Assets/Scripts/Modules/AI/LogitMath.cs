using System;

namespace Hypocycloid.Ratioscope
{
    /// <summary>Shared logit-space math for sampling and metrics.</summary>
    static class LogitMath
    {
        /// <summary>
        /// Partial selection of the k largest logits, sorted descending into ids/values.
        /// Returns the number of filled entries.
        /// </summary>
        public static int SelectTopK(float[] logits, int k, int[] ids, float[] values)
        {
            int count = 0;
            for (int i = 0; i < logits.Length; i++)
            {
                if (count == k && logits[i] <= values[count - 1])
                    continue;

                int insert = count < k ? count : k - 1;
                while (insert > 0 && logits[i] > values[insert - 1])
                {
                    values[insert] = values[insert - 1];
                    ids[insert] = ids[insert - 1];
                    insert--;
                }
                values[insert] = logits[i];
                ids[insert] = i;
                if (count < k)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// One-pass max, softmax normalizer, and full-distribution entropy (nats):
        /// H = ln(sumExp) - sum(exp(l - max) * (l - max)) / sumExp.
        /// </summary>
        public static void SoftmaxStatistics(
            float[] logits,
            out float max,
            out double sumExp,
            out float entropy
        )
        {
            max = float.NegativeInfinity;
            for (int i = 0; i < logits.Length; i++)
            {
                if (logits[i] > max)
                    max = logits[i];
            }

            sumExp = 0;
            double weighted = 0;
            for (int i = 0; i < logits.Length; i++)
            {
                double shifted = logits[i] - max;
                double exp = Math.Exp(shifted);
                sumExp += exp;
                weighted += exp * shifted;
            }
            entropy = (float)(Math.Log(sumExp) - weighted / sumExp);
        }

        /// <summary>
        /// Temperature scaling, then top-p over the pre-selected descending top-k, then
        /// categorical sampling - the same filter order Hugging Face applies.
        /// </summary>
        public static int SampleFromSortedTopK(
            int[] ids,
            float[] logits,
            int count,
            float temperature,
            float topP,
            Random random
        )
        {
            double[] probabilities = new double[count];
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                probabilities[i] = Math.Exp((logits[i] - logits[0]) / temperature);
                sum += probabilities[i];
            }

            int kept = count;
            double cumulative = 0;
            for (int i = 0; i < count; i++)
            {
                cumulative += probabilities[i] / sum;
                if (cumulative >= topP)
                {
                    kept = i + 1;
                    break;
                }
            }

            double keptSum = 0;
            for (int i = 0; i < kept; i++)
                keptSum += probabilities[i];
            double draw = random.NextDouble() * keptSum;
            for (int i = 0; i < kept; i++)
            {
                draw -= probabilities[i];
                if (draw <= 0)
                    return ids[i];
            }
            return ids[kept - 1];
        }
    }
}
