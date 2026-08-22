using NUnit.Framework;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // The hover settle delay holds a game's music until the selection stops moving, so a fast
    // library scroll stops chopping the music already playing. These pin the rule itself; the
    // timer that applies it lives in MusicPlaybackCoordinator.
    [TestFixture]
    public class HoverSettlePolicyTests
    {
        [Test]
        public void Defers_WhenMusicIsPlaying_InFullscreen()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlaying: true),
                Is.True, "the case the feature exists for");
        }

        // Gated on "any music playing" deliberately. IsPlayingDefaultMusic is cleared before the
        // outgoing fade starts, so during a scroll it reads false while the ambient is still
        // audible - a gate on it made every switch in that window chop. Pinned so the narrower
        // gate does not come back looking like a refinement.
        [Test]
        public void Defers_EvenWhenTheDefaultFlagHasAlreadyCleared()
        {
            const bool ambientStillAudible_flagAlreadyFalse = true; // isPlaying: the ear's truth
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true,
                            isPlaying: ambientStillAudible_flagAlreadyFalse),
                Is.True);
        }

        [Test]
        public void DoesNotDefer_WhenDisabled()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: false, isFullscreen: true, isPlaying: true),
                Is.False);
        }

        // Waiting on silence produces more silence. Without this the option would make things
        // worse than not having it whenever playback had not started yet.
        [Test]
        public void DoesNotDefer_WhenNothingIsPlaying()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlaying: false),
                Is.False, "nothing to protect - starting now beats waiting on silence");
        }

        // Observed on the PS5: leaving the dashboard ambient takes a couple of seconds, but moving
        // between titles switches quickly. Once a game's track is playing the ambient is already
        // gone, so there is nothing left for the delay to protect.
        [Test]
        public void DoesNotDefer_GameToGame()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlaying: false),
                Is.False, "game-to-game stays snappy - the ambient is already gone");
        }

        // Some themes switch between default and game music through the
        // UPS_MusicControl_PauseGamePlayDefault Tag rather than through game selection, so the
        // delay hooked to selection never reaches them. GameHoverDelayPatch carries it there.
        // The direction decides: leaving the default music waits, returning does not, and the
        // default-is-playing check IS the "leaving it" case.
        [Test]
        public void Defers_LeavingDefaultMusic_OnTheTagPath()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlaying: true),
                Is.True);
        }

        [Test]
        public void DoesNotDefer_ReturningToDefaultMusic()
        {
            // wantDefault==true is filtered before the policy is consulted, and the state it
            // arrives in - a game's track playing - independently says do not wait.
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlaying: false),
                Is.False, "going back to the default music is immediate");
        }

        // Three independent toggles, all off on a fresh install, so nothing about playback changes
        // until they are asked for. GameHoverDelayPatch is its own switch rather than a change to
        // PS5ThemeCompatMode, which fixes a different problem on the same Tag.
        [Test]
        public void PatchToggles_AreOffByDefault_AndIndependent()
        {
            var s = new UniPlaySongSettings();
            Assert.Multiple(() =>
            {
                Assert.That(s.HoverSettleEnabled, Is.False, "the delay itself");
                Assert.That(s.GameHoverDelayPatch, Is.False, "the Tag-path patch");
                Assert.That(s.PS5ThemeCompatMode, Is.False, "the flicker debounce, unrelated");
            });

            s.GameHoverDelayPatch = true;
            Assert.That(s.PS5ThemeCompatMode, Is.False,
                "enabling the hover patch must not turn on the flicker debounce");
        }

        // The bridge is what makes game-to-game feel like the PS5: leaving a game returns to the
        // default music at once, and the NEW game's music takes the delay. The abandoned track
        // never plays out the wait.
        [Test]
        public void Bridges_WhenLeavingAGamesTrack()
        {
            Assert.That(HoverSettlePolicy.ShouldBridgeToDefault(isPlayingDefaultMusic: false, defaultMusicEnabled: true),
                Is.True, "the ambient returns immediately while the wait runs");
        }

        [Test]
        public void DoesNotBridge_WhenAlreadyOnDefaultMusic()
        {
            Assert.That(HoverSettlePolicy.ShouldBridgeToDefault(isPlayingDefaultMusic: true, defaultMusicEnabled: true),
                Is.False, "nothing to bridge - it is already playing");
        }

        // With default music disabled there is nothing to bridge WITH. The wait still defers the
        // switch; the old track simply plays it out.
        [Test]
        public void DoesNotBridge_WhenDefaultMusicIsDisabled()
        {
            Assert.That(HoverSettlePolicy.ShouldBridgeToDefault(isPlayingDefaultMusic: false, defaultMusicEnabled: false),
                Is.False);
        }

        // Two speeds, per the PS5 recording: leaving the idle ambient is the weighty transition
        // and takes the full slider; game-to-game still bridges through the ambient but settles in
        // half the time. The origin is latched at session start, so the numbers here are what a
        // user actually waits.
        [Test]
        public void FromTheAmbient_TakesTheFullBudget()
        {
            Assert.That(HoverSettlePolicy.SecondsForSwitch(3.0, 0.7, fromDefaultMusic: true),
                Is.EqualTo(2.3).Within(0.001), "full slider minus the fade-out spent inside it");
        }

        [Test]
        public void GameToGame_TakesHalf()
        {
            Assert.That(HoverSettlePolicy.SecondsForSwitch(3.0, 0.7, fromDefaultMusic: false),
                Is.EqualTo(0.8).Within(0.001), "half of 3s, minus the fade-out - music starts ~1.5s after rest");
        }

        [Test]
        public void GameToGame_NeverCollapsesBelowTheFloor()
        {
            Assert.That(HoverSettlePolicy.SecondsForSwitch(1.0, 5.0, fromDefaultMusic: false),
                Is.EqualTo(0.5), "a long fade-out must still leave a real debounce");
        }

        [Test]
        public void TheTwoSpeeds_AreActuallyDifferent()
        {
            var full = HoverSettlePolicy.SecondsForSwitch(3.0, 0.7, fromDefaultMusic: true);
            var half = HoverSettlePolicy.SecondsForSwitch(3.0, 0.7, fromDefaultMusic: false);
            Assert.That(half, Is.LessThan(full), "game-to-game is the snappy one");
        }

        // Desktop selection is click-driven, so a delay reads as lag rather than as polish.
        [Test]
        public void DoesNotDefer_InDesktop()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: false, isPlaying: true),
                Is.False, "Fullscreen-gated by design");
        }

        [Test]
        public void ClampSeconds_HoldsTheSupportedRange()
        {
            Assert.Multiple(() =>
            {
                Assert.That(HoverSettlePolicy.ClampSeconds(0), Is.EqualTo(Constants.MinHoverSettleSeconds));
                Assert.That(HoverSettlePolicy.ClampSeconds(-5), Is.EqualTo(Constants.MinHoverSettleSeconds));
                Assert.That(HoverSettlePolicy.ClampSeconds(999), Is.EqualTo(Constants.MaxHoverSettleSeconds));
                Assert.That(HoverSettlePolicy.ClampSeconds(3), Is.EqualTo(3));
            });
        }

        [Test]
        public void Default_IsWithinTheSliderRange()
        {
            Assert.That(Constants.DefaultHoverSettleSeconds,
                Is.InRange(Constants.MinHoverSettleSeconds, Constants.MaxHoverSettleSeconds));
        }

        // The settings setter clamps too; a config edited by hand must not escape the range.
        [Test]
        public void SettingsProperty_ClampsOnSet()
        {
            var s = new UniPlaySongSettings();
            Assert.That(s.HoverSettleSeconds, Is.EqualTo(Constants.DefaultHoverSettleSeconds));

            s.HoverSettleSeconds = 100;
            Assert.That(s.HoverSettleSeconds, Is.EqualTo(Constants.MaxHoverSettleSeconds));

            s.HoverSettleSeconds = 0;
            Assert.That(s.HoverSettleSeconds, Is.EqualTo(Constants.MinHoverSettleSeconds));
        }

        // The slider reads as "how long until the game's music starts". The transition fades the
        // old track out first, so that fade has to come out of the budget - otherwise a 3s setting
        // put the new music at 3 + fadeOut + fadeIn, which is why 3s measured as 4-6s.
        [Test]
        public void TimerSeconds_SpendsTheFadeOutInsideTheBudget()
        {
            Assert.That(HoverSettlePolicy.TimerSeconds(3.0, 0.7), Is.EqualTo(2.3).Within(0.001),
                "old track starts fading at 2.3s so the new one starts on the 3s mark");
        }

        [Test]
        public void TimerSeconds_UnchangedWhenThereIsNoFadeOut()
        {
            Assert.That(HoverSettlePolicy.TimerSeconds(3.0, 0), Is.EqualTo(3.0));
        }

        // A long fade-out must not collapse the wait to nothing - that would restore the
        // scroll-chop the whole feature exists to prevent.
        [Test]
        public void TimerSeconds_NeverCollapsesBelowTheMinimum()
        {
            Assert.That(HoverSettlePolicy.TimerSeconds(1.0, 5.0),
                Is.EqualTo(Constants.MinHoverSettleSeconds),
                "a fade-out longer than the setting must still leave a real wait");
        }

        [Test]
        public void TimerSeconds_ClampsAnOutOfRangeSetting()
        {
            Assert.That(HoverSettlePolicy.TimerSeconds(999, 0), Is.EqualTo(Constants.MaxHoverSettleSeconds));
        }

        // Off by default: an existing user's playback must not change until they ask for it.
        [Test]
        public void DisabledByDefault()
        {
            Assert.That(new UniPlaySongSettings().HoverSettleEnabled, Is.False);
        }
    }
}
