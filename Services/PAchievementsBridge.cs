using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Bridges PlayniteAchievements sidebar activity to UPS music playback.
    // Uses reflection to subscribe to PA's static SidebarActiveChanged event
    // without requiring a compile-time dependency on the PA assembly.
    internal class PAchievementsBridge : IDisposable
    {
        private static readonly Guid PA_PLUGIN_ID = Guid.Parse("e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b");
        private static readonly Random _random = new Random();

        private readonly IPlayniteAPI _playniteApi;
        private readonly MusicPlaybackCoordinator _coordinator;
        private readonly MusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly UniPlaySongSettings _settings;
        private readonly FileLogger _fileLogger;

        private EventInfo _activeEventInfo;
        private Delegate _activeHandler;
        private bool _isSubscribed;
        private bool _sidebarActive;

        public PAchievementsBridge(
            IPlayniteAPI playniteApi,
            MusicPlaybackCoordinator coordinator,
            MusicPlaybackService playbackService,
            GameMusicFileService fileService,
            UniPlaySongSettings settings,
            FileLogger fileLogger)
        {
            _playniteApi = playniteApi;
            _coordinator = coordinator;
            _playbackService = playbackService;
            _fileService = fileService;
            _settings = settings;
            _fileLogger = fileLogger;
        }

        // Call from OnApplicationStarted. Returns true if PA was found and subscribed.
        public bool TryConnect()
        {
            if (_isSubscribed) return true;

            try
            {
                var paPlugin = _playniteApi.Addons.Plugins
                    .FirstOrDefault(p => p.Id == PA_PLUGIN_ID);

                if (paPlugin == null)
                {
                    _fileLogger?.Debug("PAchievementsBridge: PA plugin not found");
                    return false;
                }

                var sidebarVmType = paPlugin.GetType().Assembly
                    .GetType("PlayniteAchievements.ViewModels.SidebarViewModel");

                if (sidebarVmType == null)
                {
                    _fileLogger?.Debug("PAchievementsBridge: SidebarViewModel type not found");
                    return false;
                }

                // Subscribe to SidebarActiveChanged (static event)
                _activeEventInfo = sidebarVmType.GetEvent("SidebarActiveChanged",
                    BindingFlags.Public | BindingFlags.Static);

                if (_activeEventInfo == null)
                {
                    _fileLogger?.Debug("PAchievementsBridge: SidebarActiveChanged event not found — PA may need the UPS-compatible build");
                    return false;
                }

                _activeHandler = new EventHandler<bool>(OnSidebarActiveChanged);
                _activeEventInfo.AddEventHandler(null, _activeHandler);
                _isSubscribed = true;

                _fileLogger?.Debug("PAchievementsBridge: Connected to PA sidebar events");
                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"PAchievementsBridge: Connection failed - {ex.Message}");
                return false;
            }
        }

        private void OnSidebarActiveChanged(object sender, bool isActive)
        {
            if (_settings?.EnablePASidebarMusic != true)
                return;

            // Respect the MusicState setting (Never/Desktop/Fullscreen/Always)
            if (_settings.EnableMusic != true || _settings.MusicVolume <= 0)
                return;

            var isFullscreen = _playniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen;
            var state = _settings.MusicState;
            if (isFullscreen && state != AudioState.Fullscreen && state != AudioState.Always)
                return;
            if (!isFullscreen && state != AudioState.Desktop && state != AudioState.Always)
                return;

            try
            {
                if (isActive)
                {
                    var musicPath = ResolveMusicPath();

                    if (string.IsNullOrEmpty(musicPath))
                    {
                        _fileLogger?.Debug("PAchievementsBridge: Sidebar active but no music source configured");
                        return;
                    }

                    _sidebarActive = true;

                    _fileLogger?.Debug($"PAchievementsBridge: Sidebar active — playing {System.IO.Path.GetFileName(musicPath)}");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _playbackService?.FadeToFile(musicPath);
                    });
                }
                else
                {
                    _fileLogger?.Debug("PAchievementsBridge: Sidebar deactivated — reverting to library game");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Clear sidebar flag AFTER revert so OnGameSelected stays blocked
                        // until the coordinator has processed the returning game
                        var currentGame = _playniteApi.MainView.SelectedGames?.FirstOrDefault();
                        _coordinator.HandleGameSelected(currentGame, isFullscreen);
                        _sidebarActive = false;
                    });
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"PAchievementsBridge: Error handling sidebar state - {ex.Message}");
            }
        }

        private string ResolveMusicPath()
        {
            switch (_settings.PASidebarMusicSource)
            {
                case 0: // Default Music Preset
                    return BundledPresetService.ResolvePresetPath(_settings.PASidebarPreset);

                case 1: // Specific Game
                    if (_settings.PASidebarGameId == Guid.Empty)
                        return null;

                    var game = _playniteApi.Database.Games.Get(_settings.PASidebarGameId);
                    if (game == null) return null;

                    var songs = _fileService?.GetAvailableSongs(game);
                    if (songs == null || songs.Count == 0) return null;

                    // Pick a random song from the game's library
                    return songs[_random.Next(songs.Count)];

                case 2: // Custom File
                    var path = _settings.PASidebarCustomPath;
                    if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                        return path;
                    return null;

                default:
                    return null;
            }
        }

        public bool IsSidebarActive => _sidebarActive;

        public void Dispose()
        {
            if (_isSubscribed && _activeEventInfo != null && _activeHandler != null)
            {
                try
                {
                    _activeEventInfo.RemoveEventHandler(null, _activeHandler);
                }
                catch { }
                _isSubscribed = false;
            }
        }
    }
}
