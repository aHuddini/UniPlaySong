using NUnit.Framework;
using UniPlaySong.Features.MusicInfoCard.Player;

namespace UniPlaySong.Tests.Features.MusicInfoCard
{
    // What the player card shows and starts: the playing row (this game's songs only), the song Play starts when nothing
    // plays, and the now-playing line naming whatever plays
    [TestFixture]
    public class CardPlaybackTests
    {
        [Test]
        public void PlayingIndex_FindsThisGamesSong_CaseInsensitive()
            => Assert.AreEqual(1, CardPlayback.PlayingIndex(new[] { @"C:\g\a.mp3", @"C:\g\b.mp3" }, @"c:\G\B.mp3"));

        [Test]
        public void PlayingIndex_OtherSource_IsMinusOne()
        {
            Assert.AreEqual(-1, CardPlayback.PlayingIndex(new[] { @"C:\g\a.mp3" }, null));
            Assert.AreEqual(-1, CardPlayback.PlayingIndex(new[] { @"C:\g\a.mp3" }, @"C:\h\x.mp3"));
        }

        [Test]
        public void StartSong_PrefersPrimary_ElseFirst_ElseNull()
        {
            Assert.AreEqual(@"C:\g\b.mp3", CardPlayback.StartSong(new[] { @"C:\g\a.mp3", @"C:\g\b.mp3" }, @"C:\g\b.mp3"));
            Assert.AreEqual(@"C:\g\a.mp3", CardPlayback.StartSong(new[] { @"C:\g\a.mp3" }, @"C:\g\gone.mp3"));
            Assert.IsNull(CardPlayback.StartSong(new string[0], null));
        }

        [Test]
        public void NowPlayingLine_NamesTheSource()
        {
            Assert.AreEqual("Area C Painting · 01:58 / 05:12",
                CardPlayback.NowPlayingLine(true, false, "Area C Painting", "", "01:58", "05:12"));
            Assert.AreEqual("Spotify · Tokyo Rain — CASPER · 01:18 / 03:45",
                CardPlayback.NowPlayingLine(true, true, "Tokyo Rain", "CASPER", "01:18", "03:45"));
            Assert.AreEqual("Nothing playing · press Play for this game's music",
                CardPlayback.NowPlayingLine(false, false, null, null, null, null));
        }
    }
}
