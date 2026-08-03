using System.Diagnostics;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Diagnostic per-token timing breakdown of the cached decode loop. Accumulates wall time for
    /// each stage of a forward so the cost of a token can be attributed without a profiler capture.
    /// Written unconditionally but only read by tooling; remove once the decode path is settled.
    /// </summary>
    public static class ChatStreamProfile
    {
        public static double PrepareMs;
        public static double SetInputMs;
        public static double ScheduleMs;
        public static double ReadbackRequestMs;
        public static double WaitMs;
        public static double CacheCopyMs;
        public static int Tokens;

        public static void Reset()
        {
            PrepareMs = 0;
            SetInputMs = 0;
            ScheduleMs = 0;
            ReadbackRequestMs = 0;
            WaitMs = 0;
            CacheCopyMs = 0;
            Tokens = 0;
        }

        static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;

        /// <summary>Timestamp to pass to <see cref="Add"/>. Allocation-free.</summary>
        public static long Now() => Stopwatch.GetTimestamp();

        public static void Add(ref double target, long startTicks)
        {
            target += (Stopwatch.GetTimestamp() - startTicks) * MillisecondsPerTick;
        }

        public static string Summary()
        {
            int n = Tokens > 0 ? Tokens : 1;
            double total =
                PrepareMs + SetInputMs + ScheduleMs + ReadbackRequestMs + WaitMs + CacheCopyMs;
            return $"tokens={Tokens} prepare={PrepareMs / n:0.0} setInput={SetInputMs / n:0.0} "
                + $"schedule={ScheduleMs / n:0.0} readbackReq={ReadbackRequestMs / n:0.0} "
                + $"wait={WaitMs / n:0.0} cacheCopy={CacheCopyMs / n:0.0} sum={total / n:0.0}ms";
        }
    }
}
