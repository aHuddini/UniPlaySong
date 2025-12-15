# Settings Service - Implementation Plan

**Date**: 2025-12-07  
**Status**: Ready for Implementation  
**Priority**: High (Prevents Future Issues)  
**Estimated Time**: 4-6 hours

---

## Goals

1. **Make adding new settings easier** - No need to manually update propagation
2. **Prevent forgotten updates** - Automatic notification to all services
3. **Single source of truth** - Always current settings
4. **Event-driven updates** - Services automatically react to changes

---

## Current Problems This Solves

### Problem 1: Manual Propagation
**Current:**
```csharp
// In OnSettingsSaved() - must remember to update each service
_coordinator?.UpdateSettings(_settings);
_playbackService.SetVolume(_settings.MusicVolume / Constants.VolumeDivisor);
// Easy to forget when adding new settings!
```

**With SettingsService:**
```csharp
// Services subscribe once, automatically get updates
_settingsService.SettingsChanged += OnSettingsChanged;
// No need to remember to update each service!
```

### Problem 2: Settings Passed as Parameters
**Current:**
```csharp
// Must pass settings to every service
new MusicPlaybackCoordinator(..., settings, ...)
new DownloadDialogService(..., settings)
// Easy to forget to pass or update
```

**With SettingsService:**
```csharp
// Services get settings from service
new MusicPlaybackCoordinator(..., settingsService, ...)
// Access via: settingsService.Current.MusicVolume
```

### Problem 3: Adding New Settings
**Current:**
1. Add property to `UniPlaySongSettings`
2. Remember to update `OnSettingsSaved()`
3. Remember to pass to services
4. Remember to update `UpdateSettings()` methods
5. **Easy to miss a step!**

**With SettingsService:**
1. Add property to `UniPlaySongSettings`
2. **Done!** Services automatically get updates via events

---

## SettingsService Design

### Core Service

