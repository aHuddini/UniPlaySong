using System;
using System.IO;

namespace UniPlaySong.Downloaders
{
    // Why a download failed, in words the user can act on. yt-dlp's stderr names the cause; the dialogs used to
    // show a generic "see the log" pointing at a hard-coded %AppData% path, and the most common cause - a browser
    // whose cookies yt-dlp could not read (issue #99) - was not recognised at all.
    public static class DownloadFailure
    {
        public enum Kind { Unknown, Cookies, BrokenYtDlp, BotCheck, Network, FFmpeg, Unavailable, Permission, DiskSpace }

        // The last download on THIS thread. A download runs start to finish on its caller's thread (yt-dlp is waited
        // on synchronously), so this stays right while batch downloads run several at once. Read it on the thread that
        // called DownloadSong, before hopping to the dispatcher. Null when the cause is unknown or nothing failed.
        [ThreadStatic] private static string _reason;

        public static string Reason
        {
            get => _reason;
            internal set => _reason = value;
        }

        // First match wins; cookies go first because a browser that can't be read is the cause even when YouTube's
        // own complaints follow it in the same output. The strings are yt-dlp's own (yt_dlp/cookies.py,
        // YoutubeDL.py at 2026.03.17): "Could not copy Chrome cookie database", "Failed to decrypt with DPAPI",
        // "could not find <browser> cookies database", "failed to load cookies".
        public static Kind Classify(string ytDlpError)
        {
            if (string.IsNullOrWhiteSpace(ytDlpError)) return Kind.Unknown;
            var e = ytDlpError.ToLowerInvariant();

            if (e.Contains("cookie database") || e.Contains("cookies database") || e.Contains("dpapi")
                || e.Contains("failed to load cookies") || e.Contains("cookies are no longer valid"))
                return Kind.Cookies;
            if (e.Contains("failed to load python dll") || e.Contains("pyi-") || e.Contains("_internal"))
                return Kind.BrokenYtDlp;
            if (e.Contains("sign in to confirm") || e.Contains("not a bot"))
                return Kind.BotCheck;
            if (e.Contains("unable to download") || e.Contains("http error") || e.Contains("network"))
                return Kind.Network;
            if (e.Contains("ffmpeg") || e.Contains("postprocess"))
                return Kind.FFmpeg;
            if (e.Contains("private video") || e.Contains("unavailable"))
                return Kind.Unavailable;
            if (e.Contains("permission") || e.Contains("access denied"))
                return Kind.Permission;
            if (e.Contains("no space left") || e.Contains("not enough space"))
                return Kind.DiskSpace;
            return Kind.Unknown;
        }

        public static string Describe(Kind kind)
        {
            switch (kind)
            {
                case Kind.Cookies:
                    return "yt-dlp couldn't read your browser's cookies. Close the browser and try again, or choose "
                        + "\"No cookies\" (or Firefox) under Setup > Downloads > Cookie Source.";
                case Kind.BrokenYtDlp:
                    return "yt-dlp.exe looks damaged or incomplete. Replace it with the single-file yt-dlp.exe from "
                        + "yt-dlp's GitHub releases.";
                case Kind.BotCheck:
                    return "YouTube asked to confirm you're not a bot. Make sure Deno is installed, update yt-dlp, "
                        + "or wait a few minutes and try again.";
                case Kind.Network:
                    return "YouTube couldn't be reached or refused the download. Check your connection and try again.";
                case Kind.FFmpeg:
                    return "FFmpeg failed while converting the audio. Check the FFmpeg path under Setup > Tools.";
                case Kind.Unavailable:
                    return "This video is private or unavailable.";
                case Kind.Permission:
                    return "The song couldn't be saved: Windows denied access to the music folder.";
                case Kind.DiskSpace:
                    return "The song couldn't be saved: the disk is full.";
                default:
                    return null;
            }
        }

        // The dialog text: what failed, why (when known), and where the full yt-dlp output is. Playnite writes
        // extensions.log in its configuration folder, which is next to Playnite.exe on a portable install.
        public static string ForUser(string headline, string reason, string playniteConfigurationPath)
        {
            var logPath = Path.Combine(playniteConfigurationPath ?? string.Empty, "extensions.log");
            return reason == null
                ? $"{headline}\n\nFull details: {logPath}"
                : $"{headline}\n\n{reason}\n\nFull details: {logPath}";
        }
    }
}
