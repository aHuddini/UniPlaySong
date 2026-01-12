using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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

        // ============================================================================
        // Toast Notification Styling Constants
        // These can be customized to match different themes
        // ============================================================================

        /// <summary>
        /// Toast background color - matches our controller dialog background (#1E1E1E)
        /// </summary>
        public static readonly Color ToastBackgroundColor = Color.FromRgb(30, 30, 30);

        /// <summary>
        /// Toast border color for success messages (Material Design green)
        /// </summary>
        public static readonly Color ToastSuccessBorderColor = Color.FromRgb(76, 175, 80);

        /// <summary>
        /// Toast accent/title color for success messages
        /// </summary>
        public static readonly Color ToastSuccessAccentColor = Color.FromRgb(129, 199, 132);

        /// <summary>
        /// Toast border color for error messages (Material Design red - #F44336)
        /// </summary>
        public static readonly Color ToastErrorBorderColor = Color.FromRgb(244, 67, 54);

        /// <summary>
        /// Toast accent/title color for error messages
        /// </summary>
        public static readonly Color ToastErrorAccentColor = Color.FromRgb(255, 138, 128);

        /// <summary>
        /// Toast text color (light gray for readability on dark background)
        /// </summary>
        public static readonly Color ToastTextColor = Color.FromRgb(224, 224, 224);

        /// <summary>
        /// Toast outer border color (default medium gray - #424242, matches controller dialog border)
        /// This is the default value; the actual value used is ToastBorderColorValue which is configurable.
        /// </summary>
        public static readonly Color ToastOuterBorderColorDefault = Color.FromRgb(66, 66, 66);

        /// <summary>
        /// Configurable toast border color (RGB uint format: 0xRRGGBB).
        /// Default: 0x2A2A2A (dark gray that blends with blur background)
        /// </summary>
        public static uint ToastBorderColorValue = 0x2A2A2A;

        /// <summary>
        /// Toast border thickness in pixels.
        /// Default: 1 (thin border that's subtle)
        /// </summary>
        public static double ToastBorderThickness = 1;

        /// <summary>
        /// Toast position on screen. Change this to reposition all toasts.
        /// </summary>
        public enum ToastPosition
        {
            TopRight,
            TopLeft,
            BottomRight,
            BottomLeft,
            TopCenter,
            BottomCenter
        }

        /// <summary>
        /// Current toast position setting. Modify this to change where toasts appear.
        /// Default is TopRight for non-intrusive notifications.
        /// </summary>
        public static ToastPosition CurrentToastPosition = ToastPosition.TopRight;

        /// <summary>
        /// Margin from screen edge for toast positioning (in pixels)
        /// </summary>
        public static int ToastEdgeMargin = 30;

        /// <summary>
        /// Whether to enable acrylic blur effect on toast notifications.
        /// Uses Windows DWM APIs for native blur - falls back gracefully if unavailable.
        /// </summary>
        public static bool EnableToastAcrylicBlur = true;

        /// <summary>
        /// Toast blur opacity (0-255). Higher = more opaque/darker, lower = more transparent/more blur visible.
        /// Default: 94 (37% opacity)
        /// </summary>
        public static byte ToastBlurOpacity = 94;

        /// <summary>
        /// Toast blur tint color (RGB). This is the color that tints the blur effect.
        /// Default: #000521 (very dark blue)
        /// Format: 0xRRGGBB
        /// </summary>
        public static uint ToastBlurTintColor = 0x000521;

        /// <summary>
        /// Toast blur mode: 0 = Basic blur (less intense), 1 = Acrylic blur (more intense with noise texture).
        /// Default: 1 (Acrylic)
        /// </summary>
        public static int ToastBlurMode = 1;

        /// <summary>
        /// Toast corner radius in pixels. Set to 0 for square corners.
        /// Default: 0 (square corners avoid blur corner artifacts)
        /// </summary>
        public static double ToastCornerRadius = 0;

        /// <summary>
        /// Toast width in pixels.
        /// Default: 420
        /// </summary>
        public static double ToastWidth = 420;

        /// <summary>
        /// Toast minimum height in pixels.
        /// Default: 90
        /// </summary>
        public static double ToastMinHeight = 90;

        /// <summary>
        /// Toast maximum height in pixels.
        /// Default: 180
        /// </summary>
        public static double ToastMaxHeight = 180;

        /// <summary>
        /// Toast display duration in milliseconds. Default: 4000
        /// </summary>
        public static int ToastDurationMs = 4000;

        /// <summary>
        /// Synchronizes toast settings from UniPlaySongSettings to DialogHelper static fields.
        /// Call this when settings are loaded or changed.
        /// </summary>
        /// <param name="settings">The settings object to sync from</param>
        public static void SyncToastSettings(UniPlaySongSettings settings)
        {
            if (settings == null) return;

            EnableToastAcrylicBlur = settings.EnableToastAcrylicBlur;
            ToastBlurOpacity = (byte)settings.ToastBlurOpacity;
            ToastBlurMode = settings.ToastBlurMode;
            ToastCornerRadius = settings.ToastCornerRadius;
            ToastWidth = settings.ToastWidth;
            ToastMinHeight = settings.ToastMinHeight;
            ToastMaxHeight = settings.ToastMaxHeight;
            ToastDurationMs = settings.ToastDurationMs;
            ToastEdgeMargin = settings.ToastEdgeMargin;

            // Parse hex color string to uint (format: "RRGGBB" -> 0xRRGGBB)
            if (!string.IsNullOrEmpty(settings.ToastBlurTintColor))
            {
                try
                {
                    string hex = settings.ToastBlurTintColor.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        ToastBlurTintColor = Convert.ToUInt32(hex, 16);
                    }
                }
                catch
                {
                    // Keep default value on parse error
                }
            }

            // Parse border color hex string to uint
            if (!string.IsNullOrEmpty(settings.ToastBorderColor))
            {
                try
                {
                    string hex = settings.ToastBorderColor.TrimStart('#');
                    if (hex.Length == 6)
                    {
                        ToastBorderColorValue = Convert.ToUInt32(hex, 16);
                    }
                }
                catch
                {
                    // Keep default value on parse error
                }
            }

            ToastBorderThickness = settings.ToastBorderThickness;
        }

        // ============================================================================
        // Windows Acrylic Blur Effect API
        // Uses SetWindowCompositionAttribute for Windows 10 1803+ acrylic blur
        // ============================================================================

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        // Windows 11 DWM API for native backdrop effects with proper rounded corner support
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        // DWM Window Attributes for Windows 11
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // Window corner preference values
        private const int DWMWCP_DEFAULT = 0;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_ROUNDSMALL = 3;

        // System backdrop type values (Windows 11 22H2+)
        private const int DWMSBT_AUTO = 0;
        private const int DWMSBT_DISABLE = 1;      // None
        private const int DWMSBT_MAINWINDOW = 2;   // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        private const int DWMSBT_TABBEDWINDOW = 4; // Tabbed

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_BLURBEHIND = 3,        // Basic blur (Windows 10+)
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4  // Acrylic blur (Windows 10 1803+)
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor; // Format: AABBGGRR (Alpha, Blue, Green, Red)
            public int AnimationId;
        }

        /// <summary>
        /// Checks if the current Windows version supports the DWM backdrop API (Windows 11 22H2+, build 22621+).
        /// This API provides native acrylic with proper rounded corner support.
        /// </summary>
        private static bool IsWindows11WithBackdropSupport()
        {
            try
            {
                // Windows 11 22H2 is build 22621+
                // The DWMWA_SYSTEMBACKDROP_TYPE attribute was added in this version
                var version = Environment.OSVersion.Version;
                return version.Major >= 10 && version.Build >= 22621;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if we're on Windows 11 (build 22000+) which supports DWMWA_WINDOW_CORNER_PREFERENCE.
        /// </summary>
        private static bool IsWindows11()
        {
            try
            {
                var version = Environment.OSVersion.Version;
                return version.Major >= 10 && version.Build >= 22000;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sets a rounded rectangle region on the window at the native Win32 level.
        /// This clips the actual window (including acrylic blur) to rounded corners,
        /// not just the WPF content.
        /// </summary>
        /// <param name="window">The window to clip</param>
        /// <param name="cornerRadius">Radius for rounded corners in pixels</param>
        private static void SetRoundedWindowRegion(Window window, int cornerRadius)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var hwnd = windowHelper.EnsureHandle();

                // Get window dimensions in device pixels (not DIP)
                var source = PresentationSource.FromVisual(window);
                if (source?.CompositionTarget == null) return;

                var dpiScale = source.CompositionTarget.TransformToDevice.M11;
                int width = (int)(window.ActualWidth * dpiScale);
                int height = (int)(window.ActualHeight * dpiScale);
                int scaledRadius = (int)(cornerRadius * dpiScale);

                // Create a rounded rectangle region
                // Adding 1 to dimensions helps with edge cases
                IntPtr hRgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, scaledRadius * 2, scaledRadius * 2);
                if (hRgn != IntPtr.Zero)
                {
                    // SetWindowRgn takes ownership of the region, so we don't delete it
                    SetWindowRgn(hwnd, hRgn, true);
                    // Note: Do NOT call DeleteObject on hRgn - the system owns it now
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "SetRoundedWindowRegion failed");
            }
        }

        /// <summary>
        /// Enables acrylic blur effect on a window using SetWindowCompositionAttribute.
        /// Uses configurable ToastBlurOpacity and ToastBlurTintColor settings.
        /// Requires Windows 10 1803+ for acrylic effect, falls back to basic blur on older versions.
        /// </summary>
        /// <param name="window">The window to apply blur to</param>
        private static void EnableWindowBlur(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var hwnd = windowHelper.EnsureHandle();

                // GradientColor format is AABBGGRR (Alpha, Blue, Green, Red)
                // Convert RGB (0xRRGGBB) to BGR format and add alpha
                uint r = (ToastBlurTintColor >> 16) & 0xFF;
                uint g = (ToastBlurTintColor >> 8) & 0xFF;
                uint b = ToastBlurTintColor & 0xFF;
                uint gradientColor = ((uint)ToastBlurOpacity << 24) | (b << 16) | (g << 8) | r;

                // Select blur mode: 0 = Basic blur (less intense), 1 = Acrylic blur (more intense with noise texture)
                var blurState = ToastBlurMode == 0
                    ? AccentState.ACCENT_ENABLE_BLURBEHIND
                    : AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;

                var accent = new AccentPolicy
                {
                    AccentState = blurState,
                    AccentFlags = 0, // No special flags needed
                    GradientColor = gradientColor,
                    AnimationId = 0
                };

                var accentSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentSize);

                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);

                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = accentSize
                    };

                    int result = SetWindowCompositionAttribute(hwnd, ref data);
                    Logger.Debug($"EnableWindowBlur ACRYLICBLURBEHIND result: {result}");

                    // Fallback to BLURBEHIND if acrylic fails
                    if (result == 0)
                    {
                        accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                        Marshal.StructureToPtr(accent, accentPtr, false);
                        result = SetWindowCompositionAttribute(hwnd, ref data);
                        Logger.Debug($"EnableWindowBlur BLURBEHIND fallback result: {result}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "EnableWindowBlur failed");
            }
        }

        /// <summary>
        /// Gets the current DPI scale factor for the primary screen.
        /// Returns 1.0 for 96 DPI (100%), 1.5 for 144 DPI (150%), 2.0 for 192 DPI (200%), etc.
        /// </summary>
        public static double GetDpiScaleFactor()
        {
            try
            {
                // Get DPI from the main application window or create a temporary source
                var source = PresentationSource.FromVisual(Application.Current?.MainWindow);
                if (source?.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }

                // Fallback: try to get system DPI using legacy method
                using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    return graphics.DpiX / 96.0;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error getting DPI scale factor, using default 1.0");
                return 1.0;
            }
        }

        /// <summary>
        /// Scales a dimension based on DPI scale factor.
        /// For 4K displays at 150% scaling, dimensions will be multiplied by 1.5.
        /// </summary>
        public static double ScaleForDpi(double baseDimension)
        {
            var scaleFactor = GetDpiScaleFactor();
            return baseDimension * scaleFactor;
        }

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

            /// <summary>Whether to scale width/height based on system DPI (for 4K displays)</summary>
            /// <remarks>Disabled by default - DPI scaling doesn't work well with remote streaming scenarios</remarks>
            public bool ScaleForDpi { get; set; } = false;
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

            // Apply DPI scaling if enabled (for 4K displays and remote streaming)
            // Also ensure the dialog fits within the screen bounds
            var width = options.Width;
            var height = options.Height;
            if (options.ScaleForDpi)
            {
                var scaleFactor = GetDpiScaleFactor();
                if (scaleFactor > 1.0)
                {
                    width = options.Width * scaleFactor;
                    height = options.Height * scaleFactor;
                    Logger.Debug($"DPI scaling applied: {scaleFactor:F2}x ({options.Width}x{options.Height} -> {width:F0}x{height:F0})");
                }
            }

            // Ensure dialog fits within screen bounds (with margin for window chrome/taskbar)
            try
            {
                var screenWidth = SystemParameters.PrimaryScreenWidth;
                var screenHeight = SystemParameters.PrimaryScreenHeight;
                var maxWidth = screenWidth * 0.95;  // 95% of screen width
                var maxHeight = screenHeight * 0.90; // 90% of screen height (account for taskbar)

                if (width > maxWidth || height > maxHeight)
                {
                    // Scale down proportionally to fit
                    var widthRatio = maxWidth / width;
                    var heightRatio = maxHeight / height;
                    var scaleDown = Math.Min(widthRatio, heightRatio);

                    var oldWidth = width;
                    var oldHeight = height;
                    width = width * scaleDown;
                    height = height * scaleDown;
                    Logger.Debug($"Dialog scaled to fit screen: {oldWidth:F0}x{oldHeight:F0} -> {width:F0}x{height:F0} (screen: {screenWidth}x{screenHeight})");
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error getting screen dimensions for size clamping");
            }

            window.Title = options.Title;
            window.Width = width;
            window.Height = height;
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

        /// <summary>
        /// Shows a controller-friendly message dialog with larger text for fullscreen mode.
        /// This is an alternative to Playnite's ShowMessage that provides better readability on TVs.
        /// Includes XInput controller support - press A or Enter to close.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="message">The message to display</param>
        /// <param name="title">Dialog title</param>
        /// <param name="isError">Whether this is an error message (changes styling)</param>
        public static void ShowControllerMessage(IPlayniteAPI playniteApi, string message, string title, bool isError = false)
        {
            if (playniteApi == null) return;

            try
            {
                Window window = null;
                System.Threading.CancellationTokenSource controllerCts = null;
                ushort lastButtonState = 0;

                // Create the message content
                var grid = new System.Windows.Controls.Grid();
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

                // Message text
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = message,
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = isError
                        ? new SolidColorBrush(Color.FromRgb(255, 100, 100))
                        : new SolidColorBrush(Colors.White),
                    Margin = new Thickness(20, 20, 20, 10),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                System.Windows.Controls.Grid.SetRow(textBlock, 0);
                grid.Children.Add(textBlock);

                // Controller hint
                var hintText = new System.Windows.Controls.TextBlock
                {
                    Text = "Press A or Enter to continue",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 10)
                };
                System.Windows.Controls.Grid.SetRow(hintText, 1);
                grid.Children.Add(hintText);

                // OK Button
                var button = new System.Windows.Controls.Button
                {
                    Content = "OK",
                    Width = 150,
                    Height = 50,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 20),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(3)
                };
                button.Click += (s, e) => window?.Close();
                System.Windows.Controls.Grid.SetRow(button, 2);
                grid.Children.Add(button);

                window = CreateDialog(playniteApi, grid, new DialogOptions
                {
                    Title = title,
                    Width = 600,
                    Height = 350,
                    CanResize = false,
                    ShowMaximizeButton = false,
                    ShowMinimizeButton = false,
                    ApplyDarkBackground = true,
                    StartupLocation = WindowStartupLocation.CenterOwner,
                    SetOwner = true
                });

                // Handle keyboard input
                window.KeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape)
                    {
                        window?.Close();
                        e.Handled = true;
                    }
                };

                // Start controller monitoring
                controllerCts = new System.Threading.CancellationTokenSource();

                // Initialize lastButtonState with current controller state
                try
                {
                    XInputWrapper.XINPUT_STATE initState = new XInputWrapper.XINPUT_STATE();
                    if (XInputWrapper.XInputGetState(0, ref initState) == 0)
                    {
                        lastButtonState = initState.Gamepad.wButtons;
                    }
                }
                catch { }

                var controllerTask = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(100); // Initial delay

                    while (!controllerCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                            if (XInputWrapper.XInputGetState(0, ref state) == 0)
                            {
                                ushort currentButtons = state.Gamepad.wButtons;
                                ushort pressedButtons = (ushort)(currentButtons & ~lastButtonState);

                                // A or B button closes the dialog
                                if ((pressedButtons & (XInputWrapper.XINPUT_GAMEPAD_A | XInputWrapper.XINPUT_GAMEPAD_B)) != 0)
                                {
                                    window.Dispatcher.Invoke(() => window?.Close());
                                }

                                lastButtonState = currentButtons;
                            }

                            await System.Threading.Tasks.Task.Delay(50, controllerCts.Token);
                        }
                        catch (System.Threading.Tasks.TaskCanceledException)
                        {
                            break;
                        }
                        catch { }
                    }
                }, controllerCts.Token);

                window.Closed += (s, e) =>
                {
                    controllerCts?.Cancel();
                };

                window.ShowDialog();

                controllerCts?.Cancel();

                // Wait for all buttons to be released before returning
                // This prevents the A/B button press that closed this dialog from being
                // detected as a new press by the calling dialog
                WaitForButtonRelease();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error showing controller message, falling back to standard dialog");
                // Fallback to standard Playnite dialog
                if (isError)
                    playniteApi.Dialogs.ShowErrorMessage(message, title);
                else
                    playniteApi.Dialogs.ShowMessage(message, title);
            }
        }

        /// <summary>
        /// Shows a controller-friendly Yes/No confirmation dialog with larger text for fullscreen mode.
        /// Includes XInput controller support for navigation and selection.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="message">The message to display</param>
        /// <param name="title">Dialog title</param>
        /// <returns>True if Yes was clicked, false otherwise</returns>
        public static bool ShowControllerConfirmation(IPlayniteAPI playniteApi, string message, string title)
        {
            if (playniteApi == null) return false;

            try
            {
                bool result = false;
                Window window = null;
                System.Windows.Controls.Button yesButton = null;
                System.Windows.Controls.Button noButton = null;
                int selectedIndex = 0; // 0 = Yes, 1 = No
                System.Threading.CancellationTokenSource controllerCts = null;
                ushort lastButtonState = 0;

                // Create buttons with references
                Action updateButtonStyles = () =>
                {
                    if (yesButton == null || noButton == null) return;

                    // Yes button style
                    yesButton.Background = selectedIndex == 0
                        ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green when selected
                        : new SolidColorBrush(Color.FromRgb(60, 60, 60));   // Gray when not selected
                    yesButton.BorderBrush = selectedIndex == 0
                        ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                        : new SolidColorBrush(Color.FromRgb(100, 100, 100));
                    yesButton.BorderThickness = selectedIndex == 0 ? new Thickness(3) : new Thickness(1);

                    // No button style
                    noButton.Background = selectedIndex == 1
                        ? new SolidColorBrush(Color.FromRgb(244, 67, 54))   // Red when selected
                        : new SolidColorBrush(Color.FromRgb(60, 60, 60));    // Gray when not selected
                    noButton.BorderBrush = selectedIndex == 1
                        ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
                        : new SolidColorBrush(Color.FromRgb(100, 100, 100));
                    noButton.BorderThickness = selectedIndex == 1 ? new Thickness(3) : new Thickness(1);
                };

                // Create the confirmation content with controller support
                var grid = new System.Windows.Controls.Grid();
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

                // Message text
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = message,
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.White),
                    Margin = new Thickness(20, 20, 20, 10),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                System.Windows.Controls.Grid.SetRow(textBlock, 0);
                grid.Children.Add(textBlock);

                // Controller hint
                var hintText = new System.Windows.Controls.TextBlock
                {
                    Text = "D-Pad/Arrows: Select  •  A/Enter: Confirm  •  B/Esc: Cancel",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 10)
                };
                System.Windows.Controls.Grid.SetRow(hintText, 1);
                grid.Children.Add(hintText);

                // Button panel
                var buttonPanel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 20)
                };

                yesButton = new System.Windows.Controls.Button
                {
                    Content = "Yes",
                    Width = 140,
                    Height = 50,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(10, 0, 10, 0),
                    Foreground = new SolidColorBrush(Colors.White)
                };
                yesButton.Click += (s, e) => { result = true; window?.Close(); };

                noButton = new System.Windows.Controls.Button
                {
                    Content = "No",
                    Width = 140,
                    Height = 50,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(10, 0, 10, 0),
                    Foreground = new SolidColorBrush(Colors.White)
                };
                noButton.Click += (s, e) => { result = false; window?.Close(); };

                buttonPanel.Children.Add(yesButton);
                buttonPanel.Children.Add(noButton);

                System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
                grid.Children.Add(buttonPanel);

                // Initial button styles
                updateButtonStyles();

                window = CreateDialog(playniteApi, grid, new DialogOptions
                {
                    Title = title,
                    Width = 650,
                    Height = 400,
                    CanResize = false,
                    ShowMaximizeButton = false,
                    ShowMinimizeButton = false,
                    ApplyDarkBackground = true,
                    StartupLocation = WindowStartupLocation.CenterOwner,
                    SetOwner = true
                });

                // Handle keyboard input
                window.KeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Left || e.Key == System.Windows.Input.Key.Right)
                    {
                        selectedIndex = selectedIndex == 0 ? 1 : 0;
                        updateButtonStyles();
                        e.Handled = true;
                    }
                    else if (e.Key == System.Windows.Input.Key.Enter)
                    {
                        result = selectedIndex == 0;
                        window?.Close();
                        e.Handled = true;
                    }
                    else if (e.Key == System.Windows.Input.Key.Escape)
                    {
                        result = false;
                        window?.Close();
                        e.Handled = true;
                    }
                };

                // Start controller monitoring
                controllerCts = new System.Threading.CancellationTokenSource();

                // Initialize lastButtonState with current controller state
                try
                {
                    XInputWrapper.XINPUT_STATE initState = new XInputWrapper.XINPUT_STATE();
                    if (XInputWrapper.XInputGetState(0, ref initState) == 0)
                    {
                        lastButtonState = initState.Gamepad.wButtons;
                    }
                }
                catch { }

                var controllerTask = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(100); // Initial delay

                    while (!controllerCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                            if (XInputWrapper.XInputGetState(0, ref state) == 0)
                            {
                                ushort currentButtons = state.Gamepad.wButtons;
                                ushort pressedButtons = (ushort)(currentButtons & ~lastButtonState);

                                if (pressedButtons != 0)
                                {
                                    window.Dispatcher.Invoke(() =>
                                    {
                                        // D-pad left/right to switch selection
                                        if ((pressedButtons & (XInputWrapper.XINPUT_GAMEPAD_DPAD_LEFT | XInputWrapper.XINPUT_GAMEPAD_DPAD_RIGHT)) != 0)
                                        {
                                            selectedIndex = selectedIndex == 0 ? 1 : 0;
                                            updateButtonStyles();
                                        }
                                        // A button = confirm selection
                                        else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
                                        {
                                            result = selectedIndex == 0;
                                            window?.Close();
                                        }
                                        // B button = cancel (No)
                                        else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_B) != 0)
                                        {
                                            result = false;
                                            window?.Close();
                                        }
                                    });
                                }

                                lastButtonState = currentButtons;
                            }

                            await System.Threading.Tasks.Task.Delay(50, controllerCts.Token);
                        }
                        catch (System.Threading.Tasks.TaskCanceledException)
                        {
                            break;
                        }
                        catch { }
                    }
                }, controllerCts.Token);

                window.Closed += (s, e) =>
                {
                    controllerCts?.Cancel();
                };

                window.ShowDialog();

                controllerCts?.Cancel();

                // Wait for all buttons to be released before returning
                // This prevents the A button press that closed this dialog from being
                // detected as a new press by the calling dialog
                WaitForButtonRelease();

                return result;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error showing controller confirmation, falling back to standard dialog");
                // Fallback to standard Playnite dialog
                return playniteApi.Dialogs.ShowMessage(message, title, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            }
        }

        /// <summary>
        /// Shows a non-blocking notification using Playnite's built-in notification system.
        /// This is ideal for controller/fullscreen mode as it doesn't require button dismissal
        /// and avoids the double-press issues that modal dialogs have with XInput.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="id">Unique identifier for this notification (allows updating/removing)</param>
        /// <param name="message">The message to display</param>
        /// <param name="type">Notification type (Info, Error, etc.)</param>
        /// <param name="onClick">Optional action to perform when notification is clicked</param>
        public static void ShowNotification(
            IPlayniteAPI playniteApi,
            string id,
            string message,
            NotificationType type = NotificationType.Info,
            Action onClick = null)
        {
            if (playniteApi == null) return;

            try
            {
                var notification = new NotificationMessage(id, message, type, onClick);
                playniteApi.Notifications.Add(notification);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Error showing notification: {message}");
            }
        }

        /// <summary>
        /// Shows a success notification (green, informational).
        /// Notifications auto-dismiss and don't require user interaction.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="id">Unique identifier for this notification</param>
        /// <param name="message">The success message to display</param>
        public static void ShowSuccessNotification(IPlayniteAPI playniteApi, string id, string message)
        {
            ShowNotification(playniteApi, id, message, NotificationType.Info);
        }

        /// <summary>
        /// Shows an error notification (red, stands out).
        /// Notifications auto-dismiss and don't require user interaction.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="id">Unique identifier for this notification</param>
        /// <param name="message">The error message to display</param>
        public static void ShowErrorNotification(IPlayniteAPI playniteApi, string id, string message)
        {
            ShowNotification(playniteApi, id, message, NotificationType.Error);
        }

        /// <summary>
        /// Removes a notification by its ID.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="id">The notification ID to remove</param>
        public static void RemoveNotification(IPlayniteAPI playniteApi, string id)
        {
            if (playniteApi == null || string.IsNullOrEmpty(id)) return;

            try
            {
                playniteApi.Notifications.Remove(id);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Error removing notification: {id}");
            }
        }

        /// <summary>
        /// Positions a toast window based on the CurrentToastPosition setting.
        /// Uses primary screen dimensions for consistent positioning.
        /// </summary>
        /// <param name="toastWindow">The toast window to position</param>
        private static void PositionToastWindow(Window toastWindow)
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var toastWidth = toastWindow.ActualWidth;
            var toastHeight = toastWindow.ActualHeight;
            var margin = ToastEdgeMargin;

            switch (CurrentToastPosition)
            {
                case ToastPosition.TopRight:
                    toastWindow.Left = screenWidth - toastWidth - margin;
                    toastWindow.Top = margin;
                    break;

                case ToastPosition.TopLeft:
                    toastWindow.Left = margin;
                    toastWindow.Top = margin;
                    break;

                case ToastPosition.BottomRight:
                    toastWindow.Left = screenWidth - toastWidth - margin;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;

                case ToastPosition.BottomLeft:
                    toastWindow.Left = margin;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;

                case ToastPosition.TopCenter:
                    toastWindow.Left = (screenWidth - toastWidth) / 2;
                    toastWindow.Top = margin;
                    break;

                case ToastPosition.BottomCenter:
                    toastWindow.Left = (screenWidth - toastWidth) / 2;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;

                default:
                    // Default to top-right
                    toastWindow.Left = screenWidth - toastWidth - margin;
                    toastWindow.Top = margin;
                    break;
            }
        }

        /// <summary>
        /// Shows an auto-closing toast popup that works in fullscreen mode.
        /// Unlike Playnite's notification system (which only shows in desktop mode),
        /// this creates an actual WPF window that auto-closes after a specified duration.
        /// The toast position is controlled by CurrentToastPosition setting.
        /// No button press is required to dismiss, avoiding XInput double-press issues.
        /// </summary>
        /// <param name="playniteApi">Playnite API instance</param>
        /// <param name="message">The message to display</param>
        /// <param name="title">Toast title</param>
        /// <param name="isError">Whether this is an error message (changes styling)</param>
        /// <param name="durationMs">How long to show the toast. If not specified, uses ToastDurationMs setting.</param>
        public static void ShowAutoCloseToast(
            IPlayniteAPI playniteApi,
            string message,
            string title,
            bool isError = false,
            int? durationMs = null)
        {
            if (playniteApi == null) return;

            try
            {
                // Must run on UI thread
                var app = Application.Current;
                if (app == null) return;

                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Window toastWindow = null;
                        System.Windows.Threading.DispatcherTimer closeTimer = null;

                        // Use our toast color constants (matching controller dialog styling)
                        var accentColor = isError ? ToastErrorAccentColor : ToastSuccessAccentColor;
                        var borderColor = isError ? ToastErrorBorderColor : ToastSuccessBorderColor;

                        // Create content grid with accent bar as a column instead of border
                        // Using a solid Rectangle avoids anti-aliasing artifacts from Border rendering
                        var contentGrid = new System.Windows.Controls.Grid();
                        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(4) }); // Accent bar column
                        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content column

                        // Accent bar as a solid rectangle (no border artifacts)
                        // Use a small margin to prevent overlap with outer border edge
                        var accentBar = new System.Windows.Shapes.Rectangle
                        {
                            Fill = new SolidColorBrush(borderColor),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Margin = new Thickness(0.5, 0.5, 0, 0.5) // Small inset to avoid border overlap
                        };
                        System.Windows.Controls.Grid.SetColumn(accentBar, 0);
                        contentGrid.Children.Add(accentBar);

                        // Inner grid for text content
                        var textGrid = new System.Windows.Controls.Grid();
                        textGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                        textGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        System.Windows.Controls.Grid.SetColumn(textGrid, 1);
                        contentGrid.Children.Add(textGrid);

                        // Title - FontSize 24 matches our controller dialog title
                        var titleBlock = new System.Windows.Controls.TextBlock
                        {
                            Text = title,
                            FontSize = 24,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(accentColor),
                            Margin = new Thickness(16, 16, 20, 6),
                            HorizontalAlignment = HorizontalAlignment.Left
                        };
                        System.Windows.Controls.Grid.SetRow(titleBlock, 0);
                        textGrid.Children.Add(titleBlock);

                        // Message - FontSize 18 for readability (slightly smaller than title)
                        // LineHeight provides extra spacing between lines for better readability
                        var messageBlock = new System.Windows.Controls.TextBlock
                        {
                            Text = message,
                            FontSize = 18,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ToastTextColor),
                            Margin = new Thickness(16, 4, 20, 16),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            TextAlignment = System.Windows.TextAlignment.Left,
                            LineHeight = 28, // Increased line height for better spacing between lines
                            LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight
                        };
                        System.Windows.Controls.Grid.SetRow(messageBlock, 1);
                        textGrid.Children.Add(messageBlock);

                        // Create outer container with semi-transparent background
                        // The blur effect will show through the transparency
                        // Convert ToastBorderColorValue (0xRRGGBB) to Color
                        var borderR = (byte)((ToastBorderColorValue >> 16) & 0xFF);
                        var borderG = (byte)((ToastBorderColorValue >> 8) & 0xFF);
                        var borderB = (byte)(ToastBorderColorValue & 0xFF);
                        var outerBorderColor = Color.FromRgb(borderR, borderG, borderB);

                        var outerBorder = new Border
                        {
                            BorderBrush = new SolidColorBrush(outerBorderColor),
                            BorderThickness = new Thickness(ToastBorderThickness),
                            CornerRadius = new CornerRadius(ToastCornerRadius),
                            // Semi-transparent dark background - allows blur to show through
                            Background = new SolidColorBrush(Color.FromArgb(1, 45, 45, 45)), // Nearly transparent
                            Padding = new Thickness(0),
                            Child = contentGrid
                        };

                        // Wrapper Grid for the toast content
                        // Note: The Windows SetWindowCompositionAttribute API applies blur to the entire
                        // rectangular window. There's no way to clip it to rounded corners - this is a
                        // known limitation of this undocumented API. The dark corners in rounded toasts
                        // are unavoidable with acrylic blur enabled. To avoid them, either:
                        // 1. Set corner radius to 0 (square corners)
                        // 2. Disable blur (use solid background)
                        var clipWrapper = new System.Windows.Controls.Grid
                        {
                            ClipToBounds = true
                        };
                        clipWrapper.Children.Add(outerBorder);

                        // Create window WITH AllowsTransparency for blur effect
                        // FluentWpfChromes uses AllowsTransparency=true with SetWindowCompositionAttribute
                        toastWindow = new Window
                        {
                            Title = title,
                            Width = ToastWidth,
                            SizeToContent = SizeToContent.Height,
                            MinHeight = ToastMinHeight,
                            MaxHeight = ToastMaxHeight,
                            WindowStyle = WindowStyle.None,
                            AllowsTransparency = true, // Required for transparent/blur window
                            Background = Brushes.Transparent, // Transparent window background
                            ResizeMode = ResizeMode.NoResize,
                            Content = clipWrapper,
                            Topmost = true,
                            ShowInTaskbar = false,
                            ShowActivated = false, // Don't steal focus from main dialog
                            Focusable = false
                        };

                        // Apply rounded clip to the wrapper Grid after layout to clip the blur effect
                        // This clips everything including the blur that shows through transparent areas
                        clipWrapper.Loaded += (wrapperSender, wrapperArgs) =>
                        {
                            try
                            {
                                var wrapper = wrapperSender as System.Windows.Controls.Grid;
                                if (wrapper != null && wrapper.ActualWidth > 0 && wrapper.ActualHeight > 0)
                                {
                                    // Create a rounded rectangle geometry matching the border's corner radius
                                    var clipGeometry = new RectangleGeometry(
                                        new Rect(0, 0, wrapper.ActualWidth, wrapper.ActualHeight),
                                        ToastCornerRadius,
                                        ToastCornerRadius);
                                    wrapper.Clip = clipGeometry;
                                }
                            }
                            catch { /* Ignore clip errors */ }
                        };

                        // Position toast and apply blur effect after window loads
                        toastWindow.SourceInitialized += (s, e) =>
                        {
                            try
                            {
                                // Apply blur effect
                                if (EnableToastAcrylicBlur)
                                {
                                    EnableWindowBlur(toastWindow);
                                }
                            }
                            catch (Exception blurEx)
                            {
                                Logger.Debug(blurEx, "Failed to apply blur effect");
                            }
                        };

                        toastWindow.Loaded += (s, e) =>
                        {
                            try
                            {
                                PositionToastWindow(toastWindow);
                            }
                            catch
                            {
                                // Fallback positioning (top-right)
                                toastWindow.Left = SystemParameters.PrimaryScreenWidth - toastWindow.ActualWidth - ToastEdgeMargin;
                                toastWindow.Top = ToastEdgeMargin;
                            }
                        };

                        // Setup auto-close timer (use provided duration or fallback to configured ToastDurationMs)
                        var actualDuration = durationMs ?? ToastDurationMs;
                        closeTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(actualDuration)
                        };
                        closeTimer.Tick += (s, e) =>
                        {
                            closeTimer.Stop();
                            try
                            {
                                toastWindow?.Close();
                            }
                            catch { /* Ignore close errors */ }
                        };

                        // Allow clicking anywhere on toast to dismiss early
                        toastWindow.MouseDown += (s, e) =>
                        {
                            closeTimer?.Stop();
                            try
                            {
                                toastWindow?.Close();
                            }
                            catch { /* Ignore close errors */ }
                        };

                        // Start timer and show window non-modally
                        closeTimer.Start();
                        toastWindow.Show();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, $"Error showing auto-close toast: {message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, $"Error dispatching auto-close toast: {message}");
            }
        }

        /// <summary>
        /// Shows a success toast that auto-closes (works in fullscreen mode).
        /// Uses ToastDurationMs setting by default.
        /// </summary>
        public static void ShowSuccessToast(IPlayniteAPI playniteApi, string message, string title = "Success", int? durationMs = null)
        {
            ShowAutoCloseToast(playniteApi, message, title, isError: false, durationMs: durationMs);
        }

        /// <summary>
        /// Shows an error toast that auto-closes (works in fullscreen mode).
        /// Uses ToastDurationMs setting by default (errors may need longer duration).
        /// </summary>
        public static void ShowErrorToast(IPlayniteAPI playniteApi, string message, string title = "Error", int? durationMs = null)
        {
            // Error toasts get 25% more time by default for reading error details
            var actualDuration = durationMs ?? (int)(ToastDurationMs * 1.25);
            ShowAutoCloseToast(playniteApi, message, title, isError: true, durationMs: actualDuration);
        }

        /// <summary>
        /// Wait for all controller buttons to be released, then add a grace period.
        /// Call this after modal dialogs close to prevent button presses from
        /// "leaking" into the parent dialog.
        ///
        /// The grace period is critical: even after the button is released, the parent
        /// dialog's polling loop might be in the middle of a cycle and could detect
        /// the release as a state change. The grace period ensures the parent has
        /// time to sync its button state.
        /// </summary>
        private static void WaitForButtonRelease(int timeoutMs = 1000, int gracePeriodMs = 150)
        {
            try
            {
                var startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                    if (XInputWrapper.XInputGetState(0, ref state) == 0)
                    {
                        // Check if A and B buttons are released (these are the confirm/cancel buttons)
                        ushort confirmButtons = (ushort)(
                            XInputWrapper.XINPUT_GAMEPAD_A |
                            XInputWrapper.XINPUT_GAMEPAD_B
                        );

                        if ((state.Gamepad.wButtons & confirmButtons) == 0)
                        {
                            // Buttons released - now wait the grace period
                            // This gives parent dialogs time to sync their button state
                            System.Threading.Thread.Sleep(gracePeriodMs);
                            return;
                        }
                    }
                    System.Threading.Thread.Sleep(16); // ~60Hz polling
                }
                // Timeout reached - still add grace period
                System.Threading.Thread.Sleep(gracePeriodMs);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error in WaitForButtonRelease");
            }
        }

    }
}
