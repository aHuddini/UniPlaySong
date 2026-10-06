using System.Threading;
using NUnit.Framework;
using UniPlaySong.Downloaders;

namespace UniPlaySong.Tests.Downloaders
{
    // The dialogs used to say "see %AppData%\Playnite\extensions.log" whatever went wrong - a path that does not
    // exist on a portable install - and the cause in issue #99, a browser whose cookies yt-dlp could not read, was
    // not recognised at all. The error lines below are yt-dlp's own (yt_dlp/cookies.py, 2026.03.17).
    [TestFixture]
    public class DownloadFailureTests
    {
        [TestCase("ERROR: Could not copy Chrome cookie database. See  https://github.com/yt-dlp/yt-dlp/issues/7271  for more info")]
        [TestCase("ERROR: Failed to decrypt with DPAPI. See  https://github.com/yt-dlp/yt-dlp/issues/10927  for more info")]
        [TestCase("ERROR: could not find edge cookies database in \"C:\\Users\\x\\AppData\\Local\\Microsoft\\Edge\\User Data\"")]
        [TestCase("ERROR: failed to load cookies")]
        public void BrowserCookieFailures_AreCookies(string stderr)
        {
            Assert.AreEqual(DownloadFailure.Kind.Cookies, DownloadFailure.Classify(stderr));
        }

        // A cookie failure ends the run before YouTube is asked anything, but whatever follows it in the output must
        // not take over the diagnosis.
        [Test]
        public void Cookies_WinOverWhatFollowsInTheSameOutput()
        {
            var stderr = "ERROR: Could not copy Chrome cookie database.\nERROR: unable to download video data: HTTP Error 403";
            Assert.AreEqual(DownloadFailure.Kind.Cookies, DownloadFailure.Classify(stderr));
        }

        [Test]
        public void BotCheck_IsRecognised()
        {
            Assert.AreEqual(DownloadFailure.Kind.BotCheck,
                DownloadFailure.Classify("ERROR: [youtube] abc: Sign in to confirm you're not a bot. Use --cookies-from-browser"));
        }

        // The old check was Contains("bot"), which "both" matches.
        [Test]
        public void TheWordBoth_IsNotABotCheck()
        {
            Assert.AreNotEqual(DownloadFailure.Kind.BotCheck,
                DownloadFailure.Classify("WARNING: both formats were rejected"));
        }

        [TestCase("ERROR: [youtube] abc: Private video. Sign in if you've been granted access", DownloadFailure.Kind.Unavailable)]
        [TestCase("ERROR: [youtube] abc: Video unavailable", DownloadFailure.Kind.Unavailable)]
        [TestCase("ERROR: unable to download video data: HTTP Error 403: Forbidden", DownloadFailure.Kind.Network)]
        [TestCase("ERROR: Postprocessing: audio conversion failed", DownloadFailure.Kind.FFmpeg)]
        [TestCase("[PYI-1234:ERROR] Failed to load Python DLL", DownloadFailure.Kind.BrokenYtDlp)]
        [TestCase("ERROR: unable to open for writing: [Errno 28] No space left on device", DownloadFailure.Kind.DiskSpace)]
        [TestCase("something nobody has seen before", DownloadFailure.Kind.Unknown)]
        [TestCase("", DownloadFailure.Kind.Unknown)]
        [TestCase(null, DownloadFailure.Kind.Unknown)]
        public void Classify(string stderr, DownloadFailure.Kind expected)
        {
            Assert.AreEqual(expected, DownloadFailure.Classify(stderr));
        }

        // Every known kind has words for the user, and the cookie advice names the setting that fixed #99.
        [Test]
        public void EveryKnownKind_HasAMessage()
        {
            foreach (DownloadFailure.Kind kind in System.Enum.GetValues(typeof(DownloadFailure.Kind)))
            {
                if (kind == DownloadFailure.Kind.Unknown) Assert.IsNull(DownloadFailure.Describe(kind));
                else Assert.IsNotEmpty(DownloadFailure.Describe(kind), kind.ToString());
            }
            StringAssert.Contains("No cookies", DownloadFailure.Describe(DownloadFailure.Kind.Cookies));
        }

        // The log is wherever Playnite keeps its configuration - next to Playnite.exe on a portable install.
        [Test]
        public void ForUser_PointsAtTheLogInPlaynitesOwnFolder()
        {
            var text = DownloadFailure.ForUser("Failed to download preview.", "Because.", @"D:\Games\Playnite");

            StringAssert.StartsWith("Failed to download preview.", text);
            StringAssert.Contains("Because.", text);
            StringAssert.Contains(@"D:\Games\Playnite\extensions.log", text);
            StringAssert.DoesNotContain("%AppData%", text);
        }

        [Test]
        public void ForUser_WithoutAReason_StillGivesTheLog()
        {
            var text = DownloadFailure.ForUser("Failed.", null, @"C:\P");
            Assert.AreEqual("Failed.\n\nFull details: C:\\P\\extensions.log", text);
        }

        // Batch downloads run on several threads at once; one thread's failure must not explain another's.
        [Test]
        public void Reason_BelongsToTheThreadThatFailed()
        {
            DownloadFailure.Reason = "this thread";
            string seenElsewhere = "unset";
            var other = new Thread(() => seenElsewhere = DownloadFailure.Reason);
            other.Start();
            other.Join();

            Assert.IsNull(seenElsewhere);
            Assert.AreEqual("this thread", DownloadFailure.Reason);
            DownloadFailure.Reason = null;
        }
    }
}
