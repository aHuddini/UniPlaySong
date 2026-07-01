using System;
using Moq;
using NUnit.Framework;
using UniPlaySong;
using UniPlaySong.Services;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    [TestFixture]
    public class SpotifyControlServiceTests
    {
        private Mock<IMusicPlaybackService> _playback;
        private Mock<ISpotifyClient> _client;
        private UniPlaySongSettings _settings;
        private SpotifyControlService _service;

        [SetUp]
        public void SetUp()
        {
            _playback = new Mock<IMusicPlaybackService>();
            _client = new Mock<ISpotifyClient>();
            _settings = new UniPlaySongSettings();
            // Default: Spotify available, playing, controllable.
            _client.SetupGet(c => c.IsAvailable).Returns(true);
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _client.Setup(c => c.TryPause()).Returns(true);
            _client.Setup(c => c.TryResume()).Returns(true);
            _client.Setup(c => c.TrySkipNext()).Returns(true);
            _client.Setup(c => c.TrySkipPrevious()).Returns(true);
            _client.Setup(c => c.TryTogglePlayPause()).Returns(true);
            _service = new SpotifyControlService(_playback.Object, _client.Object, () => _settings, null);
        }

        [Test]
        public void RadioModeOn_SpotifyAvailable_IsActive()
        {
            _settings.SpotifyRadioMode = true;
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
            Assert.IsTrue(_settings.SpotifyActive);
        }

        [Test]
        public void SpotifyRadioOn_IsActive_WithoutRadioMode()
        {
            // v1.5.8 decoupling: SpotifyRadioMode is a standalone source; no RadioModeEnabled needed.
            _settings.RadioModeEnabled = false;
            _settings.SpotifyRadioMode = true;
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
        }

        [Test]
        public void SpotifyUnavailable_NotActive_EvenInRadioMode()
        {
            _client.SetupGet(c => c.IsAvailable).Returns(false);
            _settings.SpotifyRadioMode = true;
            _service.Recompute();
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void DefaultSource_InDefaultGap_IsActive()
        {
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
        }

        [Test]
        public void DefaultSource_NotInGap_NotActive()
        {
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false);
            _service.Recompute();
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void RadioWinsOverDefaultSource_WhenBothSet()
        {
            // Radio on, but not "in a gap": radio precedence still makes it active.
            _settings.SpotifyRadioMode = true;
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false);
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
        }

        [Test]
        public void Active_AndSpotifyPaused_StartsSpotify()
        {
            // Spotify becomes the active music but is currently paused → UPS plays it.
            _settings.SpotifyRadioMode = true;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Once);
        }

        [Test]
        public void Active_AndSpotifyAlreadyPlaying_DoesNotReissuePlay()
        {
            // v1.5.8 two-flag radio: on the engage edge, the seed (LifecyclePausedByUps=true)
            // causes Decide to return Play exactly once — even if Spotify is already playing.
            // This is a single non-blocking post, not a redundant command; subsequent recomputes
            // with IsPlaying=true return None (no further TryResume). TryPause is never issued.
            _settings.SpotifyRadioMode = true;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.Recompute();                              // engage edge → one Resume post
            _client.Verify(c => c.TryResume(), Times.Once);   // exactly one, from the engage seed
            _client.Verify(c => c.TryPause(), Times.Never);   // never paused
            _service.Recompute();                              // subsequent recompute: already playing → None
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Once);   // still only the one from engage
        }

        [Test]
        public void Active_AndUpsLifecyclePaused_PausesSpotify()
        {
            // Spotify is the active music but a UPS lifecycle pause (e.g. game launch,
            // video, lock) is in effect → Spotify should pause too.
            _settings.SpotifyRadioMode = true;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _service.Recompute();                              // take the wheel (plays)
            _client.SetupGet(c => c.IsPlaying).Returns(true);  // now playing
            _playback.SetupGet(p => p.IsPaused).Returns(true); // lifecycle pause engages
            _service.Recompute();
            _client.Verify(c => c.TryPause(), Times.Once);
        }

        [Test]
        public void GameWithMusicTakesOver_PausesSpotify()
        {
            // Spotify was the active default music (driving), then a game with its own
            // music is selected (active=false) → UPS pauses Spotify so game music plays.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                                       // active → plays Spotify
            Assert.IsTrue(_service.IsSpotifyActive);
            _client.SetupGet(c => c.IsPlaying).Returns(true);           // Spotify now playing
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false); // game music took over
            _service.Recompute();                                       // inactive → pause Spotify
            _client.Verify(c => c.TryPause(), Times.Once);
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void NeverActive_NeverTouchesSpotify()
        {
            // Spotify was never the active music (mode never engaged), so UPS never took the
            // wheel — it must issue neither play nor pause, leaving the user's Spotify alone.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(true); // user is playing their own Spotify
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false); // not in a gap
            _service.Recompute();
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Never);
            _client.Verify(c => c.TryPause(), Times.Never);
        }

        [Test]
        public void TookWheelButResumeNotYetLanded_StillPausesOnTakeover()
        {
            // RACE GUARD: gap entered → we attempt TryResume (take the wheel) but Spotify's
            // status hasn't flipped to Playing yet; then a game with music immediately takes
            // over. We MUST still issue the pause (unconditionally), or Spotify plays over the
            // game once its delayed play lands. This reproduces the Fullscreen→Desktop bug.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(false); // resume hasn't landed yet
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                                   // take the wheel (TryResume)
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false); // game music takes over
            _service.Recompute();                                   // inactive while resume in flight
            _client.Verify(c => c.TryPause(), Times.Once);          // pause issued despite IsPlaying=false
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void SkipOnGap_SkipsWhenFreshlyTakingOver_FromGameWithMusic()
        {
            // SpotifySkipOnGap on: when Spotify FRESHLY takes over (a game with its own music was
            // playing → now a no-music game), advance to a new track instead of resuming.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _settings.SpotifySkipOnGap = true;
            // Was a game with music (not a gap): not driving Spotify, not active.
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false);
            _service.Recompute();                              // inactive: nothing to do
            // Now a no-music game is selected → Spotify takes over.
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                              // fresh takeover → skip
            _client.Verify(c => c.TrySkipNext(), Times.Once);
            _client.Verify(c => c.TryResume(), Times.Never);   // skip replaces resume
        }

        [Test]
        public void SkipOnGap_DoesNotSkip_NoMusicGameToNoMusicGame_StaysActive()
        {
            // REGRESSION: switching from one no-music game to ANOTHER while Spotify stays the
            // active music (IsPlayingDefaultMusic remains true across the switch) must NOT skip
            // again — we never left the gap, so _drivingSpotify stays true and the !_drivingSpotify
            // guard suppresses the second skip.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _settings.SpotifySkipOnGap = true;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                              // no-music game A: fresh takeover, skips once
            _client.SetupGet(c => c.IsPlaying).Returns(true);  // Spotify now playing, still in the gap
            _service.Recompute();                              // no-music game B (still active): must NOT skip
            _service.Recompute();                              // periodic recompute: must NOT skip
            _client.Verify(c => c.TrySkipNext(), Times.Once);  // only game A skipped
        }

        [Test]
        public void SkipOnGap_DoesNotReskip_OnRepeatRecomputeWhileActive()
        {
            // Skip fires only on the ENTERING edge, not on subsequent recomputes (e.g. the
            // periodic safety timer) while we remain in the same gap.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _settings.SpotifySkipOnGap = true;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                              // entering: skips once
            _client.SetupGet(c => c.IsPlaying).Returns(true);  // Spotify now playing
            _service.Recompute();                              // still active, NOT entering
            _service.Recompute();                              // still active, NOT entering
            _client.Verify(c => c.TrySkipNext(), Times.Once);  // only the first (edge) skipped
        }

        [Test]
        public void SkipOnGap_FallsBackToResume_WhenSkipUnavailable()
        {
            // If skip is unavailable (end of queue, no autoplay → TrySkipNext returns false),
            // fall back to resume so the gap is never silent.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _settings.SpotifySkipOnGap = true;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _client.Setup(c => c.TrySkipNext()).Returns(false); // skip not accepted
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();
            _client.Verify(c => c.TrySkipNext(), Times.Once);
            _client.Verify(c => c.TryResume(), Times.Once);     // fallback fired
        }

        [Test]
        public void ManualPause_WhileActive_Sticks_NoAutoResume()
        {
            // REGRESSION: user pauses Spotify from the menu while it's the active music. UPS must
            // NOT auto-resume on subsequent recomputes — the pause must stick.
            // v1.5.8 two-flag radio: the engage edge issues exactly one TryResume on first Recompute
            // (via LifecyclePausedByUps=true seed). After the user's manual pause, Spotify is
            // paused externally → Decide returns None (UserPausedExternally=true) on all subsequent
            // calls — no auto-resume is ever issued after the manual pause.
            _settings.SpotifyRadioMode = true;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.Recompute();                              // engage edge → one Resume post
            _client.Verify(c => c.TryResume(), Times.Once);   // engage-edge resume only
            // User toggles Play/Pause via menu: was playing → pauses.
            _service.ToggleManualPlayPause();
            _client.Verify(c => c.TryTogglePlayPause(), Times.Once);
            _client.SetupGet(c => c.IsPlaying).Returns(false); // Spotify now paused externally
            _service.Recompute();                              // UserPausedExternally=true → None
            _service.Recompute();                              // periodic recompute: still None
            _client.Verify(c => c.TryResume(), Times.Once);   // still only the engage-edge call, never auto-resumed
        }

        [Test]
        public void ManualPause_ThenToggleAgain_Resumes()
        {
            // User pauses (hold set), then toggles again → resumes, hold cleared.
            _settings.SpotifyRadioMode = true;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.Recompute();
            _service.ToggleManualPlayPause();                  // pause: was playing → hold
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _service.ToggleManualPlayPause();                  // toggle again: was paused → resume, clear hold
            _client.Verify(c => c.TryTogglePlayPause(), Times.Exactly(2));
            // Hold cleared: a recompute while wanting-playing now auto-resumes again.
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.AtLeastOnce);
        }

        [Test]
        public void ManualPauseHold_ClearedWhenSpotifyLeavesActiveMusic()
        {
            // Hold must clear when Spotify stops being the active music (game with music takes
            // over), so the NEXT time it becomes active it plays rather than inheriting the hold.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                              // gap: active, driving
            _service.ToggleManualPlayPause();                  // user pauses → hold
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            // Game with music takes over → Spotify no longer active → hold should clear.
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false);
            _service.Recompute();                              // !active: clears _drivingSpotify + hold
            // Back to a no-music game → Spotify active again → should PLAY (hold was cleared).
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.AtLeastOnce); // plays, not held
        }

        [Test]
        public void SkipOnGap_Off_ResumesAsBefore()
        {
            // With the toggle off, entering the gap resumes (the existing behavior) — no skip.
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _settings.SpotifySkipOnGap = false;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();
            _client.Verify(c => c.TrySkipNext(), Times.Never);
            _client.Verify(c => c.TryResume(), Times.Once);
        }
    }

    // v1.5.8 — Radio Mode and Spotify Radio Mode are mutually exclusive alternative
    // continuous-music sources. Enabling one disables the other, enforced in the setters.
    [TestFixture]
    public class SpotifyRadioModeMutualExclusionTests
    {
        [Test]
        public void EnablingSpotifyRadio_DisablesRadioMode()
        {
            var s = new UniPlaySongSettings { RadioModeEnabled = true };
            s.SpotifyRadioMode = true;
            Assert.IsTrue(s.SpotifyRadioMode);
            Assert.IsFalse(s.RadioModeEnabled, "Radio Mode should turn off when Spotify Radio Mode is enabled");
        }

        [Test]
        public void EnablingRadioMode_DisablesSpotifyRadio()
        {
            var s = new UniPlaySongSettings { SpotifyRadioMode = true };
            s.RadioModeEnabled = true;
            Assert.IsTrue(s.RadioModeEnabled);
            Assert.IsFalse(s.SpotifyRadioMode, "Spotify Radio Mode should turn off when Radio Mode is enabled");
        }

        [Test]
        public void DisablingOne_DoesNotEnableOrRecurseTheOther()
        {
            var s = new UniPlaySongSettings { SpotifyRadioMode = true };
            s.SpotifyRadioMode = false;
            Assert.IsFalse(s.SpotifyRadioMode);
            Assert.IsFalse(s.RadioModeEnabled);
        }

        [Test]
        public void TogglingBackAndForth_KeepsExactlyOneOn()
        {
            var s = new UniPlaySongSettings();
            s.RadioModeEnabled = true;
            Assert.IsTrue(s.RadioModeEnabled); Assert.IsFalse(s.SpotifyRadioMode);
            s.SpotifyRadioMode = true;
            Assert.IsFalse(s.RadioModeEnabled); Assert.IsTrue(s.SpotifyRadioMode);
            s.RadioModeEnabled = true;
            Assert.IsTrue(s.RadioModeEnabled); Assert.IsFalse(s.SpotifyRadioMode);
        }
    }
}
