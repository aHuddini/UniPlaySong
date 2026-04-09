using System.Runtime.InteropServices;
using System.Windows.Interop;
using Playnite;

namespace UniPlaySong.Services;

// Global media key interception via Win32 RegisterHotKey.
// Creates a hidden message-only window to receive WM_HOTKEY messages.
class MediaKeyService : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetLogger();

    private const int WM_HOTKEY = 0x0312;
    private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const int VK_MEDIA_NEXT_TRACK = 0xB0;
    private const int VK_MEDIA_STOP = 0xB2;

    private const int HOTKEY_PLAYPAUSE = 1;
    private const int HOTKEY_NEXT = 2;
    private const int HOTKEY_STOP = 3;

    private HwndSource? _hwndSource;
    private bool _disposed;
    private bool _registered;

    public event Action? PlayPausePressed;
    public event Action? NextTrackPressed;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void Start()
    {
        if (_registered) return;

        try
        {
            // Create a hidden message-only window for receiving hotkey messages
            _hwndSource = new HwndSource(new HwndSourceParameters("UniPlaySong_MediaKeys")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0
            });
            _hwndSource.AddHook(WndProc);

            var hwnd = _hwndSource.Handle;
            var r1 = RegisterHotKey(hwnd, HOTKEY_PLAYPAUSE, 0, VK_MEDIA_PLAY_PAUSE);
            var r2 = RegisterHotKey(hwnd, HOTKEY_NEXT, 0, VK_MEDIA_NEXT_TRACK);
            var r3 = RegisterHotKey(hwnd, HOTKEY_STOP, 0, VK_MEDIA_STOP);

            _registered = true;
            _logger.Info($"MediaKeyService: started (PlayPause={r1}, Next={r2}, Stop={r3})");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MediaKeyService: failed to start");
        }
    }

    public void Stop()
    {
        if (!_registered || _hwndSource == null) return;

        try
        {
            var hwnd = _hwndSource.Handle;
            UnregisterHotKey(hwnd, HOTKEY_PLAYPAUSE);
            UnregisterHotKey(hwnd, HOTKEY_NEXT);
            UnregisterHotKey(hwnd, HOTKEY_STOP);

            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
            _registered = false;
            _logger.Info("MediaKeyService: stopped");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "MediaKeyService: failed to stop");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_PLAYPAUSE:
                case HOTKEY_STOP: // Stop key acts as play/pause toggle
                    _logger.Info($"MediaKey: PlayPause (id={id})");
                    PlayPausePressed?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_NEXT:
                    _logger.Info("MediaKey: NextTrack");
                    NextTrackPressed?.Invoke();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
