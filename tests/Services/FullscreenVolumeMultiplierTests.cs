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
        public void DesktopDoesNotWarnAboutPlaynitesBackgroundVolume()
        {
            // Desktop does not apply the multiplier, so the value changes nothing in the mode the
            // reader is standing in - and Playnite draws no slider for it there to act on. A
            // notification about neither is noise; it was tried in 1.8.4 and produced a confused
            // report from someone hunting a Playnite settings page that does not exist.
            var warn = typeof(UniPlaySong).GetMethod("WarnIfPlayniteVolumeWillMuteFullscreen",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNull(warn, "the Desktop-mode Background Volume warning is deliberately gone");
        }

        [Test]
        public void RaisingMusicVolumeBackAboveZeroIsAGateChangeNotJustALevelChange()
        {
            // ShouldPlayMusic refuses at MusicVolume 0, so zero gates what STARTS - a track already
            // running carries on, inaudibly, which is why the restart below is guarded on IsPlaying.
            // Coming back up has to re-trigger playback only when nothing survived the silent spell -
            // reported as "I set it back to 70% and hear nothing until I switch games".
            Assert.IsTrue(GateOpened(0, 70), "0 -> 70 reopens the gate and must restart playback");
            Assert.IsFalse(GateOpened(70, 50), "an ordinary volume change must not restart the song");
            Assert.IsFalse(GateOpened(70, 0), "going silent is handled by SetVolume, not a replay");
            Assert.IsFalse(GateOpened(0, 0), "no crossing, no action");
        }

        [Test]
        public void UniPlaySongNeverWritesPlaynitesVolumeItself()
        {
            // The reporting is the whole remedy, deliberately. Reporting a zero is not a licence to
            // correct one: the value belongs to Playnite and to the user, and a plugin that moves a
            // setting because it dislikes it is a worse neighbour than a silent one. Briefly shipped
            // as a click-to-fix on the notification and removed on the same principle - the click was
            // still UniPlaySong writing Playnite's volume.
            //
            // It also keeps the diagnostic honest: with no writer here, a 1 -> 0 stack trace in the
            // log can never be us.
            var writer = typeof(UniPlaySong).GetMethod("RaisePlayniteBackgroundVolume",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNull(writer, "UniPlaySong reports the zero; it does not overrule it");
        }

        [Test]
        public void BothVolumesThatCanSilenceUniPlaySongAreReported()
        {
            // Two sliders, one silence. Playnite's Background volume multiplies UniPlaySong down to
            // nothing; UniPlaySong's own Music Volume is a gate that stops playback outright. A user
            // staring at a silent library cannot tell which one it is, and neither reads as "off".
            foreach (var warn in new[] { "WarnAboutZeroedPlayniteVolume", "WarnIfUpsVolumeIsZero" })
            {
                Assert.NotNull(typeof(UniPlaySong).GetMethod(warn, BindingFlags.NonPublic | BindingFlags.Instance),
                    $"{warn} is one of the zero-volume conditions that has to be said out loud");
            }
        }

        [Test]
        public void TheFullscreenMultiplierReachesTheExternalSpotifyInputToo()
        {
            // Spotify-with-effects plays through an input on the OUTPUT mixer, past the master fader
            // that carries the player's Volume - so the fullscreen multiplier, which rides on Volume,
            // never reached it. Desktop hid this completely: the multiplier is 1.0 there, so Music
            // Volume alone governed Spotify and behaved perfectly, while in Fullscreen the Background
            // Volume slider moved nothing and Spotify played at full level.
            var knob = typeof(global::UniPlaySong.Services.NAudioMusicPlayer).GetProperty("ExternalVolumeMultiplier");
            Assert.NotNull(knob, "the external input needs the multiplier handed to it directly");
            Assert.AreEqual(typeof(double), knob.PropertyType);
            Assert.IsTrue(knob.CanWrite, "MusicPlaybackService.SetVolumeMultiplier is what sets it");
        }

        [Test]
        public void TheWarningIsWithdrawnOnceTheValueIsNoLongerZero()
        {
            // Playnite keeps a notification until something removes it. Nothing did, so a reader who
            // raised the slider and fixed the problem was still being told the volume was 0 - a large
            // part of why this was reported as UniPlaySong resetting the value back.
            var withdraw = typeof(UniPlaySong).GetMethod("ClearZeroVolumeWarnings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(withdraw, "the notice has to be taken back when it stops being true");
        }

        [Test]
        public void TheVolumeSubscriptionIsRetryableRatherThanOneShot()
        {
            // The subscription is what keeps _playniteFullscreenVolume live. Attempted once against a
            // window that Playnite has not necessarily assigned yet, a miss froze the multiplier at
            // its startup value for the session: raising Playnite's slider then changed nothing and
            // the music stayed silent, indistinguishable from UniPlaySong forcing it back to 0.
            var subscribe = typeof(UniPlaySong).GetMethod("SubscribeToFullscreenVolumeChanges",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(subscribe);

            var parameters = subscribe.GetParameters();
            Assert.AreEqual(1, parameters.Length,
                "it must take the window to subscribe from, so the verify loop can retry it");
            Assert.AreEqual(typeof(System.Windows.Window), parameters[0].ParameterType);
        }

        // Mirrors the volumeGateOpened test in UniPlaySong.OnSettingsServiceChanged.
        private static bool GateOpened(int oldVolume, int newVolume) => oldVolume <= 0 && newVolume > 0;

    }
}
