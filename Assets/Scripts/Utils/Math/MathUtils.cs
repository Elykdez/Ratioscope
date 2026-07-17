using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Hypocycloid.Utils
{
    public static class MathUtils
    {
        public const float ONE_THIRD = 1.0f / 3.0f;
        public const float EPS = 1e-6f;

        // Empirically constant for float reciprocal
        // In integer-bit space 1/value = (1/(1+m)) * 2^{-(e-127)} the exponent bits are linearly related to the final bit pattern;
        // Subtracting the float bits from a constant reverses the exponent and gives a linearized approximation of 1/(1+m).
        const uint Kr = 0x7EF311C2u;

        // Empirically constant for float reciprocal square root
        const uint Krs = 0x5F375A86u;

        /// <summary>Calculate the least amount of groups needed based on thread count.</summary>
        /// <param name="threadCount">total amount of size available</param>
        /// <param name="grpSize">maximum size of each divisions after the split</param>
        /// <returns>Group count.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CalculateGrids(int threadCount, int grpSize) => (threadCount + grpSize - 1) / grpSize;

        /// <summary>Calculate the least amount of groups needed based on thread count.</summary>
        /// <param name="threadCount">total amount of size available</param>
        /// <param name="grpSize">maximum size of each divisions after the split</param>
        /// <returns>Group count.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CalculateGrids(uint threadCount, uint grpSize) =>
            (int)((threadCount + grpSize - 1) / grpSize);

        /// <summary>Convert float3 point location to int3 grid location.</summary>
        /// <param name="p">point</param>
        /// <param name="unitSize">unit size</param>
        /// <remarks>
        /// Point location should always be positive in all axis to obtain accurate grid location.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 PointToGrid(float3 p, float unitSize) => new(p / unitSize);

        /// <summary>
        /// Set all values in that array to the given value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetArray<T>(ref T[] array, T value)
        {
            for (int i = 0; i < array.Length; i++)
                array[i] = value;
        }

        /// <summary>
        /// Generate an int array with sequence id as it's value
        /// (ex: [0, 1, 2, 3, 4] will be generated if `length` of 5 is being given)
        /// </summary>
        /// <param name="length">number of elements</param>
        public static int[] GenerateSeqArray(int length)
        {
            int[] array = new int[length];
            for (int i = 0; i < length; i++)
                array[i] = i;
            return array;
        }

        /// <summary>Shuffles an array.</summary>
        public static void ShuffleArray<T>(ref T[] decklist)
        {
            for (int i = 0; i < decklist.Length; i++)
            {
                int randomIdx = UnityEngine.Random.Range(0, decklist.Length);
                (decklist[i], decklist[randomIdx]) = (decklist[randomIdx], decklist[i]);
            }
        }

        /// <summary>
        /// 快速倒数<br/>
        /// 适用于精度要求不高 + 每帧执行的计算<br/>
        /// 建议非桌面平台如WebGL用, 桌面平台有硬件加速建议直接用原生。
        /// </summary>
        /// <param name="iter">Number of Newton-Raphson refinement steps.</param>
        /// <returns>Approximation of 1 / x.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Rcp(float x, int iter = 1)
        {
            if (x == 0f)
                return float.PositiveInfinity;

            // Bit-level hack: initial guess (approx 1 / a)
            uint ui = math.asuint(x);
            // r0 = reinterpret_as_float(K - int_bits(a)) is a fast linear-ish approximation over mantissa + exponent.
            ui = Kr - ui;
            float r = math.asfloat(ui);

            // Computes the reciprocal (1 / x) using Newton–Raphson iteration.
            // Common-case unrolled for performance
            if (iter <= 0)
                return r;

            if (iter == 1)
            {
                r *= 2f - x * r;
                return r;
            }

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            r = math.rcp(x); // use hardware rcp
#else
            // fallback for larger iter
            for (int n = 0; n < iter; n++)
                r *= 2f - x * r;
#endif

            return r;
        }

        /// <summary>
        /// 快速平方根倒数<br/>
        /// 适用于精度要求不高 + 每帧执行的计算<br/>
        /// </summary>
        /// <param name="iter">Number of Newton-Raphson refinement steps.</param>
        /// <returns>Approximation of 1 / sqrt(x).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RcpSqrt(float x, int iter = 1)
        {
            if (x <= 0f)
                return x == 0f ? float.PositiveInfinity : float.NaN;

            // Bit-level hack: initial guess (use Unity.Mathematics bit-casts to avoid BitConverter)
            uint ui = math.asuint(x);
            // Divide by 2 is same as right shift by 1
            ui = Krs - (ui >> 1);
            float r = math.asfloat(ui);

            /// Computes the reciprocal square root (1 / sqrt(x)) using Newton-Raphson iteration.
            // common-case unrolled for performance
            if (iter <= 0)
                return r;

            if (iter == 1)
            {
                float halfX = 0.5f * x;
                r *= 1.5f - halfX * r * r;
                return r;
            }

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            r = math.rsqrt(x); // use hardware rsqrt
#else
            // fallback for larger iter
            float half = 0.5f * x;
            for (int n = 0; n < iter; n++)
                r *= 1.5f - half * r * r;
#endif

            return r;
        }

        /// <summary>
        /// 快速平方根(使用 RcpSqrt 乘原数)<br/>
        /// 适用于精度要求不高 + 每帧执行的计算<br/>
        /// Computes sqrt(x) using RcpSqrt approximation (sqrt(x) = x * rcp_sqrt(x)).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sqrt(float x, int iter = 1)
        {
            if (x == 0f)
                return 0f;
            return x * RcpSqrt(x, iter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Near(float a, float b)
        {
            // If a or b is zero, compare that the other is less or equal to epsilon.
            // If neither a or b are 0, then find an epsilon that is good for
            // comparing numbers at the maximum magnitude of a and b.
            // Floating points have about 7 significant digits, so
            // 1.000001f can be represented while 1.0000001f is rounded to zero,
            // thus we could use an epsilon of 0.000001f for comparing values close to 1.
            // We multiply this epsilon by the biggest magnitude of a and b.
            return math.abs(b - a) < math.max(0.000001f * math.max(math.abs(a), math.abs(b)), math.EPSILON * 8);
        }

        /// <summary>Check if number is close to zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearZero(float a) => math.abs(a) < 1e-6f;

        /// <summary>Inverse lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Invlerp(float from, float to, float value) => (value - from) / (to - from);

        /// <summary>Inverse lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Invlerp(float2 from, float2 to, float2 value) => (value - from) / (to - from);

        /// <summary>Inverse lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 Invlerp(float3 from, float3 to, float3 value) => (value - from) / (to - from);

        /// <summary>Inverse lerp.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 Invlerp(float4 from, float4 to, float4 value) => (value - from) / (to - from);
    }
}
