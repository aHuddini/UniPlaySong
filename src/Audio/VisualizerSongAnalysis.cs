using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Audio
{
    // The Jukebox Fountain's song analysis, UniPlaySong's half. When the song being heard changes, it tells the renderer
    // where that song's analysis file is - a VisualAnalysis folder in the game's music folder, or one beside Games for
    // other music - and, when the file is missing or out of date, decodes the song to mono 48 kHz float for the renderer
    // to analyse into it. The analysing itself runs in the 64-bit renderer (native/visualizer/analysis.h), not here.
    // Emulated formats (they'd have to be played through to decode) and songs without a file stay live-only.
    internal sealed class VisualizerSongAnalysis : IDisposable
    {
        internal const uint FileMagic = 0x31415655u; // "UVA1", native/visualizer/analysis.h
        internal const int AnalysisVersion = 4;     // VIS_ANALYSIS_VERSION, native/visualizer/visualizer_shared.h
        private const int Rate = 48000;              // the rate the bench analyses at

        private readonly VisualizerAudioFeed _feed;
        private readonly FileLogger _log;
        private readonly string _otherMusicFolder;
        private readonly object _gate = new object();
        private readonly List<string> _decoded = new List<string>();   // temporary song files handed over
        private int _song;                 // the provider last handled (UI thread)
        private int _generation;           // per song; a job hands over only while its song is still the current one
        private CancellationTokenSource _cancel;
        private readonly Stopwatch _sinceSong = new Stopwatch();   // since the song heard changed: the debug log's timings

        internal long SinceSongMs => _sinceSong.ElapsedMilliseconds;

        // otherMusicFolder: where analyses of music outside the game folders go
        public VisualizerSongAnalysis(VisualizerAudioFeed feed, FileLogger log, string otherMusicFolder)
        {
            _feed = feed;
            _log = log;
            _otherMusicFolder = otherMusicFolder;
        }

        // Once per displayed frame, on the UI thread: a new song being heard gets its analysis.
        public void Tick()
        {
            var provider = VisualizationDataProvider.Current;
            if (provider == null || provider.SongId == _song) return;
            _song = provider.SongId;
            _sinceSong.Restart();
            _log?.Debug($"[Visualizer] Song {provider.SongId} heard: {Path.GetFileName(provider.SongPath)}");
            Start((uint)provider.SongId, provider.SongPath);
        }

        private void Start(uint song, string path)
        {
            int generation;
            CancellationToken token;
            lock (_gate)
            {
                generation = ++_generation;
                _cancel?.Cancel();
                _cancel = new CancellationTokenSource();
                token = _cancel.Token;
            }
            if (string.IsNullOrEmpty(path) || !Analysable(path)) { HandOver(generation, 0, null, null, 0, 0); return; }
            // off the UI thread from here: the file checks too (a network drive can stall)
            Task.Run(() =>
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) { HandOver(generation, 0, null, null, 0, 0); return; }
                    long bytes = info.Length, time = info.LastWriteTimeUtc.ToFileTimeUtc();
                    string file = AnalysisPath(path, _otherMusicFolder);
                    if (IsCurrent(file, bytes, time)) { HandOver(generation, song, file, null, bytes, time); return; }
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    RemoveOrphans(file);
                    string pcm = Decode(path, song, token);
                    lock (_gate) _decoded.Add(pcm);
                    if (!HandOver(generation, song, file, pcm, bytes, time)) TryDelete(pcm);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _log?.Warn($"[Visualizer] Song analysis skipped for {Path.GetFileName(path)}: {ex.Message}"); }
            }, token);
        }

        private bool HandOver(int generation, uint song, string file, string pcm, long bytes, long time)
        {
            lock (_gate)
            {
                if (generation != _generation) return false;
                _feed.PublishSong(song, file, pcm, bytes, time);
                _log?.Debug($"[Visualizer] Song {song} sent at +{SinceSongMs} ms: {(file == null ? "live only" : pcm == null ? "analysis on file" : "to analyse")}");
                return true;
            }
        }

        // Emulated formats would have to be played through to decode: those stay live-only.
        internal static bool Analysable(string path)
        {
            string ext = Path.GetExtension(path);
            return !GmeNative.IsGmeExtension(ext) && !PsfFile.IsPsfExtension(ext);
        }

        // A game's song: <its music folder>\VisualAnalysis\<song file>.vab. Other music (the defaults, a folder of the
        // user's own): the folder beside Games, under a name from the song's full path.
        internal static string AnalysisPath(string songPath, string otherMusicFolder)
        {
            string dir = Path.GetDirectoryName(songPath) ?? string.Empty;
            string parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? string.Empty);
            if (Guid.TryParse(Path.GetFileName(dir), out _) && parent.Equals(Constants.GamesFolderName, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(dir, Constants.VisualAnalysisFolderName, Path.GetFileName(songPath) + ".vab");
            string name;
            using (var sha = SHA1.Create())
                name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(songPath.ToLowerInvariant()))).Replace("-", string.Empty).ToLowerInvariant();
            return Path.Combine(otherMusicFolder, name + ".vab");
        }

        // The file is there, from this version, for the song file as it is now.
        internal static bool IsCurrent(string file, long sourceBytes, long sourceTime)
        {
            try
            {
                if (!File.Exists(file)) return false;
                using (var r = new BinaryReader(File.OpenRead(file)))
                    return r.BaseStream.Length >= 24 && r.ReadUInt32() == FileMagic && r.ReadUInt32() == AnalysisVersion
                        && r.ReadInt64() == sourceBytes && r.ReadInt64() == sourceTime;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // In a game's VisualAnalysis folder: analyses whose song is gone (deleted, renamed, trimmed to a new name).
        private static void RemoveOrphans(string file)
        {
            string dir = Path.GetDirectoryName(file);
            if (!Constants.VisualAnalysisFolderName.Equals(Path.GetFileName(dir), StringComparison.OrdinalIgnoreCase)) return;
            string songs = Path.GetDirectoryName(dir);
            if (!Guid.TryParse(Path.GetFileName(songs), out _)) return;
            foreach (var vab in Directory.GetFiles(dir, "*.vab"))
                if (!File.Exists(Path.Combine(songs, Path.GetFileNameWithoutExtension(vab)))) TryDelete(vab);
        }

        // The song as mono 48 kHz float, into a temporary file - the renderer reads it, analyses it and deletes it.
        private static string Decode(string path, uint song, CancellationToken token)
        {
            string dir = Path.Combine(Path.GetTempPath(), Constants.ExtensionFolderName);
            Directory.CreateDirectory(dir);
            string pcm = Path.Combine(dir, $"vis_{Process.GetCurrentProcess().Id}_{song}.f32");
            using (var reader = NAudioMusicPlayer.CreateAudioReader(path))
            using (var output = new FileStream(pcm, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                ISampleProvider source = (ISampleProvider)reader;
                if (source.WaveFormat.Channels > 2)
                {
                    var firstTwo = new MultiplexingSampleProvider(new[] { source }, 2);
                    firstTwo.ConnectInputToOutput(0, 0);
                    firstTwo.ConnectInputToOutput(1, 1);
                    source = firstTwo;
                }
                // the two channels' mean, as the bench mixes them
                if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f };
                if (source.WaveFormat.SampleRate != Rate) source = new WdlResamplingSampleProvider(source, Rate);
                var block = new float[Rate];
                var bytes = new byte[block.Length * sizeof(float)];
                int read;
                while ((read = source.Read(block, 0, block.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    Buffer.BlockCopy(block, 0, bytes, 0, read * sizeof(float));
                    output.Write(bytes, 0, read * sizeof(float));
                }
            }
            return pcm;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _generation++;
                _cancel?.Cancel();
                // the renderer deletes each song file once it has read it; any it never got to go now
                foreach (var pcm in _decoded) TryDelete(pcm);
                _decoded.Clear();
            }
        }
    }
}
