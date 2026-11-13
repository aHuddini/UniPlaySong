using NUnit.Framework;
using UniPlaySong.Monitors;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Issue #78: brief silences in a video resumed the music, because one setting timed both the pause and the resume
    // and its default (0) resumed on the first silent poll.
    [TestFixture]
    public class ExternalAudioResumeDelayTests
    {
        [TestCase(0, 500.0, 1)]   // instant: the next poll
        [TestCase(0, 1000.0, 1)]
        [TestCase(2, 500.0, 4)]   // the new default, on the instant-mode poll
        [TestCase(2, 1000.0, 2)]
        [TestCase(3, 1000.0, 3)]
        [TestCase(15, 500.0, 30)]
        public void PollsForCoversTheSeconds(int seconds, double intervalMs, int expected)
        {
            Assert.AreEqual(expected, ExternalAudioMonitor.PollsFor(seconds, intervalMs));
        }

        [Test]
        public void ResumeDelayDefaultsToTwoSeconds()
        {
            Assert.AreEqual(2, new UniPlaySongSettings().ExternalAudioResumeDelaySeconds);
        }

        // A debounce the user set used to time the resume too, so it carries over; the default 0 takes the new default.
        [TestCase(0, 2)]
        [TestCase(4, 4)]
        [TestCase(10, 10)]
        public void MigrationKeepsAChosenDebounceAsTheResumeDelay(int debounce, int expectedResume)
        {
            var s = new UniPlaySongSettings { ExternalAudioDebounceSeconds = debounce };

            Assert.IsTrue(SettingsService.MigrateExternalAudioResumeDelay(s));
            Assert.AreEqual(expectedResume, s.ExternalAudioResumeDelaySeconds);
            Assert.IsTrue(s.ExternalAudioResumeDelayMigrated);
        }

        [Test]
        public void MigrationRunsOnceSoALaterChoiceSticks()
        {
            var s = new UniPlaySongSettings { ExternalAudioDebounceSeconds = 5 };
            SettingsService.MigrateExternalAudioResumeDelay(s);
            s.ExternalAudioResumeDelaySeconds = 1;

            Assert.IsFalse(SettingsService.MigrateExternalAudioResumeDelay(s));
            Assert.AreEqual(1, s.ExternalAudioResumeDelaySeconds);
        }
    }
}
