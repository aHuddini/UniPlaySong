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
        // One truth table for the defer rule. Inputs are (enabled, isFullscreen, isPlaying).
        [Test]
        public void ShouldDefer_TruthTable()
        {
            var cases = new[]
            {
                // The case the feature exists for. Also the Tag path: some themes switch default/game
                // music through the UPS_MusicControl_PauseGamePlayDefault Tag rather than selection,
                // and GameHoverDelayPatch carries the delay there - leaving the default music waits,
                // and the default-is-playing check IS the "leaving it" case.
                // Gated on "any music playing" deliberately: IsPlayingDefaultMusic is cleared before
                // the outgoing fade starts, so during a scroll it reads false while the ambient is
                // still audible - a gate on it made every switch in that window chop. isPlaying is the
                // ear's truth; pinned so the narrower gate does not come back looking like a refinement.
                new { Enabled = true, Fullscreen = true, Playing = true, Expected = true, Why = "music playing in Fullscreen (incl. default flag already cleared, Tag path)" },
                new { Enabled = false, Fullscreen = true, Playing = true, Expected = false, Why = "disabled" },
                // Waiting on silence produces more silence. Also game-to-game (PS5: once a game's track
                // plays the ambient is already gone) and returning to default music (wantDefault is
                // filtered before the policy, and a game's track playing independently says do not wait).
                new { Enabled = true, Fullscreen = true, Playing = false, Expected = false, Why = "nothing to protect / game-to-game / returning to default" },
                // Desktop selection is click-driven, so a delay reads as lag rather than as polish.
                new { Enabled = true, Fullscreen = false, Playing = true, Expected = false, Why = "Desktop - Fullscreen-gated by design" },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.That(HoverSettlePolicy.ShouldDefer(enabled: c.Enabled, isFullscreen: c.Fullscreen, isPlaying: c.Playing),
                        Is.EqualTo(c.Expected), $"enabled={c.Enabled} fullscreen={c.Fullscreen} playing={c.Playing}: {c.Why}");
            });
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
        // never plays out the wait. With default music disabled there is nothing to bridge WITH;
        // the wait still defers the switch and the old track simply plays it out.
        [Test]
        public void ShouldBridgeToDefault_TruthTable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(HoverSettlePolicy.ShouldBridgeToDefault(isPlayingDefaultMusic: false, defaultMusicEnabled: true),
                    Is.True, "leaving a game's track - the ambient returns immediately while the wait runs");
                Assert.That(HoverSettlePolicy.ShouldBridgeToDefault(isPlayingDefaultMusic: true, defaultMusicEnabled: true),
                    Is.False, "already on default music - nothing to bridge");
                Assert.That(HoverSettlePolicy.ShouldBridgeToDefault(isPlayingDefaultMusic: false, defaultMusicEnabled: false),
                    Is.False, "default music disabled - nothing to bridge with");
            });
        }

        // The slider reads as "how long until the game's music starts". The transition fades the
        // old track out first, so that fade has to come out of the budget - otherwise a 3s setting
        // put the new music at 3 + fadeOut + fadeIn, which is why 3s measured as 4-6s.
        // Two speeds, per the PS5 recording: leaving the idle ambient is the weighty transition and
        // takes the full slider; game-to-game still bridges through the ambient but settles in half
        // the time. The origin is latched at session start, so these are what a user actually waits.
        // A long fade-out must never collapse the wait to nothing - that would restore the
        // scroll-chop the whole feature exists to prevent.
        [Test]
        public void SettleTiming_Table()
        {
            var cases = new[]
            {
                new { Name = "TimerSeconds(3, 0.7)", Get = (System.Func<double>)(() => HoverSettlePolicy.TimerSeconds(3.0, 0.7)), Expected = 2.3, Why = "old track starts fading at 2.3s so the new one starts on the 3s mark" },
                new { Name = "TimerSeconds(3, 0)", Get = (System.Func<double>)(() => HoverSettlePolicy.TimerSeconds(3.0, 0)), Expected = 3.0, Why = "unchanged when there is no fade-out" },
                new { Name = "TimerSeconds(1, 5)", Get = (System.Func<double>)(() => HoverSettlePolicy.TimerSeconds(1.0, 5.0)), Expected = Constants.MinHoverSettleSeconds, Why = "a fade-out longer than the setting must still leave a real wait" },
                new { Name = "TimerSeconds(999, 0)", Get = (System.Func<double>)(() => HoverSettlePolicy.TimerSeconds(999, 0)), Expected = Constants.MaxHoverSettleSeconds, Why = "an out-of-range setting is clamped" },
                new { Name = "SecondsForSwitch(3, 0.7, fromDefault)", Get = (System.Func<double>)(() => HoverSettlePolicy.SecondsForSwitch(3.0, 0.7, fromDefaultMusic: true)), Expected = 2.3, Why = "from the ambient: full slider minus the fade-out spent inside it" },
                new { Name = "SecondsForSwitch(3, 0.7, gameToGame)", Get = (System.Func<double>)(() => HoverSettlePolicy.SecondsForSwitch(3.0, 0.7, fromDefaultMusic: false)), Expected = 0.8, Why = "game-to-game: half of 3s minus the fade-out - music starts ~1.5s after rest" },
                new { Name = "SecondsForSwitch(1, 5, gameToGame)", Get = (System.Func<double>)(() => HoverSettlePolicy.SecondsForSwitch(1.0, 5.0, fromDefaultMusic: false)), Expected = 0.5, Why = "game-to-game never collapses below the floor" },
            };
            Assert.Multiple(() =>
            {
                foreach (var c in cases)
                    Assert.That(c.Get(), Is.EqualTo(c.Expected).Within(0.001), $"{c.Name}: {c.Why}");
            });
        }

        // The bridge's outgoing fade is a quick duck, not the configured FadeOutDuration - the
        // abandoned track must get out of the ambient's way at PS5 speed. It has to stay well under
        // the shortest possible wait, or the bridge would still be fading when the commit's own
        // fade-out wants the player.
        [Test]
        public void BridgeFade_IsAQuickDuck()
        {
            Assert.That(Constants.HoverBridgeFadeOutSeconds, Is.LessThanOrEqualTo(0.5),
                "the ambient should arrive almost immediately");
            Assert.That(Constants.HoverBridgeFadeOutSeconds,
                Is.LessThan(Constants.MinHoverSettleSeconds / 2.0),
                "must finish inside even the shortest game-to-game wait");
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
    }
}
