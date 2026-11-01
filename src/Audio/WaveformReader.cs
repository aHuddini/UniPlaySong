using System;
using System.IO;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Opens an audio file for waveform analysis (Trim and Amplify). OGG uses OggFileReader (NVorbis); all others use
    // AudioFileReader. The caller disposes the stream.
    internal static class WaveformReader
    {
        public static (WaveStream stream, ISampleProvider samples) Open(string filePath)
        {
            if (Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                var ogg = new OggFileReader(filePath);
                return (ogg, ogg);
            }
            var afr = new AudioFileReader(filePath);
            return (afr, afr);
        }
    }
}
