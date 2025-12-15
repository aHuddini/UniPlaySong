# Settings Service - Detailed Analysis

**Date**: 2025-12-07  
**Status**: Proposed (Not Yet Implemented)  
**Priority**: Medium (Deferred)  
**Estimated Time**: 4-6 hours

---

## Current State Analysis

### How Settings Are Currently Used

**1. Settings Storage:**
- `UniPlaySong.cs` holds `_settings` as a private field
- Settings are loaded via `LoadPluginSettings<UniPlaySongSettings>()`
- Settings are saved via Playnite's `ISettings` interface

**2. Settings Access Patterns:**

**Pattern A: Direct Field Access** (Most Common)
```csharp
// In UniPlaySong.cs
_settings.MusicVolume
_settings.EnableMusic
_settings.MusicState
```

**Pattern B: Passed as Parameter** (Services)
```csharp
// MusicPlaybackCoordinator receives settings in constructor
public MusicPlaybackCoordinator(
    IMusicPlaybackService playbackService,
    UniPlaySongSettings settings,  // <-- Passed in
    ILogger logger,
    ...
)

// DownloadDialogService receives settings in constructor
public DownloadDialogService(
    IPlayniteAPI playniteApi,
    IDownloadManager downloadManager,
    IMusicPlaybackService playbackService,
    GameMusicFileService fileService,
    UniPlaySongSettings settings  // <-- Passed in
)
```

**Pattern C: PropertyChanged Subscription**
```csharp
// In UniPlaySong.cs
_settings.PropertyChanged += OnSettingsChanged;

private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
{
    // Handle specific property changes (e.g., VideoIsPlaying)
}
```

**3. Settings Update Flow:**

**Current Flow:**
```
User saves settings in UI
  ↓
OnSettingsSaved() called
  ↓
Reload settings from disk
  ↓
Unsubscribe from old settings
  ↓
Update _settings reference
  ↓
Subscribe to new settings
  ↓
Manually update coordinator: _coordinator?.UpdateSettings(_settings)
  ↓
Manually update playback service: _playbackService.SetVolume(...)
```

**4. Settings Usage Count:**
- **83 references** across 4 files:
  - `UniPlaySong.cs`: 13 references
  - `MusicPlaybackCoordinator.cs`: 11 references
  - `DownloadDialogService.cs`: 9 references
  - `UniPlaySongSettingsViewModel.cs`: 50 references (UI binding)

---

## Proposed Settings Service

### Design

