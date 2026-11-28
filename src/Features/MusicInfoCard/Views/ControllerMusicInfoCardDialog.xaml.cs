using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Controls;
using UniPlaySong.Features.MusicInfoCard.Models;
using UniPlaySong.Features.MusicInfoCard.Player;
using UniPlaySong.Features.MusicInfoCard.Services;
using UniPlaySong.Models;
using UniPlaySong.IconGlow;
using UniPlaySong.Services.ActiveMedia;
using UniPlaySong.Services.Controller;

namespace UniPlaySong.Features.MusicInfoCard.Views
{
    // The Fullscreen Music Info Card: a small player for one game's music. Player page: the stats, the game's songs (the
    // playing one tinted, the primary one pink) and a pinned dock - Previous, Play/Pause in its seek ring, Next, Calm Down,
    // Live Effects. Details page: the other stats. The controller's rules live in CardInputRules; this class draws and
    // carries the commands out through the host.
    public partial class ControllerMusicInfoCardDialog : UserControl, IControllerInputReceiver
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();

        private CancellationTokenSource _cts;
        private Game _game;
        private IMusicStatsProvider _statsProvider;
        private Action<string> _playSong; // without a host: A on a song plays it
        private BitmapSource _frost; // the glass tiles' extra-blurred backdrop (GlassTiles)

        private Func<(double X, double Y)> _stick;       // the right stick, both axes; null on a Playnite without analog input
        private MusicInfoCardPlayerHost _host;          // the player controls; null = stats only
        private DispatcherTimer _stickPoll;              // slow idle check for a tilt
        private bool _stickScrolling;                    // frame-synced scrolling hooked while the stick is tilted
        private TimeSpan _lastFrameTime;
        private double _stickVelocity;                   // px/s, eased toward the stick so reading noise doesn't jolt

        private CardInputRules _rules;                   // null until the songs are known
        private readonly List<SongRow> _rows = new List<SongRow>();
        private List<string> _rowPaths = new List<string>();   // the rows' files, for the playing-row lookup
        private DateTime _lastDirectionTime;
        private DispatcherTimer _refresh;                // ring, glyph, toggles, playing row, line
        private DispatcherTimer _seekRepeat;             // LT/RT held
        private double _repeatSeconds;
        private DispatcherTimer _jogPoll;                // right stick as a jog wheel while Play/Pause is live
        private double? _jogAngle;
        private RelativeSeeker _jogSeeker;
        private int _playingIndex = -1;
        private string _shownLine;                       // the now-playing line as last drawn; redrawn only on a change
        private string _primaryPath;                     // read once per open: the lookup touches the disk
        private readonly HeldSeek _held = new HeldSeek();
        private bool _closed;                            // set first on Unloaded; late events and timers stand down
        private bool _statsShown;                        // the loader never appears once the pages are filled
        private bool _playedSong;                        // the card started a song; on close the host may hand back (Spotify Radio)
        private bool _ringButtonDown;                    // mouse pressed on the Play/Pause button itself: a click
        private double? _ringScrub;                      // mouse on the ring band: the song fraction the knob is held at
        private DateTime _lastScrubSeek;
        private DateTime _ringHoldUntil;                 // after a scrub, the audio catches up before the refresh moves the knob
        private bool _ringHover;
        private Window _window;                          // keys are read at window level, wherever focus sits
        private readonly SeekRing Ring = new SeekRing { Width = 64, Height = 64 };

        private string _imagePath;
        private IconColorExtractor _colorExtractor;
        private SolidColorBrush _accentBrush;
        private AccentTints _tints;                      // the accent's see-through shades, made once per accent colour

