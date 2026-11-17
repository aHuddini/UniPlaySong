using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Menus
{
    // Builds the game right-click menu (single and multi-game, Desktop and Fullscreen/controller sections). Only reads
    // plugin state; the actions it wires go to the dialog handlers. Built once, after InitializeMenuHandlers.
    internal class GameMenuBuilder
    {
        private readonly FileLogger _fileLogger;
        private readonly GameMusicFileService _fileService;
        private readonly GameMenuHandler _gameMenuHandler;
        private readonly Handlers.ControllerDialogHandler _controllerDialogHandler;
        private readonly Features.MusicInfoCard.MusicInfoCardHandler _musicInfoCardHandler;
        private readonly Handlers.NormalizationDialogHandler _normalizationDialogHandler;
        private readonly Handlers.TrimDialogHandler _trimDialogHandler;
        private readonly Handlers.WaveformTrimDialogHandler _waveformTrimDialogHandler;
        private readonly Handlers.AmplifyDialogHandler _amplifyDialogHandler;
        private readonly Handlers.NsfTrackManagerHandler _nsfTrackManagerHandler;
        private readonly Handlers.HesSplitHandler _hesSplitHandler;
        private readonly Func<bool> _isFullscreen;
        private readonly Func<List<(string Label, Action Invoke)>> _getSpotifyMenuActions; // shared with the main menu
        private readonly Func<UniPlaySongSettings> _getSettings; // read per build: a save swaps the settings object

        public GameMenuBuilder(
            FileLogger fileLogger,
            GameMusicFileService fileService,
            GameMenuHandler gameMenuHandler,
            Handlers.ControllerDialogHandler controllerDialogHandler,
            Features.MusicInfoCard.MusicInfoCardHandler musicInfoCardHandler,
            Handlers.NormalizationDialogHandler normalizationDialogHandler,
            Handlers.TrimDialogHandler trimDialogHandler,
            Handlers.WaveformTrimDialogHandler waveformTrimDialogHandler,
            Handlers.AmplifyDialogHandler amplifyDialogHandler,
            Handlers.NsfTrackManagerHandler nsfTrackManagerHandler,
            Handlers.HesSplitHandler hesSplitHandler,
            Func<bool> isFullscreen,
            Func<List<(string Label, Action Invoke)>> getSpotifyMenuActions,
            Func<UniPlaySongSettings> getSettings)
        {
            _getSettings = getSettings;
            _fileLogger = fileLogger;
            _fileService = fileService;
            _gameMenuHandler = gameMenuHandler;
            _controllerDialogHandler = controllerDialogHandler;
            _musicInfoCardHandler = musicInfoCardHandler;
            _normalizationDialogHandler = normalizationDialogHandler;
            _trimDialogHandler = trimDialogHandler;
            _waveformTrimDialogHandler = waveformTrimDialogHandler;
            _amplifyDialogHandler = amplifyDialogHandler;
            _nsfTrackManagerHandler = nsfTrackManagerHandler;
            _hesSplitHandler = hesSplitHandler;
            _isFullscreen = isFullscreen;
            _getSpotifyMenuActions = getSpotifyMenuActions;
        }

        public IEnumerable<GameMenuItem> Build(GetGameMenuItemsArgs args)
        {
            if (args.Games.Count == 0)
                return Enumerable.Empty<GameMenuItem>();

            var items = new List<GameMenuItem>();
            var games = args.Games.ToList();

            // Which entries the user keeps (Settings -> UI). Playnite rebuilds this menu on every open, so a change
            // applies on the next right-click.
            var ui = _getSettings?.Invoke() ?? new UniPlaySongSettings();
            bool fs = _isFullscreen();
            bool bulk = fs ? ui.FullscreenMenuBulkActions : ui.DesktopMenuBulkActions;
            bool infoLine = fs ? ui.FullscreenMenuInfoLine : ui.DesktopMenuInfoLine;
            bool musicInfoCard = fs ? ui.FullscreenMenuMusicInfoCard : ui.DesktopMenuMusicInfoCard;
            bool spotify = fs ? ui.FullscreenMenuSpotify : ui.DesktopMenuSpotify;
            void AddIf(bool show, GameMenuItem item) { if (show) items.Add(item); }

            // Debug: Log how many games Playnite is passing to us
            _fileLogger?.Debug($"[Menu] GetGameMenuItems called with {games.Count} game(s)");

            // For single game: get songs early so we can show count in menu header
            List<string> songs = null;
            if (games.Count == 1)
                songs = _fileService?.GetAvailableSongs(games[0]) ?? new List<string>();

            // Dynamic menu header: "UniPlaySong (3 songs)" for single game with music
            string menuSection = (songs != null && songs.Count > 0)
                ? $"{Constants.MenuSectionName} ({songs.Count} {(songs.Count == 1 ? "song" : "songs")})"
                : Constants.MenuSectionName;

            // Multi-game selection: Show bulk operations
            if (games.Count > 1)
            {
                // Download All - uses Source.All (tries KHInsider first, falls back to YouTube)
                AddIf(bulk, new GameMenuItem
                {
                    Description = $"Download All ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGames(games, Models.Source.All)
                });

                // Normalize All - bulk normalize selected games
                AddIf(bulk, new GameMenuItem
                {
                    Description = $"Normalize All ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _normalizationDialogHandler.NormalizeSelectedGames(games)
                });

                // Trim All - bulk trim selected games
                AddIf(bulk, new GameMenuItem
                {
                    Description = $"Trim All ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _trimDialogHandler.TrimSelectedGames(games)
                });

                // Delete Music (All) - bulk delete music for selected games
                AddIf(bulk, new GameMenuItem
                {
                    Description = $"Delete Music (All) ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DeleteAllMusicForGames(games)
                });

                // Export M3U Playlist - combined playlist for selected games
                AddIf(bulk, new GameMenuItem
                {
                    Description = $"Export M3U Playlist ({games.Count} games)",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.ExportPlaylistForGames(games)
                });

                // Add retry option if there are failed downloads
                var failedCount = _gameMenuHandler.FailedDownloads.Count(fd => !fd.Resolved);
                if (failedCount > 0)
                {
                    items.Add(new GameMenuItem
                    {
                        Description = "-",
                        MenuSection = menuSection
                    });
                    items.Add(new GameMenuItem
                    {
                        Description = $"Retry Failed Downloads ({failedCount})",
                        MenuSection = menuSection,
                        Action = _ => _gameMenuHandler.RetryFailedDownloads()
                    });
                }

                return TidySeparators(items);
            }

            // Single game selection
            var game = games[0];

            // Info line: show folder size when game has music
            if (songs.Count > 0)
            {
                long folderBytes = _fileService.GetMusicFolderSize(game);
                AddIf(infoLine, new GameMenuItem
                {
                    Description = $"[ {songs.Count} {(songs.Count == 1 ? "song" : "songs")} | {FormatFileSize(folderBytes)} ]",
                    MenuSection = menuSection
                });

                // Music Info Card — opens a per-game stats dialog.
                // Lives at the parent menu level (above the divider) so it sits next to the count/size info line as a "see
                // more" affordance. Picks Desktop vs Fullscreen flavor internally. No emoji prefix — Playnite Desktop's
                // right-click menu can silently drop items whose Description starts with chars it can't render through its font
                // fallback chain; Fullscreen controller dialogs render emojis fine because they use a different UI surface.
                AddIf(musicInfoCard, new GameMenuItem
                {
                    Description = "Music Info Card",
                    MenuSection = menuSection,
                    Action = _ => _musicInfoCardHandler.Show(game)
                });

                // Spotify transport commands — only when a controllable Spotify session is present.
                // Nested in a dedicated "Spotify" submenu under the UniPlaySong section (Playnite uses
                // '|' as the submenu separator), placed right below Music Info Card. One source of
                // truth via GetSpotifyMenuActions.
                var spotifySection = menuSection + "|Spotify";
                foreach (var (label, action) in _getSpotifyMenuActions())
                {
                    AddIf(spotify, new GameMenuItem
                    {
                        Description = label,
                        MenuSection = spotifySection,
                        Action = _ => action()
                    });
                }

                items.Add(new GameMenuItem
                {
                    Description = "-",
                    MenuSection = menuSection
                });
            }

            if (_isFullscreen())
            {
                // === FULLSCREEN MODE: Organized with submenus for controller navigation ===
                var primarySongSection = $"{menuSection}|Primary Song";
                var audioProcessingSection = $"{menuSection}|Audio Processing";
                var manageMusicSection = $"{menuSection}|Manage Music";
                var pcModeSection = $"{menuSection}|🖥️ PC Mode";

                // Download Music (🎮 Mode) - at parent level for quick access
                AddIf(ui.FullscreenMenuDownloadMusic, new GameMenuItem
                {
                    Description = "🎮 Download Music",
                    MenuSection = menuSection,
                    Action = _ => _controllerDialogHandler.ShowDownloadDialog(game)
                });

                // === Primary Song Submenu (🎮 Mode options) ===
                AddIf(ui.FullscreenMenuPrimarySong, new GameMenuItem
                {
                    Description = "🎮 Set Primary Song",
                    MenuSection = primarySongSection,
                    Action = _ => _controllerDialogHandler.ShowSetPrimarySong(game)
                });

                AddIf(ui.FullscreenMenuPrimarySong, new GameMenuItem
                {
                    Description = "🎮 Clear Primary Song",
                    MenuSection = primarySongSection,
                    Action = _ => _controllerDialogHandler.ClearPrimarySong(game)
                });

                // === Audio Processing Submenu ===
                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "🎮 Normalize Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _controllerDialogHandler.ShowNormalizeIndividualSong(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Normalize Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _normalizationDialogHandler?.NormalizeSelectedGamesFullscreen(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "🎮 Silence Trim - Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _controllerDialogHandler.ShowTrimIndividualSong(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Silence Trim - Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _trimDialogHandler?.TrimSelectedGamesFullscreen(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "🎮 Precise Trim",
                    MenuSection = audioProcessingSection,
                    Action = _ => _waveformTrimDialogHandler.ShowControllerPreciseTrimDialog(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "🎮 Amplify Audio",
                    MenuSection = audioProcessingSection,
                    Action = _ => _amplifyDialogHandler.ShowControllerAmplifyDialog(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "🎮 Repair Audio File",
                    MenuSection = audioProcessingSection,
                    Action = _ => _controllerDialogHandler.ShowRepairIndividualSong(game)
                });

                AddIf(ui.FullscreenMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Repair Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _gameMenuHandler.RepairAllAudioFiles(game)
                });

                // === Manage Music Submenu (🎮 Mode options) ===
                AddIf(ui.FullscreenMenuManageMusic, new GameMenuItem
                {
                    Description = "🎮 Add Music File",
                    MenuSection = manageMusicSection,
                    Action = _ => _controllerDialogHandler.ShowAddMusicFile(game)
                });

                AddIf(ui.FullscreenMenuManageMusic, new GameMenuItem
                {
                    Description = "🎮 Add Music Folder",
                    MenuSection = manageMusicSection,
                    Action = _ => _controllerDialogHandler.ShowAddMusicFolder(game)
                });

                AddIf(ui.FullscreenMenuManageMusic, new GameMenuItem
                {
                    Description = "🎮 Delete Songs",
                    MenuSection = manageMusicSection,
                    Action = _ => _controllerDialogHandler.ShowDeleteSongs(game)
                });

                AddIf(ui.FullscreenMenuManageMusic, new GameMenuItem
                {
                    Description = "Open Music Folder",
                    MenuSection = manageMusicSection,
                    Action = _ => _gameMenuHandler.OpenMusicFolder(game)
                });

                // === PC Mode Submenu (Desktop dialogs for keyboard/mouse users) ===
                AddIf(ui.FullscreenMenuPcMode, new GameMenuItem
                {
                    Description = "🖥️ Download Music",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGame(game)
                });

                AddIf(ui.FullscreenMenuPcMode, new GameMenuItem
                {
                    Description = "🖥️ Download From URL",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.DownloadFromUrl(game)
                });

                AddIf(ui.FullscreenMenuPcMode, new GameMenuItem
                {
                    Description = "🖥️ Set Primary Song",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.SetPrimarySong(game)
                });

                AddIf(ui.FullscreenMenuPcMode, new GameMenuItem
                {
                    Description = "🖥️Clear Primary Song",
                    MenuSection = pcModeSection,
                    Action = _ => _gameMenuHandler.ClearPrimarySong(game)
                });
            }
            else
            {
                // === DESKTOP MODE: Organized with submenus ===
                var audioProcessingSection = $"{menuSection}|Audio Processing";
                var audioEditingSection = $"{menuSection}|Audio Editing";
                var controllerModeSection = $"{menuSection}|🎮 Controller Mode";

                // Download Music - Desktop download dialog with source selection
                AddIf(ui.DesktopMenuDownloadMusic, new GameMenuItem
                {
                    Description = "Download Music",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DownloadMusicForGame(game)
                });

                // Download From URL - Download audio from a specific YouTube URL
                AddIf(ui.DesktopMenuDownloadFromUrl, new GameMenuItem
                {
                    Description = "Download From URL",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.DownloadFromUrl(game)
                });

                // Add Music File - Browse local files to add to game
                AddIf(ui.DesktopMenuAddMusicFile, new GameMenuItem
                {
                    Description = "Add Music File",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.AddMusicFile(game)
                });

                // Add Music Folder - copy every playable file in a folder (plus PSF libraries / HES sidecars)
                AddIf(ui.DesktopMenuAddMusicFolder, new GameMenuItem
                {
                    Description = "Add Music Folder",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.AddMusicFolder(game)
                });

                // Separator before Primary Song section
                items.Add(new GameMenuItem
                {
                    Description = "-",
                    MenuSection = menuSection
                });

                // Set Primary Song - Desktop file picker
                AddIf(ui.DesktopMenuPrimarySong, new GameMenuItem
                {
                    Description = "Set Primary Song",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.SetPrimarySong(game)
                });

                // Clear Primary Song - Desktop
                AddIf(ui.DesktopMenuPrimarySong, new GameMenuItem
                {
                    Description = "Clear Primary Song",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.ClearPrimarySong(game)
                });

                // === Audio Processing Submenu (Normalize and Silence Trim options) ===
                AddIf(ui.DesktopMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Normalize Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _gameMenuHandler.ShowNormalizeIndividualSong(game)
                });

                AddIf(ui.DesktopMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Normalize Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _normalizationDialogHandler.NormalizeSelectedGames(new List<Game> { game })
                });

                AddIf(ui.DesktopMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Silence Trim - Single Song",
                    MenuSection = audioProcessingSection,
                    Action = _ => _gameMenuHandler.ShowTrimIndividualSong(game)
                });

                AddIf(ui.DesktopMenuAudioProcessing, new GameMenuItem
                {
                    Description = "Silence Trim - Music Folder",
                    MenuSection = audioProcessingSection,
                    Action = _ => _trimDialogHandler.TrimSelectedGames(new List<Game> { game })
                });

                // === Audio Editing Submenu (Precise Trim, Amplify, and Repair options) ===
                AddIf(ui.DesktopMenuAudioEditing, new GameMenuItem
                {
                    Description = "Precise Trim",
                    MenuSection = audioEditingSection,
                    Action = _ => _waveformTrimDialogHandler.ShowPreciseTrimDialog(game)
                });

                AddIf(ui.DesktopMenuAudioEditing, new GameMenuItem
                {
                    Description = "Amplify Audio",
                    MenuSection = audioEditingSection,
                    Action = _ => _amplifyDialogHandler.ShowAmplifyDialog(game)
                });

                AddIf(ui.DesktopMenuAudioEditing, new GameMenuItem
                {
                    Description = "Repair Audio File",
                    MenuSection = audioEditingSection,
                    Action = _ => _gameMenuHandler.ShowRepairAudioFile(game)
                });

                AddIf(ui.DesktopMenuAudioEditing, new GameMenuItem
                {
                    Description = "Repair Music Folder",
                    MenuSection = audioEditingSection,
                    Action = _ => _gameMenuHandler.RepairAllAudioFiles(game)
                });

                // === Chiptunes Submenu (NSF Track Manager + HES Splitter) ===
                var chiptunesSection = $"{menuSection}|Chiptunes";
                bool hasNsf = songs != null && songs.Any(s => s.EndsWith(".nsf", StringComparison.OrdinalIgnoreCase));
                if (hasNsf)
                {
                    AddIf(ui.DesktopMenuChiptunes, new GameMenuItem
                    {
                        Description = "NSF Management",
                        MenuSection = chiptunesSection,
                        Action = _ => _nsfTrackManagerHandler.ShowForGame(game)
                    });
                }

                // HES splitter: only visible when at least one .hes file in the
                // game's music folder has a sibling .m3u with 2+ tracks. Cheap
                // discovery — file enumeration + small sidecar parse.
                if (_hesSplitHandler != null && _hesSplitHandler.FindSplittable(game).Count > 0)
                {
                    AddIf(ui.DesktopMenuChiptunes, new GameMenuItem
                    {
                        Description = "Split HES Tracks",
                        MenuSection = chiptunesSection,
                        Action = _ => _hesSplitHandler.RunForGame(game)
                    });
                }

                // Separator before utility options
                items.Add(new GameMenuItem
                {
                    Description = "-",
                    MenuSection = menuSection
                });

                // Open Music Folder
                AddIf(ui.DesktopMenuOpenMusicFolder, new GameMenuItem
                {
                    Description = "Open Music Folder",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.OpenMusicFolder(game)
                });

                // Export Song List — deferred to future release
                // items.Add(new GameMenuItem
                // {
                //     Description = "Export Song List",
                //     MenuSection = menuSection,
                //     Action = _ => _gameMenuHandler.ExportSongList(game)
                // });

                // Export M3U Playlist
                AddIf(ui.DesktopMenuExportPlaylist, new GameMenuItem
                {
                    Description = "Export M3U Playlist",
                    MenuSection = menuSection,
                    Action = _ => _gameMenuHandler.ExportPlaylist(game)
                });

                // === Controller Mode Submenu (Controller-friendly dialogs) ===
                AddIf(ui.DesktopMenuControllerMode, new GameMenuItem
                {
                    Description = "🎮 Download Music",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ShowDownloadDialog(game)
                });

                AddIf(ui.DesktopMenuControllerMode, new GameMenuItem
                {
                    Description = "🎮 Set Primary Song",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ShowSetPrimarySong(game)
                });

                AddIf(ui.DesktopMenuControllerMode, new GameMenuItem
                {
                    Description = "🎮 Clear Primary Song",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ClearPrimarySong(game)
                });

                AddIf(ui.DesktopMenuControllerMode, new GameMenuItem
                {
                    Description = "🎮 Delete Songs",
                    MenuSection = controllerModeSection,
                    Action = _ => _controllerDialogHandler.ShowDeleteSongs(game)
                });

                AddIf(ui.DesktopMenuControllerMode, new GameMenuItem
                {
                    Description = "🎮 Precise Trim",
                    MenuSection = controllerModeSection,
                    Action = _ => _waveformTrimDialogHandler.ShowControllerPreciseTrimDialog(game)
                });
            }

            return TidySeparators(items);
        }

        // Hidden entries can leave a separator first, last or doubled within its submenu; drop those.
        internal static List<GameMenuItem> TidySeparators(List<GameMenuItem> items)
        {
            var kept = new List<GameMenuItem>(items.Count);
            var lastInSection = new Dictionary<string, GameMenuItem>();
            foreach (var item in items)
            {
                var section = item.MenuSection ?? string.Empty;
                lastInSection.TryGetValue(section, out var previous);
                if (item.Description == "-" && (previous == null || previous.Description == "-")) continue;
                kept.Add(item);
                lastInSection[section] = item;
            }
            foreach (var last in lastInSection.Values)
                if (last.Description == "-") kept.Remove(last);
            return kept;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1_073_741_824)
                return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)
                return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} B";
        }
    }
}
