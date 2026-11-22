using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Models;
using UniPlaySong.Common;
using UniPlaySong.Features.MusicInfoCard.Services;
using UniPlaySong.Features.MusicInfoCard.Views;
using UniPlaySong.IconGlow;
using UniPlaySong.Services;

namespace UniPlaySong.Features.MusicInfoCard
{
    // Public entry point for the Music Info Card feature. This is the only
    // type the rest of the plugin depends on — everything else under
    // Features/MusicInfoCard/ is internal to the module.
    //
    // Construct once in UniPlaySong.cs alongside the other handlers, then
    // call Show(game) from the right-click menu wiring.
    public class MusicInfoCardHandler
    {
        private static readonly ILogger Logger = global::UniPlaySong.Common.GatedLogger.Get();

        private readonly IPlayniteAPI _playniteApi;
        private readonly IMusicStatsProvider _statsProvider;
        // Reused across all Show() calls — IconColorExtractor's internal
        // cache (keyed by Game.Id) means re-opening the card for the same
        // game skips the bitmap pixel scan.
        private readonly IconColorExtractor _colorExtractor;
        private readonly bool _isFullscreen;
        private readonly IMusicPlaybackService _playbackService; // plays a clicked song row
        private readonly Controls.RightStick _rightStick; // Fullscreen: scrolls the card

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public MusicInfoCardHandler(
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            bool isFullscreen,
            IMusicPlaybackService playbackService)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            if (fileService == null) throw new ArgumentNullException(nameof(fileService));
            _statsProvider = new MusicStatsService(fileService);
            _colorExtractor = new IconColorExtractor();
            _isFullscreen = isFullscreen;
            _playbackService = playbackService;
            if (isFullscreen) _rightStick = Controls.RightStick.TryCreate(playniteApi, out _); // null on older Playnite
        }

        // Opens the Music Info Card for the given game. Picks the Desktop
        // or Fullscreen view based on the construction-time _isFullscreen
        // flag (Playnite mode doesn't change mid-session).
        //
        // Window strategy: a raw borderless WPF Window rather than Playnite's CreateWindow, for full control over chrome
        // and corners. Trade-off: we lose Playnite's titlebar — replaced by a custom title strip + close glyph inside the
        // dialog XAML, with drag-to-move on the title strip via Window.DragMove.
        //
        // The card uses the game's BackgroundImage (when set) as a
        // heavily-blurred backdrop layer inside the dialog. Falls back
        // to cover image, then to a solid dark base if neither exists.
        //
        // Dialog opens immediately; stats populate async. Closing the
        // dialog before stats finish cancels the background read.
        public void Show(Game game)
        {
            if (game == null) return;

            try
            {
                string imagePath = ResolveIconPath(game);
                var (backdrop, frost) = LoadBackdropBitmap(game);

                FrameworkElement dialogContent;
                double width;
                double height;
                Window ownerWindow = null;

                if (_isFullscreen)
                {
                    var dialog = new ControllerMusicInfoCardDialog();
                    dialog.Initialize(game, _statsProvider, imagePath, _colorExtractor, backdrop,
                        path => _playbackService?.PlaySong(game, path), frost,
                        _rightStick == null ? (Func<double>)null : () => _rightStick.TryRead(out _, out var y) ? y : 0);
                    dialogContent = dialog;
                    width = 720;
                    height = 600;
                }
                else
                {
                    var dialog = new MusicInfoCardDialog();
                    dialog.Initialize(game, _statsProvider, imagePath, _colorExtractor, backdrop,
                        path => _playbackService?.PlaySong(game, path), frost);
                    dialogContent = dialog;
                    width = 620;
                    height = 580;
                }

                try
                {
                    ownerWindow = _playniteApi.Dialogs.GetCurrentAppWindow();
                }
                catch (Exception ownerEx)
                {
                    Logger.Debug($"MusicInfoCardHandler: GetCurrentAppWindow failed: {ownerEx.Message}");
                }

                var window = new Window
                {
                    Title = $"Music Info — {game.Name}",
                    Width = width,
                    Height = height,
                    WindowStyle = WindowStyle.None,
                    // An opaque window, not a transparent (layered) one: a layered window is copied back from the GPU and
                    // pushed to the screen in software on every changed frame, which made the stats fade-in stutter. The
                    // card's own colour fills it, and Windows 11 rounds its corners (SourceInitialized below).
                    Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x0F, 0x22)),
                    ResizeMode = ResizeMode.NoResize,
                    Content = dialogContent,
                    ShowInTaskbar = false,
                    WindowStartupLocation = ownerWindow != null
                        ? WindowStartupLocation.CenterOwner
                        : WindowStartupLocation.CenterScreen,
                    Owner = ownerWindow
                };

