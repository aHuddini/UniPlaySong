using System;
using System.Linq;
using System.Windows.Input;

namespace UniPlaySong.Common
{
    // A keyboard shortcut as saved in settings ("Ctrl+Alt+P", "MediaPlayPause") and as Windows registers it. A shortcut
    // works system-wide, so a key used for typing needs a modifier; F-keys and media/browser/volume keys don't.
    public struct KeyCombo : IEquatable<KeyCombo>
    {
        private static readonly KeyConverter Keys = new KeyConverter();

        public ModifierKeys Modifiers { get; }
        public Key Key { get; }

        private KeyCombo(ModifierKeys modifiers, Key key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public static bool TryCreate(ModifierKeys modifiers, Key key, out KeyCombo combo)
        {
            combo = new KeyCombo(modifiers, key);
            if (key == Key.None || IsModifierKey(key)) return false;
            return modifiers != ModifierKeys.None || WorksAlone(key);
        }

        public static bool TryParse(string text, out KeyCombo combo)
        {
            combo = default(KeyCombo);
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+').Select(p => p.Trim()).ToArray();
            var modifiers = ModifierKeys.None;
            foreach (var part in parts.Take(parts.Length - 1))
            {
                switch (part)
                {
                    case "Ctrl": modifiers |= ModifierKeys.Control; break;
                    case "Alt": modifiers |= ModifierKeys.Alt; break;
                    case "Shift": modifiers |= ModifierKeys.Shift; break;
                    case "Win": modifiers |= ModifierKeys.Windows; break;
                    default: return false;
                }
            }

            Key key;
            try { key = (Key)Keys.ConvertFromInvariantString(parts[parts.Length - 1]); }
            catch (Exception) { return false; }
            return TryCreate(modifiers, key, out combo);
        }

        public override string ToString()
        {
            var text = "";
            if ((Modifiers & ModifierKeys.Control) != 0) text += "Ctrl+";
            if ((Modifiers & ModifierKeys.Alt) != 0) text += "Alt+";
            if ((Modifiers & ModifierKeys.Shift) != 0) text += "Shift+";
            if ((Modifiers & ModifierKeys.Windows) != 0) text += "Win+";
            return text + Keys.ConvertToInvariantString(Key);
        }

        // MOD_ALT 0x1, MOD_CONTROL 0x2, MOD_SHIFT 0x4, MOD_WIN 0x8: the same bits as ModifierKeys.
        public uint Win32Modifiers => (uint)Modifiers & 0xF;

        public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

        private static bool IsModifierKey(Key key) =>
            key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
            || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin || key == Key.System;

        private static bool WorksAlone(Key key) =>
            (key >= Key.F1 && key <= Key.F24) || (key >= Key.BrowserBack && key <= Key.LaunchApplication2);

        public bool Equals(KeyCombo other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object obj) => obj is KeyCombo other && Equals(other);
        public override int GetHashCode() => ((int)Modifiers << 16) ^ (int)Key;
    }
}
