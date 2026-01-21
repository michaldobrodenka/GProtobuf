using System;

namespace GProtobuf.Core
{
    /// <summary>
    /// Helper methods for DateTime serialization in protobuf-net BCL format.
    /// Implements hybrid approach: write with optimal Scale, read with universal Scale support.
    /// </summary>
    internal static class DateTimeHelper
    {
        /// <summary>
        /// TimeSpanScale enumeration matching protobuf-net BCL format.
        /// </summary>
        private enum TimeSpanScale
        {
            Days = 0,
            Hours = 1,
            Minutes = 2,
            Seconds = 3,
            Milliseconds = 4,
            Ticks = 5,
            MinMax = 15  // Special scale for DateTime.Min/Max
        }

        // Constants for scale conversion
        private const long TicksPerMillisecond = TimeSpan.TicksPerMillisecond; // 10,000
        private const long TicksPerSecond = TimeSpan.TicksPerSecond;           // 10,000,000
        private const long TicksPerMinute = TimeSpan.TicksPerMinute;           // 600,000,000
        private const long TicksPerHour = TimeSpan.TicksPerHour;               // 36,000,000,000
        private const long TicksPerDay = TimeSpan.TicksPerDay;                 // 864,000,000,000

        // Unix epoch offset (January 1, 1970 UTC) in .NET ticks
        private const long UnixEpochTicks = 621355968000000000L;

        /// <summary>
        /// Determines the optimal scale for a DateTime value to minimize wire size.
        /// Matches protobuf-net BCL behavior.
        /// Uses Unix Epoch offset (1970-01-01) for all scales except MinMax.
        /// </summary>
        /// <returns>Tuple of (scaled value, scale enum value)</returns>
        public static (long scaledValue, int scale) GetOptimalScale(DateTime value)
        {
            // Special cases: DateTime.Min/Max use Scale=15 (MinMax)
            if (value == DateTime.MinValue)
                return (-1, (int)TimeSpanScale.MinMax);

            if (value == DateTime.MaxValue)
                return (1, (int)TimeSpanScale.MinMax);

            long ticks = value.Ticks;

            // Try Seconds first (most common for APIs, best compression)
            // IMPORTANT: protobuf-net uses Unix Epoch offset for Seconds
            // Example: 2024-01-15 10:30:45 UTC → 1705314645 seconds from Unix Epoch
            if (ticks % TicksPerSecond == 0)
            {
                long ticksFromEpoch = ticks - UnixEpochTicks;
                long seconds = ticksFromEpoch / TicksPerSecond;
                return (seconds, (int)TimeSpanScale.Seconds);
            }

            // Try Milliseconds (for timestamps with millisecond precision)
            // Also uses Unix Epoch offset
            if (ticks % TicksPerMillisecond == 0)
            {
                long ticksFromEpoch = ticks - UnixEpochTicks;
                long milliseconds = ticksFromEpoch / TicksPerMillisecond;
                return (milliseconds, (int)TimeSpanScale.Milliseconds);
            }

            // Fallback to Ticks (for high-precision timestamps)
            // Use offset from Unix epoch to reduce varint size
            long adjustedTicks = ticks - UnixEpochTicks;
            return (adjustedTicks, (int)TimeSpanScale.Ticks);
        }

        /// <summary>
        /// Converts a scaled value back to .NET ticks based on the scale.
        /// Supports all protobuf-net BCL TimeSpanScale values for forward/backward compatibility.
        /// IMPORTANT: protobuf-net uses Unix Epoch offset for most scales.
        /// </summary>
        public static long ConvertToTicks(long scaledValue, int scale)
        {
            return scale switch
            {
                // Days, Hours, Minutes: Use Unix Epoch offset (protobuf-net behavior)
                (int)TimeSpanScale.Days => (scaledValue * TicksPerDay) + UnixEpochTicks,
                (int)TimeSpanScale.Hours => (scaledValue * TicksPerHour) + UnixEpochTicks,
                (int)TimeSpanScale.Minutes => (scaledValue * TicksPerMinute) + UnixEpochTicks,

                // Seconds and Milliseconds: Use Unix Epoch offset (confirmed)
                (int)TimeSpanScale.Seconds => (scaledValue * TicksPerSecond) + UnixEpochTicks,
                (int)TimeSpanScale.Milliseconds => (scaledValue * TicksPerMillisecond) + UnixEpochTicks,

                // Ticks: Already uses Unix Epoch offset
                (int)TimeSpanScale.Ticks => scaledValue + UnixEpochTicks,

                // MinMax: Special case for DateTime.Min/Max
                (int)TimeSpanScale.MinMax => scaledValue == -1 ? 0 : DateTime.MaxValue.Ticks,

                _ => throw new InvalidOperationException($"Unknown TimeSpanScale: {scale}")
            };
        }
    }
}
