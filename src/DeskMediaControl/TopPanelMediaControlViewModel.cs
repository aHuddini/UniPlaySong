using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using UniPlaySong.Common;
using UniPlaySong.Models;
using UniPlaySong.Services;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.DeskMediaControl
{
    // Desktop top panel media controls ViewModel (play/pause, skip).
    public class TopPanelMediaControlViewModel
    {
        private readonly Func<IMusicPlaybackService> _getPlaybackService;
        private readonly Func<UniPlaySongSettings> _getSettings;
        private readonly Func<Game> _getCurrentGame;
        private readonly Action<string> _log;
        private readonly Action<Exception, string> _handleError;
        private readonly Func<SpotifyControlService> _getSpotifyService;

        // Applies a settings change the way the Fullscreen quick menu does: clone, mutate, push
        // through SettingsService so the diff event fires, and persist. Toggling CalmDownModeEnabled
        // can force an SDL2 -> NAudio backend swap, which only the diff event triggers.
        private readonly Action<Action<UniPlaySongSettings>> _updateSettings;

        private TopPanelItem _playPauseItem;
        private TopPanelItem _skipItem;
        private TopPanelItem _nowPlayingItem;
        private TopPanelItem _spectrumItem;
        private TopPanelItem _peakMeterItem;
        private TopPanelItem _progressItem;
        private TopPanelItem _calmDownItem;
        private TextBlock _playPauseIcon;
        private TextBlock _skipIcon;
        private TextBlock _calmDownIcon;

        // Matches the skip button's dimming, so an inactive control reads the same way across the panel.
        private const double CalmDownOffOpacity = 0.3;
        private const double CalmDownOnOpacity = 1.0;
        private NowPlayingPanel _nowPlayingPanel;
        private SpectrumVisualizerControl _spectrumVisualizer;
        private PeakMeterControl _peakMeter;
        private SongProgressBar _progressBar;
        private SongMetadataService _metadataService;

        public TopPanelItem PlayPauseItem => _playPauseItem;
        public TopPanelItem SkipItem => _skipItem;
        public TopPanelItem NowPlayingItem => _nowPlayingItem;

        public IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            var settings = _getSettings?.Invoke();
            var progressPosition = settings?.ProgressBarPosition ?? ProgressBarPosition.AfterSkipButton;

            // Every item is always yielded and shown or hidden through Visible. Playnite builds the panel from what this
            // returns at startup, so an item yielded conditionally could not be turned on again without a restart;
            // toggling Visible on an item that is always returned works live. ApplyVisibility (one rule, Decide) sets
            // them here and on every refresh.
            ApplyVisibility(settings, _getPlaybackService?.Invoke());

            yield return _playPauseItem;
            yield return _skipItem;
            if (_calmDownItem != null)
                yield return _calmDownItem;

            // Progress bar — position depends on setting (BelowNowPlaying is embedded, not a separate item)
            if (_progressItem != null && progressPosition == ProgressBarPosition.AfterSkipButton)
                yield return _progressItem;

            if (_spectrumItem != null)
                yield return _spectrumItem;

            if (_peakMeterItem != null)
                yield return _peakMeterItem;

            if (_progressItem != null && progressPosition == ProgressBarPosition.AfterVisualizer)
                yield return _progressItem;

            if (_nowPlayingItem != null)
                yield return _nowPlayingItem;

            if (_progressItem != null && progressPosition == ProgressBarPosition.AfterNowPlaying)
                yield return _progressItem;
        }

        public TopPanelMediaControlViewModel(
            Func<IMusicPlaybackService> getPlaybackService,
            Func<UniPlaySongSettings> getSettings,
            Func<Game> getCurrentGame,
            Action<string> log = null,
            Action<Exception, string> handleError = null,
            Func<SpotifyControlService> getSpotifyService = null,
            Action<Action<UniPlaySongSettings>> updateSettings = null)
        {
            _getPlaybackService = getPlaybackService ?? throw new ArgumentNullException(nameof(getPlaybackService));
            _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
            _getCurrentGame = getCurrentGame ?? throw new ArgumentNullException(nameof(getCurrentGame));
            _log = log;
            _handleError = handleError;
            _getSpotifyService = getSpotifyService;
            _updateSettings = updateSettings;

            InitializeTopPanelItems();
            InitializeSpectrumVisualizer();
            InitializePeakMeter();
            InitializeProgressBar();
            InitializeNowPlayingPanel();
            SubscribeToEvents(_getPlaybackService());
        }

        private void InitializeTopPanelItems()
        {
            var icoFont = Playnite.SDK.ResourceProvider.GetResource("FontIcoFont") as FontFamily;

            // Play/Pause button — 18pt, no bold, theme-adaptive margins
            _playPauseIcon = new TextBlock
            {
                Text = MediaControlIcons.Play,
                FontSize = 18,
                FontFamily = icoFont,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _playPauseItem = new TopPanelItem
            {
                Icon = _playPauseIcon,
                Title = "UniPlaySong: Play Music",
                Visible = true,
                Activated = OnPlayPauseActivated
            };

            // Skip/Next button — greyed out until 2+ songs available
            _skipIcon = new TextBlock
            {
                Text = MediaControlIcons.Next,
                FontSize = 18,
                FontFamily = icoFont,
                Opacity = 0.3,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _skipItem = new TopPanelItem
            {
                Icon = _skipIcon,
                Title = "UniPlaySong: Skip to Next Song (No additional songs)",
                Visible = true,
                Activated = OnSkipActivated
            };

            // Calm Down Mode — a moon that dims the music (low-pass + volume drop) rather than
            // stopping it. Fullscreen has had this on its quick menu since 1.5.0; on Desktop it was
            // buried in the settings dialog, which is the wrong place for something you reach for
            // because the music is too much right now.
            //
            // Dimmed when off, full opacity when on: the glyph is the same either way, so opacity
            // is what says whether it is engaged.
            _calmDownIcon = new TextBlock
            {
                Text = MediaControlIcons.Moon,
                FontSize = 18,
                FontFamily = icoFont,
                Opacity = CalmDownOffOpacity,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _calmDownItem = new TopPanelItem
            {
                Icon = _calmDownIcon,
                Title = "UniPlaySong: Calm Down Mode",
                Visible = false,   // set from the setting on every refresh
                Activated = OnCalmDownActivated
            };

            // Reduce the gap between play/pause and skip so they look grouped.
            // Trims play/pause's right margin and skip's left margin symmetrically,
            // leaving all other theme styling (MinWidth, Padding, Background) intact.
            _playPauseIcon.Loaded += (s, ev) => AdjustTopPanelItemMargin(_playPauseIcon, trimRight: true);
            _skipIcon.Loaded += (s, ev) => AdjustTopPanelItemMargin(_skipIcon, trimLeft: true);
        }

        // Finds the TopPanelItem container for an icon and trims one side of its margin
        // to group adjacent buttons closer together.
        private void AdjustTopPanelItemMargin(FrameworkElement icon, bool trimLeft = false, bool trimRight = false)
        {
            try
            {
                DependencyObject current = icon;
                for (int i = 0; i < 10; i++)
                {
                    current = VisualTreeHelper.GetParent(current);
                    if (current == null) break;

                    var fe = current as FrameworkElement;
                    if (fe != null && fe.GetType().Name == "TopPanelItem")
                    {
                        var m = fe.Margin;
                        double left = trimLeft ? Math.Max(1, Math.Round(m.Left * 0.2)) : m.Left;
                        double right = trimRight ? Math.Max(1, Math.Round(m.Right * 0.2)) : m.Right;
                        fe.Margin = new Thickness(left, m.Top, right, m.Bottom);
                        break;
                    }

                    if (current.GetType().Name == "StackPanel")
                        break;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Error adjusting margin: {ex.Message}");
            }
        }

        private void InitializeSpectrumVisualizer()
        {
            try
            {
                _spectrumVisualizer = new SpectrumVisualizerControl();
                _spectrumVisualizer.SetSettingsProvider(_getSettings);
                _spectrumItem = new TopPanelItem
                {
                    Icon = _spectrumVisualizer,
                    Title = "UniPlaySong: Spectrum Visualizer",
                    Visible = true
                };
                _log?.Invoke("TopPanel: Spectrum visualizer initialized");
            }
            catch (System.Exception ex)
            {
                _log?.Invoke($"TopPanel: Error initializing spectrum visualizer: {ex.Message}");
            }
        }

        private void InitializePeakMeter()
        {
            try
            {
                _peakMeter = new PeakMeterControl();
                _peakMeter.SetSettingsProvider(_getSettings);
                _peakMeterItem = new TopPanelItem
                {
                    Icon = _peakMeter,
                    Title = "UniPlaySong: Peak Meter",
                    Visible = true
                };
                _log?.Invoke("TopPanel: Peak meter initialized");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Error initializing peak meter: {ex.Message}");
            }
        }

        private void InitializeProgressBar()
        {
            try
            {
                var settings = _getSettings?.Invoke();
                if (settings?.ShowProgressBar != true) return;

                // BelowNowPlaying mode: embedded in NowPlayingPanel, not a separate TopPanelItem
                if (settings.ProgressBarPosition == ProgressBarPosition.BelowNowPlaying) return;

                _progressBar = new SongProgressBar(_getPlaybackService);
                _progressItem = new TopPanelItem
                {
                    Icon = _progressBar,
                    Title = "UniPlaySong: Song Progress",
                    Visible = true
                };
                _log?.Invoke("TopPanel: Progress bar initialized");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Error initializing progress bar: {ex.Message}");
            }
        }

        private const string NowPlayingIdleTitle = "UniPlaySong: Now Playing (click to open music folder)";

        // The song the panel shows changed: the tooltip and an open card follow it; an emptied panel closes the card.
        private void OnNowPlayingSongShown(SongInfo song)
        {
            _shownSong = song;
            UpdateNowPlayingTooltip();
            if (song == null) _card?.HideCard();
            _cardHost?.RaiseChanged();
        }

        // The tooltip names the whole song: the panel shows only what fits, or a moving slice of it. With the card on,
        // no tooltip: the card shows all of it and the two would stack.
        private void UpdateNowPlayingTooltip()
        {
            if (_nowPlayingItem == null) return;
            var song = _shownSong;
            string title;
            if (CardEnabled) title = null;
            else if (song == null || string.IsNullOrEmpty(song.Title)) title = NowPlayingIdleTitle;
            else
            {
                var lines = string.IsNullOrEmpty(song.Artist) ? song.Title : song.Title + "\n" + song.Artist;
                title = lines + "\n\nClick to open the music folder";
            }
            if (_nowPlayingItem.Title != title) _nowPlayingItem.Title = title;
        }

        // ---- Now Playing card: hover opens it after a moment, leaving the panel and the card closes it ----

        private NowPlayingCardHost _cardHost;
        private NowPlayingCard _card;
        private SongInfo _shownSong;
        private DispatcherTimer _cardOpenTimer, _cardCloseTimer;
        private static readonly TimeSpan CardOpenDelay = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan CardCloseDelay = TimeSpan.FromMilliseconds(350);

        private bool CardEnabled => _cardHost != null && _getSettings?.Invoke()?.ShowNowPlayingCard != false;

        // Called once by the plugin with what the card may read and do. Open folder stays this view model's.
        internal void AttachCard(NowPlayingCardHost host)
        {
            _cardHost = host;
            host.OpenFolder = OpenMusicFolder;
            host.CanOpenFolder = () => File.Exists(_getPlaybackService?.Invoke()?.CurrentSongPath ?? string.Empty);
            RecentTracks.RadioUpNext.Items.CollectionChanged += (s, e) => host.RaiseChanged();

            // Normal priority: at Background a busy Playnite (just after a game is selected) held them back a second
            _cardOpenTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = CardOpenDelay };
            _cardOpenTimer.Tick += (s, e) => { _cardOpenTimer.Stop(); OpenCard(); };
            _cardCloseTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = CardCloseDelay };
            _cardCloseTimer.Tick += (s, e) =>
            {
                _cardCloseTimer.Stop();
                // Still on the way between the bar and the card, or the card just folded its list away under the mouse
                if (MouseInCardZone() || (_card != null && DateTime.UtcNow < _card.KeepOpenUntil)) _cardCloseTimer.Start();
                else _card?.HideCard();
            };

            if (_nowPlayingPanel != null)
            {
                // The whole top-panel button reacts, not just the text inside it: its padding counts as Now Playing.
                // Found once the panel is in the bar; the panel itself until then.
                _nowPlayingPanel.MouseEnter += (s, e) => { if (_cardHoverTarget == null) OnCardHoverEnter(); };
                _nowPlayingPanel.MouseLeave += (s, e) => { if (_cardHoverTarget == null) OnCardHoverLeave(); };
                _nowPlayingPanel.Loaded += (s, e) => AttachCardHoverTarget();
                AttachCardHoverTarget();
            }
            UpdateNowPlayingTooltip();
        }

        // Where the card's top goes, in screen pixels: the top edge of the first view below the top bar, under Now
        // Playing (themes often leave a band between Playnite's TopPanel and the game view, which reads as part of the
        // bar). Found by walking down from the bar's bottom with hit tests until an element starts there; the bar's
        // bottom edge when nothing does.
        internal static double CardTopEdge(FrameworkElement panel, out string source)
        {
            var bar = TopBarOf(panel);
            var window = Window.GetWindow(panel);
            source = bar.GetType().Name;
            if (window == null) return bar.PointToScreen(new Point(0, bar.ActualHeight)).Y;

            double barBottom = bar.TransformToAncestor(window).Transform(new Point(0, bar.ActualHeight)).Y;
            double x = panel.TransformToAncestor(window).Transform(new Point(panel.ActualWidth / 2, 0)).X;
            for (double y = barBottom + 1; y < barBottom + CardEdgeSearchDepth; y += 2)
            {
                for (var node = VisualTreeHelper.HitTest(window, new Point(x, y))?.VisualHit; node != null && node != window; node = VisualTreeHelper.GetParent(node))
                {
                    if (!(node is FrameworkElement element) || bar.IsAncestorOf(element) || element == bar) break;
                    double top = element.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
                    // Only something you can see counts: layout containers often start right at the bar's edge
                    // with nothing drawn, while the view the eye reads as "below the bar" starts further down
                    if (top >= barBottom - 0.5 && top <= y + 0.5 && HasVisibleSurface(element))
                    {
                        source = element.GetType().Name + " below " + bar.GetType().Name;
                        return window.PointToScreen(new Point(0, top)).Y;
                    }
                    if (top < barBottom - 0.5) break; // a background that starts above the bar's edge: keep walking down
                }
            }
            return window.PointToScreen(new Point(0, barBottom)).Y;
        }

        private const double CardEdgeSearchDepth = 120; // how far below the bar a view may start, in DIPs

        private static bool HasVisibleSurface(FrameworkElement element)
        {
            switch (element)
            {
                case System.Windows.Controls.Image image: return image.Source != null;
                case System.Windows.Shapes.Shape shape: return Visible(shape.Fill);
                case Border border: return Visible(border.Background);
                case Panel panel: return Visible(panel.Background);
                case Control control: return Visible(control.Background);
                default: return false;
            }
        }

        private static bool Visible(Brush brush) =>
            brush != null && brush.Opacity > 0 && !(brush is SolidColorBrush solid && solid.Color.A == 0);

        // The top bar holding the panel: Playnite's own TopPanel control, which themes keep; failing that, the outermost
        // ancestor that is still bar-sized (the next level up is the whole window body).
        internal static FrameworkElement TopBarOf(FrameworkElement panel)
        {
            FrameworkElement fallback = panel;
            bool fallbackDone = false;
            double limit = Math.Max(120, panel.ActualHeight * 3);
            for (var node = System.Windows.Media.VisualTreeHelper.GetParent(panel); node != null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            {
                if (node is Window) break;
                if (!(node is FrameworkElement element) || element.ActualHeight <= 0) continue;
                if (element.GetType().Name == "TopPanel") return element;
                if (element.ActualHeight > limit) fallbackDone = true;
                else if (!fallbackDone) fallback = element;
            }
            return fallback;
        }

        private FrameworkElement _cardHoverTarget;

        private void AttachCardHoverTarget()
        {
            if (_cardHoverTarget != null || _nowPlayingPanel == null) return;
            for (var node = VisualTreeHelper.GetParent(_nowPlayingPanel); node != null && !(node is Window); node = VisualTreeHelper.GetParent(node))
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase button)
                {
                    _cardHoverTarget = button;
                    button.MouseEnter += (s, e) => OnCardHoverEnter();
                    button.MouseLeave += (s, e) => OnCardHoverLeave();
                    return;
                }
            }
        }

        // The button, the card and the corridor between them, in screen pixels: crossing the gap to the card
        // (the card hangs from the game view, some way below the bar) must not close it.
        private bool MouseInCardZone()
        {
            if (_card?.IsOpen != true || !GetCursorPos(out var cursor)) return false;
            var target = _cardHoverTarget ?? _nowPlayingPanel;
            if (target == null || !target.IsVisible) return false;
            var a = target.PointToScreen(new Point(0, 0));
            var b = target.PointToScreen(new Point(target.ActualWidth, target.ActualHeight));
            var zone = Rect.Union(new Rect(a, b), _card.ScreenBounds);
            return zone.Contains(new Point(cursor.X, cursor.Y));
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        private void OnCardHoverEnter()
        {
            _cardCloseTimer?.Stop();
            if (!CardEnabled || _card?.IsOpen == true) return;
            _cardOpenTimer?.Start();
            // Get the window and its data ready during the wait, so the card's first frame isn't held up by them
            EnsureCard()?.Prepare();
        }

        private void OnCardHoverLeave()
        {
            _cardOpenTimer?.Stop();
            if (_card?.IsOpen == true) _cardCloseTimer?.Start();
        }

        private void OpenCard()
        {
            if (!CardEnabled || _nowPlayingPanel == null || !_nowPlayingPanel.IsVisible || _shownSong == null) return;
            try
            {
                if (EnsureCard() == null) return;
                // Centred on Now Playing, its top on the line where the top bar ends and the game view begins
                var topLeft = _nowPlayingPanel.PointToScreen(new Point(0, 0));
                var right = _nowPlayingPanel.PointToScreen(new Point(_nowPlayingPanel.ActualWidth, 0)).X;
                var edge = CardTopEdge(_nowPlayingPanel, out var source);
                _log?.Invoke($"TopPanel: card top at {edge:F0}px ({source}), panel top {topLeft.Y:F0}px");
                _card.ShowUnder(new Rect(topLeft, new Point(right, Math.Max(edge, topLeft.Y))));
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Now Playing card failed: {ex.Message}");
            }
        }

        private NowPlayingCard EnsureCard()
        {
            if (_card != null) return _card;
            var owner = _nowPlayingPanel == null ? null : Window.GetWindow(_nowPlayingPanel);
            if (owner == null || _cardHost == null) return null;
            _card = new NowPlayingCard(owner, _cardHost);
            _card.MouseEnter += (s, e) => _cardCloseTimer?.Stop();
            _card.MouseLeave += (s, e) => _cardCloseTimer?.Start();
            return _card;
        }

        private void InitializeNowPlayingPanel()
        {
            try
            {
                _nowPlayingPanel = new NowPlayingPanel(_log);
                _nowPlayingPanel.SongShown += OnNowPlayingSongShown;

                // Enable embedded progress bar if BelowNowPlaying mode
                var progressSettings = _getSettings?.Invoke();
                if (progressSettings?.ShowProgressBar == true &&
                    progressSettings.ProgressBarPosition == ProgressBarPosition.BelowNowPlaying)
                {
                    _nowPlayingPanel.EnableEmbeddedProgressBar(_getPlaybackService);
                }

                _nowPlayingItem = new TopPanelItem
                {
                    Icon = _nowPlayingPanel,
                    Title = NowPlayingIdleTitle,
                    Visible = true,
                    Activated = OnNowPlayingActivated
                };

                // Create metadata service and subscribe to song changes
                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService != null)
                {
                    _metadataService = new SongMetadataService(playbackService, null, _getSettings);
                    _metadataService.OnSongInfoChanged += OnSongInfoChanged;

                    // If there's already a song playing, update immediately
                    if (!string.IsNullOrEmpty(playbackService.CurrentSongPath))
                    {
                        var songInfo = _metadataService.ReadMetadata(playbackService.CurrentSongPath);
                        _nowPlayingPanel.UpdateSongInfo(songInfo);
                    }
                }

                // Subscribe to Spotify now-playing changes so the panel refreshes when Spotify's track changes
                var spotify = _getSpotifyService?.Invoke();
                if (spotify != null)
                    spotify.NowPlayingChanged += OnSpotifyNowPlayingChanged;

                _log?.Invoke("TopPanel: Now Playing panel initialized");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"TopPanel: Error initializing Now Playing panel: {ex.Message}");
            }
        }

        // Click: the card at once when it's on (its footer opens the folder); otherwise the folder, as before.
        private void OnNowPlayingActivated()
        {
            if (CardEnabled)
            {
                _cardOpenTimer?.Stop();
                OpenCard();
                return;
            }
            OpenMusicFolder();
        }

        private void OpenMusicFolder()
        {
            try
            {
                var playbackService = _getPlaybackService?.Invoke();
                var currentSongPath = playbackService?.CurrentSongPath;

                if (!string.IsNullOrEmpty(currentSongPath) && File.Exists(currentSongPath))
                {
                    // Open the folder containing the current song
                    var folder = Path.GetDirectoryName(currentSongPath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        ShellHelper.OpenFolderInExplorer(folder);
                        _log?.Invoke($"TopPanel: Opening music folder: {folder}");
                    }
                }
                else
                {
                    _log?.Invoke("TopPanel: No song playing, cannot open folder");
                }
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "opening music folder from top panel");
            }
        }

        private void OnSongInfoChanged(SongInfo songInfo)
        {
            // When Spotify is active, show Spotify's track off-thread so we never block the UI
            // with a synchronous SMTC call. The callback is marshaled back to the UI thread.
            var spotify = _getSpotifyService?.Invoke();
            if (spotify != null && spotify.IsSpotifyActive)
            {
                spotify.RequestNowPlaying(np =>
                {
                    if (!np.IsEmpty)
                        _nowPlayingPanel?.UpdateSongInfo(new SongInfo(string.Empty, np.Title, np.Artist, np.Duration));
                });
                return;
            }

            _nowPlayingPanel?.UpdateSongInfo(songInfo);

            // Reset progress bar on song change so it doesn't show stale position during crossfade
            _progressBar?.Reset();
            _nowPlayingPanel?.ResetEmbeddedProgress();

            // Feed duration to progress bar (standalone or embedded)
            var duration = songInfo?.Duration ?? TimeSpan.Zero;
            _progressBar?.SetDuration(duration);
            _nowPlayingPanel?.SetEmbeddedDuration(duration);
        }

        // Refreshes the Now Playing panel when Spotify's active state or track changes. Shows Spotify track when
        // active; restores the last UPS song when Spotify becomes inactive. Last Spotify state/track this handler acted
        // on. SMTC re-fires NowPlayingChanged on every position tick (many/sec); without these each tick did a full
        // UpdateIcons Dispatcher.Invoke on the UI thread plus a panel refresh — a constant UI-thread drip that
        // hitched themes.
        private string _lastSpotifyIconKey;
        private string _lastSpotifyPanelKey;

        private void OnSpotifyNowPlayingChanged()
        {
            // Refresh the play/pause glyph only when Spotify's STATE actually changed (play/pause
            // flip, active/inactive). This event only signals Spotify changes, so gating on the
            // Spotify snapshot is safe — UPS-side changes drive UpdateIcons from their own events.
            var spotify = _getSpotifyService?.Invoke();
            bool active = spotify != null && spotify.IsSpotifyActive;
            var iconKey = active + "|" + (spotify?.IsSpotifyPlaying ?? false);
            if (iconKey != _lastSpotifyIconKey)
            {
                _lastSpotifyIconKey = iconKey;
                UpdateIcons();
            }

            if (active)
            {
                // Fetch off-thread so we never block the UI with a synchronous SMTC call.
                // The callback is marshaled back to the UI thread by the client.
                spotify.RequestNowPlaying(np =>
                {
                    // Same track as last panel update -> skip the UI-thread panel refresh.
                    var panelKey = np.IsEmpty ? string.Empty : (np.Title + "\n" + np.Artist);
                    if (panelKey == _lastSpotifyPanelKey) return;
                    _lastSpotifyPanelKey = panelKey;

                    if (!np.IsEmpty)
                        _nowPlayingPanel?.UpdateSongInfo(new SongInfo(string.Empty, np.Title, np.Artist, np.Duration));
                    else
                    {
                        // Active but track not yet available — restore UPS's current song info
                        var fallback = _metadataService?.CurrentSongInfo ?? SongInfo.Empty;
                        _nowPlayingPanel?.UpdateSongInfo(fallback);
                    }
                });
                return;
            }

            // Spotify became inactive — restore UPS's current song info (and clear the panel key so
            // the next Spotify activation always refreshes).
            _lastSpotifyPanelKey = null;
            var upsSong = _metadataService?.CurrentSongInfo ?? SongInfo.Empty;
            _nowPlayingPanel?.UpdateSongInfo(upsSong);
        }

        private void SubscribeToEvents(IMusicPlaybackService playbackService)
        {
            if (playbackService == null) return;

            playbackService.OnMusicStarted += _ => UpdateIcons();
            playbackService.OnMusicStopped += _ => UpdateIcons();
            playbackService.OnPlaybackStateChanged += UpdateIcons;
            playbackService.OnSongChanged += _ => UpdateIcons();
            playbackService.OnSongCountChanged += UpdateSkipVisibility;
        }

        // Toggles Calm Down Mode. Routed through the same settings writer the Fullscreen quick menu
        // uses rather than mutating the settings object directly: turning Calm Down on while the
        // player is SDL2 has to swap the backend to NAudio (SDL2 has no post-mixer hook to host the
        // processor), and that swap hangs off the diff event which only a real UpdateSettings fires.
        // Direct mutation would leave the moon lit with nothing dimming the audio.
        private void OnCalmDownActivated()
        {
            try
            {
                if (_updateSettings == null)
                {
                    _log?.Invoke("TopPanel: Calm Down button has no settings writer");
                    return;
                }

                bool turningOn = _getSettings?.Invoke()?.CalmDownModeEnabled != true;
                _updateSettings(s => s.CalmDownModeEnabled = turningOn);
                _log?.Invoke($"TopPanel: Calm Down Mode {(turningOn ? "on" : "off")}");

                UpdateCalmDownVisual(turningOn);
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "toggling Calm Down Mode");
            }
        }

        // The glyph never changes - only how lit it is - so this is the whole visual state.
        private void UpdateCalmDownVisual(bool isOn)
        {
            if (_calmDownIcon == null) return;

            _calmDownIcon.Opacity = isOn ? CalmDownOnOpacity : CalmDownOffOpacity;
            if (_calmDownItem != null)
                _calmDownItem.Title = isOn
                    ? "UniPlaySong: Calm Down Mode (on)"
                    : "UniPlaySong: Calm Down Mode";
        }

        private void OnPlayPauseActivated()
        {
            try
            {
                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService == null)
                {
                    _log?.Invoke("TopPanel: PlaybackService is null");
                    return;
                }

                // When Spotify is the active source (Radio Mode), the play/pause button controls SPOTIFY via SMTC — not UPS's
                // own (silent) player. Same path as the "SPOTIFY: PAUSE/PLAY" menu. Pausing here is a user pause the two-flag
                // radio logic respects (it won't auto-resume); playing clears it. Both react off the UI thread via the worker.
                var spotify = _getSpotifyService?.Invoke();
                if (spotify != null && spotify.IsSpotifyActive)
                {
                    _log?.Invoke("TopPanel: toggling Spotify play/pause (Spotify is the active source)");
                    spotify.ToggleManualPlayPause();
                    UpdateIcons();
                    return;
                }

                if (playbackService.IsPaused)
                {
                    _log?.Invoke("TopPanel: Resuming playback via manual toggle");
                    playbackService.NotifyManualStart();
                    // Clear all automatic pause sources — user explicitly wants to play
                    playbackService.RemovePauseSource(PauseSource.Idle);
                    playbackService.RemovePauseSource(PauseSource.ExternalAudio);
                    playbackService.RemovePauseSource(PauseSource.SystemLock);
                    playbackService.RemovePauseSource(PauseSource.Suspended); // a missed wake event must not strand it
                    playbackService.RemovePauseSource(PauseSource.Manual);
                }
                else if (playbackService.IsPlaying || playbackService.IsLoaded)
                {
                    _log?.Invoke("TopPanel: Pausing playback via manual toggle");
                    playbackService.AddPauseSource(PauseSource.Manual);
                }
                else
                {
                    var currentGame = _getCurrentGame?.Invoke();
                    if (currentGame != null)
                    {
                        _log?.Invoke($"TopPanel: Starting playback for {currentGame.Name}");
                        playbackService.NotifyManualStart();
                        playbackService.PlayGameMusic(currentGame, _getSettings?.Invoke());
                    }
                    else
                    {
                        _log?.Invoke("TopPanel: No game selected, cannot start playback");
                    }
                }

                UpdateIcons();
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "toggling playback from top panel");
            }
        }

        private void OnSkipActivated()
        {
            try
            {
                if (!_canSkip)
                {
                    _log?.Invoke("TopPanel: Skip disabled (not enough songs)");
                    return;
                }

                // Spotify as active source: route skip to Spotify's next-track (mirrors play/pause).
                var spotify = _getSpotifyService?.Invoke();
                if (spotify != null && spotify.IsSpotifyActive)
                {
                    _log?.Invoke("TopPanel: skipping Spotify track (Spotify is the active source)");
                    spotify.SkipNext();
                    return;
                }

                var playbackService = _getPlaybackService?.Invoke();
                if (playbackService == null)
                {
                    _log?.Invoke("TopPanel: PlaybackService is null, cannot skip");
                    return;
                }

                _log?.Invoke("TopPanel: Skipping to next song");
                playbackService.SkipToNextSong();
            }
            catch (Exception ex)
            {
                _handleError?.Invoke(ex, "skipping to next song from top panel");
            }
        }

        private bool _canSkip = false;

        private void UpdateSkipVisibility()
        {
            var playbackService = _getPlaybackService?.Invoke();
            UpdateSkipState(playbackService);
            ApplyVisibility(_getSettings?.Invoke(), playbackService);
        }

        private void UpdateSkipState(IMusicPlaybackService playbackService)
        {
            if (_skipItem == null || _skipIcon == null) return;

            // Spotify as active source: skip routes to Spotify's next-track (SMTC), always available.
            var spotify = _getSpotifyService?.Invoke();
            bool spotifySkip = spotify != null && spotify.IsSpotifyActive;

            _canSkip = spotifySkip
                || playbackService?.CurrentGameSongCount >= 2
                || playbackService?.IsPlayingPoolBasedDefault == true;
            _skipIcon.Opacity = _canSkip ? 1.0 : 0.3;
            _skipItem.Title = _canSkip
                ? "UniPlaySong: Skip to Next Song"
                : "UniPlaySong: Skip to Next Song (No additional songs)";
        }

        public void UpdateIcons()
        {
            UpdateNowPlayingMotion();
            if (_playPauseIcon == null || _playPauseItem == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    var playbackService = _getPlaybackService?.Invoke();
                    bool isPlaying = playbackService?.IsPlaying == true && playbackService?.IsPaused != true;

                    // Play/pause GLYPH: when Spotify is the active source, reflect SPOTIFY's transport state (UPS plays nothing in
                    // radio mode, so its own IsPlaying is always false). The visualizer/peak meter use the same Spotify-aware state:
                    // the process- loopback capture feeds them Spotify audio, so they react whenever Spotify plays. The
                    // progress bar stays on UPS state — UPS doesn't know Spotify's track position.
                    var spotify = _getSpotifyService?.Invoke();
                    bool iconPlaying = (spotify != null && spotify.IsSpotifyActive)
                        ? spotify.IsSpotifyPlaying
                        : isPlaying;

                    _playPauseIcon.Text = iconPlaying ? MediaControlIcons.Pause : MediaControlIcons.Play;
                    _playPauseItem.Title = iconPlaying ? "UniPlaySong: Pause Music" : "UniPlaySong: Play Music";

                    UpdateSkipState(playbackService);

                    var settings = _getSettings?.Invoke();

                    // Calm Down can also be toggled from the settings dialog, a theme, or the
                    // Fullscreen menu, so the button reflects the setting rather than trusting its
                    // own last click.
                    if (_calmDownItem != null)
                        UpdateCalmDownVisual(settings?.CalmDownModeEnabled == true);

                    var shown = ApplyVisibility(settings, playbackService);
                    _spectrumVisualizer?.SetActive(iconPlaying && shown.Spectrum);
                    _peakMeter?.SetActive(iconPlaying && shown.PeakMeter);
                    _progressBar?.SetActive(isPlaying);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Error updating top panel icons: {ex.Message}");
                }
            });
        }

        // Which top-panel items show. Decide is the whole rule; ApplyVisibility feeds it the live state and sets each
        // item's Visible.
        internal sealed class PanelVisibility
        {
            public bool PlayPause, Skip, CalmDown, Spectrum, PeakMeter, Progress, NowPlaying;
        }

        // musicLoaded: a song is loaded (playing or paused) or Spotify is the active source. Auto-hide only reacts to
        // nothing loaded, never to a pause, so Play stays reachable to resume. hideNowPlayingForDefault keeps Now
        // Playing's own rule (hidden while non-pool default music plays, when that option is on). defaultMusicPlaying
        // counts as idle too when AutoHideForDefaultMusic is on.
        internal static PanelVisibility Decide(UniPlaySongSettings s, bool musicLoaded, bool canSkip, bool hideNowPlayingForDefault,
                                               bool defaultMusicPlaying = false)
        {
            if (s == null) return new PanelVisibility();
            // idle: auto-hide applies right now. Each item is only collapsed if the user picked it (AutoHide*).
            bool idle = s.AutoHideMediaControls
                && (!musicLoaded || (s.AutoHideForDefaultMusic && defaultMusicPlaying));
            bool controls = s.ShowDesktopMediaControls;
            return new PanelVisibility
            {
                PlayPause = controls && s.ShowPlayPauseButton && !(idle && s.AutoHidePlayPause),
                Skip = controls && s.ShowSkipButton
                    && !(s.AutoHideSkip && (idle || (s.AutoHideMediaControls && !canSkip))),
                CalmDown = controls && s.ShowCalmDownButton && !(idle && s.AutoHideCalmDown),
                Spectrum = s.ShowSpectrumVisualizer && !(idle && s.AutoHideSpectrum),
                PeakMeter = s.ShowPeakMeter && !(idle && s.AutoHidePeakMeter),
                Progress = s.ShowProgressBar && !(idle && s.AutoHideProgress),
                NowPlaying = s.ShowNowPlayingInTopPanel && !hideNowPlayingForDefault,
            };
        }

        private PanelVisibility ApplyVisibility(UniPlaySongSettings settings, IMusicPlaybackService playbackService)
        {
            var spotify = _getSpotifyService?.Invoke();
            bool musicLoaded = playbackService?.IsLoaded == true || playbackService?.IsPlaying == true
                || (spotify != null && spotify.IsSpotifyActive);
            bool hideForDefault = settings?.HideNowPlayingForDefaultMusic == true
                && playbackService?.IsPlayingDefaultMusic == true
                && playbackService?.IsPlayingPoolBasedDefault != true;

            bool defaultMusicPlaying = playbackService?.IsPlayingDefaultMusic == true;
            var v = Decide(settings, musicLoaded, _canSkip, hideForDefault, defaultMusicPlaying);
            if (_playPauseItem != null) _playPauseItem.Visible = v.PlayPause;
            if (_skipItem != null) _skipItem.Visible = v.Skip;
            if (_calmDownItem != null) _calmDownItem.Visible = v.CalmDown;
            if (_spectrumItem != null) _spectrumItem.Visible = v.Spectrum;
            if (_peakMeterItem != null) _peakMeterItem.Visible = v.PeakMeter;
            if (_progressItem != null) _progressItem.Visible = v.Progress;
            if (_nowPlayingItem != null) _nowPlayingItem.Visible = v.NowPlaying;
            return v;
        }

        // Keep old method name for backwards compatibility
        public void UpdateIcon() => UpdateIcons();

        // The Now Playing title scrolls only while music plays (UPS or Spotify) and the user wants it to. Ahead of the
        // play/pause button's guard in UpdateIcons: the panel shows even when that button is hidden.
        private void UpdateNowPlayingMotion()
        {
            if (_nowPlayingPanel == null) return;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                var playback = _getPlaybackService?.Invoke();
                var spotify = _getSpotifyService?.Invoke();
                bool playing = spotify != null && spotify.IsSpotifyActive
                    ? spotify.IsSpotifyPlaying
                    : playback?.IsPlaying == true && playback?.IsPaused != true;
                _nowPlayingPanel.SetPlaying(playing);
                var settings = _getSettings?.Invoke();
                _nowPlayingPanel.SetScrollEnabled(settings?.ScrollNowPlayingTitle != false);
                _nowPlayingPanel.SetLegacyAnimation(settings?.UseLegacyNowPlayingAnimation == true);
                UpdateNowPlayingTooltip();        // the card setting may have changed
                if (!CardEnabled) _card?.HideCard();
                _cardHost?.RaiseChanged();        // play state, Calm Down
            }));
        }
    }
}
