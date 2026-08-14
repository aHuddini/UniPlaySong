using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // The prewarm gate decides whether UniPlaySong opens its audio device ahead of the first sound.
    // It has to name every feature that plays through JingleService: a feature missing from it still
    // works, but pays the full cold cost on every fire (~110ms device open + ~30ms first decode) and
    // pays it again after each idle teardown. That is invisible in tests and easy to reintroduce —
    // the ControlUp ding shipped with exactly this defect, gated behind the celebration settings.
    [TestFixture]
    public class PrewarmGateTests
    {
        // Settings the gate consults, kept in sync with AnySoundFeatureWantsPrewarm.
        private static readonly string[] GatedSettings =
        {
            "EnableCompletionCelebration",
            "EnableAbandonedSound",
            "EnableAchievementSound",
            "EnableControlUpDetectSound",
        };

        [Test]
        public void TheGateNamesASettingThatExists()
        {
            foreach (var name in GatedSettings)
            {
                Assert.IsNotNull(typeof(global::UniPlaySong.UniPlaySongSettings).GetProperty(name),
                    $"{name} is gated but no longer exists — the gate would not compile, or silently drifted");
            }
        }

        // Every JingleEvent must be reachable from something the gate turns on. A new event added
        // without extending the gate is the exact defect this pins.
        [Test]
        public void EveryJingleEvent_IsCoveredByAGatedSetting()
        {
            // event -> the setting that enables it
            var coverage = new System.Collections.Generic.Dictionary<JingleEvent, string>
            {
                { JingleEvent.Completion,           "EnableCompletionCelebration" },
                { JingleEvent.Abandoned,            "EnableAbandonedSound" },
                { JingleEvent.Achievement,          "EnableAchievementSound" },
                { JingleEvent.AchievementCommon,    "EnableAchievementSound" },
                { JingleEvent.AchievementUncommon,  "EnableAchievementSound" },
                { JingleEvent.AchievementRare,      "EnableAchievementSound" },
                { JingleEvent.AchievementUltraRare, "EnableAchievementSound" },
                { JingleEvent.AchievementHidden,    "EnableAchievementSound" },
                { JingleEvent.AchievementCapstone,  "EnableAchievementSound" },
                { JingleEvent.ControllerDetected,   "EnableControlUpDetectSound" },
            };

            foreach (JingleEvent evt in Enum.GetValues(typeof(JingleEvent)))
            {
                Assert.IsTrue(coverage.ContainsKey(evt),
                    $"JingleEvent.{evt} is new: map it to the setting that enables it, and make sure " +
                    "AnySoundFeatureWantsPrewarm consults that setting — otherwise its first sound is cold");

                CollectionAssert.Contains(GatedSettings, coverage[evt],
                    $"JingleEvent.{evt} is enabled by {coverage[evt]}, which the prewarm gate does not check");
            }
        }

        // Pins the reason the gate must be wider than the celebration settings: external notification
        // events are played by the same service, through the same device.
        [Test]
        public void ExternalEvents_ArePlayedByTheSameServiceAsCelebrationJingles()
        {
            var method = typeof(JingleService).GetMethod(
                "IsExternalNotificationEvent", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method, "the external-vs-celebration split is what makes the gate load-bearing");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { JingleEvent.ControllerDetected }));
            Assert.IsTrue((bool)method.Invoke(null, new object[] { JingleEvent.Achievement }));
            Assert.IsFalse((bool)method.Invoke(null, new object[] { JingleEvent.Completion }));
        }
    }
}
