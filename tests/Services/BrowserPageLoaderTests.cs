using NUnit.Framework;
using UniPlaySong.Downloaders;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class BrowserPageLoaderTests
    {
        // The bot check's page sends the downloader to the browser; a real page (or nothing) doesn't
        [Test]
        public void RecognisesTheBotCheckPage()
        {
            Assert.IsTrue(BrowserPageLoader.IsChallenge("<html><head><title>Just a moment...</title></head></html>"));
            Assert.IsTrue(BrowserPageLoader.IsChallenge("<script src=\"/cdn-cgi/challenge-platform/h/b/orchestrate\"></script>"));
            Assert.IsFalse(BrowserPageLoader.IsChallenge("<table><tr><td><a href=\"/game-soundtracks/album/x\">X</a></td></tr></table>"));
            Assert.IsFalse(BrowserPageLoader.IsChallenge(null));
        }
    }
}
