using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong;

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
            var text = ReadSource("src", "Monitors", "IdleMonitor.cs");

            var poll = text.IndexOf("private void OnIdlePollTick(");
            Assert.Greater(poll, -1, "the shared idle poll should exist");

            var body = text.Substring(poll, text.IndexOf("private void StartIdleVolumeFade(") - poll);

            StringAssert.Contains("CalmDownIdleActive", body, "idle drives the runtime flag");
            Assert.IsFalse(body.Contains("CalmDownModeEnabled ="),
                "idle must never assign the persisted user setting");
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
            var text = ReadSource("src", "Monitors", "IdleMonitor.cs");
            var poll = text.IndexOf("private void OnIdlePollTick(");
            var body = text.Substring(poll, text.IndexOf("private void StartIdleVolumeFade(") - poll);

            StringAssert.Contains("_settings.CalmDownIdleActive != wantCalm", body,
                "every tick compares and corrects, so the flag survives a settings save");
        }
    }
}
