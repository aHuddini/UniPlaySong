using System;
using System.Reflection;
using System.Windows.Media;
using NAudio.Wave;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Players
{
    // Pins the branch-order contract in MusicFader.Tick that produced a reported cold-start bug:
    // "no music on the first game details view after a cold start", and "second game silent, third
    // onward fine".
    //
    // Tick chooses between switching and pausing with
    //     if (_isFadingOut && volume <= 0 && _pauseAction == null && _playAction != null)  -> switch
    //     if (_isFadingOut && volume <= 0 && (_pauseAction != null || _stopAction != null)) -> pause
    //
    // so a pause left armed from earlier makes a genuine song switch take the PAUSE branch. That
    // branch cleared only the pause and stop actions, leaving _playAction set but never invoked -
    // the next track was preloaded and then silently never played. The following game worked
    // because _pauseAction was null by then, which is exactly the "second one is silent" shape.
    //
    // Tested through reflection rather than behaviour because the fader drives itself from a
    // DispatcherTimer and Application.Current, neither of which exists in a test host. The state
    // these assert is the state the branch conditions read.
    [TestFixture]
    public class MusicFaderPendingActionTests
    {
        private static object NewFader(out Type type)
        {
            var asm = typeof(MusicPlaybackService).Assembly;
            type = asm.GetType("UniPlaySong.Players.MusicFader");
            Assert.NotNull(type, "MusicFader type not found");

            var player = new SilentPlayer();
            return Activator.CreateInstance(type, new object[]
            {
                player,
                (Func<double>)(() => 1.0),
                (Func<double>)(() => 0.5),
                (Func<double>)(() => 0.5),
                null,
                null
            });
        }

        private static void SetField(object target, Type type, string name, object value) =>
            type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static object GetField(object target, Type type, string name) =>
            type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        [Test]
        public void Switch_ClearsAPendingPauseSoTheSwitchBranchCanRun()
        {
            var fader = NewFader(out var type);

            // A pause armed earlier and never completed - what the startup ThemeOverlay
            // pause/resume cycle could leave behind.
            SetField(fader, type, "_pauseAction", (Action)(() => { }));

            type.GetMethod("Switch").Invoke(fader, new object[]
            {
                (Action)(() => { }),   // stop
                null,                  // preload
                (Action)(() => { }),   // play
                null                   // fadeOutOverride
            });

            Assert.IsNull(GetField(fader, type, "_pauseAction"),
                "a switch must supersede a pause that never completed, or Tick takes the PAUSE " +
                "branch and the newly selected song is never played");
            Assert.IsNotNull(GetField(fader, type, "_playAction"),
                "and the play action must survive, since it is the whole point of the switch");
        }

        [Test]
        public void SwitchWithoutAPlayAction_LeavesAPendingPauseAlone()
        {
            // A stop-only Switch is not a song change and must not cancel a pause in flight -
            // clearing it there would resume audio the user asked to be silenced.
            var fader = NewFader(out var type);
            var pause = (Action)(() => { });
            SetField(fader, type, "_pauseAction", pause);

            type.GetMethod("Switch").Invoke(fader, new object[]
            {
                (Action)(() => { }), null, null, null
            });

            Assert.AreSame(pause, GetField(fader, type, "_pauseAction"),
                "only a switch that actually plays something supersedes a pending pause");
        }

        // Minimal IMusicPlayer: the fader only reads Volume/IsActive and calls SetVolumeRamp on the
        // paths under test, and a real backend would open an audio device the test host has not got.
        private sealed class SilentPlayer : IMusicPlayer
        {
            public event EventHandler MediaEnded;
            public event EventHandler<ExceptionEventArgs> MediaFailed;

            public double Volume { get; set; }
            public bool IsLoaded => false;
            public bool IsActive => false;
            public TimeSpan? CurrentTime => TimeSpan.Zero;
            public TimeSpan? TotalTime => TimeSpan.Zero;
            public string Source => null;

            public void PreLoad(string filePath) { }
            public void Load(string filePath) { }
            public void LoadExternalSource(ISampleProvider source) { }
            public void StopExternalSource() { }
            public void Play() { }
            public void Play(TimeSpan startFrom) { }
            public void Stop() { }
            public void Pause() { }
            public void Resume(Action onReady = null) { onReady?.Invoke(); }
            public void Close() { }
            public void SetVolumeRamp(double targetVolume, double durationSeconds) { Volume = targetVolume; }

            public bool IsAudioDeviceOpen => false;
            public void ReleaseAudioDevice() { }
            public void PrewarmAudioDevice() { }
            public string AudioDeviceLabel => nameof(SilentPlayer);

            private void Unused() { MediaEnded?.Invoke(this, EventArgs.Empty); MediaFailed?.Invoke(this, null); }
        }
    }
}
