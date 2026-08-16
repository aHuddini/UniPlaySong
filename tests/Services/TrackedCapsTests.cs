using NUnit.Framework;
using UniPlaySong.Controls.Settings;

namespace UniPlaySong.Tests.Services
{
    // Section headings are small tracked uppercase so they read as band markers rather than
    // competing with the setting labels beneath them. WPF's TextBlock has neither
    // text-transform nor letter-spacing, so TrackedCaps supplies both by rewriting the string.
    [TestFixture]
    public class TrackedCapsTests
    {
        private const string Hair = " ";

        [Test]
        public void UppercasesAndSpacesEveryLetter()
        {
            Assert.That(TrackedCaps.Render("Games"),
                Is.EqualTo("G" + Hair + "A" + Hair + "M" + Hair + "E" + Hair + "S"));
        }

        [Test]
        public void DoesNotWidenAnExistingWordGap()
        {
            // A real space already reads as a gap; padding both sides of it would open a chasm
            // between words and make the heading read as two headings.
            var rendered = TrackedCaps.Render("Window Focus");

            Assert.That(rendered, Does.Not.Contain(Hair + " "), "spacer before the word gap");
            Assert.That(rendered, Does.Not.Contain(" " + Hair), "spacer after the word gap");
            Assert.That(rendered, Does.Contain("W" + Hair + "I"), "letters inside a word are spaced");
            Assert.That(rendered, Does.Contain("W F"), "the word gap survives as a plain space");
        }

        [Test]
        public void LeavesNoTrailingSpacer()
        {
            Assert.That(TrackedCaps.Render("System"), Does.Not.EndWith(Hair));
        }

        [Test]
        public void HandlesEmptyAndNull()
        {
            Assert.That(TrackedCaps.Render(null), Is.Empty);
            Assert.That(TrackedCaps.Render(string.Empty), Is.Empty);
        }

        [Test]
        public void SingleCharacterGetsNoSpacer()
        {
            Assert.That(TrackedCaps.Render("A"), Is.EqualTo("A"));
        }
    }
}
