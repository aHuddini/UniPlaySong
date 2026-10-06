using System;
using NAudio.Wave;

namespace UniPlaySong.Audio
{
    // Graphic equalizer, 15 bands (standard 2/3-octave) or 10 (Winamp's), +-12 dB each plus a preamp. One stage just
    // before the output device, so it covers game music, crossfades and an effected Spotify source alike; with the
    // switch off, or every band and the preamp at zero, audio passes through untouched.
    //
    // Filter design after Spotifast's eq.rs (MIT): peaking filters whose width reaches halfway to each neighbouring
    // band, using the Audio EQ Cookbook's digital bandwidth form. NAudio's PeakingEQ takes an analog Q, which comes
    // out several times too narrow near the top bands and ripples the treble.
    public sealed class Equalizer : ISampleProvider
    {
        public static readonly int[] Frequencies10 = { 60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000 };
        public static readonly int[] Frequencies15 =
            { 25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000 };
        public const int RangeDb = 12;

        // A curve's layout is told by its length: 10 or 15 bands.
        public static int[] FrequenciesFor(int bandCount) => bandCount == 10 ? Frequencies10 : Frequencies15;

        private readonly ISampleProvider _source;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly Biquad[][] _filters; // [channel][band], sized for the larger layout
        private int[] _appliedBands = new int[0];
        private double[] _gains = new double[0]; // solved per-filter gains, dB
        private int _appliedPreamp;
        private bool _wasActive;

