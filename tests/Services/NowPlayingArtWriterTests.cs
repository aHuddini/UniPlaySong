using System.IO;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class NowPlayingArtWriterTests
    {
        private string _dir;
        private NowPlayingArtWriter _writer;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ups_art_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _writer = new NowPlayingArtWriter(_dir, null);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Test]
        public void WriteBytes_WithValidBytes_WritesFileAndReturnsPath()
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            var path = _writer.WriteBytes(bytes);
            Assert.IsNotEmpty(path);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(bytes, File.ReadAllBytes(path));
            Assert.AreEqual(path, _writer.ArtFilePath);
        }

        [Test]
        public void WriteBytes_TwiceWithDifferentBytes_ReturnsDifferentPaths_AndDeletesOld()
        {
            var pathA = _writer.WriteBytes(new byte[] { 1, 2, 3 });
            Assert.IsTrue(File.Exists(pathA));

            var pathB = _writer.WriteBytes(new byte[] { 4, 5, 6 });
            Assert.AreNotEqual(pathA, pathB);
            Assert.IsTrue(File.Exists(pathB));
            Assert.IsFalse(File.Exists(pathA)); // old file deleted
            Assert.AreEqual(pathB, _writer.ArtFilePath);
        }

        [Test]
        public void WriteBytes_WithNullOrEmpty_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _writer.WriteBytes(null));
            Assert.AreEqual(string.Empty, _writer.WriteBytes(new byte[0]));
        }

        [Test]
        public void WriteFromAudioFile_NonexistentFile_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _writer.WriteFromAudioFile(Path.Combine(_dir, "nope.mp3")));
        }

        [Test]
        public void WriteFromAudioFile_NullPath_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _writer.WriteFromAudioFile(null));
        }

        [Test]
        public void Clear_RemovesFile_AndDoesNotThrowWhenMissing()
        {
            _writer.WriteBytes(new byte[] { 9 });
            Assert.IsTrue(File.Exists(_writer.ArtFilePath));
            _writer.Clear();
            Assert.IsFalse(File.Exists(_writer.ArtFilePath));
            Assert.DoesNotThrow(() => _writer.Clear()); // already gone
        }
    }
}
