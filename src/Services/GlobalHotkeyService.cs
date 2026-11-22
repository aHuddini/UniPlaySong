using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    public enum ShortcutAction { PlayPause, Next, CalmDown, VolumeUp, VolumeDown }

    // System-wide keyboard shortcuts through RegisterHotKey on a hidden window. Knows keys, not music: it raises
    // Pressed and the plugin decides what an action does. Windows grants a combination to one app at a time, so a
    // refused registration means another app owns it; Refused records which, for the settings page.
    public sealed class GlobalHotkeyService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int ProbeId = 0xBFFF;

        private readonly FileLogger _fileLogger;
        private HwndSource _window;
        private IReadOnlyDictionary<ShortcutAction, string> _bindings = new Dictionary<ShortcutAction, string>();
        private readonly Dictionary<ShortcutAction, KeyCombo> _held = new Dictionary<ShortcutAction, KeyCombo>();
        private readonly HashSet<ShortcutAction> _refused = new HashSet<ShortcutAction>();
        private bool _suspended;

        public event Action<ShortcutAction> Pressed;

        public IReadOnlyCollection<ShortcutAction> Refused => _refused;

        // Must be constructed on the UI thread: the window's messages, and so Pressed, arrive there.
        public GlobalHotkeyService(FileLogger fileLogger)
        {
            _fileLogger = fileLogger;
            _window = new HwndSource(new HwndSourceParameters("UniPlaySongHotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
            _window.AddHook(WndProc);
        }

        public void Apply(IReadOnlyDictionary<ShortcutAction, string> bindings)
        {
            // RegisterHotKey only works from the thread that owns the window.
            if (_window != null && !_window.Dispatcher.CheckAccess())
            {
                _window.Dispatcher.BeginInvoke(new Action(() => Apply(bindings)));
                return;
            }
            _bindings = bindings ?? new Dictionary<ShortcutAction, string>();
            if (!_suspended) Register();
        }

        // While the settings window is open: a shortcut there would change settings behind the dialog, and pressing a
        // bound one in the recorder must record it, not fire it. Bindings applied meanwhile wait for Resume.
        public void Suspend()
        {
            _suspended = true;
            UnregisterAll();
        }

        public void Resume()
        {
            _suspended = false;
            Register();
        }

        public bool IsAvailable(KeyCombo combo)
        {
            if (_held.ContainsValue(combo)) return true;
            if (_window == null) return false;
            if (!RegisterHotKey(_window.Handle, ProbeId, combo.Win32Modifiers | MOD_NOREPEAT, combo.VirtualKey)) return false;
            UnregisterHotKey(_window.Handle, ProbeId);
            return true;
        }

        private void Register()
        {
            // Unregister first: re-registering a combo we still hold would be refused as "in use".
            UnregisterAll();
            _refused.Clear();
            if (_window == null) return;

            foreach (var binding in _bindings)
            {
                if (!KeyCombo.TryParse(binding.Value, out var combo)) continue;
                if (RegisterHotKey(_window.Handle, IdOf(binding.Key), combo.Win32Modifiers | MOD_NOREPEAT, combo.VirtualKey))
                {
                    _held[binding.Key] = combo;
                    _fileLogger?.Debug($"[Shortcuts] {binding.Key} = {combo}");
                }
                else
                {
                    _refused.Add(binding.Key);
                    _fileLogger?.Debug($"[Shortcuts] {binding.Key} = {combo} refused (error {Marshal.GetLastWin32Error()}): another app holds it");
                }
            }
        }

        private void UnregisterAll()
        {
            if (_window != null)
                foreach (var action in _held.Keys)
                    UnregisterHotKey(_window.Handle, IdOf(action));
            _held.Clear();
        }

        private static int IdOf(ShortcutAction action) => (int)action + 1;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                var action = (ShortcutAction)(wParam.ToInt32() - 1);
                if (_held.ContainsKey(action))
                {
                    handled = true;
                    Pressed?.Invoke(action);
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_window == null) return;
            _window.RemoveHook(WndProc);
            _window.Dispose();
            _window = null;
        }
    }
}
