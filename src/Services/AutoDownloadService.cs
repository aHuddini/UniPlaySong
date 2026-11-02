using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Downloaders;

namespace UniPlaySong.Services
{
    // Reacts to Playnite library updates: auto-downloads a song for newly added games and runs the auto-tag scan, each
    // when its setting is on. Also used for the download when a game is newly installed. Built once in InitializeServices.
    public class AutoDownloadService
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();

        private readonly IPlayniteAPI _api;
        private readonly FileLogger _fileLogger;
        private readonly SettingsService _settingsService;
        private readonly IDownloadManager _downloadManager;
        private readonly GameMusicFileService _fileService;
        private readonly INormalizationService _normalizationService;
        private readonly GameMusicTagService _tagService;
        private readonly Action _playDownloadCompleteSound;
        private readonly Action<UniPlaySongSettings> _saveSettings; // the plugin's SavePluginSettings

        // Read live: a settings save replaces the whole settings object.
        private UniPlaySongSettings _settings => _settingsService?.Current;

        public AutoDownloadService(IPlayniteAPI api, FileLogger fileLogger, SettingsService settingsService,
            IDownloadManager downloadManager, GameMusicFileService fileService, INormalizationService normalizationService,
            GameMusicTagService tagService, Action playDownloadCompleteSound, Action<UniPlaySongSettings> saveSettings)
        {
            _api = api;
            _fileLogger = fileLogger;
            _settingsService = settingsService;
            _downloadManager = downloadManager;
            _fileService = fileService;
            _normalizationService = normalizationService;
            _tagService = tagService;
            _playDownloadCompleteSound = playDownloadCompleteSound;
            _saveSettings = saveSettings;
        }