                // ESC closes the dialog like the existing Close button.
                // Both Desktop and Fullscreen dialogs benefit from this.
                window.KeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Escape)
                    {
                        window.Close();
                        e.Handled = true;
                    }
                };

                window.SourceInitialized += (s, e) =>
                {
                    // Windows 11 only (elsewhere both calls just fail): round the corners, and drop the grey 1 px outline
                    // Windows draws around rounded windows so the backdrop runs to the edge.
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    int round = 2; // DWMWCP_ROUND
                    DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
                    int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
                    DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int)); // DWMWA_BORDER_COLOR
                };

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "music info card close");
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MusicInfoCardHandler: failed to show dialog");
                _playniteApi.Dialogs.ShowErrorMessage(
                    "Failed to open Music Info Card. See extension.log for details.",
                    "UniPlaySong");
            }
        }

        // Loads the game's BackgroundImage (preferred) or CoverImage as a BitmapSource for the dialog's blurred
        // backdrop layer. Returns null when neither exists or the file is missing/unreadable — the dialog falls back
        // to its solid-dark Background in that case.
        // A frozen bitmap read with OnLoad caching, so the FileStream is released immediately and Playnite can rename or
        // replace the file later without "in use" errors. Shared by the handler and both dialogs.
        internal static BitmapImage LoadUnlockedBitmap(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            if (bitmap.CanFreeze) bitmap.Freeze();
            return bitmap;
        }

        private (BitmapSource Backdrop, BitmapSource Frost) LoadBackdropBitmap(Game game)
        {
            try
            {
                string path = null;
                if (!string.IsNullOrEmpty(game.BackgroundImage))
                {
                    var bgPath = _playniteApi.Database.GetFullFilePath(game.BackgroundImage);
                    if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
                        path = bgPath;
                }
                if (path == null && !string.IsNullOrEmpty(game.CoverImage))
                {
                    var coverPath = _playniteApi.Database.GetFullFilePath(game.CoverImage);
                    if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
                        path = coverPath;
                }
                if (path == null) return (null, null);

                return LoadBlurredBackdrop(path);
            }
            catch (Exception ex)
            {
                Logger.Debug($"MusicInfoCardHandler: LoadBackdropBitmap failed: {ex.Message}");
                return (null, null);
            }
        }

        // The backdrop is blurred here, once, instead of by a WPF BlurEffect in the dialog: the live effect added ~50 ms
        // to the card's first frame on every open, on top of a full-size decode. A small decode blurred three times with
        // a box filter (≈ Gaussian) looks the same once stretched to the card, and is cached per file.
        private const int BackdropDecodeWidth = 512; // stretched ~2x to fill the card; 256 showed blocks and banding
        private const int BackdropBlurRadius = 8; // in decoded pixels; ~the old Radius 45 at the card's size
        private const double BackdropSaturation = 1.25; // frosted-glass vibrancy: colour survives the dark veil
        private const int BackdropCacheLimit = 24;
        // The glass tiles' copy: blurred further, then darkened to sit near the veiled backdrop with the tiles' 9% white
        // glass tint baked in (see Views/GlassTiles).
        private const int FrostBlurRadius = 16;
        private const double FrostDarken = 0.27;
        private const byte FrostTint = 23; // 9% white
        private static readonly System.Collections.Generic.Dictionary<string, (BitmapSource Backdrop, BitmapSource Frost)> _backdropCache =
            new System.Collections.Generic.Dictionary<string, (BitmapSource, BitmapSource)>();

        private static (BitmapSource Backdrop, BitmapSource Frost) LoadBlurredBackdrop(string path)
        {
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (_backdropCache.TryGetValue(key, out var cached)) return cached;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = BackdropDecodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();

            var source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int w = source.PixelWidth, h = source.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            source.CopyPixels(pixels, stride, 0);

            var scratch = new byte[pixels.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(pixels, scratch, w, h, BackdropBlurRadius, horizontal: true);
                BoxBlur(scratch, pixels, w, h, BackdropBlurRadius, horizontal: false);
            }
            Saturate(pixels, BackdropSaturation);

            var blurred = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            blurred.Freeze();

            var frostPixels = (byte[])pixels.Clone();
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(frostPixels, scratch, w, h, FrostBlurRadius, horizontal: true);
                BoxBlur(scratch, frostPixels, w, h, FrostBlurRadius, horizontal: false);
            }
            for (int i = 0; i < frostPixels.Length; i += 4)
                for (int c = 0; c < 3; c++)
                    frostPixels[i + c] = (byte)Math.Min(255, frostPixels[i + c] * FrostDarken + FrostTint);
            var frost = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, frostPixels, stride);
            frost.Freeze();

            // ponytail: drop-all eviction; an LRU only if people flip between more than 24 games' cards
            if (_backdropCache.Count >= BackdropCacheLimit) _backdropCache.Clear();
            _backdropCache[key] = (blurred, frost);
            return (blurred, frost);
        }

        // Pushes each pixel's colour away from its own grey by the factor (BGRA, alpha untouched).
        private static void Saturate(byte[] bgra, double factor)
        {
            for (int i = 0; i < bgra.Length; i += 4)
            {
                double grey = 0.114 * bgra[i] + 0.587 * bgra[i + 1] + 0.299 * bgra[i + 2];
                for (int c = 0; c < 3; c++)
                    bgra[i + c] = (byte)Math.Max(0, Math.Min(255, grey + (bgra[i + c] - grey) * factor));
            }
        }

        // One box-blur pass along rows or columns, sliding-window sum per channel, edges clamped.
        internal static void BoxBlur(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
        {
            int lines = horizontal ? h : w, length = horizontal ? w : h;
            int step = horizontal ? 4 : w * 4, lineStep = horizontal ? w * 4 : 4;
            int window = 2 * r + 1;
            for (int line = 0; line < lines; line++)
            {
                int start = line * lineStep;
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int k = -r; k <= r; k++)
                        sum += src[start + Math.Min(Math.Max(k, 0), length - 1) * step + c];
                    for (int i = 0; i < length; i++)
                    {
                        dst[start + i * step + c] = (byte)(sum / window);
                        int outIdx = Math.Max(i - r, 0), inIdx = Math.Min(i + r + 1, length - 1);
                        sum += src[start + inIdx * step + c] - src[start + outIdx * step + c];
                    }
                }
            }
        }

        // Resolves the visible image for the card: icon preferred, cover
        // fallback. Returns null when the game has neither, in which case
        // the dialog renders a placeholder music-note glyph.
        private string ResolveIconPath(Game game)
        {
            try
            {
                if (!string.IsNullOrEmpty(game.Icon))
                {
                    var iconPath = _playniteApi.Database.GetFullFilePath(game.Icon);
                    if (!string.IsNullOrEmpty(iconPath)) return iconPath;
                }
                if (!string.IsNullOrEmpty(game.CoverImage))
                {
                    var coverPath = _playniteApi.Database.GetFullFilePath(game.CoverImage);
                    if (!string.IsNullOrEmpty(coverPath)) return coverPath;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"MusicInfoCardHandler: ResolveIconPath failed for {game?.Name}: {ex.Message}");
            }
            return null;
        }
    }
}