```csharp
namespace UniPlaySong.Services
{
    /// <summary>
    /// Centralized settings management service
    /// Provides single source of truth and automatic propagation
    /// </summary>
    public class SettingsService
    {
        private readonly IPlayniteAPI _api;
        private readonly ILogger _logger;
        private readonly FileLogger _fileLogger;
        private UniPlaySongSettings _currentSettings;
        private UniPlaySong _plugin;

        /// <summary>
        /// Fired when settings are updated (new settings object)
        /// </summary>
        public event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        /// <summary>
        /// Fired when a specific setting property changes
        /// </summary>
        public event EventHandler<PropertyChangedEventArgs> SettingPropertyChanged;

        /// <summary>
        /// Current settings (always up-to-date)
        /// </summary>
        public UniPlaySongSettings Current => _currentSettings;

        public SettingsService(IPlayniteAPI api, ILogger logger, FileLogger fileLogger, UniPlaySong plugin)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileLogger = fileLogger;
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            
            // Load initial settings
            LoadSettings();
        }

        /// <summary>
        /// Loads settings from disk and updates all subscribers
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                var newSettings = _plugin.LoadPluginSettings<UniPlaySongSettings>();
                if (newSettings != null)
                {
                    UpdateSettings(newSettings, source: "LoadSettings");
                }
                else
                {
                    // Create default settings if none exist
                    _currentSettings = new UniPlaySongSettings();
                    _fileLogger?.Info("SettingsService: Created default settings");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading settings");
                _fileLogger?.Error($"Error loading settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates settings and notifies all subscribers
        /// This is the ONLY place settings are updated - ensures consistency
        /// </summary>
        public void UpdateSettings(UniPlaySongSettings newSettings, string source = "Unknown")
        {
            if (newSettings == null)
            {
                _logger.Warn("UpdateSettings called with null settings");
                return;
            }

            // Unsubscribe from old settings
            if (_currentSettings != null)
            {
                _currentSettings.PropertyChanged -= OnSettingPropertyChanged;
            }

            // Store old settings for comparison
            var oldSettings = _currentSettings;
            _currentSettings = newSettings;

            // Subscribe to new settings property changes
            _currentSettings.PropertyChanged += OnSettingPropertyChanged;

            // Notify all subscribers that settings changed
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(oldSettings, newSettings, source));
            
            _logger.Info($"SettingsService: Settings updated (source: {source})");
            _fileLogger?.Info($"SettingsService: Settings updated from {source}");
        }

        /// <summary>
        /// Validates settings and returns list of errors
        /// </summary>
        public List<string> ValidateSettings(UniPlaySongSettings settings)
        {
            var errors = new List<string>();

            if (settings == null)
            {
                errors.Add("Settings cannot be null");
                return errors;
            }

            // Volume validation
            if (settings.MusicVolume < Constants.MinMusicVolume || 
                settings.MusicVolume > Constants.MaxMusicVolume)
            {
                errors.Add($"Music volume must be between {Constants.MinMusicVolume} and {Constants.MaxMusicVolume}");
            }

            // Fade duration validation
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

            // File path validation
            if (!string.IsNullOrEmpty(settings.YtDlpPath) && !System.IO.File.Exists(settings.YtDlpPath))
            {
                errors.Add($"yt-dlp path does not exist: {settings.YtDlpPath}");
            }

            if (!string.IsNullOrEmpty(settings.FFmpegPath) && !System.IO.File.Exists(settings.FFmpegPath))
            {
                errors.Add($"ffmpeg path does not exist: {settings.FFmpegPath}");
            }

            if (settings.EnableDefaultMusic && 
                !string.IsNullOrEmpty(settings.DefaultMusicPath) && 
                !System.IO.File.Exists(settings.DefaultMusicPath))
            {
                errors.Add($"Default music file does not exist: {settings.DefaultMusicPath}");
            }

            return errors;
        }

        /// <summary>
        /// Handles property changes from settings object
        /// </summary>
        private void OnSettingPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SettingPropertyChanged?.Invoke(this, e);
        }
    }

    /// <summary>
    /// Event args for settings changed event
    /// </summary>
    public class SettingsChangedEventArgs : EventArgs
    {
        public UniPlaySongSettings OldSettings { get; }
        public UniPlaySongSettings NewSettings { get; }
        public string Source { get; }

        public SettingsChangedEventArgs(UniPlaySongSettings oldSettings, UniPlaySongSettings newSettings, string source)
        {
            OldSettings = oldSettings;
            NewSettings = newSettings;
            Source = source;
        }
    }
}
```

---

## Implementation Strategy

### Phase 1: Create Service (No Usage Yet)
**Goal**: Create SettingsService without breaking anything

1. Create `SettingsService.cs`
2. Add to dependency injection in `UniPlaySong.cs`
3. **Don't use it yet** - just create it
4. Test that it compiles

**Files:**
- Create: `UniPSong/Services/SettingsService.cs`
- Modify: `UniPSong/UniPlaySong.cs` (add service creation)

---

### Phase 2: Update UniPlaySong.cs (Main Plugin)
**Goal**: Use SettingsService in main plugin, keep old system working

1. Replace `_settings` field with `_settingsService`
2. Update `OnSettingsSaved()` to use SettingsService
3. Subscribe to SettingsChanged event
4. Keep old `_settings` reference temporarily for compatibility
5. Test that settings still work

**Changes:**
```csharp
// Before
private UniPlaySongSettings _settings;

// After
private SettingsService _settingsService;
private UniPlaySongSettings _settings => _settingsService?.Current; // Compatibility property
```

**Files:**
- Modify: `UniPSong/UniPlaySong.cs`

---

### Phase 3: Update MusicPlaybackCoordinator
**Goal**: Use SettingsService instead of settings parameter

