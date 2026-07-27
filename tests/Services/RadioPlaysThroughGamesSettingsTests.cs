using NUnit.Framework;
using UniPlaySong;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class RadioPlaysThroughGamesSettingsTests
    {
        // Opt-in feature: must default off so existing users see no behaviour change.
        [Test]
        public void RadioPlaysThroughGames_DefaultsToFalse()
        {
            var s = new UniPlaySongSettings();
            Assert.IsFalse(s.RadioPlaysThroughGames);
        }

        [Test]
        public void RadioPlaysThroughGames_SetterRoundTrips()
        {
            var s = new UniPlaySongSettings();
            s.RadioPlaysThroughGames = true;
            Assert.IsTrue(s.RadioPlaysThroughGames);
        }
    }
}
