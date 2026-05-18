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
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _playniteApi;
        private readonly IMusicStatsProvider _statsProvider;
        // Reused across all Show() calls — IconColorExtractor's internal
        // cache (keyed by Game.Id) means re-opening the card for the same
        // game skips the bitmap pixel scan.
        private readonly IconColorExtractor _colorExtractor;
        private readonly bool _isFullscreen;

        public MusicInfoCardHandler(
            IPlayniteAPI playniteApi,
            GameMusicFileService fileService,
            bool isFullscreen)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            if (fileService == null) throw new ArgumentNullException(nameof(fileService));
            _statsProvider = new MusicStatsService(fileService);
            _colorExtractor = new IconColorExtractor();
            _isFullscreen = isFullscreen;
        }

        // Opens the Music Info Card for the given game. Picks the Desktop
        // or Fullscreen view based on the construction-time _isFullscreen
        // flag (Playnite mode doesn't change mid-session).
        //
        // Window strategy: a raw transparent WPF Window (same pattern as
        // toasts/controller dialogs) rather than Playnite's CreateWindow.
        // This gives us full control over chrome, corners, transparency,
        // and lets the WPF BlurEffect on the backdrop image render
        // properly. Trade-off: we lose Playnite's titlebar — replaced by
        // a custom title strip + close glyph inside the dialog XAML,
        // with drag-to-move on the title strip via Window.DragMove.
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
                BitmapSource backdrop = LoadBackdropBitmap(game);

                FrameworkElement dialogContent;
                double width;
                double height;
                Window ownerWindow = null;

                if (_isFullscreen)
                {
                    var dialog = new ControllerMusicInfoCardDialog();
                    dialog.Initialize(game, _statsProvider, imagePath, _colorExtractor, backdrop);
                    dialogContent = dialog;
                    width = 720;
                    height = 600;
                }
                else
                {
                    var dialog = new MusicInfoCardDialog();
                    dialog.Initialize(game, _statsProvider, imagePath, _colorExtractor, backdrop);
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
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
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

        // Loads the game's BackgroundImage (preferred) or CoverImage as a
        // BitmapSource for the dialog's blurred backdrop layer. Returns
        // null when neither exists or the file is missing/unreadable —
        // the dialog falls back to its solid-dark Background in that case.
        //
        // BitmapImage is created with OnLoad caching so the underlying
        // FileStream releases immediately (Playnite can rename/replace
        // the file later without "in use" errors).
        private BitmapSource LoadBackdropBitmap(Game game)
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
                if (path == null) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Debug($"MusicInfoCardHandler: LoadBackdropBitmap failed: {ex.Message}");
                return null;
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
