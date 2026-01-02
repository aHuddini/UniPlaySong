using System;

namespace UniPlaySong.Models.WaveformTrim
{
    /// <summary>
    /// Represents a trim selection window with start and end times
    /// </summary>
    public class TrimWindow
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }

        /// <summary>
        /// Duration of the kept portion
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Start position as percentage (0-100)
        /// </summary>
        public double StartPercent => TotalDuration.TotalMilliseconds > 0
            ? StartTime.TotalMilliseconds / TotalDuration.TotalMilliseconds * 100
            : 0;

        /// <summary>
        /// End position as percentage (0-100)
        /// </summary>
        public double EndPercent => TotalDuration.TotalMilliseconds > 0
            ? EndTime.TotalMilliseconds / TotalDuration.TotalMilliseconds * 100
            : 100;

        /// <summary>
        /// Whether the trim window is valid
        /// </summary>
        public bool IsValid => StartTime >= TimeSpan.Zero
            && EndTime <= TotalDuration
            && StartTime < EndTime
            && Duration.TotalSeconds >= 1.0; // Minimum 1 second

        /// <summary>
        /// Create a trim window representing the full duration
        /// </summary>
        public static TrimWindow FullDuration(TimeSpan totalDuration)
        {
            return new TrimWindow
            {
                StartTime = TimeSpan.Zero,
                EndTime = totalDuration,
                TotalDuration = totalDuration
            };
        }

        /// <summary>
        /// Shift both markers by a percentage of total duration
        /// </summary>
        public void ShiftByPercent(double percent)
        {
            var shiftAmount = TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds * percent / 100);
            ShiftByTime(shiftAmount);
        }

        /// <summary>
        /// Shift both markers by a fixed time amount (positive = forward, negative = backward)
        /// </summary>
        public void ShiftByTime(TimeSpan amount)
        {
            var newStart = StartTime + amount;
            var newEnd = EndTime + amount;

            // Clamp to valid range, preserving window size
            if (newStart < TimeSpan.Zero)
            {
                newEnd = newEnd - newStart; // Shift end by the overflow
                newStart = TimeSpan.Zero;
            }
            if (newEnd > TotalDuration)
            {
                newStart = newStart - (newEnd - TotalDuration); // Shift start by the overflow
                newEnd = TotalDuration;
            }

            StartTime = newStart < TimeSpan.Zero ? TimeSpan.Zero : newStart;
            EndTime = newEnd > TotalDuration ? TotalDuration : newEnd;
        }

        /// <summary>
        /// Adjust window size by moving edges symmetrically (percentage)
        /// </summary>
        public void AdjustSizeByPercent(double percent)
        {
            var adjustAmount = TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds * percent / 100);
            AdjustSizeByTime(adjustAmount);
        }

        /// <summary>
        /// Adjust window size by moving edges symmetrically by a fixed time (positive = expand, negative = contract)
        /// </summary>
        public void AdjustSizeByTime(TimeSpan amount)
        {
            var newStart = StartTime - amount;
            var newEnd = EndTime + amount;

            // Clamp to valid range
            if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
            if (newEnd > TotalDuration) newEnd = TotalDuration;

            // Ensure minimum duration
            if ((newEnd - newStart).TotalSeconds >= 1.0)
            {
                StartTime = newStart;
                EndTime = newEnd;
            }
        }

        /// <summary>
        /// Adjust start marker by a percentage
        /// </summary>
        public void AdjustStartByPercent(double percent)
        {
            var adjustAmount = TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds * percent / 100);
            AdjustStartByTime(adjustAmount);
        }

        /// <summary>
        /// Adjust start marker by a fixed time amount (positive = move later, negative = move earlier)
        /// </summary>
        public void AdjustStartByTime(TimeSpan amount)
        {
            var newStart = StartTime + amount;

            // Clamp and ensure minimum duration
            if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
            if ((EndTime - newStart).TotalSeconds >= 1.0)
            {
                StartTime = newStart;
            }
        }

        /// <summary>
        /// Adjust end marker by a percentage
        /// </summary>
        public void AdjustEndByPercent(double percent)
        {
            var adjustAmount = TimeSpan.FromMilliseconds(TotalDuration.TotalMilliseconds * percent / 100);
            AdjustEndByTime(adjustAmount);
        }

        /// <summary>
        /// Adjust end marker by a fixed time amount (positive = move later, negative = move earlier)
        /// </summary>
        public void AdjustEndByTime(TimeSpan amount)
        {
            var newEnd = EndTime + amount;

            // Clamp and ensure minimum duration
            if (newEnd > TotalDuration) newEnd = TotalDuration;
            if (newEnd < TimeSpan.Zero) newEnd = TimeSpan.Zero;
            if ((newEnd - StartTime).TotalSeconds >= 1.0)
            {
                EndTime = newEnd;
            }
        }

        /// <summary>
        /// Reset to full duration
        /// </summary>
        public void Reset()
        {
            StartTime = TimeSpan.Zero;
            EndTime = TotalDuration;
        }
    }
}
