using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.UI.Composition;
using WinColor = Windows.UI.Color;
using CompVisual = Windows.UI.Composition.Visual;
using CompContainer = Windows.UI.Composition.ContainerVisual;

namespace UniPlaySong.DeskMediaControl
{
    // The Now Playing title, scrolled by the system compositor instead of WPF (issue #55). WPF draws the title once into
    // a composition surface; a looping key-frame animation slides it, run by the compositor every refresh at the
    // display's own rate, with no per-frame work in Playnite. Measured at 120 Hz: 1.4% GPU in total against 6.1% for
    // the WPF loop. Knows nothing about songs: the panel says what to show, where, and whether it may move.
    //
    // The layer sits on Playnite's window above all WPF content (a topmost DesktopWindowTarget), so it draws exactly
    // over the panel and hides with it. TryCreate returns null wherever this can't work (Windows before 1809, no D3D,
    // another target on the window): the panel then keeps its WPF animation.
    internal sealed class CompositionTicker
    {
        [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositorInterop
        {
            void CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr surface);
            void CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr surface);
            void CreateGraphicsDevice(IntPtr renderingDevice, out CompositionGraphicsDevice device);
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int W, H; }

        [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositionDrawingSurfaceInterop
        {
            void BeginDraw(IntPtr updateRect, [In] ref Guid iid, out IntPtr updateObject, out NativePoint updateOffset);
            void EndDraw();
            void Resize(NativeSize size);
            void Scroll(IntPtr scrollRect, IntPtr clipRect, int offsetX, int offsetY);
            void ResumeDraw();
            void SuspendDraw();
        }

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels,
            uint levelCount, uint sdkVersion, out IntPtr device, out int level, out IntPtr context);

        [StructLayout(LayoutKind.Sequential)] private struct D3D11Box { public uint Left, Top, Front, Right, Bottom, Back; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UpdateSubresourceFn(IntPtr context, IntPtr resource, uint subresource, ref D3D11Box box, IntPtr data, uint rowPitch, uint depthPitch);

        private const int UpdateSubresourceSlot = 48; // ID3D11DeviceContext vtable
        private static readonly Guid Texture2DIid = new Guid("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

        private readonly Compositor _compositor;
        private readonly ICompositionTarget _target;
        private readonly CompositionGraphicsDevice _graphics;
        private readonly IntPtr _context;
        private readonly CompContainer _clip;   // the part of the panel WPF actually shows, clipped
        private readonly CompContainer _anchor; // puts the text where the panel's text area starts
        private readonly CompContainer _slide;  // the fade-in's opacity and slide
        private readonly SpriteVisual _text;      // the title; its Offset.X scrolls
        private readonly SpriteVisual _edge;      // right-edge fade
        private CompositionDrawingSurface _surface;
        private bool _scrolling;
        private bool _paused;

        private CompositionTicker(Compositor compositor, ICompositionTarget target, CompositionGraphicsDevice graphics, IntPtr context)
        {
            _compositor = compositor;
            _target = target;
            _graphics = graphics;
            _context = context;

            var root = compositor.CreateContainerVisual();
            target.Root = root;
            _clip = compositor.CreateContainerVisual();
            _clip.Clip = compositor.CreateInsetClip();
            _clip.IsVisible = false;
            root.Children.InsertAtTop(_clip);
            _anchor = compositor.CreateContainerVisual();
            _clip.Children.InsertAtTop(_anchor);
            _slide = compositor.CreateContainerVisual();
            _anchor.Children.InsertAtTop(_slide);
            _text = compositor.CreateSpriteVisual();
            _slide.Children.InsertAtTop(_text);
            _edge = compositor.CreateSpriteVisual();
            _clip.Children.InsertAtTop(_edge);
        }

        public static CompositionTicker TryCreate(Window host, Action<string> log)
        {
            try
            {
                var hwnd = host == null ? IntPtr.Zero : new WindowInteropHelper(host).Handle;
                if (hwnd == IntPtr.Zero) return null;
                var (compositor, target) = CompositionLayer.Create(hwnd);

                // 0x20 = D3D11_CREATE_DEVICE_BGRA_SUPPORT, 7 = D3D11_SDK_VERSION
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out var device, out _, out var context));
                ((ICompositorInterop)(object)compositor).CreateGraphicsDevice(device, out var graphics);
                Marshal.Release(device); // the graphics device holds its own reference

                return new CompositionTicker(compositor, target, graphics, context);
            }
            catch (Exception ex)
            {
                log?.Invoke($"NowPlaying: compositor ticker unavailable, using WPF animation ({ex.GetType().Name}: {ex.Message})");
                return null;
            }
        }

