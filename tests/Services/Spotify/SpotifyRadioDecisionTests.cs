using NUnit.Framework;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    [TestFixture]
    public class SpotifyRadioDecisionTests
    {
        private static SpotifyRadioState S(bool life, bool user) =>
            new SpotifyRadioState { LifecyclePausedByUps = life, UserPausedExternally = user };

        // The two-flag radio decision: UPS pauses and resumes only what it paused itself, and never resumes a pause the
        // user made in Spotify. A null expected flag is not checked.
        [Test]
        public void Decide_TruthTable()
        {
            var cases = new[]
            {
                new { Radio = true, Lifecycle = true, Playing = true, Prev = S(false, false), Action = SpotifyRadioAction.Pause,
                      NextLife = (bool?)true, NextUser = (bool?)null, Why = "lifecycle pause engages: pause and own it" },
                // Also the engage seed: radio turned on with Spotify not playing issues one resume.
                new { Radio = true, Lifecycle = false, Playing = false, Prev = S(true, false), Action = SpotifyRadioAction.Play,
                      NextLife = (bool?)false, NextUser = (bool?)null, Why = "lifecycle clears while UPS owns the pause: resume" },
                new { Radio = true, Lifecycle = false, Playing = false, Prev = S(false, false), Action = SpotifyRadioAction.None,
                      NextLife = (bool?)null, NextUser = (bool?)true, Why = "paused but not by UPS: the user did it - must NOT resume, record it" },
                new { Radio = true, Lifecycle = false, Playing = false, Prev = S(false, true), Action = SpotifyRadioAction.None,
                      NextLife = (bool?)null, NextUser = (bool?)null, Why = "external pause stays respected on the next tick" },
                new { Radio = true, Lifecycle = false, Playing = true, Prev = S(false, true), Action = SpotifyRadioAction.None,
                      NextLife = (bool?)null, NextUser = (bool?)false, Why = "user resumed in Spotify: clear the external-pause flag" },
                new { Radio = true, Lifecycle = false, Playing = true, Prev = S(false, false), Action = SpotifyRadioAction.None,
                      NextLife = (bool?)null, NextUser = (bool?)null, Why = "already playing: no command spam" },
                new { Radio = false, Lifecycle = false, Playing = true, Prev = S(true, true), Action = SpotifyRadioAction.None,
                      NextLife = (bool?)false, NextUser = (bool?)false, Why = "radio off: no action, flags cleared" },
            };
            Assert.Multiple(() =>
            {
                foreach (var t in cases)
                {
                    var (action, next) = SpotifyRadioDecision.Decide(radioOn: t.Radio, lifecyclePaused: t.Lifecycle,
                        spotifyIsPlaying: t.Playing, prev: t.Prev);
                    Assert.That(action, Is.EqualTo(t.Action), t.Why);
                    if (t.NextLife.HasValue) Assert.That(next.LifecyclePausedByUps, Is.EqualTo(t.NextLife.Value), t.Why + " (lifecycle flag)");
                    if (t.NextUser.HasValue) Assert.That(next.UserPausedExternally, Is.EqualTo(t.NextUser.Value), t.Why + " (external flag)");
                }
            });
        }
    }
}
