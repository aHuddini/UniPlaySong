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
        private const int LoaderGraceMs = 200;

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
        private const double StickDeadzone = 0.25;
        private const double StickPixelsPerSecond = 1400; // at full tilt

        private CardInputRules _rules;                   // null until the songs are known
        private readonly List<SongRow> _rows = new List<SongRow>();
        private DateTime _lastDirectionTime;
        private const int DirectionDebounceMs = 150;     // a D-pad press arrives twice: controller event + arrow key
        private DispatcherTimer _refresh;                // ring, glyph, toggles, playing row, line
        private DispatcherTimer _seekRepeat;             // LT/RT held
        private double _repeatSeconds;
        private DispatcherTimer _jogPoll;                // right stick as a jog wheel while Play/Pause is live
        private double? _jogAngle;
        private RelativeSeeker _jogSeeker;
        private int _playingIndex = -1;
        private readonly SeekRing Ring = new SeekRing { Width = 64, Height = 64 };

        private string _imagePath;
        private IconColorExtractor _colorExtractor;
        private SolidColorBrush _accentBrush = CreateFrozenBrush(Color.FromRgb(0x7E, 0x57, 0xC2));

        private static readonly SolidColorBrush White = CreateFrozenBrush(Colors.White);
        private static readonly SolidColorBrush Transparent = CreateFrozenBrush(Colors.Transparent);
        private static readonly SolidColorBrush PrimaryPink = CreateFrozenBrush(Color.FromRgb(0xE9, 0xA8, 0xF2));
        private static readonly SolidColorBrush Teal = CreateFrozenBrush(Color.FromRgb(0x1F, 0xD8, 0xC1));
        private static readonly SolidColorBrush SpotifyGreen = CreateFrozenBrush(Color.FromRgb(0x1D, 0xB9, 0x54));
        private static readonly SolidColorBrush LabelGrey = CreateFrozenBrush(Color.FromRgb(0xA9, 0xA2, 0x9D));
        private static readonly SolidColorBrush DockFill = CreateFrozenBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush DockRim = CreateFrozenBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush TabIdle = CreateFrozenBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));

        public ControllerMusicInfoCardDialog()
        {
            InitializeComponent();
            RingSlot.Children.Insert(0, Ring);

            Loaded += (s, e) =>
            {
                Focus();
                Keyboard.Focus(this);
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
                _stickPoll?.Stop();
                StopStickFrames();
                _refresh?.Stop();
                _seekRepeat?.Stop();
                _jogPoll?.Stop();
                _jogSeeker?.Flush();
                try { _cts?.Cancel(); } catch { /* best effort */ }
                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Unregister(this);
                }
            };

            PreviewKeyDown += OnKeyDown;
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

        private void ApplyAccentColor(Color color)
        {
            _accentBrush = CreateFrozenBrush(color);
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
                await System.Threading.Tasks.Task.Delay(LoaderGraceMs).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (LoadingPanel != null && PagesRoot.Opacity < 0.5) LoadingPanel.Visibility = Visibility.Visible;
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
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    EmptyStateText.Text = "Failed to read music metadata.";
                    EmptyStateText.Visibility = Visibility.Visible;
                    FadeInPages();
                }));
            }
        }

        private void RenderStats(MusicStats stats)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;

            if (stats == null || stats.FileCount == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
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

        // Details page (Task 5 replaces these tiles with charts)
        private void RenderDetails(MusicStats stats)
        {
            if (stats.PlaylistFileCount > 0)
            {
                PlaylistInfoText.Text =
                    $"{stats.PlaylistFileCount} playlist file" + (stats.PlaylistFileCount == 1 ? "" : "s") +
                    $" containing {stats.PlaylistTrackCount} track" + (stats.PlaylistTrackCount == 1 ? "" : "s") + ".";
                PlaylistPanel.Visibility = Visibility.Visible;
            }
            if (stats.LongestTrack.HasValue)
            {
                LongestTitleText.Text = stats.LongestTrack.Value.Title;
                LongestDurationText.Text = FormatDuration(stats.LongestTrack.Value.Duration);
            }
            if (stats.ShortestTrack.HasValue)
            {
                ShortestTitleText.Text = stats.ShortestTrack.Value.Title;
                ShortestDurationText.Text = FormatDuration(stats.ShortestTrack.Value.Duration);
            }
            var totalFiles = stats.FileCount;
            FormatList.ItemsSource = stats.FormatBreakdown
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new
                {
                    Extension = kv.Key,
                    Count = kv.Value,
                    Percentage = totalFiles > 0 ? $"({(int)(kv.Value * 100.0 / totalFiles)}%)" : string.Empty
                })
                .ToList();
            if (stats.UnreadableCount > 0)
            {
                UnreadableText.Text = $"⚠ {stats.UnreadableCount} file" + (stats.UnreadableCount == 1 ? "" : "s") + " could not be read.";
                UnreadablePanel.Visibility = Visibility.Visible;
            }
        }

        // ---- Player page ----

        private void StartPlayer(IReadOnlyList<SongEntry> songs)
        {
            _rows.Clear();
            foreach (var s in songs)
                _rows.Add(new SongRow { Title = s.Title, Path = s.FilePath, DurationText = s.Duration > TimeSpan.Zero ? FormatDuration(s.Duration) : string.Empty });
            SongList.ItemsSource = _rows;
            SongListCountText.Text = _rows.Count > 0 ? $"({_rows.Count})" : string.Empty;

            _playingIndex = CardPlayback.PlayingIndex(_rows.Select(r => r.Path).ToList(), _host?.PlayingPath?.Invoke());
            _rules = new CardInputRules(_rows.Count, Math.Max(0, _playingIndex));

            RefreshPlayback();
            Redraw();
            ScrollSongIntoView();

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.MusicInfoCardPositionRefreshMs) };
            _refresh.Tick += (s, e) => RefreshPlayback();
            _refresh.Start();
        }

        // What plays right now: the ring, the play/pause glyph, the toggles, the playing row and the line under the songs
        private void RefreshPlayback()
        {
            if (_host == null) { NowPlayingLine.Text = string.Empty; return; }
            var snap = _host.Snapshot?.Invoke();
            bool hasMedia = snap?.HasActiveMedia == true;
            bool playing = snap?.IsPlaying == true;

            Ring.Progress = hasMedia ? (snap.Progress / 100.0) : 0;
            PlayGlyph.Data = (Geometry)FindResource(playing ? "IconPause" : "IconPlay");
            PlayLabel.Text = playing ? "Pause" : "Play";
            StyleToggle(CalmButton, _host.CalmDownOn?.Invoke() == true);
            StyleToggle(FxButton, _host.LiveEffectsOn?.Invoke() == true);

            int playingIndex = CardPlayback.PlayingIndex(_rows.Select(r => r.Path).ToList(), _host.PlayingPath?.Invoke());
            if (playingIndex != _playingIndex) { _playingIndex = playingIndex; RestyleRows(); }

            var (title, artist) = _host.NowPlaying?.Invoke() ?? (null, null);
            bool spotify = snap?.SourceKind == ActiveMediaSourceKind.Spotify;
            var line = CardPlayback.NowPlayingLine(hasMedia, spotify, title, artist, snap?.PositionText, snap?.DurationText);
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
            Ring.Live = playLive && controlsLive;
            PlayButton.Stroke = playLive && !controlsLive ? WithOpacity(_accentBrush, 0.45) : CreateFrozenBrush(Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF));
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
                bool isPrimary = _host != null && CardPlayback.IsPlayingRow(row.Path, _host.PrimaryPath?.Invoke(_game));
                bool cursor = _rules != null && _rules.SongIndex == i;
                bool live = cursor && _rules.Live == CardCursor.Songs;

                row.Background = isPlaying ? WithOpacity(_accentBrush, 0.18) : CreateFrozenBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
                row.Outline = live ? _accentBrush : cursor ? WithOpacity(_accentBrush, 0.45) : isPlaying ? WithOpacity(_accentBrush, 0.45) : Transparent;
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
            button.Stroke = live ? _accentBrush : cursor ? WithOpacity(_accentBrush, 0.45) : (keepFill ? button.Stroke : DockRim);
            button.StrokeThickness = live ? 2 : 1;
            label.Foreground = live ? White : LabelGrey;
            if (!cursor && keepFill) StyleToggle(button, button == CalmButton ? _host?.CalmDownOn?.Invoke() == true : _host?.LiveEffectsOn?.Invoke() == true);
        }

        // A toggle that's on is tinted in the accent colour
        private void StyleToggle(System.Windows.Shapes.Ellipse button, bool on)
        {
            button.Fill = on ? WithOpacity(_accentBrush, 0.25) : DockFill;
            bool cursor = (_rules?.Control == CardControl.CalmDown && button == CalmButton) || (_rules?.Control == CardControl.LiveEffects && button == FxButton);
            if (!cursor) button.Stroke = on ? WithOpacity(_accentBrush, 0.7) : DockRim;
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
            tab.Background = on ? WithOpacity(_accentBrush, 0.25) : TabIdle;
            tab.BorderBrush = on ? WithOpacity(_accentBrush, 0.6) : Transparent;
            text.Foreground = on ? White : LabelGrey;
        }

        // ---- Input ----

        public void OnControllerButtonPressed(ControllerInput button)
            => Dispatcher.BeginInvoke(new Action(() => HandleInput(button)));

        public void OnControllerButtonReleased(ControllerInput button)
        {
            if (button == ControllerInput.TriggerLeft || button == ControllerInput.TriggerRight)
                Dispatcher.BeginInvoke(new Action(() => _seekRepeat?.Stop()));
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            ControllerInput input;
            switch (e.Key)
            {
                case Key.Escape: input = ControllerInput.B; break;
                case Key.Up: input = ControllerInput.DPadUp; break;
                case Key.Down: input = ControllerInput.DPadDown; break;
                case Key.Left: input = ControllerInput.DPadLeft; break;
                case Key.Right: input = ControllerInput.DPadRight; break;
                case Key.Enter: input = ControllerInput.A; break;
                case Key.PageUp: input = ControllerInput.LeftShoulder; break;
                case Key.PageDown: input = ControllerInput.RightShoulder; break;
                default: return;
            }
            e.Handled = true; // the card drives scrolling, not the ScrollViewers' own key handling
            HandleInput(input);
        }

        private void HandleInput(ControllerInput button)
        {
            if (button == ControllerInput.B || button == ControllerInput.Back) { CloseDialog(); return; }
            if (_rules == null) return; // the songs aren't known yet

            if (IsDirection(button))
            {
                var now = DateTime.Now;
                if ((now - _lastDirectionTime).TotalMilliseconds < DirectionDebounceMs) return;
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
                    Seek(command.SeekSeconds);
                    StartSeekRepeat(command.SeekSeconds);
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
                        PlaySong(CardPlayback.StartSong(_rows.Select(r => r.Path).ToList(), _host.PrimaryPath?.Invoke(_game)));
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
            _repeatSeconds = seconds;
            if (_seekRepeat == null)
            {
                _seekRepeat = new DispatcherTimer();
                _seekRepeat.Tick += (s, e) =>
                {
                    _seekRepeat.Interval = TimeSpan.FromMilliseconds(Constants.MusicInfoCardSeekRepeatMs);
                    Seek(_repeatSeconds);
                };
            }
            _seekRepeat.Stop();
            _seekRepeat.Interval = TimeSpan.FromMilliseconds(Constants.MusicInfoCardSeekRepeatMs * 2.5);
            _seekRepeat.Start();
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
                if (degrees != 0 && _jogSeeker.CanSeek) _jogSeeker.Add(SeekDiscMath.SecondsForDegrees(degrees));
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
                if (!_stickScrolling && Math.Abs(_stick().Y) >= StickDeadzone) StartStickFrames();
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
            if (a >= StickDeadzone)
            {
                double t = (a - StickDeadzone) / (1 - StickDeadzone);
                target = Math.Sign(y) * t * t * StickPixelsPerSecond;
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
