# Song End Fade-Out Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** When a song auto-advances (Radio Mode or RandomizeOnMusicEnd), fade the ending song out in the final N seconds instead of letting it play to full volume and cut off abruptly.

**Architecture:** A one-shot `DispatcherTimer` is scheduled at song start (inside the existing `MarkSongStart()` method) whenever the feature is enabled and auto-advance will occur. It fires N seconds before song end and calls `_fader.FadeOut()` — a new no-action fade that just ramps volume to zero, leaving `OnMediaEnded` to handle the actual advance as it already does. `TotalTime` is added to `IMusicPlayer` and implemented in both backends: NAudio via `_audioFile.TotalTime`, SDL2 via a new `Mix_MusicDuration` P/Invoke binding (SDL2_mixer 2.8.0 is bundled — supports this function).

**Tech Stack:** C# / .NET 4.6.2, WPF DispatcherTimer, NAudio AudioFileReader, SDL2_mixer 2.8.0 P/Invoke, existing MusicFader infrastructure.

---

## Task 1: Add `Mix_MusicDuration` P/Invoke to SDL2_mixer wrapper

**Files:**
- Modify: `src/Players/SDL/SDL_mixer.cs:46` (after `Mix_GetMusicPosition`)

**Step 1: Add the binding**

Open `src/Players/SDL/SDL_mixer.cs`. After the `Mix_GetMusicPosition` declaration (line 46), add:

```csharp
[DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
public static extern double Mix_MusicDuration(IntPtr music);
```

**Step 2: Build to verify no compile errors**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/Players/SDL/SDL_mixer.cs
git commit -m "feat: add Mix_MusicDuration P/Invoke binding to SDL2_mixer wrapper"
```

---

## Task 2: Add `TotalTime` to `IMusicPlayer` interface and both implementations

**Files:**
- Modify: `src/Services/IMusicPlayer.cs:16` (after `CurrentTime` property)
- Modify: `src/Services/NAudioMusicPlayer.cs:74` (after `CurrentTime` property)
- Modify: `src/Services/SDL2MusicPlayer.cs` (after `CurrentTime` property)

**Step 1: Add `TotalTime` to the interface**

In `src/Services/IMusicPlayer.cs`, add after line 16 (`TimeSpan? CurrentTime { get; }`):

```csharp
TimeSpan? TotalTime { get; }
```

**Step 2: Implement in NAudioMusicPlayer**

In `src/Services/NAudioMusicPlayer.cs`, add after line 74 (`public TimeSpan? CurrentTime => _audioFile?.CurrentTime;`):

```csharp
public TimeSpan? TotalTime => _audioFile?.TotalTime;
```

**Step 3: Implement in SDL2MusicPlayer**

First find the `CurrentTime` property in `src/Services/SDL2MusicPlayer.cs` (search for `CurrentTime`). Add `TotalTime` directly after it:

```csharp
public TimeSpan? TotalTime =>
    _music != IntPtr.Zero
        ? TimeSpan.FromSeconds(SDL2Mixer.Mix_MusicDuration(_music))
        : (TimeSpan?)null;
```

**Step 4: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git add src/Services/IMusicPlayer.cs src/Services/NAudioMusicPlayer.cs src/Services/SDL2MusicPlayer.cs
git commit -m "feat: add TotalTime to IMusicPlayer interface — NAudio via AudioFileReader, SDL2 via Mix_MusicDuration"
```

---

## Task 3: Add settings properties for the feature

**Files:**
- Modify: `src/UniPlaySongSettings.cs` — add 2 new properties near `RandomizeOnMusicEnd` (around line 1200)

**Step 1: Add backing fields and properties**

In `src/UniPlaySongSettings.cs`, after the `randomizeOnMusicEnd` backing field block (around line 1196), add:

```csharp
private bool fadeOutBeforeSongEnd = false;
private double fadeOutBeforeSongEndDuration = 3.0;

// Fade out the ending song N seconds before it finishes during auto-advance (Radio Mode or RandomizeOnMusicEnd).
// Only active when Radio Mode or Randomize on Music End is enabled. NAudio and SDL2 both supported.
public bool FadeOutBeforeSongEnd
{
    get => fadeOutBeforeSongEnd;
    set { fadeOutBeforeSongEnd = value; OnPropertyChanged(); }
}

// Duration in seconds of the pre-end fade-out (1–5s). Default: 3s.
public double FadeOutBeforeSongEndDuration
{
    get => fadeOutBeforeSongEndDuration;
    set { fadeOutBeforeSongEndDuration = Math.Max(1.0, Math.Min(5.0, value)); OnPropertyChanged(); }
}
```