```csharp
namespace UniPlaySong.Services
{
    /// <summary>
    /// Centralized settings management service
    /// Provides single source of truth for settings access and change notifications
    /// </summary>
    public class SettingsService
    {
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private UniPlaySongSettings _currentSettings;
        
        public event EventHandler<SettingsChangedEventArgs> SettingsChanged;
        public event EventHandler<PropertyChangedEventArgs> SettingPropertyChanged;

        public UniPlaySongSettings Current => _currentSettings;

        public SettingsService(IPlayniteAPI api, ILogger logger)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Load initial settings
            LoadSettings();
        }

        /// <summary>
        /// Loads settings from disk
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                var plugin = _api.Addons.Plugins.OfType<UniPlaySong>().FirstOrDefault();
                if (plugin != null)
                {
                    var newSettings = plugin.LoadPluginSettings<UniPlaySongSettings>();
                    if (newSettings != null)
                    {
                        UpdateSettings(newSettings);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading settings");
            }
        }

        /// <summary>
        /// Updates settings and notifies subscribers
        /// </summary>
        public void UpdateSettings(UniPlaySongSettings newSettings)
        {
            if (newSettings == null)
                return;

            // Unsubscribe from old settings
            if (_currentSettings != null)
            {
                _currentSettings.PropertyChanged -= OnSettingPropertyChanged;
            }

            // Update reference
            var oldSettings = _currentSettings;
            _currentSettings = newSettings;

            // Subscribe to new settings
            _currentSettings.PropertyChanged += OnSettingPropertyChanged;

            // Notify subscribers
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(oldSettings, newSettings));
            
            _logger.Info("Settings updated");
        }

        /// <summary>
        /// Validates settings and returns validation errors
        /// </summary>
        public List<string> ValidateSettings(UniPlaySongSettings settings)
        {
            var errors = new List<string>();

            if (settings.MusicVolume < Constants.MinMusicVolume || 
                settings.MusicVolume > Constants.MaxMusicVolume)
            {
                errors.Add($"Music volume must be between {Constants.MinMusicVolume} and {Constants.MaxMusicVolume}");
            }

            if (settings.FadeInDuration < Constants.MinFadeDuration || 
                settings.FadeInDuration > Constants.MaxFadeDuration)
            {
                errors.Add($"Fade-in duration must be between {Constants.MinFadeDuration} and {Constants.MaxFadeDuration} seconds");
            }

            if (settings.FadeOutDuration < Constants.MinFadeDuration || 
                settings.FadeOutDuration > Constants.MaxFadeDuration)
            {
                errors.Add($"Fade-out duration must be between {Constants.MinFadeDuration} and {Constants.MaxFadeDuration} seconds");
            }

            // Validate file paths if provided
            if (!string.IsNullOrEmpty(settings.YtDlpPath) && !File.Exists(settings.YtDlpPath))
            {
                errors.Add($"yt-dlp path does not exist: {settings.YtDlpPath}");
            }

            if (!string.IsNullOrEmpty(settings.FFmpegPath) && !File.Exists(settings.FFmpegPath))
            {
                errors.Add($"ffmpeg path does not exist: {settings.FFmpegPath}");
            }

            if (settings.EnableDefaultMusic && 
                !string.IsNullOrEmpty(settings.DefaultMusicPath) && 
                !File.Exists(settings.DefaultMusicPath))
            {
                errors.Add($"Default music file does not exist: {settings.DefaultMusicPath}");
            }

            return errors;
        }

        /// <summary>
        /// Gets a specific setting value (type-safe accessor)
        /// </summary>
        public T GetSetting<T>(Func<UniPlaySongSettings, T> selector)
        {
            return selector(_currentSettings);
        }

        private void OnSettingPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SettingPropertyChanged?.Invoke(this, e);
        }
    }

    public class SettingsChangedEventArgs : EventArgs
    {
        public UniPlaySongSettings OldSettings { get; }
        public UniPlaySongSettings NewSettings { get; }

        public SettingsChangedEventArgs(UniPlaySongSettings oldSettings, UniPlaySongSettings newSettings)
        {
            OldSettings = oldSettings;
            NewSettings = newSettings;
        }
    }
}
```

---

## Benefits

### 1. Centralized Access
**Current:**
```csharp
// Settings passed around everywhere
public MusicPlaybackCoordinator(..., UniPlaySongSettings settings, ...)
public DownloadDialogService(..., UniPlaySongSettings settings)
```

**With SettingsService:**
```csharp
// Single service injected
public MusicPlaybackCoordinator(..., SettingsService settingsService, ...)
// Access via: settingsService.Current.MusicVolume
```

### 2. Event-Driven Updates
**Current:**
```csharp
// Manual updates in OnSettingsSaved()
_coordinator?.UpdateSettings(_settings);
_playbackService.SetVolume(_settings.MusicVolume / Constants.VolumeDivisor);
```

**With SettingsService:**
```csharp
// Services subscribe to changes
_settingsService.SettingsChanged += (sender, args) => {
    _coordinator?.UpdateSettings(args.NewSettings);
    _playbackService.SetVolume(args.NewSettings.MusicVolume / Constants.VolumeDivisor);
};
```

### 3. Validation Layer
**Current:**
- Validation happens in property setters (basic range checks)
- No centralized validation
- No validation error reporting

**With SettingsService:**
```csharp
var errors = _settingsService.ValidateSettings(newSettings);
if (errors.Count > 0)
{
    // Show validation errors to user
    _api.Dialogs.ShowErrorMessage(string.Join("\n", errors), "Settings Validation");
    return;
}
```

### 4. Better Testability
**Current:**
- Hard to mock settings (passed as concrete objects)
- Settings loading tied to Playnite API

**With SettingsService:**
```csharp
// Easy to mock in tests
var mockSettingsService = new Mock<SettingsService>();
mockSettingsService.Setup(s => s.Current).Returns(testSettings);
```

### 5. Single Source of Truth
**Current:**
- Settings reference can be stale if not updated
- Multiple places need to know about settings changes

**With SettingsService:**
- Always current settings via `Current` property
- All subscribers notified automatically

---

## Drawbacks / Concerns

### 1. Additional Complexity
- Adds another service layer
- Requires dependency injection changes
- More code to maintain

### 2. Current System Works Fine
- Settings are already working correctly
- `OnSettingsSaved()` is straightforward (35 lines)
- No reported issues with current approach

