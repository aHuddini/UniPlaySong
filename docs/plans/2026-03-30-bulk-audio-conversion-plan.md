# Bulk Audio Format Conversion — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a bulk audio format conversion feature that converts all music files in the library to OGG or MP3 at selectable bitrate (128/192/256 kbps), with optional backup and parallel processing.

**Architecture:** New `AudioConversionService` handles FFmpeg conversion logic with parallelization and size tracking. New `ConversionDialogHandler` manages UI flow (validation, confirmation, progress dialog). Reuses existing `NormalizationProgressDialog` for progress display. Follows the exact same pattern as the normalization pipeline.

**Tech Stack:** C# / .NET 4.6.2 / FFmpeg (user-installed) / WPF / Parallel.ForEach

---

### Task 1: Add Settings Properties

**Files:**
- Modify: `src/UniPlaySongSettings.cs`

**Step 1: Add backing fields**

After the existing normalization backing fields (around line 1122), add:

```csharp
private string conversionTargetFormat = "ogg";
private string conversionBitrate = "192";
private bool conversionKeepOriginals = false;
```

**Step 2: Add public properties**

After the existing normalization properties (around line 1190), add:

```csharp
public string ConversionTargetFormat
{
    get => conversionTargetFormat;
    set { conversionTargetFormat = value ?? "ogg"; OnPropertyChanged(); }
}

public string ConversionBitrate
{
    get => conversionBitrate;
    set { conversionBitrate = value ?? "192"; OnPropertyChanged(); }
}

public bool ConversionKeepOriginals
{
    get => conversionKeepOriginals;
    set { conversionKeepOriginals = value; OnPropertyChanged(); }
}
```

**Step 3: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 2: Create AudioConversionService

**Files:**
- Create: `src/Services/AudioConversionService.cs`

**Step 1: Create the service**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Models;

namespace UniPlaySong.Services
{
    public class AudioConversionService
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private readonly FileLogger _fileLogger;

        public AudioConversionService(FileLogger fileLogger = null)
        {
            _fileLogger = fileLogger;
        }

        public bool ValidateFFmpegAvailable(string ffmpegPath)
        {
            return FFmpegHelper.IsAvailable(ffmpegPath);
        }

        public async Task<ConversionResult> ConvertBulkAsync(
            List<string> files,
            string ffmpegPath,
            string targetFormat,
            string bitrate,
            bool keepOriginals,
            IProgress<NormalizationProgress> progress,
            CancellationToken cancellationToken)
        {
            var result = new ConversionResult
            {
                TotalFiles = files.Count
            };

            var failedFiles = new ConcurrentBag<string>();
            int completedCount = 0;
            long totalOriginalSize = 0;
            long totalNewSize = 0;

            string codec = targetFormat == "mp3" ? "libmp3lame" : "libvorbis";
            string extension = targetFormat == "mp3" ? ".mp3" : ".ogg";

            int maxParallelism = Math.Min(Environment.ProcessorCount, 3);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            };

            await Task.Run(() =>
            {
                Parallel.ForEach(files, parallelOptions, (filePath, loopState) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        loopState.Stop();
                        return;
                    }

                    try
                    {
                        var originalSize = new FileInfo(filePath).Length;
                        var success = ConvertFile(filePath, ffmpegPath, codec, bitrate, extension, keepOriginals);

                        if (success)
                        {
                            Interlocked.Increment(ref result.SuccessCount);
                            Interlocked.Add(ref totalOriginalSize, originalSize);

                            // Get new file size
                            var newPath = Path.Combine(
                                Path.GetDirectoryName(filePath),
                                Path.GetFileNameWithoutExtension(filePath) + extension);
                            if (File.Exists(newPath))
                            {
                                Interlocked.Add(ref totalNewSize, new FileInfo(newPath).Length);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref result.FailureCount);
                            failedFiles.Add(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger?.Error($"Conversion failed for '{filePath}': {ex.Message}");
                        Interlocked.Increment(ref result.FailureCount);
                        failedFiles.Add(filePath);
                    }

                    int current = Interlocked.Increment(ref completedCount);
                    progress?.Report(new NormalizationProgress
                    {
                        CurrentFile = Path.GetFileName(filePath),
                        CurrentIndex = current,
                        TotalFiles = files.Count,
                        SuccessCount = result.SuccessCount,
                        FailureCount = result.FailureCount,
                        Status = $"Converted: {Path.GetFileName(filePath)}",
                        IsComplete = current >= files.Count
                    });
                });
            }, cancellationToken);

            result.FailedFiles = failedFiles.ToList();
            result.TotalOriginalBytes = totalOriginalSize;
            result.TotalNewBytes = totalNewSize;
            result.IsComplete = true;

