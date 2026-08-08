using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Builds the candidate song lists for the pool-based sources — the Default Music pool and the
    // Radio Mode pool. Lifted out of UniPlaySong.cs, where the two switch statements sat side by
    // side and repeated the same four lookups with one-word differences.
    //
    // The two pools resolve different settings but share their gathering strategies, so each strategy lives in one
    // place here. That matters for CompletionStatusPool in particular: it has to be handled in BOTH pools, and when
    // the cases were duplicated it was easy to add a source to one switch and forget the other.
    public class SongPoolProvider
    {
        private readonly IPlayniteAPI _api;
        // Resolved per call, not captured: the plugin rebuilds its GameMusicFileService when the
        // music path changes, so a captured reference would go stale.
        private readonly Func<GameMusicFileService> _fileService;
        private readonly FileLogger _fileLogger;

        public SongPoolProvider(IPlayniteAPI api, Func<GameMusicFileService> fileService, FileLogger fileLogger)
        {
            _api = api;
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _fileLogger = fileLogger;
        }

        // Default music pool — used when a game has no music of its own.
        public List<string> GetDefaultSongPool(DefaultMusicSource source, UniPlaySongSettings settings)
        {
            switch (source)
            {
                case DefaultMusicSource.CustomFolder:
                    return SongsInFolder(settings?.DefaultMusicFolderPath, logPrefix: null);

                case DefaultMusicSource.RandomGame:
                    return SongsFromAllGames();

                case DefaultMusicSource.CustomRotation:
                    return SongsFromGameIds(settings?.CustomRotationGameIds);

                case DefaultMusicSource.CompletionStatusPool:
                    return SongsFromCompletionStatuses(settings?.DefaultMusicStatusPoolIds);

                default:
                    return new List<string>();
            }
        }

        // Radio Mode pool — plays continuously across game selections.
        public List<string> GetRadioSongPool(RadioMusicSource source, UniPlaySongSettings settings)
        {
            switch (source)
            {
                case RadioMusicSource.FullLibrary:
                    return SongsFromAllGames();

                case RadioMusicSource.CustomFolder:
                    // Radio has its own folder, falling back to the Default Music folder when unset
                    // (v1.5.8 — preserves pre-decouple behavior for users who never picked one).
                    var folder = settings?.RadioCustomFolderPath;
                    if (string.IsNullOrWhiteSpace(folder))
                        folder = settings?.DefaultMusicFolderPath;
                    return SongsInFolder(folder, logPrefix: "RadioMode: ");

                case RadioMusicSource.CustomRotation:
                    return SongsFromGameIds(settings?.CustomRotationGameIds);

                case RadioMusicSource.CompletionStatusPool:
                    return SongsFromCompletionStatuses(settings?.DefaultMusicStatusPoolIds);

                default:
                    return new List<string>();
            }
        }

        private List<string> SongsInFolder(string folder, string logPrefix)
        {
            var songs = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return songs;

            try
            {
                songs = Directory.GetFiles(folder)
                    .Where(f => Constants.SupportedAudioExtensionsLowercase.Contains(Path.GetExtension(f)))
                    .ToList();
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"{logPrefix}Error scanning custom folder '{folder}': {ex.Message}");
            }

            return songs;
        }

        private List<string> SongsFromAllGames()
        {
            var songs = new List<string>();
            var games = _api?.Database?.Games;
            if (games == null) return songs;

            foreach (var game in games)
                AddSongsFor(game, songs);

            return songs;
        }

        private List<string> SongsFromGameIds(IEnumerable<Guid> gameIds)
        {
            var songs = new List<string>();
            if (gameIds == null || _api?.Database?.Games == null) return songs;

            foreach (var gameId in gameIds)
            {
                var game = _api.Database.Games[gameId];
                if (game != null)
                    AddSongsFor(game, songs);
            }

            return songs;
        }

        private List<string> SongsFromCompletionStatuses(ICollection<Guid> statusIds)
        {
            var songs = new List<string>();
            if (statusIds == null || statusIds.Count == 0 || _api?.Database?.Games == null) return songs;

            var wanted = new HashSet<Guid>(statusIds);
            foreach (var game in _api.Database.Games)
            {
                if (wanted.Contains(game.CompletionStatusId))
                    AddSongsFor(game, songs);
            }

            return songs;
        }

        private void AddSongsFor(Playnite.SDK.Models.Game game, List<string> songs)
        {
            var gameSongs = _fileService()?.GetAvailableSongs(game);
            if (gameSongs != null && gameSongs.Count > 0)
                songs.AddRange(gameSongs);
        }
    }
}