### 3. Limited Benefit
- Only 3-4 services use settings
- Settings don't change frequently
- Current manual update is simple and clear

### 4. Risk of Breaking Changes
- Need to update all services to use SettingsService
- Risk of missing a settings update somewhere
- Could break existing functionality

---

## Implementation Impact

### Files to Create:
- `UniPSong/Services/SettingsService.cs`

### Files to Modify:
1. **UniPlaySong.cs**:
   - Create SettingsService instance
   - Replace `_settings` field with `_settingsService`
   - Update `OnSettingsSaved()` to use SettingsService
   - Subscribe to SettingsChanged event

2. **MusicPlaybackCoordinator.cs**:
   - Replace `UniPlaySongSettings settings` parameter with `SettingsService settingsService`
   - Access settings via `settingsService.Current`
   - Subscribe to SettingsChanged event

3. **DownloadDialogService.cs**:
   - Replace `UniPlaySongSettings settings` parameter with `SettingsService settingsService`
   - Access settings via `settingsService.Current`

4. **Other Services** (if any use settings):
   - Similar pattern - replace parameter with SettingsService

### Migration Strategy:
1. Create SettingsService (don't use it yet)
2. Add SettingsService to dependency injection
3. Update one service at a time (start with non-critical)
4. Test after each service update
5. Finally update UniPlaySong.cs

---

## Comparison: Current vs. SettingsService

### Current Approach
**Pros:**
- ✅ Simple and straightforward
- ✅ Already working correctly
- ✅ Easy to understand
- ✅ No additional abstraction

**Cons:**
- ⚠️ Manual update propagation
- ⚠️ Settings passed as parameters
- ⚠️ No centralized validation
- ⚠️ Harder to test

### SettingsService Approach
**Pros:**
- ✅ Centralized access
- ✅ Event-driven updates
- ✅ Validation layer
- ✅ Better testability
- ✅ Single source of truth

**Cons:**
- ⚠️ Additional complexity
- ⚠️ More code to maintain
- ⚠️ Requires refactoring
- ⚠️ Risk of breaking changes

---

## Recommendation

### Current Assessment: **DEFER**

**Why Defer:**
1. **Current system works well** - No reported issues
2. **Limited benefit** - Only 3-4 services use settings
3. **Low complexity** - `OnSettingsSaved()` is simple (35 lines)
4. **Risk vs. Reward** - Medium risk, medium value
5. **Not causing problems** - Settings updates work correctly

### When to Reconsider:

**Implement if:**
- Settings validation becomes complex
- Multiple services need to react to settings changes
- Settings are accessed from many places
- Need better testability for settings
- Settings update logic becomes complicated

**Don't implement if:**
- Current system continues to work well
- Settings remain simple
- No validation requirements beyond current
- Team prefers simpler approach

---

## Alternative: Lightweight Approach

If you want some benefits without full service:

### Option 1: Settings Helper (Static)
```csharp
public static class SettingsHelper
{
    public static void ValidateSettings(UniPlaySongSettings settings) { ... }
    public static bool IsValid(UniPlaySongSettings settings) { ... }
}
```

### Option 2: Settings Extension Methods
```csharp
public static class SettingsExtensions
{
    public static bool IsValid(this UniPlaySongSettings settings) { ... }
    public static List<string> GetValidationErrors(this UniPlaySongSettings settings) { ... }
}
```

### Option 3: Keep Current, Add Validation
- Add validation method to `UniPlaySongSettings` class
- Call validation in `OnSettingsSaved()`
- No service needed

---

## Decision Matrix

| Factor | Current | SettingsService | Winner |
|--------|---------|-----------------|--------|
| **Simplicity** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Current |
| **Maintainability** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | Tie |
| **Testability** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | SettingsService |
| **Flexibility** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | SettingsService |
| **Risk** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Current |
| **Current Value** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Current |

**Verdict**: Current approach is better for now, SettingsService is better for future growth.

---

## Conclusion

The Settings Service is a **nice-to-have** improvement that would provide better architecture, but the **current system works well** and doesn't need fixing.

**Recommendation**: 
- **DEFER** for now
- Re-evaluate if:
  - Settings validation becomes complex
  - More services need settings access
  - Settings update logic grows
  - Better testability becomes critical

**Priority**: Medium (can wait)

---

**Last Updated**: 2025-12-07  
**Status**: Analysis Complete - Recommendation: Defer

