// SPIKE (feature/music-visual-control only): first cut of the UPS_MusicVisualControl theme element. Runs the projectM
// renderer only while the element is visible, and shows its frames with real transparency over whatever the theme
// draws behind it. Never focusable or hit-testable, so it can't take controller focus. Uses the spike binaries
// and the local preset pack, so it only works on the dev machine.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Playnite.SDK.Controls;
using static UniPlaySong.Spike.VisualTransportSpike;

namespace UniPlaySong.Spike
{
    public class MusicVisualControl : PluginUserControl
    {
        // The consumer helper holds one device and one set of shared textures, so only one element can show at a time.
        private static MusicVisualControl _owner;
        // UPS's own presets (native/vistransport-spike/presets), not the community pack.
        private const string UpsPresetDir = @"C:\Projects\UniPSound\UniPlaySong\native\vistransport-spike\presets";
        // The renderer runs from a temp copy, so a running page never locks the build output.
        private const string RendererBuildDir = @"C:\Projects\UniPSound\UniPlaySong\native\vistransport-spike\obj\dev";
        private static readonly string RendererRunDir = Path.Combine(Path.GetTempPath(), "ups_vt_spike", "run");

        private readonly Image _image = new Image { Stretch = Stretch.Uniform };
        private Process _renderer;
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _view;
        private IntPtr _tick;
        private D3DImage _d3d;
        private int _w, _h;
        private long _lastFrame;
        private int _generation;
        private static Task _rendererExited = Task.FromResult(0);
        private Size _startedAt;
        private readonly System.Windows.Threading.DispatcherTimer _resizeTimer =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        public MusicVisualControl()
        {
            Focusable = false;
            IsHitTestVisible = false;
            Content = _image;
            // The renderer's frame size is fixed when it starts. When the element settles at another size (a page's
            // text loading, a resolution change), restart it so the frame matches again - presets lay out around the
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
            // Start once layout has run, so the renderer can match the element's real size.
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Start(); }), System.Windows.Threading.DispatcherPriority.Loaded);
                else Stop();
            };
            Unloaded += (s, e) => Stop();
        }

        private void Start()
        {
            string exe = Path.Combine(RendererRunDir, "vt_milkdrop.exe");
            if (_owner != null || !Directory.Exists(UpsPresetDir)) return;
            // A new renderer opening the old one's shared memory would reset its stop flag and leave it running.
            if (!_rendererExited.IsCompleted)
            {
                _rendererExited.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Start(); })));
                return;
            }
            Directory.CreateDirectory(RendererRunDir);
            foreach (var file in Directory.GetFiles(RendererBuildDir, "*.*").Where(f => f.EndsWith(".exe") || f.EndsWith(".dll")))
                try { File.Copy(file, Path.Combine(RendererRunDir, Path.GetFileName(file)), true); } catch (IOException) { }
            if (!File.Exists(exe)) return;
            _owner = this;
            int generation = ++_generation;
            LoadHelper();

            // Render at the element's size in device pixels, so nothing is scaled up.
            var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice ?? Matrix.Identity;
            int w = (int)(ActualWidth * toDevice.M11) & ~1, h = (int)(ActualHeight * toDevice.M22) & ~1;
            if (w < 64 || h < 64) { w = 1280; h = 720; }
            _startedAt = new Size(ActualWidth, ActualHeight);
            w = Math.Min(w, 2560); h = Math.Min(h, 1440);

            var psi = new ProcessStartInfo(exe, $"\"{UpsPresetDir}\" {w} {h} luma 45")
                { UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["VT_PARENT"] = Process.GetCurrentProcess().Id.ToString();
            _renderer = Process.Start(psi);

            // Waiting for the renderer's shared memory takes a moment; don't hold the UI thread for it.
            Task.Run(() =>
            {
                MemoryMappedFile mmf = null;
                for (int i = 0; i < 100 && mmf == null; i++)
                {
                    try { mmf = MemoryMappedFile.OpenExisting(@"Local\UPS_VisualTransport_Spike"); }
                    catch (FileNotFoundException) { System.Threading.Thread.Sleep(50); }
                }
                var view = mmf?.CreateViewAccessor();
                for (int i = 0; view != null && i < 100 && view.ReadUInt32(OffMagic) != Magic; i++)
                    System.Threading.Thread.Sleep(20);
                Dispatcher.BeginInvoke(new Action(() => Attach(generation, mmf, view)));
            });
        }

        private void Attach(int generation, MemoryMappedFile mmf, MemoryMappedViewAccessor view)
        {
            if (generation != _generation || view == null || view.ReadUInt32(OffMagic) != Magic)
            {
                view?.Dispose(); mmf?.Dispose();
                if (generation == _generation) Stop();
                return;
            }
            _mmf = mmf;
            _view = view;
            _w = view.ReadInt32(OffWidth);
            _h = view.ReadInt32(OffHeight);
            _tick = CreateEventW(IntPtr.Zero, false, false, @"Local\UPS_VisualTransport_Spike_Tick");

            var hwnd = (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero || vt_init(hwnd) != 0) { Stop(); return; }
            for (int i = 0; i < 3; i++)
                if (vt_open(i, view.ReadUInt64(OffHandles + 8 * i), _w, _h) != 0) { Stop(); return; }
            var back = vt_backbuffer(_w, _h);
            if (back == IntPtr.Zero) { Stop(); return; }

            _d3d = new D3DImage();
            _d3d.Lock();
            _d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, back, true);
            _d3d.Unlock();
            _image.Source = _d3d;
            _lastFrame = 0;
            CompositionTarget.Rendering += OnRendering;
        }

        // One renderer frame per WPF frame: ask for the next, show the newest that's ready.
        private void OnRendering(object sender, EventArgs e)
        {
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
            _rendererExited = Task.Run(() => { try { if (renderer != null && !renderer.WaitForExit(3000)) renderer.Kill(); } catch { } renderer?.Dispose(); });
            _renderer = null;
            _view?.Dispose(); _view = null;
            _mmf?.Dispose(); _mmf = null;
            _owner = null;
        }
    }
}
