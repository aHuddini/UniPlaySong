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
        public void Defers_LeavingTheAmbient_InFullscreen()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlayingDefaultMusic: true),
                Is.True, "the case the feature exists for");
        }

        [Test]
        public void DoesNotDefer_WhenDisabled()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: false, isFullscreen: true, isPlayingDefaultMusic: true),
                Is.False);
        }

        // Waiting on silence produces more silence. Without this the option would make things
        // worse than not having it whenever playback had not started yet.
        [Test]
        public void DoesNotDefer_WhenNothingIsPlaying()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlayingDefaultMusic: false),
                Is.False, "nothing to protect - starting now beats waiting on silence");
        }

        // Observed on the PS5: leaving the dashboard ambient takes a couple of seconds, but moving
        // between titles switches quickly. Once a game's track is playing the ambient is already
        // gone, so there is nothing left for the delay to protect.
        [Test]
        public void DoesNotDefer_GameToGame()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlayingDefaultMusic: false),
                Is.False, "game-to-game stays snappy - the ambient is already gone");
        }

        // The PS5 theme drives the ambient-to-game transition through its
        // ForceDefaultMusicOverride binding rather than through selection, so the delay is honoured
        // on that path too - gated on PS5ThemeCompatMode, the same way every other change to that
        // path is gated. The direction decides: leaving the ambient waits, returning does not, and
        // the ambient-is-playing check IS the "leaving the ambient" case.
        [Test]
        public void Defers_LeavingTheAmbient_OnTheOverridePath()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlayingDefaultMusic: true),
                Is.True);
        }

        [Test]
        public void DoesNotDefer_ReturningToTheAmbient()
        {
            // wantDefault==true is filtered before the policy is consulted, and the state it
            // arrives in - a game's track playing - independently says do not wait.
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlayingDefaultMusic: false),
                Is.False, "going back to the ambient is immediate");
        }

        // Both toggles are off by default, so a fresh install behaves exactly as before on every
        // theme - the override path included.
        [Test]
        public void BothGates_AreOffByDefault()
        {
            var s = new UniPlaySongSettings();
            Assert.Multiple(() =>
            {
                Assert.That(s.HoverSettleEnabled, Is.False, "the feature itself");
                Assert.That(s.PS5ThemeCompatMode, Is.False, "the theme patch that carries it to the override path");
            });
        }

        // Desktop selection is click-driven, so a delay reads as lag rather than as polish.
        [Test]
        public void DoesNotDefer_InDesktop()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: false, isPlayingDefaultMusic: true),
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
