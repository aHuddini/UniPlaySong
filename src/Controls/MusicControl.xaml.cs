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
    // Theme integration control: Tag="True" pauses music via ThemeOverlayActive
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
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_musicControls.Contains(this))
            {
                _musicControls.Add(this);
            }
            UpdateMute();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _musicControls.Remove(this);
            UpdateMute();
        }

        // If ANY control has Tag=True, ThemeOverlayActive=true (pause music)
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
                _settings.ThemeOverlayActive = mute;
            }
        }

        // Handles both bool and string "True"/"False" from XAML DataTriggers
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

        // Whether video/overlay is playing (mirrors ThemeOverlayActive for theme binding)
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

        // Whether a theme overlay is active (music should be paused)
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

        // ---- Theme-bindable user settings (v1.4.6+) ----
        // These mirror the most-requested fullscreen-toggleable UPS settings
        // so theme authors can wire CheckBox / ToggleButton IsChecked directly
        // to UPS's persistent settings via standard WPF two-way binding:
        //   IsChecked="{Binding ElementName=upsAudio, Path=EnableGameMusic, Mode=TwoWay}"
        //
        // Setter assignments go through UniPlaySongSettings's existing
        // PropertyChanged → settings-service → playback-coordinator pipeline,
        // so flipping a theme toggle has the same effect as toggling the
        // corresponding setting in the UPS desktop settings dialog.

        // Game music enable/disable. False = no game-specific music plays.
        // Default music still plays as a fallback when EnableDefaultMusic is
        // true — that's the longstanding UPS architectural choice and is
        // preserved unchanged. Toggle EnableDefaultMusic separately to silence
        // the fallback layer.
        //
        // The theme-side property is named "EnableGameMusic" for clarity even
        // though the underlying C# settings field is _settings.EnableMusic;
        // the field predates the EnableDefaultMusic split and the legacy name
        // is preserved on the settings type for backward-compat with existing
        // user config.ini files.
        public bool EnableGameMusic
        {
            get => _settings?.EnableMusic ?? true;
            set
            {
                if (_settings != null && _settings.EnableMusic != value)
                {
                    _settings.EnableMusic = value;
                    OnPropertyChanged();
                }
            }
        }

        // Default music enable/disable. False = no fallback ambient music
        // plays when a game has no music of its own. Pair with EnableGameMusic
        // to give themes independent control of both audio layers.
        public bool EnableDefaultMusic
        {
            get => _settings?.EnableDefaultMusic ?? true;
            set
            {
                if (_settings != null && _settings.EnableDefaultMusic != value)
                {
                    _settings.EnableDefaultMusic = value;
                    OnPropertyChanged();
                }
            }
        }

        // Radio Mode — pool-based continuous playback that auto-advances
        // through random games' songs.
        public bool RadioModeEnabled
        {
            get => _settings?.RadioModeEnabled ?? false;
            set
            {
                if (_settings != null && _settings.RadioModeEnabled != value)
                {
                    _settings.RadioModeEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        // Play music only when the user actively selects a game in the library
        // (suppresses ambient game music when browsing the list view).
        public bool PlayOnlyOnGameSelect
        {
            get => _settings?.PlayOnlyOnGameSelect ?? false;
            set
            {
                if (_settings != null && _settings.PlayOnlyOnGameSelect != value)
                {
                    _settings.PlayOnlyOnGameSelect = value;
                    OnPropertyChanged();
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
            else if (e.PropertyName == nameof(UniPlaySongSettings.EnableMusic))
            {
                OnPropertyChanged(nameof(EnableGameMusic));
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.EnableDefaultMusic))
            {
                OnPropertyChanged(nameof(EnableDefaultMusic));
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.RadioModeEnabled))
            {
                OnPropertyChanged(nameof(RadioModeEnabled));
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.PlayOnlyOnGameSelect))
            {
                OnPropertyChanged(nameof(PlayOnlyOnGameSelect));
            }
        }

        #region Static Initialization

        // Updates static settings reference when services are re-initialized
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
            else if (e.PropertyName == nameof(UniPlaySongSettings.EnableMusic))
            {
                foreach (var control in _musicControls)
                {
                    control.OnPropertyChanged(nameof(EnableGameMusic));
                }
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.EnableDefaultMusic))
            {
                foreach (var control in _musicControls)
                {
                    control.OnPropertyChanged(nameof(EnableDefaultMusic));
                }
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.RadioModeEnabled))
            {
                foreach (var control in _musicControls)
                {
                    control.OnPropertyChanged(nameof(RadioModeEnabled));
                }
            }
            else if (e.PropertyName == nameof(UniPlaySongSettings.PlayOnlyOnGameSelect))
            {
                foreach (var control in _musicControls)
                {
                    control.OnPropertyChanged(nameof(PlayOnlyOnGameSelect));
                }
            }
        }

        #endregion
    }
}
