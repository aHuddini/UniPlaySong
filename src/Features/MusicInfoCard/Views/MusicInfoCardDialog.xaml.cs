using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Features.MusicInfoCard.Models;
using UniPlaySong.Features.MusicInfoCard.Services;
using UniPlaySong.IconGlow;

namespace UniPlaySong.Features.MusicInfoCard.Views
{
    // Desktop dialog for the Music Info Card. Opens immediately with a
    // loading indicator and populates the stats panel when the background
    // computation completes. Closing the dialog cancels the in-flight
    // computation so TagLib reads abort cleanly on large folders.
    //
    // Visual style ported from BeautyCons: a per-game accent color is
    // extracted from the icon image and threaded through the accent strip,
    // icon glow effect, format chips, and headline color tint. Falls back
    // to neutral purple when no icon is available (placeholder shown).
    public partial class MusicInfoCardDialog : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Delay (ms) before the indeterminate progress bar appears. Reads
        // that complete before this never flash the loader — common for
        // small game folders. WPF convention for "snappy enough" UX.
        private const int LoaderGraceMs = 200;

        private CancellationTokenSource _cts;
        private Game _game;
        private IMusicStatsProvider _statsProvider;
        private string _imagePath;
        private IconColorExtractor _colorExtractor;

        // Resolved at icon-load time and reused for chip colors so each
        // dialog open does ONE pixel scan, then everything inherits it.
        private SolidColorBrush _accentBrush;
        private SolidColorBrush _accentBrushTransparent;

        public MusicInfoCardDialog()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
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

            // Pre-captured Playnite snapshot supplied by the handler.
            // null when capture failed — Image stays empty and the
            // OuterCardBorder Background shows through instead.
            if (backdropSnapshot != null)
            {
                BlurredBackdrop.Source = backdropSnapshot;
            }

            // Load icon + extract color synchronously — image decoding for
            // a single icon is cheap (<10ms typically) and the color is
            // needed before the panels fade in. If anything fails, the
            // placeholder note glyph stays visible and accent stays default.
            TryApplyGameIconAndAccent();

