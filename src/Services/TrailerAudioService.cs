using System;
using System.IO;
using System.Linq;
using Playnite.SDK.Models;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Extracts the audio track from a game's EML VideoTrailer.mp4 and caches it per game
    // so UPS can play it as default music. Full trailers only (no micro-trailer fallback).
    // Every failure path returns null so the caller stays silent — never throws to the caller.
    public class TrailerAudioService : ITrailerAudioService
    {
        private const string CacheFolderName = "TrailerAudioCache";

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
            return Path.Combine(GetCacheDir(), game.Id.ToString() + ".m4a");
        }

        private string GetCacheDir()
        {
            var dir = Path.Combine(_pluginDataPath, CacheFolderName);
            Directory.CreateDirectory(dir);
            return dir;
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

        // Implemented fully in Task 3.
        public string GetOrExtractAudio(Game game)
        {
            var cached = GetCachedPath(game);
            if (cached != null && File.Exists(cached) && new FileInfo(cached).Length > 0)
            {
                return cached;
            }
            return null;
        }

        public (int filesDeleted, long bytesFreed) ClearCache()
        {
            int files = 0;
            long bytes = 0;
            try
            {
                var dir = Path.Combine(_pluginDataPath ?? string.Empty, CacheFolderName);
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
                _fileLogger?.Error($"TrailerAudio: ClearCache failed: {ex.Message}");
            }
            return (files, bytes);
        }
    }
}
