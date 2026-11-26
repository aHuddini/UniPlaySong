using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class RecentTrackArtCacheTests
    {
        private string _root;

        [SetUp]
        public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "ups-recent-art-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string Song(string relative)
        {
            var path = Path.Combine(_root, "songs", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        private static BitmapSource Thumb()
        {
            var bmp = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr32, null, new byte[4 * 4 * 4], 4 * 4);
            bmp.Freeze();
            return bmp;
        }

        // A thumbnail kept for a listed song comes back after a restart; a game song's and other music's go to their
        // own caches; a song that left the list loses its file, so the cache never outgrows the list
        [Test]
        public void SavedThumbsReload_SeparateCaches_PrunedToTheList()
        {
            var cache = new RecentTrackArtCache(_root);
            var gameSong = Song(Path.Combine("Games", Guid.NewGuid().ToString(), "battle.mp3"));
            var folderSong = Song(Path.Combine("MyMusic", "track.mp3"));

            cache.Save(gameSong, Thumb());
            cache.Save(folderSong, Thumb());

            StringAssert.Contains(Path.Combine("RecentTrackArt", "Games"), cache.PathFor(gameSong));
            StringAssert.Contains(Path.Combine("RecentTrackArt", "OtherMusic"), cache.PathFor(folderSong));
            Assert.IsNotNull(new RecentTrackArtCache(_root).Load(gameSong));
            Assert.IsNotNull(new RecentTrackArtCache(_root).Load(folderSong));

            cache.Prune(new[] { folderSong, "Title\nArtist\nAlbum" });   // an old row's key is not a path: ignored
            Assert.IsFalse(File.Exists(cache.PathFor(gameSong)));
            Assert.IsTrue(File.Exists(cache.PathFor(folderSong)));

            // The song file replaced since: its old picture is not shown
            File.SetLastWriteTimeUtc(folderSong, DateTime.UtcNow.AddMinutes(5));
            Assert.IsNull(cache.Load(folderSong));
        }
    }
}
