using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK.Controls;
using UniPlaySong.Audio;
using UniPlaySong.Common;
using UniPlaySong.Services;
using static UniPlaySong.Controls.VisualizerBridge;

namespace UniPlaySong.Controls
{
    // Theme element "UPS_MusicVisualControl" (experimental, behind MusicVisualizerEnabled): a MilkDrop visualizer drawn by
    // a separate 64-bit renderer (Visualizer\UniPlaySongVisualizer.exe, projectM) and shown here with real transparency
    // over whatever the theme draws behind it. The renderer runs only while the element is on screen and the setting is
    // on, takes its music from UPS itself (VisualizerAudioFeed), and draws one frame per WPF frame. Never focusable or
    // hit-testable, so it can't take controller focus. Layout and native side: native/visualizer/.
    public class MusicVisualControl : PluginUserControl
    {
        private static readonly string Folder =
            Path.Combine(Path.GetDirectoryName(typeof(MusicVisualControl).Assembly.Location) ?? string.Empty, "Visualizer");
        private static readonly string RendererPath = Path.Combine(Folder, "UniPlaySongVisualizer.exe");
        // The one style that ships for now; native/visualizer/presets/make_presets.py generates the others.
        private static readonly string PresetPath = Path.Combine(Folder, "presets", "UPS - Glow Bars.milk");

        // The bridge holds one device and one set of shared textures, so only one element shows at a time.
        private static MusicVisualControl _owner;
        private static Task _rendererExited = Task.FromResult(0);
        private static int _starts;

        private readonly ISettingsProvider _settings;
        private readonly FileLogger _log;
        private readonly Image _image = new Image { Stretch = Stretch.Uniform };
        private readonly DispatcherTimer _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private UniPlaySongSettings _subscribedTo;
        private Process _renderer;
        private VisualizerAudioFeed _feed;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _view;
        private IntPtr _tick;
        private D3DImage _d3d;
        private int _w, _h;
        private long _lastFrame;
        private int _generation;
        private Size _startedAt;

        public MusicVisualControl(ISettingsProvider settings, FileLogger log)
        {
            _settings = settings;
            _log = log;
            Focusable = false;
            IsHitTestVisible = false;
            Content = _image;

            // The renderer's frame size is fixed when it starts. When the element settles at another size (a page's text
            // loading, a resolution change), restart it so the frame matches again - the presets lay out around the
            // centre to line up with the page, and a scaled-to-fit frame would drift off it.
            _resizeTimer.Tick += (s, e) => { _resizeTimer.Stop(); if (_owner == this && IsVisible) { Stop(); Start(); } };
            SizeChanged += (s, e) =>
            {
                if (_owner != this) return;
                if (Math.Abs(e.NewSize.Width - _startedAt.Width) < 8 && Math.Abs(e.NewSize.Height - _startedAt.Height) < 8) return;
                _resizeTimer.Stop();
                _resizeTimer.Start();
            };
            // A collapsed element stays loaded, so visibility - not Loaded - decides whether the renderer runs.
            IsVisibleChanged += (s, e) => Refresh();
            Loaded += (s, e) => { Subscribe(); Refresh(); };
            Unloaded += (s, e) => { Unsubscribe(); Stop(); };
        }

        private bool Enabled => _settings?.Current?.MusicVisualizerEnabled == true;

