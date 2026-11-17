using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Manages game music status tags (adds/removes based on whether music is downloaded)
    public class GameMusicTagService
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
        private readonly IPlayniteAPI _playniteApi;
        private readonly GameMusicFileService _fileService;

        // Cached tag IDs to avoid repeated lookups
        private Guid? _hasMusicTagId;
        private Guid? _noMusicTagId;

        public GameMusicTagService(IPlayniteAPI playniteApi, GameMusicFileService fileService)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        }

        private Guid GetOrCreateTag(string tagName)
        {
            // Check if tag already exists
            var existingTag = _playniteApi.Database.Tags.FirstOrDefault(t => t.Name == tagName);
            if (existingTag != null)
            {
                return existingTag.Id;
            }

            // Create new tag
            var newTag = new Tag(tagName);
            _playniteApi.Database.Tags.Add(newTag);
            return newTag.Id;
        }

        // Re-resolved when the cached tag was deleted in Playnite, or games would get an ID no tag matches.
        private Guid GetHasMusicTagId()
        {
            if (!_hasMusicTagId.HasValue || _playniteApi.Database.Tags.Get(_hasMusicTagId.Value) == null)
            {
                _hasMusicTagId = GetOrCreateTag(Constants.TagHasMusic);
            }
            return _hasMusicTagId.Value;
        }

        private Guid GetNoMusicTagId()
        {
            if (!_noMusicTagId.HasValue || _playniteApi.Database.Tags.Get(_noMusicTagId.Value) == null)
            {
                _noMusicTagId = GetOrCreateTag(Constants.TagNoMusic);
            }
            return _noMusicTagId.Value;
        }

        // Updates the game's music status tag; false if no change was needed.
        public bool UpdateGameMusicTag(Game game)
        {
            if (game == null) return false;

            try
            {
                bool hasMusic = _fileService.HasMusic(game);

                // The write runs on the UI thread: Playnite's library views only refresh there, and
                // parallel batch downloads must not each create the tag.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                return dispatcher != null && !dispatcher.CheckAccess()
                    ? dispatcher.Invoke(() => ApplyMusicTag(game, hasMusic))
                    : ApplyMusicTag(game, hasMusic);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error updating music tag for game '{game?.Name}': {ex.Message}");
                return false;
            }
        }

        private bool ApplyMusicTag(Game game, bool hasMusic)
        {
            var tagToAdd = hasMusic ? GetHasMusicTagId() : GetNoMusicTagId();
            var tagToRemove = hasMusic ? GetNoMusicTagId() : GetHasMusicTagId();

            var tagIds = game.TagIds ?? new List<Guid>();
            if (tagIds.Contains(tagToAdd) && !tagIds.Contains(tagToRemove))
            {
                return false;
            }

            // Assign a new list: editing TagIds in place raises no change notification, so Playnite
            // saved the tag but kept showing the old one until restart.
            var updated = tagIds.Where(id => id != tagToRemove && id != tagToAdd).ToList();
            updated.Add(tagToAdd);
            game.TagIds = updated;
            _playniteApi.Database.Games.Update(game);
            return true;
        }

        public async Task<TagScanResult> ScanAndTagAllGamesAsync(
            IProgress<TagScanProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new TagScanResult();

            try
            {
                var allGames = _playniteApi.Database.Games.ToList();
                result.TotalGames = allGames.Count;

                await Task.Run(() =>
                {
                    int processed = 0;

                    foreach (var game in allGames)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        bool hasMusic = _fileService.HasMusic(game);
                        bool wasModified = UpdateGameMusicTag(game);

                        if (hasMusic)
                        {
                            result.GamesWithMusic++;
                        }
                        else
                        {
                            result.GamesWithoutMusic++;
                        }

                        if (wasModified)
                        {
                            result.GamesModified++;
                        }

                        processed++;

                        progress?.Report(new TagScanProgress
                        {
                            CurrentGame = game.Name,
                            ProcessedCount = processed,
                            TotalCount = result.TotalGames,
                            HasMusic = hasMusic
                        });
                    }
                }, cancellationToken);

                result.IsComplete = !cancellationToken.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                result.IsComplete = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error during tag scan: {ex.Message}");
                result.IsComplete = false;
            }

            return result;
        }

        // Removes all UniPlaySong music tags from all games (for cleanup/reset)
        public void RemoveAllMusicTags()
        {
            try
            {
                var hasMusicTag = _playniteApi.Database.Tags.FirstOrDefault(t => t.Name == Constants.TagHasMusic);
                var noMusicTag = _playniteApi.Database.Tags.FirstOrDefault(t => t.Name == Constants.TagNoMusic);

                int removed = 0;

                foreach (var game in _playniteApi.Database.Games)
                {
                    bool modified = false;

                    if (game.TagIds != null)
                    {
                        if (hasMusicTag != null && game.TagIds.Contains(hasMusicTag.Id))
                        {
                            game.TagIds.Remove(hasMusicTag.Id);
                            modified = true;
                        }
                        if (noMusicTag != null && game.TagIds.Contains(noMusicTag.Id))
                        {
                            game.TagIds.Remove(noMusicTag.Id);
                            modified = true;
                        }
                    }

                    if (modified)
                    {
                        _playniteApi.Database.Games.Update(game);
                        removed++;
                    }
                }

                // Optionally remove the tags themselves from the database
                if (hasMusicTag != null)
                {
                    _playniteApi.Database.Tags.Remove(hasMusicTag);
                }
                if (noMusicTag != null)
                {
                    _playniteApi.Database.Tags.Remove(noMusicTag);
                }

                // Clear cached IDs
                _hasMusicTagId = null;
                _noMusicTagId = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error removing music tags: {ex.Message}");
            }
        }
    }

    // Progress information for tag scanning
    public class TagScanProgress
    {
        public string CurrentGame { get; set; }
        public int ProcessedCount { get; set; }
        public int TotalCount { get; set; }
        public bool HasMusic { get; set; }
    }

    // Result of a tag scan operation
    public class TagScanResult
    {
        public int TotalGames { get; set; }
        public int GamesWithMusic { get; set; }
        public int GamesWithoutMusic { get; set; }
        public int GamesModified { get; set; }
        public bool IsComplete { get; set; }
    }
}
