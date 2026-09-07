using System.IO;
using NUnit.Framework;

namespace UniPlaySong.Tests.Services
{
    // SDL_mixer has ONE music stream per process, and Playnite's Fullscreen background music runs on
    // the same library UniPlaySong's SDL2 player uses. So Mix_HaltMusic is not a private operation:
    // calling it stops whatever is playing, including Playnite's own music.
    //
    // Solaris issue #137 — with "Suppress Playnite Vanilla Theme Music" turned off (the user wants
    // Playnite's music, and UniPlaySong only for jingles), Playnite's music started at Fullscreen
    // launch and cut out seconds later. The jingle device prewarm loads a warm-up file, never plays
    // it, and its cleanup ran Close() -> Stop() -> Mix_HaltMusic(). Invisible to anyone with the
    // default suppression on, because that music was not playing in the first place. Traced to 1.7.2,
    // which is the release that added the prewarm.
    [TestFixture]
    public class SdlSharedMusicStreamTests
    {
        private static string Source()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "Services", "SDL2MusicPlayer.cs");
            if (!File.Exists(path)) Assert.Ignore("SDL2MusicPlayer source not reachable from the test output directory");
            return File.ReadAllText(path);
        }

        private static string Method(string text, string signature)
        {
            var start = text.IndexOf(signature);
            Assert.Greater(start, -1, $"{signature} should exist");

            // Walk to the closing brace of the method body.
            var open = text.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0)
                    return text.Substring(start, i - start + 1);
            }
            Assert.Fail($"could not find the end of {signature}");
            return null;
        }

        [Test]
        public void StopOnlyHaltsTheStreamThisPlayerIsUsing()
        {
            // The fix. Without the guard, any Stop() — including one from a throwaway player that
            // never played a note — silences Playnite's music.
            var body = Method(Source(), "public void Stop()");

            StringAssert.Contains("Mix_HaltMusic", body, "Stop still needs to halt when it should");
            StringAssert.Contains("_isActive || _isPausedMidPlayback", body,
                "the halt must be gated on this instance actually using the music stream");

            var gate = body.IndexOf("_isActive || _isPausedMidPlayback");
            var halt = body.IndexOf("Mix_HaltMusic");
            Assert.Less(gate, halt, "the ownership check must come before the halt, not after");
        }

        [Test]
        public void ClosingAPlayerThatNeverPlayedCannotHaltAnything()
        {
            // Close() calls Stop() whenever a file was loaded, which is exactly the prewarm's
            // situation: it loads a warm-up file and disposes without playing. With Stop() gated,
            // that path can no longer reach Mix_HaltMusic.
            var text = Source();
            var close = Method(text, "public void Close()");

            StringAssert.Contains("Stop()", close,
                "Close still stops a playing song before freeing it");
            Assert.IsFalse(close.Contains("Mix_HaltMusic"),
                "Close must route through Stop's ownership gate, never halt directly");
        }

        [Test]
        public void OnlyPlayMarksThePlayerAsUsingTheStream()
        {
            // _isActive is the discriminator the gate depends on. If anything other than Play()
            // starts setting it true, a player that never played could halt Playnite again.
            var text = Source();

            // Two places may claim it: starting playback, and resuming a paused stream. Anything
            // else setting it true would let a player that never played halt Playnite's music again.
            var assignments = System.Text.RegularExpressions.Regex.Matches(text, @"_isActive\s*=\s*true");
            Assert.AreEqual(2, assignments.Count,
                "only the play and resume paths may claim the music stream — see the gate in Stop()");
        }
    }
}