**Step 2: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/UniPlaySongSettings.cs
git commit -m "feat: add FadeOutBeforeSongEnd settings properties"
```

---

## Task 4: Add `FadeOut()` method to MusicFader

The existing `FadeOutAndStop()` and `Pause()` both attach stop/pause actions to the fade completion. We need a pure volume fade-to-zero with no action — so `OnMediaEnded` fires naturally when SDL2/NAudio reaches silence.

**Files:**
- Modify: `src/Players/MusicFader.cs` — add after `FadeIn()` method (around line 258)

**Step 1: Add the method**

In `src/Players/MusicFader.cs`, add after the `FadeIn()` method:

```csharp
// Fades volume to zero with no stop/pause action.
// Used for pre-song-end fade: the player reaches natural EOF at vol=0,
// then OnMediaEnded fires normally to handle the auto-advance.
public void FadeOut()
{
    _isFadingOut = true;
    _pauseAction = null;
    _stopAction = null;
    _playAction = null;
    SnapshotFadeParams();
    EnsureTimer();
}
```

**Step 2: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/Players/MusicFader.cs
git commit -m "feat: add FadeOut() to MusicFader — pure volume-to-zero with no stop/pause action"
```

---

## Task 5: Wire the one-shot timer in MusicPlaybackService

The timer is created fresh each time `ScheduleSongEndFade()` is called and disposed on cancel. It is never created when the feature is disabled.

**Files:**
- Modify: `src/Services/MusicPlaybackService.cs`

**Step 1: Add the timer field**

In `MusicPlaybackService`, find the private fields near `_previewTimer` (around line 83). Add:

```csharp
private System.Windows.Threading.DispatcherTimer _songEndFadeTimer;
```

**Step 2: Add `ScheduleSongEndFade()` method**

Add this private method near `MarkSongStart()` (around line 1557):

```csharp
// Schedules a one-shot timer to fade out the current song before it ends.
// Only created when: FadeOutBeforeSongEnd is on AND auto-advance is active AND TotalTime is known.
private void ScheduleSongEndFade()
{
    CancelSongEndFade();

    if (_currentSettings?.FadeOutBeforeSongEnd != true)
        return;

    bool autoAdvanceActive = (_currentSettings.RadioModeEnabled && _isInRadioMode)
        || (_currentSettings.RandomizeOnMusicEnd && !_isCurrentSongDefaultMusic);
    if (!autoAdvanceActive)
        return;

    var totalTime = _musicPlayer?.TotalTime;
    double fadeDuration = _currentSettings.FadeOutBeforeSongEndDuration;
    double minSongLength = fadeDuration + 5.0; // don't bother on very short songs

    if (totalTime == null || totalTime.Value.TotalSeconds < minSongLength)
        return;

    double delay = totalTime.Value.TotalSeconds - fadeDuration;

    _songEndFadeTimer = new System.Windows.Threading.DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(delay)
    };
    _songEndFadeTimer.Tick += (s, e) =>
    {
        _songEndFadeTimer.Stop();
        _songEndFadeTimer = null;
        if (_musicPlayer?.IsActive == true && !_isPaused)
        {
            _fileLogger?.Debug($"[SongEndFade] Fading out {Path.GetFileName(_currentSongPath)} ({fadeDuration}s before end)");
            _fader.FadeOut();
        }
    };
    _songEndFadeTimer.Start();
    _fileLogger?.Debug($"[SongEndFade] Scheduled fade in {delay:F1}s for {Path.GetFileName(_currentSongPath)}");
}

private void CancelSongEndFade()
{
    if (_songEndFadeTimer != null)
    {
        _songEndFadeTimer.Stop();
        _songEndFadeTimer = null;
    }
}
```

**Step 3: Call `ScheduleSongEndFade()` from `MarkSongStart()`**

In `MarkSongStart()` (line 1558), add the call at the end:

```csharp
private void MarkSongStart()
{
    _songStartTime = DateTime.Now;

    if (_currentSettings?.EnablePreviewMode == true &&
        !_isPlayingDefaultMusic &&
        !_isCurrentSongDefaultMusic)
    {
        _previewTimer.Start();
        _fileLogger?.Debug($"Preview timer started: {Path.GetFileName(_currentSongPath)} ({_currentSettings.PreviewDuration}s)");
    }

    ScheduleSongEndFade(); // NEW
}
```

**Step 4: Cancel the timer wherever playback stops or switches**

Find `StopPreviewTimer()` call sites — every place that calls it should also call `CancelSongEndFade()`. Search for `StopPreviewTimer()` in `MusicPlaybackService.cs` and add `CancelSongEndFade();` on the next line at each call site. There are approximately 4 call sites (song switch stop action, FadeOutAndStop, Stop(), preview timer handler). Also cancel in `FadeOutAndStop()`:

```csharp
private void FadeOutAndStop()
{
    CancelSongEndFade(); // NEW — cancel pre-end timer before stopping
    bool playerActive = ...
```

**Step 5: Build to verify**

```bash
dotnet build -c Release
```
Expected: `Build succeeded. 0 Error(s)`

**Step 6: Commit**

```bash
git add src/Services/MusicPlaybackService.cs
git commit -m "feat: schedule one-shot fade-out before song end for auto-advance scenarios"
```

---

## Task 6: Add UI controls to Settings → Playback tab

The toggle and slider appear in the Playback tab, after the existing fade duration controls. They are always visible (not gated by Radio/Randomize mode) since the setting itself is the gate.