        public Equalizer(ISampleProvider source, Func<UniPlaySongSettings> getSettings)
        {
            _source = source;
            _getSettings = getSettings;
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;
            _filters = new Biquad[_channels][];
            for (int ch = 0; ch < _channels; ch++)
            {
                _filters[ch] = new Biquad[Frequencies15.Length];
                for (int b = 0; b < Frequencies15.Length; b++) _filters[ch][b] = new Biquad();
            }
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            var s = _getSettings?.Invoke();
            int[] bands = s == null ? null : Bands(s);
            int preamp = s?.EqualizerPreampDb ?? 0;
            bool active = s != null && s.EqualizerEnabled && (preamp != 0 || Array.Exists(bands, g => g != 0));

            if (!active)
            {
                _wasActive = false;
                return read;
            }

            // Coming back on, or switching layouts: start from silence rather than another curve's state.
            if (!_wasActive || bands.Length != _appliedBands.Length)
            {
                foreach (var channel in _filters) foreach (var f in channel) f.Reset();
                _wasActive = true;
            }
            if (preamp != _appliedPreamp || !BandsEqual(bands, _appliedBands)) Configure(bands, preamp);

            float gain = DbToLinear(_appliedPreamp);
            int n = _gains.Length;
            for (int i = 0; i < read; i += _channels)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    double x = buffer[offset + i + ch] * gain;
                    var filters = _filters[ch];
                    for (int b = 0; b < n; b++)
                        if (Math.Abs(_gains[b]) > Flat) x = filters[b].Process(x);
                    buffer[offset + i + ch] = EffectsChain.SoftKneeLimiter((float)x);
                }
            }
            return read;
        }

        public static int BandCount(UniPlaySongSettings s) => s.EqualizerBandCount == 10 ? 10 : 15;

        // The active layout's band gains, in its Frequencies order.
        public static int[] Bands(UniPlaySongSettings s) => BandCount(s) == 10 ? Bands10(s) : Bands15(s);

        public static int[] Bands10(UniPlaySongSettings s) => new[]
        {
            s.EqualizerBand60, s.EqualizerBand170, s.EqualizerBand310, s.EqualizerBand600, s.EqualizerBand1k,
            s.EqualizerBand3k, s.EqualizerBand6k, s.EqualizerBand12k, s.EqualizerBand14k, s.EqualizerBand16k,
        };

        public static int[] Bands15(UniPlaySongSettings s) => new[]
        {
            s.Equalizer15Band25, s.Equalizer15Band40, s.Equalizer15Band63, s.Equalizer15Band100, s.Equalizer15Band160,
            s.Equalizer15Band250, s.Equalizer15Band400, s.Equalizer15Band630, s.Equalizer15Band1k, s.Equalizer15Band1k6,
            s.Equalizer15Band2k5, s.Equalizer15Band4k, s.Equalizer15Band6k3, s.Equalizer15Band10k, s.Equalizer15Band16k,
        };

        // Writes a curve into the layout its length names.
        public static void ApplyBands(UniPlaySongSettings s, int[] b)
        {
            if (b.Length == 10)
            {
                s.EqualizerBand60 = b[0]; s.EqualizerBand170 = b[1]; s.EqualizerBand310 = b[2]; s.EqualizerBand600 = b[3];
                s.EqualizerBand1k = b[4]; s.EqualizerBand3k = b[5]; s.EqualizerBand6k = b[6]; s.EqualizerBand12k = b[7];
                s.EqualizerBand14k = b[8]; s.EqualizerBand16k = b[9];
                return;
            }
            s.Equalizer15Band25 = b[0]; s.Equalizer15Band40 = b[1]; s.Equalizer15Band63 = b[2]; s.Equalizer15Band100 = b[3];
            s.Equalizer15Band160 = b[4]; s.Equalizer15Band250 = b[5]; s.Equalizer15Band400 = b[6]; s.Equalizer15Band630 = b[7];
            s.Equalizer15Band1k = b[8]; s.Equalizer15Band1k6 = b[9]; s.Equalizer15Band2k5 = b[10]; s.Equalizer15Band4k = b[11];
            s.Equalizer15Band6k3 = b[12]; s.Equalizer15Band10k = b[13]; s.Equalizer15Band16k = b[14];
        }

        // Band gains (dB) per preset for a layout. Null for Custom: the sliders are the user's own. The presets are
        // written for 10 bands; the 15-band versions are what the 10-band curve plays at the 15 centres, so a preset
        // sounds the same in either layout.
        public static int[] PresetBands(EqualizerPreset preset, int bandCount = 10)
        {
            int[] ten;
            switch (preset)
            {
                case EqualizerPreset.Flat:        ten = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; break;
                case EqualizerPreset.BassBoost:   ten = new[] { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 }; break;
                case EqualizerPreset.TrebleBoost: ten = new[] { 0, 0, 0, 0, 0, 1, 3, 4, 4, 4 }; break;
                case EqualizerPreset.Vocal:       ten = new[] { -2, -2, -1, 1, 3, 4, 3, 1, 0, -1 }; break;
                case EqualizerPreset.Rock:        ten = new[] { 5, 3, -2, -3, -1, 2, 4, 4, 4, 4 }; break;
                case EqualizerPreset.Pop:         ten = new[] { -1, 2, 4, 5, 3, 0, -1, -1, -1, -1 }; break;
                case EqualizerPreset.Classical:   ten = new[] { 0, 0, 0, 0, 0, 0, -3, -3, -3, -4 }; break;
                case EqualizerPreset.Electronic:  ten = new[] { 5, 4, 1, 0, -2, 2, 1, 2, 3, 3 }; break;
                case EqualizerPreset.Loudness:    ten = new[] { 6, 4, 0, -2, -1, 0, 1, 3, 4, 4 }; break;
                default:                          return null;
            }
            return bandCount == 10 ? ten : CarryCurve(ten, bandCount);
        }

        // The curve that sounds like `from` in the other layout: what `from` plays at each of the target layout's
        // centres, rounded to the sliders' 1 dB steps. Used when switching layouts and for the 15-band presets.
        public static int[] CarryCurve(int[] from, int toBandCount)
        {
            var to = FrequenciesFor(toBandCount);
            var result = new int[to.Length];
            if (Array.TrueForAll(from, g => g == 0)) return result;
            for (int i = 0; i < to.Length; i++)
            {
                int db = (int)Math.Round(ResponseDb(from, to[i]), MidpointRounding.AwayFromZero);
                result[i] = Math.Max(-RangeDb, Math.Min(RangeDb, db));
            }
            return result;
        }

        private void Configure(int[] bands, int preamp)
        {
            var freqs = FrequenciesFor(bands.Length);
            var widths = BandWidthsOctaves(freqs);
            _gains = SolveGains(bands, _sampleRate);
            for (int b = 0; b < freqs.Length; b++)
                foreach (var channel in _filters)
                    channel[b].SetPeaking(_sampleRate, freqs[b], widths[b], _gains[b]);
            _appliedBands = (int[])bands.Clone();
            _appliedPreamp = preamp;
        }

        private const double Flat = 0.05;      // a filter this close to 0 dB is skipped
        private const double SolvedLimit = 36; // guard on how far a solved gain may go past the sliders

        // Neighbouring bands overlap and add up: Treble Boost's +4 on 12-16 kHz played +7 at 12 kHz with each filter
        // simply set to its slider. So the gains are solved together until the combined response meets every slider
        // at its centre (after Spotifast's eq.rs): each round measures the miss and corrects it through the matrix of
        // how much each band moves each centre per dB.
        internal static double[] SolveGains(int[] targets, int sampleRate = 44100)
        {
            var freqs = FrequenciesFor(targets.Length);
            int n = freqs.Length;
            var widths = BandWidthsOctaves(freqs);
            var gains = Array.ConvertAll(targets, t => (double)t);
            if (Array.TrueForAll(targets, t => t == 0)) return gains;

            var unit = new double[n, n];
            for (int j = 0; j < n; j++)
            {
                var f = new Biquad();
                f.SetPeaking(sampleRate, freqs[j], widths[j], 1.0);
                for (int i = 0; i < n; i++) unit[i, j] = f.GainDb(freqs[i], sampleRate);
            }

            for (int round = 0; round < 6; round++)
            {
                var residual = new double[n];
                bool met = true;
                for (int i = 0; i < n; i++)
                {
                    residual[i] = PlayedDb(gains, freqs, widths, freqs[i], sampleRate) - targets[i];
                    if (Math.Abs(residual[i]) >= 0.01) met = false;
                }
                if (met) break;

                var correction = Solve(unit, residual);
                for (int j = 0; j < n; j++)
                    gains[j] = Math.Max(-SolvedLimit, Math.Min(SolvedLimit, gains[j] - correction[j]));
            }
            return gains;
        }

        private static double PlayedDb(double[] gains, int[] freqs, double[] widths, double hz, int sampleRate)
        {
            double total = 0;
            for (int b = 0; b < gains.Length; b++)
            {
                if (Math.Abs(gains[b]) <= Flat) continue;
                var f = new Biquad();
                f.SetPeaking(sampleRate, freqs[b], widths[b], gains[b]);
                total += f.GainDb(hz, sampleRate);
            }
            return total;
        }

        // Gaussian elimination with partial pivoting: unit * x = rhs.
        private static double[] Solve(double[,] unit, double[] rhs)
        {
            int n = rhs.Length;
            var m = (double[,])unit.Clone();
            var r = (double[])rhs.Clone();
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col])) pivot = row;
                for (int k = 0; k < n; k++) { var t = m[col, k]; m[col, k] = m[pivot, k]; m[pivot, k] = t; }
                { var t = r[col]; r[col] = r[pivot]; r[pivot] = t; }
                for (int row = col + 1; row < n; row++)
                {
                    double factor = m[row, col] / m[col, col];
                    for (int k = col; k < n; k++) m[row, k] -= factor * m[col, k];
                    r[row] -= factor * r[col];
                }
            }
            var x = new double[n];
            for (int row = n - 1; row >= 0; row--)
            {
                double sum = r[row];
                for (int k = row + 1; k < n; k++) sum -= m[row, k] * x[k];
                x[row] = sum / m[row, row];
            }
            return x;
        }

        // Each band reaches halfway (in octaves) to its neighbours; the outer bands three octaves below the lowest
        // centre and one above the highest, as Winamp's do.
        internal static double[] BandWidthsOctaves(int[] freqs)
        {
            var octaves = Array.ConvertAll(freqs, f => Math.Log(f, 2));
            var widths = new double[freqs.Length];
            for (int i = 0; i < widths.Length; i++)
            {
                double below = i > 0 ? octaves[i - 1] : octaves[i] - 3.0;
                double above = i < widths.Length - 1 ? octaves[i + 1] : octaves[i] + 1.0;
                widths[i] = (octaves[i] - below) / 2.0 + (above - octaves[i]) / 2.0;
            }
            return widths;
        }

        // Combined gain of a curve at a frequency, in dB, as played (preamp excluded). For tests and tuning.
        internal static double ResponseDb(int[] bands, double hz, int sampleRate = 44100)
        {
            var freqs = FrequenciesFor(bands.Length);
            return PlayedDb(SolveGains(bands, sampleRate), freqs, BandWidthsOctaves(freqs), hz, sampleRate);
        }

        private static bool BandsEqual(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static float DbToLinear(int db) => (float)Math.Pow(10, db / 20.0);

        // Audio EQ Cookbook peaking filter, bandwidth in octaves between the half-gain points, digital form.
        private sealed class Biquad
        {
            private double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            public void SetPeaking(int sampleRate, double hz, double widthOctaves, double gainDb)
            {
                double a = Math.Pow(10, gainDb / 40.0);
                double w0 = 2 * Math.PI * hz / sampleRate;
                double sin = Math.Sin(w0), cos = Math.Cos(w0);
                double alpha = sin * Math.Sinh(Math.Log(2) / 2 * widthOctaves * w0 / sin);
                double a0 = 1 + alpha / a;
                _b0 = (1 + alpha * a) / a0;
                _b1 = -2 * cos / a0;
                _b2 = (1 - alpha * a) / a0;
                _a1 = -2 * cos / a0;
                _a2 = (1 - alpha / a) / a0;
            }

            public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

            public double Process(double x)
            {
                double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x;
                _y2 = _y1; _y1 = y;
                return y;
            }

            // Magnitude of the transfer function on the unit circle at `hz`, in dB.
            public double GainDb(double hz, int sampleRate)
            {
                double w = 2 * Math.PI * hz / sampleRate;
                double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
                double nr = _b0 + _b1 * c1 + _b2 * c2, ni = -(_b1 * s1 + _b2 * s2);
                double dr = 1 + _a1 * c1 + _a2 * c2, di = -(_a1 * s1 + _a2 * s2);
                return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
            }
        }
    }
}
