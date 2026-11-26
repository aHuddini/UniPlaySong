using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class SettingsMigrationTests
    {
        // Custom Folder became a list in 1.9.6: the old single folder moves in once and is cleared, so a list the user
        // empties stays empty; the same folder written differently is listed once
        [Test]
        public void MigrateFolderLists_MovesTheOldPathOnceAndClearsIt()
        {
            var s = new UniPlaySongSettings { DefaultMusicFolderPath = @"D:\Music", RadioCustomFolderPath = @"E:\Radio" };

            Assert.IsTrue(SettingsService.MigrateFolderLists(s));
            Assert.AreEqual(new[] { @"D:\Music" }, s.DefaultMusicFolderPaths.ToArray());
            Assert.AreEqual(new[] { @"E:\Radio" }, s.RadioCustomFolderPaths.ToArray());
            Assert.IsTrue(string.IsNullOrEmpty(s.DefaultMusicFolderPath) && string.IsNullOrEmpty(s.RadioCustomFolderPath));

            Assert.IsFalse(SettingsService.MigrateFolderLists(s));   // the next load: nothing to do
            s.DefaultMusicFolderPaths = new List<string>();           // the user removed every folder
            Assert.IsFalse(SettingsService.MigrateFolderLists(s));
            Assert.IsEmpty(s.DefaultMusicFolderPaths);                // and none comes back

            s.RadioCustomFolderPaths = new List<string> { @"E:\Radio", @"e:\radio\", null, " " };
            Assert.AreEqual(new[] { @"E:\Radio" }, s.RadioCustomFolderPaths.ToArray());
        }
    }
}
