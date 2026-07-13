using System;
using NAudio.Wave;
using UniPlaySong.Common;

namespace UniPlaySong.Audio
{
    // Presents Spotify's captured PCM (from SpotifyLoopbackClient) as an IEEE-float
    // ISampleProvider at the capture's sample rate/channels. This is the "seam" — downstream
    // (EffectsChain, VisualizationDataProvider, mixer resample/mono-to-stereo) treats it
    // identically to an AudioFileReader. The shim delivers 32-bit float; a 16-bit path is
    // kept as a fallback. Underrun -> silence (the client already zero-fills).
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

        // The loopback tap sits post-session-volume, so effects mode ducks Spotify to 2^-10
        // and sets this to 1024 to restore the original level (power-of-2, lossless in float).
        // Viz-only mode captures at full volume — leave at 1.
        public float GainCompensation { get; set; } = 1f;

        public int Read(float[] buffer, int offset, int count)
        {
            float gain = GainCompensation;

            if (_client.Format.BitsPerSample == 32)
            {
                // IEEE float source (the shim's requested format)
                int bytesNeeded = count * 4;
                if (_byteBuf.Length < bytesNeeded) _byteBuf = new byte[bytesNeeded];
                _client.Read(_byteBuf, 0, bytesNeeded);
                Buffer.BlockCopy(_byteBuf, 0, buffer, offset * 4, bytesNeeded);
                for (int i = 0; i < count; i++)
                {
                    float v = buffer[offset + i] * gain;
                    // Clamp: if any un-ducked sample slips through, gain would blast it far past 0 dBFS.
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    buffer[offset + i] = v;
                }
            }
            else
            {
                // 16-bit PCM fallback
                int bytesNeeded = count * 2;
                if (_byteBuf.Length < bytesNeeded) _byteBuf = new byte[bytesNeeded];
                _client.Read(_byteBuf, 0, bytesNeeded);
                for (int i = 0; i < count; i++)
                {
                    short s = (short)(_byteBuf[i * 2] | (_byteBuf[i * 2 + 1] << 8));
                    float v = s / 32768f * gain;
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    buffer[offset + i] = v;
                }
            }

            return count;
        }
    }
}
