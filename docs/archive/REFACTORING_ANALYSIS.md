# Refactoring Analysis - Logging & Path Usage

**Date**: 2025-12-07  
**Purpose**: Analyze logging patterns and path usage to determine next refactoring steps

---

## 📊 Logging Pattern Analysis

### Current Logging Patterns

**Pattern 1: Static Readonly Logger** (Most Common - 10+ files)
```csharp
private static readonly ILogger Logger = LogManager.GetLogger();
// Usage: Logger.Info(), Logger.Error()
```
**Files Using This**:
- `MusicPlaybackService.cs`
- `MusicFader.cs`
- `DownloadManager.cs`
- `GameMusicFileService.cs`
- `SDL2MusicPlayer.cs`
- `MusicPlayer.cs`
- `YouTubeDownloader.cs`
- `KHInsiderDownloader.cs`
- `YouTubeClient.cs`
- `WindowMonitor.cs`

**Pattern 2: Instance Logger (lowercase)** (2 files)
```csharp
private static readonly ILogger logger = LogManager.GetLogger();
// Usage: logger.Info(), logger.Error()
```
**Files Using This**:
- `UniPlaySong.cs`
- `MediaElementsMonitor.cs`

**Pattern 3: Instance Logger (underscore)** (2 files)
```csharp
private readonly ILogger _logger;
// Usage: _logger.Info(), _logger.Error()
```
**Files Using This**:
- `MusicPlaybackCoordinator.cs`
- `GameMenuHandler.cs`

**Pattern 4: FileLogger** (3 files)
```csharp
private readonly FileLogger _fileLogger;
// Usage: _fileLogger?.Info(), _fileLogger?.Error()
```
**Files Using This**:
- `UniPlaySong.cs`
- `MusicPlaybackService.cs`
- `MusicPlaybackCoordinator.cs`

### Logging Pattern Issues

1. **Inconsistent Naming**:
   - `Logger` (static, uppercase) vs `logger` (static, lowercase) vs `_logger` (instance)
   - No clear convention

2. **Dual Logging**:
   - Some files use only Playnite's `ILogger`
   - Some files use only `FileLogger`
   - Some files use both (MusicPlaybackService, UniPlaySong, MusicPlaybackCoordinator)

3. **Log Level Inconsistency**:
   - Mix of `Info()`, `Debug()`, `Warn()`, `Error()`
   - Some areas use `Debug()` for detailed info, others use `Info()`

### Recommendation: Logging Consistency

**Priority**: ⭐⭐⭐ **HIGH** - Significant value, low risk

**Strategy**:
1. **Standardize Naming**: Use `_logger` (instance) or `Logger` (static) consistently
2. **Dual Logging Pattern**: Keep both loggers where needed (playback services)
3. **Log Level Guidelines**:
   - `Debug()`: Detailed technical info (file paths, state changes)
   - `Info()`: Important events (music playing, game selected)
   - `Warn()`: Recoverable issues (fallbacks, missing files)
   - `Error()`: Exceptions and failures

**Files to Update** (in order):
1. Non-critical: `DownloadManager.cs`, `GameMenuHandler.cs`
2. Services: `MusicPlaybackCoordinator.cs`, `GameMusicFileService.cs`
3. Core: `MusicPlaybackService.cs` (careful - working code)
4. Main: `UniPlaySong.cs`

---

## 📁 Path Usage Analysis

### Path Constructions Found

**1. UniPlaySong.cs** (Main Plugin)
- Extension directory: `Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)` (lines 57, 124)
- Base path: `Path.Combine(_api.Paths.ConfigurationPath, Constants.ExtraMetadataFolderName, Constants.ExtensionFolderName)` ✅ **Already uses Constants**
- Games path: `Path.Combine(basePath, Constants.GamesFolderName)` ✅ **Already uses Constants**
- Temp path: `Path.Combine(basePath, Constants.TempFolderName)` ✅ **Already uses Constants**
- DLL/EXE paths: `Path.Combine(extensionPath, $"{assemblyName}.dll")` (assembly resolution)

**2. FileLogger.cs**
- Extension log path: `Path.Combine(extensionPath, "UniPlaySong.log")` (line 26)
- Playnite extensions: `Path.Combine(..., "Playnite", "Extensions")` (line 30)
- Fallback log: `Path.Combine(..., "Playnite", "UniPlaySong.log")` (line 42)
- **Hardcoded strings**: "Playnite", "Extensions", "UniPlaySong.log"

