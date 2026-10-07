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
using System.Windows.Media.Imaging;

// SPIKE A host: shows the producer's frames in a 32-bit WPF window over a busy background, either through
// D3DImage (GPU path) or a WriteableBitmap fed from shared memory (CPU path), and measures what arrives.
//
//   VtHost.exe gpu|cpu soft|hw seconds results.txt shot.png
static class Program
{
    [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] static extern int vt_init(IntPtr hwnd);
    [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] static extern int vt_open(int index, ulong handle, int w, int h);
    [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr vt_backbuffer(int w, int h);
    [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] static extern int vt_copy(int index);
    [DllImport("vt_consumer32.dll", CallingConvention = CallingConvention.Cdecl)] static extern void vt_shutdown();

    // Offsets in VtHeader (vt_shared.h, pack 8).
    const int OffMagic = 0, OffWidth = 4, OffHeight = 8, OffHandles = 16, OffLatest = 40, OffFrame = 48, OffQpc = 56, OffStop = 80;
    const uint Magic = 0x31585456u;
    const int CpuOffset = 4096;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateEventW(IntPtr attrs, bool manual, bool initial, string name);
    [DllImport("kernel32.dll")] static extern bool SetEvent(IntPtr h);

    [STAThread]
    static int Main(string[] args)
    {
        bool gpu = args.Length < 1 || args[0] == "gpu";
        bool soft = args.Length > 1 && args[1] == "soft";
        int seconds = args.Length > 2 ? int.Parse(args[2]) : 8;
        string results = args.Length > 3 ? args[3] : "results.txt";
        string shot = args.Length > 4 ? args[4] : "shot.png";
        if (soft) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly; // what Playnite's "Disable hardware acceleration" sets

        MemoryMappedFile mmf = null;
        for (int i = 0; i < 50 && mmf == null; i++)
        {
            try { mmf = MemoryMappedFile.OpenExisting(@"Local\UPS_VisualTransport_Spike"); }
            catch (FileNotFoundException) { System.Threading.Thread.Sleep(100); }
        }
        if (mmf == null) { File.WriteAllText(results, "no producer"); return 1; }
        var view = mmf.CreateViewAccessor();
        while (view.ReadUInt32(OffMagic) != Magic) System.Threading.Thread.Sleep(20);
        int w = view.ReadInt32(OffWidth), h = view.ReadInt32(OffHeight);

        IntPtr tickEvent = CreateEventW(IntPtr.Zero, false, false, @"Local\UPS_VisualTransport_Spike_Tick");
        string summary = null;
        var app = new Application();
        var window = new Window
        {
            Title = $"VtHost {(gpu ? "GPU" : "CPU")} {(soft ? "software" : "hardware")}",
            Width = 1000, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true,
            Background = new LinearGradientBrush(Color.FromRgb(0xC0, 0x30, 0x60), Color.FromRgb(0x20, 0x60, 0xC0), 45),
        };
        // Busy backdrop: stripes and text, so anything opaque that should be transparent is obvious.
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
        WriteableBitmap wb = null;
        IntPtr basePtr = IntPtr.Zero;
        var latencies = new List<double>();
        long lastFrame = 0, presented = 0, dropped = 0, firstProducerFrame = -1, lastProducerFrame = 0;
        var cpuStart = Process.GetCurrentProcess().TotalProcessorTime;
        var sw = new Stopwatch();
        bool shotTaken = false;
        // Instrumentation: WPF frame callbacks, their spacing, and time inside the hand-off calls.
        long ticks = 0, idleTicks = 0, jumpTicks = 0;
        double lockMs = 0, copyMs = 0, unlockMs = 0, maxHandoffMs = 0, lastTickMs = -1, maxGapMs = 0;
        var gaps = new List<double>();
        string error = null;

        window.SourceInitialized += (s, e) =>
        {
            if (gpu)
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                int hr = vt_init(hwnd);
                if (hr != 0) { error = $"vt_init 0x{hr:x8}"; return; }
                for (int i = 0; i < 3; i++)
                {
                    hr = vt_open(i, view.ReadUInt64(OffHandles + 8 * i), w, h);
                    if (hr != 0) { error = $"vt_open({i}) 0x{hr:x8}"; return; }
                }
                IntPtr back = vt_backbuffer(w, h);
                if (back == IntPtr.Zero) { error = "vt_backbuffer failed"; return; }
                d3d = new D3DImage();
                d3d.Lock();
                d3d.SetBackBuffer(D3DResourceType.IDirect3DSurface9, back, true); // software fallback allowed
                d3d.Unlock();
                image.Source = d3d;
            }
            else
            {
                unsafe { byte* p = null; view.SafeMemoryMappedViewHandle.AcquirePointer(ref p); basePtr = (IntPtr)p; }
                wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                image.Source = wb;
            }
        };

        CompositionTarget.Rendering += (s, e) =>
        {
            if (error != null) { window.Close(); return; }
            if (gpu ? d3d == null : wb == null) return; // first frames arrive before SourceInitialized has set up
            if (!sw.IsRunning) { sw.Start(); cpuStart = Process.GetCurrentProcess().TotalProcessorTime; }
            ticks++;
            SetEvent(tickEvent); // on-demand producer: render the next frame now
            double nowMs = sw.Elapsed.TotalMilliseconds;
            if (lastTickMs >= 0) { gaps.Add(nowMs - lastTickMs); maxGapMs = Math.Max(maxGapMs, nowMs - lastTickMs); }
            lastTickMs = nowMs;
            long frame = view.ReadInt64(OffFrame);
            if (frame == lastFrame) idleTicks++;
            else if (lastFrame > 0 && frame - lastFrame > 1) jumpTicks++;
            if (frame != lastFrame && frame > 0)
            {
                int latest = view.ReadInt32(OffLatest);
                if (firstProducerFrame < 0) firstProducerFrame = frame;
                if (lastFrame > 0) dropped += Math.Max(0, frame - lastFrame - 1);
                lastFrame = frame;
                lastProducerFrame = frame;
                if (gpu)
                {
                    if (d3d.IsFrontBufferAvailable)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        d3d.Lock();
                        long t1 = Stopwatch.GetTimestamp();
                        vt_copy(latest);
                        d3d.AddDirtyRect(new Int32Rect(0, 0, w, h));
                        long t2 = Stopwatch.GetTimestamp();
                        d3d.Unlock();
                        long t3 = Stopwatch.GetTimestamp();
                        double f = 1000.0 / Stopwatch.Frequency;
                        lockMs += (t1 - t0) * f; copyMs += (t2 - t1) * f; unlockMs += (t3 - t2) * f;
                        maxHandoffMs = Math.Max(maxHandoffMs, (t3 - t0) * f);
                    }
                }
                else
                {
                    wb.WritePixels(new Int32Rect(0, 0, w, h), basePtr + CpuOffset + w * h * 4 * latest, w * h * 4, w * 4);
                }
                presented++;
                long produced = view.ReadInt64(OffQpc + 8 * latest);
                latencies.Add((Stopwatch.GetTimestamp() - produced) * 1000.0 / Stopwatch.Frequency);
            }

            // Measure first, then screenshot: the capture itself stalls the UI for 100-200 ms.
            if (summary == null && sw.Elapsed.TotalSeconds >= seconds) summary = Summarize();
            if (summary != null && !shotTaken)
            {
                shotTaken = true;
                var p = window.PointToScreen(new Point(0, 0));
                var src = PresentationSource.FromVisual(window);
                double sx = src.CompositionTarget.TransformToDevice.M11, sy = src.CompositionTarget.TransformToDevice.M22;
                int cw = (int)(((FrameworkElement)window.Content).ActualWidth * sx), ch = (int)(((FrameworkElement)window.Content).ActualHeight * sy);
                using (var bmp = new System.Drawing.Bitmap(cw, ch))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen((int)p.X, (int)p.Y, 0, 0, bmp.Size);
                    bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            if (shotTaken) window.Close();
        };

        string Summarize()
        {
            double elapsed = sw.Elapsed.TotalSeconds;
            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuStart).TotalSeconds;
            latencies.Sort();
            string Pct(double q) => latencies.Count == 0 ? "-" : latencies[(int)Math.Min(latencies.Count - 1, latencies.Count * q)].ToString("F1");
            return string.Join(Environment.NewLine, new[]
            {
                $"path={(gpu ? "GPU (D3DImage)" : "CPU (WriteableBitmap)")} render={(soft ? "software" : "hardware")} size={w}x{h}",
                $"shown={presented} in {elapsed:F2}s = {presented / elapsed:F1} fps; producer advanced {lastProducerFrame - firstProducerFrame + 1} frames; skipped {dropped}",
                $"latency ms: median {Pct(0.5)}, p95 {Pct(0.95)}, max {(latencies.Count > 0 ? latencies.Last().ToString("F1") : "-")}",
                $"WPF callbacks: {ticks / elapsed:F1}/s, gap median {(gaps.Count > 0 ? gaps.OrderBy(g => g).ElementAt(gaps.Count / 2).ToString("F1") : "-")} ms, max {maxGapMs:F1} ms; no new frame {idleTicks}, jumped 2+ frames {jumpTicks}",
                $"hand-off per shown frame (GPU): lock {(presented > 0 ? lockMs / presented : 0):F2} ms, copy {(presented > 0 ? copyMs / presented : 0):F2} ms, unlock {(presented > 0 ? unlockMs / presented : 0):F2} ms, worst {maxHandoffMs:F1} ms",
                $"host CPU: {cpu / elapsed * 100:F1}% of one core ({cpu / elapsed / Environment.ProcessorCount * 100:F1}% of the machine, {Environment.ProcessorCount} logical cores)",
            });
        }

        window.Closed += (s, e) =>
        {
            view.Write(OffStop, 1);
            SetEvent(tickEvent); // release an on-demand producer waiting for the next frame
            File.WriteAllText(results, error ?? summary ?? Summarize());
            if (gpu) vt_shutdown();
        };

        app.DispatcherUnhandledException += (s, e) => { File.WriteAllText(results + ".error.txt", "dispatcher: " + e.Exception); e.Handled = true; window.Close(); };
        AppDomain.CurrentDomain.UnhandledException += (s, e) => File.WriteAllText(results + ".error.txt", "domain: " + e.ExceptionObject);
        try { app.Run(window); }
        catch (Exception ex) { File.WriteAllText(results, "run: " + ex); }
        return 0;
    }
}
