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
        public void Defers_WhenEnabled_InFullscreen_WithMusicPlaying()
        {
            Assert.That(HoverSettlePolicy.ShouldDefer(enabled: true, isFullscreen: true, isPlaying: true),
                Is.True, "the case the feature exists for");
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
