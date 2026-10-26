using System.Windows;
using Moq;
using NUnit.Framework;
using UniPlaySong.Audio;
using UniPlaySong.Controls;
using UniPlaySong.Models;
using UniPlaySong.Services.ActiveMedia;

namespace UniPlaySong.Tests.Controls
{
    // The music player's spectrum strip (UPS_MusicPlayerControlVisual) shares MusicVisualControl with the page
    // visualizer: when each wants the renderer, and the frame each asks it for.
    [TestFixture]
    public class MusicVisualControlStripTests
    {
        // A strip that goes hands the renderer back to the pages waiting for it - even one that never got it (closed while
        // the page's renderer was still exiting) - unless another strip has it, or this one is only restarting.
        [Test]
        public void HandsBack_WhenTheStripGoes_EvenIfItNeverGotTheRenderer()
        {
            Assert.IsTrue(MusicVisualControl.HandsBack(true, false, false), "closed while waiting for the old renderer");
            Assert.IsFalse(MusicVisualControl.HandsBack(true, false, true), "restarting at a new size keeps it");
            Assert.IsFalse(MusicVisualControl.HandsBack(true, true, false), "another strip has it");
            Assert.IsFalse(MusicVisualControl.HandsBack(false, false, false), "a page lends nothing");
        }

        // A theme picks the page's style and palette by the element's Tag (a bare word stays the palette, as before): a
        // built-in style, or one shipped as a .vis by name - never a path, and never one UniPlaySong doesn't have
        [Test]
        public void ThemeTag_PicksStyleAndPalette()
        {
            Assert.AreEqual(((string)null, "Cover"), MusicVisualControl.ParseTag("Cover"));
            Assert.AreEqual(("Glow Bars", "Disc"), MusicVisualControl.ParseTag("style = Glow Bars; Palette=Disc"));
            Assert.AreEqual(((string)null, (string)null), MusicVisualControl.ParseTag(null));

            System.Func<string, bool> shipped = f => f == "UPS - Midnight Neon.vis";
            Assert.AreEqual("UPS - Glow Bars.milk", MusicVisualControl.ThemeStyleFile("Glow Bars", shipped));
            Assert.AreEqual("UPS - Midnight Neon.vis", MusicVisualControl.ThemeStyleFile("Midnight Neon", shipped));
            Assert.IsNull(MusicVisualControl.ThemeStyleFile("Not Shipped", shipped), "unknown style: the user's");
            Assert.IsNull(MusicVisualControl.ThemeStyleFile("5", shipped), "a number is not a style");
            Assert.IsNull(MusicVisualControl.ThemeStyleFile(@"..\..\Themes\Mine", f => true), "a path is not a style");
        }

        // SDL2 (no NAudio provider): the popup keeps the plain bar - no empty strip row above it.
        [Test, Apartment(System.Threading.ApartmentState.STA)]
        public void Overlay_OnSdl2_ShowsThePlainBar()
        {
            VisualizationDataProvider.Current = null;
            var service = new Mock<IActiveMediaService>();
            service.Setup(s => s.GetSnapshot()).Returns(ActiveMediaSnapshot.Empty);
            var vm = new ActiveMediaViewModel(new FakeSettingsProvider(new UniPlaySongSettings()), service.Object);
            var overlay = new MediaControllerOverlay(vm, new System.Windows.Controls.Border());
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)overlay.FindName("StripHost")).Visibility);
        }

        // The frosted cover fills the card and never sizes it: a tall cover made the card tall (the blur layer asked for room
        // in the cover's proportions - 2040 px against 478). With the same art, the variant is the plain popup's height plus
        // its taller seek bar, and the frost shows the cover.
        [Test, Apartment(System.Threading.ApartmentState.STA)]
        public void Overlay_FrostedArt_NeverStretchesTheCard()
        {
            string art = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UPS_TallArt_" + System.Guid.NewGuid().ToString("N") + ".png");
            var px = new byte[100 * 400 * 4];
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(100, 400, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, 100 * 4);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(art)) enc.Save(f);
            try
            {
                var service = new Mock<IActiveMediaService>();
                service.Setup(s => s.GetSnapshot()).Returns(new ActiveMediaSnapshot(
                    true, ActiveMediaSourceKind.Ups, "UniPlaySong", true, false, 40, "0:40", "1:40", 50, true, true));   // a song playing
                var vm = new ActiveMediaViewModel(new FakeSettingsProvider(new UniPlaySongSettings { NowPlayingAlbumArtPath = art }), service.Object);
                vm.Attach();   // its first snapshot: a song playing, so the popup shows
                var plain = new MediaControllerOverlay(vm);
                var visual = new MediaControllerOverlay(vm, new System.Windows.Controls.Border());
                // on screen, as a theme shows it: the bindings (the art) resolve and the layout runs
                var host = new Window { Content = new System.Windows.Controls.StackPanel { Children = { plain, visual } }, SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -10000, Top = -10000, ShowActivated = false };
                host.Show();
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var glass = ((System.Windows.Shapes.Rectangle)visual.FindName("FrostFill")).Fill;
                host.Close();
                Assert.IsInstanceOf<System.Windows.Media.ImageBrush>(glass, "the cover, frosted");
                Assert.Less(visual.ActualHeight, plain.ActualHeight + 20);
            }
            finally { try { System.IO.File.Delete(art); } catch (System.IO.IOException) { } }
        }

    }
}
