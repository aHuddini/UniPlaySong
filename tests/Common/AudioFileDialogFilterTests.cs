using System.Linq;
using NUnit.Framework;
using UniPlaySong.Common;

namespace UniPlaySong.Tests.Common
{
    // The song pickers (Set Primary Song, Add Music File) once hard-coded mp3/wav/ogg/flac, so a
    // .vgm that played fine could not be chosen as the primary song. The filter is now built from
    // the playable-extension list; this pins that every playable format is pickable.
    [TestFixture]
    public class AudioFileDialogFilterTests
    {
        [Test]
        public void EveryPlayableFormatIsPickable()
        {
            var parts = Constants.AudioFileDialogFilter.Split('|');
            Assert.AreEqual(2, parts.Length, "an OpenFileDialog filter is description|patterns");

            var patterns = parts[1].Split(';');
            CollectionAssert.AreEquivalent(
                Constants.SupportedAudioExtensions.Select(e => "*" + e),
                patterns);
            CollectionAssert.Contains(patterns, "*.vgm");
            CollectionAssert.Contains(patterns, "*.psf");
        }
    }
}