**Files:**
- Modify: `src/UniPlaySongSettingsView.xaml` — Playback tab, after fade duration section
- Modify: `src/UniPlaySongSettingsView.xaml.cs` — `ResetPlaybackTab_Click` handler

**Step 1: Add the XAML**

In `UniPlaySongSettingsView.xaml`, find the fade duration controls in the Playback tab (search for `FadeOutDuration` or `FadeInDuration`). Add the following block immediately after the fade duration section:

```xml
<!-- Song End Fade-Out -->
<CheckBox Content="Fade out before song ends (auto-advance only)"
          IsChecked="{Binding Settings.FadeOutBeforeSongEnd}"
          Margin="0,12,0,4"
          ToolTip="When Radio Mode or Randomize on Song End is active, fades the song out N seconds before it finishes naturally. Both SDL2 and NAudio supported." />
<StackPanel Orientation="Horizontal"
            Margin="20,0,0,0"
            IsEnabled="{Binding Settings.FadeOutBeforeSongEnd}">
    <TextBlock Text="Fade duration:" VerticalAlignment="Center" Margin="0,0,8,0"/>
    <Slider Minimum="1" Maximum="5"
            Value="{Binding Settings.FadeOutBeforeSongEndDuration}"
            Width="120" TickFrequency="0.5" IsSnapToTickEnabled="True"
            VerticalAlignment="Center"/>
    <TextBlock Text="{Binding Settings.FadeOutBeforeSongEndDuration, StringFormat='{}{0:F1}s'}"
               VerticalAlignment="Center" Margin="8,0,0,0" Foreground="Gray"/>
</StackPanel>
```

**Step 2: Update `ResetPlaybackTab_Click`**

In `src/UniPlaySongSettingsView.xaml.cs`, inside `ResetPlaybackTab_Click` (line 62), add after `s.RadioMusicSource = RadioMusicSource.FullLibrary;`:

```csharp
s.FadeOutBeforeSongEnd = false;
s.FadeOutBeforeSongEndDuration = 3.0;
```

**Step 3: Build and package**

```bash
dotnet clean -c Release && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```
Expected: `Build succeeded. 0 Error(s)` then `PACKAGE CREATED SUCCESSFULLY`

**Step 4: Manual test checklist**
- Enable `RandomizeOnMusicEnd` with multiple songs for a game. Enable `FadeOutBeforeSongEnd` at 3s. Let a short-ish song play to natural end — confirm it fades out in the last 3 seconds, then the next song fades in.
- Enable Radio Mode. Same test — confirm fade-out before end, next song fades in.
- Disable `FadeOutBeforeSongEnd` — confirm no fade (abrupt advance as before).
- Switch games mid-song — confirm the timer is cancelled (no spurious fade-out on the new song).
- Songs under 8s (5s guard + 3s fade) — confirm no timer is scheduled, song ends normally.
- SDL2 mode (Live Effects/Visualizer off) — confirm `TotalTime` works and fade fires correctly.
- NAudio mode (Live Effects or Visualizer on) — same.

**Step 5: Commit**

```bash
git add src/UniPlaySongSettingsView.xaml src/UniPlaySongSettingsView.xaml.cs
git commit -m "feat: add Fade Out Before Song End UI controls to Playback tab"
```

---

## Task 7: Update CHANGELOG and README for v1.3.6

**Files:**
- Modify: `CHANGELOG.md` — add `[1.3.6]` section at top
- Modify: `README.md` — update badge and "What's New" section

**CHANGELOG entry:**

```markdown
## [1.3.6] - 2026-03-09

### Added
- **Fade Out Before Song End** — When Radio Mode or Randomize on Song End is active, a configurable fade-out (1–5s, default 3s) starts before the song finishes naturally, creating a smooth DJ-style transition instead of an abrupt cut. Works with both SDL2 and NAudio backends. Setting: Playback tab.
```

**README "What's New" entry** (replace current v1.3.5 block with v1.3.6):

```markdown
## What's New - v1.3.6

- [New Feature] **Fade Out Before Song End** — When using Radio Mode or Randomize on Song End, music now fades out smoothly in the final seconds before auto-advancing to the next song. Configurable duration (1–5s). Works in both SDL2 and NAudio modes.

### Previous Version
- **v1.3.5**: Open Music Folder create prompt, Bulk folder creation, Game folder breadcrumbs, Game index file, Open Game Index button, Localization infrastructure
```

**Step 1: Commit docs**

```bash
git add CHANGELOG.md README.md
git commit -m "docs: update CHANGELOG and README for v1.3.6 song end fade-out"
```

---

## Verification

After all tasks complete, run the full build and verify:

```bash
dotnet clean -c Release && dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/package_extension.ps1
```

Grep sanity checks:
```bash
# Timer should only be created inside ScheduleSongEndFade
grep -n "_songEndFadeTimer = new" src/Services/MusicPlaybackService.cs

# FadeOut() should only be called from the timer tick
grep -n "_fader.FadeOut()" src/Services/MusicPlaybackService.cs

# Mix_MusicDuration should appear exactly once
grep -n "Mix_MusicDuration" src/Players/SDL/SDL_mixer.cs src/Services/SDL2MusicPlayer.cs
```
