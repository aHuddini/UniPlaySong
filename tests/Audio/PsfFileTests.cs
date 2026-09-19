using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UniPlaySong.Audio;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Audio
{
    // The container parser against the spec (Corlett, PSF v1.4), on files built here so every
    // rule has a case: load order with libraries, whose region decides the refresh rate, the
    // three length formats, the tag whitespace and case rules, and the CRC that the spec says
    // makes a file corrupt when it does not match.
    [TestFixture]
    public class PsfFileTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ups_psf_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // ---- builders --------------------------------------------------------------------------------

        // A PS-X EXE: 2048-byte header with the region marker at 0x4c, then `text`.
        private static byte[] Exe(string region, byte[] text)
        {
            var header = new byte[2048];
            Encoding.ASCII.GetBytes("PS-X EXE").CopyTo(header, 0);
            BitConverter.GetBytes(0x80010000u).CopyTo(header, 0x10);
            BitConverter.GetBytes(0x80010000u).CopyTo(header, 0x18);
            BitConverter.GetBytes((uint)text.Length).CopyTo(header, 0x1c);
            Encoding.ASCII.GetBytes("Sony Computer Entertainment Inc. for " + region + " area").CopyTo(header, 0x4c);
            return header.Concat(text).ToArray();
        }

        private static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9c);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    deflate.Write(data, 0, data.Length);
                // Adler-32 trailer, so the stream is a real zlib stream and not just what our reader tolerates
                uint a = 1, b = 0;
                foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
                uint adler = b << 16 | a;
                ms.Write(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, 0, 4);
                return ms.ToArray();
            }
        }

        // Bitwise CRC-32, independent of the table in PsfFile - a shared bug would cancel out.
        private static uint Crc(byte[] data)
        {
            uint crc = 0xffffffff;
            foreach (var x in data)
            {
                crc ^= x;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            }
            return ~crc;
        }

        private static byte[] Psf(byte[] exe, string tags, uint? crcOverride = null, byte version = 1)
        {
            var z = Zlib(exe);
            using (var ms = new MemoryStream())
            {
                ms.Write(Encoding.ASCII.GetBytes("PSF"), 0, 3);
                ms.WriteByte(version);
                ms.Write(BitConverter.GetBytes(0u), 0, 4);
                ms.Write(BitConverter.GetBytes((uint)z.Length), 0, 4);
                ms.Write(BitConverter.GetBytes(crcOverride ?? Crc(z)), 0, 4);
                ms.Write(z, 0, z.Length);
                if (tags != null)
                {
                    var block = Encoding.UTF8.GetBytes("[TAG]" + tags);
                    ms.Write(block, 0, block.Length);
                }
                return ms.ToArray();
            }
        }

        private string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static byte[] Text(string marker) => Encoding.ASCII.GetBytes(marker).Concat(new byte[64]).ToArray();

        // ---- tests --------------------------------------------------------------------------------

        [Test]
        public void LoadsProgramAndTags()
        {
            var exe = Exe("North America", Text("PRIMARY"));
            var path = Write("song.psf", Psf(exe, "title=Run Straight\nartist=Yoshino Aoki\ngame=Breath of Fire IV\nlength=1:26\nfade=10\n"));

            var psf = PsfFile.Load(path);

            Assert.AreEqual(1, psf.Sections.Count);
            CollectionAssert.AreEqual(exe, psf.Sections[0], "the program must come back exactly as it went in");
            Assert.AreEqual("Run Straight", psf.Title);
            Assert.AreEqual("Yoshino Aoki", psf.Artist);
            Assert.AreEqual("Breath of Fire IV", psf.Game);
            Assert.AreEqual(TimeSpan.FromSeconds(86), psf.Length);
            Assert.AreEqual(TimeSpan.FromSeconds(10), psf.Fade);
            Assert.AreEqual(TimeSpan.FromSeconds(96), psf.Duration);
            Assert.AreEqual(60, psf.RefreshHz);
        }

        // Spec: _lib loads first and supplies PC/SP, the file's own program goes on top, then
        // _lib2, _lib3... in order.
        [Test]
        public void LibrariesLoadInSpecOrder()
        {
            Write("driver.psflib", Psf(Exe("Japan", Text("LIB")), null));
            Write("extra.psflib", Psf(Exe("Japan", Text("LIB2")), null));
            var path = Write("song.minipsf", Psf(Exe("Japan", Text("PRIMARY")), "_lib=driver.psflib\n_lib2=extra.psflib\nlength=0:30\n"));

            var psf = PsfFile.Load(path);

            Assert.AreEqual(3, psf.Sections.Count);
            StringAssert.StartsWith("LIB", Encoding.ASCII.GetString(psf.Sections[0], 2048, 3));
            StringAssert.StartsWith("PRIMARY", Encoding.ASCII.GetString(psf.Sections[1], 2048, 7));
            StringAssert.StartsWith("LIB2", Encoding.ASCII.GetString(psf.Sections[2], 2048, 4));
        }

        // Spec: "Region information in all _lib EXE headers should be ignored." The library was
        // ripped from a European disc; the song was not.
        [Test]
        public void RefreshRateComesFromThePrimaryNotTheLibrary()
        {
            Write("driver.psflib", Psf(Exe("Europe", Text("LIB")), null));
            var path = Write("song.minipsf", Psf(Exe("North America", Text("PRIMARY")), "_lib=driver.psflib\n"));

            Assert.AreEqual(60, PsfFile.Load(path).RefreshHz);
        }

        [Test]
        public void EuropeanPrimaryIs50Hz_AndRefreshTagOverridesRegion()
        {
            var europe = Write("pal.psf", Psf(Exe("Europe", Text("PRIMARY")), "title=pal\n"));
            Assert.AreEqual(50, PsfFile.Load(europe).RefreshHz);

            var forced = Write("forced.psf", Psf(Exe("Europe", Text("PRIMARY")), "_refresh=60\n"));
            Assert.AreEqual(60, PsfFile.Load(forced).RefreshHz);
        }

        [Test]
        public void MissingLibraryNamesTheLibrary()
        {
            var path = Write("song.minipsf", Psf(Exe("Japan", Text("PRIMARY")), "_lib=gone.psflib\n"));

            var ex = Assert.Throws<FileNotFoundException>(() => PsfFile.Load(path));
            StringAssert.Contains("gone.psflib", ex.Message);
        }

        // Spec: "a PSF file may be regarded as corrupt if [the CRC] does not match".
        [Test]
        public void WrongCrcIsCorrupt()
        {
            var path = Write("bad.psf", Psf(Exe("Japan", Text("PRIMARY")), "title=x\n", crcOverride: 0x12345678));

            Assert.Throws<InvalidDataException>(() => PsfFile.Load(path));
        }

        [Test]
        public void Psf2IsRefused()
        {
            var path = Write("ps2.psf", Psf(Exe("Japan", Text("PRIMARY")), null, version: 2));

            var ex = Assert.Throws<InvalidDataException>(() => PsfFile.Load(path));
            StringAssert.Contains("PSF1", ex.Message);
        }

        [Test]
        public void ReadTagsDoesNotNeedTheLibrary()
        {
            // No driver.psflib on disk: tags must still come back, because Now Playing and the
            // statistics page read them without ever starting the engine.
            var path = Write("song.minipsf", Psf(Exe("Japan", Text("PRIMARY")), "_lib=driver.psflib\ntitle=Opening\nlength=2:08\nfade=10\n"));

            var psf = PsfFile.ReadTags(path);

            Assert.AreEqual("Opening", psf.Title);
            Assert.AreEqual(TimeSpan.FromSeconds(138), psf.Duration);
        }

        [TestCase("1:26", 86.0)]
        [TestCase("0:35.5", 35.5)]
        [TestCase("1:02:03,250", 3723.25)]
        [TestCase("12.5", 12.5)]
        [TestCase(" 45 ", 45.0)]
        public void ParsesEveryLengthFormat(string text, double seconds)
        {
            Assert.AreEqual(TimeSpan.FromSeconds(seconds), PsfFile.ParseTime(text));
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("1:xx")]
        public void UnparseableLengthIsNull(string text)
        {
            Assert.IsNull(PsfFile.ParseTime(text));
        }

        // Spec: names are case-insensitive, characters 0x01-0x20 are whitespace, blank lines are
        // ignored, and a repeated name continues the value on a new line.
        [Test]
        public void TagBlockFollowsTheSpec()
        {
            var tags = PsfFile.ParseTagBlock("Title = First\r\n\n  comment=line one\ncomment=line two\t\n\x01length\x02=\x031:00\nnoequals\n");

            Assert.AreEqual("First", tags["title"]);
            Assert.AreEqual("First", tags["TITLE"]);
            Assert.AreEqual("line one\nline two", tags["comment"]);
            Assert.AreEqual("1:00", tags["length"]);
            Assert.AreEqual(3, tags.Count);
        }

        [Test]
        public void Utf8TagDecodesTitleAsUtf8()
        {
            var path = Write("utf.psf", Psf(Exe("Japan", Text("PRIMARY")), "utf8=1\ntitle=Café Éclair\n"));

            Assert.AreEqual("Café Éclair", PsfFile.ReadTags(path).Title);
        }

        [Test]
        public void ExtensionsAgreeAcrossTheCodebase()
        {
            Assert.IsTrue(PsfFile.IsPsfExtension(".psf"));
            Assert.IsTrue(PsfFile.IsPsfExtension(".MINIPSF"));
            Assert.IsFalse(PsfFile.IsPsfExtension(".psflib"), "a library is not a track");

            // The library must be invisible to every song list, which it is only while it stays out of this set.
            Assert.IsFalse(Constants.SupportedAudioExtensionsLowercase.Contains(".psflib"));

            CollectionAssert.AreEquivalent(Constants.SupportedAudioExtensions, Constants.SupportedAudioExtensionsLowercase,
                "the array and the HashSet in Constants are two copies of one list");
            CollectionAssert.IsSubsetOf(GmeNative.SupportedExtensions.Concat(PsfFile.SupportedExtensions), Constants.SupportedAudioExtensions,
                "every emulated format must also be a supported audio extension or it never reaches a player");
        }
    }
}
