using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Playnite.SDK.Models;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Extracts the audio track from a game's EML VideoTrailer.mp4 and caches it per game
    // so UPS can play it as default music. Full trailers only (no micro-trailer fallback).
    // Every failure path returns null so the caller stays silent — never throws to the caller.
    public class TrailerAudioService : ITrailerAudioService
    {
        private readonly UniPlaySongSettings _settings;
        private readonly string _emlGamesPath;     // <Config>\ExtraMetadata\Games
        private readonly string _pluginDataPath;   // <Config>\ExtraMetadata\UniPlaySong
        private readonly FileLogger _fileLogger;

        // Once-per-session log guards so config errors don't spam the log on every game select.
        private bool _loggedNoFfmpeg;
        private bool _loggedNoEmlRoot;

        public TrailerAudioService(UniPlaySongSettings settings, string emlGamesPath, string pluginDataPath, FileLogger fileLogger = null)
        {
            _settings = settings;
            _emlGamesPath = emlGamesPath;
            _pluginDataPath = pluginDataPath;
            _fileLogger = fileLogger;
        }

        public string GetCachedPath(Game game)
        {
            if (game == null || string.IsNullOrEmpty(_pluginDataPath))
            {
                return null;
            }
            return Path.Combine(GetCacheDir(), game.Id.ToString() + Constants.TrailerAudioExtension);
        }

        // Pure path computation only — no I/O. The caller that actually writes a file
        // (the FFmpeg extraction task) is responsible for ensuring the directory exists.
        private string GetCacheDir()
        {
            return Path.Combine(_pluginDataPath, Constants.TrailerAudioCacheFolderName);
        }

        // <emlGamesPath>\{GameId}\VideoTrailer.mp4 if it exists, else null. Full trailer ONLY
        // (deliberately narrower than GameMusicFileService.HasTrailerVideo, which OR's micro).
        private string ResolveFullTrailer(Game game)
        {
            if (game == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(_emlGamesPath))
            {
                if (!_loggedNoEmlRoot)
                {
                    _fileLogger?.Debug("TrailerAudio: EML games root unresolved; trailer audio unavailable.");
                    _loggedNoEmlRoot = true;
                }
                return null;
            }

            var path = Path.Combine(_emlGamesPath, game.Id.ToString(), Constants.VideoTrailerFileName);
            return File.Exists(path) ? path : null;
        }

        public string GetOrExtractAudio(Game game)
        {
            var cached = GetCachedPath(game);
            if (cached == null)
            {
                return null; // null game / no plugin data path
            }
            // Hot path 1: the lossless .m4a copy from a prior run.
            var cachedInfo = new FileInfo(cached);
            if (cachedInfo.Exists && cachedInfo.Length > 0)
            {
                return cached; // hot path — no logging
            }
            // Corrupt/empty cache file: treat as miss and re-extract.
            if (cachedInfo.Exists)
            {
                _fileLogger?.Debug($"TrailerAudio: cached file for {game.Name} was empty; re-extracting.");
                try { File.Delete(cached); } catch { }
            }

            // Hot path 2: the .mp3 transcode fallback from a prior run (non-AAC trailers).
            // GetCachedPath only knows the .m4a path, so without this check a non-AAC game
            // would re-transcode on every play despite a valid cached .mp3 existing.
            var mp3Cached = Path.ChangeExtension(cached, Constants.DefaultAudioExtension);
            var mp3Info = new FileInfo(mp3Cached);
            if (mp3Info.Exists && mp3Info.Length > 0)
            {
                return mp3Cached; // hot path — no logging
            }
            if (mp3Info.Exists)
            {
                _fileLogger?.Debug($"TrailerAudio: cached transcode for {game.Name} was empty; re-extracting.");
                try { File.Delete(mp3Cached); } catch { }
            }

            var trailer = ResolveFullTrailer(game);
            if (trailer == null)
            {
                return null; // no full trailer — expected for most games, not logged
            }

            var ffmpeg = _settings?.FFmpegPath;
            if (!FFmpegHelper.IsAvailable(ffmpeg))
            {
                if (!_loggedNoFfmpeg)
                {
                    _fileLogger?.Info("TrailerAudio: FFmpeg not available — set its path in the Editing tab. Trailer audio disabled.");
                    _loggedNoFfmpeg = true;
                }
                return null;
            }

            var sw = Stopwatch.StartNew();
            // Fast path: copy the AAC stream into .m4a (lossless, near-instant).
            if (Extract(ffmpeg, trailer, cached, transcode: false))
            {
                _fileLogger?.Info($"TrailerAudio: extracted audio for {game.Name} in {sw.ElapsedMilliseconds} ms.");
                return cached;
            }
            // Fallback: transcode to .mp3 (rare non-AAC trailer audio).
            // mp3Cached already computed at the top of the method (hot-path 2 check).
            if (Extract(ffmpeg, trailer, mp3Cached, transcode: true))
            {
                _fileLogger?.Info($"TrailerAudio: extracted audio (transcoded) for {game.Name} in {sw.ElapsedMilliseconds} ms.");
                return mp3Cached;
            }

            _fileLogger?.Error($"TrailerAudio: extraction failed for {game.Name}; staying silent.");
            return null;
        }

        // Demux/transcode the trailer audio into outPath via a unique temp + atomic move.
        // transcode=false => -c:a copy into .m4a; transcode=true => re-encode into .mp3.
        // Returns true only if a non-empty output file landed at outPath.
        private bool Extract(string ffmpeg, string trailerMp4, string outPath, bool transcode)
        {
            // Unique temp so concurrent same-game extractions never read each other's partial file.
            // The temp MUST keep outPath's real extension last (.m4a/.mp3): FFmpeg selects the
            // output muxer from the filename extension, so a trailing ".tmp" makes it fail with
            // "Unable to choose an output format" (exit -22). Insert the GUID before the extension.
            var outDir = Path.GetDirectoryName(outPath);
            var outExt = Path.GetExtension(outPath); // includes the leading dot, e.g. ".m4a"
            var temp = Path.Combine(outDir, Path.GetFileNameWithoutExtension(outPath) + "." + Guid.NewGuid().ToString("N") + outExt);
            var args = transcode
                ? $"-y -i \"{trailerMp4}\" -vn \"{temp}\""
                : $"-y -i \"{trailerMp4}\" -vn -c:a copy \"{temp}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                Directory.CreateDirectory(outDir);

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.Start();

                    string stderr = null;
                    var readTask = System.Threading.Tasks.Task.Run(async () =>
                    {
                        var errTask = process.StandardError.ReadToEndAsync();
                        var outTask = process.StandardOutput.ReadToEndAsync();
                        if (!process.WaitForExit(60000)) // 1 minute — trailers are short
                        {
                            try { process.Kill(); } catch { }
                            throw new TimeoutException("FFmpeg trailer extraction timed out.");
                        }
                        stderr = await errTask.ConfigureAwait(false);
                        await outTask.ConfigureAwait(false);
                    });
                    readTask.GetAwaiter().GetResult();

                    if (process.ExitCode != 0)
                    {
                        _fileLogger?.Error($"TrailerAudio: FFmpeg exit {process.ExitCode}: {stderr}");
                        TryDelete(temp);
                        return false;
                    }
                }

                if (!File.Exists(temp) || new FileInfo(temp).Length == 0)
                {
                    TryDelete(temp);
                    return false;
                }

                // Atomic publish. Last writer wins on concurrent same-game extraction.
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                File.Move(temp, outPath);
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"TrailerAudio: extraction error: {ex.Message}", ex);
                TryDelete(temp);
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public (int filesDeleted, long bytesFreed) ClearCache()
        {
            int files = 0;
            long bytes = 0;
            try
            {
                var dir = Path.Combine(_pluginDataPath ?? string.Empty, Constants.TrailerAudioCacheFolderName);
                if (!Directory.Exists(dir))
                {
                    return (0, 0);
                }
                foreach (var f in Directory.GetFiles(dir))
                {
                    try
                    {
                        bytes += new FileInfo(f).Length;
                        File.Delete(f);
                        files++;
                    }
                    catch { /* skip locked file; report what we cleared */ }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"TrailerAudio: ClearCache failed: {ex.Message}", ex);
            }
            return (files, bytes);
        }
    }
}
