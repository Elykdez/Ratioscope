using System;
using System.Collections.Generic;
using System.Text;

namespace Hypocycloid.Utils
{
    public static class ArrayHelper
    {
        /// <summary>
        /// Count array elements by counting commas + 1, without splitting
        /// </summary>
        public static int CountArrayElements(string arrayContent)
        {
            if (string.IsNullOrEmpty(arrayContent))
                return 0;

            int commaCount = 0;
            for (int i = 0; i < arrayContent.Length; i++)
            {
                if (arrayContent[i] == ',')
                    commaCount++;
            }
            return commaCount + 1;
        }

        /// <summary>
        /// Append the first N elements from an array string without splitting the entire string
        /// </summary>
        public static void AppendFirstArrayElements(StringBuilder sb, string arrayContent, int count)
        {
            if (string.IsNullOrEmpty(arrayContent) || count <= 0)
                return;

            int elementStart = 0;
            int elementsAdded = 0;

            for (int i = 0; i <= arrayContent.Length && elementsAdded < count; i++)
            {
                if (i == arrayContent.Length || arrayContent[i] == ',')
                {
                    // Found end of element
                    if (elementsAdded > 0)
                        sb.Append(',');

                    // Append element with trimming
                    int elementEnd = i;

                    // Trim whitespace from start
                    while (elementStart < elementEnd && char.IsWhiteSpace(arrayContent[elementStart]))
                        elementStart++;

                    // Trim whitespace from end
                    while (elementEnd > elementStart && char.IsWhiteSpace(arrayContent[elementEnd - 1]))
                        elementEnd--;

                    if (elementEnd > elementStart)
                        sb.Append(arrayContent.AsSpan(elementStart, elementEnd - elementStart));

                    elementsAdded++;
                    elementStart = i + 1;
                }
            }
        }

        /// <summary>
        /// Returns a new shuffled copy of the given array (Fisher–Yates).
        /// No generic constraint needed; works for any T.
        /// </summary>
        public static T[] ShuffledCopy<T>(T[] array, int? seed = null)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            var copy = (T[])array.Clone();
            ShuffleInPlace(copy, seed);
            return copy;
        }

        /// <summary>
        /// Returns a new shuffled copy of the given list (Fisher–Yates).
        /// </summary>
        public static T[] ShuffledCopy<T>(IList<T> list, int? seed = null)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            var arr = new T[list.Count];
            for (int i = 0; i < list.Count; i++)
                arr[i] = list[i];
            ShuffleInPlace(arr, seed);
            return arr;
        }

        /// <summary>
        /// Shuffles an array in place (Fisher–Yates).
        /// </summary>
        public static void ShuffleInPlace<T>(T[] array, int? seed = null)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            var rng = seed.HasValue ? new Random(seed.Value) : new Random(unchecked(Environment.TickCount * 397));
            for (int i = array.Length - 1; i > 0; i--)
            {
                int k = rng.Next(i + 1);
                // swap
                (array[k], array[i]) = (array[i], array[k]);
            }
        }

        /// <summary>
        /// Returns an endless sequence that yields each element exactly once per cycle in a new random order.
        /// Useful if you want to "keep showing tips forever" without repeats until a cycle ends.
        /// </summary>
        public static IEnumerable<T> EndlessShuffledCycle<T>(IList<T> source, int? seed = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Count == 0)
                yield break;

            var buffer = ShuffledCopy(source, seed);
            var rng = seed.HasValue ? new Random(seed.Value) : new Random(unchecked(Environment.TickCount * 397));

            while (true)
            {
                for (int i = 0; i < buffer.Length; i++)
                    yield return buffer[i];

                // reshuffle in place for next cycle
                for (int i = buffer.Length - 1; i > 0; i--)
                {
                    int k = rng.Next(i + 1);
                    (buffer[k], buffer[i]) = (buffer[i], buffer[k]);
                }
            }
        }

        /// <summary>
        /// Utility for cycling an index [0..length). Returns 0 if length == 0.
        /// </summary>
        public static int NextCycledIndex(int index, int length)
        {
            if (length <= 0)
                return 0;
            index++;
            return (index >= length) ? 0 : index;
        }
    }
}
