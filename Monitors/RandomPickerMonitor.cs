using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Monitors
{
    // Monitors Playnite's Random Game Picker dialog and plays music for each displayed game
    public class RandomPickerMonitor
    {
        private static readonly Random _random = new Random();
        private static IMusicPlaybackService _playbackService;
        private static UniPlaySongSettings _settings;
        private static FileLogger _fileLogger;

        // True while the picker dialog is open and this monitor is handling playback
        public static bool IsActive => _hookedViewModel != null;

        private static bool _classHandlerRegistered;
        private static INotifyPropertyChanged _hookedViewModel;
        private static PropertyInfo _selectedGameProperty;
        private static PropertyInfo _selectedActionProperty;
        private static Game _gameBeforePicker;

        public static void Attach(IMusicPlaybackService playbackService, UniPlaySongSettings settings, FileLogger fileLogger = null)
        {
            _playbackService = playbackService;
            _settings = settings;
            _fileLogger = fileLogger;

            // RegisterClassHandler is permanent (no unregister API) — only call once
            if (!_classHandlerRegistered)
            {
                EventManager.RegisterClassHandler(
                    typeof(Window),
                    Window.LoadedEvent,
                    new RoutedEventHandler(OnWindowLoaded));
                _classHandlerRegistered = true;
            }
        }

        public static void Detach()
        {
            UnhookViewModel();
            _playbackService = null;
            _settings = null;
            _fileLogger = null;
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!(sender is Window window)) return;
            if (_settings?.EnableRandomPickerMusic != true) return;
            if (_hookedViewModel != null) return;

            // DataContext may not be set yet when Loaded fires (Playnite sets it via CreateAndOpenDialog)
            if (TryHookViewModel(window)) return;

            // Fallback: wait for DataContext to be assigned
            window.DataContextChanged += OnWindowDataContextChanged;
        }

        private static void OnWindowDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is Window window)) return;

            window.DataContextChanged -= OnWindowDataContextChanged;

            if (_settings?.EnableRandomPickerMusic != true) return;
            if (_hookedViewModel != null) return;

            TryHookViewModel(window);
        }

        private static bool TryHookViewModel(Window window)
        {
            var dc = window.DataContext;
            if (dc == null || dc.GetType().Name != "RandomGameSelectViewModel") return false;
            if (!(dc is INotifyPropertyChanged inpc)) return false;

            _selectedGameProperty = dc.GetType().GetProperty("SelectedGame");
            _selectedActionProperty = dc.GetType().GetProperty("SelectedAction");
            if (_selectedGameProperty == null) return false;

            _fileLogger?.Debug("RandomPickerMonitor: Hooked into Random Game Picker dialog");

            _gameBeforePicker = _playbackService?.CurrentGame;
            _hookedViewModel = inpc;
            _hookedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            window.Closed += OnPickerWindowClosed;

            PlayPickerGame();
            return true;
        }

        private static void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "SelectedGame")
                PlayPickerGame();
        }

        private static void PlayPickerGame()
        {
            if (_selectedGameProperty == null || _hookedViewModel == null) return;

            var game = _selectedGameProperty.GetValue(_hookedViewModel) as Game;
            if (game == null) return;

            _fileLogger?.Debug($"RandomPickerMonitor: Playing music for '{game.Name}'");

            if (_playbackService == null) return;

            // Instant switch via PlayPreview (no fader) to avoid volume wobble from
            // overlapping fade-out/fade-in cycles during rapid browsing.
            // Deferred via BeginInvoke so the picker UI updates before playback starts.
            var songs = _playbackService.GetAvailableSongs(game);
            if (songs.Count > 0)
            {
                var song = songs[_random.Next(songs.Count)];
                var volume = _settings.MusicVolume / 100.0;
                _fileLogger?.Debug($"RandomPickerMonitor: Playing '{System.IO.Path.GetFileName(song)}'");
                Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    // Guard: skip if picker was closed before this deferred action ran
                    if (!IsActive) return;
                    _playbackService?.PlayPreview(song, volume);
                }));
                return;
            }

            // No game-specific songs — fall through to PlayGameMusic for default music fallback
            _playbackService.PlayGameMusic(game, _settings, true);
        }

        private static void OnPickerWindowClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
                window.Closed -= OnPickerWindowClosed;

            // Check if user committed (Play/Navigate) or cancelled
            var action = _selectedActionProperty?.GetValue(_hookedViewModel)?.ToString();
            _fileLogger?.Debug($"RandomPickerMonitor: Picker closed with action={action}");

            var shouldRestore = (action == "None");
            UnhookViewModel();

            if (shouldRestore && _gameBeforePicker != null && _playbackService != null)
            {
                _fileLogger?.Debug($"RandomPickerMonitor: Restoring music for '{_gameBeforePicker.Name}'");
                _playbackService.PlayGameMusic(_gameBeforePicker, _settings, false);
            }
            _gameBeforePicker = null;
        }

        private static void UnhookViewModel()
        {
            if (_hookedViewModel != null)
            {
                _hookedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _hookedViewModel = null;
            }
            _selectedGameProperty = null;
            _selectedActionProperty = null;
        }
    }
}
