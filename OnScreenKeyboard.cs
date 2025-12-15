using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Playnite.SDK;

namespace UniPlaySong
{
    /// <summary>
    /// On-Screen Keyboard control optimized for controller navigation
    /// Implemented as code-behind control for maximum compatibility
    /// </summary>
    public class OnScreenKeyboard : UserControl
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        // UI Elements
        private TextBox _textDisplay;
        private Button _shiftButton;
        private List<Button> _allButtons = new List<Button>();
        
        // State
        private bool _isShiftActive = false;
        private int _currentButtonIndex = 0;
        
        // Events
        public event EventHandler<string> TextChanged;
        public event EventHandler<string> TextConfirmed;
        public event EventHandler CloseRequested;
        
        // Text property
        private string _text = string.Empty;
        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                if (_textDisplay != null)
                {
                    _textDisplay.Text = _text;
                }
                TextChanged?.Invoke(this, _text);
            }
        }

        public OnScreenKeyboard()
        {
            try
            {
                InitializeKeyboard();
                Logger.Debug("OnScreenKeyboard initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize OnScreenKeyboard");
            }
        }

        private void InitializeKeyboard()
        {
            // Set control properties
            Background = new SolidColorBrush(Colors.White);
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            BorderThickness = new Thickness(1);
            Width = 580;
            MinHeight = 220;
            Focusable = true;
            
            // Main container
            var mainGrid = new Grid();
            mainGrid.Margin = new Thickness(8);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Title
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Text display
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Keyboard
            
            // Title bar
            var titleBar = CreateTitleBar();
            Grid.SetRow(titleBar, 0);
            mainGrid.Children.Add(titleBar);
            
            // Text display
            var textDisplayBorder = CreateTextDisplay();
            Grid.SetRow(textDisplayBorder, 1);
            mainGrid.Children.Add(textDisplayBorder);
            
            // Keyboard grid
            var keyboardGrid = CreateKeyboardGrid();
            Grid.SetRow(keyboardGrid, 2);
            mainGrid.Children.Add(keyboardGrid);
            
            Content = mainGrid;
            
            // Handle keyboard navigation
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += OnLoaded;
        }

        private Grid CreateTitleBar()
        {
            var grid = new Grid();
            grid.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            grid.Margin = new Thickness(0, 0, 0, 4);
            
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var title = new TextBlock
            {
                Text = "On-Screen Keyboard",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 4, 0, 4)
            };
            Grid.SetColumn(title, 0);
            grid.Children.Add(title);
            
            var closeButton = CreateKeyButton("×", 24, 24);
            closeButton.FontSize = 16;
            closeButton.FontWeight = FontWeights.Bold;
            closeButton.Margin = new Thickness(4);
            closeButton.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            closeButton.ToolTip = "Close (Escape)";
            Grid.SetColumn(closeButton, 1);
            grid.Children.Add(closeButton);
            
            return grid;
        }

        private Border CreateTextDisplay()
        {
            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 4, 8, 4)
            };
            
            _textDisplay = new TextBox
            {
                FontSize = 14,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxLength = 100,
                Text = _text
            };
            
            border.Child = _textDisplay;
            return border;
        }

        private Grid CreateKeyboardGrid()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Numbers
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // QWERTY
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // ASDF
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // ZXCV
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Space
            
            // Row 0: Numbers
            var row0 = CreateKeyRow(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" });
            var backspace = CreateKeyButton("⌫", 50, 30);
            backspace.Click += (s, e) => PerformBackspace();
            backspace.ToolTip = "Backspace";
            row0.Children.Add(backspace);
            _allButtons.Add(backspace);
            Grid.SetRow(row0, 0);
            grid.Children.Add(row0);
            
            // Row 1: QWERTY
            var row1 = CreateKeyRow(new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" });
            Grid.SetRow(row1, 1);
            grid.Children.Add(row1);
            
            // Row 2: ASDF
            var row2 = CreateKeyRow(new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" });
            Grid.SetRow(row2, 2);
            grid.Children.Add(row2);
            
            // Row 3: ZXCV with Shift and Clear
            var row3 = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
            
            _shiftButton = CreateKeyButton("Shift", 50, 30);
            _shiftButton.Click += (s, e) => ToggleShift();
            _shiftButton.ToolTip = "Shift (Toggle Case)";
            row3.Children.Add(_shiftButton);
            _allButtons.Add(_shiftButton);
            
            foreach (var key in new[] { "Z", "X", "C", "V", "B", "N", "M" })
            {
                var btn = CreateKeyButton(key);
                btn.Click += OnKeyClick;
                row3.Children.Add(btn);
                _allButtons.Add(btn);
            }
            
            var clearButton = CreateKeyButton("Clear", 50, 30);
            clearButton.Click += (s, e) => Text = string.Empty;
            clearButton.ToolTip = "Clear All";
            row3.Children.Add(clearButton);
            _allButtons.Add(clearButton);
            
            Grid.SetRow(row3, 3);
            grid.Children.Add(row3);
            
            // Row 4: Space and Enter
            var row4 = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
            
            var spaceButton = CreateKeyButton("Space", 200, 30);
            spaceButton.Click += (s, e) => AppendText(" ");
            spaceButton.ToolTip = "Space";
            row4.Children.Add(spaceButton);
            _allButtons.Add(spaceButton);
            
            var enterButton = CreateKeyButton("Enter", 60, 30);
            enterButton.Click += (s, e) => TextConfirmed?.Invoke(this, Text);
            enterButton.ToolTip = "Confirm (Enter)";
            row4.Children.Add(enterButton);
            _allButtons.Add(enterButton);
            
            Grid.SetRow(row4, 4);
            grid.Children.Add(row4);
            
            return grid;
        }

        private WrapPanel CreateKeyRow(string[] keys)
        {
            var panel = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            
            foreach (var key in keys)
            {
                var button = CreateKeyButton(key);
                button.Click += OnKeyClick;
                panel.Children.Add(button);
                _allButtons.Add(button);
            }
            
            return panel;
        }

        private Button CreateKeyButton(string content, double width = 32, double height = 30)
        {
            var button = new Button
            {
                Content = content,
                Width = width,
                Height = height,
                Margin = new Thickness(2),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                Focusable = true,
                FocusVisualStyle = CreateFocusStyle()
            };
            
            return button;
        }

        private Style CreateFocusStyle()
        {
            var style = new Style(typeof(Control));
            
            var template = new ControlTemplate(typeof(Control));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(33, 150, 243)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(3));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.MarginProperty, new Thickness(-3));
            
            var effect = new DropShadowEffect
            {
                Color = Color.FromRgb(33, 150, 243),
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.6
            };
            border.SetValue(Border.EffectProperty, effect);
            
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            
            return style;
        }

        private void OnKeyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button)
                {
                    var key = button.Content?.ToString();
                    if (!string.IsNullOrEmpty(key) && key.Length == 1)
                    {
                        // Apply shift state
                        if (_isShiftActive)
                        {
                            key = key.ToUpper();
                            ToggleShift(); // Turn off shift after use
                        }
                        else
                        {
                            key = key.ToLower();
                        }
                        
                        AppendText(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error handling key click");
            }
        }

        private void AppendText(string character)
        {
            if (Text.Length < 100)
            {
                Text += character;
            }
        }

        private void PerformBackspace()
        {
            if (Text.Length > 0)
            {
                Text = Text.Substring(0, Text.Length - 1);
            }
        }

        private void ToggleShift()
        {
            _isShiftActive = !_isShiftActive;
            UpdateShiftButtonStyle();
        }

        private void UpdateShiftButtonStyle()
        {
            if (_shiftButton != null)
            {
                if (_isShiftActive)
                {
                    _shiftButton.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    _shiftButton.Foreground = Brushes.White;
                    _shiftButton.BorderBrush = new SolidColorBrush(Color.FromRgb(25, 118, 210));
                }
                else
                {
                    _shiftButton.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                    _shiftButton.Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51));
                    _shiftButton.BorderBrush = new SolidColorBrush(Color.FromRgb(189, 189, 189));
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Focus the first button for controller navigation
                if (_allButtons.Count > 0)
                {
                    _allButtons[0].Focus();
                    _currentButtonIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error setting initial focus in OSK");
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case Key.Left:
                        NavigateButtons(-1);
                        e.Handled = true;
                        break;
                    case Key.Right:
                        NavigateButtons(1);
                        e.Handled = true;
                        break;
                    case Key.Up:
                        NavigateButtons(-10); // Approximate row jump
                        e.Handled = true;
                        break;
                    case Key.Down:
                        NavigateButtons(10); // Approximate row jump
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        CloseRequested?.Invoke(this, EventArgs.Empty);
                        e.Handled = true;
                        break;
                    case Key.Back:
                        PerformBackspace();
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error handling key down in OSK");
            }
        }

        private void NavigateButtons(int direction)
        {
            if (_allButtons.Count == 0) return;
            
            try
            {
                // Find currently focused button
                var focusedElement = Keyboard.FocusedElement as Button;
                if (focusedElement != null && _allButtons.Contains(focusedElement))
                {
                    _currentButtonIndex = _allButtons.IndexOf(focusedElement);
                }
                
                // Move to next/previous button
                _currentButtonIndex = (_currentButtonIndex + direction);
                
                // Clamp to valid range
                if (_currentButtonIndex < 0)
                    _currentButtonIndex = _allButtons.Count - 1;
                else if (_currentButtonIndex >= _allButtons.Count)
                    _currentButtonIndex = 0;
                
                // Focus the new button
                _allButtons[_currentButtonIndex].Focus();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Error navigating OSK buttons");
            }
        }

        /// <summary>
        /// Set initial text
        /// </summary>
        public void SetText(string text)
        {
            Text = text ?? string.Empty;
            if (_textDisplay != null)
            {
                _textDisplay.CaretIndex = Text.Length;
            }
        }

        /// <summary>
        /// Focus a specific button by index
        /// </summary>
        public void FocusButton(int index)
        {
            if (index >= 0 && index < _allButtons.Count)
            {
                _allButtons[index].Focus();
                _currentButtonIndex = index;
            }
        }
    }
}