# Waveform Trim Feature - Implementation Plan

## Status: ✅ IMPLEMENTED

This feature has been fully implemented and tested. The documentation below reflects the final implementation.

## Overview

A **precise audio trimming capability** that allows users to visually select trim points using an interactive waveform editor. **Fully usable in Playnite's Fullscreen Mode with complete Xbox controller support** - no keyboard or mouse required.

## Final Decisions

| Decision | Value |
|----------|-------|
| Suffix | `-ptrimmed` |
| Original files | Keep in `PreservedOriginals/` folder |
| Preview behavior | Play the **kept portion** (start to end markers) |
| Zoom for V1 | Not needed |
| Fade edges | No - users hear pops in preview and can adjust |
| Menu location | Audio Processing subfolder |
| Folder structure | `WaveformTrim/` subfolder for related files |
| Already-trimmed files | Show with indicator, don't hide |

---

## File Structure

```
Models/WaveformTrim/
├── TrimWindow.cs           # Trim selection model (start/end times)
└── WaveformData.cs         # Waveform samples for display

Services/
├── WaveformTrimService.cs  # NAudio waveform generation + FFmpeg trim
└── IWaveformTrimService.cs # Service interface

Views/
├── WaveformTrimDialog.xaml              # Desktop version
├── WaveformTrimDialog.xaml.cs
├── ControllerWaveformTrimDialog.xaml    # Controller version (multi-step)
└── ControllerWaveformTrimDialog.xaml.cs

Handlers/
└── WaveformTrimDialogHandler.cs         # Orchestration (like other handlers)
```

---

## Controller Scheme (Final Implementation)

The controller scheme was refined through user testing to provide an intuitive, console-like experience.

### File Selection Step (Step 1)
| Input | Action |
|-------|--------|
| D-Pad Up/Down | Navigate file list |
| A Button | Select file, proceed to waveform editor |
| B Button | Cancel/close dialog |

### Waveform Editing Step (Step 2)
| Input | Action |
|-------|--------|
| D-Pad Left | Move start marker earlier (0.5s steps) |
| D-Pad Right | Move start marker later (0.5s steps) |
| D-Pad Up | Move end marker later / extend (0.5s steps) |
| D-Pad Down | Move end marker earlier / shorten (0.5s steps) |
| Left Bumper (LB) | Contract window (move both edges inward) |
| Right Bumper (RB) | Expand window (move both edges outward) |
| A Button | Preview the kept portion |
| B Button | Go back to file selection |
| X Button | Apply trim and save |
| Y Button | Reset to full duration |

### Continuous D-Pad Navigation
When holding D-pad directions, markers move continuously:
- **Initial delay**: 200ms before continuous movement begins
- **Repeat interval**: 50ms for smooth, precise control
- **Fixed increments**: 0.5 second steps for predictable adjustments

This design ensures users can make both quick adjustments (tap) and fine-tuned positioning (hold).

---

## Menu Integration

### Fullscreen Mode
```
UniPlaySong
├── 🎮 Download Music
├── Primary Song/
├── Audio Processing/
│   ├── Normalize Music Folder
│   ├── Trim Leading Silence
│   └── 🎮 Precise Trim          <-- NEW
├── Manage Music/
└── 🖥️ PC Mode/
    └── Precise Trim             <-- NEW (desktop dialog)
```

### Desktop Mode
```
UniPlaySong
├── Download Music
├── ...
├── Normalize Music Folder
├── Trim Leading Silence
├── Precise Trim                 <-- NEW
├── ...
└── 🎮 Controller Mode/
    └── 🎮 Precise Trim          <-- NEW
```

---

## Technical Details

### WaveformTrimService

```csharp
public interface IWaveformTrimService
{
    /// <summary>
    /// Generate waveform data for display (~1000 samples)
    /// </summary>
    Task<WaveformData> GenerateWaveformAsync(string audioFilePath, CancellationToken token);

    /// <summary>
    /// Apply trim using FFmpeg, save to output path
    /// Original moved to PreservedOriginals folder
    /// </summary>
    Task<bool> ApplyTrimAsync(string inputPath, TrimWindow trimWindow, string suffix, CancellationToken token);

    /// <summary>
    /// Get audio duration for calculating trim positions
    /// </summary>
    Task<TimeSpan> GetAudioDurationAsync(string audioFilePath);

    /// <summary>
    /// Validate FFmpeg is available
    /// </summary>
    bool ValidateFFmpegAvailable(string ffmpegPath);
}
```

### TrimWindow Model

```csharp
namespace UniPlaySong.Models.WaveformTrim
{
    public class TrimWindow
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public TimeSpan TotalDuration { get; set; }

        // Percentage-based for UI binding
        public double StartPercent => TotalDuration.TotalMilliseconds > 0
            ? StartTime.TotalMilliseconds / TotalDuration.TotalMilliseconds * 100
            : 0;
        public double EndPercent => TotalDuration.TotalMilliseconds > 0
            ? EndTime.TotalMilliseconds / TotalDuration.TotalMilliseconds * 100
            : 100;

        public bool IsValid => StartTime >= TimeSpan.Zero
            && EndTime <= TotalDuration
            && StartTime < EndTime;

        public static TrimWindow FullDuration(TimeSpan totalDuration)
        {
            return new TrimWindow
            {
                StartTime = TimeSpan.Zero,
                EndTime = totalDuration,
                TotalDuration = totalDuration
            };
        }
    }
}
```

### WaveformData Model

```csharp
namespace UniPlaySong.Models.WaveformTrim
{
    public class WaveformData
    {
        /// <summary>
        /// Normalized samples (-1.0 to 1.0) for display
        /// Approximately 1000 samples for smooth rendering
        /// </summary>
        public float[] Samples { get; set; }

        /// <summary>
        /// Total duration of the audio file
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Sample rate of source audio
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// Source file path
        /// </summary>
        public string FilePath { get; set; }
    }
}
```

