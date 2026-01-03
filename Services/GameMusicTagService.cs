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
    /// <summary>
    /// Service for managing game music status tags.
    /// Adds/removes tags to games based on whether they have music downloaded.
    /// </summary>
    public class GameMusicTagService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
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

        /// <summary>
        /// Gets or creates a tag by name and returns its ID.
        /// </summary>
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
            Logger.Info($"Created new tag: {tagName} with ID: {newTag.Id}");
            return newTag.Id;
        }

        /// <summary>
        /// Gets the cached "Has Music" tag ID, creating the tag if needed.
        /// </summary>
        private Guid GetHasMusicTagId()
        {
            if (!_hasMusicTagId.HasValue)
            {
                _hasMusicTagId = GetOrCreateTag(Constants.TagHasMusic);
            }
            return _hasMusicTagId.Value;
        }

        /// <summary>
        /// Gets the cached "No Music" tag ID, creating the tag if needed.
        /// </summary>
        private Guid GetNoMusicTagId()
        {
            if (!_noMusicTagId.HasValue)
            {
                _noMusicTagId = GetOrCreateTag(Constants.TagNoMusic);
            }
            return _noMusicTagId.Value;
        }

        /// <summary>
        /// Updates the music status tag for a single game.
        /// </summary>
        /// <param name="game">The game to update</param>
        /// <returns>True if the game was modified, false if no change was needed</returns>
        public bool UpdateGameMusicTag(Game game)
        {
            if (game == null) return false;

            try
            {
                bool hasMusic = _fileService.HasMusic(game);
                var hasMusicTagId = GetHasMusicTagId();
                var noMusicTagId = GetNoMusicTagId();

                // Determine which tag to add and which to remove
                Guid tagToAdd = hasMusic ? hasMusicTagId : noMusicTagId;
                Guid tagToRemove = hasMusic ? noMusicTagId : hasMusicTagId;

                // Check current state
                bool hasTagToAdd = game.TagIds?.Contains(tagToAdd) ?? false;
                bool hasTagToRemove = game.TagIds?.Contains(tagToRemove) ?? false;

                // Skip if already in correct state
                if (hasTagToAdd && !hasTagToRemove)
                {
                    return false;
                }

                // Initialize TagIds if null
                if (game.TagIds == null)
                {
                    game.TagIds = new List<Guid>();
                }

                // Remove old tag if present
                if (hasTagToRemove)
                {
                    game.TagIds.Remove(tagToRemove);
                }

                // Add new tag if not present
                if (!hasTagToAdd)
                {
                    game.TagIds.Add(tagToAdd);
                }

                // Save changes
                _playniteApi.Database.Games.Update(game);
                Logger.Debug($"Updated tag for '{game.Name}': {(hasMusic ? "Has Music" : "No Music")}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error updating music tag for game '{game?.Name}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scans all games and updates their music status tags.
        /// </summary>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result containing counts of games processed</returns>
        public async Task<TagScanResult> ScanAndTagAllGamesAsync(
            IProgress<TagScanProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new TagScanResult();

            try
            {
                var allGames = _playniteApi.Database.Games.ToList();
                result.TotalGames = allGames.Count;

                Logger.Info($"Starting music tag scan for {result.TotalGames} games");

                // Ensure tags exist before scanning
                GetHasMusicTagId();
                GetNoMusicTagId();

                await Task.Run(() =>
                {
                    int processed = 0;

                    foreach (var game in allGames)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Logger.Info("Tag scan cancelled by user");
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
                Logger.Info($"Tag scan complete: {result.GamesWithMusic} with music, {result.GamesWithoutMusic} without music, {result.GamesModified} modified");
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Tag scan was cancelled");
                result.IsComplete = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error during tag scan: {ex.Message}");
                result.IsComplete = false;
            }

            return result;
        }

        /// <summary>
        /// Updates tags for a specific list of games (e.g., after download).
        /// </summary>
        /// <param name="games">Games to update</param>
        public void UpdateTagsForGames(IEnumerable<Game> games)
        {
            if (games == null) return;

            try
            {
                int updated = 0;
                foreach (var game in games)
                {
                    if (UpdateGameMusicTag(game))
                    {
                        updated++;
                    }
                }

                if (updated > 0)
                {
                    Logger.Info($"Updated music tags for {updated} game(s)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error updating tags for games: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes all UniPlaySong tags from all games.
        /// Useful for cleanup or reset.
        /// </summary>
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

                Logger.Info($"Removed music tags from {removed} game(s)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error removing music tags: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Progress information for tag scanning
    /// </summary>
    public class TagScanProgress
    {
        public string CurrentGame { get; set; }
        public int ProcessedCount { get; set; }
        public int TotalCount { get; set; }
        public bool HasMusic { get; set; }
    }

    /// <summary>
    /// Result of a tag scan operation
    /// </summary>
    public class TagScanResult
    {
        public int TotalGames { get; set; }
        public int GamesWithMusic { get; set; }
        public int GamesWithoutMusic { get; set; }
        public int GamesModified { get; set; }
        public bool IsComplete { get; set; }
    }
}