            return result;
        }

        private bool ConvertFile(string filePath, string ffmpegPath, string codec, string bitrate, string targetExtension, bool keepOriginals)
        {
            var directory = Path.GetDirectoryName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            var tempPath = Path.Combine(directory, $"{nameWithoutExt}.converting{targetExtension}");
            var finalPath = Path.Combine(directory, $"{nameWithoutExt}{targetExtension}");

            try
            {
                // If source and target would have the same path (same extension), use a different temp name
                bool sameExtension = Path.GetExtension(filePath).Equals(targetExtension, StringComparison.OrdinalIgnoreCase);

                var args = $"-i \"{filePath}\" -c:a {codec} -b:a {bitrate}k -y \"{tempPath}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(psi))
                {
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(120000); // 2 minute timeout per file

                    if (!process.HasExited)
                    {
                        process.Kill();
                        CleanupTempFile(tempPath);
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        CleanupTempFile(tempPath);
                        return false;
                    }
                }

                // Verify temp file was created
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                {
                    CleanupTempFile(tempPath);
                    return false;
                }

                // Handle original file
                if (sameExtension)
                {
                    if (keepOriginals)
                    {
                        var backupPath = Path.Combine(directory, $"{nameWithoutExt}-preconvert{Path.GetExtension(filePath)}");
                        File.Move(filePath, backupPath);
                    }
                    else
                    {
                        File.Delete(filePath);
                    }
                }
                else
                {
                    if (keepOriginals)
                    {
                        var backupPath = Path.Combine(directory, $"{nameWithoutExt}-preconvert{Path.GetExtension(filePath)}");
                        File.Move(filePath, backupPath);
                    }
                    else
                    {
                        File.Delete(filePath);
                    }
                }

                // Move temp to final
                if (File.Exists(finalPath) && finalPath != filePath)
                {
                    File.Delete(finalPath);
                }
                File.Move(tempPath, finalPath);

                return true;
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"ConvertFile error for '{filePath}': {ex.Message}");
                CleanupTempFile(tempPath);
                return false;
            }
        }

        private void CleanupTempFile(string tempPath)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best effort cleanup */ }
        }
    }

    public class ConversionResult
    {
        public int TotalFiles { get; set; }
        public int SuccessCount;
        public int FailureCount;
        public List<string> FailedFiles { get; set; } = new List<string>();
        public long TotalOriginalBytes;
        public long TotalNewBytes;
        public bool IsComplete { get; set; }

        public string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            return $"{bytes / 1024.0:F1} KB";
        }
    }
}
```

**Step 2: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 3: Create ConversionDialogHandler

**Files:**
- Create: `src/Handlers/ConversionDialogHandler.cs`

**Step 1: Create the handler**

Follow the exact pattern of `NormalizationDialogHandler`. Constructor takes `IPlayniteAPI`, `AudioConversionService`, `IMusicPlaybackService`, `GameMusicFileService`, `Func<UniPlaySongSettings>`.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using UniPlaySong.Common;
using UniPlaySong.Services;

namespace UniPlaySong.Handlers
{
    public class ConversionDialogHandler
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI _playniteApi;
        private readonly AudioConversionService _conversionService;
        private readonly IMusicPlaybackService _playbackService;
        private readonly GameMusicFileService _fileService;
        private readonly Func<UniPlaySongSettings> _settingsProvider;

        public ConversionDialogHandler(
            IPlayniteAPI playniteApi,
            AudioConversionService conversionService,
            IMusicPlaybackService playbackService,
            GameMusicFileService fileService,
            Func<UniPlaySongSettings> settingsProvider)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _conversionService = conversionService;
            _playbackService = playbackService;
            _fileService = fileService;
            _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        }

        public async void ConvertAllMusicFiles()
        {
            try
            {
                var settings = _settingsProvider();

                if (_conversionService == null)
                {
                    _playniteApi.Dialogs.ShowErrorMessage("Conversion service not available.", "UniPlaySong");
                    return;
                }

                if (string.IsNullOrWhiteSpace(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        "FFmpeg path is not configured. Please configure FFmpeg in Settings > Audio Editing.",
                        "FFmpeg Not Configured");
                    return;
                }

                if (!_conversionService.ValidateFFmpegAvailable(settings.FFmpegPath))
                {
                    _playniteApi.Dialogs.ShowErrorMessage(
                        $"FFmpeg not found or not accessible at: {settings.FFmpegPath}\n\nPlease verify the FFmpeg path in extension settings.",
                        "FFmpeg Not Available");
                    return;
                }

                // Stop music playback to prevent file locking
                await StopPlaybackForProcessingAsync("bulk conversion");

                // Collect all music files
                var allMusicFiles = new List<string>();
                foreach (var game in _playniteApi.Database.Games)
                {
                    var gameFiles = _fileService?.GetAvailableSongs(game) ?? new List<string>();
                    allMusicFiles.AddRange(gameFiles);
                }

                if (allMusicFiles.Count == 0)
                {
                    _playniteApi.Dialogs.ShowMessage("No music files found in your library.", "No Files to Convert");
                    return;
                }

                var format = settings.ConversionTargetFormat.ToUpperInvariant();
                var bitrate = settings.ConversionBitrate;

                var confirmed = _playniteApi.Dialogs.ShowMessage(
                    $"This will convert {allMusicFiles.Count} files to {format} {bitrate}kbps.\n\n" +
                    (settings.ConversionKeepOriginals
                        ? "Original files will be kept as backups (-preconvert suffix)."
                        : "Original files will be DELETED after successful conversion.") +
                    "\n\nContinue?",
                    "Confirm Bulk Conversion",
                    System.Windows.MessageBoxButton.YesNo);

                if (confirmed != System.Windows.MessageBoxResult.Yes) return;

                ShowConversionProgress(allMusicFiles, settings);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in ConvertAllMusicFiles");
                _playniteApi.Dialogs.ShowErrorMessage($"Error starting conversion: {ex.Message}", "Conversion Error");
            }
        }

        private void ShowConversionProgress(List<string> musicFiles, UniPlaySongSettings settings)
        {
            try
            {
                var progressDialog = new Views.NormalizationProgressDialog();
                var window = DialogHelper.CreateStandardDialog(
                    _playniteApi,
                    $"Converting to {settings.ConversionTargetFormat.ToUpperInvariant()} {settings.ConversionBitrate}kbps",
                    progressDialog,
                    width: 600,
                    height: 500);

                DialogHelper.AddFocusReturnHandler(window, _playniteApi, "conversion dialog close");

                Task.Run(async () =>
                {
                    try
                    {
                        var progress = new Progress<Models.NormalizationProgress>(p =>
                        {
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                progressDialog.ReportProgress(p);
                            }));
                        });

                        var result = await _conversionService.ConvertBulkAsync(
                            musicFiles,
                            settings.FFmpegPath,
                            settings.ConversionTargetFormat,
                            settings.ConversionBitrate,
                            settings.ConversionKeepOriginals,
                            progress,
                            progressDialog.CancellationToken);

                        // Invalidate cache for affected directories
                        if (result.SuccessCount > 0 && _fileService != null)
                        {
                            var directories = musicFiles.Select(f => Path.GetDirectoryName(f)).Distinct();
                            foreach (var dir in directories)
                            {
                                _fileService.InvalidateCacheForDirectory(dir);
                            }
                        }

                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();

                            var saved = result.TotalOriginalBytes - result.TotalNewBytes;
                            var savedPercent = result.TotalOriginalBytes > 0
                                ? (saved * 100.0 / result.TotalOriginalBytes)
                                : 0;

                            var message = $"Conversion Complete!\n\n" +
                                        $"Converted: {result.SuccessCount} | Failed: {result.FailureCount} | Total: {result.TotalFiles}\n\n" +
                                        $"Original size: {result.FormatBytes(result.TotalOriginalBytes)}\n" +
                                        $"New size: {result.FormatBytes(result.TotalNewBytes)}\n" +
                                        $"Space saved: {result.FormatBytes(Math.Abs(saved))} ({Math.Abs(savedPercent):F0}%)";

                            if (saved < 0)
                            {
                                message = message.Replace("Space saved:", "Space increased:");
                            }

                            if (result.FailedFiles.Count > 0)
                            {
                                message += $"\n\nFailed files:\n{string.Join("\n", result.FailedFiles.Take(5).Select(f => Path.GetFileName(f)))}";
                                if (result.FailedFiles.Count > 5)
                                    message += $"\n... and {result.FailedFiles.Count - 5} more";
                            }

                            _playniteApi.Dialogs.ShowMessage(message, "Conversion Complete");
                        }));
                    }
                    catch (OperationCanceledException)
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            _playniteApi.Dialogs.ShowMessage("Conversion was cancelled.", "Conversion Cancelled");
                        }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error during conversion");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            window.Close();
                            _playniteApi.Dialogs.ShowErrorMessage($"Error during conversion: {ex.Message}", "Conversion Error");
                        }));
                    }
                });

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing conversion progress dialog");
                _playniteApi.Dialogs.ShowErrorMessage($"Error showing progress dialog: {ex.Message}", "Conversion Error");
            }
        }

        private async Task StopPlaybackForProcessingAsync(string context)
        {
            try
            {
                if (_playbackService != null && _playbackService.IsPlaying)
                {
                    Logger.Debug($"Stopping music playback before {context}");
                    _playbackService.Stop();
                    await Task.Delay(300);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error stopping playback before {context}");
            }
        }
    }
}
```

