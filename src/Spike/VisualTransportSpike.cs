// SPIKE A — TEMPORARY, NEVER COMMIT. In-process check of the visual transport inside Playnite: the 64-bit
// producer renders, a window in Playnite's own (32-bit) process shows the frames through D3DImage, measured the
// same way as native/vistransport-spike/VtHost. Loads the spike binaries straight from the spike's obj folder,
// so it only works on the dev machine.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Playnite.SDK;

namespace UniPlaySong.Spike
{
    internal static class VisualTransportSpike
    {
        private const string SpikeDir = @"C:\Projects\UniPSound\UniPlaySong\native\vistransport-spike\obj";
        private static string Producer => Path.Combine(SpikeDir, "vt_producer.exe");
        private static string Helper => Path.Combine(SpikeDir, "vt_consumer32.dll");
        private static string ResultsDir => Path.Combine(SpikeDir, "results");

        public static bool Available => File.Exists(Producer) && File.Exists(Helper);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEventW(IntPtr a, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] private static extern bool SetEvent(IntPtr h);
        [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int vt_init(IntPtr hwnd);
        [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int vt_open(int index, ulong handle, int w, int h);
        [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr vt_backbuffer(int w, int h);
        [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int vt_copy(int index);
        [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void vt_shutdown();

        private const int OffMagic = 0, OffWidth = 4, OffHeight = 8, OffHandles = 16, OffLatest = 40, OffFrame = 48, OffQpc = 56, OffStop = 80;
        private const uint Magic = 0x31585456u;
        private const int Seconds = 8;

        public static void Run(IPlayniteAPI api)
        {
            // DllImport by name binds to an already-loaded module, so load the spike helper from its own folder first.
            LoadLibraryW(Helper);
            Directory.CreateDirectory(ResultsDir);
            string mode = api.ApplicationInfo.Mode == ApplicationMode.Fullscreen ? "fullscreen" : "desktop";
            string tag = $"playnite-{mode}{(api.ApplicationSettings.DisableHwAcceleration ? "-swrender" : "")}";
            string resultsPath = Path.Combine(ResultsDir, tag + ".txt"), shotPath = Path.Combine(ResultsDir, tag + ".png");

            var producer = Process.Start(new ProcessStartInfo(Producer, "1280 720 60 30 ondemand") { UseShellExecute = false, CreateNoWindow = true });
            MemoryMappedFile mmf = null;
            for (int i = 0; i < 50 && mmf == null; i++)
            {
                try { mmf = MemoryMappedFile.OpenExisting(@"Local\UPS_VisualTransport_Spike"); }
                catch (FileNotFoundException) { System.Threading.Thread.Sleep(100); }
            }
            if (mmf == null) { api.Dialogs.ShowErrorMessage("Spike: producer did not start.", "UniPlaySong"); return; }
            var view = mmf.CreateViewAccessor();
            while (view.ReadUInt32(OffMagic) != Magic) System.Threading.Thread.Sleep(20);
            int w = view.ReadInt32(OffWidth), h = view.ReadInt32(OffHeight);
            IntPtr tick = CreateEventW(IntPtr.Zero, false, false, @"Local\UPS_VisualTransport_Spike_Tick");

            var owner = api.Dialogs.GetCurrentAppWindow();
            var window = new Window
            {
                Title = "UPS spike: visual transport (" + tag + ")", Width = 1000, Height = 600, Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true, ShowInTaskbar = false,
                Background = new LinearGradientBrush(Color.FromRgb(0xC0, 0x30, 0x60), Color.FromRgb(0x20, 0x60, 0xC0), 45),
            };
            var grid = new Grid();
            var stripes = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < 25; i++)
                stripes.Children.Add(new Border { Width = 40, Background = i % 2 == 0 ? Brushes.Gold : Brushes.Transparent, Opacity = 0.6 });
            grid.Children.Add(stripes);
            grid.Children.Add(new TextBlock { Text = "THEME BACKGROUND SHOULD SHOW THROUGH", FontSize = 40, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(20) };
            grid.Children.Add(image);
            window.Content = grid;

            D3DImage d3d = null;
            string error = null, summary = null;
            var latencies = new List<double>();
            long lastFrame = 0, presented = 0, dropped = 0, ticks = 0;
            var sw = new Stopwatch();
            var cpuStart = TimeSpan.Zero;
            bool shot = false;

            window.SourceInitialized += (s, e) =>
            {
                int hr = vt_init(new WindowInteropHelper(window).Handle);
                if (hr != 0) { error = $"vt_init 0x{hr:x8}"; return; }
                for (int i = 0; i < 3; i++)
                {
                    hr = vt_open(i, view.ReadUInt64(OffHandles + 8 * i), w, h);
                    if (hr != 0) { error = $"vt_open({i}) 0x{hr:x8}"; return; }
                }
                var back = vt_backbuffer(w, h);
                if (back == IntPtr.Zero) { error = "vt_backbuffer failed"; return; }
                d3d = new D3DImage();
                d3d.Lock();
                d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, back, true);
                d3d.Unlock();
                image.Source = d3d;
            };

            EventHandler onRender = null;
            onRender = (s, e) =>
            {
                if (error != null) { window.Close(); return; }
                if (d3d == null) return;
                if (!sw.IsRunning) { sw.Start(); cpuStart = Process.GetCurrentProcess().TotalProcessorTime; }
                ticks++;
                SetEvent(tick);
                long frame = view.ReadInt64(OffFrame);
                if (frame != lastFrame && frame > 0)
                {
                    int latest = view.ReadInt32(OffLatest);
                    if (lastFrame > 0) dropped += Math.Max(0, frame - lastFrame - 1);
                    lastFrame = frame;
                    if (d3d.IsFrontBufferAvailable)
                    {
                        d3d.Lock();
                        vt_copy(latest);
                        d3d.AddDirtyRect(new Int32Rect(0, 0, w, h));
                        d3d.Unlock();
                    }
                    presented++;
                    latencies.Add((Stopwatch.GetTimestamp() - view.ReadInt64(OffQpc + 8 * latest)) * 1000.0 / Stopwatch.Frequency);
                }

                if (summary == null && sw.Elapsed.TotalSeconds >= Seconds)
                {
                    double elapsed = sw.Elapsed.TotalSeconds;
                    double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalSeconds;
                    latencies.Sort();
                    string Pct(double q) => latencies.Count == 0 ? "-" : latencies[(int)Math.Min(latencies.Count - 1, latencies.Count * q)].ToString("F1");
                    summary = string.Join(Environment.NewLine, new[]
                    {
                        $"Playnite {mode}, process {(Environment.Is64BitProcess ? "64" : "32")}-bit, render mode {RenderOptions.ProcessRenderMode}, render tier {RenderCapability.Tier >> 16}, DisableHwAcceleration={api.ApplicationSettings.DisableHwAcceleration}",
                        $"shown={presented} in {elapsed:F2}s = {presented / elapsed:F1} fps; WPF frames {ticks / elapsed:F1}/s; skipped {dropped}",
                        $"latency ms: median {Pct(0.5)}, p95 {Pct(0.95)}, max {(latencies.Count > 0 ? latencies.Last().ToString("F1") : "-")}",
                        $"Playnite process CPU during the run: {cpu / elapsed * 100:F1}% of one core (includes everything Playnite was doing)",
                    });
                }
                if (summary != null && !shot)
                {
                    shot = true;
                    var p = window.PointToScreen(new Point(0, 0));
                    var src = PresentationSource.FromVisual(window);
                    int cw = (int)(grid.ActualWidth * src.CompositionTarget.TransformToDevice.M11), ch = (int)(grid.ActualHeight * src.CompositionTarget.TransformToDevice.M22);
                    using (var bmp = new System.Drawing.Bitmap(cw, ch))
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, bmp.Size);
                        bmp.Save(shotPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    window.Close();
                }
            };
            CompositionTarget.Rendering += onRender;

            window.Closed += (s, e) =>
            {
                CompositionTarget.Rendering -= onRender;
                view.Write(OffStop, 1);
                SetEvent(tick);
                vt_shutdown();
                File.WriteAllText(resultsPath, error ?? summary ?? "closed early");
                try { if (!producer.WaitForExit(3000)) producer.Kill(); } catch { }
                view.Dispose(); mmf.Dispose();
                api.Dialogs.ShowMessage(error ?? summary ?? "closed early", "UPS spike: visual transport");
            };
            window.Show();
        }
    }
}