            Loaded += OnDialogLoaded;
        }

        private void TryApplyGameIconAndAccent()
        {
            try
            {
                if (string.IsNullOrEmpty(_imagePath) || !File.Exists(_imagePath))
                    return;

                // BitmapImage with OnLoad caching frees the underlying
                // FileStream immediately so Playnite can rename/move the
                // file later without "in use" errors.
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(_imagePath, UriKind.Absolute);
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();

                GameIconImage.Source = bitmap;
                GameIconImage.Visibility = Visibility.Visible;
                // Hide the placeholder Border (not just the TextBlock inside)
                // so the icon's transparent edges show through the parent
                // Grid without the dark backdrop bleeding through.
                IconPlaceholderBorder.Visibility = Visibility.Collapsed;

                if (_colorExtractor != null && _game != null)
                {
                    var (primary, _) = _colorExtractor.GetGlowColors(_game.Id, bitmap);
                    ApplyAccentColor(primary);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"MusicInfoCardDialog: icon load failed: {ex.Message}");
            }
        }

        // Threads the extracted color through the controls that should
        // adopt it: the accent strip at the top of the card, the icon's
        // DropShadowEffect glow, the subtitle label, the format chips in
        // the song list, and a very-low-alpha whole-card tint that gives
        // the entire dialog a matching mood. The headline numerals keep
        // their hardcoded tri-color palette so they remain consistent
        // across games (visual anchor for "Files / Duration / Size").
        private void ApplyAccentColor(Color color)
        {
            _accentBrush = new SolidColorBrush(color);
            _accentBrush.Freeze();

            var transparent = Color.FromArgb(40, color.R, color.G, color.B);
            _accentBrushTransparent = new SolidColorBrush(transparent);
            _accentBrushTransparent.Freeze();

            AccentStrip.Background = _accentBrush;
            HeaderSubtitle.Foreground = _accentBrush;
            IconGlowEffect.Color = color;

            // Whole-card tint at ~5% alpha (alpha=13 of 255). Bumped
            // from 3% — the blurred background image is now dimmer
            // (Opacity 0.40), so the tint layer needs to carry a bit
            // more of the per-game color signal. Still well below the
            // ~10% level that previously made the card feel saturated.
            var tintColor = Color.FromArgb(13, color.R, color.G, color.B);
            var tintBrush = new SolidColorBrush(tintColor);
            tintBrush.Freeze();
            CardTintLayer.Background = tintBrush;
        }

        private async void OnDialogLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnDialogLoaded;
            await LoadStatsAsync().ConfigureAwait(false);
        }

        private async System.Threading.Tasks.Task LoadStatsAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Show the loader only if the read hasn't completed by the
            // grace period. Avoids the flash for small folders.
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
            catch (OperationCanceledException)
            {
                // dialog closed before completion — expected, no-op
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MusicInfoCardDialog: stats computation failed");
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    EmptyStateText.Text = "Failed to read music metadata. See extension.log.";
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

            // Playlist-aware row — only show when at least one playlist
            // file is present so non-chiptune games don't see a confusing
            // empty section.
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

            // Format breakdown rendered as a small list bound to anonymous
            // row objects. Sorted descending by count so the most-common
            // format shows first.
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
                    " could not be read (corrupt or locked). See extension.log for details.";
                UnreadablePanel.Visibility = Visibility.Visible;
            }

            RenderSongList(stats);

            // Fade in once all child panels have been populated. Without
            // this, users see the entire body pop in at once — jarring.
            FadeInStatsPanel();
        }

        // Brushes used by song-list rows. Playlist tracks always use the
        // fixed amber so they remain visually distinguishable regardless
        // of the per-game accent. Standard files use the accent when set
        // (icon-derived), else the fallback purple — done in RenderSongList.
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

        // Songs list — populated from MusicStats.Songs (already sorted by
        // title in the service). Each row is an anonymous projection so
        // the DataTemplate can bind without a separate ViewModel type.
        // ChipBackground is a Brush (not a string) so the Border.Background
        // binding resolves directly without a converter.
        private void RenderSongList(MusicStats stats)
        {
            if (stats.Songs == null || stats.Songs.Count == 0) return;

            var standardBrush = _accentBrush ?? DefaultStandardChipBrush;

            var rows = stats.Songs.Select(s => new
            {
                Title = s.Title,
                // Format chip — extension w/o the dot, uppercase. Playlist
                // rows get a "TRACK" tag so the visual distinguishes them
                // from standalone files of the same format.
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

        // Smooth fade-in for the entire stats panel after population —
        // 250ms is fast enough to feel snappy but long enough to register
        // visually so users know fresh content arrived. Driven by Opacity
        // since StatsPanel starts at 0 in XAML.
        private void FadeInStatsPanel()
        {
            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            StatsPanel.BeginAnimation(OpacityProperty, anim);
        }

        // h:mm:ss for ≥1h, mm:ss otherwise. Matches the rest of UPS's
        // duration formatting style (NowPlayingPanel, song progress, etc.).
        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            return $"{duration.Minutes:D2}:{duration.Seconds:D2}";
        }

        // Auto-unit byte formatting. Same rules as FormatFileSize in
        // UniPlaySong.cs (kept local so the module stays self-contained).
        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }

        // Drag-to-move replacement for the missing Playnite titlebar.
        // Calls Window.DragMove which is the standard WPF idiom for
        // custom chrome — it processes input until the user releases
        // the mouse button. Wrapped in try/catch because DragMove
        // throws InvalidOperationException if called when the mouse
        // has already been released (race condition between click +
        // release happening faster than the event marshals through).
        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            try
            {
                Window.GetWindow(this)?.DragMove();
            }
            catch (InvalidOperationException) { /* mouse-up before drag started */ }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { /* best effort */ }
        }
    }
}