        private static readonly SolidColorBrush White = CreateFrozenBrush(Colors.White);
        private static readonly SolidColorBrush Transparent = CreateFrozenBrush(Colors.Transparent);
        private static readonly SolidColorBrush PrimaryPink = CreateFrozenBrush(Color.FromRgb(0xE9, 0xA8, 0xF2));
        private static readonly SolidColorBrush Teal = CreateFrozenBrush(Color.FromRgb(0x1F, 0xD8, 0xC1));
        private static readonly SolidColorBrush SpotifyGreen = CreateFrozenBrush(Color.FromRgb(0x1D, 0xB9, 0x54));
        private static readonly SolidColorBrush LabelGrey = CreateFrozenBrush(Color.FromRgb(0xA9, 0xA2, 0x9D));
        private static readonly SolidColorBrush DockFill = CreateFrozenBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush DockRim = CreateFrozenBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush TabIdle = CreateFrozenBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush RowFill = CreateFrozenBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush PlayRim = CreateFrozenBrush(Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF));

        public ControllerMusicInfoCardDialog()
        {
            InitializeComponent();
            RingSlot.Children.Insert(0, Ring);
            SetAccent(Color.FromRgb(0x7E, 0x57, 0xC2));

            Loaded += (s, e) =>
            {
                Focus();
                Keyboard.Focus(this);
                _window = Window.GetWindow(this);
                if (_window != null) { _window.PreviewKeyDown += OnKeyDown; _window.PreviewKeyUp += OnKeyUp; }
                GlassTiles.Attach(BlurredBackdrop, PagesRoot, DetailsScroll, _frost);
                StartStickScroll();
                ShowPage();

                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Register(this);
                }
            };

            Unloaded += (s, e) =>
            {
                // Nothing keeps seeking, scrolling or refreshing after the card closes (Review Focus 5)
                _closed = true;
                if (_window != null) { _window.PreviewKeyDown -= OnKeyDown; _window.PreviewKeyUp -= OnKeyUp; }
                _stickPoll?.Stop();
                StopStickFrames();
                _refresh?.Stop();
                _seekRepeat?.Stop();
                _jogPoll?.Stop();
                _jogSeeker?.Flush();
                if (_playedSong) _host?.LeavePlayer?.Invoke(_game);
                try { _cts?.Cancel(); } catch { /* best effort */ }
                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Unregister(this);
                }
            };

            HookMouse();
        }

        internal void Initialize(
            Game game,
            IMusicStatsProvider statsProvider,
            string imagePath,
            IconColorExtractor colorExtractor,
            BitmapSource backdropSnapshot,
            Action<string> playSong = null,
            BitmapSource frost = null,
            Func<(double X, double Y)> stick = null,
            MusicInfoCardPlayerHost host = null)
        {
            _game = game;
            _playSong = playSong;
            _frost = frost;
            _stick = stick;
            _host = host;
            _statsProvider = statsProvider;
            _imagePath = imagePath;
            _colorExtractor = colorExtractor;
            GameNameText.Text = game?.Name ?? "Unknown Game";
            var rediscover = game == null ? null : host?.Rediscover?.Invoke(game);
            if (!string.IsNullOrEmpty(rediscover)) { RediscoverText.Text = rediscover; RediscoverText.Visibility = Visibility.Visible; }
            if (_host?.SeekBy != null)
                _jogSeeker = new RelativeSeeker(() => _host.Snapshot?.Invoke()?.HasActiveMedia == true, _host.SeekBy);

            // See MusicInfoCardDialog for the snapshot pattern.
            if (backdropSnapshot != null) BlurredBackdrop.Source = backdropSnapshot;

            TryApplyGameIconAndAccent();
            Ring.Accent = _accentBrush;
            Loaded += OnDialogLoadedOnce;
        }

        private void TryApplyGameIconAndAccent()
        {
            try
            {
                if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath)) return;

                var bitmap = MusicInfoCardHandler.LoadUnlockedBitmap(_imagePath);
                GameIconImage.Source = bitmap;
                GameIconImage.Visibility = Visibility.Visible;
                IconPlaceholderBorder.Visibility = Visibility.Collapsed;

                if (_colorExtractor != null && _game != null)
                {
                    var (primary, _) = _colorExtractor.GetGlowColors(_game.Id, bitmap);
                    ApplyAccentColor(primary);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"ControllerMusicInfoCardDialog: icon load failed: {ex.Message}");
            }
        }

        private void SetAccent(Color color)
        {
            _accentBrush = CreateFrozenBrush(color);
            _tints = new AccentTints(_accentBrush);
        }

        private void ApplyAccentColor(Color color)
        {
            SetAccent(color);
            AccentStrip.Background = _accentBrush;
            HeaderSubtitle.Foreground = _accentBrush;
            IconGlowEffect.Color = color;
            CardTintLayer.Background = CreateFrozenBrush(Color.FromArgb(13, color.R, color.G, color.B)); // ~5% whole-card tint
        }

        private async void OnDialogLoadedOnce(object sender, RoutedEventArgs e)
        {
            Loaded -= OnDialogLoadedOnce;
            await LoadStatsAsync().ConfigureAwait(false);
        }

        private async System.Threading.Tasks.Task LoadStatsAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(Constants.MusicInfoCardLoaderGraceMs).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_statsShown && !_closed) LoadingPanel.Visibility = Visibility.Visible;
                }));
            });

            try
            {
                var stats = await _statsProvider.ComputeAsync(_game, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                await Dispatcher.BeginInvoke(new Action(() => RenderStats(stats)));
            }
            catch (OperationCanceledException) { /* expected on close */ }
            catch (Exception ex)
            {
                Logger.Error(ex, "ControllerMusicInfoCardDialog: stats computation failed");
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    _statsShown = true;
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    EmptyStateText.Text = "Failed to read music metadata.";
                    EmptyStateText.Visibility = Visibility.Visible;
                    FadeInPages();
                }));
            }
        }

        private void RenderStats(MusicStats stats)
        {
            if (_closed) return;
            _statsShown = true;
            LoadingPanel.Visibility = Visibility.Collapsed;

            if (stats == null || stats.FileCount == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                // No songs left, but the history and the notes (unreadable files, playlists) still apply
                RenderListening();
                if (stats != null) RenderNotes(stats);
                StartPlayer(new List<SongEntry>());
                FadeInPages();
                return;
            }

            FileCountText.Text = stats.FileCount.ToString();
            TotalDurationText.Text = FormatDuration(stats.TotalDuration);
            TotalSizeText.Text = FormatSize(stats.TotalSizeBytes);

            RenderDetails(stats);
            StartPlayer(stats.Songs ?? new List<SongEntry>());
            FadeInPages();
        }

        // ---- Details page: Listening, Formats, Song lengths, then the playlist / unreadable notes ----

        private static readonly SolidColorBrush Purple = CreateFrozenBrush(Color.FromRgb(0xB9, 0x8B, 0xF5));
        private static readonly SolidColorBrush Orange = CreateFrozenBrush(Color.FromRgb(0xFF, 0xB3, 0x47));
        private static readonly SolidColorBrush Sky = CreateFrozenBrush(Color.FromRgb(0x5C, 0xC8, 0xFF));
        private static readonly SolidColorBrush[] FormatPalette = { Purple, Teal, Orange, PrimaryPink, Sky };

        private void RenderDetails(MusicStats stats)
        {
            RenderListening();

            // Formats: the donut's slices in the legend's order and colours
            var formats = stats.FormatBreakdown.OrderByDescending(kv => kv.Value).ToList();
            int total = Math.Max(1, stats.FileCount);
            var slices = formats.Select((kv, i) => ((double)kv.Value, (Brush)FormatPalette[i % FormatPalette.Length])).ToList();
            FormatDonutHost.Content = CardCharts.Donut(slices, stats.FileCount.ToString(), 76);
            FormatLegend.ItemsSource = formats.Select((kv, i) => new
            {
                Swatch = FormatPalette[i % FormatPalette.Length],
                Extension = kv.Key,
                Share = $"{kv.Value} · {(int)Math.Round(kv.Value * 100.0 / total)}%"
            }).ToList();

            // Song lengths
            var lengths = (stats.Songs ?? new List<SongEntry>()).Where(x => x.Duration > TimeSpan.Zero).Select(x => x.Duration).ToList();
            LengthBarsHost.Content = CardCharts.LengthBars(lengths, WithOpacity(Teal, 0.35), Teal, Purple, 62);
            if (stats.LongestTrack.HasValue)
                SetLengthLine(LongestText, Teal, "Longest: ", stats.LongestTrack.Value.Title, stats.LongestTrack.Value.Duration);
            if (stats.ShortestTrack.HasValue)
                SetLengthLine(ShortestText, Purple, "Shortest: ", stats.ShortestTrack.Value.Title, stats.ShortestTrack.Value.Duration);

            RenderNotes(stats);
        }

        private void RenderNotes(MusicStats stats)
        {
            if (stats.PlaylistFileCount > 0)
            {
                PlaylistInfoText.Text =
                    $"{stats.PlaylistFileCount} playlist file" + (stats.PlaylistFileCount == 1 ? "" : "s") +
                    $" containing {stats.PlaylistTrackCount} track" + (stats.PlaylistTrackCount == 1 ? "" : "s") + ".";
                PlaylistPanel.Visibility = Visibility.Visible;
            }
            if (stats.UnreadableCount > 0)
            {
                UnreadableText.Text = $"⚠ {stats.UnreadableCount} file" + (stats.UnreadableCount == 1 ? "" : "s") + " could not be read.";
                UnreadablePanel.Visibility = Visibility.Visible;
            }
        }

        private static void SetLengthLine(TextBlock text, Brush swatch, string label, string title, TimeSpan duration)
        {
            text.Inlines.Clear();
            text.Inlines.Add(new Run("■ ") { Foreground = swatch });
            text.Inlines.Add(new Run(label) { Foreground = LabelGrey });
            text.Inlines.Add(new Run(title + " "));
            text.Inlines.Add(new Run(FormatDuration(duration)) { Foreground = swatch });
        }

        // This game's listening history: totals, then the top 3 with bars sized by plays
        private void RenderListening()
        {
            var snapshot = _host?.Listening?.Invoke();
            var view = snapshot == null || _game == null
                ? new GameListeningView()
                : GameListeningView.Build(snapshot, _game.Id.ToString(), _host.GameMusicFolder?.Invoke(_game), DateTime.UtcNow);
            NoHistoryText.Visibility = view.HasHistory ? Visibility.Collapsed : Visibility.Visible;
            HistoryPanel.Visibility = view.HasHistory ? Visibility.Visible : Visibility.Collapsed;
            if (!view.HasHistory) return;

            ListenedText.Text = FormatListened(view.Listened);
            PlaysText.Text = view.Plays.ToString();
            LastPlayedText.Text = view.DaysSinceLastPlayed == null ? "—"
                : view.DaysSinceLastPlayed == 0 ? "today"
                : view.DaysSinceLastPlayed == 1 ? "1 day" : $"{view.DaysSinceLastPlayed} days";

            int top = view.MostPlayed.Count > 0 ? Math.Max(1, view.MostPlayed[0].Plays) : 1;
            MostPlayedLabel.Visibility = view.MostPlayed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            MostPlayedList.ItemsSource = view.MostPlayed.Select((t, i) => new
            {
                Rank = (i + 1).ToString(),
                t.Title,
                Plays = t.Plays.ToString(),
                Accent = _accentBrush,
                Bar = Orange,
                Fill = new GridLength(t.Plays, GridUnitType.Star),
                Rest = new GridLength(Math.Max(0, top - t.Plays), GridUnitType.Star)
            }).ToList();
        }

        private static string FormatListened(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m";
            return $"{t.Seconds}s";
        }

        // ---- Player page ----

        private void StartPlayer(IReadOnlyList<SongEntry> songs)
        {
            _primaryPath = _host?.PrimaryPath?.Invoke(_game);
            _rows.Clear();
            foreach (var s in songs)
                _rows.Add(new SongRow { Title = s.Title, Path = s.FilePath, DurationText = s.Duration > TimeSpan.Zero ? FormatDuration(s.Duration) : string.Empty });
            SongList.ItemsSource = _rows;
            _rowPaths = _rows.Select(r => r.Path).ToList();
            SongListCountText.Text = _rows.Count > 0 ? $"({_rows.Count})" : string.Empty;

            _playingIndex = CardPlayback.PlayingIndex(_rowPaths, _host?.PlayingPath?.Invoke());
            _rules = new CardInputRules(_rows.Count, Math.Max(0, _playingIndex));

            RefreshPlayback();
            Redraw();
            ScrollSongIntoView();

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.MusicInfoCardPositionRefreshMs) };
            _refresh.Tick += (s, e) => { if (!_closed) RefreshPlayback(); };
            if (!_closed) _refresh.Start();
        }

        // What plays right now: the ring, the play/pause glyph, the toggles, the playing row and the line under the songs
        private void RefreshPlayback()
        {
            if (_host == null) { NowPlayingLine.Text = string.Empty; return; }
            var snap = _host.Snapshot?.Invoke();
            bool hasMedia = snap?.HasActiveMedia == true;
            bool playing = snap?.IsPlaying == true;

            if (!RingHeld) Ring.Progress = hasMedia ? (snap.Progress / 100.0) : 0;
            PlayGlyph.Data = (Geometry)FindResource(playing ? "IconPause" : "IconPlay");
            PlayLabel.Text = playing ? "Pause" : "Play";
            StyleToggle(CalmButton, _host.CalmDownOn?.Invoke() == true);
            StyleToggle(FxButton, _host.LiveEffectsOn?.Invoke() == true);

            int playingIndex = CardPlayback.PlayingIndex(_rowPaths, _host.PlayingPath?.Invoke());
            if (playingIndex != _playingIndex) { _playingIndex = playingIndex; RestyleRows(); }

            var (title, artist) = _host.NowPlaying?.Invoke() ?? (null, null);
            bool spotify = snap?.SourceKind == ActiveMediaSourceKind.Spotify;
            var line = CardPlayback.NowPlayingLine(hasMedia, spotify, title, artist, snap?.PositionText, snap?.DurationText);
            var shown = $"{hasMedia}|{spotify}|{_playingIndex >= 0}|{line}";
            if (shown == _shownLine) return;
            _shownLine = shown;
            NowPlayingLine.Inlines.Clear();
            if (!hasMedia) NowPlayingLine.Inlines.Add(new Run(line) { Foreground = LabelGrey });
            else if (spotify && line.StartsWith("Spotify"))
            {
                NowPlayingLine.Inlines.Add(new Run("Spotify") { Foreground = SpotifyGreen });
                NowPlayingLine.Inlines.Add(new Run(line.Substring("Spotify".Length)) { Foreground = Teal });
            }
            else
            {
                if (_playingIndex >= 0) NowPlayingLine.Inlines.Add(new Run("▶ ") { Foreground = _accentBrush });
                NowPlayingLine.Inlines.Add(new Run(line) { Foreground = Teal });
            }
        }

        // Just the knob, while it's being turned: the seeker lands the audio every 120 ms, the 250 ms refresh lagged behind it
        private void RefreshRing()
        {
            if (RingHeld) return;
            var snap = _host?.Snapshot?.Invoke();
            Ring.Progress = snap?.HasActiveMedia == true ? snap.Progress / 100.0 : 0;
        }

        private bool RingHeld => _ringScrub != null || DateTime.UtcNow < _ringHoldUntil;

        // Cursor states: the live cursor glows, the other keeps its place, faint
        private void Redraw()
        {
            RestyleRows();
            bool controlsLive = _rules?.Live == CardCursor.Controls;
            var control = _rules?.Control ?? CardControl.PlayPause;
            StyleDockButton(PrevButton, PrevLabel, control == CardControl.Previous, controlsLive);
            StyleDockButton(NextButton, NextLabel, control == CardControl.Next, controlsLive);
            StyleDockButton(CalmButton, CalmLabel, control == CardControl.CalmDown, controlsLive, keepFill: true);
            StyleDockButton(FxButton, FxLabel, control == CardControl.LiveEffects, controlsLive, keepFill: true);

            bool playLive = control == CardControl.PlayPause;
            Ring.Live = (playLive && controlsLive) || _ringHover || _ringScrub != null;
            PlayButton.Stroke = playLive && !controlsLive ? _tints.Cursor : PlayRim;
            PlayLabel.Foreground = playLive && controlsLive ? White : LabelGrey;
            JogHint.Foreground = _accentBrush;
            // Hidden, not collapsed: its line stays reserved, so the songs don't jump when it appears
            JogHint.Visibility = _rules?.RightStickJogs == true && _stick != null ? Visibility.Visible : Visibility.Hidden;
            UpdateJogPoll();
        }

        private void RestyleRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool isPlaying = _playingIndex >= 0 && CardPlayback.IsPlayingRow(row.Path, _rows[_playingIndex].Path);
                bool isPrimary = CardPlayback.IsPlayingRow(row.Path, _primaryPath);
                bool cursor = _rules != null && _rules.SongIndex == i;
                bool live = cursor && _rules.Live == CardCursor.Songs;

                row.Background = isPlaying ? _tints.Playing : RowFill;
                row.Outline = live ? _accentBrush : cursor || isPlaying ? _tints.Cursor : Transparent;
                row.OutlineThickness = new Thickness(live ? 2 : 1);
                row.TitleBrush = isPrimary ? PrimaryPink : White;
                row.Mark = isPlaying ? "▶  " : string.Empty;
                row.MarkBrush = _accentBrush;
                row.Changed();
            }
        }

        private void StyleDockButton(System.Windows.Shapes.Ellipse button, TextBlock label, bool cursor, bool controlsLive, bool keepFill = false)
        {
            bool live = cursor && controlsLive;
            button.Stroke = live ? _accentBrush : cursor ? _tints.Cursor : (keepFill ? button.Stroke : DockRim);
            button.StrokeThickness = live ? 2 : 1;
            label.Foreground = live ? White : LabelGrey;
            if (!cursor && keepFill) StyleToggle(button, button == CalmButton ? _host?.CalmDownOn?.Invoke() == true : _host?.LiveEffectsOn?.Invoke() == true);
        }

        // A toggle that's on is tinted in the accent colour
        private void StyleToggle(System.Windows.Shapes.Ellipse button, bool on)
        {
            button.Fill = on ? _tints.Fill : DockFill;
            bool cursor = (_rules?.Control == CardControl.CalmDown && button == CalmButton) || (_rules?.Control == CardControl.LiveEffects && button == FxButton);
            if (!cursor) button.Stroke = on ? _tints.ToggleRim : DockRim;
        }

        private void ScrollSongIntoView()
        {
            if (_rules == null || _rows.Count == 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var container = SongList.ItemContainerGenerator.ContainerFromIndex(_rules.SongIndex) as FrameworkElement;
                container?.BringIntoView();
            }), DispatcherPriority.Loaded);
        }

        // ---- Pages ----

        private void ShowPage()
        {
            bool details = _rules?.Page == CardPage.Details;
            PlayerPage.Visibility = details ? Visibility.Collapsed : Visibility.Visible;
            DetailsScroll.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            PlayerFooter.Visibility = details ? Visibility.Collapsed : Visibility.Visible;
            DetailsFooter.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
            StyleTab(PlayerTab, PlayerTabText, !details);
            StyleTab(DetailsTab, DetailsTabText, details);
        }

        private void StyleTab(Border tab, TextBlock text, bool on)
        {
            tab.Background = on ? _tints.Fill : TabIdle;
            tab.BorderBrush = on ? _tints.TabRim : Transparent;
            text.Foreground = on ? White : LabelGrey;
        }

        // ---- Input ----

        // The router already calls on the UI thread; queueing again let a press land after the card had closed
        public void OnControllerButtonPressed(ControllerInput button) => HandleInput(button);

        public void OnControllerButtonReleased(ControllerInput button)
        {
            if (button == ControllerInput.TriggerLeft || button == ControllerInput.TriggerRight) EndSeek();
        }

        // Keyboard (CardKeyMap): arrows, Enter, Esc, Q/E pages, [ ] seek, Space plays/pauses
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                if (!_closed && _rules != null && !e.IsRepeat) PressControl(CardControl.PlayPause);
                return;
            }
            var input = CardKeyMap.ToInput(e.Key);
            if (input == null) return;
            e.Handled = true; // the card drives scrolling, not the ScrollViewers' own key handling
            HandleInput(input.Value);
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.OemOpenBrackets || e.Key == Key.OemCloseBrackets) EndSeek();
        }

        private void HandleInput(ControllerInput button)
        {
            if (_closed) return;
            if (button == ControllerInput.B || button == ControllerInput.Back) { CloseDialog(); return; }
            if (_rules == null) return; // the songs aren't known yet

            if (IsDirection(button))
            {
                var now = DateTime.Now;
                if ((now - _lastDirectionTime).TotalMilliseconds < Constants.MusicInfoCardDirectionDebounceMs) return;
                _lastDirectionTime = now;
            }

            var pageBefore = _rules.Page;
            var command = _rules.Press(button);
            switch (command.Kind)
            {
                case CardCommandKind.Redraw:
                    if (_rules.Page != pageBefore) ShowPage();
                    Redraw();
                    if (_rules.Live == CardCursor.Songs) ScrollSongIntoView();
                    break;
                case CardCommandKind.PlaySong:
                    PlaySong(_rows[command.SongIndex].Path);
                    break;
                case CardCommandKind.Press:
                    PressControl(command.Control);
                    break;
                case CardCommandKind.Seek:
                    // A held trigger is resent by Playnite (and a held key by Windows); the card's own repeat paces it
                    if (_held.Begin(command.SeekSeconds))
                    {
                        Seek(command.SeekSeconds);
                        StartSeekRepeat(command.SeekSeconds);
                    }
                    break;
                case CardCommandKind.Close:
                    CloseDialog();
                    break;
            }
        }

        private static bool IsDirection(ControllerInput b) =>
            b == ControllerInput.DPadUp || b == ControllerInput.DPadDown || b == ControllerInput.DPadLeft || b == ControllerInput.DPadRight ||
            b == ControllerInput.LeftStickUp || b == ControllerInput.LeftStickDown || b == ControllerInput.LeftStickLeft || b == ControllerInput.LeftStickRight;

        private void PlaySong(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            _playedSong = true;
            if (_host?.PlaySong != null) _host.PlaySong(_game, path);
            else _playSong?.Invoke(path);
            RefreshPlayback();
        }

        private void PressControl(CardControl control)
        {
            if (_host == null) return;
            switch (control)
            {
                case CardControl.Previous: _host.Previous?.Invoke(); break;
                case CardControl.Next: _host.Next?.Invoke(); break;
                case CardControl.PlayPause:
                    // Nothing playing: Play starts this game's music, the primary song first
                    if (_host.Snapshot?.Invoke()?.HasActiveMedia != true)
                        PlaySong(CardPlayback.StartSong(_rowPaths, _primaryPath));
                    else
                        _host.PlayPause?.Invoke();
                    break;
                case CardControl.CalmDown: _host.ToggleCalmDown?.Invoke(); break;
                case CardControl.LiveEffects: _host.ToggleLiveEffects?.Invoke(); break;
            }
            RefreshPlayback();
            Redraw();
        }

        private void Seek(double seconds)
        {
            _host?.SeekBy?.Invoke(seconds);
            RefreshPlayback();
        }

        // LT/RT held: one more step every MusicInfoCardSeekRepeatMs, after a short first wait, until released
        private void StartSeekRepeat(double seconds)
        {
            if (_closed) return;
            _repeatSeconds = seconds;
            if (_seekRepeat == null)
            {
                _seekRepeat = new DispatcherTimer();
                _seekRepeat.Tick += (s, e) =>
                {
                    if (_closed) { _seekRepeat.Stop(); return; }
                    _seekRepeat.Interval = TimeSpan.FromMilliseconds(Constants.MusicInfoCardSeekRepeatMs);
                    Seek(_repeatSeconds);
                };
            }
            _seekRepeat.Stop();
            _seekRepeat.Interval = TimeSpan.FromMilliseconds(Constants.MusicInfoCardSeekRepeatMs * 2.5);
            _seekRepeat.Start();
        }

        private void EndSeek()
        {
            _held.End();
            _seekRepeat?.Stop();
        }

        // ---- Mouse: a click selects what it hits and acts, as the D-pad and A would ----

        private void HookMouse()
        {
            SongList.Cursor = Cursors.Hand;
            SongList.MouseLeftButtonUp += (s, e) =>
            {
                if (_closed || _rules == null) return;
                var container = ItemsControl.ContainerFromElement(SongList, e.OriginalSource as DependencyObject);
                int index = container == null ? -1 : SongList.ItemContainerGenerator.IndexFromContainer(container);
                if (index < 0 || index >= _rows.Count) return;
                _rules.SelectSong(index);
                Redraw();
                PlaySong(_rows[index].Path);
            };

            HookDockClick(PrevButton, CardControl.Previous);
            HookDockClick(NextButton, CardControl.Next);
            HookDockClick(CalmButton, CardControl.CalmDown);
            HookDockClick(FxButton, CardControl.LiveEffects);
            HookTab(PlayerTab, CardPage.Player);
            HookTab(DetailsTab, CardPage.Details);

            // Play/Pause: the button in the middle plays/pauses; the ring around it is the song - press or drag on it and
            // the knob goes where the pointer is (RingSeek). Hovering shows the knob.
            RingSlot.Cursor = Cursors.Hand;
            RingSlot.Background = Transparent; // hit-testable across the whole ring, not just the drawn parts
            RingSlot.MouseEnter += (s, e) => { _ringHover = true; Redraw(); };
            RingSlot.MouseLeave += (s, e) => { _ringHover = false; Redraw(); };
            RingSlot.MouseLeftButtonDown += (s, e) =>
            {
                if (_closed || _rules == null) return;
                _rules.SelectControl(CardControl.PlayPause);
                var (x, y) = FromRingCentre(e.GetPosition(RingSlot));
                if (Math.Sqrt(x * x + y * y) <= PlayButton.ActualWidth / 2) _ringButtonDown = true;
                else if (_host?.SeekTo != null && _host.Snapshot?.Invoke()?.HasActiveMedia == true) ScrubTo(RingSeek.FractionAt(x, y, null), force: true);
                else return;
                Redraw();
                RingSlot.CaptureMouse();
                e.Handled = true;
            };
            RingSlot.MouseMove += (s, e) =>
            {
                if (_ringScrub == null || !RingSlot.IsMouseCaptured) return;
                var (x, y) = FromRingCentre(e.GetPosition(RingSlot));
                ScrubTo(RingSeek.FractionAt(x, y, _ringScrub), force: false);
            };
            RingSlot.MouseLeftButtonUp += (s, e) =>
            {
                bool pressed = _ringButtonDown;
                _ringButtonDown = false;
                if (_ringScrub != null)
                {
                    ScrubTo(_ringScrub.Value, force: true); // land exactly where the knob was let go
                    _ringScrub = null;
                    _ringHoldUntil = DateTime.UtcNow.AddMilliseconds(Constants.MusicInfoCardScrubSettleMs);
                    Redraw();
                }
                RingSlot.ReleaseMouseCapture();
                if (pressed && !_closed) PressControl(CardControl.PlayPause);
            };
        }

        private (double X, double Y) FromRingCentre(Point p) => (p.X - RingSlot.ActualWidth / 2, p.Y - RingSlot.ActualHeight / 2);

        // The knob follows the hand at once; the audio follows at most every MusicInfoCardScrubSeekMs, and exactly on release
        private void ScrubTo(double fraction, bool force)
        {
            _ringScrub = fraction;
            Ring.Progress = fraction;
            var now = DateTime.UtcNow;
            if (!force && (now - _lastScrubSeek).TotalMilliseconds < Constants.MusicInfoCardScrubSeekMs) return;
            _lastScrubSeek = now;
            _host?.SeekTo?.Invoke(fraction * 100);
        }

        // The whole column (button, icon and label) is the click target
        private void HookDockClick(System.Windows.Shapes.Ellipse button, CardControl control)
        {
            var column = ((FrameworkElement)button.Parent).Parent as FrameworkElement ?? button;
            column.Cursor = Cursors.Hand;
            column.MouseLeftButtonUp += (s, e) =>
            {
                if (_closed || _rules == null) return;
                _rules.SelectControl(control);
                PressControl(control);
            };
        }

        private void HookTab(Border tab, CardPage page)
        {
            tab.Cursor = Cursors.Hand;
            tab.MouseLeftButtonUp += (s, e) =>
            {
                if (_closed || _rules == null || _rules.Page == page) return;
                _rules.SelectPage(page);
                ShowPage();
                Redraw();
            };
        }

        // ---- Right stick: jog wheel on Play/Pause, scrolling everywhere else ----

        private void UpdateJogPoll()
        {
            bool jog = _rules?.RightStickJogs == true && _stick != null && _jogSeeker != null;
            if (jog && _jogPoll == null)
            {
                _jogPoll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
                _jogPoll.Tick += (s, e) => PollJog();
            }
            if (jog) { StopStickFrames(); _jogPoll.Start(); }
            else if (_jogPoll != null) { _jogPoll.Stop(); _jogAngle = null; _jogSeeker?.Flush(); }
        }

        // As SeekDisc: circling the stick turns the knob; clockwise is forward; near the centre the angle is noise
        private void PollJog()
        {
            var (x, y) = _stick();
            if (Math.Sqrt(x * x + y * y) < SeekDiscMath.StickDeadzone)
            {
                if (_jogAngle != null) { _jogAngle = null; _jogSeeker.Flush(); RefreshPlayback(); }
                return;
            }
            double angle = SeekDiscMath.AngleDegrees(x, y);
            if (_jogAngle != null)
            {
                double degrees = SeekDiscMath.WrapDegrees(angle - _jogAngle.Value);
                if (degrees != 0 && _jogSeeker.CanSeek) { _jogSeeker.Add(SeekDiscMath.SecondsForDegrees(degrees)); RefreshRing(); }
            }
            _jogAngle = angle;
        }

        // Right-stick scrolling (the song list on Player, the page on Details). A slow check (50 ms) waits for a tilt; while
        // tilted, scrolling runs on WPF's frame clock so it moves once per drawn frame. Squared response: a light tilt
        // creeps, a full tilt flies. Stands down while the stick is the jog wheel.
        private void StartStickScroll()
        {
            if (_stick == null) return;
            _stickPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _stickPoll.Tick += (s, e) =>
            {
                if (_rules?.RightStickJogs == true) return;
                if (!_stickScrolling && Math.Abs(_stick().Y) >= Constants.MusicInfoCardStickDeadzone) StartStickFrames();
            };
            _stickPoll.Start();
        }

        private void StartStickFrames()
        {
            _stickScrolling = true;
            _lastFrameTime = TimeSpan.Zero;
            _stickVelocity = 0;
            CompositionTarget.Rendering += OnStickFrame;
        }

        private void StopStickFrames()
        {
            if (!_stickScrolling) return;
            _stickScrolling = false;
            CompositionTarget.Rendering -= OnStickFrame;
        }

        private void OnStickFrame(object sender, EventArgs e)
        {
            // RenderingTime repeats when WPF raises the event twice for one frame; only move on a new frame.
            var frameTime = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            if (frameTime == _lastFrameTime) return;
            double dt = _lastFrameTime == TimeSpan.Zero ? 1.0 / 60 : Math.Min(0.1, (frameTime - _lastFrameTime).TotalSeconds);
            _lastFrameTime = frameTime;

            double y = _stick().Y;
            double a = Math.Abs(y);
            double target = 0;
            if (a >= Constants.MusicInfoCardStickDeadzone)
            {
                double t = (a - Constants.MusicInfoCardStickDeadzone) / (1 - Constants.MusicInfoCardStickDeadzone);
                target = Math.Sign(y) * t * t * Constants.MusicInfoCardStickPixelsPerSecond;
            }
            _stickVelocity += (target - _stickVelocity) * Math.Min(1, dt * 12);

            if (target == 0 && Math.Abs(_stickVelocity) < 5) { StopStickFrames(); return; }
            var scroll = _rules?.Page == CardPage.Details ? DetailsScroll : SongScroll;
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + _stickVelocity * dt);
        }

        // ---- Helpers ----

        // Fades the populated pages in. PagesRoot sits at Opacity 0.01, not 0: WPF skips drawing fully transparent
        // content, so from 0 the fade's first frame had to draw every row at once. (Smooth only because the card's window
        // is opaque; see MusicInfoCardHandler.Show.)
        private void FadeInPages()
        {
            int frames = 0;
            EventHandler onFrame = null;
            onFrame = (s, e) =>
            {
                if (++frames < 2) return;
                CompositionTarget.Rendering -= onFrame;
                PagesRoot.BeginAnimation(OpacityProperty, new DoubleAnimation
                {
                    From = 0.01, To = 1, Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            };
            CompositionTarget.Rendering += onFrame;
        }

        private void CloseDialog() => Window.GetWindow(this)?.Close();

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static SolidColorBrush WithOpacity(SolidColorBrush brush, double opacity)
            => CreateFrozenBrush(Color.FromArgb((byte)(opacity * 255), brush.Color.R, brush.Color.G, brush.Color.B));

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            return $"{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // The accent colour's see-through shades, by what they colour
        private sealed class AccentTints
        {
            public readonly SolidColorBrush Playing, Fill, Cursor, TabRim, ToggleRim;

            public AccentTints(SolidColorBrush accent)
            {
                Playing = WithOpacity(accent, 0.18);   // the playing row
                Fill = WithOpacity(accent, 0.25);      // a toggle that's on, the current tab
                Cursor = WithOpacity(accent, 0.45);    // a cursor that isn't live; the playing row's outline
                TabRim = WithOpacity(accent, 0.6);
                ToggleRim = WithOpacity(accent, 0.7);
            }
        }

        // One song row's look, set from code (see RestyleRows)
        public sealed class SongRow : INotifyPropertyChanged
        {
            public string Title { get; set; }
            public string Path { get; set; }
            public string DurationText { get; set; }
            public Brush Background { get; set; }
            public Brush Outline { get; set; }
            public Thickness OutlineThickness { get; set; }
            public Brush TitleBrush { get; set; }
            public string Mark { get; set; }
            public Brush MarkBrush { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
