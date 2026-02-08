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
        }

        #endregion
    }
}
