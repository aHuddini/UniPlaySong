using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Add Music Folder copies what plays, plus what the playable files need to play, and nothing
    // that could break an existing set.
    [TestFixture]
    public class ImportMusicFolderTests
    {
        private string _src;
        private string _dest;

        [SetUp]
        public void SetUp()
        {
            var root = Path.Combine(Path.GetTempPath(), "ups_import_" + Guid.NewGuid().ToString("N"));
            _src = Path.Combine(root, "src");
            _dest = Path.Combine(root, "dest");
            Directory.CreateDirectory(_src);
            Directory.CreateDirectory(_dest);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(Path.GetDirectoryName(_src), true); } catch { }
        }

        private void Touch(string dir, string name, string content = "x") => File.WriteAllText(Path.Combine(dir, name), content);

        private string[] DestNames() => Directory.GetFiles(_dest).Select(Path.GetFileName).OrderBy(n => n).ToArray();

        [Test]
        public void CopiesPlayableFormatsAndTheirCompanions()
        {
            Touch(_src, "a.mp3");
            Touch(_src, "b.VGM");
            Touch(_src, "c.minipsf");
            Touch(_src, "driver.psflib");         // a minipsf is silent without its library
            Touch(_src, "d.hes");
            Touch(_src, "d.m3u");                 // the .hes track list - same basename
            Touch(_src, "playlist.m3u");          // an ordinary playlist - no matching .hes
            Touch(_src, "cover.jpg");
            Touch(_src, "notes.txt");
            Directory.CreateDirectory(Path.Combine(_src, "Disc 2"));
            Touch(Path.Combine(_src, "Disc 2"), "e.mp3");   // top level only

            var (copied, skipped) = GameMusicFileService.ImportMusicFolder(_src, _dest);

            CollectionAssert.AreEqual(
                new[] { "a.mp3", "b.VGM", "c.minipsf", "d.hes", "d.m3u", "driver.psflib" },
                DestNames());
            Assert.AreEqual(6, copied);
            Assert.AreEqual(0, skipped);
        }

        // A renamed .psflib breaks every _lib tag that names it, and re-importing the same folder
        // should not double the songs - so an existing name is left alone and counted.
        [Test]
        public void ExistingNamesAreSkippedNotRenamedOrOverwritten()
        {
            Touch(_dest, "a.mp3", "original");
            Touch(_dest, "driver.psflib", "original");
            Touch(_src, "a.mp3", "incoming");
            Touch(_src, "driver.psflib", "incoming");
            Touch(_src, "b.mp3");

            var (copied, skipped) = GameMusicFileService.ImportMusicFolder(_src, _dest);

            Assert.AreEqual(1, copied);
            Assert.AreEqual(2, skipped);
            CollectionAssert.AreEqual(new[] { "a.mp3", "b.mp3", "driver.psflib" }, DestNames());
            Assert.AreEqual("original", File.ReadAllText(Path.Combine(_dest, "a.mp3")));
            Assert.AreEqual("original", File.ReadAllText(Path.Combine(_dest, "driver.psflib")));
        }

        [Test]
        public void FolderWithNoMusicCopiesNothing()
        {
            Touch(_src, "cover.jpg");

            var (copied, skipped) = GameMusicFileService.ImportMusicFolder(_src, _dest);

            Assert.AreEqual(0, copied);
            Assert.AreEqual(0, skipped);
            Assert.IsEmpty(DestNames());
        }
    }
}
