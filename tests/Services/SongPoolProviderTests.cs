using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Covers the folder-backed pool paths, which need no Playnite database. The library-backed
    // sources (RandomGame, FullLibrary, CustomRotation, CompletionStatusPool) go through
    // IPlayniteAPI and are exercised only for their null-API safety here.
    [TestFixture]
    public class SongPoolProviderTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ups_pool_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        private string Touch(string name)
        {
            var p = Path.Combine(_dir, name);
            File.WriteAllText(p, "x");
            return p;
        }

        // api is null throughout: these paths must not touch the database.
        private static SongPoolProvider Provider() => new SongPoolProvider(null, () => null, null);

        [Test]
        public void DefaultPool_CustomFolder_ReturnsOnlySupportedAudio()
        {
            Touch("a.mp3");
            Touch("b.ogg");
            Touch("notes.txt");
            Touch("cover.jpg");

            Directory.CreateDirectory(Path.Combine(_dir, "sub"));
            File.WriteAllText(Path.Combine(_dir, "sub", "nested.mp3"), "x"); // subfolders aren't scanned

            var settings = new UniPlaySongSettings { DefaultMusicFolderPaths = new List<string> { _dir } };
            var songs = Provider().GetDefaultSongPool(DefaultMusicSource.CustomFolder, settings);

            Assert.AreEqual(2, songs.Count);
            CollectionAssert.AllItemsAreUnique(songs);
        }

        [Test]
        public void DefaultPool_CustomFolder_MissingFolderReturnsEmpty()
        {
            var settings = new UniPlaySongSettings
            {
                DefaultMusicFolderPaths = new List<string> { Path.Combine(_dir, "does-not-exist") }
            };

            var songs = Provider().GetDefaultSongPool(DefaultMusicSource.CustomFolder, settings);

            Assert.IsNotNull(songs);
            Assert.IsEmpty(songs);
        }

        // v1.5.8 decoupled the radio's folder from the default-music folder, but kept a fallback so
        // users who never picked a radio-specific folder keep their previous behaviour.
        [Test]
        public void RadioPool_CustomFolder_FallsBackToDefaultFolderWhenRadioFolderUnset()
        {
            Touch("song.mp3");

            var settings = new UniPlaySongSettings
            {
                DefaultMusicFolderPaths = new List<string> { _dir }
            };

            var songs = Provider().GetRadioSongPool(RadioMusicSource.CustomFolder, settings);

            Assert.AreEqual(1, songs.Count);
        }

        [Test]
        public void RadioPool_CustomFolder_PrefersRadioFolderOverDefault()
        {
            Touch("radio.mp3");

            var otherDir = Path.Combine(Path.GetTempPath(), "ups_pool_other_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(otherDir);
            try
            {
                File.WriteAllText(Path.Combine(otherDir, "default1.mp3"), "x");
                File.WriteAllText(Path.Combine(otherDir, "default2.mp3"), "x");

                var settings = new UniPlaySongSettings
                {
                    RadioCustomFolderPaths = new List<string> { _dir },
                    DefaultMusicFolderPaths = new List<string> { otherDir }
                };

                var songs = Provider().GetRadioSongPool(RadioMusicSource.CustomFolder, settings);

                Assert.AreEqual(1, songs.Count, "radio folder should win, not the 2-file default folder");
                StringAssert.Contains("radio.mp3", songs[0]);
            }
            finally
            {
                try { Directory.Delete(otherDir, true); } catch { }
            }
        }

        // Several folders merge into one pool; the same folder written differently counts once (the list setter keeps
        // one; the pool keeps one even if handed both); a missing folder is skipped, not an error
        [Test]
        public void DefaultPool_SeveralFolders_MergedOnceEach_MissingSkipped()
        {
            var second = Path.Combine(_dir, "second");
            Directory.CreateDirectory(second);
            var a = Touch("a.mp3");
            var b = Path.Combine(second, "b.ogg");
            File.WriteAllText(b, "x");

            var settings = new UniPlaySongSettings
            {
                DefaultMusicFolderPaths = new List<string> { _dir, second, Path.Combine(_dir, "gone") }
            };
            var songs = Provider().GetDefaultSongPool(DefaultMusicSource.CustomFolder, settings);

            CollectionAssert.AreEquivalent(new[] { a, b }, songs);
        }

        [Test]
        public void RadioPool_BothListsEmpty_EmptyPool()
        {
            var songs = Provider().GetRadioSongPool(RadioMusicSource.CustomFolder, new UniPlaySongSettings());
            Assert.IsNotNull(songs);
            Assert.IsEmpty(songs);
        }

        // Every source must be handled in BOTH switches (CompletionStatusPool was once easy to add to one and forget
        // in the other). Neither may fall through to a null return.
        [Test]
        public void NullSettings_NeverThrows()
        {
            var p = Provider();

            foreach (DefaultMusicSource s in Enum.GetValues(typeof(DefaultMusicSource)))
                Assert.IsNotNull(p.GetDefaultSongPool(s, null), $"default pool {s} returned null");

            foreach (RadioMusicSource s in Enum.GetValues(typeof(RadioMusicSource)))
                Assert.IsNotNull(p.GetRadioSongPool(s, null), $"radio pool {s} returned null");
        }
    }
}
