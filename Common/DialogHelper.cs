using System;
using System.Windows;
using System.Windows.Media;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Helper class for creating and configuring Playnite dialog windows.
    /// Centralizes window creation patterns for consistent styling and behavior.
    /// </summary>
    public static class DialogHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Default dark background color for dialogs (matches Playnite dark theme)
        /// </summary>
        public static readonly Color DefaultDarkBackground = Color.FromRgb(33, 33, 33);

        /// <summary>
        /// Options for configuring a dialog window
        /// </summary>
        public class DialogOptions
        {
            /// <summary>Window title</summary>
            public string Title { get; set; } = "Dialog";

            /// <summary>Window width in pixels</summary>
            public double Width { get; set; } = 600;

            /// <summary>Window height in pixels</summary>
            public double Height { get; set; } = 500;

            /// <summary>Whether the window can be resized</summary>
            public bool CanResize { get; set; } = true;

            /// <summary>Whether to show the maximize button</summary>
            public bool ShowMaximizeButton { get; set; } = true;

            /// <summary>Whether to show the minimize button</summary>
            public bool ShowMinimizeButton { get; set; } = false;

            /// <summary>Whether to show the close button</summary>
            public bool ShowCloseButton { get; set; } = true;

            /// <summary>Whether to show the window in the taskbar (default true for Desktop mode accessibility)</summary>
            public bool ShowInTaskbar { get; set; } = true;

            /// <summary>Whether to set the window as topmost (for fullscreen mode)</summary>
            public bool Topmost { get; set; } = false;

            /// <summary>Whether to apply dark background (for fullscreen mode)</summary>
            public bool ApplyDarkBackground { get; set; } = false;

            /// <summary>Whether to set the owner window</summary>
            public bool SetOwner { get; set; } = false;

            /// <summary>Window startup location</summary>
            public WindowStartupLocation StartupLocation { get; set; } = WindowStartupLocation.CenterOwner;
        }

        /// <summary>
        /// Creates a standard dialog window with common defaults.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="title">Window title</param>
        /// <param name="content">Window content (UserControl)</param>
        /// <param name="width">Window width</param>
        /// <param name="height">Window height</param>
        /// <returns>Configured Window instance</returns>
        public static Window CreateStandardDialog(
            IPlayniteAPI playniteApi,
            string title,
            object content,
            double width = 600,
            double height = 500)
        {
            return CreateDialog(playniteApi, content, new DialogOptions
            {
                Title = title,
                Width = width,
                Height = height,
                CanResize = true,
                ShowMaximizeButton = true
            });
        }

        /// <summary>
        /// Creates a fixed-size dialog window (no resize).
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="title">Window title</param>
        /// <param name="content">Window content (UserControl)</param>
        /// <param name="width">Window width</param>
        /// <param name="height">Window height</param>
        /// <returns>Configured Window instance</returns>
        public static Window CreateFixedDialog(
            IPlayniteAPI playniteApi,
            string title,
            object content,
            double width = 600,
            double height = 500)
        {
            return CreateDialog(playniteApi, content, new DialogOptions
            {
                Title = title,
                Width = width,
                Height = height,
                CanResize = false,
                ShowMaximizeButton = false
            });
        }

        /// <summary>
        /// Creates a fullscreen-optimized dialog window with dark background and topmost setting.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="title">Window title</param>
        /// <param name="content">Window content (UserControl)</param>
        /// <param name="width">Window width</param>
        /// <param name="height">Window height</param>
        /// <param name="isFullscreenMode">Whether Playnite is in fullscreen mode</param>
        /// <returns>Configured Window instance</returns>
        public static Window CreateFullscreenDialog(
            IPlayniteAPI playniteApi,
            string title,
            object content,
            double width = 600,
            double height = 500,
            bool isFullscreenMode = false)
        {
            return CreateDialog(playniteApi, content, new DialogOptions
            {
                Title = title,
                Width = width,
                Height = height,
                CanResize = true,
                ShowMaximizeButton = true,
                Topmost = isFullscreenMode,
                ApplyDarkBackground = isFullscreenMode,
                SetOwner = true,
                ShowInTaskbar = !isFullscreenMode // Hide from taskbar in fullscreen mode (topmost handles visibility)
            });
        }

        /// <summary>
        /// Creates a dialog window with full customization options.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="content">Window content (UserControl)</param>
        /// <param name="options">Dialog configuration options</param>
        /// <returns>Configured Window instance</returns>
        public static Window CreateDialog(
            IPlayniteAPI playniteApi,
            object content,
            DialogOptions options)
        {
            if (playniteApi == null)
                throw new ArgumentNullException(nameof(playniteApi));

            if (options == null)
                options = new DialogOptions();

            var window = playniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMinimizeButton = options.ShowMinimizeButton,
                ShowMaximizeButton = options.ShowMaximizeButton,
                ShowCloseButton = options.ShowCloseButton
            });

            window.Title = options.Title;
            window.Width = options.Width;
            window.Height = options.Height;
            window.WindowStartupLocation = options.StartupLocation;
            window.ShowInTaskbar = options.ShowInTaskbar;
            window.ResizeMode = options.CanResize ? ResizeMode.CanResize : ResizeMode.NoResize;
            window.Content = content;

            if (options.Topmost)
            {
                window.Topmost = true;
            }

            if (options.ApplyDarkBackground)
            {
                window.Background = new SolidColorBrush(DefaultDarkBackground);
            }

            if (options.SetOwner)
            {
                try
                {
                    var ownerWindow = playniteApi.Dialogs.GetCurrentAppWindow();
                    if (ownerWindow != null)
                    {
                        window.Owner = ownerWindow;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Error setting window owner");
                }
            }

            return window;
        }

        /// <summary>
        /// Adds a closing handler that returns focus to the main Playnite window.
        /// </summary>
        /// <param name="window">The window to attach the handler to</param>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="context">Optional context string for logging</param>
        public static void AddFocusReturnHandler(Window window, IPlayniteAPI playniteApi, string context = null)
        {
            if (window == null || playniteApi == null)
                return;

            window.Closing += (s, e) => ReturnFocusToMainWindow(playniteApi, context);
        }

        /// <summary>
        /// Returns focus to the main Playnite window.
        /// Useful for ensuring proper focus after dialogs close, especially in fullscreen mode.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="context">Optional context string for logging</param>
        public static void ReturnFocusToMainWindow(IPlayniteAPI playniteApi, string context = null)
        {
            try
            {
                var mainWindow = playniteApi?.Dialogs?.GetCurrentAppWindow();
                if (mainWindow != null)
                {
                    mainWindow.Activate();
                    mainWindow.Focus();
                    // Toggle topmost to ensure focus is grabbed
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false;
                }
            }
            catch (Exception ex)
            {
                var logContext = string.IsNullOrEmpty(context) ? "dialog close" : context;
                Logger.Debug(ex, $"Error returning focus during {logContext}");
            }
        }

        /// <summary>
        /// Shows a dialog and returns whether it was confirmed (DialogResult == true).
        /// </summary>
        /// <param name="window">The window to show</param>
        /// <returns>True if the dialog was confirmed, false otherwise</returns>
        public static bool ShowDialogAndGetResult(Window window)
        {
            return window?.ShowDialog() == true;
        }
    }
}
