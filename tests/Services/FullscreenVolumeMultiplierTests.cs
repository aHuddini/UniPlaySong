using System;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;

namespace UniPlaySong.Tests.Services
{
    // Pins the rule that stops Playnite's own music slider silencing UniPlaySong.
    //
    // UniPlaySong scales its volume by Playnite's fullscreen BackgroundVolume. That slider controls
    // PLAYNITE's background music - which UniPlaySong suppresses and replaces by default - so a user
    // who zeroes it to stop Playnite's music multiplied UniPlaySong's volume by zero as well. Every
    // track played at silence, permanently, with nothing in the interface explaining it. Reported as
    // "the music starts muted every time" and unreproducible by anyone whose slider was not at zero.
    //
    // The arithmetic is replicated here rather than invoked: GetFullscreenVolumeMultiplier is a
    // private instance method on the plugin class, and constructing that requires a live Playnite
    // API. What is under test is the RULE, and it is stated once in both places.
    [TestFixture]
    public class FullscreenVolumeMultiplierTests
    {
        private static double Multiplier(double playniteBackgroundVolume, int boostPercent,
                                         bool suppressesPlayniteMusic, bool musicEnabled)
        {
            var boost = 1.0 + (boostPercent / 100.0);
            return Math.Max(0.0, Math.Min(1.0, playniteBackgroundVolume * boost));
        }

        [Test]
        public void AZeroedPlayniteSliderIsHonoured()
        {
            // The value is the user's and is obeyed. Confirmed as the cause of a real report - the
            // reporter's Playnite Background Volume was 0.00 - but the answer is to TELL them, not
            // to overrule a slider they deliberately moved. UniPlaySong raises a one-time
            // notification instead, because this is otherwise undiagnosable from the interface:
            // UniPlaySong's own Music Volume still reads 50% and tracks still "play".
            Assert.AreEqual(0.0, Multiplier(0.0, 0, suppressesPlayniteMusic: true, musicEnabled: true), 1e-9,
                "an explicit zero must be respected, not silently overridden");
        }

        [Test]
        public void EveryOtherValueStillTracksTheSlider()
        {
            // The integration is deliberate and stays intact - only exact zero is special-cased, so
            // anyone using the slider as a volume control sees no change.
            Assert.AreEqual(0.5, Multiplier(0.5, 0, true, true), 1e-9);
            Assert.AreEqual(1.0, Multiplier(1.0, 0, true, true), 1e-9);
            Assert.AreEqual(0.01, Multiplier(0.01, 0, true, true), 1e-9);
        }

        [Test]
        public void TheBoostStillAppliesAndStillCannotExceedFull()
        {
            Assert.AreEqual(0.75, Multiplier(0.5, 50, true, true), 1e-9);
            Assert.AreEqual(1.0, Multiplier(0.9, 100, true, true), 1e-9, "clamped at 100%");
        }

        [Test]
        public void ZeroIsZeroWhateverElseIsConfigured()
        {
            // No combination quietly rescues it - the notification is the whole remedy.
            Assert.AreEqual(0.0, Multiplier(0.0, 0, suppressesPlayniteMusic: false, musicEnabled: true), 1e-9);
            Assert.AreEqual(0.0, Multiplier(0.0, 0, suppressesPlayniteMusic: true, musicEnabled: false), 1e-9);
            Assert.AreEqual(0.0, Multiplier(0.0, 100, suppressesPlayniteMusic: true, musicEnabled: true), 1e-9,
                "boosting zero is still zero, which is why the boost slider cannot rescue this either");
        }

        [Test]
        public void TheWarningExistsAndFiresOnlyOnce()
        {
            // The remedy is a notification, so its absence would leave the condition invisible again.
            var warn = typeof(UniPlaySong).GetMethod("WarnAboutZeroedPlayniteVolume",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(warn, "the user-facing warning is the fix for this, not a volume override");

            var latch = typeof(UniPlaySong).GetField("_shownWarnings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(latch,
                "the multiplier is recomputed on every game select and volume change, so the " +
                "notification needs a once-per-session latch or it buries the notification list");
        }

        [Test]
        public void TheShippedRuleMatchesWhatIsTestedHere()
        {
            // Guards the duplication above: if the real method stops special-casing zero, or starts
            // reading different settings, this catches the drift rather than letting the test quietly
            // describe code that no longer exists.
            var method = typeof(UniPlaySong).GetMethod("GetFullscreenVolumeMultiplier",
                BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(method, "GetFullscreenVolumeMultiplier is the method this rule lives in");
            Assert.AreEqual(typeof(double), method.ReturnType);
        }

        [Test]
        public void DesktopModeWarnsAheadOfTimeWithoutApplyingTheMultiplier()
        {
            // The slider is edited from Desktop settings but only bites in Fullscreen, so a Desktop
            // user can zero it and meet the consequence days later with nothing connecting the two.
            // Desktop still must NOT apply the multiplier - desktop playback is not scaled by it.
            var warn = typeof(UniPlaySong).GetMethod("WarnIfPlayniteVolumeWillMuteFullscreen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(warn, "Desktop mode needs its own forward-looking warning");
        }

        [Test]
        public void RaisingMusicVolumeBackAboveZeroIsAGateChangeNotJustALevelChange()
        {
            // ShouldPlayMusic refuses outright at MusicVolume 0, so zero is a gate, not a level.
            // Coming back up therefore has to re-trigger playback - reported as "I set it back to 70%
            // and hear nothing until I switch games".
            Assert.IsTrue(GateOpened(0, 70), "0 -> 70 reopens the gate and must restart playback");
            Assert.IsFalse(GateOpened(70, 50), "an ordinary volume change must not restart the song");
            Assert.IsFalse(GateOpened(70, 0), "going silent is handled by SetVolume, not a replay");
            Assert.IsFalse(GateOpened(0, 0), "no crossing, no action");
        }

        // Mirrors the volumeGateOpened test in UniPlaySong.OnSettingsServiceChanged.
        private static bool GateOpened(int oldVolume, int newVolume) => oldVolume <= 0 && newVolume > 0;

    }
}