### Preview Integration

The preview will use the existing `IMusicPlaybackService` without modifying it. The dialog will:

1. Call `_playbackService.Stop()` to stop current playback
2. Load the file and seek to `TrimWindow.StartTime`
3. Start a timer to stop playback at `TrimWindow.EndTime`
4. On dialog close, restore normal playback behavior

```csharp
private async void PreviewTrimmedAudio()
{
    if (_trimWindow == null || !_trimWindow.IsValid) return;

    _playbackService.Stop();

    // Use SDL2 player's seek capability
    _playbackService.PlayFile(_currentFilePath);
    _playbackService.SeekTo(_trimWindow.StartTime);

    // Set up stop timer
    var previewDuration = _trimWindow.Duration;
    _previewStopTimer = new DispatcherTimer
    {
        Interval = previewDuration
    };
    _previewStopTimer.Tick += (s, e) =>
    {
        _playbackService.Stop();
        _previewStopTimer.Stop();
    };
    _previewStopTimer.Start();
}
```

---

## WPF Waveform Rendering

### Approach
Use a WPF `Canvas` with a `Polyline` for the waveform and `Rectangle` elements for the trim window overlay.

```xaml
<Canvas x:Name="WaveformCanvas" Background="#1a1a1a">
    <!-- Waveform line -->
    <Polyline x:Name="WaveformLine"
              Stroke="LimeGreen"
              StrokeThickness="1"/>

    <!-- Excluded region (left of start) -->
    <Rectangle x:Name="ExcludedLeft"
               Fill="#80000000"/>

    <!-- Excluded region (right of end) -->
    <Rectangle x:Name="ExcludedRight"
               Fill="#80000000"/>

    <!-- Trim window (kept region) -->
    <Rectangle x:Name="TrimWindowRect"
               Fill="#2000FF00"
               Stroke="LimeGreen"
               StrokeThickness="2"/>

    <!-- Start marker -->
    <Rectangle x:Name="StartMarker"
               Width="4"
               Fill="Blue"
               Cursor="SizeWE"/>

    <!-- End marker -->
    <Rectangle x:Name="EndMarker"
               Width="4"
               Fill="Red"
               Cursor="SizeWE"/>

    <!-- Playhead (during preview) -->
    <Line x:Name="Playhead"
          Stroke="Yellow"
          StrokeThickness="2"
          Visibility="Collapsed"/>
</Canvas>
```

### Mouse Interaction (Desktop)
- Click and drag start marker (blue) to adjust start time
- Click and drag end marker (red) to adjust end time
- Click and drag the trim window (green area) to move both markers together
- Double-click inside trim window to preview

---

## NAudio Dependency

Add to project via NuGet:
```
NAudio 2.2.1 (or latest stable)
```

NAudio is well-maintained, compatible with .NET Framework 4.6.2+ (Playnite's target), and provides:
- `AudioFileReader` for reading various audio formats
- Sample data access for waveform generation
- Duration detection

---

## Implementation Phases

### Phase 1: Core Infrastructure ✅
- [x] Add NAudio NuGet package
- [x] Create `Models/WaveformTrim/` folder with TrimWindow.cs and WaveformData.cs
- [x] Create `IWaveformTrimService` and `WaveformTrimService`
- [x] Implement waveform generation (NAudio sample extraction + downsampling)
- [x] Implement FFmpeg trim with PreservedOriginals backup

### Phase 2: Desktop UI ✅
- [x] Create `WaveformTrimDialog.xaml` with Canvas-based waveform display
- [x] Implement mouse interaction (drag markers, drag window)
- [x] Add preview functionality using existing playback service
- [x] Create `WaveformTrimDialogHandler` for orchestration
- [x] Add menu item to Desktop mode

### Phase 3: Controller UI ✅
- [x] Create `ControllerWaveformTrimDialog.xaml` (multi-step: file selection + waveform)
- [x] Implement XInput handling with debouncing
- [x] Implement continuous D-pad navigation (hold to move continuously)
- [x] Add visual controller hints and audio feedback
- [x] Add menu item to Fullscreen mode
- [x] Refine controller scheme based on user testing (intuitive D-pad mapping)

### Phase 4: Polish ✅
- [x] Add loading indicator during waveform generation
- [x] Add file info display (duration, format, size)
- [x] Add indicator for already-trimmed files (`-ptrimmed` suffix)
- [x] Update ARCHITECTURE.md documentation
- [x] Test with various audio formats (MP3, FLAC, OGG, WAV)
- [x] Fix preview to seek to start marker position
- [x] Fix FFmpeg path resolution for trim operations

---

## Settings

Add to `UniPlaySongSettings.cs`:
```csharp
public string PreciseTrimSuffix { get; set; } = "-ptrimmed";
```

---

## Error Handling

- **No FFmpeg**: Show error dialog with path configuration instructions
- **Unsupported format**: NAudio handles most formats; show friendly error if not
- **File locked**: Stop playback before trim operation
- **Trim too short**: Validate minimum duration (e.g., 1 second)
- **Out of disk space**: Check before creating trimmed file

---

## Notes

- This feature is distinct from "Trim Leading Silence" which is automatic
- "Precise Trim" gives users full control over what to keep
- The waveform helps users visually identify intro/outro sections to remove
- **Full controller support** makes this usable from the couch during gaming sessions
- Works seamlessly in **Playnite's Fullscreen Mode** - no keyboard/mouse required
- The intuitive D-pad mapping mirrors standard audio editing conventions:
  - Left/Right controls the start (blue) marker
  - Up/Down controls the end (red) marker
  - LB/RB expand or contract the window symmetrically