**3. GameMusicFileService.cs**
- Game directory: `Path.Combine(_baseMusicPath, gameId)` (line 39)
- **Already well-designed**: Takes base path in constructor

**4. DownloadManager.cs**
- Download directory: `Path.GetDirectoryName(downloadPath)` (line 130)
- Temp file: `Path.Combine(_tempPath, hash + extension)` (line 271)
- **Uses _tempPath from constructor** (already passed in)

**5. PrimarySongManager.cs**
- Metadata file: `Path.Combine(musicDirectory, PrimarySongFileName)` (lines 20, 46)
- Song path: `Path.Combine(musicDirectory, metadata.PrimarySongFileName)` (line 46)
- **Uses musicDirectory parameter** (already passed in)

**6. UniPlaySongSettingsViewModel.cs**
- Default music dir: `Path.Combine(extensionDataPath, "UniPlaySong", "DefaultMusic")` (line 151)
- **Hardcoded**: "UniPlaySong", "DefaultMusic"

**7. ViewModels/DownloadDialogViewModel.cs**
- Preview files: Uses temp path (passed in)
- **No direct path construction**

### Path Usage Summary

**Total Path Constructions**: ~15-20 instances

**Already Using Constants**:
- ✅ UniPlaySong.cs base/games/temp paths (3 paths)
- ✅ GameMusicFileService uses passed-in base path
- ✅ DownloadManager uses passed-in temp path
- ✅ PrimarySongManager uses passed-in music directory

**Hardcoded Path Strings**:
- ❌ FileLogger.cs: "Playnite", "Extensions", "UniPlaySong.log" (3 strings)
- ❌ UniPlaySongSettingsViewModel.cs: "UniPlaySong", "DefaultMusic" (2 strings)
- ❌ UniPlaySong.cs: Assembly resolution paths (2 paths, but dynamic)

**Path Constructions by Type**:
- **Configuration paths**: 3 (already using Constants) ✅
- **Extension directory**: 2 (assembly location - dynamic)
- **Log paths**: 3 (FileLogger fallbacks)
- **Default music path**: 1 (SettingsViewModel)
- **Game-specific paths**: Handled by GameMusicFileService ✅
- **Temp paths**: Handled by DownloadManager ✅

### PathService Recommendation

**Verdict**: ⚠️ **NOT NEEDED** - Low value for effort

**Reasoning**:
1. **Most paths already centralized**:
   - Game music paths → `GameMusicFileService` (well-designed)
   - Temp paths → `DownloadManager` (uses constructor parameter)
   - Configuration paths → Already using Constants ✅

2. **Remaining hardcoded paths are minimal**:
   - FileLogger: 3 strings (fallback logic, acceptable)
   - SettingsViewModel: 2 strings (one-time default music path)
   - Assembly resolution: Dynamic (can't be centralized)

3. **PathService would add little value**:
   - Would only centralize 5-6 hardcoded strings
   - Most path logic is already in appropriate services
   - Low return on investment

**Alternative**: Extract remaining hardcoded strings to Constants
- Add "DefaultMusic" folder name to Constants
- Consider adding log file name constant
- Keep FileLogger fallback logic as-is (it's working)

---

## 📋 Final Recommendations

### ✅ DO NEXT: Logging Consistency

**Priority**: High  
**Risk**: Low  
**Value**: High  
**Time**: 3-4 hours

**Action Plan**:
1. Standardize logger naming convention
2. Document dual-logging pattern (when to use both)
3. Standardize log levels
4. Update files incrementally (non-critical first)

### ❌ SKIP: PathService

**Reason**: 
- Most paths already well-organized
- Remaining hardcoded strings are minimal (5-6)
- Better to extract remaining strings to Constants if needed
- Low value for effort

**Alternative**: 
- Add "DefaultMusic" folder name to Constants (if we want)
- Keep FileLogger paths as-is (fallback logic is working)

### 📝 Next Steps After Logging

1. **Error Handler Service** (if needed)
2. **Re-evaluate PathService** (only if more path usage discovered)

---

**Last Updated**: 2025-12-07

