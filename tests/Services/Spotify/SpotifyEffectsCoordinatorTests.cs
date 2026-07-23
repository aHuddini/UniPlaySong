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

        [Test]
        public void EffectsOnSpotify_MutesSpotify_AndOutputs()
        {
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true };
            Make(e).Evaluate();
            Assert.That(e.CaptureRunning, Is.True);
            Assert.That(e.EffectedOutput, Is.True);
            Assert.That(e.MutedNow, Is.True);            // invariant: effected output => Spotify muted
        }

        [Test]
        public void VizOnly_CapturesButDoesNotMute()
        {
            var e = new Env { Visualizer = true, SpotifyActive = true };  // effects off
            Make(e).Evaluate();
            Assert.That(e.CaptureRunning, Is.True);
            Assert.That(e.EffectedOutput, Is.False);
            Assert.That(e.MutedNow, Is.Not.EqualTo(true)); // dry Spotify -> not muted
        }

        [Test]
        public void CalmDownAlone_MutesSpotify_AndOutputs()
        {
            // Calm Down on, Live Effects OFF — should still duck + produce effected (calmed) output.
            var e = new Env { CalmDown = true, SpotifyActive = true };
            Make(e).Evaluate();
            Assert.That(e.CaptureRunning, Is.True);
            Assert.That(e.EffectedOutput, Is.True);
            Assert.That(e.MutedNow, Is.True);
        }

        [Test]
        public void CalmDownOff_WithNothingElse_UnmutesSpotify()
        {
            var e = new Env { CalmDown = true, SpotifyActive = true };
            var c = Make(e); c.Evaluate();
            e.CalmDown = false; c.Evaluate();
            Assert.That(e.EffectedOutput, Is.False);
            Assert.That(e.CaptureRunning, Is.False);
            Assert.That(e.MutedNow, Is.False);
        }

        [Test]
        public void EffectsToggledOff_UnmutesSpotify()
        {
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true };
            var c = Make(e); c.Evaluate();
            e.ApplyToSpotify = false; c.Evaluate();
            Assert.That(e.EffectedOutput, Is.False);
            Assert.That(e.MutedNow, Is.False);           // invariant: stop output => unmute
        }

        [Test]
        public void SpotifyNoLongerActive_StopsAndUnmutes()
        {
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true };
            var c = Make(e); c.Evaluate();
            e.SpotifyActive = false; c.Evaluate();
            Assert.That(e.CaptureRunning, Is.False);
            Assert.That(e.MutedNow, Is.False);
        }

        [Test]
        public void MuteFails_DoesNotStartEffectedOutput()
        {
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true, MuteSucceeds = false };
            Make(e).Evaluate();
            Assert.That(e.EffectedOutput, Is.False);     // better dry than doubled
        }

        [Test]
        public void OsNotCapable_NeverCaptures()
        {
            var e = new Env { LiveEffects = true, ApplyToSpotify = true, SpotifyActive = true, OsCapable = false };
            Make(e).Evaluate();
            Assert.That(e.CaptureRunning, Is.False);
            Assert.That(e.EffectedOutput, Is.False);
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
