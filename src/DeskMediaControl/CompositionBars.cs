using System;
using System.Numerics;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.Composition;
using WinColor = Windows.UI.Color;
using CompContainer = Windows.UI.Composition.ContainerVisual;

namespace UniPlaySong.DeskMediaControl
{
    // The Now Playing card's "playing" marker: three bars bouncing at the display's own refresh rate, animated by the
    // system compositor, so Playnite does no per-frame work (the same bars as WPF animation cost ~7.5% GPU at 120 Hz).
    // Knows nothing about songs or rows: the card says where the bars go, what clips them and whether they move.
    internal sealed class CompositionBars
    {
        // Each bar: width and gap in DIPs; its loop: from, to (scale of the full height) and half-period in seconds
        private const double BarWidth = 2.4, BarGap = 1.4, BarHeight = 10;
        private static readonly (float From, float To, double Half)[] Loops = { (0.3f, 1f, 0.45), (1f, 0.35f, 0.38), (0.45f, 0.95f, 0.52) };

        private readonly Compositor _compositor;
        private readonly ICompositionTarget _target; // kept: the layer lives as long as the target
        private readonly CompContainer _clip;   // the list's visible area
        private readonly CompContainer _group;  // the three bars, where the playing row's marker is
        private readonly SpriteVisual[] _bars = new SpriteVisual[Loops.Length];
        private Rect _placedBars = Rect.Empty, _placedClip = Rect.Empty;
        private bool _live;

        private CompositionBars(Compositor compositor, ICompositionTarget target, System.Windows.Media.Color accent)
        {
            _compositor = compositor;
            _target = target;
            var root = compositor.CreateContainerVisual();
            target.Root = root;
            _clip = compositor.CreateContainerVisual();
            _clip.Clip = compositor.CreateInsetClip();
            _clip.IsVisible = false;
            root.Children.InsertAtTop(_clip);
            _group = compositor.CreateContainerVisual();
            _clip.Children.InsertAtTop(_group);
            var brush = compositor.CreateColorBrush(WinColor.FromArgb(accent.A, accent.R, accent.G, accent.B));
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i] = compositor.CreateSpriteVisual();
                _bars[i].Brush = brush;
                _group.Children.InsertAtTop(_bars[i]);
            }
        }

        public static CompositionBars TryCreate(Window host, System.Windows.Media.Color accent, Action<string> log)
        {
            try
            {
                var hwnd = host == null ? IntPtr.Zero : new WindowInteropHelper(host).Handle;
                if (hwnd == IntPtr.Zero) return null;
                var (compositor, target) = CompositionLayer.Create(hwnd);
                return new CompositionBars(compositor, target, accent);
            }
            catch (Exception ex)
            {
                log?.Invoke($"NowPlaying card: compositor bars unavailable, marker stays still ({ex.GetType().Name}: {ex.Message})");
                return null;
            }
        }

        // bars: where the marker is; clip: the list's visible area. Both in the window's DIPs.
        public void Place(Rect bars, Rect clip, double scale)
        {
            if (bars == _placedBars && clip == _placedClip) return;
            _placedBars = bars;
            _placedClip = clip;

            _clip.Offset = new Vector3((float)Math.Round(clip.X * scale), (float)Math.Round(clip.Y * scale), 0);
            _clip.Size = new Vector2((float)(clip.Width * scale), (float)(clip.Height * scale));
            _group.Offset = new Vector3((float)Math.Round((bars.X - clip.X) * scale), (float)Math.Round((bars.Y + bars.Height - BarHeight - clip.Y) * scale), 0);

            float width = (float)(BarWidth * scale), height = (float)(BarHeight * scale);
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i].Size = new Vector2(width, height);
                _bars[i].Offset = new Vector3((float)Math.Round(i * (BarWidth + BarGap) * scale), 0, 0);
                _bars[i].CenterPoint = new Vector3(width / 2, height, 0); // bounce from the bottom
            }
        }

        // Live: shown and bouncing. Otherwise hidden (the card shows its still WPF bars instead).
        public void SetLive(bool live)
        {
            if (live == _live) return;
            _live = live;
            _clip.IsVisible = live;
            var ease = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.37f, 0f), new Vector2(0.63f, 1f)); // sine in-out
            for (int i = 0; i < _bars.Length; i++)
            {
                if (!live) { _bars[i].StopAnimation("Scale.Y"); continue; }
                var (from, to, half) = Loops[i];
                var loop = _compositor.CreateScalarKeyFrameAnimation();
                loop.InsertKeyFrame(0f, from, ease);
                loop.InsertKeyFrame(0.5f, to, ease);
                loop.InsertKeyFrame(1f, from, ease);
                loop.Duration = TimeSpan.FromSeconds(half * 2);
                loop.IterationBehavior = AnimationIterationBehavior.Forever;
                _bars[i].Scale = new Vector3(1, from, 1);
                _bars[i].StartAnimation("Scale.Y", loop);
            }
        }
    }
}
