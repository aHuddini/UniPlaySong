using System;

namespace UniPlaySong.Models.WaveformTrim
{
    // Waveform sample data for visual display
    public class WaveformData
    {
        // Normalized samples (-1.0 to 1.0) for display. Approximately 1000 samples for smooth rendering.
        public float[] Samples { get; set; }

        // Total duration of the audio file
        public TimeSpan Duration { get; set; }

        // Sample rate of source audio
        public int SampleRate { get; set; }

        // Number of channels (1=mono, 2=stereo)
        public int Channels { get; set; }

        // Source file path
        public string FilePath { get; set; }

        // Whether the waveform data is valid and ready for display
        public bool IsValid => Samples != null && Samples.Length > 0 && Duration.TotalSeconds > 0;

    }
}
