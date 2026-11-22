using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Controls.Settings
{
    // A box's text: "Press keys…" while it records, else the saved shortcut spaced ("Media Next Track"), or "Not set".
    public class ShortcutTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length > 1 && values[1] is bool recording && recording) return "Press keys…";
            var saved = values[0] as string;
            return string.IsNullOrEmpty(saved) ? "Not set" : Regex.Replace(saved, "(?<=[a-z])(?=[A-Z])", " ");
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Each row's Tag names its ShortcutAction; the setting is "Shortcut" + Tag. The boxes bind to the setting, so this
    // page's two copies (General and Quick Start) and a reset or import all show the same value. The live shortcuts
    // are suspended while the settings window is open (the view model's BeginEdit/EndEdit/CancelEdit), so a bound
    // one records instead of firing.
    public partial class KeyboardShortcutsPage : UserControl
    {
        private static readonly ShortcutTextConverter TextConverter = new ShortcutTextConverter();

        public KeyboardShortcutsPage()
        {
            InitializeComponent();
            foreach (var box in Boxes)
            {
                box.SetBinding(TextBox.TextProperty, new MultiBinding
                {
                    Converter = TextConverter,
                    Mode = BindingMode.OneWay,
                    Bindings =
                    {
                        new Binding("Settings.Shortcut" + box.Tag),
                        new Binding(nameof(IsKeyboardFocused)) { RelativeSource = RelativeSource.Self },
                    },
                });
                box.PreviewKeyDown += Box_PreviewKeyDown;
            }
            Loaded += (s, e) =>
            {
                var refused = Hotkeys?.Refused;
                if (refused != null)
                    foreach (var action in refused) Warn(action.ToString(), "Another app is already using this shortcut.");
            };
        }

        private TextBox[] Boxes => new[] { BoxPlayPause, BoxNext, BoxCalmDown, BoxVolumeUp, BoxVolumeDown };

        // The settings window's view model; a headless page render has a plain holder instead, and no hotkeys.
        private UniPlaySongSettings Settings =>
            DataContext?.GetType().GetProperty("Settings")?.GetValue(DataContext) as UniPlaySongSettings;

        private GlobalHotkeyService Hotkeys => (DataContext as UniPlaySongSettingsViewModel)?.Hotkeys;

        private string Get(string action) =>
            typeof(UniPlaySongSettings).GetProperty("Shortcut" + action)?.GetValue(Settings) as string;

        private void Set(string action, string value)
        {
            if (Settings == null) return;
            typeof(UniPlaySongSettings).GetProperty("Shortcut" + action)?.SetValue(Settings, value);
        }

        private void Warn(string action, string text)
        {
            if (!(FindName("Warn" + action) is TextBlock warning)) return;
            warning.Text = text ?? "";
            warning.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return; // still moves focus
            e.Handled = true;
            var box = (TextBox)sender;
            var action = (string)box.Tag;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            switch (key)
            {
                case Key.LeftCtrl: case Key.RightCtrl: case Key.LeftAlt: case Key.RightAlt:
                case Key.LeftShift: case Key.RightShift: case Key.LWin: case Key.RWin:
                    return; // wait for the key that completes the combination
                case Key.Escape:
                    Keyboard.ClearFocus();
                    return;
                case Key.Back:
                case Key.Delete:
                    Set(action, "");
                    Warn(action, null);
                    Keyboard.ClearFocus();
                    return;
            }

            if (!KeyCombo.TryCreate(Keyboard.Modifiers, key, out var combo))
            {
                Warn(action, "Add Ctrl, Alt, Shift or Win to this key.");
                return;
            }

            var text = combo.ToString();
            // One combination, one action: the newest assignment keeps it.
            foreach (var other in Boxes.Where(b => (string)b.Tag != action && Get((string)b.Tag) == text))
            {
                Set((string)other.Tag, "");
                Warn((string)other.Tag, null);
            }

            Set(action, text);
            Warn(action, Hotkeys != null && !Hotkeys.IsAvailable(combo) ? "Another app is already using this shortcut." : null);
            Keyboard.ClearFocus();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var action = (string)((FrameworkElement)sender).Tag;
            Set(action, "");
            Warn(action, null);
        }
    }
}
