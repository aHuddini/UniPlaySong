using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;
using Playnite.SDK;
using Playnite.SDK.Controls;

namespace UniPlaySong.Controls
{
    /// <summary>
    /// Theme integration control for UniPlaySong.
    /// Follows the PlayniteSound pattern for maximum compatibility.
    ///
    /// Allows themes to pause/resume music by setting the Tag property.
    /// When Tag="True", sets ThemeOverlayActive=true which pauses music.
    /// When Tag="False", sets ThemeOverlayActive=false which resumes music.
    ///
    /// Uses ThemeOverlayActive instead of VideoIsPlaying to prevent conflicts
    /// with MediaElementsMonitor which also sets VideoIsPlaying.
    /// </summary>
    public partial class MusicControl : PluginUserControl, INotifyPropertyChanged
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static UniPlaySongSettings _settings;
        private static readonly List<MusicControl> _musicControls = new List<MusicControl>();

        static MusicControl()
        {
            TagProperty.OverrideMetadata(typeof(MusicControl), new FrameworkPropertyMetadata(null, OnTagChanged));
        }

        public MusicControl(UniPlaySongSettings settings)
        {
            ((IComponentConnector)this).InitializeComponent();
            DataContext = this;
            _settings = settings;

            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChanged;
            }

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            _musicControls.Add(this);

            Logger.Debug("[MusicControl] Instance created");
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_musicControls.Contains(this))
            {
                _musicControls.Add(this);
            }
            UpdateMute();
            Logger.Debug($"[MusicControl] Loaded (total instances: {_musicControls.Count})");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _musicControls.Remove(this);
            UpdateMute();
            Logger.Debug($"[MusicControl] Unloaded (total instances: {_musicControls.Count})");
        }

        /// <summary>
        /// Updates ThemeOverlayActive based on all MusicControl Tag values.
        /// If ANY control has Tag=True, ThemeOverlayActive=true (pause music).
        /// Only resumes when ALL controls have Tag=False.
        ///
        /// Uses ThemeOverlayActive instead of VideoIsPlaying to prevent conflicts
        /// with MediaElementsMonitor which also sets VideoIsPlaying.
        /// </summary>
        private static void UpdateMute()
        {
            // Check if any control has Tag=True (should mute/pause)
            // Handle both boolean and string Tag values from XAML
            bool mute = _musicControls.Count(c => ConvertTagToBool(c.Tag)) > 0;

            if (_settings == null)
            {
                Logger.Warn("[MusicControl] Settings is null, cannot update mute state");
                return;
            }

            if (_settings.ThemeOverlayActive != mute)
            {
                Logger.Debug($"[MusicControl] Setting ThemeOverlayActive={mute} (was {_settings.ThemeOverlayActive})");
                _settings.ThemeOverlayActive = mute;
            }
        }

        /// <summary>
        /// Converts Tag value to boolean, handling both bool and string types from XAML.
        /// XAML DataTriggers often set Tag as string "True"/"False" rather than boolean.
        /// </summary>
        private static bool ConvertTagToBool(object tag)
        {
            if (tag == null)
                return false;

            if (tag is bool boolValue)
                return boolValue;

            if (tag is string stringValue)
                return string.Equals(stringValue, "True", StringComparison.OrdinalIgnoreCase);

            // Fallback to standard conversion
            try
            {
                return Convert.ToBoolean(tag);
            }
            catch
            {
                return false;
            }
        }

        private static void OnTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MusicControl)
            {
                Logger.Debug($"[MusicControl] Tag changed: {e.OldValue} -> {e.NewValue}");
                UpdateMute();
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion

        #region Bindable Properties (PlayniteSound compatible)

        /// <summary>
        /// Whether video/overlay is playing (music should be paused).
        /// This mirrors the settings property for theme binding.
        /// Note: Internally uses ThemeOverlayActive to avoid conflicts with MediaElementsMonitor.
        /// </summary>
        public bool VideoIsPlaying
        {
            get => _settings?.ThemeOverlayActive ?? false;
            set
            {
                if (_settings != null)
                {
                    _settings.ThemeOverlayActive = value;
                }
            }
        }

        /// <summary>
        /// Whether a theme overlay is active (music should be paused).
        /// This is the actual property used internally.
        /// </summary>
        public bool ThemeOverlayActive
        {
            get => _settings?.ThemeOverlayActive ?? false;
            set
            {
                if (_settings != null)
                {
                    _settings.ThemeOverlayActive = value;
                }
            }
        }

        #endregion

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_settings.ThemeOverlayActive))
            {
                OnPropertyChanged(nameof(VideoIsPlaying));
                OnPropertyChanged(nameof(ThemeOverlayActive));
            }
        }

        #region Static Initialization

        /// <summary>
        /// Updates the static settings reference.
        /// Called when services are re-initialized.
        /// </summary>
        public static void UpdateServices(UniPlaySongSettings settings)
        {
            if (_settings != null)
            {
                _settings.PropertyChanged -= OnSettingsChangedStatic;
            }

            _settings = settings;

            if (_settings != null)
            {
                _settings.PropertyChanged += OnSettingsChangedStatic;
            }

            Logger.Debug("[MusicControl] Static services updated");
        }

        private static void OnSettingsChangedStatic(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.ThemeOverlayActive))
            {
                foreach (var control in _musicControls)
                {
                    control.OnPropertyChanged(nameof(VideoIsPlaying));
                    control.OnPropertyChanged(nameof(ThemeOverlayActive));
                }
            }
        }

        #endregion
    }
}
