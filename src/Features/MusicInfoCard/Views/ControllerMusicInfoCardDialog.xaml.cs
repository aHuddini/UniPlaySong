using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using UniPlaySong.Features.MusicInfoCard.Models;
using UniPlaySong.Features.MusicInfoCard.Services;
using UniPlaySong.IconGlow;
using UniPlaySong.Services.Controller;

namespace UniPlaySong.Features.MusicInfoCard.Views
{
    // Fullscreen / controller-friendly Music Info Card. Same data as the
    // Desktop version but with larger fonts, focused-on-load StatsScroll
    // for D-pad scrolling, and B-button-to-close via ControllerEventRouter.
    //
    // Read-only — no inner navigation logic. The ScrollViewer accepts
    // D-pad scrolling natively, so the only controller input we handle
    // explicitly is the close button (B / Back / Escape).
    public partial class ControllerMusicInfoCardDialog : UserControl, IControllerInputReceiver
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();
        private const int LoaderGraceMs = 200;

        private CancellationTokenSource _cts;
        private Game _game;
        private IMusicStatsProvider _statsProvider;
        private Action<string> _playSong; // set by the handler; A on a highlighted song row plays that file
        private BitmapSource _frost; // the glass tiles' extra-blurred backdrop (GlassTiles)

        // Right stick scrolls the card: vertical tilt, -1..1 (down positive); null on a Playnite without analog input.
        private Func<double> _stickY;
        private System.Windows.Threading.DispatcherTimer _stickPoll; // slow idle check for a tilt
        private bool _stickScrolling;      // frame-synced scrolling hooked while the stick is tilted
        private TimeSpan _lastFrameTime;
        private double _stickVelocity;     // px/s, eased toward the stick so reading noise doesn't jolt
        private const double StickDeadzone = 0.25;
        private const double StickPixelsPerSecond = 1400; // at full tilt

        // Song-row highlight for the controller. -1 = none (the stats above the list are in view).
        private int _selectedRow = -1;
        private DateTime _lastDpadNavigationTime;
        private const int DpadDebounceMs = 150; // a D-pad press arrives twice: controller event + arrow key
        private static readonly SolidColorBrush RowBrush = CreateFrozenBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush SelectedRowBrush = CreateFrozenBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        private string _imagePath;
        private IconColorExtractor _colorExtractor;
        private SolidColorBrush _accentBrush;

