using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Playnite.SDK;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Playnite's own Fullscreen settings - Background Volume and Swap Confirm/Cancel - which the SDK does not expose,
    // so they are read through reflection (the pattern PlayniteSound proved). This knows Playnite's internals and
    // nothing about UPS: what UPS does with the values (the volume multiplier, the zero-volume notice, the confirm
    // button) lives in the plugin. If a Playnite update renames the private field, this is the one place that breaks.
    public sealed class PlayniteFullscreenSettings
    {
        private readonly IPlayniteAPI _api;
        private readonly FileLogger _fileLogger;
        private dynamic _settingsObj;
        private INotifyPropertyChanged _notifier; // kept for unsubscribe on dispose

        // Raised when Playnite's Background Volume changes (from either mode's settings).
        public event Action BackgroundVolumeChanged;

        public PlayniteFullscreenSettings(IPlayniteAPI api, FileLogger fileLogger)
        {
            _api = api;
            _fileLogger = fileLogger;
        }

        // Initializes the cached reference to Playnite's internal fullscreen settings object.
        public void Initialize()
        {
            try
            {
                _settingsObj = _api.ApplicationSettings.Fullscreen
                    .GetType()
                    .GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(_api.ApplicationSettings.Fullscreen);

                if (_settingsObj != null)
                {
                    _fileLogger?.Debug($"InitializeFullscreenSettingsRef: Got fullscreen settings, BackgroundVolume={BackgroundVolume:F2}");
                }
                else
                {
                    _fileLogger?.Warn("InitializeFullscreenSettingsRef: Could not get fullscreen settings object");
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"InitializeFullscreenSettingsRef: Failed - {ex.Message}");
            }
        }

        // Playnite's fullscreen BackgroundVolume, 0..1. Returns 1.0 if the settings object is unavailable or the read
        // fails (graceful degradation).
        public double BackgroundVolume
        {
            get
            {
                try
                {
                    if (_settingsObj == null)
                    {
                        return 1.0;
                    }

                    float bgVolume = (float)(_settingsObj.BackgroundVolume);
                    return Math.Max(0.0, Math.Min(1.0, bgVolume));
                }
                catch (Exception ex)
                {
                    _fileLogger?.Debug($"GetPlayniteFullscreenVolume: Failed to read BackgroundVolume - {ex.Message}");
                    return 1.0;
                }
            }
        }

        // True when the user enabled Swap Confirm/Cancel (B confirms). False on any failure.
        public bool SwapsConfirmCancel
        {
            get
            {
                try
                {
                    if (_settingsObj != null)
                    {
                        var prop = _settingsObj.GetType()
                            .GetProperty("SwapConfirmCancelButtons");
                        if (prop?.GetValue(_settingsObj) is bool swap && swap)
                            return true;
                    }
                }
                catch { }
                return false;
            }
        }

        // Subscribes to Playnite's fullscreen settings PropertyChanged to detect BackgroundVolume changes.
        // Uses the same pattern proven by PlayniteSound (ctx.AppSettings.Fullscreen as
        // INotifyPropertyChanged).
        // Without this subscription the cached volume is a frozen startup snapshot: a slider
        // raised from 0 never arrives, and the music stays silent for the rest of the session.
        //
        // Retried by the startup verify loop, from the window it was handed: Playnite raises
        // OnApplicationStarted from inside OpenView(), so arriving before that window exists is
        // ordinary. Runs in Desktop too - the value is edited there and a wrong write needs to be
        // visible wherever it happens.
        public void Subscribe(Window window)
        {
            if (_notifier != null) return;

            try
            {
                dynamic ctx = window?.DataContext;
                if (ctx == null) return;

                var fullscreenSettings = ctx.AppSettings?.Fullscreen as INotifyPropertyChanged;
                if (fullscreenSettings != null)
                {
                    fullscreenSettings.PropertyChanged += OnSettingsChanged;
                    _notifier = fullscreenSettings;
                    _fileLogger?.Debug("SubscribeToFullscreenVolumeChanges: Subscribed to fullscreen settings changes");
                }
                else
                {
                    _fileLogger?.Warn("SubscribeToFullscreenVolumeChanges: Could not cast fullscreen settings to INotifyPropertyChanged");
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Warn($"SubscribeToFullscreenVolumeChanges: Failed - {ex.Message}");
            }
        }

        // Unsubscribes from Playnite's fullscreen settings PropertyChanged: the held notifier, and the main window's
        // settings object as well. Safe to call more than once.
        public void Unsubscribe()
        {
            if (_notifier != null)
            {
                _notifier.PropertyChanged -= OnSettingsChanged;
                _notifier = null;
            }

            try
            {
                dynamic ctx = Application.Current?.MainWindow?.DataContext;
                if (ctx == null)
                    return;

                var fullscreenSettings = ctx.AppSettings?.Fullscreen as INotifyPropertyChanged;
                if (fullscreenSettings != null)
                {
                    fullscreenSettings.PropertyChanged -= OnSettingsChanged;
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"UnsubscribeFromFullscreenVolumeChanges: {ex.Message}");
            }
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "BackgroundVolume")
                BackgroundVolumeChanged?.Invoke();
        }
    }
}
