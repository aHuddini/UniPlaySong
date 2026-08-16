using System.Linq;
using NUnit.Framework;
using UniPlaySong.Controls.Settings;

namespace UniPlaySong.Tests.Services
{
    // Section headings are small tracked uppercase so they read as band markers rather than
    // competing with the setting labels beneath them. WPF's TextBlock has neither
    // text-transform nor letter-spacing, so TrackedCaps supplies both by emitting a run per
    // character with a scaled-down space between them.
    [TestFixture]
    public class TrackedCapsTests
    {
        private const double Size = 10.0;
        private const double Tracking = 0.1;

        [Test]
        public void UppercasesEveryLetter()
        {
            var text = string.Concat(TrackedCaps.Segments("Games", Tracking, Size).Select(s => s.Text));
            Assert.That(text.Replace(" ", string.Empty), Is.EqualTo("GAMES"));
        }

        [Test]
        public void PutsASpacerBetweenLettersButNotAfterTheLast()
        {
            var segs = TrackedCaps.Segments("ABC", Tracking, Size).ToList();

            // A B C plus two spacers
            Assert.That(segs.Count, Is.EqualTo(5));
            Assert.That(segs.Count(s => s.FontSize.HasValue), Is.EqualTo(2));
            Assert.That(segs.Last().FontSize.HasValue, Is.False, "no trailing spacer");
        }

        [Test]
        public void DoesNotTrackAcrossAWordGap()
        {
            // A word gap is already a gap. Tracking it too opens a chasm and the heading reads as
            // two headings, which is what multi-word titles looked like.
            var segs = TrackedCaps.Segments("AB CD", Tracking, Size).ToList();
            var gap = segs.FindIndex(s => s.Text == " " && !s.FontSize.HasValue);

            Assert.That(gap, Is.GreaterThan(0), "the real space survives");
            Assert.That(segs[gap - 1].FontSize.HasValue, Is.False, "no spacer before the word gap");
            Assert.That(segs[gap + 1].FontSize.HasValue, Is.False, "no spacer after the word gap");
        }

        [Test]
        public void SpacerScalesWithTrackingAndFontSize()
        {
            // A space is about a quarter em, so 0.1em of tracking is a space at 0.4x the size.
            var spacer = TrackedCaps.Segments("AB", 0.1, 10.0).Single(s => s.FontSize.HasValue).FontSize.Value;
            Assert.That(spacer, Is.EqualTo(4.0).Within(0.001));

            var wider = TrackedCaps.Segments("AB", 0.2, 10.0).Single(s => s.FontSize.HasValue).FontSize.Value;
            Assert.That(wider, Is.EqualTo(8.0).Within(0.001), "double the tracking, double the spacer");

            var bigger = TrackedCaps.Segments("AB", 0.1, 20.0).Single(s => s.FontSize.HasValue).FontSize.Value;
            Assert.That(bigger, Is.EqualTo(8.0).Within(0.001), "tracking follows the font size");
        }

        [Test]
        public void HandlesEmptyAndNullAndSingleCharacter()
        {
            Assert.That(TrackedCaps.Segments(null, Tracking, Size), Is.Empty);
            Assert.That(TrackedCaps.Segments(string.Empty, Tracking, Size), Is.Empty);

            var one = TrackedCaps.Segments("A", Tracking, Size).ToList();
            Assert.That(one.Count, Is.EqualTo(1));
            Assert.That(one[0].FontSize.HasValue, Is.False);
        }
    }
}