1. Replace `UniPlaySongSettings settings` parameter with `SettingsService settingsService`
2. Access settings via `settingsService.Current`
3. Subscribe to SettingsChanged event for automatic updates
4. Remove `UpdateSettings()` method (no longer needed)
5. Test that coordinator still works

**Changes:**
```csharp
// Before
public MusicPlaybackCoordinator(..., UniPlaySongSettings settings, ...)
{
    _settings = settings;
}

public void UpdateSettings(UniPlaySongSettings newSettings)
{
    _settings = newSettings;
}

// After
public MusicPlaybackCoordinator(..., SettingsService settingsService, ...)
{
    _settingsService = settingsService;
    _settings = settingsService.Current;
    
    // Subscribe to automatic updates
    settingsService.SettingsChanged += (sender, args) => {
        _settings = args.NewSettings;
        _fileLogger?.Info("MusicPlaybackCoordinator: Settings updated automatically");
    };
}

// UpdateSettings() method removed - no longer needed!
```

**Files:**
- Modify: `UniPSong/Services/MusicPlaybackCoordinator.cs`
- Modify: `UniPSong/Services/IMusicPlaybackCoordinator.cs` (remove UpdateSettings)
- Modify: `UniPSong/UniPlaySong.cs` (update constructor call)

---

### Phase 4: Update DownloadDialogService
**Goal**: Use SettingsService instead of settings parameter

1. Replace `UniPlaySongSettings settings` parameter with `SettingsService settingsService`
2. Access settings via `settingsService.Current`
3. Test that downloads still work

**Changes:**
```csharp
// Before
public DownloadDialogService(..., UniPlaySongSettings settings)
{
    _settings = settings;
}

// After
public DownloadDialogService(..., SettingsService settingsService)
{
    _settingsService = settingsService;
}

// Usage: _settingsService.Current.YtDlpPath
```

**Files:**
- Modify: `UniPSong/Services/DownloadDialogService.cs`
- Modify: `UniPSong/UniPlaySong.cs` (update constructor call)

---

### Phase 5: Update Other Services
**Goal**: Update any remaining services that use settings

1. Check for any other services using settings
2. Update them to use SettingsService
3. Test thoroughly

**Files to Check:**
- Any other services that receive settings as parameters

---

### Phase 6: Cleanup
**Goal**: Remove compatibility code and old patterns

1. Remove compatibility `_settings` property if added
2. Remove any remaining `UpdateSettings()` calls
3. Update documentation
4. Final testing

---

## Key Benefits After Implementation

### Adding New Settings Becomes Easy

**Before (Current):**
```csharp
// 1. Add property to UniPlaySongSettings
public bool NewFeature { get; set; }

// 2. Remember to update OnSettingsSaved()
_coordinator?.UpdateSettings(_settings);
_newService?.UpdateSettings(_settings); // Easy to forget!

// 3. Remember to pass to services
new SomeService(..., _settings)

// 4. Remember to add UpdateSettings() method
public void UpdateSettings(UniPlaySongSettings newSettings)
{
    _settings = newSettings; // Easy to forget!
}
```

**After (With SettingsService):**
```csharp
// 1. Add property to UniPlaySongSettings
public bool NewFeature { get; set; }

// 2. Done! Services automatically get updates via events
// No need to remember anything!
```

### Automatic Propagation

**Before:**
```csharp
// Must manually update each service
_coordinator?.UpdateSettings(_settings);
_playbackService.SetVolume(_settings.MusicVolume / Constants.VolumeDivisor);
// Easy to forget when adding new service!
```

**After:**
```csharp
// Services subscribe once, get updates automatically
_settingsService.SettingsChanged += (sender, args) => {
    // All services automatically notified
    // No need to remember to update!
};
```

---

## Migration Checklist

### Phase 1: Create Service
- [ ] Create `SettingsService.cs`
- [ ] Add to dependency injection
- [ ] Test compilation

