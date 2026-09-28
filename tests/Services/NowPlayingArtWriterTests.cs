using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // The art file a theme displays is held open by WPF while shown, and until a GC after the binding
    // moves on. The previous writer deleted each old file at the moment the next was written - exactly
    // when it is still locked - swallowed the failure, and never tried again: one leaked file per song
    // change, 40,000 of them on one machine. These tests pin the replacement: content-addressed files,
    // a sweep that spares what may still be displayed and retries what is locked, and a one-time
    // cleanup of the old files that cannot reach anything else.
    [TestFixture]
    public class NowPlayingArtWriterTests
    {
        private string _root;
        private string _artDir;
        private NowPlayingArtWriter _writer;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ups_art_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _writer = new NowPlayingArtWriter(_root, null);
            _artDir = _writer.ArtDirectory;
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        // Sweep leaves anything written in the last minute alone; tests age files instead of waiting.
        private static void Age(string path) => File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));

        private string[] ArtFiles() => Directory.Exists(_artDir) ? Directory.GetFiles(_artDir) : new string[0];

        [Test]
        public void WriteBytes_WritesIntoTheArtFolder_AndReportsTheCurrentPath()
        {
            var path = _writer.WriteBytes(new byte[] { 1, 2, 3, 4 });

            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(_artDir, Path.GetDirectoryName(path));
            Assert.AreEqual(path, _writer.ArtFilePath);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
        }

        // Same art, same path, nothing rewritten: a displayed (locked, cached) file is never touched, and a
        // theme's image cache can never show stale bytes because a name always means the same bytes.
        [Test]
        public void SameBytes_ReturnTheSamePath_WithoutRewriting()
        {
            var first = _writer.WriteBytes(new byte[] { 7, 7, 7 });
            Age(first);
            var stamp = File.GetLastWriteTimeUtc(first);

            var second = _writer.WriteBytes(new byte[] { 7, 7, 7 });

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, ArtFiles().Length);
            Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(second), "the existing file was rewritten");
        }

        // Different art must change the path, or a theme's binding never updates.
        [Test]
        public void DifferentBytes_ReturnDifferentPaths()
        {
            Assert.AreNotEqual(_writer.WriteBytes(new byte[] { 1 }), _writer.WriteBytes(new byte[] { 2 }));
        }

        [Test]
        public void Sweep_KeepsCurrentAndPrevious_DeletesOlder()
        {
            var a = _writer.WriteBytes(new byte[] { 1 });
            var b = _writer.WriteBytes(new byte[] { 2 });
            var c = _writer.WriteBytes(new byte[] { 3 });
            foreach (var f in new[] { a, b, c }) Age(f);

            Assert.AreEqual(1, _writer.Sweep());

            Assert.IsFalse(File.Exists(a), "an old file survived the sweep");
            Assert.IsTrue(File.Exists(b), "the previous file may still be on screen and must be kept");
            Assert.IsTrue(File.Exists(c), "the current file was deleted");
        }

        // The failure the old writer made permanent. A file held open - what a theme's Image does while it
        // shows it - survives the sweep, and the next sweep after release removes it.
        [Test]
        public void LockedFile_SurvivesASweep_AndGoesOnTheNextOneAfterRelease()
        {
            var old = _writer.WriteBytes(new byte[] { 1 });
            _writer.WriteBytes(new byte[] { 2 });
            _writer.WriteBytes(new byte[] { 3 });
            foreach (var f in ArtFiles()) Age(f);

            using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.AreEqual(0, _writer.Sweep());
                Assert.IsTrue(File.Exists(old));
            }

            Assert.AreEqual(1, _writer.Sweep());
            Assert.IsFalse(File.Exists(old));
        }

        [Test]
        public void Sweep_LeavesRecentWritesAlone()
        {
            _writer.WriteBytes(new byte[] { 1 });
            _writer.WriteBytes(new byte[] { 2 });
            _writer.WriteBytes(new byte[] { 3 }); // none aged: all inside the grace period

            Assert.AreEqual(0, _writer.Sweep());
            Assert.AreEqual(3, ArtFiles().Length);
        }

        // Clear deletes nothing: the art that was showing may still be locked. It becomes "previous".
        [Test]
        public void Clear_DeletesNothing_AndKeepsTheLastArtAsPrevious()
        {
            var a = _writer.WriteBytes(new byte[] { 1 });
            Age(a);

            _writer.Clear();

            Assert.AreEqual(string.Empty, _writer.ArtFilePath);
            Assert.AreEqual(0, _writer.Sweep());
            Assert.IsTrue(File.Exists(a));
        }

        [Test]
        public void Reuse_ReclaimsAnExistingFile_OrReportsItGone()
        {
            var a = _writer.WriteBytes(new byte[] { 1 });
            _writer.WriteBytes(new byte[] { 2 });

            Assert.AreEqual(a, _writer.Reuse(a));
            Assert.AreEqual(a, _writer.ArtFilePath);

            File.Delete(a);
            Assert.AreEqual(string.Empty, _writer.Reuse(a));
        }

        // The one-time cleanup runs in the root that also holds every game's music folder and UPS's side
        // files. It must match the old art files exactly and nothing else.
        [Test]
        public void SweepLegacy_RemovesOnlyTheOldArtFiles()
        {
            File.WriteAllText(Path.Combine(_root, "nowplaying_art_1.png"), "x");
            File.WriteAllText(Path.Combine(_root, "nowplaying_art_49463.png"), "x");
            File.WriteAllText(Path.Combine(_root, "nowplaying_art.png"), "keep");      // no number
            File.WriteAllText(Path.Combine(_root, "nowplaying_art_x1.png"), "keep");   // not digits
            File.WriteAllText(Path.Combine(_root, "nowplaying_art_1.pngx"), "keep");   // not .png
            File.WriteAllText(Path.Combine(_root, "nowplaying_art_1.txt"), "keep");
            File.WriteAllText(Path.Combine(_root, "listening-history.json"), "keep");
            var gameDir = Directory.CreateDirectory(Path.Combine(_root, "Games", "g1")).FullName;
            File.WriteAllText(Path.Combine(gameDir, "nowplaying_art_2.png"), "keep");  // never recurses
            File.WriteAllText(Path.Combine(gameDir, "song.mp3"), "keep");

            Assert.AreEqual(2, _writer.SweepLegacy());

            var left = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(_root.Length + 1)).OrderBy(n => n).ToArray();
            CollectionAssert.AreEquivalent(new[]
            {
                "listening-history.json",
                "nowplaying_art.png",
                "nowplaying_art_1.pngx",
                "nowplaying_art_1.txt",
                "nowplaying_art_x1.png",
                Path.Combine("Games", "g1", "nowplaying_art_2.png"),
                Path.Combine("Games", "g1", "song.mp3"),
            }, left);
        }

        [Test]
        public void WriteBytes_WithNullOrEmpty_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _writer.WriteBytes(null));
            Assert.AreEqual(string.Empty, _writer.WriteBytes(new byte[0]));
        }

        [Test]
        public void WriteFromAudioFile_NonexistentOrNullPath_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, _writer.WriteFromAudioFile(Path.Combine(_root, "nope.mp3")));
            Assert.AreEqual(string.Empty, _writer.WriteFromAudioFile(null));
        }

        [Test]
        public void Sweep_WithNoArtFolder_ReturnsZero()
        {
            Assert.AreEqual(0, _writer.Sweep());
        }
    }
}