        // Handles library update events from Playnite. Triggers auto-download of music for newly added games
        // if enabled. Also triggers auto-tagging of games if enabled.
        public void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            try
            {
                _fileLogger?.Debug($"OnLibraryUpdated: Library updated event received");

                if (_settings == null)
                {
                    _fileLogger?.Warn("OnLibraryUpdated: Settings is null");
                    return;
                }

                // Handle auto-download
                if (_settings.AutoDownloadOnLibraryUpdate)
                {
                    // Get games added since last auto-download check
                    var lastCheck = _settings.LastAutoLibUpdateAssetsDownload;
                    _fileLogger?.Debug($"OnLibraryUpdated: Checking for games added after {lastCheck}");

                    var newGames = _api.Database.Games
                        .Where(g => g.Added.HasValue && g.Added.Value > lastCheck)
                        .ToList();

                    _fileLogger?.Debug($"OnLibraryUpdated: Found {newGames.Count} new game(s)");

                    // Start auto-download in background if we have new games
                    if (newGames.Count > 0)
                    {
                        Task.Run(() => AutoDownloadMusicForGamesAsync(newGames));
                    }

                    // Update timestamp AFTER identifying games (like PlayniteSound does).
                    // Guarded: never persist if settings didn't genuinely load from disk, or this
                    // save would overwrite the user's settings with defaults.
                    _settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
                    if (_settingsService?.SettingsLoadedFromDisk == true) _saveSettings(_settings);
                }
                else
                {
                    _fileLogger?.Debug("OnLibraryUpdated: Auto-download disabled");
                    // Still update timestamp to avoid processing old games later (same guard as above)
                    _settings.LastAutoLibUpdateAssetsDownload = DateTime.Now;
                    if (_settingsService?.SettingsLoadedFromDisk == true) _saveSettings(_settings);
                }

                // Handle auto-tagging (scans all games, not just new ones)
                if (_settings.AutoTagOnLibraryUpdate && _tagService != null)
                {
                    _fileLogger?.Debug("OnLibraryUpdated: Starting auto-tag scan in background");
                    Task.Run(async () =>
                    {
                        try
                        {
                            var result = await _tagService.ScanAndTagAllGamesAsync();
                            _fileLogger?.Debug($"OnLibraryUpdated: Auto-tag complete - {result.GamesWithMusic} with music, {result.GamesWithoutMusic} without, {result.GamesModified} updated");
                        }
                        catch (Exception ex)
                        {
                            _fileLogger?.Error($"OnLibraryUpdated: Auto-tag error - {ex.Message}", ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"OnLibraryUpdated: Error - {ex.Message}", ex);
                Logger.Error(ex, "Error in OnLibraryUpdated");
            }
        }

        // Automatically downloads music for a list of games. Uses BestAlbumPick and BestSongPick to select
        // the most relevant music.
        public async Task AutoDownloadMusicForGamesAsync(List<Game> games)
        {
            if (games == null || games.Count == 0)
                return;

            _fileLogger?.Debug($"AutoDownloadMusicForGamesAsync: Starting auto-download for {games.Count} game(s)");

            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;
            var failedGames = new List<string>(); // name the failures in the notification

            foreach (var game in games)
            {
                try
                {
                    // Check if game already has music
                    if (_fileService.HasMusic(game))
                    {
                        _fileLogger?.Debug($"AutoDownload: Skipping '{game.Name}' - already has music");
                        skipCount++;
                        continue;
                    }

                    var success = await AutoDownloadMusicForGameAsync(game);
                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        failedGames.Add(game.Name);
                    }

                    // Rate limiting: wait between downloads to avoid throttling
                    await Task.Delay(2000);
                }
                catch (Exception ex)
                {
                    _fileLogger?.Error($"AutoDownload: Error processing '{game.Name}' - {ex.Message}", ex);
                    failCount++;
                    failedGames.Add(game.Name);
                }
            }

            _fileLogger?.Debug($"AutoDownloadMusicForGamesAsync: Completed - Success: {successCount}, Skipped: {skipCount}, Failed: {failCount}");

            if (successCount > 0)
                _playDownloadCompleteSound();

            // Show notification with results. Failures name the affected games — a bare count
            // gives users nothing to act on — and use the Error type so they
            // stand out in Playnite's notification panel instead of blending in as Info.
            if (successCount > 0 || failCount > 0)
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    var message = $"Auto-download completed:\n" +
                                  $"• Downloaded: {successCount}\n" +
                                  $"• Skipped (already have music): {skipCount}\n" +
                                  $"• Failed: {failCount}";
                    if (failedGames.Count > 0)
                    {
                        const int maxNames = 8; // keep the toast readable on big library imports
                        var names = string.Join(", ", failedGames.Take(maxNames));
                        if (failedGames.Count > maxNames)
                            names += $" (+{failedGames.Count - maxNames} more)";
                        message += $"\n• Failed games: {names}\n" +
                                   "Retry via right-click → UniPlaySong → Download Music.";
                    }
                    _api.Notifications.Add(new NotificationMessage(
                        "UniPlaySong_AutoDownload",
                        message,
                        failCount > 0 ? NotificationType.Error : NotificationType.Info));
                }));
            }
        }

        // Automatically downloads music for a single game. Returns true if music was successfully downloaded.
        private async Task<bool> AutoDownloadMusicForGameAsync(Game game)
        {
            try
            {
                _fileLogger?.Debug($"AutoDownload: Processing '{game.Name}'");

                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                {
                    // Get albums for this game (try KHInsider first, fallback to YouTube)
                    var albums = _downloadManager.GetAlbumsForGame(game.Name, Models.Source.All, cts.Token, auto: true);
                    if (albums == null || !albums.Any())
                    {
                        _fileLogger?.Debug($"AutoDownload: No albums found for '{game.Name}'");
                        return false;
                    }

                    // Use BestAlbumPick to select the best album
                    var bestAlbum = _downloadManager.BestAlbumPick(albums, game);
                    if (bestAlbum == null)
                    {
                        _fileLogger?.Debug($"AutoDownload: No suitable album found for '{game.Name}'");
                        return false;
                    }

                    _fileLogger?.Debug($"AutoDownload: Selected album '{bestAlbum.Name}' for '{game.Name}'");

                    // Get songs from the album
                    var songs = _downloadManager.GetSongsFromAlbum(bestAlbum, cts.Token);
                    if (songs == null || !songs.Any())
                    {
                        _fileLogger?.Debug($"AutoDownload: No songs found in album '{bestAlbum.Name}'");
                        return false;
                    }

                    // Use BestSongPick to select the best song (only 1 song for auto-download)
                    var bestSongs = _downloadManager.BestSongPick(songs, game.Name, maxSongs: 1);
                    if (bestSongs == null || bestSongs.Count == 0)
                    {
                        _fileLogger?.Debug($"AutoDownload: No suitable song found for '{game.Name}'");
                        return false;
                    }

                    var bestSong = bestSongs.First();
                    _fileLogger?.Debug($"AutoDownload: Selected song '{bestSong.Name}' for '{game.Name}'");

                    // Ensure game music directory exists
                    var gameDir = _fileService.EnsureGameMusicDirectory(game);
                    if (string.IsNullOrWhiteSpace(gameDir))
                    {
                        _fileLogger?.Error($"AutoDownload: Failed to create music directory for '{game.Name}'");
                        return false;
                    }

                    // Generate safe filename
                    var safeFileName = Common.StringHelper.CleanForPath(bestSong.Name);
                    var extension = Path.GetExtension(bestSong.Id);
                    if (string.IsNullOrEmpty(extension))
                        extension = Constants.DefaultAudioExtension;
                    var downloadPath = Path.Combine(gameDir, safeFileName + extension);

                    // Download the song
                    var success = _downloadManager.DownloadSong(bestSong, downloadPath, cts.Token, isPreview: false);
                    if (success)
                    {
                        _fileLogger?.Debug($"AutoDownload: Successfully downloaded '{bestSong.Name}' for '{game.Name}'");

                        // Invalidate song cache since we added a new file
                        _fileService.InvalidateCacheForGame(game);

                        // Auto-normalize if enabled
                        if (_settings?.AutoNormalizeAfterDownload == true && _normalizationService != null)
                        {
                            try
                            {
                                // skipAlreadyNormalized: true is the class default these auto-download paths always used
                                var normSettings = Models.NormalizationSettings.From(_settings, _settings.FFmpegPath, skipAlreadyNormalized: true);
                                await _normalizationService.NormalizeFileAsync(
                                    downloadPath,
                                    normSettings,
                                    null,
                                    CancellationToken.None);
                                _fileLogger?.Debug($"AutoDownload: Normalized '{bestSong.Name}'");
                            }
                            catch (Exception ex)
                            {
                                _fileLogger?.Warn($"AutoDownload: Normalization failed for '{bestSong.Name}' - {ex.Message}");
                            }
                        }

                        return true;
                    }
                    else
                    {
                        _fileLogger?.Warn($"AutoDownload: Download failed for '{bestSong.Name}'");
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _fileLogger?.Warn($"AutoDownload: Timeout for '{game.Name}'");
                return false;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"AutoDownload: Error for '{game.Name}' - {ex.Message}", ex);
                return false;
            }
        }
    }
}
