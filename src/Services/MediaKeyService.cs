using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using UniPlaySong.Common;

namespace UniPlaySong.Services
{
    // Listens for global media key presses (Play/Pause, Next, Previous, Stop)
    // using Win32 RegisterHotKey with a hidden HwndSource message window.
    public class MediaKeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int ID_PLAY_PAUSE = 1;
        private const int ID_NEXT = 2;
        private const int ID_PREV = 3;
        private const int ID_STOP = 4;

        private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
        private const uint VK_MEDIA_PREV_TRACK = 0xB1;
        private const uint VK_MEDIA_STOP = 0xB2;
        private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;

        private HwndSource _hwndSource;
        private readonly bool[] _registered = new bool[5]; // index 1-4
        private readonly FileLogger _fileLogger;

        public event Action PlayPausePressed;
        public event Action NextTrackPressed;
        public event Action PreviousTrackPressed;

        public MediaKeyService(FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
        }

        public void Start()
        {
            if (_hwndSource != null) return;

            var parameters = new HwndSourceParameters("UniPlaySongMediaKeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0
            };
            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);

            var hwnd = _hwndSource.Handle;
            _registered[ID_PLAY_PAUSE] = RegisterHotKey(hwnd, ID_PLAY_PAUSE, MOD_NOREPEAT, VK_MEDIA_PLAY_PAUSE);
            _registered[ID_NEXT] = RegisterHotKey(hwnd, ID_NEXT, MOD_NOREPEAT, VK_MEDIA_NEXT_TRACK);
            _registered[ID_PREV] = RegisterHotKey(hwnd, ID_PREV, MOD_NOREPEAT, VK_MEDIA_PREV_TRACK);
            _registered[ID_STOP] = RegisterHotKey(hwnd, ID_STOP, MOD_NOREPEAT, VK_MEDIA_STOP);

            _fileLogger?.Debug($"MediaKeyService started: PlayPause={_registered[ID_PLAY_PAUSE]}, Next={_registered[ID_NEXT]}, Prev={_registered[ID_PREV]}, Stop={_registered[ID_STOP]}");
        }

        public void Stop()
        {
            if (_hwndSource == null) return;

            var hwnd = _hwndSource.Handle;
            for (int i = 1; i <= 4; i++)
            {
                if (_registered[i])
                {
                    UnregisterHotKey(hwnd, i);
                    _registered[i] = false;
                }
            }

            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;

            _fileLogger?.Debug("MediaKeyService stopped");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case ID_PLAY_PAUSE:
                        PlayPausePressed?.Invoke();
                        break;
                    case ID_NEXT:
                        NextTrackPressed?.Invoke();
                        break;
                    case ID_PREV:
                        PreviousTrackPressed?.Invoke();
                        break;
                    case ID_STOP:
                        PlayPausePressed?.Invoke(); // Stop = Play/Pause toggle
                        break;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose() => Stop();
    }
}
