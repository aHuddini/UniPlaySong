using System;
using System.Diagnostics;
using Playnite.SDK;

namespace UniPlaySong.Common
{
    // Helpers for invoking the Windows shell (Explorer, default-app launches, etc.)
    // without leaking Process handles.
    public static class ShellHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Opens a folder (or file path) in Windows Explorer.
        //
        // Why this exists instead of `Process.Start("explorer.exe", path)`:
        //  1. ShellExecute lets Explorer reuse an existing shell host where Windows
        //     allows it (no second explorer.exe worker process per call).
        //  2. Disposes the returned Process handle immediately, avoiding the
        //     .NET-side handle leak that accumulated on every Open Music Folder click.
        //  3. Centralizes the call so future shell-related changes (UWP integration,
        //     /select, argument quoting) only need updating in one place.
        public static void OpenFolderInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            try
            {
                // UseShellExecute=true routes through ShellExecuteEx — this is the path
                // that lets the existing Explorer shell host handle the request rather
                // than spawning a fresh explorer.exe child. The using-dispose immediately
                // releases the .NET Process handle since we don't need to track the
                // spawned process at all.
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                }))
                {
                    // Process disposed at end of using block.
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"ShellHelper.OpenFolderInExplorer failed for path: {path}");
            }
        }
    }
}