        // Draws the title once at device resolution, in the panel's own font and colour.
        public void SetText(string text, Typeface typeface, double fontSize, Brush foreground, double height, double scale)
        {
            var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, fontSize, foreground, scale);
            int width = Math.Max(1, (int)Math.Ceiling(formatted.WidthIncludingTrailingWhitespace * scale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawText(formatted, new Point(0, 0));
            var bitmap = new RenderTargetBitmap(width, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[width * pixelHeight * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);

            _surface = _graphics.CreateDrawingSurface(new Windows.Foundation.Size(width, pixelHeight),
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                Windows.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            Upload(_surface, pixels, width, pixelHeight);
            _text.Brush = _compositor.CreateSurfaceBrush(_surface);
            _text.Size = new Vector2(width, pixelHeight);
        }

        private void Upload(CompositionDrawingSurface surface, byte[] pixels, int width, int height)
        {
            var drawing = (ICompositionDrawingSurfaceInterop)(object)surface;
            var iid = Texture2DIid;
            drawing.BeginDraw(IntPtr.Zero, ref iid, out var texture, out var offset);
            try
            {
                var update = (UpdateSubresourceFn)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(_context), UpdateSubresourceSlot * IntPtr.Size), typeof(UpdateSubresourceFn));
                var box = new D3D11Box
                {
                    Left = (uint)offset.X, Top = (uint)offset.Y, Front = 0,
                    Right = (uint)(offset.X + width), Bottom = (uint)(offset.Y + height), Back = 1
                };
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { update(_context, texture, 0, ref box, pin.AddrOfPinnedObject(), (uint)width * 4, 0); }
                finally { pin.Free(); }
            }
            finally
            {
                drawing.EndDraw();
                Marshal.Release(texture);
            }
        }

        // origin: where the panel's text area starts; shown: the part of it WPF actually shows (the theme's clipping and
        // anything drawn over it already taken off). Both in window DIPs; the layer draws above WPF, so it must not
        // draw anywhere WPF wouldn't.
        public void SetPlacement(Point origin, Rect shown, double scale, bool visible, Color edgeColor)
        {
            float left = (float)Math.Round(shown.X * scale), top = (float)Math.Round(shown.Y * scale);
            _clip.Offset = new Vector3(left, top, 0);
            _clip.Size = new Vector2((float)(shown.Width * scale), (float)(shown.Height * scale));
            _clip.IsVisible = visible && shown.Width > 0 && shown.Height > 0;
            _anchor.Offset = new Vector3((float)Math.Round(origin.X * scale) - left, (float)Math.Round(origin.Y * scale) - top, 0);

            float edge = (float)(NowPlayingPanel.FadeWidth * scale);
            _edge.Size = new Vector2(edge, _clip.Size.Y);
            _edge.Offset = new Vector3(_clip.Size.X - edge, 0, 0);
            if (_edge.Brush == null)
            {
                var gradient = _compositor.CreateLinearGradientBrush();
                gradient.StartPoint = new Vector2(0, 0);
                gradient.EndPoint = new Vector2(1, 0);
                gradient.ColorStops.Add(_compositor.CreateColorGradientStop(0, WinColor.FromArgb(0, edgeColor.R, edgeColor.G, edgeColor.B)));
                gradient.ColorStops.Add(_compositor.CreateColorGradientStop(1, WinColor.FromArgb(edgeColor.A, edgeColor.R, edgeColor.G, edgeColor.B)));
                _edge.Brush = gradient;
            }
        }

        // Hold, scroll left, hold, scroll back, forever: the same path as the WPF animation.
        public void StartScroll(double distance, double speed, double hold, double scale)
        {
            double travel = distance / speed, total = 2 * hold + 2 * travel;
            float far = (float)(-distance * scale);
            var linear = _compositor.CreateLinearEasingFunction();
            var animation = _compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(0f, 0f, linear);
            animation.InsertKeyFrame((float)(hold / total), 0f, linear);
            animation.InsertKeyFrame((float)((hold + travel) / total), far, linear);
            animation.InsertKeyFrame((float)((2 * hold + travel) / total), far, linear);
            animation.InsertKeyFrame(1f, 0f, linear);
            animation.Duration = TimeSpan.FromSeconds(total);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            _text.StartAnimation("Offset.X", animation);
            _scrolling = true;
            if (_paused) _text.TryGetAnimationController("Offset.X")?.Pause();
        }

        public void StopScroll()
        {
            _text.StopAnimation("Offset.X");
            _text.Offset = Vector3.Zero;
            _scrolling = false;
        }

        // Paused in place, not reset: hover, focus loss, paused music.
        public void SetPaused(bool paused)
        {
            if (paused == _paused) return;
            _paused = paused;
            if (!_scrolling) return;
            var controller = _text.TryGetAnimationController("Offset.X");
            if (paused) controller?.Pause();
            else controller?.Resume();
        }

        // A new song: fades in while sliding in from the right.
        public void FadeIn(double slideDistance, TimeSpan duration, double scale)
        {
            var opacity = _compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 0f);
            opacity.InsertKeyFrame(1f, 1f);
            opacity.Duration = duration;
            _slide.StartAnimation("Opacity", opacity);

            var slide = _compositor.CreateScalarKeyFrameAnimation();
            slide.InsertKeyFrame(0f, (float)(slideDistance * scale));
            slide.InsertKeyFrame(1f, 0f);
            slide.Duration = duration;
            _slide.StartAnimation("Offset.X", slide);
        }

        public void Hide() => _clip.IsVisible = false;
    }
}