**Step 2: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 4: Wire Up in UniPlaySong.cs

**Files:**
- Modify: `src/UniPlaySong.cs`

**Step 1: Add field**

After `_normalizationDialogHandler` field declaration, add:

```csharp
private Handlers.ConversionDialogHandler _conversionDialogHandler;
```

**Step 2: Instantiate in InitializeServices**

After the `_normalizationDialogHandler = new ...` line (~line 2196), add:

```csharp
var conversionService = new Services.AudioConversionService(_fileLogger);
_conversionDialogHandler = new Handlers.ConversionDialogHandler(
    _api, conversionService, _playbackService, _fileService, () => _settings);
```

**Step 3: Add public accessor method**

After the existing `NormalizeAllMusicFiles()` method (~line 2656), add:

```csharp
public void ConvertAllMusicFiles()
{
    _conversionDialogHandler?.ConvertAllMusicFiles();
}
```

**Step 4: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 5: Add ViewModel Command

**Files:**
- Modify: `src/UniPlaySongSettingsViewModel.cs`

**Step 1: Add command**

After the `TrimAllMusicCommand` (~line 570), add:

```csharp
public ICommand ConvertAllMusicCommand => new Common.RelayCommand<object>((a) =>
{
    var errorHandler = plugin.GetErrorHandlerService();
    errorHandler?.Try(
        () =>
        {
            plugin.ConvertAllMusicFiles();
        },
        context: "converting all music files",
        showUserMessage: true
    );
});
```

