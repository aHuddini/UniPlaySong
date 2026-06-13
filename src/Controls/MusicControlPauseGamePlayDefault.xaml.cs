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
    // v1.5.3 theme-integration sibling of MusicControl. Tag=True on any active
    // instance flips ForceDefaultMusicOverride=true, which makes PlayGameMusic
    // skip the current game's own songs and fall through to the default-music
    // branch. Tag=False reverts to game music. Multiple instances stack via OR
    // (if ANY instance has Tag=True, the override is active) — same pattern as
    // MusicControl's UpdateMute().
    public partial class MusicControlPauseGamePlayDefault : PluginUserControl, INotifyPropertyChanged
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static UniPlaySongSettings _settings;
        private static readonly List<MusicControlPauseGamePlayDefault> _instances = new List<MusicControlPauseGamePlayDefault>();

        static MusicControlPauseGamePlayDefault()
        {
            TagProperty.OverrideMetadata(typeof(MusicControlPauseGamePlayDefault), new FrameworkPropertyMetadata(null, OnTagChanged));
        }

        public MusicControlPauseGamePlayDefault(UniPlaySongSettings settings)
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

            _instances.Add(this);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_instances.Contains(this))
            {
                _instances.Add(this);
            }
            UpdateOverride();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe per-instance settings handler to prevent leak when WPF rebuilds the tree
            if (_settings != null)
            {
                _settings.PropertyChanged -= OnSettingsChanged;
            }
            _instances.Remove(this);
            UpdateOverride();
        }

        // If ANY active instance has Tag=True, ForceDefaultMusicOverride=true.
        private static void UpdateOverride()
        {
            bool active = _instances.Count(c => ConvertTagToBool(c.Tag)) > 0;

            if (_settings == null)
            {
                Logger.Warn("[MusicControlPauseGamePlayDefault] Settings is null, cannot update override state");
                return;
            }

            if (_settings.ForceDefaultMusicOverride != active)
            {
                _settings.ForceDefaultMusicOverride = active;
            }
        }

        private static bool ConvertTagToBool(object tag)
        {
            if (tag == null) return false;
            if (tag is bool boolValue) return boolValue;
            if (tag is string stringValue) return string.Equals(stringValue, "True", StringComparison.OrdinalIgnoreCase);
            try { return Convert.ToBoolean(tag); }
            catch { return false; }
        }

        private static void OnTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MusicControlPauseGamePlayDefault)
            {
                UpdateOverride();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Bindable property mirroring ForceDefaultMusicOverride for theme XAML
        public bool ForceDefaultMusicOverride
        {
            get => _settings?.ForceDefaultMusicOverride ?? false;
            set
            {
                if (_settings != null)
                {
                    _settings.ForceDefaultMusicOverride = value;
                }
            }
        }

        private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_settings.ForceDefaultMusicOverride))
            {
                OnPropertyChanged(nameof(ForceDefaultMusicOverride));
            }
        }

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
            if (e.PropertyName == nameof(UniPlaySongSettings.ForceDefaultMusicOverride))
            {
                foreach (var instance in _instances)
                {
                    instance.OnPropertyChanged(nameof(ForceDefaultMusicOverride));
                }
            }
        }
    }
}
