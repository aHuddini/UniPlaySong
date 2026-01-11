using System;
using System.Windows;
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
        /// Wait for all controller buttons to be released.
        /// Call this after modal dialogs close to prevent button presses from
        /// "leaking" into the parent dialog.
        /// </summary>
        private static void WaitForButtonRelease(int timeoutMs = 500)
        {
            try
            {
                var startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                    if (XInputWrapper.XInputGetState(0, ref state) == 0)
                    {
                        // Check if all buttons are released (only check face buttons, D-pad, shoulders, triggers)
                        ushort relevantButtons = (ushort)(
                            XInputWrapper.XINPUT_GAMEPAD_A |
                            XInputWrapper.XINPUT_GAMEPAD_B |
                            XInputWrapper.XINPUT_GAMEPAD_X |
                            XInputWrapper.XINPUT_GAMEPAD_Y |
                            XInputWrapper.XINPUT_GAMEPAD_DPAD_UP |
                            XInputWrapper.XINPUT_GAMEPAD_DPAD_DOWN |
                            XInputWrapper.XINPUT_GAMEPAD_DPAD_LEFT |
                            XInputWrapper.XINPUT_GAMEPAD_DPAD_RIGHT |
                            XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER |
                            XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER
                        );

                        if ((state.Gamepad.wButtons & relevantButtons) == 0 &&
                            state.Gamepad.bLeftTrigger < 50 &&
                            state.Gamepad.bRightTrigger < 50)
                        {
                            // All buttons released, wait a tiny bit more for debounce
                            System.Threading.Thread.Sleep(50);
                            return;
                        }
                    }
                    System.Threading.Thread.Sleep(16); // ~60Hz polling
                }
                Logger.Debug("WaitForButtonRelease: timeout reached, some buttons may still be pressed");
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error in WaitForButtonRelease");
            }
        }

    }
}