**Step 2: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 6: Add Settings UI

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml`

**Step 1: Add conversion controls**

After the "Create Folders for All Games" StackPanel (~line 2515), before the `<Separator>`, add:

```xml
<Separator Margin="0,15"/>
<TextBlock Text="Format Conversion" FontSize="14" FontWeight="SemiBold" Margin="0,10,0,10"/>

<StackPanel Orientation="Horizontal" Margin="0,5">
    <TextBlock Text="Target Format:" VerticalAlignment="Center" Width="110"/>
    <ComboBox Width="80" Margin="0,0,15,0"
              SelectedValue="{Binding Settings.ConversionTargetFormat}"
              SelectedValuePath="Tag">
        <ComboBoxItem Content="OGG" Tag="ogg"/>
        <ComboBoxItem Content="MP3" Tag="mp3"/>
    </ComboBox>
    <TextBlock Text="Quality:" VerticalAlignment="Center" Margin="0,0,5,0"/>
    <ComboBox Width="100" Margin="0,0,15,0"
              SelectedValue="{Binding Settings.ConversionBitrate}"
              SelectedValuePath="Tag">
        <ComboBoxItem Content="128 kbps" Tag="128"/>
        <ComboBoxItem Content="192 kbps" Tag="192"/>
        <ComboBoxItem Content="256 kbps" Tag="256"/>
    </ComboBox>
    <CheckBox Content="Keep originals as backup"
              IsChecked="{Binding Settings.ConversionKeepOriginals}"
              VerticalAlignment="Center"/>
</StackPanel>

<StackPanel Orientation="Horizontal" Margin="0,10">
    <Button Content="Convert All Music Files"
            Width="220"
            Margin="0,0,10,0"
            Command="{Binding ConvertAllMusicCommand}"/>
    <TextBlock Text="Convert all music files in your library to the selected format and quality."
               VerticalAlignment="Center"
               FontSize="11"
               Foreground="Gray"
               TextWrapping="Wrap"/>
</StackPanel>
```

**Step 2: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 7: Update Reset Handler

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml.cs`

**Step 1: Add conversion defaults to ResetEditingTab_Click**

In `ResetEditingTab_Click()` (~line 220), before `ShowButtonFeedback`, add:

```csharp
s.ConversionTargetFormat = "ogg";
s.ConversionBitrate = "192";
s.ConversionKeepOriginals = false;
```

**Step 2: Build**

```bash
dotnet clean -c Release && dotnet build -c Release
```

---

### Task 8: Manual Testing

**Step 1: Package**

```bash
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

**Step 2: Test in Playnite**

- Verify dropdowns appear in Settings > Editing > Bulk Actions
- Verify format/quality selections persist
- Test conversion on a small set of files
- Verify backup behavior (on/off)
- Verify progress dialog shows correctly
- Verify completion message with file size report
- Test cancellation mid-conversion
- Verify failed file reporting
- Test reset button clears conversion settings

---

### Task 9: Update Documentation

**Files:**
- Modify: `docs/dev_docs/ARCHITECTURE.md` — add conversion service section
- Modify: `CHANGELOG.md` — add v1.3.10 entry for bulk conversion
- Modify: `Manifest/installer.yaml` — add changelog item
- Modify: `README.md` — add to What's New
