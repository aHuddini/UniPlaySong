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
        private static readonly ILogger Logger = LogManager.GetLogger();
        private const int LoaderGraceMs = 200;

        private CancellationTokenSource _cts;
        private Game _game;
        private IMusicStatsProvider _statsProvider;
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

                if (Application.Current?.Properties?.Contains("UniPlaySongPlugin") == true)
                {
                    var plugin = Application.Current.Properties["UniPlaySongPlugin"] as UniPlaySong;
                    plugin?.GetControllerEventRouter()?.Register(this);
                }
            };

            Unloaded += (s, e) =>
            {
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
            BitmapSource backdropSnapshot)
        {
            _game = game;
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

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(_imagePath, UriKind.Absolute);
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();

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

            if (stats.AverageBitrateKbps.HasValue)
            {
                BitrateText.Text = $"{stats.AverageBitrateKbps.Value} kbps";
                BitratePanel.Visibility = Visibility.Visible;
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

        // Playlist tracks always use the fixed amber chip so they remain
        // visually distinguishable regardless of per-game accent color.
        // Standard files use the icon-derived accent when set, else the
        // fallback purple.
        private static readonly SolidColorBrush PlaylistChipBrush =
            CreateFrozenBrush(0xFF, 0xB7, 0x4D);
        private static readonly SolidColorBrush DefaultStandardChipBrush =
            CreateFrozenBrush(0x7E, 0x57, 0xC2);

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // Same projection as the Desktop dialog. Kept duplicated rather
        // than shared because the row anonymous types are dialog-private
        // and the chip backgrounds differ by font size only — extracting
        // them into a shared helper would force a real ViewModel type
        // and isn't worth the indirection for two short methods.
        private void RenderSongList(MusicStats stats)
        {
            if (stats.Songs == null || stats.Songs.Count == 0) return;

            var standardBrush = _accentBrush ?? DefaultStandardChipBrush;

            var rows = stats.Songs.Select(s => new
            {
                Title = s.Title,
                ChipText = s.IsPlaylistTrack
                    ? "TRACK"
                    : (s.Extension ?? string.Empty).TrimStart('.').ToUpperInvariant(),
                ChipBackground = s.IsPlaylistTrack ? PlaylistChipBrush : standardBrush,
                SizeText = FormatSize(s.FileSizeBytes),
                DurationText = s.Duration > TimeSpan.Zero
                    ? FormatDuration(s.Duration)
                    : string.Empty
            }).ToList();

            SongList.ItemsSource = rows;
            SongListCountText.Text = $"({stats.Songs.Count})";
            SongListPanel.Visibility = Visibility.Visible;
        }

        // Smooth fade-in for the body panel. Slightly slower than Desktop
        // (300ms) since the larger Fullscreen content benefits from a
        // touch more breathing room visually.
        private void FadeInStatsPanel()
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            StatsPanel.BeginAnimation(OpacityProperty, anim);
        }

        public void OnControllerButtonPressed(ControllerInput button)
        {
            // B = Cancel/Back on Xbox-style layout; Back is the dedicated
            // back button on most controllers. Both close the dialog.
            if (button == ControllerInput.B || button == ControllerInput.Back)
            {
                Dispatcher.BeginInvoke(new Action(CloseDialog));
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
