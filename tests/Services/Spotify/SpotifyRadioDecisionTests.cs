using NUnit.Framework;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    [TestFixture]
    public class SpotifyRadioDecisionTests
    {
        private static SpotifyRadioState S(bool life, bool user) =>
            new SpotifyRadioState { LifecyclePausedByUps = life, UserPausedExternally = user };

        [Test]
        public void LifecyclePause_Engages_IssuesPause_AndOwnsIt()
        {
            var (action, next) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: true, spotifyIsPlaying: true, prev: S(false, false));
            Assert.AreEqual(SpotifyRadioAction.Pause, action);
            Assert.IsTrue(next.LifecyclePausedByUps);
        }

        [Test]
        public void LifecycleClears_WhenUpsOwnsPause_IssuesResume()
        {
            var (action, next) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: false, spotifyIsPlaying: false, prev: S(true, false));
            Assert.AreEqual(SpotifyRadioAction.Play, action);
            Assert.IsFalse(next.LifecyclePausedByUps);
        }

        [Test]
        public void ExternalPause_NotLifecycle_Respected_NoResume()
        {
            // radio on, not lifecycle-paused, Spotify is paused but UPS didn't pause it → user did.
            var (action, next) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: false, spotifyIsPlaying: false, prev: S(false, false));
            Assert.AreEqual(SpotifyRadioAction.None, action, "must NOT resume a user's external pause");
            Assert.IsTrue(next.UserPausedExternally, "records the external pause");
        }

        [Test]
        public void ExternalPause_ThenNextTick_StillNoResume()
        {
            var (action, _) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: false, spotifyIsPlaying: false, prev: S(false, true));
            Assert.AreEqual(SpotifyRadioAction.None, action, "external pause stays respected");
        }

        [Test]
        public void ExternalResume_ClearsFlag()
        {
            // user resumed in Spotify → spotifyIsPlaying true → clear the external-pause flag.
            var (action, next) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: false, spotifyIsPlaying: true, prev: S(false, true));
            Assert.AreEqual(SpotifyRadioAction.None, action);
            Assert.IsFalse(next.UserPausedExternally);
        }

        [Test]
        public void RadioEngage_NotPlaying_NotPaused_IssuesResumeOnce()
        {
            var (action, _) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: false, spotifyIsPlaying: false, prev: S(true, false));
            Assert.AreEqual(SpotifyRadioAction.Play, action);
        }

        [Test]
        public void AlreadyPlaying_NotLifecyclePaused_NoAction()
        {
            var (action, _) = SpotifyRadioDecision.Decide(radioOn: true, lifecyclePaused: false, spotifyIsPlaying: true, prev: S(false, false));
            Assert.AreEqual(SpotifyRadioAction.None, action, "no command spam when already in the desired state");
        }

        [Test]
        public void RadioOff_NoAction_FlagsCleared()
        {
            var (action, next) = SpotifyRadioDecision.Decide(radioOn: false, lifecyclePaused: false, spotifyIsPlaying: true, prev: S(true, true));
            Assert.AreEqual(SpotifyRadioAction.None, action);
            Assert.IsFalse(next.LifecyclePausedByUps);
            Assert.IsFalse(next.UserPausedExternally);
        }
    }
}
