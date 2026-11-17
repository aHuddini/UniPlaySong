using NUnit.Framework;
using System;
using UniPlaySong.Services.Spotify;

namespace UniPlaySong.Tests.Services.Spotify
{
    [TestFixture]
    public class SpotifyEffectsCoordinatorTests
    {
        // Test double capturing mute state + capture/output starts.
        private class Env
        {
            public bool LiveEffects, ApplyToSpotify, Visualizer, SpotifyActive, OsCapable = true, CalmDown;
            public bool? MutedNow;       // null = never touched
            public bool CaptureRunning, EffectedOutput;
            public bool MuteSucceeds = true;
        }

        private SpotifyEffectsCoordinator Make(Env e) => new SpotifyEffectsCoordinator(
            isLiveEffects: () => e.LiveEffects,
            isApplyToSpotify: () => e.ApplyToSpotify,
            isVisualizer: () => e.Visualizer,
            isSpotifyActive: () => e.SpotifyActive,
            isOsCapable: () => e.OsCapable,
            setMuted: m => { if (!e.MuteSucceeds) return false; e.MutedNow = m; return true; },
            startCapture: () => { e.CaptureRunning = true; return true; },
            stopCapture: () => e.CaptureRunning = false,
            startEffectedOutput: () => e.EffectedOutput = true,
            stopEffectedOutput: () => e.EffectedOutput = false,
            isCalmDown: () => e.CalmDown);

        // The safety invariant: effected output => Spotify muted, dry Spotify => not muted. A null expectation is not checked.
        [Test]
        public void SingleEvaluate_TruthTable()
        {
            var cases = new[]
            {
                new { E = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true },
                      Capture = (bool?)true, Output = true, Muted = (bool?)true, Why = "effects on Spotify: mute and output" },
                new { E = new Env { Visualizer = true, SpotifyActive = true },
                      Capture = (bool?)true, Output = false, Muted = (bool?)false, Why = "visualizer only: capture, dry Spotify stays unmuted" },
                // Calm Down on, Live Effects OFF - should still duck + produce effected (calmed) output.
                new { E = new Env { CalmDown = true, SpotifyActive = true },
                      Capture = (bool?)true, Output = true, Muted = (bool?)true, Why = "Calm Down alone: mute and output" },
                new { E = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true, MuteSucceeds = false },
                      Capture = (bool?)null, Output = false, Muted = (bool?)null, Why = "mute failed: better dry than doubled" },
                new { E = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true, OsCapable = false },
                      Capture = (bool?)false, Output = false, Muted = (bool?)null, Why = "OS not capable: never captures" },
            };
            Assert.Multiple(() =>
            {
                foreach (var t in cases)
                {
                    Make(t.E).Evaluate();
                    if (t.Capture.HasValue) Assert.That(t.E.CaptureRunning, Is.EqualTo(t.Capture.Value), t.Why + " (capture)");
                    Assert.That(t.E.EffectedOutput, Is.EqualTo(t.Output), t.Why + " (output)");
                    if (t.Muted.HasValue) Assert.That(t.E.MutedNow == true, Is.EqualTo(t.Muted.Value), t.Why + " (muted)");
                }
            });
        }

        // Whatever ends the effected output must unmute Spotify (invariant: stop output => unmute).
        [Test]
        public void TurningItOff_StopsAndUnmutes()
        {
            var cases = new[]
            {
                new { E = new Env { CalmDown = true, SpotifyActive = true }, Change = (Action<Env>)(x => x.CalmDown = false),
                      Capture = (bool?)false, Output = (bool?)false, Why = "Calm Down off with nothing else" },
                new { E = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true }, Change = (Action<Env>)(x => x.ApplyToSpotify = false),
                      Capture = (bool?)null, Output = (bool?)false, Why = "effects toggled off" },
                new { E = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true }, Change = (Action<Env>)(x => x.SpotifyActive = false),
                      Capture = (bool?)false, Output = (bool?)null, Why = "Spotify no longer active" },
            };
            Assert.Multiple(() =>
            {
                foreach (var t in cases)
                {
                    var c = Make(t.E);
                    c.Evaluate();
                    t.Change(t.E);
                    c.Evaluate();
                    if (t.Capture.HasValue) Assert.That(t.E.CaptureRunning, Is.EqualTo(t.Capture.Value), t.Why + " (capture)");
                    if (t.Output.HasValue) Assert.That(t.E.EffectedOutput, Is.EqualTo(t.Output.Value), t.Why + " (output)");
                    Assert.That(t.E.MutedNow, Is.EqualTo(false), t.Why + " (unmuted)");
                }
            });
        }

        [Test]
        public void WhileEffecting_ReassertsDuckOnEvaluate()
        {
            // A device switch spawns a fresh UNducked Spotify session mid-effects; the
            // coordinator must re-duck on the next evaluate or the audio doubles (v1.6.8).
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true };
            var c = Make(e); c.Evaluate();
            e.MutedNow = false;              // simulate the duck being lost externally
            c.Evaluate();
            Assert.That(e.MutedNow, Is.True);
        }

        [Test]
        public void Shutdown_UnmutesEvenIfActive()
        {
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true };
            var c = Make(e); c.Evaluate();
            c.Shutdown();
            Assert.That(e.MutedNow, Is.False);
        }
    }
}
