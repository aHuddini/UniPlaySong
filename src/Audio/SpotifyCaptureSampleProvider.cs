using NAudio.Wave;
using UniPlaySong.Common;

namespace UniPlaySong.Audio
{
    // Presents Spotify's captured 16-bit PCM (from SpotifyLoopbackClient) as an IEEE-float
    // ISampleProvider at the capture's sample rate/channels. This is the "seam" — downstream
    // (EffectsChain, VisualizationDataProvider, mixer resample/mono-to-stereo) treats it
    // identically to an AudioFileReader. Underrun -> silence (the client already zero-fills).
    public class SpotifyCaptureSampleProvider : ISampleProvider
    {
        private readonly SpotifyLoopbackClient _client;
        private byte[] _byteBuf = new byte[0];

        public SpotifyCaptureSampleProvider(SpotifyLoopbackClient client)
        {
            _client = client;
            var f = client.Format;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(f.SampleRate, f.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int bytesNeeded = count * 2; // 16-bit source
            if (_byteBuf.Length < bytesNeeded) _byteBuf = new byte[bytesNeeded];
            _client.Read(_byteBuf, 0, bytesNeeded);
            for (int i = 0; i < count; i++)
            {
                short s = (short)(_byteBuf[i * 2] | (_byteBuf[i * 2 + 1] << 8));
                buffer[offset + i] = s / 32768f;
            }
            return count;
        }
    }
}
