using System;

namespace UniPlaySong.Models.WaveformTrim
{
    /// <summary>
    /// Waveform sample data for visual display
    /// </summary>
    public class WaveformData
    {
        /// <summary>
        /// Normalized samples (-1.0 to 1.0) for display.
        /// Approximately 1000 samples for smooth rendering.
        /// </summary>
        public float[] Samples { get; set; }

        /// <summary>
        /// Total duration of the audio file
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Sample rate of source audio
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// Number of channels (1=mono, 2=stereo)
        /// </summary>
        public int Channels { get; set; }

        /// <summary>
        /// Source file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Whether the waveform data is valid and ready for display
        /// </summary>
        public bool IsValid => Samples != null && Samples.Length > 0 && Duration.TotalSeconds > 0;

        /// <summary>
        /// Get the time position for a given sample index
        /// </summary>
        public TimeSpan GetTimeAtIndex(int index)
        {
            if (Samples == null || Samples.Length == 0) return TimeSpan.Zero;
            var fraction = (double)index / Samples.Length;
            return TimeSpan.FromMilliseconds(Duration.TotalMilliseconds * fraction);
        }

        /// <summary>
        /// Get the sample index for a given time position
        /// </summary>
        public int GetIndexAtTime(TimeSpan time)
        {
            if (Samples == null || Samples.Length == 0 || Duration.TotalMilliseconds <= 0) return 0;
            var fraction = time.TotalMilliseconds / Duration.TotalMilliseconds;
            return (int)Math.Round(fraction * (Samples.Length - 1));
        }
    }
}
