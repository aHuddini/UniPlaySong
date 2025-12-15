# Log Analysis Quick Reference

## Critical Events to Track

### 1. OnApplicationStarted
**What it tells us**: When the extension initializes
**What to check**:
- Does it fire at the same time for both extensions?
- What is the initial `_firstSelect` value?
- What is the initial `VideoIsPlaying` value?

### 2. OnGameSelected
**What it tells us**: When a game is selected (this is the problematic event)
**What to check**:
- When does it fire relative to login screen?
- What is `_firstSelect` value at this moment?
- What is `SkipMusic` value?
- Does it call `PlayMusicBasedOnSelected()`?
- When is `_firstSelect` cleared?

### 3. VideoIsPlaying Changes
**What it tells us**: When videos are detected/ended
**What to check**:
- When is it first set to `true`?
- When does it change from `true` to `false`?
- Does this happen before or after `OnGameSelected`?

### 4. ShouldPlayMusic/ShouldPlayAudio
**What it tells us**: Whether music should actually play
**What to check**:
- What does it return?
- Why does it return that value?
- What are all the conditions being checked?

### 5. _firstSelect State Changes
**What it tells us**: When the first selection flag is cleared
**What to check**:
- When is it cleared?
- Is it cleared before or after `ShouldPlayAudio` checks it?
- Does the timing match PlayniteSound?

## Key Questions to Answer

### Question 1: Does OnGameSelected fire too early?
**Check**: Compare timing of first `OnGameSelected` vs when login screen ends
**Expected**: Should fire after login screen is passed
**If wrong**: Need to delay or skip the first selection

### Question 2: Is VideoIsPlaying initialized correctly?
**Check**: What is `VideoIsPlaying` value when `OnGameSelected` first fires?
**Expected**: Should be `true` if videos are playing, `false` otherwise
**If wrong**: Need to initialize it correctly or detect videos earlier

### Question 3: Is _firstSelect cleared at the right time?
**Check**: When is `_firstSelect` cleared relative to `ShouldPlayAudio` check?
**Expected**: Should be checked in `ShouldPlayAudio` before being cleared
**If wrong**: Need to delay clearing or check earlier

### Question 4: Does ShouldPlayMusic return the correct value?
**Check**: What does `ShouldPlayMusic` return for the first selection?
**Expected**: Should return `false` if `_firstSelect` is `true` and setting is enabled
**If wrong**: Check the logic in `ShouldPlayMusic` and `ShouldPlayAudio`

## Red Flags in Logs

### 🚩 Red Flag 1: OnGameSelected fires before VideoIsPlaying is set
```
[0ms] [UniPlaySong] OnGameSelected - ...
[50ms] [UniPlaySong] VideoIsPlaying changing from false to true
```
**Problem**: Video detection happens too late
**Fix**: Initialize VideoIsPlaying earlier or delay OnGameSelected

### 🚩 Red Flag 2: _firstSelect cleared before ShouldPlayAudio checks it
```
[0ms] [UniPlaySong] OnGameSelected - FirstSelect: true
[1ms] [UniPlaySong] OnGameSelected - Setting _firstSelect to false
[2ms] [UniPlaySong] ShouldPlayAudio - FirstSelect: false (already cleared!)
```
**Problem**: Flag cleared too early
**Fix**: Delay clearing until after ShouldPlayAudio check

### 🚩 Red Flag 3: ShouldPlayMusic returns true when it shouldn't
```
[0ms] [UniPlaySong] OnGameSelected - FirstSelect: true, SkipMusic: true
[1ms] [UniPlaySong] ShouldPlayMusic returned: true (WRONG!)
```
**Problem**: Logic error in ShouldPlayMusic
**Fix**: Check the conditions in ShouldPlayMusic/ShouldPlayAudio

### 🚩 Red Flag 4: VideoIsPlaying never changes
```
[0ms] [UniPlaySong] OnApplicationStarted
[100ms] [UniPlaySong] OnGameSelected
(No VideoIsPlaying changes)
```
**Problem**: MediaElementsMonitor not detecting videos
**Fix**: Check MediaElementsMonitor initialization and detection logic

## Comparison Checklist

When comparing UniPlaySong vs PlayniteSound logs:

- [ ] Do both extensions initialize at the same time?
- [ ] Do both have the same initial `_firstSelect` value?
- [ ] Do both detect videos at the same time?
- [ ] Do both fire `OnGameSelected` at the same time?
- [ ] Do both clear `_firstSelect` at the same time?
- [ ] Do both return the same `ShouldPlayMusic` result?
- [ ] Do both have the same event sequence?

If any answer is "no", that's likely the root cause!

## Example: Good Log Sequence

```
[0ms] [UniPlaySong] OnApplicationStarted - FirstSelect: true
[10ms] [UniPlaySong] MediaElementsMonitor: VideoIsPlaying changing to true
[500ms] [UniPlaySong] MediaElementsMonitor: VideoIsPlaying changing to false
[501ms] [UniPlaySong] OnGameSelected - FirstSelect: true, SkipMusic: true
[502ms] [UniPlaySong] ShouldPlayAudio - FirstSelect: true, SkipMusic: true, Result: false
[503ms] [UniPlaySong] ShouldPlayMusic returned: false
[504ms] [UniPlaySong] OnGameSelected - Setting _firstSelect to false
```

**This is good because**:
- VideoIsPlaying is set before OnGameSelected
- _firstSelect is checked before being cleared
- ShouldPlayMusic correctly returns false
- Music doesn't play

## Example: Bad Log Sequence

```
[0ms] [UniPlaySong] OnApplicationStarted - FirstSelect: true
[10ms] [UniPlaySong] OnGameSelected - FirstSelect: true, SkipMusic: true
[11ms] [UniPlaySong] OnGameSelected - Setting _firstSelect to false
[12ms] [UniPlaySong] ShouldPlayAudio - FirstSelect: false (WRONG!), Result: true
[13ms] [UniPlaySong] ShouldPlayMusic returned: true
[14ms] [UniPlaySong] PlayMusicBasedOnSelected - Playing music (WRONG!)
[500ms] [UniPlaySong] MediaElementsMonitor: VideoIsPlaying changing to true
```

**This is bad because**:
- OnGameSelected fires before video detection
- _firstSelect is cleared before ShouldPlayAudio checks it
- ShouldPlayMusic incorrectly returns true
- Music plays during login screen

## Next Steps After Analysis

1. **Identify the root cause** from the logs
2. **Compare with PlayniteSound** to see how it handles it
3. **Implement the fix** based on the identified issue
4. **Re-test and re-analyze** to verify the fix works

