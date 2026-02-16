using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Helper for creating dialogs and toast notifications
    public static class DialogHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // ============================================================================
        // Color Constants
        // ============================================================================

        /// Dialog background (#212121)
        public static readonly Color DefaultDarkBackground = Color.FromRgb(33, 33, 33);

        /// Toast background (#1E1E1E)
        public static readonly Color ToastBackgroundColor = Color.FromRgb(30, 30, 30);

        /// Success border - Material Design green (#4CAF50)
        public static readonly Color ToastSuccessBorderColor = Color.FromRgb(76, 175, 80);

        /// Success accent (#81C784)
        public static readonly Color ToastSuccessAccentColor = Color.FromRgb(129, 199, 132);

        /// Error border - Material Design red (#F44336)
        public static readonly Color ToastErrorBorderColor = Color.FromRgb(244, 67, 54);

        /// Error accent (#FF8A80)
        public static readonly Color ToastErrorAccentColor = Color.FromRgb(255, 138, 128);

        /// Toast text (#E0E0E0)
        public static readonly Color ToastTextColor = Color.FromRgb(224, 224, 224);

        /// Default border (#424242) - configurable via ToastBorderColorValue
        public static readonly Color ToastOuterBorderColorDefault = Color.FromRgb(66, 66, 66);

        // Controller Dialog UI Colors
        private static readonly Color ErrorTextColor = Color.FromRgb(255, 100, 100);          // #FF6464
        private static readonly Color HintTextColor = Color.FromRgb(150, 150, 150);           // #969696
        private static readonly Color ButtonBlue = Color.FromRgb(33, 150, 243);               // Material Blue #2196F3
        private static readonly Color ButtonUnselectedBg = Color.FromRgb(60, 60, 60);         // #3C3C3C
        private static readonly Color ButtonUnselectedBorder = Color.FromRgb(100, 100, 100);  // #646464

        // ============================================================================
        // Configurable Toast Settings (synced from UniPlaySongSettings)
        // ============================================================================

        /// Border color as 0xRRGGBB (default: #2A2A2A)
        public static uint ToastBorderColorValue = 0x2A2A2A;

        /// Border thickness in pixels (default: 1)
        public static double ToastBorderThickness = 1;

        public enum ToastPosition { TopRight, TopLeft, BottomRight, BottomLeft, TopCenter, BottomCenter }

        /// Where toasts appear on screen (default: TopRight)
        public static ToastPosition CurrentToastPosition = ToastPosition.TopRight;

        /// Margin from screen edge in pixels (default: 30)
        public static int ToastEdgeMargin = 30;

        /// Enable acrylic blur effect (default: true, requires Windows 10 1803+)
        public static bool EnableToastAcrylicBlur = true;

        /// Blur opacity 0-255 (default: 94 = 37%)
        public static byte ToastBlurOpacity = 94;

        /// Blur tint color as 0xRRGGBB (default: #000521)
        public static uint ToastBlurTintColor = 0x000521;

        /// Blur mode: 0 = Basic, 1 = Acrylic (default: 1)
        public static int ToastBlurMode = 1;

        /// Corner radius in pixels (default: 0 to avoid blur artifacts)
        public static double ToastCornerRadius = 0;

        /// Toast width in pixels (default: 420)
        public static double ToastWidth = 420;

        /// Toast min height in pixels (default: 90)
        public static double ToastMinHeight = 90;

        /// Toast max height in pixels (default: 180)
        public static double ToastMaxHeight = 180;

        /// Display duration in ms (default: 4000)
        public static int ToastDurationMs = 4000;

        // ============================================================================
        // Helper Methods
        // ============================================================================

        /// Parses hex color string ("RRGGBB" or "#RRGGBB") to uint (0xRRGGBB). Returns defaultValue on error.
        private static uint ParseHexColor(string hexColor, uint defaultValue)
        {
            if (string.IsNullOrEmpty(hexColor)) return defaultValue;
            try
            {
                var hex = hexColor.TrimStart('#');
                return hex.Length == 6 ? Convert.ToUInt32(hex, 16) : defaultValue;
            }
            catch { return defaultValue; }
        }

        /// Extracts RGB bytes from uint color (0xRRGGBB format).
        private static (byte R, byte G, byte B) HexToRgb(uint hexColor) =>
            ((byte)((hexColor >> 16) & 0xFF), (byte)((hexColor >> 8) & 0xFF), (byte)(hexColor & 0xFF));

        /// Syncs toast settings from UniPlaySongSettings to DialogHelper static fields.
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
            ToastBorderThickness = settings.ToastBorderThickness;

            ToastBlurTintColor = ParseHexColor(settings.ToastBlurTintColor, ToastBlurTintColor);
            ToastBorderColorValue = ParseHexColor(settings.ToastBorderColor, ToastBorderColorValue);
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

        /// Returns true if Windows 11 22H2+ (build 22621+) with DWMWA_SYSTEMBACKDROP_TYPE support.
        private static bool IsWindows11WithBackdropSupport()
        {
            try
            {
                var version = Environment.OSVersion.Version;
                return version.Major >= 10 && version.Build >= 22621;
            }
            catch { return false; }
        }

        /// Returns true if Windows 11 (build 22000+) with DWMWA_WINDOW_CORNER_PREFERENCE support.
        private static bool IsWindows11()
        {
            try
            {
                var version = Environment.OSVersion.Version;
                return version.Major >= 10 && version.Build >= 22000;
            }
            catch { return false; }
        }

        /// Sets a rounded rectangle region on the window at Win32 level (clips blur to rounded corners).
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

        /// Enables acrylic blur on a window (Windows 10 1803+, falls back to basic blur).
        private static void EnableWindowBlur(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var hwnd = windowHelper.EnsureHandle();

                // GradientColor format is AABBGGRR - convert from RGB to BGR and add alpha
                var (r, g, b) = HexToRgb(ToastBlurTintColor);
                uint gradientColor = ((uint)ToastBlurOpacity << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

                // Blur mode: 0 = Basic, 1 = Acrylic (with noise texture)
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

        // Gets DPI scale factor for primary screen (1.0 = 96 DPI, 1.5 = 144 DPI, etc.)
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

        /// Scales a dimension based on DPI scale factor.
        public static double ScaleForDpi(double baseDimension)
        {
            var scaleFactor = GetDpiScaleFactor();
            return baseDimension * scaleFactor;
        }

        /// Dialog window configuration options.
        public class DialogOptions
        {
            public string Title { get; set; } = "Dialog";
            public double Width { get; set; } = 600;
            public double Height { get; set; } = 500;
            public bool CanResize { get; set; } = true;
            public bool ShowMaximizeButton { get; set; } = true;
            public bool ShowMinimizeButton { get; set; } = false;
            public bool ShowCloseButton { get; set; } = true;
            public bool ShowInTaskbar { get; set; } = true;
            public bool Topmost { get; set; } = false;
            public bool ApplyDarkBackground { get; set; } = false;
            public bool SetOwner { get; set; } = false;
            public WindowStartupLocation StartupLocation { get; set; } = WindowStartupLocation.CenterOwner;
            /// DPI scaling (disabled by default - doesn't work well with remote streaming)
            public bool ScaleForDpi { get; set; } = false;
        }

        /// Creates a resizable dialog with maximize button.
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

        /// Creates a fixed-size dialog (no resize/maximize).
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

        /// Creates a fullscreen-optimized dialog (topmost, dark background when in fullscreen mode).
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

        /// Creates a dialog with full customization options.
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

        /// Attaches a closing handler that returns focus to Playnite's main window.
        public static void AddFocusReturnHandler(Window window, IPlayniteAPI playniteApi, string context = null)
        {
            if (window == null || playniteApi == null)
                return;

            window.Closing += (s, e) => ReturnFocusToMainWindow(playniteApi, context);
        }

        /// Returns focus to Playnite's main window (important for fullscreen mode).
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

        /// Shows a dialog and returns true if confirmed (DialogResult == true).
        public static bool ShowDialogAndGetResult(Window window)
        {
            return window?.ShowDialog() == true;
        }

        /// Shows a controller-friendly message dialog with XInput support (A/Enter to close).
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
                    Foreground = new SolidColorBrush(isError ? ErrorTextColor : Colors.White),
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
                    Foreground = new SolidColorBrush(HintTextColor),
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
                    Background = new SolidColorBrush(ButtonBlue),
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

        /// Shows a controller-friendly Yes/No dialog with XInput support. Returns true if Yes selected.
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

                    // Yes button: green when selected, gray when not
                    yesButton.Background = new SolidColorBrush(selectedIndex == 0 ? ToastSuccessBorderColor : ButtonUnselectedBg);
                    yesButton.BorderBrush = new SolidColorBrush(selectedIndex == 0 ? Colors.White : ButtonUnselectedBorder);
                    yesButton.BorderThickness = selectedIndex == 0 ? new Thickness(3) : new Thickness(1);

                    // No button: red when selected, gray when not
                    noButton.Background = new SolidColorBrush(selectedIndex == 1 ? ToastErrorBorderColor : ButtonUnselectedBg);
                    noButton.BorderBrush = new SolidColorBrush(selectedIndex == 1 ? Colors.White : ButtonUnselectedBorder);
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
                    Foreground = new SolidColorBrush(HintTextColor),
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

        /// Shows a non-blocking Playnite notification (auto-dismisses, no XInput issues).
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

        /// Shows a success notification (Info type).
        public static void ShowSuccessNotification(IPlayniteAPI playniteApi, string id, string message)
        {
            ShowNotification(playniteApi, id, message, NotificationType.Info);
        }

        /// Shows an error notification (Error type).
        public static void ShowErrorNotification(IPlayniteAPI playniteApi, string id, string message)
        {
            ShowNotification(playniteApi, id, message, NotificationType.Error);
        }

        /// Removes a notification by its ID.
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

        /// Positions a toast window based on CurrentToastPosition setting.
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

        /// Shows an auto-closing toast (works in fullscreen mode, no button dismiss needed).
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

                        // Create outer container - blur effect shows through the transparent background
                        var (borderR, borderG, borderB) = HexToRgb(ToastBorderColorValue);
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

        /// Shows a success toast (auto-closes).
        public static void ShowSuccessToast(IPlayniteAPI playniteApi, string message, string title = "Success", int? durationMs = null)
        {
            ShowAutoCloseToast(playniteApi, message, title, isError: false, durationMs: durationMs);
        }

        /// Shows an error toast (auto-closes, 25% longer duration than success).
        public static void ShowErrorToast(IPlayniteAPI playniteApi, string message, string title = "Error", int? durationMs = null)
        {
            // Error toasts get 25% more time by default for reading error details
            var actualDuration = durationMs ?? (int)(ToastDurationMs * 1.25);
            ShowAutoCloseToast(playniteApi, message, title, isError: true, durationMs: actualDuration);
        }

        // Gold celebration colors
        private static readonly Color CelebrationGoldBorder = Color.FromRgb(255, 193, 7);   // #FFC107 - Material Amber
        private static readonly Color CelebrationGoldAccent = Color.FromRgb(255, 215, 64);   // #FFD740
        private static readonly Color CelebrationGoldGlow = Color.FromArgb(60, 255, 193, 7); // Semi-transparent gold

        // Shows a celebration toast with gold accent and smooth radial glow pulse animation.
        // Positioned at top-center of screen. Uses WPF Storyboard for 60fps composition-thread animation.
        public static void ShowCelebrationToast(IPlayniteAPI playniteApi, string gameName)
        {
            if (playniteApi == null) return;

            try
            {
                var app = Application.Current;
                if (app == null) return;

                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Window toastWindow = null;
                        System.Windows.Threading.DispatcherTimer closeTimer = null;

                        // Content grid with gold accent bar
                        var contentGrid = new System.Windows.Controls.Grid();
                        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(4) });
                        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        var accentBar = new System.Windows.Shapes.Rectangle
                        {
                            Fill = new SolidColorBrush(CelebrationGoldBorder),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Margin = new Thickness(0.5, 0.5, 0, 0.5)
                        };
                        System.Windows.Controls.Grid.SetColumn(accentBar, 0);
                        contentGrid.Children.Add(accentBar);

                        // Text content with glow overlay behind it
                        var textContainer = new System.Windows.Controls.Grid();
                        System.Windows.Controls.Grid.SetColumn(textContainer, 1);
                        contentGrid.Children.Add(textContainer);

                        // Radial glow overlay (pulsing gold glow behind the text)
                        var glowBrush = new RadialGradientBrush
                        {
                            Center = new Point(0.5, 0.5),
                            GradientOrigin = new Point(0.5, 0.5),
                            RadiusX = 0.8,
                            RadiusY = 1.2,
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromArgb(50, 255, 215, 64), 0.0),
                                new GradientStop(Color.FromArgb(25, 255, 193, 7), 0.5),
                                new GradientStop(Color.FromArgb(0, 255, 193, 7), 1.0)
                            }
                        };
                        var glowRect = new System.Windows.Shapes.Rectangle
                        {
                            Fill = glowBrush,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Opacity = 0.0
                        };
                        textContainer.Children.Add(glowRect);

                        // Text grid
                        var textGrid = new System.Windows.Controls.Grid();
                        textGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                        textGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        textContainer.Children.Add(textGrid);

                        var titleBlock = new System.Windows.Controls.TextBlock
                        {
                            Text = "\u2B50 GAME COMPLETED \u2B50",
                            FontSize = 24,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(CelebrationGoldAccent),
                            Margin = new Thickness(16, 16, 20, 6),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = System.Windows.TextAlignment.Center
                        };
                        System.Windows.Controls.Grid.SetRow(titleBlock, 0);
                        textGrid.Children.Add(titleBlock);

                        var messageBlock = new System.Windows.Controls.TextBlock
                        {
                            FontSize = 18,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(ToastTextColor),
                            Margin = new Thickness(16, 4, 20, 16),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = System.Windows.TextAlignment.Center,
                            LineHeight = 28,
                            LineStackingStrategy = System.Windows.LineStackingStrategy.BlockLineHeight
                        };
                        messageBlock.Inlines.Add(new System.Windows.Documents.Run("Congratulations on clearing "));
                        messageBlock.Inlines.Add(new System.Windows.Documents.Run(gameName + "!")
                        {
                            FontWeight = FontWeights.Bold
                        });
                        System.Windows.Controls.Grid.SetRow(messageBlock, 1);
                        textGrid.Children.Add(messageBlock);

                        // Outer border with gold tint
                        var outerBorder = new Border
                        {
                            BorderBrush = new SolidColorBrush(CelebrationGoldBorder),
                            BorderThickness = new Thickness(Math.Max(ToastBorderThickness, 1)),
                            CornerRadius = new CornerRadius(ToastCornerRadius),
                            Background = new SolidColorBrush(Color.FromArgb(1, 45, 45, 45)),
                            Padding = new Thickness(0),
                            Child = contentGrid
                        };

                        var clipWrapper = new System.Windows.Controls.Grid { ClipToBounds = true };
                        clipWrapper.Children.Add(outerBorder);

                        toastWindow = new Window
                        {
                            Title = "Game Completed",
                            Width = ToastWidth,
                            SizeToContent = SizeToContent.Height,
                            MinHeight = ToastMinHeight,
                            MaxHeight = ToastMaxHeight,
                            WindowStyle = WindowStyle.None,
                            AllowsTransparency = true,
                            Background = Brushes.Transparent,
                            ResizeMode = ResizeMode.NoResize,
                            Content = clipWrapper,
                            Topmost = true,
                            ShowInTaskbar = false,
                            ShowActivated = false,
                            Focusable = false
                        };

                        clipWrapper.Loaded += (wrapperSender, wrapperArgs) =>
                        {
                            try
                            {
                                var wrapper = wrapperSender as System.Windows.Controls.Grid;
                                if (wrapper != null && wrapper.ActualWidth > 0 && wrapper.ActualHeight > 0)
                                {
                                    wrapper.Clip = new RectangleGeometry(
                                        new Rect(0, 0, wrapper.ActualWidth, wrapper.ActualHeight),
                                        ToastCornerRadius, ToastCornerRadius);
                                }
                            }
                            catch { }
                        };

                        toastWindow.SourceInitialized += (s, e) =>
                        {
                            try { if (EnableToastAcrylicBlur) EnableWindowBlur(toastWindow); }
                            catch { }
                        };

                        // Position at top-center of screen
                        toastWindow.Loaded += (s, e) =>
                        {
                            try
                            {
                                toastWindow.Left = (SystemParameters.PrimaryScreenWidth - toastWindow.ActualWidth) / 2;
                                toastWindow.Top = ToastEdgeMargin;
                            }
                            catch
                            {
                                toastWindow.Left = (SystemParameters.PrimaryScreenWidth - ToastWidth) / 2;
                                toastWindow.Top = ToastEdgeMargin;
                            }
                        };

                        // Smooth glow pulse using WPF Storyboard (runs on composition thread at 60fps)
                        var pulseAnimation = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            From = 0.0,
                            To = 0.8,
                            Duration = new Duration(TimeSpan.FromSeconds(0.8)),
                            AutoReverse = true,
                            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                            EasingFunction = new System.Windows.Media.Animation.SineEase
                            {
                                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                            }
                        };

                        // Auto-close after 6 seconds
                        var celebrationDuration = Math.Max(ToastDurationMs, 6000);
                        closeTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(celebrationDuration)
                        };
                        closeTimer.Tick += (s, e) =>
                        {
                            closeTimer.Stop();
                            try { toastWindow?.Close(); }
                            catch { }
                        };

                        toastWindow.MouseDown += (s, e) =>
                        {
                            closeTimer?.Stop();
                            try { toastWindow?.Close(); }
                            catch { }
                        };

                        // Release all resources when the window closes (animation, timer, visual tree)
                        toastWindow.Closed += (s, e) =>
                        {
                            try
                            {
                                closeTimer?.Stop();
                                closeTimer = null;
                                glowRect.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                                clipWrapper.Children.Clear();
                                contentGrid.Children.Clear();
                                textContainer.Children.Clear();
                                textGrid.Children.Clear();
                                toastWindow.Content = null;
                            }
                            catch { }
                        };

                        closeTimer.Start();
                        glowRect.BeginAnimation(System.Windows.UIElement.OpacityProperty, pulseAnimation);
                        toastWindow.Show();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Error showing celebration toast");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error dispatching celebration toast");
            }
        }

        /// Waits for controller A/B buttons to be released + grace period (prevents button leak to parent dialog).
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
