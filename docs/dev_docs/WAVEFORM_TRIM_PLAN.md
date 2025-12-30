# Waveform Trim Feature - Revised Implementation Plan

## Overview

Add a **precise audio trimming capability** that allows users to visually select trim points using an interactive waveform editor. Supports both mouse/keyboard and Xbox controller input.

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

## Controller Scheme

```
FILE SELECTION STEP (Step 1):
  D-Pad Up/Down:      Navigate file list
  A Button:           Select file, proceed to waveform editor
  B Button:           Cancel/close dialog

WAVEFORM EDITING STEP (Step 2):
  D-Pad Left/Right:   Move BOTH markers together (shift window) - 2% steps
  D-Pad Up/Down:      Adjust window SIZE (edges move symmetrically) - 2% steps
  Left Bumper (LB):   Move START marker only (earlier/later)
  Right Bumper (RB):  Move END marker only (earlier/later)
  Left Trigger:       Fine-tune start marker (analog precision)
  Right Trigger:      Fine-tune end marker (analog precision)
  A Button:           Preview kept portion
  X Button:           Confirm and apply trim
  B Button:           Go back to file selection
  Y Button:           Reset to full duration
```

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

### Phase 1: Core Infrastructure
- [ ] Add NAudio NuGet package
- [ ] Create `Models/WaveformTrim/` folder with TrimWindow.cs and WaveformData.cs
- [ ] Create `IWaveformTrimService` and `WaveformTrimService`
- [ ] Implement waveform generation (NAudio sample extraction + downsampling)
- [ ] Implement FFmpeg trim with PreservedOriginals backup

### Phase 2: Desktop UI
- [ ] Create `WaveformTrimDialog.xaml` with Canvas-based waveform display
- [ ] Implement mouse interaction (drag markers, drag window)
- [ ] Add preview functionality using existing playback service
- [ ] Create `WaveformTrimDialogHandler` for orchestration
- [ ] Add menu item to Desktop mode

### Phase 3: Controller UI
- [ ] Create `ControllerWaveformTrimDialog.xaml` (multi-step: file selection + waveform)
- [ ] Implement XInput handling with debouncing
- [ ] Add analog trigger support for fine-tuning
- [ ] Add visual controller hints
- [ ] Add menu item to Fullscreen mode

### Phase 4: Polish
- [ ] Add loading indicator during waveform generation
- [ ] Add file info display (duration, format, size)
- [ ] Add indicator for already-trimmed files (`-ptrimmed` suffix)
- [ ] Update ARCHITECTURE.md documentation
- [ ] Test with various audio formats (MP3, FLAC, OGG, WAV)

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
- Controller support makes this usable from the couch during gaming sessions
