using System;
using System.Threading;
using Moq;
using NUnit.Framework;
using UniPlaySong.Models;
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

        // A just-launched Spotify ignores the engage's Play while it loads, and a plain recompute then reads "not playing"
        // as the user's pause and stays hands-off. ReassertRadioStart (the launcher, until Spotify plays) sends Play
        // again; once Spotify plays it does nothing, so a later pause is left alone.
        [Test]
        public void ReassertRadioStart_SendsPlayAgainUntilSpotifyPlays()
        {
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;

            _service.Recompute();                       // engage: the one Play, ignored by the loading app
            _service.Recompute();                       // still silent: treated as the user's pause
            _client.Verify(c => c.TryResume(), Times.Once);

            _service.ReassertRadioStart();
            _client.Verify(c => c.TryResume(), Times.Exactly(2));

            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.ReassertRadioStart();
            _client.Verify(c => c.TryResume(), Times.Exactly(2));
        }

        // "Where music plays" covers Spotify radio too: in a mode where neither game nor default music is allowed, UPS
        // pauses Spotify (its own pause), and moving back to an allowed mode resumes it.
        [Test]
        public void SpotifyRadio_PausesWhereNoMusicIsAllowed_ResumesWhereItIs()
        {
            bool fullscreen = true;
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _settings.MusicState = AudioState.Desktop;
            _settings.DefaultMusicState = AudioState.Desktop;
            var service = new SpotifyControlService(_playback.Object, _client.Object, () => _settings, null, isFullscreen: () => fullscreen);

            service.Recompute();
            _client.Verify(c => c.TryPause(), Times.Once);
            _client.Verify(c => c.TryResume(), Times.Never);

            _client.SetupGet(c => c.IsPlaying).Returns(false);
            fullscreen = false;
            service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Once);
        }

        // Spotify is the active music when Spotify radio is on (SpotifyRadioMode is derived: radio on + Spotify source),
        // or when it is the default source and the engine is in a default-music gap. Radio wins without a gap; an
        // unavailable Spotify is never active. Settings.SpotifyActive mirrors the result for themes.
        [Test]
        public void IsSpotifyActive_TruthTable()
        {
            var cases = new[]
            {
                new { Radio = true, DefaultSpotify = false, InGap = false, Available = true, Expected = true, Why = "Spotify radio on" },
                new { Radio = true, DefaultSpotify = false, InGap = false, Available = false, Expected = false, Why = "unavailable, even in radio mode" },
                new { Radio = false, DefaultSpotify = true, InGap = true, Available = true, Expected = true, Why = "default source, in a gap" },
                new { Radio = false, DefaultSpotify = true, InGap = false, Available = true, Expected = false, Why = "default source, not in a gap" },
                new { Radio = true, DefaultSpotify = true, InGap = false, Available = true, Expected = true, Why = "radio wins over the default source" },
            };
            Assert.Multiple(() =>
            {
                foreach (var t in cases)
                {
                    var client = new Mock<ISpotifyClient>();
                    client.SetupGet(c => c.IsAvailable).Returns(t.Available);
                    client.SetupGet(c => c.IsPlaying).Returns(true);
                    client.Setup(c => c.TryResume()).Returns(true);
                    var playback = new Mock<IMusicPlaybackService>();
                    playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(t.InGap);
                    var settings = new UniPlaySongSettings();
                    if (t.Radio)
                    {
                        settings.RadioModeEnabled = true;
                        settings.RadioMusicSource = RadioMusicSource.Spotify;
                    }
                    if (t.DefaultSpotify)
                        settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;

                    using (var svc = new SpotifyControlService(playback.Object, client.Object, () => settings, null))
                    {
                        svc.Recompute();
                        Assert.That(svc.IsSpotifyActive, Is.EqualTo(t.Expected), t.Why);
                        Assert.That(settings.SpotifyActive, Is.EqualTo(t.Expected), t.Why + " (settings mirror)");
                    }
                }
            });
        }

        // Engage = "I want music now": turning Radio Mode on (or entering a mode / launching with it on) seeds
        // LifecyclePausedByUps so Decide issues exactly one Resume, even from a paused Spotify - that is what makes
        // Fullscreen entry auto-start. When Spotify already plays the Resume is a harmless no-op; afterwards the flags
        // clear, so further recomputes issue nothing (no command spam).
        [TestCase(false, TestName = "Engage_SpotifyPaused_StartsRadioWithOneResume")]
        [TestCase(true, TestName = "Engage_SpotifyAlreadyPlaying_ResumesOnceThenNoSpam")]
        public void Engage_ResumesExactlyOnce(bool alreadyPlaying)
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(alreadyPlaying);
            _service.Recompute();                              // engage: seed → Resume once
            _client.Verify(c => c.TryResume(), Times.Once,
                "enabling Radio Mode / entering with it on must start the radio (resume once)");
            _client.Verify(c => c.TryPause(), Times.Never);
            _service.Recompute();
            _service.Recompute();
            _client.Verify(c => c.TryResume(), Times.Once);   // count stays at 1 (no spam)
        }

        [Test]
        public void Active_AndUpsLifecyclePaused_PausesSpotify()
        {
            // Spotify is the active music but a UPS lifecycle pause (e.g. game launch,
            // video, lock) is in effect → Spotify should pause too.
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _service.Recompute();                              // take the wheel (plays)
            _client.SetupGet(c => c.IsPlaying).Returns(true);  // now playing
            _playback.SetupGet(p => p.IsPaused).Returns(true); // lifecycle pause engages
            _service.Recompute();
            _client.Verify(c => c.TryPause(), Times.Once);
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

        // Spotify was the active default music (driving), then a game with its own music is selected → UPS pauses
        // Spotify so game music plays. RACE GUARD (resumeLanded=false): the take-the-wheel TryResume has not flipped
        // Spotify to Playing yet when the game takes over; the pause must still be issued, or Spotify plays over the
        // game once its delayed play lands (the Fullscreen→Desktop bug).
        [TestCase(true, TestName = "GameWithMusicTakesOver_PausesSpotify")]
        [TestCase(false, TestName = "TookWheelButResumeNotYetLanded_StillPausesOnTakeover")]
        public void GameWithMusicTakesOver_PausesSpotify(bool resumeLanded)
        {
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(true);
            _service.Recompute();                                       // active → take the wheel (TryResume)
            Assert.IsTrue(_service.IsSpotifyActive);
            if (resumeLanded)
                _client.SetupGet(c => c.IsPlaying).Returns(true);       // Spotify now playing
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(false); // game music took over
            _service.Recompute();                                       // inactive → pause Spotify
            _client.Verify(c => c.TryPause(), Times.Once);
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
            // guard suppresses the second skip. Skip fires only on the ENTERING edge, never on the
            // periodic safety-timer recomputes within the same gap.
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
            // v1.5.8 two-flag radio: engage issues one Resume to start the radio (already playing →
            // harmless no-op). After the user's manual pause, Spotify is paused externally → Decide
            // returns None (UserPausedExternally=true). The resume count stays at 1 (the engage one) —
            // NO auto-resume follows the manual pause.
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.Recompute();                              // engage: seed → Resume once
            _client.Verify(c => c.TryResume(), Times.Once);   // the engage resume
            // User toggles Play/Pause via menu: was playing → pauses.
            _service.ToggleManualPlayPause();
            _client.Verify(c => c.TryTogglePlayPause(), Times.Once);
            _client.SetupGet(c => c.IsPlaying).Returns(false); // Spotify now paused externally
            _service.Recompute();                              // UserPausedExternally=true → None
            _service.Recompute();                              // periodic recompute: still None
            _client.Verify(c => c.TryResume(), Times.Once);   // still 1 — never auto-resumed after the pause
        }

        [Test]
        public void ManualPause_ThenToggleAgain_Resumes()
        {
            // User pauses (via toggle), then toggles again → resumes via TryTogglePlayPause.
            // In radio mode the user's own transport toggle is the pause/resume channel; UPS itself
            // only ever issues the single engage Resume. So after engage (one TryResume) plus two
            // manual toggles, TryResume stays at 1 and TryTogglePlayPause is exactly 2.
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.Recompute();                              // engage: seed → Resume once
            _service.ToggleManualPlayPause();                  // pause via toggle
            _client.SetupGet(c => c.IsPlaying).Returns(false);
            _service.ToggleManualPlayPause();                  // toggle again: resume (TryTogglePlayPause)
            _client.Verify(c => c.TryTogglePlayPause(), Times.Exactly(2)); // both toggles sent
            _client.SetupGet(c => c.IsPlaying).Returns(true);  // Spotify is now playing after the toggle
            _service.Recompute();                              // already playing → None; no extra resume
            _client.Verify(c => c.TryResume(), Times.Once);    // only the engage resume; toggles used TryTogglePlayPause
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

        // RequestNowPlaying passthrough tests -----------------------------------------------

        [Test]
        public void RequestNowPlaying_WhenActive_ForwardsToClient()
        {
            // Arrange: Spotify is the active music (radio mode on, available).
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _service.Recompute();
            Assert.IsTrue(_service.IsSpotifyActive);
            var expected = new SpotifyNowPlaying("Song", "Artist", null, null, TimeSpan.FromSeconds(200));
            SpotifyNowPlaying received = default;
            _client.Setup(c => c.RequestNowPlaying(It.IsAny<Action<SpotifyNowPlaying>>()))
                   .Callback<Action<SpotifyNowPlaying>>(cb => cb(expected));

            // Act
            _service.RequestNowPlaying(np => received = np);

            // Assert: forwarded to client, result passed through.
            _client.Verify(c => c.RequestNowPlaying(It.IsAny<Action<SpotifyNowPlaying>>()), Times.Once);
            Assert.AreEqual(expected.Title, received.Title);
        }

        [Test]
        public void RequestNowPlaying_WhenInactive_ReturnsEmptySynchronously()
        {
            // Arrange: Spotify is NOT the active music (RadioModeEnabled defaults to false → SpotifyRadioMode false).
            _service.Recompute();
            Assert.IsFalse(_service.IsSpotifyActive);
            SpotifyNowPlaying received = default;

            // Act
            _service.RequestNowPlaying(np => received = np);

            // Assert: callback fired with Empty; client NOT touched.
            _client.Verify(c => c.RequestNowPlaying(It.IsAny<Action<SpotifyNowPlaying>>()), Times.Never);
            Assert.IsTrue(received.IsEmpty);
        }

        [Test]
        public void RequestNowPlaying_NullCallback_DoesNotThrow()
        {
            // Should silently return without calling the client.
            Assert.DoesNotThrow(() => _service.RequestNowPlaying(null));
            _client.Verify(c => c.RequestNowPlaying(It.IsAny<Action<SpotifyNowPlaying>>()), Times.Never);
        }

        // Spotify radio was active + playing; the user turns Radio Mode off, or keeps it on but switches the source to a
        // pool → Spotify must pause rather than keep playing alongside game music / pool radio.
        [TestCase(false, TestName = "RadioModeOff_WhileSpotifyWasActive_PausesSpotify")]
        [TestCase(true, TestName = "RadioSource_SpotifyToPool_PausesSpotify")]
        public void LeavingSpotifyRadio_PausesSpotify(bool switchToPool)
        {
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(true);
            _service.Recompute();                              // engage: active
            Assert.IsTrue(_service.IsSpotifyActive);
            if (switchToPool)
                _settings.RadioMusicSource = RadioMusicSource.FullLibrary;
            else
                _settings.RadioModeEnabled = false;
            _service.Recompute();                              // disengage
            _client.Verify(c => c.TryPause(), Times.Once);     // Spotify paused on disengage
            Assert.IsFalse(_service.IsSpotifyActive);
        }

        [Test]
        public void EngageWaitsForAppReady_NoResumeUntilStarted()
        {
            // Fullscreen-switch churn fix: the engage Resume must not fire before the app is
            // visible (OnApplicationStarted) — it previously played for ~1s during startup and
            // was then paused by the theme-overlay pause source (pause -> play -> pause churn).
            bool ready = false;
            var svc = new SpotifyControlService(_playback.Object, _client.Object, () => _settings, null, () => ready);
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
            _client.SetupGet(c => c.IsPlaying).Returns(false);

            svc.Recompute();                                   // engage attempt before app started
            _client.Verify(c => c.TryResume(), Times.Never);   // held back

            ready = true;
            svc.Recompute();                                   // app now started
            _client.Verify(c => c.TryResume(), Times.Once);    // radio starts exactly once
            svc.Dispose();
        }

        [Test]
        public void NowPlayingChanged_RaisedOutsideRecomputeLock_NoDeadlock()
        {
            // Regression: the radio path raised NowPlayingChanged INSIDE _recomputeLock. A subscriber
            // doing a sync Dispatcher.Invoke then blocks on a UI thread that is itself entering
            // Recompute (playback event / timer) -> both wait on each other -> Playnite frozen.
            // This test simulates that: the handler blocks until a SECOND thread finishes Recompute.
            // If the raise happens while the lock is held, the second Recompute can't finish -> timeout.
            _settings.RadioModeEnabled = true;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;

            var handlerEntered = new ManualResetEventSlim(false);
            var otherRecomputeDone = new ManualResetEventSlim(false);
            bool deadlocked = false;

            _service.NowPlayingChanged += () =>
            {
                if (handlerEntered.IsSet) return;   // only the first raise participates
                handlerEntered.Set();
                // Mirrors a sync Dispatcher.Invoke waiting on a thread that calls Recompute.
                if (!otherRecomputeDone.Wait(TimeSpan.FromSeconds(2)))
                    deadlocked = true;
            };

            var other = new Thread(() =>
            {
                handlerEntered.Wait(TimeSpan.FromSeconds(2));
                _service.Recompute();               // must NOT block on _recomputeLock
                otherRecomputeDone.Set();
            });
            other.Start();

            _service.Recompute();                    // engage -> raises NowPlayingChanged
            other.Join(TimeSpan.FromSeconds(5));

            Assert.IsFalse(deadlocked, "Recompute on another thread blocked while a NowPlayingChanged handler ran — the event is raised inside _recomputeLock.");
            Assert.IsTrue(otherRecomputeDone.IsSet);
        }

        // ---- Manual pause hold across mode changes ----
        // A pause the user makes (Top Panel / menu) holds Spotify while that mode lasts. Leaving the mode - Spotify radio
        // turned off, or turned on from a default-music gap - is a fresh start, so the next mode must not inherit it.
        // A pause an integration makes over a URI (themes, FullReel) is that integration's to end, so it does carry over.

        private void SpotifyRadio(bool on)
        {
            _settings.RadioModeEnabled = on;
            _settings.RadioMusicSource = RadioMusicSource.Spotify;
        }

        private void InSpotifyGap(bool gap)
        {
            _settings.DefaultMusicSourceOption = DefaultMusicSource.Spotify;
            _playback.SetupGet(p => p.IsPlayingDefaultMusic).Returns(gap);
        }

        [TestCase(false, TestName = "ManualHold_RadioPausedByToggle_ThenRadioOff_GapPlays")]
        [TestCase(true, TestName = "ManualHold_RadioPausedByUri_ThenRadioOff_StaysPaused")]
        public void ManualHold_AcrossRadioOff(bool viaUri)
        {
            InSpotifyGap(true);
            SpotifyRadio(true);
            _service.Recompute();                                   // radio engaged, Spotify playing
            if (viaUri) _service.ManualPause(); else _service.ToggleManualPlayPause();
            _client.SetupGet(c => c.IsPlaying).Returns(false);

            SpotifyRadio(false);                                    // radio off; a no-music game is selected
            _client.Invocations.Clear();
            _service.Recompute();

            if (viaUri)
                _client.Verify(c => c.TryResume(), Times.Never, "an integration's pause holds until it resumes");
            else
                _client.Verify(c => c.TryResume(), Times.Once, "the default-music gap must start Spotify again");
        }

        [TestCase(false, TestName = "ManualHold_GapPausedByToggle_ThenRadioOn_RadioStarts")]
        [TestCase(true, TestName = "ManualHold_GapPausedByUri_ThenRadioOn_StaysPaused")]
        public void ManualHold_AcrossRadioOn(bool viaUri)
        {
            InSpotifyGap(true);
            _service.Recompute();                                   // gap: Spotify is the active music
            if (viaUri) _service.ManualPause(); else _service.ToggleManualPlayPause();
            _client.SetupGet(c => c.IsPlaying).Returns(false);

            SpotifyRadio(true);                                     // turning radio on means "music now"
            _client.Invocations.Clear();
            _service.Recompute();

            if (!viaUri)
            {
                _client.Verify(c => c.TryResume(), Times.Once, "radio must start on engage");
                return;
            }
            _client.Verify(c => c.TryResume(), Times.Never, "an integration's pause holds until it resumes");
            _service.ManualResume();                                // the integration ends its pause
            _client.Verify(c => c.TryResume(), Times.Once);
        }

        [Test]
        public void ManualHold_StillHoldsWhileGapContinues()
        {
            InSpotifyGap(true);
            _service.Recompute();
            _service.ToggleManualPlayPause();
            _client.SetupGet(c => c.IsPlaying).Returns(false);

            _client.Invocations.Clear();
            _service.Recompute();

            _client.Verify(c => c.TryResume(), Times.Never, "the user's pause must stick within the same gap");
        }

        // Playnite saves SpotifyActive despite [JsonIgnore], so a session that ended with Spotify active starts the
        // next one with it true. The first recompute must correct it, or themes, the visualizer palette and the
        // external-audio exclusion all act on a Spotify that isn't there.
        [Test]
        public void SpotifyActive_SavedTrue_CorrectedOnFirstRecompute()
        {
            _settings.SpotifyActive = true;
            _client.SetupGet(c => c.IsAvailable).Returns(false);

            _service.Recompute();

            Assert.IsFalse(_settings.SpotifyActive);
        }
    }
}