        // Start or stop to match "on screen and switched on". Deferred to after layout, so the renderer can match the
        // element's real size.
        private void Refresh() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible && Enabled) Start();
            else Stop();
        }), DispatcherPriority.Loaded);

        // A settings save replaces the whole settings object, so follow it rather than the one seen at load.
        private void Subscribe()
        {
            if (_settings == null || _subscribedTo != null) return;
            _settings.SettingsChanged += OnSettingsReplaced;
            _subscribedTo = _settings.Current;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged += OnSettingChanged;
        }

        private void Unsubscribe()
        {
            if (_settings == null) return;
            _settings.SettingsChanged -= OnSettingsReplaced;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged -= OnSettingChanged;
            _subscribedTo = null;
        }

        private void OnSettingsReplaced(object sender, SettingsChangedEventArgs e)
        {
            if (_subscribedTo != null) _subscribedTo.PropertyChanged -= OnSettingChanged;
            _subscribedTo = e.NewSettings;
            if (_subscribedTo != null) _subscribedTo.PropertyChanged += OnSettingChanged;
            Refresh();
        }

        private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UniPlaySongSettings.MusicVisualizerEnabled)) Refresh();
        }

        private void Start()
        {
            if (_owner != null || !Enabled) return;
            // A new renderer must not start before the old one has gone: its shared memory would still be in use.
            if (!_rendererExited.IsCompleted)
            {
                _rendererExited.ContinueWith(_ => Refresh());
                return;
            }
            if (!File.Exists(RendererPath) || !File.Exists(PresetPath))
            {
                _log?.Warn($"[Visualizer] Not installed: {RendererPath} or its preset is missing");
                return;
            }

            _owner = this;
            int generation = ++_generation;
            int pid = Process.GetCurrentProcess().Id;
            string channel = $@"Local\UniPlaySong_Visualizer_{pid}_{++_starts}";
            try
            {
                Load(Folder);
                _feed = VisualizerAudioFeed.Open(channel + "_Audio");
            }
            catch (Exception ex)
            {
                _log?.Warn($"[Visualizer] Could not start: {ex.Message}");
                _owner = null;
                return;
            }

            // Render at the element's size in device pixels, so nothing is scaled up.
            var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice ?? Matrix.Identity;
            int w = (int)(ActualWidth * toDevice.M11) & ~1, h = (int)(ActualHeight * toDevice.M22) & ~1;
            if (w < 64 || h < 64) { w = 1280; h = 720; }
            _startedAt = new Size(ActualWidth, ActualHeight);
            w = Math.Min(w, 2560); h = Math.Min(h, 1440);

            try
            {
                // The parent pid lets the renderer exit with Playnite even if Playnite crashes.
                _renderer = Process.Start(new ProcessStartInfo(RendererPath, $"\"{PresetPath}\" {w} {h} {channel} {pid}")
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Folder });
            }
            catch (Exception ex)
            {
                _log?.Warn($"[Visualizer] Renderer did not start: {ex.Message}");
                Stop();
                return;
            }
            _log?.Debug($"[Visualizer] Started {w}x{h}, channel {channel}");

            // The renderer's shared memory takes a moment to appear; don't hold the UI thread for it.
            Task.Run(() =>
            {
                MemoryMappedFile mmf = null;
                for (int i = 0; i < 100 && mmf == null; i++)
                {
                    try { mmf = MemoryMappedFile.OpenExisting(channel + "_Frames"); }
                    catch (FileNotFoundException) { System.Threading.Thread.Sleep(50); }
                }
                var view = mmf?.CreateViewAccessor();
                for (int i = 0; view != null && i < 100 && view.ReadUInt32(OffMagic) != FramesMagic; i++)
                    System.Threading.Thread.Sleep(20);
                Dispatcher.BeginInvoke(new Action(() => Attach(generation, channel, mmf, view)));
            });
        }

        private void Attach(int generation, string channel, MemoryMappedFile mmf, MemoryMappedViewAccessor view)
        {
            if (generation != _generation || view == null || view.ReadUInt32(OffMagic) != FramesMagic)
            {
                view?.Dispose(); mmf?.Dispose();
                if (generation == _generation)
                {
                    _log?.Warn("[Visualizer] Renderer never published its frames");
                    Stop();
                }
                return;
            }
            _mmf = mmf;
            _view = view;
            _w = view.ReadInt32(OffWidth);
            _h = view.ReadInt32(OffHeight);
            _tick = CreateEventW(IntPtr.Zero, false, false, channel + "_Tick");

            var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            int hr = hwnd == IntPtr.Zero ? -1 : vt_init(hwnd);
            for (int i = 0; hr == 0 && i < 3; i++) hr = vt_open(i, view.ReadUInt64(OffHandles + 8 * i), _w, _h);
            var back = hr == 0 ? vt_backbuffer(_w, _h) : IntPtr.Zero;
            if (back == IntPtr.Zero)
            {
                _log?.Warn($"[Visualizer] Could not open the renderer's textures (0x{hr:x8})");
                Stop();
                return;
            }

            _d3d = new D3DImage();
            _d3d.Lock();
            _d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, back, true);
            _d3d.Unlock();
            _image.Source = _d3d;
            _lastFrame = 0;
            CompositionTarget.Rendering += OnRendering;
        }

        // One renderer frame per WPF frame: tell it what's being heard, ask for the next frame, show the newest ready.
        private void OnRendering(object sender, EventArgs e)
        {
            _feed?.PublishPlayed();
            SetEvent(_tick);
            long frame = _view.ReadInt64(OffFrame);
            if (frame == _lastFrame || frame <= 0 || !_d3d.IsFrontBufferAvailable) return;
            _lastFrame = frame;
            _d3d.Lock();
            vt_copy(_view.ReadInt32(OffLatest));
            _d3d.AddDirtyRect(new Int32Rect(0, 0, _w, _h));
            _d3d.Unlock();
        }

        private void Stop()
        {
            if (_owner != this) return;
            _generation++;
            CompositionTarget.Rendering -= OnRendering;
            if (_d3d != null)
            {
                _d3d.Lock();
                _d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _d3d.Unlock();
                _d3d = null;
                _image.Source = null;
            }
            vt_shutdown();
            if (_view != null) { _view.Write(OffStop, 1); SetEvent(_tick); }
            var renderer = _renderer;
            _rendererExited = Task.Run(() =>
            {
                try { if (renderer != null && !renderer.WaitForExit(3000)) renderer.Kill(); } catch { }
                renderer?.Dispose();
            });
            _renderer = null;
            _view?.Dispose(); _view = null;
            _mmf?.Dispose(); _mmf = null;
            _feed?.Dispose(); _feed = null;
            _owner = null;
        }
    }

    // The x86 bridge DLL (Visualizer\UniPlaySongVisualizerBridge.dll) that opens the renderer's shared D3D11 textures
    // through D3D9Ex for D3DImage, and the frame header layout (native/visualizer/visualizer_shared.h, VisFrames).
    internal static class VisualizerBridge
    {
        private const string Dll = "UniPlaySongVisualizerBridge.dll";
        private static bool _loaded;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr CreateEventW(IntPtr a, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] internal static extern bool SetEvent(IntPtr h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_init(IntPtr hwnd);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_open(int index, ulong handle, int w, int h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr vt_backbuffer(int w, int h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_copy(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void vt_shutdown();

        internal const int OffMagic = 0, OffWidth = 4, OffHeight = 8, OffHandles = 16, OffLatest = 40, OffFrame = 48, OffStop = 80;
        internal const uint FramesMagic = 0x31585456u; // "VTX1"

        // DllImport by name binds to an already-loaded module; the bridge lives in the Visualizer subfolder, which the
        // loader doesn't search, so load it by full path first.
        internal static void Load(string folder)
        {
            if (_loaded) return;
            if (LoadLibraryW(Path.Combine(folder, Dll)) == IntPtr.Zero)
                throw new DllNotFoundException($"{Dll} (error {Marshal.GetLastWin32Error()})");
            _loaded = true;
        }
    }
}
