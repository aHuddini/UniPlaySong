using System;
using System.Collections.Generic;
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

        // The bridge holds one device and one set of shared textures, so only one element shows at a time.
        private static MusicVisualControl _owner;
        private static Task _rendererExited = Task.FromResult(0);
        private static int _starts;

        private readonly ISettingsProvider _settings;
        private readonly FileLogger _log;
        // Sized in code to the frame's box in device pixels, centred: shown 1:1, never stretched to the element. No layout
        // rounding: it rounds to the DPI's grid, which under Fullscreen's Viewbox isn't the pixel grid - a 988 px frame
        // (592.8 units) came out 593 units, 988.33 px, and every row was resampled. _snap puts it on whole pixels instead.
        private readonly Image _image = new Image
        {
            Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = false
        };
        private readonly TranslateTransform _snap = new TranslateTransform();   // the image's sub-pixel nudge, see OnRendering
        private Vector _deviceScale = new Vector(1, 1);
        private string _shownAt;   // where the frame was last logged as shown (debug log)
        private readonly DispatcherTimer _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private UniPlaySongSettings _subscribedTo;
        private Process _renderer;
        private VisualizerAudioFeed _feed;
        private VisualizerSongAnalysis _songAnalysis;   // the Jukebox Fountain only: its beats and sections need the song analysed
        private readonly string _otherMusicFolder;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _view;
        private IntPtr _tick;
        private D3DImage _d3d;
        private int _w, _h;
        private long _lastFrame;
        private int _generation;
        private Size _startedAt;
        private string _startedPreset;
        private Window _window;

        // otherMusicFolder: where song analyses of music outside the game folders go (the Jukebox Fountain)
        public MusicVisualControl(ISettingsProvider settings, FileLogger log, string otherMusicFolder)
        {
            _settings = settings;
            _log = log;
            _otherMusicFolder = otherMusicFolder;
            Focusable = false;
            IsHitTestVisible = false;
            _image.RenderTransform = _snap;
            var grid = new Grid();
            grid.Children.Add(_image);
            Content = grid;

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
            Loaded += (s, e) =>
            {
                Subscribe();
                Refresh();
            };
            Unloaded += (s, e) =>
            {
                Unsubscribe();
                Stop();
            };
        }

        // True while the visualizer is on screen and drawing. Themes can bind it (Content.IsRunning on the element) to show
        // a fallback - a WPF effect, say - only when the visualizer is off or missing, never on top of it.
        private static readonly DependencyPropertyKey IsRunningKey = DependencyProperty.RegisterReadOnly(
            nameof(IsRunning), typeof(bool), typeof(MusicVisualControl), new PropertyMetadata(false));
        public static readonly DependencyProperty IsRunningProperty = IsRunningKey.DependencyProperty;
        public bool IsRunning => (bool)GetValue(IsRunningProperty);

        // The Jukebox Fountain's source: 0 reacting live, 1 analysing the song, 2 following the analysed song.
        // Themes can bind it (Content.SongState on the element).
        private static readonly DependencyPropertyKey SongStateKey = DependencyProperty.RegisterReadOnly(
            nameof(SongState), typeof(int), typeof(MusicVisualControl), new PropertyMetadata(0));
        public static readonly DependencyProperty SongStateProperty = SongStateKey.DependencyProperty;
        public int SongState => (int)GetValue(SongStateProperty);

        private bool Enabled => _settings?.Current?.MusicVisualizerEnabled == true;

        // The chosen style's file: a MilkDrop preset made by native/visualizer/presets/make_presets.py, or the Jukebox
        // Fountain's style file, made by native/visualizer/bench/tools/export_t8.mjs.
        private string PresetPath => Path.Combine(Folder, "presets", StyleFile(_settings?.Current?.MusicVisualizerStyle));

        private static string StyleFile(MusicVisualizerStyle? style)
        {
            switch (style)
            {
                case MusicVisualizerStyle.GlowBars: return "UPS - Glow Bars.milk";
                case MusicVisualizerStyle.Fountain: return "UPS - Fountain.milk";
                default: return "UPS - Jukebox Fountain.vis";
            }
        }

        // Start or stop to match "on screen and switched on". Deferred to after layout, so the renderer can match the
        // element's real size.
        private void Refresh() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible || !Enabled) { Stop(); return; }
            // A preset is loaded once at start, so a new style means a new renderer.
            if (_owner == this && _startedPreset != PresetPath) Stop();
            Start();
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
            if (e.PropertyName == nameof(UniPlaySongSettings.MusicVisualizerEnabled)
                || e.PropertyName == nameof(UniPlaySongSettings.MusicVisualizerStyle)) Refresh();
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
            string preset = PresetPath;
            if (!File.Exists(RendererPath) || !File.Exists(preset))
            {
                _log?.Warn($"[Visualizer] Not installed: {RendererPath} or its preset is missing");
                return;
            }

            _owner = this;
            _startedPreset = preset;
            int generation = ++_generation;
            int pid = Process.GetCurrentProcess().Id;
            string channel = $@"Local\UniPlaySong_Visualizer_{pid}_{++_starts}";
            try
            {
                Load(Folder);
                _feed = VisualizerAudioFeed.Open(channel + "_Audio");
                if (_settings?.Current?.MusicVisualizerStyle == MusicVisualizerStyle.JukeboxFountain)
                    _songAnalysis = new VisualizerSongAnalysis(_feed, _log, _otherMusicFolder);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[Visualizer] Could not start: {ex.Message}");
                _owner = null;
                return;
            }

            // The frame's box: the element in device pixels, at most 2560x1440, centred and shown 1:1 - pixel for pixel is
            // what keeps the visualizer sharp. Past the cap the rest of the element stays empty rather than upscaled (an
            // upscaled frame - 1728 px stretched over 2880 - looked blurry all over, and filling the width also made every
            // column wider than the page was laid out for).
            _deviceScale = ToDevice(this, new Point(1, 1)) - ToDevice(this, new Point(0, 0));
            int ew = (int)(ActualWidth * _deviceScale.X), eh = (int)(ActualHeight * _deviceScale.Y);
            if (ew < 64 || eh < 64) { ew = 1280; eh = 720; }
            _startedAt = new Size(ActualWidth, ActualHeight);
            int bw = Math.Min(ew, 2560) & ~1, bh = Math.Min(eh, 1440) & ~1;
            _image.Width = bw / _deviceScale.X;
            _image.Height = bh / _deviceScale.Y;
            int w = bw, h = bh;

            try
            {
                // The parent pid lets the renderer exit with Playnite even if Playnite crashes.
                _renderer = Process.Start(new ProcessStartInfo(RendererPath, $"\"{preset}\" {w} {h} {channel} {pid}")
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
            _shownAt = null;
            _window = Window.GetWindow(this);
            _lastFrame = 0;
            CompositionTarget.Rendering += OnRendering;
            SetValue(IsRunningKey, true);
        }

        // One renderer frame per WPF frame: tell it what's being heard, ask for the next frame, show the newest ready.
        private void OnRendering(object sender, EventArgs e)
        {
            _feed?.PublishPlayed();
            _songAnalysis?.Tick();
            // Ask for a frame only while there is something to show: not after 1.5 s of silence (paused, stopped, between
            // songs - the visuals have settled by then) and not while Playnite is in the background (a game, another app).
            // The last frame stays up and the renderer waits, drawing nothing.
            bool idle = _feed == null || unchecked(Environment.TickCount - _feed.LastSoundTick) > 1500 || _window?.IsActive == false;
            if (!idle) SetEvent(_tick);
            int songState = _view.ReadInt32(OffSongState);
            if (songState != SongState) SetValue(SongStateKey, songState);
            // Keep the frame on whole device pixels. Layout rounding works in this element's units, and under Fullscreen's
            // Viewbox a whole unit is a fraction of a pixel (the stage's top, 110 units down, lands at 183.3 px at 200%) -
            // WPF would then resample every frame and the visualizer goes soft. The layout offset leaves out _snap itself.
            if (VisualTreeHelper.GetParent(_image) is Visual parent)
            {
                var at = ToDevice(parent, (Point)VisualTreeHelper.GetOffset(_image));
                _snap.X = (Math.Round(at.X) - at.X) / _deviceScale.X;
                _snap.Y = (Math.Round(at.Y) - at.Y) / _deviceScale.Y;
                // Where the frame actually lands (after _snap), whenever that changes: whole-pixel corners and the frame's
                // own size mean it's shown 1:1.
                var tl = ToDevice(_image, new Point(0, 0));
                var br = ToDevice(_image, new Point(_image.ActualWidth, _image.ActualHeight));
                var shownAt = $"at {tl.X:0.###},{tl.Y:0.###}, {br.X - tl.X:0.###}x{br.Y - tl.Y:0.###} device px for a {_w}x{_h} frame";
                if (_image.ActualWidth > 0 && shownAt != _shownAt) { _shownAt = shownAt; _log?.Debug($"[Visualizer] Shown {shownAt}"); }
            }
            long frame = _view.ReadInt64(OffFrame);
            if (frame == _lastFrame || frame <= 0 || !_d3d.IsFrontBufferAvailable) return;
            if (_lastFrame == 0) _log?.Debug($"[Visualizer] Frames by {(_view.ReadInt32(OffTransport) == 2 ? "zero-copy (GPU only)" : "copy (through the CPU)")}");
            _lastFrame = frame;
            _d3d.Lock();
            vt_copy(_view.ReadInt32(OffLatest));
            _d3d.AddDirtyRect(new Int32Rect(0, 0, _w, _h));
            _d3d.Unlock();
        }

        // A point in v's units, in device pixels: DPI and every transform above v. TransformToDevice alone is only the DPI,
        // and Playnite's Fullscreen window shows its whole view through a Viewbox (laid out 1080 units tall, stretched to
        // the screen) - sizing by DPI alone drew a 2560 px frame into 2133 px on a 2880x1800 screen at 200%.
        private static Point ToDevice(Visual v, Point p)
        {
            var source = PresentationSource.FromVisual(v);
            if (source?.RootVisual == null) return p;
            return source.CompositionTarget.TransformToDevice.Transform(v.TransformToAncestor(source.RootVisual).Transform(p));
        }

        private void Stop()
        {
            if (_owner != this) return;
            _generation++;
            SetValue(IsRunningKey, false);
            SetValue(SongStateKey, 0);
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
            // Our handle to the tick event; the renderer holds its own, so closing ours never strands it.
            if (_tick != IntPtr.Zero) { CloseHandle(_tick); _tick = IntPtr.Zero; }
            var renderer = _renderer;
            _rendererExited = Task.Run(() =>
            {
                try { if (renderer != null && !renderer.WaitForExit(3000)) renderer.Kill(); } catch { }
                renderer?.Dispose();
            });
            _renderer = null;
            _view?.Dispose(); _view = null;
            _mmf?.Dispose(); _mmf = null;
            _songAnalysis?.Dispose(); _songAnalysis = null;
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
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_init(IntPtr hwnd);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_open(int index, ulong handle, int w, int h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr vt_backbuffer(int w, int h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int vt_copy(int index);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void vt_shutdown();

        internal const int OffMagic = 0, OffWidth = 4, OffHeight = 8, OffHandles = 16, OffLatest = 40, OffSongState = 44, OffFrame = 48, OffStop = 80, OffTransport = 84;
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
