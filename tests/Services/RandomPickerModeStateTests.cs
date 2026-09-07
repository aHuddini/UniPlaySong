using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Monitors;

namespace UniPlaySong.Tests.Services
{
    // Random Game Picker: two faults reported together in issue #95.
    //
    // The picker plays a track for each game as you press "Pick Another". It did that regardless of
    // Where Music Plays (Mode State), and its music overlapped the library's own - the picker's
    // track and the selected game's track audible at once in Fullscreen.
    [TestFixture]
    public class RandomPickerModeStateTests
    {
        private static UniPlaySongSettings With(AudioState state)
        {
            return new UniPlaySongSettings { EnableMusic = true, MusicState = state };
        }

        [Test]
        public void AlwaysPlaysInBothModes()
        {
            var s = With(AudioState.Always);
            Assert.IsTrue(s.AllowsMusicInMode(isFullscreen: true));
            Assert.IsTrue(s.AllowsMusicInMode(isFullscreen: false));
        }

        [Test]
        public void DesktopOnlyIsSilentInFullscreen()
        {
            // The reported case: the picker sang in Fullscreen with this set to Desktop only.
            var s = With(AudioState.Desktop);
            Assert.IsFalse(s.AllowsMusicInMode(isFullscreen: true));
            Assert.IsTrue(s.AllowsMusicInMode(isFullscreen: false));
        }

        [Test]
        public void FullscreenOnlyIsSilentOnDesktop()
        {
            var s = With(AudioState.Fullscreen);
            Assert.IsTrue(s.AllowsMusicInMode(isFullscreen: true));
            Assert.IsFalse(s.AllowsMusicInMode(isFullscreen: false));
        }

        [Test]
        public void NeverIsSilentEverywhere()
        {
            var s = With(AudioState.Never);
            Assert.IsFalse(s.AllowsMusicInMode(isFullscreen: true));
            Assert.IsFalse(s.AllowsMusicInMode(isFullscreen: false));
        }

        [Test]
        public void ThePickerAndTheCoordinatorShareOneModeRule()
        {
            // Both read AllowsMusicInMode. If the picker grew its own copy of the rule, the two
            // would drift and this setting would mean different things in different places - which
            // is how the picker came to ignore it in the first place.
            var rule = typeof(UniPlaySongSettings).GetMethod(nameof(UniPlaySongSettings.AllowsMusicInMode));

            Assert.NotNull(rule, "the mode rule must live on the settings, reachable by both callers");
            Assert.AreEqual(typeof(bool), rule.ReturnType);
            Assert.AreEqual(1, rule.GetParameters().Length, "mode in, allowed out - nothing else");
        }

        [Test]
        public void ThePickerAsksBeforeItHooksTheDialog()
        {
            // Gating at the hook, not at playback: if the mode disallows music the monitor never
            // takes ownership at all, so IsActive stays false and the coordinator carries on
            // normally instead of being told to stand down for a picker that will not play.
            var gate = typeof(RandomPickerMonitor).GetMethod("MusicAllowedHere",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(gate, "the picker must consult the mode state before hooking");
            Assert.AreEqual(typeof(bool), gate.ReturnType);
        }

        [Test]
        public void TheCoordinatorYieldsToThePickerBeforeTouchingPlayback()
        {
            // The overlap bug. The RandomPickerMonitor.IsActive guard used to sit AFTER the
            // ShouldPlayMusic gate, so the branches above it - the EnableMusic=off route into
            // PlayGameMusic, the null-game fade, and ShouldPlayMusic's own Stop() on a mode
            // mismatch - all ran while the picker owned playback, starting or stopping a track
            // underneath it. The guard must come first, before any branch that plays or stops.
            var path = System.IO.Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Services", "MusicPlaybackCoordinator.cs");

            if (!System.IO.File.Exists(path))
                Assert.Ignore("coordinator source not reachable from the test output directory");

            // Comments name these calls while explaining the ordering, so strip them first - the
            // assertion is about the order of actual calls, not of prose about them.
            var text = System.IO.File.ReadAllText(path);
            var stripped = System.Text.RegularExpressions.Regex.Replace(
                text, @"//[^
]*", string.Empty);

            var method = stripped.IndexOf("public void HandleGameSelected(");
            Assert.Greater(method, -1, "HandleGameSelected should exist");

            var body = stripped.Substring(method);
            var guard = body.IndexOf("RandomPickerMonitor.IsActive");
            Assert.Greater(guard, -1, "the picker guard should be inside HandleGameSelected");

            foreach (var call in new[] { "PlayGameMusic", "_playbackService?.Stop()", "ShouldPlayMusic" })
            {
                var first = body.IndexOf(call);
                if (first < 0) continue;
                Assert.Less(guard, first,
                    $"the picker guard must come before the first {call} - otherwise it can act on playback the picker owns");
            }
        }
    }
}