### Phase 2: Update Main Plugin
- [ ] Replace `_settings` with `_settingsService`
- [ ] Update `OnSettingsSaved()`
- [ ] Subscribe to SettingsChanged
- [ ] Test settings save/load

### Phase 3: Update Coordinator
- [ ] Replace settings parameter with SettingsService
- [ ] Subscribe to SettingsChanged
- [ ] Remove `UpdateSettings()` method
- [ ] Update interface
- [ ] Test coordinator still works

### Phase 4: Update DownloadDialogService
- [ ] Replace settings parameter with SettingsService
- [ ] Update constructor call
- [ ] Test downloads still work

### Phase 5: Check Other Services
- [ ] Find all services using settings
- [ ] Update to use SettingsService
- [ ] Test all functionality

### Phase 6: Cleanup
- [ ] Remove compatibility code
- [ ] Remove old UpdateSettings calls
- [ ] Final testing
- [ ] Update documentation

---

## Safety Features

### 1. Always Current Settings
```csharp
// Services always get current settings
var volume = _settingsService.Current.MusicVolume;
// No stale references!
```

### 2. Automatic Updates
```csharp
// Services subscribe once, get all updates
_settingsService.SettingsChanged += OnSettingsChanged;
// Never forget to update!
```

### 3. Validation
```csharp
// Validate before updating
var errors = _settingsService.ValidateSettings(newSettings);
if (errors.Count > 0)
{
    // Show errors to user
    return;
}
_settingsService.UpdateSettings(newSettings);
```

### 4. Logging
```csharp
// All settings updates logged
_settingsService.UpdateSettings(newSettings, source: "OnSettingsSaved");
// Easy to debug settings issues
```

---

## Testing Strategy

### Unit Testing
- Test SettingsService creation
- Test LoadSettings()
- Test UpdateSettings()
- Test SettingsChanged event
- Test ValidateSettings()

### Integration Testing
- Test settings save/load
- Test coordinator gets updates
- Test download service gets updates
- Test volume changes apply
- Test all settings work correctly

### Regression Testing
- Test existing functionality unchanged
- Test settings UI still works
- Test all services still work
- Test settings persistence

---

## Risk Mitigation

### Incremental Migration
- One service at a time
- Test after each change
- Keep old code until new code proven

### Backward Compatibility
- Keep `_settings` property temporarily
- Gradually migrate services
- Remove old code only after all migrated

### Rollback Plan
- Keep old code in comments
- Can revert if issues found
- Test thoroughly before removing old code

---

## Files to Modify

### Create:
- `UniPSong/Services/SettingsService.cs`

### Modify:
1. `UniPSong/UniPlaySong.cs`
   - Add SettingsService creation
   - Update OnSettingsSaved()
   - Subscribe to SettingsChanged
   - Update service constructors

2. `UniPSong/Services/MusicPlaybackCoordinator.cs`
   - Replace settings parameter with SettingsService
   - Subscribe to SettingsChanged
   - Remove UpdateSettings() method

3. `UniPSong/Services/IMusicPlaybackCoordinator.cs`
   - Remove UpdateSettings() from interface

4. `UniPSong/Services/DownloadDialogService.cs`
   - Replace settings parameter with SettingsService

5. Any other services using settings

---

## Example: Adding New Setting After Implementation

### Step 1: Add Property
```csharp
// In UniPlaySongSettings.cs
public bool NewFeature { get; set; }
```

### Step 2: Done!
- Services automatically get updates via SettingsChanged event
- No need to update OnSettingsSaved()
- No need to update service constructors
- No need to add UpdateSettings() methods
- **Just works!**

---

## Next Steps

1. **Review this plan** - Ensure it meets your needs
2. **Start Phase 1** - Create SettingsService (no usage yet)
3. **Test incrementally** - One phase at a time
4. **Verify benefits** - Confirm adding new settings is easier

---

**Last Updated**: 2025-12-07  
**Status**: Ready for Implementation  
**Goal**: Make adding new settings easier and prevent forgotten updates

