using System.Collections.Generic;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class NormalizationScanTests
    {
        // A song counts as normalized when its name carries the suffix (case ignored); games with nothing left out,
        // sorted by name; the report lists each game and its songs and counts what didn't fit
        [Test]
        public void ListsSongsWithoutTheSuffixByGame()
        {
            var library = new List<(string, IEnumerable<string>)>
            {
                ("Zelda", new[] { @"C:\m\z\Title.mp3", @"C:\m\z\Field-NORMALIZED.mp3" }),
                ("Doom", new[] { @"C:\m\d\E1M1.ogg", @"C:\m\d\E1M2.mp3" }),
                ("Done", new[] { @"C:\m\x\Song-normalized.flac" }),
            };

            var pending = NormalizationScan.Pending(library, "-normalized");

            Assert.AreEqual(new[] { "Doom", "Zelda" }, pending.ConvertAll(g => g.Game).ToArray());
            Assert.AreEqual(new[] { @"C:\m\z\Title.mp3" }, pending[1].Songs.ToArray());

            Assert.AreEqual("Doom\r\n    E1M1.ogg\r\n    E1M2.mp3\r\nZelda\r\n    Title.mp3", NormalizationScan.Report(pending, 10));
            StringAssert.EndsWith("…and 2 more songs in 1 more game", NormalizationScan.Report(pending, 2));
        }
    }
}
