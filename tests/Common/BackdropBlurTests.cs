using System.Linq;
using NUnit.Framework;
using UniPlaySong.Features.MusicInfoCard;

namespace UniPlaySong.Tests.Common
{
    // The Music Info Card backdrop is blurred in code (a sliding-window box blur) instead of by a WPF BlurEffect.
    // Pins the window bookkeeping: a flat image must come back unchanged, and a lone bright pixel must spread
    // symmetrically without losing brightness.
    [TestFixture]
    public class BackdropBlurTests
    {
        [Test]
        public void BoxBlurKeepsFlatImagesAndSpreadsAPointEvenly()
        {
            const int w = 9, h = 1, r = 2;

            var flat = Enumerable.Repeat((byte)120, w * h * 4).ToArray();
            var flatOut = new byte[flat.Length];
            MusicInfoCardHandler.BoxBlur(flat, flatOut, w, h, r, horizontal: true);
            Assert.That(flatOut, Is.All.EqualTo(120), "a flat image must stay flat, edges included");

            var point = new byte[w * h * 4];
            for (int c = 0; c < 4; c++) point[4 * 4 + c] = 250;   // pixel 4 of 9, mid-row
            var spread = new byte[point.Length];
            MusicInfoCardHandler.BoxBlur(point, spread, w, h, r, horizontal: true);

            var red = Enumerable.Range(0, w).Select(i => (int)spread[i * 4]).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(red, Is.EqualTo(new[] { 0, 0, 50, 50, 50, 50, 50, 0, 0 }), "a 5-wide window averages the point");
                Assert.That(red.Sum(), Is.EqualTo(250), "no brightness gained or lost");
            });
        }
    }
}
