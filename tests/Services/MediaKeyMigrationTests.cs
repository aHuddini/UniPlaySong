using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class MediaKeyMigrationTests
    {
        // The Experimental media-key toggle became keyboard shortcuts: a user who had it on gets media Play/Pause and Next
        // bound once, and a shortcut they already set is left alone
        [Test]
        public void OldMediaKeyToggleBecomesShortcutsOnce_KeepingUserBindings()
        {
            var s = new UniPlaySongSettings { EnableMediaKeyControl = true, ShortcutNext = "Ctrl+Alt+N" };
            Assert.IsTrue(SettingsService.MigrateMediaKeys(s));
            Assert.AreEqual("MediaPlayPause", s.ShortcutPlayPause);
            Assert.AreEqual("Ctrl+Alt+N", s.ShortcutNext);
            Assert.IsFalse(s.EnableMediaKeyControl);
            Assert.IsFalse(SettingsService.MigrateMediaKeys(s));
        }
    }
}