        public ControllerMusicInfoCardDialog()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                // Focus the scroll viewer so D-pad up/down scrolls the content
                // when stats overflow the visible area.
                StatsScroll.Focus();
                Keyboard.Focus(StatsScroll);
                GlassTiles.Attach(BlurredBackdrop, StatsPanel, StatsScroll, _frost);
                StartStickScroll();

                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Register(this);
                }
            };

            Unloaded += (s, e) =>
            {
                _stickPoll?.Stop();
                StopStickFrames();
                try { _cts?.Cancel(); } catch { /* best effort */ }
                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Unregister(this);
                }
            };

            PreviewKeyDown += OnKeyDown;
        }

        public void Initialize(
            Game game,
            IMusicStatsProvider statsProvider,
            string imagePath,
            IconColorExtractor colorExtractor,
            BitmapSource backdropSnapshot,
            Action<string> playSong = null,
            BitmapSource frost = null,
            Func<double> stickY = null)
        {
            _game = game;
            _playSong = playSong;
            _frost = frost;
            _stickY = stickY;
            _statsProvider = statsProvider;
            _imagePath = imagePath;
            _colorExtractor = colorExtractor;
            GameNameText.Text = game?.Name ?? "Unknown Game";

            // See MusicInfoCardDialog for the snapshot pattern.
            if (backdropSnapshot != null)
            {
                BlurredBackdrop.Source = backdropSnapshot;
            }

            TryApplyGameIconAndAccent();

            Loaded += OnDialogLoadedOnce;
        }

        private void TryApplyGameIconAndAccent()
        {
            try
            {
                if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath))
                    return;

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
            _accentBrush = new SolidColorBrush(color);
            _accentBrush.Freeze();
            AccentStrip.Background = _accentBrush;
            HeaderSubtitle.Foreground = _accentBrush;
            IconGlowEffect.Color = color;

            // Whole-card tint at ~5% alpha — see Desktop dialog.
            // Bumped to pick up the slack from the dimmer backdrop.
            var tintColor = Color.FromArgb(13, color.R, color.G, color.B);
            var tintBrush = new SolidColorBrush(tintColor);
            tintBrush.Freeze();
            CardTintLayer.Background = tintBrush;
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
                    if (LoadingPanel != null && StatsPanel?.IsVisible == false)
                        LoadingPanel.Visibility = Visibility.Visible;
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
                }));
            }
        }

        private void RenderStats(MusicStats stats)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;

            if (stats == null || stats.FileCount == 0)
            {
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            FileCountText.Text = stats.FileCount.ToString();
            TotalDurationText.Text = FormatDuration(stats.TotalDuration);
            TotalSizeText.Text = FormatSize(stats.TotalSizeBytes);

            if (stats.PlaylistFileCount > 0)
            {
                PlaylistInfoText.Text =
                    $"{stats.PlaylistFileCount} playlist file" +
                    (stats.PlaylistFileCount == 1 ? "" : "s") +
                    $" containing {stats.PlaylistTrackCount} track" +
                    (stats.PlaylistTrackCount == 1 ? "" : "s") + ".";
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
            var rows = stats.FormatBreakdown
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new
                {
                    Extension = kv.Key,
                    Count = kv.Value,
                    Percentage = totalFiles > 0
                        ? $"({(int)(kv.Value * 100.0 / totalFiles)}%)"
                        : string.Empty
                })
                .ToList();
            FormatList.ItemsSource = rows;

            if (stats.UnreadableCount > 0)
            {
                UnreadableText.Text =
                    $"⚠ {stats.UnreadableCount} file" +
                    (stats.UnreadableCount == 1 ? "" : "s") +
                    " could not be read.";
                UnreadablePanel.Visibility = Visibility.Visible;
            }

            RenderSongList(stats);

            FadeInStatsPanel();
        }

        // Playlist tracks always use the fixed amber chip so they remain visually distinguishable regardless of
        // per-game accent color. Standard files use the icon-derived accent when set, else the fallback purple.
        private static readonly SolidColorBrush PlaylistChipBrush =
            CreateFrozenBrush(0xFF, 0xB7, 0x4D);
        private static readonly SolidColorBrush DefaultStandardChipBrush =
            CreateFrozenBrush(0x7E, 0x57, 0xC2);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b) => CreateFrozenBrush(Color.FromRgb(r, g, b));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // Same projection as the Desktop dialog. Kept duplicated rather than shared because the row anonymous types are
        // dialog-private and the chip backgrounds differ by font size only — extracting them into a shared helper
        // would force a real ViewModel type and isn't worth the indirection for two short methods.
        private void RenderSongList(MusicStats stats)
        {
            if (stats.Songs == null || stats.Songs.Count == 0) return;

            var standardBrush = _accentBrush ?? DefaultStandardChipBrush;

            var rows = stats.Songs.Select(s => new
            {
                Title = s.Title,
                FilePath = s.FilePath,
                ChipText = s.IsPlaylistTrack
                    ? "TRACK"
                    : (s.Extension ?? string.Empty).TrimStart('.').ToUpperInvariant(),
                ChipBackground = s.IsPlaylistTrack ? PlaylistChipBrush : standardBrush,
                SizeText = FormatSize(s.FileSizeBytes),
                BitrateText = s.BitrateKbps > 0 ? $"{s.BitrateKbps} kbps" : string.Empty,
                DurationText = s.Duration > TimeSpan.Zero
                    ? FormatDuration(s.Duration)
                    : string.Empty
            }).ToList();

            SongList.ItemsSource = rows;
            SongListCountText.Text = $"({stats.Songs.Count})";
            SongListPanel.Visibility = Visibility.Visible;
        }

        // Fades the populated stats in. StatsPanel sits at Opacity 0.01, not 0: WPF skips drawing fully transparent
        // content, so from 0 the fade's first frame had to draw every row at once. At 0.01 the rows are drawn,
        // invisibly, as soon as they are filled in, and the fade starts two frames later. (Smooth only because the
        // card's window is opaque; see MusicInfoCardHandler.Show.)
        private void FadeInStatsPanel()
        {
            int frames = 0;
            EventHandler onFrame = null;
            onFrame = (s, e) =>
            {
                if (++frames < 2) return;
                CompositionTarget.Rendering -= onFrame;
                var anim = new DoubleAnimation
                {
                    From = 0.01,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                StatsPanel.BeginAnimation(OpacityProperty, anim);
            };
            CompositionTarget.Rendering += onFrame;
        }

        public void OnControllerButtonPressed(ControllerInput button)
        {
            // B = Cancel/Back on Xbox-style layout; Back is the dedicated
            // back button on most controllers. Both close the dialog.
            if (button == ControllerInput.B || button == ControllerInput.Back)
            {
                Dispatcher.BeginInvoke(new Action(CloseDialog));
            }
            else if (button == ControllerInput.DPadUp || button == ControllerInput.DPadDown)
            {
                var step = button == ControllerInput.DPadDown ? 1 : -1;
                Dispatcher.BeginInvoke(new Action(() => MoveSelection(step)));
            }
            else if (button == ControllerInput.A)
            {
                Dispatcher.BeginInvoke(new Action(PlaySelected));
            }
        }

        public void OnControllerButtonReleased(ControllerInput button)
        {
            // Read-only dialog — no release-edge logic needed.
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Escape mirrors the controller B-button so keyboard users in
            // Fullscreen mode can dismiss without picking up a controller.
            if (e.Key == Key.Escape)
            {
                CloseDialog();
                e.Handled = true;
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                MoveSelection(e.Key == Key.Down ? 1 : -1);
                e.Handled = true; // the rows drive scrolling now, not the ScrollViewer's own arrow handling
            }
            else if (e.Key == Key.Enter)
            {
                PlaySelected();
                e.Handled = true;
            }
        }

        // Moves the song-row highlight. Up from the first row clears it and returns to the top, so the stats above
        // the list stay reachable; the highlighted row is scrolled into view.
        private void MoveSelection(int step)
        {
            var now = DateTime.Now;
            if ((now - _lastDpadNavigationTime).TotalMilliseconds < DpadDebounceMs) return;
            _lastDpadNavigationTime = now;

            var count = SongList.Items.Count;
            if (count == 0) return;

            var next = Math.Max(-1, Math.Min(count - 1, _selectedRow + step));
            if (next == _selectedRow) return;

            SetRowHighlight(_selectedRow, false);
            _selectedRow = next;
            if (_selectedRow < 0)
            {
                StatsScroll.ScrollToTop();
                return;
            }
            var row = SetRowHighlight(_selectedRow, true);
            row?.BringIntoView();
        }

        private void PlaySelected()
        {
            if (_selectedRow < 0) return;
            var row = RowBorder(_selectedRow);
            if (row?.Tag is string path) _playSong?.Invoke(path);
        }

        private Border SetRowHighlight(int index, bool on)
        {
            var row = RowBorder(index);
            if (row != null) row.Background = on ? SelectedRowBrush : RowBrush;
            return row;
        }

        // The row template's root Border for an item (null while not generated).
        private Border RowBorder(int index)
        {
            if (index < 0) return null;
            var container = SongList.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
            return container != null && VisualTreeHelper.GetChildrenCount(container) > 0
                ? VisualTreeHelper.GetChild(container, 0) as Border
                : null;
        }

        // Right-stick scrolling. A slow check (50 ms) waits for a tilt; while tilted, scrolling runs on WPF's frame
        // clock (CompositionTarget.Rendering) so it moves once per drawn frame - a free-running timer stepped unevenly
        // against the frames and read as stutter. Squared response: a light tilt creeps, a full tilt flies.
        private void StartStickScroll()
        {
            if (_stickY == null) return;
            _stickPoll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _stickPoll.Tick += (s, e) =>
            {
                if (!_stickScrolling && Math.Abs(_stickY()) >= StickDeadzone) StartStickFrames();
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

            double y = _stickY();
            double a = Math.Abs(y);
            double target = 0;
            if (a >= StickDeadzone)
            {
                double t = (a - StickDeadzone) / (1 - StickDeadzone);
                target = Math.Sign(y) * t * t * StickPixelsPerSecond;
            }
            _stickVelocity += (target - _stickVelocity) * Math.Min(1, dt * 12);

            // Back at rest and coasted to a stop: hand back to the idle check.
            if (target == 0 && Math.Abs(_stickVelocity) < 5) { StopStickFrames(); return; }
            StatsScroll.ScrollToVerticalOffset(StatsScroll.VerticalOffset + _stickVelocity * dt);
        }

        private void CloseDialog()
        {
            Window.GetWindow(this)?.Close();
        }

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
    }
}
