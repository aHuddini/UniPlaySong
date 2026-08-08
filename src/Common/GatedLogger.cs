using System;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // ILogger wrapper that keeps Playnite's SHARED extension.log quiet during normal use.
    //
    // extension.log is not ours — every installed extension writes to it, and a user opening it to diagnose an
    // unrelated problem should not have to scroll past hundreds of UPS lines. With debug logging off this drops
    // Trace/Debug/Info/Warn and lets only Error through, which is what a bug report actually needs. Turning Enable
    // Debug Logging on restores everything.
    //
    // Gating happens HERE rather than at the ~200 call sites because almost none of the classes holding an ILogger
    // also hold a FileLogger — routing each call individually would mean threading a second logger through
    // roughly forty classes. Swapping the factory in the field initialiser gates all of them with one line per file.
    //
    // Warn is gated here but NOT in FileLogger: warnings still land in UniPlaySong.log (our own
    // file, low volume), so support keeps that signal without noising up Playnite's log.
    public sealed class GatedLogger : ILogger
    {
        private readonly ILogger _inner;

        private GatedLogger(ILogger inner)
        {
            _inner = inner;
        }

        // Drop-in replacement for LogManager.GetLogger(). This is the ONE place that may still
        // call LogManager directly — everywhere else goes through Get().
        public static ILogger Get() => new GatedLogger(LogManager.GetLogger());

        // FileLogger.IsDebugLoggingEnabled is true while the gate is unset, so logging during
        // early startup (before settings load) behaves exactly as it did before.
        private static bool Verbose => FileLogger.IsDebugLoggingEnabled;

        public void Trace(string message) { if (Verbose) _inner.Trace(message); }
        public void Trace(Exception ex, string message) { if (Verbose) _inner.Trace(ex, message); }

        public void Debug(string message) { if (Verbose) _inner.Debug(message); }
        public void Debug(Exception ex, string message) { if (Verbose) _inner.Debug(ex, message); }

        public void Info(string message) { if (Verbose) _inner.Info(message); }
        public void Info(Exception ex, string message) { if (Verbose) _inner.Info(ex, message); }

        public void Warn(string message) { if (Verbose) _inner.Warn(message); }
        public void Warn(Exception ex, string message) { if (Verbose) _inner.Warn(ex, message); }

        // Errors are never gated — they are the reason the log exists.
        public void Error(string message) => _inner.Error(message);
        public void Error(Exception ex, string message) => _inner.Error(ex, message);
    }
}
