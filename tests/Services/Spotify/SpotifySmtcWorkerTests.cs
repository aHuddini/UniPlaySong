using System;
using System.Threading;
using NUnit.Framework;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    [TestFixture]
    public class SpotifySmtcWorkerTests
    {
        [Test]
        public void PostRequest_RunsWorkOnBackgroundThread_NotCaller()
        {
            using (var w = new SpotifySmtcWorker(null))
            {
                w.Start();
                int callerThread = Thread.CurrentThread.ManagedThreadId;
                int workThread = -1;
                var done = new ManualResetEventSlim(false);
                w.PostRequest(() => { workThread = Thread.CurrentThread.ManagedThreadId; done.Set(); });
                Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)), "work did not run");
                Assert.AreNotEqual(callerThread, workThread, "work must run off the caller thread");
            }
        }

        [Test]
        public void PostControl_CoalescesToLatestIntent()
        {
            using (var w = new SpotifySmtcWorker(null))
            {
                w.Start();
                var executed = new System.Collections.Generic.List<SpotifyControlIntent>();
                var gate = new ManualResetEventSlim(false);
                // Block the worker with a slow first item so the next two queue up behind it.
                w.PostRequest(() => gate.Wait(TimeSpan.FromSeconds(5)));
                w.PostControl(SpotifyControlIntent.Play, i => { lock (executed) executed.Add(i); });
                w.PostControl(SpotifyControlIntent.Pause, i => { lock (executed) executed.Add(i); });
                gate.Set(); // release the worker
                Thread.Sleep(300); // let it drain
                lock (executed)
                {
                    // Latest control intent wins: exactly one control executes, and it is Pause.
                    Assert.AreEqual(1, executed.Count, "coalescing must collapse to one control execution");
                    Assert.AreEqual(SpotifyControlIntent.Pause, executed[0]);
                }
            }
        }

        [Test]
        public void ThrowingWork_DoesNotKillWorker()
        {
            using (var w = new SpotifySmtcWorker(null))
            {
                w.Start();
                w.PostRequest(() => throw new InvalidOperationException("boom"));
                var done = new ManualResetEventSlim(false);
                w.PostRequest(() => done.Set());
                Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)), "worker died after a throwing action");
            }
        }
    }
}
