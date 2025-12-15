# Refactoring Priority Plan - UniPlaySong

**Date**: 2025-12-07  
**Goal**: Improve code quality while preserving all working functionality  
**Strategy**: Start with lowest-risk, highest-value improvements

---

## ⚠️ Critical: What's Already Working (DO NOT BREAK)

**Current Working Features** (must be preserved):
- ✅ Default music position preservation (single-player architecture)
- ✅ MusicPlaybackCoordinator (centralized playback decisions)
- ✅ Skip logic (first select, login skip, mode switch)
- ✅ Fade system (exponential curves, Switch() pattern)
- ✅ SDL2 audio player with WPF fallback
- ✅ Video pause/resume
- ✅ Primary song selection

**Key Working Components**:
- `MusicPlaybackCoordinator` - Already handles coordination (don't duplicate)
- `MusicPlaybackService` - Core playback logic (be very careful)
- `MusicFader` - Fade transitions (working perfectly, don't touch)

---

## 🟢 PHASE 1: Zero-Risk Improvements (Start Here)

### 1. Constants Class ⭐ **START HERE**
**Risk**: ⚠️ **ZERO** - Pure extraction, no logic changes  
**Value**: ⭐⭐⭐ High - Improves maintainability  
**Time**: 1-2 hours

**Why First**:
- No risk to existing functionality
- Makes code more readable
- Easy to test (just verify same values used)
- Foundation for other improvements

**What to Extract** (Based on Current Codebase):

```csharp
// From MusicPlaybackService.cs (lines 30-32)
private double _targetVolume = 0.5;  // → Constants.DefaultTargetVolume
private double _fadeInDuration = 0.5;  // → Constants.DefaultFadeInDuration
private double _fadeOutDuration = 0.3;  // → Constants.DefaultFadeOutDuration

// From UniPlaySong.cs (lines 254, 312) and MusicPlaybackService.cs (lines 138, 206)
settings.MusicVolume / 100.0;  // → Constants.VolumeDivisor (100.0)

// From GameMusicFileService.cs (line 20) - Already has array, but make it public constant
private static readonly string[] SupportedExtensions = { ".mp3", ".wav", ".flac", ".wma", ".aif", ".m4a", ".aac", ".mid" };
// → Constants.SupportedAudioExtensions (make public)

// From UniPlaySongSettings.cs (lines 15-16, 96)
fadeInDuration = 0.5;  // → Constants.DefaultFadeInDuration
fadeOutDuration = 0.3;  // → Constants.DefaultFadeOutDuration
musicVolume = 50;  // → Constants.DefaultMusicVolume
Math.Max(0, Math.Min(100, value));  // → Constants.MinMusicVolume (0), MaxMusicVolume (100)

// From UniPlaySongSettings.cs (validation - check if exists)
// MinFadeDuration = 0.05, MaxFadeDuration = 10.0 (if used in validation)

// From UniPlaySong.cs (line 457)
const string menuSection = "UniPSound";  // → Constants.MenuSectionName

// From UniPlaySong.cs (lines 289-291)
"ExtraMetadata"  // → Constants.ExtraMetadataFolderName
"UniPlaySong"   // → Constants.ExtensionFolderName
"Games"         // → Constants.GamesFolderName
"Temp"          // → Constants.TempFolderName

// From DownloadManager.cs (line 20)
MaxSongLength = 8 minutes  // → Constants.MaxPreviewSongLengthMinutes (8)
PreferredSongEndings = { "Theme", "Title", "Menu", "Main Theme" }  // → Constants.PreferredSongEndings
```

**Files to Create**:
- `UniPSong/Common/Constants.cs`

**Files to Modify**:
- `UniPSong/Services/MusicPlaybackService.cs` (3 constants)
- `UniPSong/Services/GameMusicFileService.cs` (make SupportedExtensions public, reference Constants)
- `UniPSong/UniPlaySong.cs` (volume divisor, path strings, menu section)
- `UniPSong/UniPlaySongSettings.cs` (default values)
- `UniPSong/Downloaders/DownloadManager.cs` (max song length, preferred endings)

**Testing**:
- Verify all fade durations work the same
- Verify volume calculations unchanged (test 0, 50, 100)
- Verify file extensions still recognized
- Verify menu items still appear
- Verify paths still resolve correctly

---

### 2. PathService (Minimal) ⭐ **SECOND** - **RE-EVALUATE**
**Risk**: ⚠️ **LOW** - Centralizes existing path logic  
**Value**: ⭐⭐ Medium - Limited benefit (only 3-4 path constructions)  
**Time**: 1-2 hours

**Current State Analysis**:
- Path construction is minimal (only in `UniPlaySong.InitializeServices()` lines 289-291)
- `GameMusicFileService` already handles game music paths well
- Extension directory detection is in static constructor (line 57)
- Log directory is extension directory (line 124)

**What to Centralize** (if worth it):
```csharp
// From UniPlaySong.cs - InitializeServices() (lines 289-291)
var basePath = Path.Combine(_api.Paths.ConfigurationPath, "ExtraMetadata", "UniPlaySong");
var gamesPath = Path.Combine(basePath, "Games");
var tempPath = Path.Combine(basePath, "Temp");

// Extension directory (line 57, 124) - already handled inline
```

**Recommendation**: 
- **CONSIDER DEFERRING** - Only 3 path constructions, all in one place
- `GameMusicFileService` constructor already takes base path
- Low value for effort
- **Only do if** Constants class extraction reveals more path usage

**If Proceeding**:
- `UniPSong/Services/PathService.cs` - Simple class with 3-4 methods
- `UniPSong/UniPlaySong.cs` - Use PathService in InitializeServices()
- Keep `GameMusicFileService` as-is (it's already well-designed)

---

## 🟡 PHASE 2: Low-Risk Improvements

### 3. Logging Consistency ⭐ **THIRD**
**Risk**: ⚠️ **LOW-MEDIUM** - Standardizing logging calls  
**Value**: ⭐⭐⭐ High - Better debugging  
**Time**: 3-4 hours

**Why Third**:
- Low risk (logging doesn't affect functionality)
- Improves debugging significantly
- Can be done incrementally

**Current State**:
- Mix of `Logger.Info()`, `_fileLogger?.Info()`, `logger.Info()`
- Some areas use Playnite's logger, others use FileLogger
- Inconsistent log levels

**Strategy**:
- Keep Playnite's `ILogger` for standard logs (required by Playnite)
- Standardize `FileLogger` usage for detailed debug logs
- Create wrapper if needed for consistency

**Files to Modify**:
- All service files (incremental, one at a time)
- Start with non-critical services first

**Testing**:
- Verify logs still appear in both places
- Verify no performance impact
- Test with logging enabled/disabled

---

### 4. Error Handler Service (Wrapper) ⭐ **FOURTH**
**Risk**: ⚠️ **LOW** - Wraps existing error handling  
**Value**: ⭐⭐ Medium - Better error messages  
**Time**: 2-3 hours

**Why Fourth**:
- Low risk if done as wrapper (not replacement)
- Can be added incrementally
- Improves user experience

**Strategy**:
- Create wrapper around existing logging
- Don't replace existing try-catch blocks initially
- Add user-friendly error messages gradually

**Files to Create**:
- `UniPSong/Services/ErrorHandlerService.cs` (wrapper)

**Files to Modify**:
- Start with non-critical areas (downloads, file operations)
- **DO NOT** touch playback service error handling initially

**Testing**:
- Verify errors still logged correctly
- Verify user sees friendly messages
- Test error scenarios

---

## 🟠 PHASE 3: Medium-Risk (Requires Careful Planning)

### 5. Service Separation ⚠️ **NOT NEEDED** - **SKIP**
**Risk**: ⚠️ **MEDIUM-HIGH** - Touches core logic  
**Value**: ⭐ Low - Already well-organized  
**Time**: 8-12 hours (wasted effort)

**✅ CURRENT STATE IS GOOD**:
- `UniPlaySong.cs` is 528 lines but **well-organized**:
  - Clear regions (Playnite Events, Music Playback, Event Handlers, Initialization, Settings & Menus, Public API)
  - Already delegates to `MusicPlaybackCoordinator` (line 170, 210, 221, 249, 269, 277)
  - Most logic is in services already
  - Path construction is minimal (3 lines)
  - Settings reload is straightforward (35 lines)
  
- **Services Already Exist**:
  - ✅ `MusicPlaybackCoordinator` - Handles all playback decisions
  - ✅ `MusicPlaybackService` - Core playback logic
  - ✅ `GameMusicFileService` - File management
  - ✅ `DownloadDialogService` - Download UI
  - ✅ `GameMenuHandler` / `MainMenuHandler` - Menu logic

**Recommendation**: 
- **SKIP THIS** - Code is already well-organized
- 528 lines is reasonable for a plugin entry point
- Focus effort on Constants, Logging, Error Handling instead
- **DO NOT** create unnecessary services

---

### 6. Settings Service (Optional) ⚠️ **DEFER**
**Risk**: ⚠️ **MEDIUM** - Settings are working  
**Value**: ⭐⭐ Medium - Nice to have  
**Time**: 4-6 hours

**Why Defer**:
- Settings system is working
- `OnSettingsSaved()` logic is straightforward
- Not causing problems

**Consider If**:
- Settings validation becomes complex
- Need event-driven settings updates
- Multiple components need settings notifications

---

## 🔴 PHASE 4: High-Risk / Future Considerations

### 7. Caching Improvements
**Risk**: ⚠️ **MEDIUM** - Could affect download behavior  
**Value**: ⭐⭐ Medium - Performance improvement  
**Status**: Defer until needed

**Consider When**:
- Users report slow downloads
- API rate limiting becomes issue
- Large libraries cause performance problems

---

### 8. String Operations Optimization
**Risk**: ⚠️ **LOW** - Performance only  
**Value**: ⭐ Low - Profile first  
**Status**: Defer - Profile before optimizing

**Recommendation**: Only if profiling shows string operations as bottleneck

---

## 📋 Recommended Execution Order

### Immediate (This Week)
1. ✅ **Constants Class** - Zero risk, high value, ~1-2 hours
   - Extract all magic numbers and strings
   - 6-8 files to modify
   - Easy to test and verify

### Short Term (Next 1-2 Weeks)
2. ✅ **Logging Consistency** - Low risk, high value, ~3-4 hours
   - Standardize logging patterns
   - Keep both Playnite logger and FileLogger
   - Incremental changes

3. ✅ **Error Handler Service (Wrapper)** - Low risk, medium value, ~2-3 hours
   - Start with non-critical areas
   - Don't touch playback service initially

### Re-evaluate After Phase 1
4. ⚠️ **PathService** - Only if Constants extraction reveals more path usage
   - Currently only 3-4 path constructions
   - Low value for effort
   - **Skip if not needed**

### Skip / Defer
5. ❌ **Service Separation** - NOT NEEDED (already well-organized)
6. ⚠️ **Settings Service** - Only if complexity grows (currently fine)

### Long Term / Future
7. Caching (if needed)
8. String optimization (if profiling shows need)

---

## 🛡️ Safety Guidelines

### Before Each Refactoring:
1. ✅ Create backup (use `create_backup.ps1`)
2. ✅ Test current functionality thoroughly
3. ✅ Make small, incremental changes
4. ✅ Test after each change
5. ✅ Keep working features intact

### Red Flags (Stop and Re-evaluate):
- ❌ Any change to `MusicPlaybackService.PlayGameMusic()`
- ❌ Any change to `MusicFader` logic
- ❌ Any change to `MusicPlaybackCoordinator` skip logic
- ❌ Any change that affects fade transitions
- ❌ Any change that affects default music position preservation

### Safe Areas to Refactor:
- ✅ Constants extraction
- ✅ Path centralization (non-playback paths)
- ✅ Logging standardization
- ✅ Error message improvements
- ✅ Code organization (without logic changes)

---

## 📝 Notes

- **MusicPlaybackCoordinator already exists** - Don't duplicate this work
- **Single-player architecture is working** - Don't change it
- **Default music position preservation is complete** - Don't touch it
- **Fade system is proven** - Don't modify fade logic
- **Start small, test often** - Incremental improvements are safer

---

**Last Updated**: 2025-12-07  
**Next Review**: After Phase 1 completion

