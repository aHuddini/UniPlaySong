using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace UpsSound
{
    // UniPlaySong's out-of-process achievement sound host.
    //
    // Exists so the sound is emitted from a process tree that is not Playnite's. PlayniteAchievements
    // records unlocks and re-times the chime in post; its capture is process-tree based, so a chime
    // played from Playnite's own tree cannot be separated from an emulator Playnite launched. Played
    // from here, it can. See docs/dev_docs/features/JINGLE_SOUND_HOST.md.
    //
    // Deliberately dumb. It holds one output device open, plays one file at a time, and answers on
    // stdout. It has no settings, no state worth persisting, and no knowledge of UniPlaySong beyond
    // the line protocol. Anything cleverer belongs on the other side of the pipe, where it can be
    // tested and where a bug cannot take the sound down with it.
    internal static class Program
    {
        // Commands in, acks out. One line each, so a partial read is never a partial command.
        //   -> ready
        //   <- play <volume 0.000-1.000> <absolute path>
        //   -> ok <id> | err <id> <reason>
        //   -> done <id>
        //   <- stop
        //   <- quit
        private static readonly object Gate = new object();
        private static WaveOutEvent _output;
        private static AudioFileReader _reader;
        private static int _currentId;
        private static TextWriter _out;

        private static int Main(string[] args)
        {
            // Nothing about this process is useful without a parent reading stdout: it is spawned
            // with redirected pipes and never run by hand.
            _out = Console.Out;

            int parentPid = ParentPidFrom(args);
            if (parentPid > 0)
                StartParentWatchdog(parentPid);

            Send("ready");

            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    if (!Handle(line)) break;
                }
            }
            catch (IOException)
            {
                // Parent vanished mid-read. Nothing to report to, nothing to do but leave.
            }

            Shutdown();
            return 0;
        }

        // Returns false to exit the loop.
        private static bool Handle(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;

            if (line == "quit") return false;

            if (line == "stop")
            {
                StopCurrent();
                return true;
            }

            // play <volume> <path> — volume first so the path is the unbounded tail and may
            // contain spaces without quoting.
            if (line.StartsWith("play ", StringComparison.Ordinal))
            {
                var rest = line.Substring(5);
                int split = rest.IndexOf(' ');
                int id = Interlocked.Increment(ref _currentId);

                if (split <= 0)
                {
                    Send($"err {id} malformed");
                    return true;
                }

                var volumeText = rest.Substring(0, split);
                var path = rest.Substring(split + 1);

                if (!float.TryParse(volumeText, NumberStyles.Float, CultureInfo.InvariantCulture, out float volume))
                {
                    Send($"err {id} badvolume");
                    return true;
                }

                Play(id, path, Clamp01(volume));
                return true;
            }

            Send("err 0 unknown");
            return true;
        }

        private static void Play(int id, string path, float volume)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Send($"err {id} notfound");
                    return;
                }

                lock (Gate)
                {
                    StopCurrentLocked();

                    _reader = new AudioFileReader(path) { Volume = volume };
                    _output = new WaveOutEvent();
                    _output.PlaybackStopped += (s, e) =>
                    {
                        // Fires for both a finished file and an explicit stop. The caller only uses
                        // this to know the sound is over, so both are the same event to it.
                        Send($"done {id}");
                    };
                    _output.Init(_reader);
                    _output.Play();
                }

                Send($"ok {id}");
            }
            catch (Exception ex)
            {
                // Never let a bad file or a device that vanished take the process down: the parent
                // reads a failure here and plays the sound itself instead.
                Send($"err {id} {ex.GetType().Name}");
                StopCurrent();
            }
        }

        private static void StopCurrent()
        {
            lock (Gate) StopCurrentLocked();
        }

        private static void StopCurrentLocked()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            _output = null;
            _reader = null;
        }

        private static void Shutdown()
        {
            StopCurrent();
        }

        // Belt to the parent's job object: if UniPlaySong is killed in a way that skips the job
        // teardown, this process must still not outlive it. An orphaned audio process holding a
        // device open is a worse bug than anything this feature fixes.
        private static void StartParentWatchdog(int parentPid)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    using (var parent = Process.GetProcessById(parentPid))
                    {
                        parent.WaitForExit();
                    }
                }
                catch
                {
                    // Already gone, or never existed.
                }

                Shutdown();
                Environment.Exit(0);
            })
            {
                IsBackground = true,
                Name = "ups-sound-parent-watchdog"
            };
            thread.Start();
        }

        private static int ParentPidFrom(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--parent" && int.TryParse(args[i + 1], out int pid))
                    return pid;
            }
            return 0;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static void Send(string message)
        {
            try
            {
                lock (_out)
                {
                    _out.WriteLine(message);
                    _out.Flush();
                }
            }
            catch (IOException)
            {
                // Parent stopped reading. Playing on into a closed pipe is harmless; the watchdog
                // handles the exit.
            }
        }
    }
}
