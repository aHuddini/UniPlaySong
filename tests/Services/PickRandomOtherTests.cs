using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // Every random pick in MusicPlaybackService (skip, song end, preview restart, radio start, randomize on select)
    // goes through PickRandomOther: a song other than the one just played, without ever looping forever.
    [TestFixture]
    public class PickRandomOtherTests
    {
        [Test]
        public void PicksAnotherSongWhenThereIsOne()
        {
            var random = new Random(1);
            var two = new List<string> { "a.mp3", "b.mp3" };
            var three = new List<string> { "a.mp3", "b.mp3", "c.mp3" };

            Assert.Multiple(() =>
            {
                Assert.IsNull(MusicPlaybackService.PickRandomOther(new List<string>(), "a.mp3", random), "empty pool");
                Assert.IsNull(MusicPlaybackService.PickRandomOther(null, "a.mp3", random), "no pool");
                Assert.AreEqual("a.mp3", MusicPlaybackService.PickRandomOther(new List<string> { "a.mp3" }, "a.mp3", random),
                    "a one-song pool plays that song again");

                var picks = Enumerable.Range(0, 200).Select(_ => MusicPlaybackService.PickRandomOther(two, "a.mp3", random)).ToList();
                Assert.That(picks, Is.All.EqualTo("b.mp3"), "never repeats the avoided song when another exists");

                picks = Enumerable.Range(0, 200).Select(_ => MusicPlaybackService.PickRandomOther(three, null, random)).ToList();
                Assert.That(picks.Distinct().Count(), Is.EqualTo(3), "with nothing to avoid, any song can come up");
            });
        }
    }
}
