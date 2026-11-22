using System;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using CompVisual = Windows.UI.Composition.Visual;

namespace UniPlaySong.DeskMediaControl
{
    // A Windows.UI.Composition layer over a WPF window: the plumbing CompositionTicker (the top panel's title) and
    // CompositionBars (the Now Playing card's playing marker) share. The layer is topmost over the window's WPF
    // content, and whatever it animates runs in the system compositor at the display's refresh rate, with no per-frame
    // work in Playnite.
    internal static class CompositionLayer
    {
        [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, bool isTopmost, out ICompositionTarget target); }

        [StructLayout(LayoutKind.Sequential)] private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
        [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

        private static IntPtr _dispatcherQueueController; // one per process, kept for its lifetime

        // A compositor and a topmost target on the window. Throws where composition can't work (Windows before 1809,
        // another topmost target already on the window); callers fall back to WPF.
        public static (Compositor Compositor, ICompositionTarget Target) Create(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("No window handle yet", nameof(hwnd));

            // The compositor needs a DispatcherQueue on this (UI) thread; reuse one another component made.
            if (Windows.System.DispatcherQueue.GetForCurrentThread() == null && _dispatcherQueueController == IntPtr.Zero)
            {
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new DispatcherQueueOptions
                {
                    Size = Marshal.SizeOf(typeof(DispatcherQueueOptions)),
                    ThreadType = 2,    // DQTYPE_THREAD_CURRENT
                    ApartmentType = 2, // DQTAT_COM_STA
                }, out _dispatcherQueueController));
            }

            var compositor = new Compositor();
            ((ICompositorDesktopInterop)(object)compositor).CreateDesktopWindowTarget(hwnd, true, out var target);
            return (compositor, target);
        }
    }

    // The desktop window target's root, as the WinRT projection doesn't expose DesktopWindowTarget's interop shape
    [ComImport, Guid("A1BEA8BA-D726-4663-8129-6B5E7927FFA6"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    internal interface ICompositionTarget { CompVisual Root { get; set; } }
}
