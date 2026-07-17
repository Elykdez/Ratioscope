using System;

namespace Hypocycloid.Utils
{
    public static class DateTimeExtensions
    {
        public static bool IsEarlierThan(this DateTime a, DateTime b) => a.CompareTo(b) < 0;

        public static bool IsLaterThan(this DateTime a, DateTime b) => a.CompareTo(b) > 0;

        public static bool IsTheSameAs(this DateTime a, DateTime b) => a.CompareTo(b) == 0;
    }
}
