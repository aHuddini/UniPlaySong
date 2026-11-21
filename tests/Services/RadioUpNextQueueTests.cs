using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    [TestFixture]
    public class RadioUpNextQueueTests
    {
        // What themes show is what plays: each take is the old head, the queue stays full, a "Play Next" pick jumps the
        // line ahead of the pool's picks, and a song whose file was deleted is skipped
        [Test]
        public void Take_PlaysTheQueueInOrder_PlayNextJumpsTheLine_DeletedFilesSkipped()
        {
            var pool = new List<string> { "a", "b", "c" };
            var deleted = new HashSet<string>();
            int i = 0;
            var queue = new RadioUpNextQueue((p, avoid) => p[i++ % p.Count], path => !deleted.Contains(path));

            var first = queue.Take(pool, null);
            Assert.AreEqual(RadioUpNextQueue.Size, queue.Paths.Count);

            var shown = queue.Paths[0];
            Assert.AreEqual(shown, queue.Take(pool, first));

            queue.PlayNext("queued");
            Assert.AreEqual("queued", queue.Paths[0]);
            Assert.AreEqual("queued", queue.Take(pool, null));

            deleted.Add(queue.Paths[0]);
            var expected = queue.Paths.First(p => !deleted.Contains(p));
            Assert.AreEqual(expected, queue.Take(pool, null));
        }
    }
}
