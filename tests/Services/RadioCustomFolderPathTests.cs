using NUnit.Framework;
using UniPlaySong;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class RadioCustomFolderPathTests
    {
        // The fallback (null -> DefaultMusicFolderPath) depends on this default being null.
        [Test]
        public void RadioCustomFolderPath_DefaultsToNull()
        {
            var s = new UniPlaySongSettings();
            Assert.IsNull(s.RadioCustomFolderPath);
        }

        [Test]
        public void RadioCustomFolderPath_SetterRoundTrips()
        {
            var s = new UniPlaySongSettings();
            s.RadioCustomFolderPath = @"D:\Music\RadioMix";
            Assert.AreEqual(@"D:\Music\RadioMix", s.RadioCustomFolderPath);
        }
    }
}
