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
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_instances.Contains(this))
            {
                // Sole registration site. Registering here (not the constructor) keeps _instances
                // limited to controls actually in the visual tree — a constructed-but-never-loaded
                // or torn-down control must not contribute to the override. OnUnloaded removes it.
                _instances.Add(this);
            }

            // Recompute + re-assert the override AFTER WPF has applied this control's Tag.
            // The control's Tag mirrors its ancestor's Tag via a FindAncestor binding, and
            // that binding only settles during/after the Loaded pass — so reading Tag
            // synchronously here can see a STALE value. This bites two theme scenarios where
            // the control sits in a login-gated, collapsible host (it is reused, not rebuilt,
            // on logout→login, so its Tag keeps the pre-logout value until the binding
            // re-evaluates):
            //   - first login: Playnite already force-selected the first game and played its
            //     music before the control was in the tree;
            //   - logout→login: e.g. log out on a game (game music), log back in at the
            //     Welcome Hub (Tag should be True → default music) — stale Tag would keep
            //     game music.
            // Deferring to DispatcherPriority.Loaded lets the Tag binding settle first, then
            // UpdateOverride() reads the fresh Tag and we re-assert the resulting state
            // unconditionally (both game→default and default→game) so the theme's current
            // intent always wins over whatever was playing before.
            Application.Current?.Dispatcher?.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() =>
                {
                    try
                    {
                        // If UpdateOverride() changed the flag, the settings PropertyChanged
                        // already re-triggered playback (HandleForceDefaultMusicOverrideChange).
                        // Only re-assert explicitly when it did NOT change — the edge-triggered
                        // notification is swallowed in that case (e.g. a reused control instance
                        // on logout→login whose flag was already at the target value), so we must
                        // apply the current override state ourselves. This avoids a double-play.
                        bool changed = UpdateOverride();
                        if (!changed
                            && Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                        {
                            var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                            plugin?.GetCoordinator()?.HandleForceDefaultMusicOverrideLoaded();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[MusicControlPauseGamePlayDefault] Deferred override re-assert on load failed: {ex.Message}");
                    }
                }));
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
        // Returns true if the override flag value changed (which fires PropertyChanged →
        // HandleForceDefaultMusicOverrideChange → PlayGameMusic on its own). Returns false
        // if the value was unchanged (caller may need to re-assert playback explicitly,
        // since no PropertyChanged fires in that case).
        private static bool UpdateOverride()
        {
            bool active = _instances.Count(c => ConvertTagToBool(c.Tag)) > 0;

            if (_settings == null)
            {
                Logger.Warn("[MusicControlPauseGamePlayDefault] Settings is null, cannot update override state");
                return false;
            }

            if (_settings.ForceDefaultMusicOverride != active)
            {
                _settings.ForceDefaultMusicOverride = active;
                return true;
            }
            return false;
        }

        // Recompute the override flag from the live, currently-loaded controls — without
        // clearing the registry. Called once per launch (OnApplicationStarted).
        //
        // This is the leak guard: ForceDefaultMusicOverride is [JsonIgnore] (starts false), but
        // its writer (_settings) is static, so a stale true could otherwise carry across a
        // theme/mode reload into a theme that has NO override control (e.g. PS5-Experience →
        // Aniki), suppressing all game music. Recomputing here from _instances forces the flag
        // back to the truth for the theme we actually started in: no control loaded → false
        // (game music plays); a control present in the current theme → its Tag wins.
        //
        // Why recompute instead of _instances.Clear() + force-false: in a Fullscreen-with-theme
        // launch the override control's OnLoaded runs and sets its Tag BEFORE OnApplicationStarted
        // fires (~400ms earlier in practice). Clearing here would deregister that live control and
        // clobber its legitimate Tag=true at the Welcome Hub, so the theme's "play default music"
        // intent was lost. Statics are fresh per process (mode/theme switch = restart), so there
        // is no prior-process instance to clear anyway — only the current theme's live controls.
        public static void SyncOverrideFromLiveControls()
        {
            UpdateOverride();
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
