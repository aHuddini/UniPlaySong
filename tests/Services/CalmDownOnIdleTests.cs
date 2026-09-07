using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Calm Down after a stretch with no input, and back off on the next keypress. Shares the idle
    // poll with Pause on Idle and Lower Volume on Idle.
    //
    // The whole design rests on one rule: idle must never write CalmDownModeEnabled. That is a
    // persisted setting, so writing it would save to disk, tick the checkbox on the settings page,
    // and — worst — releasing on input would switch off a Calm Down the user had turned on
    // themselves. Idle uses a separate runtime-only flag that the processor ORs with the setting.
    [TestFixture]
    public class CalmDownOnIdleTests
    {
        private static string ReadSource(params string[] parts)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..");
            foreach (var part in parts) path = Path.Combine(path, part);
            if (!File.Exists(path)) Assert.Ignore($"{parts.Last()} not reachable from the test output directory");
            return File.ReadAllText(path);
        }

        [Test]
        public void ItIsOffByDefaultAndTimesOutInTenMinutes()
        {
            var s = new UniPlaySongSettings();

            Assert.IsFalse(s.CalmDownOnIdle, "opt-in — nobody's music changes on upgrade");
            Assert.AreEqual(10, s.CalmDownIdleTimeoutMinutes);
            Assert.IsFalse(s.CalmDownIdleActive, "runtime state starts clear every launch");
        }

        [Test]
        public void TheTimeoutIsClampedLikeTheOtherIdleSettings()
        {
            var s = new UniPlaySongSettings();

            s.CalmDownIdleTimeoutMinutes = 0;
            Assert.AreEqual(1, s.CalmDownIdleTimeoutMinutes);

            s.CalmDownIdleTimeoutMinutes = 999;
            Assert.AreEqual(60, s.CalmDownIdleTimeoutMinutes, "matches Pause on Idle and Lower Volume on Idle");
        }

        [Test]
        public void IdleNeverWritesTheUsersOwnToggle()
        {
            // The rule this feature depends on. If the poll ever assigns CalmDownModeEnabled, a user
            // who switched Calm Down on manually gets it switched off the moment they touch the mouse.
            var text = ReadSource("src", "UniPlaySong.cs");

            var poll = text.IndexOf("private void OnIdlePollTick(");
            Assert.Greater(poll, -1, "the shared idle poll should exist");

            var body = text.Substring(poll, text.IndexOf("private void StartIdleVolumeFade(") - poll);

            StringAssert.Contains("CalmDownIdleActive", body, "idle drives the runtime flag");
            Assert.IsFalse(body.Contains("CalmDownModeEnabled ="),
                "idle must never assign the persisted user setting");
        }

        [Test]
        public void EitherReasonEngagesTheEffect()
        {
            // The processor ORs them, so the user's toggle and idle are independent routes to the
            // same ramp — and one cannot cancel the other.
            var text = ReadSource("src", "Audio", "CalmDownProcessor.cs");

            StringAssert.Contains("s.CalmDownModeEnabled || s.CalmDownIdleActive", text,
                "both reasons must engage Calm Down");
        }

        [Test]
        public void OptingInForcesTheBackendThatCanHostIt()
        {
            // Calm Down needs the NAudio pipeline. Deciding that at idle time would mean swapping the
            // player mid-idle, which restarts the song — so enabling the setting settles it up front.
            var text = ReadSource("src", "UniPlaySong.cs");

            StringAssert.Contains("CalmDownOnIdle ?? false", text,
                "the player choice must account for idle Calm Down, not just the manual toggle");
        }

        [Test]
        public void EnablingItMidSessionRebuildsOntoAPlayerThatCanHostIt()
        {
            // CreateMusicPlayer includes CalmDownOnIdle, so a cold start lands on NAudio and the
            // feature works. Enabling it in the settings dialog is the other route in, and it used
            // to leave an SDL2 player in place with no CalmDownProcessor — the flag flipped on
            // schedule and absolutely nothing happened, with nothing in the log to say why.
            // Reported as "set it to 1 minute and nothing happens".
            var text = ReadSource("src", "UniPlaySong.cs");

            var handler = text.IndexOf("private void OnSettingsServiceChanged(");
            Assert.Greater(handler, -1);

            // OnSettingsServiceChanged is long; the backend-swap block sits well into it.
            var body = text.Substring(handler, System.Math.Min(16000, text.Length - handler));
            StringAssert.Contains("CalmDownOnIdle != e.NewSettings.CalmDownOnIdle", body,
                "toggling the idle setting must be able to trigger the backend swap, like the manual toggle");
        }

        [Test]
        public void TheRuntimeFlagIsNeverPersisted()
        {
            // [JsonIgnore] keeps a transient state out of the settings file, and out of the JSON
            // clone a settings save round-trips through.
            var prop = typeof(UniPlaySongSettings).GetProperty(nameof(UniPlaySongSettings.CalmDownIdleActive));
            Assert.NotNull(prop);

            var ignored = prop.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true);
            Assert.IsNotEmpty(ignored, "idle state must not be written to the settings file");
        }

        [Test]
        public void ThePollReassertsTheFlagRatherThanSettingItOnce()
        {
            // Because the flag is [JsonIgnore], a settings save clones it away. Setting it only on
            // the idle transition would leave an unrelated save mid-idle quietly lifting Calm Down
            // until the next input.
            var text = ReadSource("src", "UniPlaySong.cs");
            var poll = text.IndexOf("private void OnIdlePollTick(");
            var body = text.Substring(poll, text.IndexOf("private void StartIdleVolumeFade(") - poll);

            StringAssert.Contains("_settings.CalmDownIdleActive != wantCalm", body,
                "every tick compares and corrects, so the flag survives a settings save");
        }

        [Test]
        public void TheSettingsResetWithTheGroupWhosePageTheyAreOn()
        {
            var liveEffects = (string)typeof(SettingsGroups)
                .GetField("LiveEffects", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();

            var map = typeof(SettingsGroups)
                .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                ?.GetValue(null) as IReadOnlyDictionary<string, string[]>;
            Assert.NotNull(map);

            foreach (var name in new[]
            {
                nameof(UniPlaySongSettings.CalmDownOnIdle),
                nameof(UniPlaySongSettings.CalmDownIdleTimeoutMinutes),
            })
            {
                Assert.IsTrue(map[liveEffects].Contains(name),
                    $"{name} is on the Calm Down page, which resets with Live Effects");
            }

            var neverReset = typeof(SettingsGroups)
                .GetField("NeverReset", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as HashSet<string>;
            Assert.NotNull(neverReset);
            Assert.IsTrue(neverReset.Contains(nameof(UniPlaySongSettings.CalmDownIdleActive)),
                "live state has nothing for a reset to restore");
        }
    }
}
