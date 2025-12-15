# Audio Normalization - Architecture & Separation of Concerns

**Date**: 2025-12-14  
**Status**: Architecture Design Complete  
**Approach**: Two-pass FFmpeg loudnorm filter implementation

## 🏗️ **Architectural Design**

Following our established patterns (DownloadDialogService, GameMenuHandler, etc.), the normalization feature will be structured with clear separation of concerns.

---

## 📁 **File Structure**

```
UniPSong/
├── Services/
│   ├── AudioNormalizationService.cs       # Core normalization logic
│   └── INormalizationService.cs           # Service interface
├── Models/
│   └── NormalizationSettings.cs           # Normalization configuration
├── ViewModels/
│   └── NormalizationSettingsViewModel.cs  # Settings UI logic (if needed)
├── Views/
│   ├── NormalizationSettingsView.xaml     # Settings UI tab
│   ├── NormalizationProgressDialog.xaml   # Progress dialog (controller-friendly)
│   └── NormalizationProgressDialog.xaml.cs
├── Menus/
│   └── (GameMenuHandler.cs)               # Add normalization menu items
└── UniPlaySongSettings.cs                 # Add normalization settings properties
```

---

## 🔧 **Component Responsibilities**

### **1. AudioNormalizationService** (Services/)
**Purpose**: Core normalization logic - pure business logic, no UI dependencies

**Responsibilities**:
- ✅ Execute FFmpeg normalization commands
- ✅ Perform two-pass normalization (analyze → normalize)
- ✅ Parse FFmpeg output for measurements
- ✅ File operations (backup, replace, cleanup)
- ✅ Process management (spawn FFmpeg, handle errors)
- ✅ Progress reporting via callbacks

**Dependencies**:
- `UniPlaySongSettings` (via constructor injection)
- `ILogger` (for logging)
- `ErrorHandlerService` (for error handling)

**NO Dependencies On**:
- ❌ UI components
- ❌ Playnite API (except logger)
- ❌ User dialogs
- ❌ Menu handlers

**Interface**:
```csharp
public interface INormalizationService
{
    Task<bool> NormalizeFileAsync(string filePath, NormalizationSettings settings, 
        IProgress<NormalizationProgress> progress, CancellationToken cancellationToken);
    
    Task<NormalizationResult> NormalizeBulkAsync(IEnumerable<string> filePaths, 
        NormalizationSettings settings, IProgress<NormalizationProgress> progress, 
        CancellationToken cancellationToken);
    
    bool ValidateFFmpegAvailable();
}
```

---

### **2. NormalizationSettings** (Models/)
**Purpose**: Data model for normalization configuration

**Responsibilities**:
- ✅ Store normalization parameters (target loudness, true peak, etc.)
- ✅ Default values
- ✅ Settings validation

**Structure**:
```csharp
public class NormalizationSettings
{
    public double TargetLoudness { get; set; } = -16.0; // LUFS
    public double TruePeak { get; set; } = -1.5; // dBTP
    public double LoudnessRange { get; set; } = 11.0; // LU
    public string AudioCodec { get; set; } = "libmp3lame";
    public bool PreserveOriginals { get; set; } = true;
    public string FFmpegPath { get; set; }
}
```

---

### **3. UniPlaySongSettings** (Models/)
**Purpose**: Extension settings - add normalization properties

**New Properties**:
```csharp
public class UniPlaySongSettings : ObservableObject
{
    // Existing properties...
    
    // Normalization settings
    private bool enableNormalization = false;
    public bool EnableNormalization
    {
        get => enableNormalization;
        set { enableNormalization = value; OnPropertyChanged(); }
    }
    
    private double normalizationTargetLoudness = -16.0;
    public double NormalizationTargetLoudness
    {
        get => normalizationTargetLoudness;
        set { normalizationTargetLoudness = value; OnPropertyChanged(); }
    }
    
    private double normalizationTruePeak = -1.5;
    public double NormalizationTruePeak
    {
        get => normalizationTruePeak;
        set { normalizationTruePeak = value; OnPropertyChanged(); }
    }
    
    private double normalizationLoudnessRange = 11.0;
    public double NormalizationLoudnessRange
    {
        get => normalizationLoudnessRange;
        set { normalizationLoudnessRange = value; OnPropertyChanged(); }
    }
    
    private string normalizationCodec = "libmp3lame";
    public string NormalizationCodec
    {
        get => normalizationCodec;
        set { normalizationCodec = value; OnPropertyChanged(); }
    }
    
    private bool preserveOriginalFiles = true;
    public bool PreserveOriginalFiles
    {
        get => preserveOriginalFiles;
        set { preserveOriginalFiles = value; OnPropertyChanged(); }
    }
}
```

---

### **4. NormalizationSettingsView** (Views/)
**Purpose**: Settings UI tab - presentation only

**Responsibilities**:
- ✅ Display normalization settings
- ✅ Bind to settings properties
- ✅ Provide UI controls (textboxes, checkboxes, buttons)
- ✅ Trigger normalization actions (buttons)

**NO Business Logic**:
- ❌ Does NOT perform normalization
- ❌ Does NOT validate FFmpeg
- ❌ Delegates ALL actions to ViewModel/Handler

**Structure**:
```xaml
<UserControl>
    <TabControl>
        <!-- Normalization Tab -->
        <TabItem Header="Audio Normalization">
            <StackPanel>
                <CheckBox Content="Enable Audio Normalization" 
                         IsChecked="{Binding Settings.EnableNormalization}"/>
                
                <TextBlock Text="Target Loudness:"/>
                <TextBox Text="{Binding Settings.NormalizationTargetLoudness}"/>
                
                <!-- More settings... -->
                
                <Button Content="Normalize All Music Files" 
                       Command="{Binding NormalizeAllCommand}"/>
                <Button Content="Normalize Selected Games" 
                       Command="{Binding NormalizeSelectedCommand}"/>
            </StackPanel>
        </TabItem>
    </TabControl>
</UserControl>
```

---

### **5. NormalizationProgressDialog** (Views/)
**Purpose**: Progress display during normalization

**Responsibilities**:
- ✅ Show normalization progress
- ✅ Display current file being processed
- ✅ Show statistics (success/failure counts)
- ✅ Provide cancellation button
- ✅ Controller-friendly (follows our controller dialog pattern)

**Dependencies**:
- Receives progress updates via events/callbacks
- Uses existing controller dialog patterns

**NO Business Logic**:
- ❌ Does NOT perform normalization
- ❌ Only displays progress information

---

### **6. GameMenuHandler** (Menus/)
**Purpose**: Menu item actions - coordinates between UI and services

**Responsibilities**:
- ✅ Handle "Normalize Music" menu item clicks
- ✅ Collect file list for selected games
- ✅ Call normalization service
- ✅ Show progress dialog
- ✅ Handle errors and user feedback

**Pattern** (similar to DownloadMusicForGame):
```csharp
public class GameMenuHandler
{
    private readonly INormalizationService _normalizationService;
    private readonly GameMusicFileService _fileService;
    private readonly IPlayniteAPI _playniteApi;
    
    public void NormalizeMusicForGame(Game game)
    {
        _errorHandler.Try(() =>
        {
            // Get music files for game
            var musicFiles = _fileService.GetAvailableSongs(game);
            
            // Show progress dialog
            var progressDialog = new NormalizationProgressDialog();
            
            // Call service
            _normalizationService.NormalizeBulkAsync(
                musicFiles,
                GetNormalizationSettings(),
                progressDialog.ProgressReporter,
                progressDialog.CancellationToken
            );
        });
    }
    
    private NormalizationSettings GetNormalizationSettings()
    {
        return new NormalizationSettings
        {
            TargetLoudness = _settings.NormalizationTargetLoudness,
            TruePeak = _settings.NormalizationTruePeak,
            LoudnessRange = _settings.NormalizationLoudnessRange,
            Codec = _settings.NormalizationCodec,
            PreserveOriginals = _settings.PreserveOriginalFiles,
            FFmpegPath = _settings.FFmpegPath
        };
    }
}
```

---

### **7. UniPlaySong.cs** (Main Plugin)
**Purpose**: Dependency injection and initialization

**Responsibilities**:
- ✅ Initialize AudioNormalizationService
- ✅ Register menu items (via GameMenuHandler)
- ✅ Pass settings to service
- ✅ Coordinate service lifecycle

**Pattern**:
```csharp
public class UniPlaySong : GenericPlugin
{
    private INormalizationService _normalizationService;
    
    private void InitializeServices()
    {
        // ... existing services ...
        
        // Initialize normalization service
        _normalizationService = new AudioNormalizationService(
            _settings,
            logger,
            _errorHandler
        );
        
        // Pass to menu handler
        _gameMenuHandler.SetNormalizationService(_normalizationService);
    }
}
```

---

## 🔄 **Data Flow**

### **Normalization Workflow**

```
User Action (Button/Menu)
    ↓
GameMenuHandler.NormalizeMusicForGame()
    ↓
Collect file list (GameMusicFileService)
    ↓
Show Progress Dialog (NormalizationProgressDialog)
    ↓
AudioNormalizationService.NormalizeBulkAsync()
    ↓
For each file:
    1. First Pass: Analyze audio → Parse measurements
    2. Second Pass: Apply normalization → Replace file
    ↓
Report Progress (ProgressDialog updates)
    ↓
Completion (ProgressDialog shows results)
```

---

## 🎯 **Separation Principles Applied**

### **1. Service Layer Isolation**
- ✅ `AudioNormalizationService` has ZERO UI dependencies
- ✅ Can be unit tested independently
- ✅ Can be reused in different contexts
- ✅ Only depends on models and interfaces

### **2. UI Layer Purity**
- ✅ Views only handle presentation
- ✅ ViewModels/Handlers coordinate between UI and services
- ✅ No business logic in XAML or code-behind

### **3. Dependency Injection**
- ✅ Services injected via constructors
- ✅ Settings passed as parameters (not global access)
- ✅ Easy to mock for testing

### **4. Error Handling Separation**
- ✅ Service throws exceptions (business logic errors)
- ✅ Handlers catch and use ErrorHandlerService
- ✅ UI shows user-friendly messages

### **5. Settings Management**
- ✅ Settings in UniPlaySongSettings (data model)
- ✅ Service receives settings as parameters
- ✅ No direct settings access in service

---

## 📋 **Implementation Checklist**

### **Phase 1: Core Service** ✅
- [ ] Create `INormalizationService` interface
- [ ] Create `NormalizationSettings` model
- [ ] Implement `AudioNormalizationService`
  - [ ] FFmpeg path validation
  - [ ] First pass (analysis)
  - [ ] Parse JSON output
  - [ ] Second pass (apply normalization)
  - [ ] File backup/replace logic
  - [ ] Progress reporting
  - [ ] Error handling

### **Phase 2: Settings Integration** ✅
- [ ] Add normalization properties to `UniPlaySongSettings`
- [ ] Create `NormalizationSettingsView.xaml` UI
- [ ] Bind settings in settings view
- [ ] Add settings tab to main settings view

### **Phase 3: Progress Dialog** ✅
- [ ] Create `NormalizationProgressDialog.xaml`
- [ ] Create `NormalizationProgressDialog.xaml.cs`
- [ ] Implement progress reporting interface
- [ ] Add controller support (optional but recommended)
- [ ] Add cancellation support

### **Phase 4: Menu Integration** ✅
- [ ] Add "Normalize Music" to `GameMenuHandler`
- [ ] Add "Normalize All" to settings actions
- [ ] Wire up service calls
- [ ] Add error handling and user feedback

### **Phase 5: Initialization** ✅
- [ ] Initialize service in `UniPlaySong.cs`
- [ ] Pass service to `GameMenuHandler`
- [ ] Register menu items
- [ ] Test end-to-end workflow

---

## 🔍 **Example Service Implementation**

```csharp
public class AudioNormalizationService : INormalizationService
{
    private readonly ILogger _logger;
    private readonly ErrorHandlerService _errorHandler;
    
    public AudioNormalizationService(ILogger logger, ErrorHandlerService errorHandler)
    {
        _logger = logger;
        _errorHandler = errorHandler;
    }
    
    public async Task<bool> NormalizeFileAsync(
        string filePath, 
        NormalizationSettings settings,
        IProgress<NormalizationProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            // First pass: Analyze
            var measurements = await AnalyzeAudioAsync(filePath, settings, cancellationToken);
            
            // Second pass: Normalize
            return await ApplyNormalizationAsync(filePath, settings, measurements, 
                progress, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to normalize file: {filePath}");
            throw;
        }
    }
    
    private async Task<LoudnormMeasurements> AnalyzeAudioAsync(
        string filePath, 
        NormalizationSettings settings,
        CancellationToken cancellationToken)
    {
        // FFmpeg first pass command
        var args = $"-i \"{filePath}\" -af loudnorm=I={settings.TargetLoudness}:TP={settings.TruePeak}:LRA={settings.LoudnessRange}:print_format=json -f null -";
        
        // Execute and parse JSON output
        // Return measurements
    }
    
    private async Task<bool> ApplyNormalizationAsync(
        string filePath,
        NormalizationSettings settings,
        LoudnormMeasurements measurements,
        IProgress<NormalizationProgress> progress,
        CancellationToken cancellationToken)
    {
        // FFmpeg second pass command using measurements
        // Create backup if PreserveOriginals
        // Replace original on success
        // Report progress
    }
}
```

---

## ✅ **Benefits of This Architecture**

1. **Testability** - Service can be unit tested without UI
2. **Maintainability** - Clear responsibilities, easy to modify
3. **Reusability** - Service can be used in different contexts
4. **Consistency** - Follows established patterns in codebase
5. **Scalability** - Easy to add features (batch operations, etc.)
6. **Error Handling** - Centralized error handling via ErrorHandlerService

---

## 🏁 **Summary**

This architecture follows our established separation of concerns pattern:

- **Service Layer**: Pure business logic, no UI dependencies
- **Model Layer**: Data structures and settings
- **View Layer**: Presentation only, delegates to handlers
- **Handler Layer**: Coordinates UI and services
- **Plugin Layer**: Dependency injection and initialization

This ensures the normalization feature is:
- ✅ Maintainable
- ✅ Testable
- ✅ Consistent with existing codebase
- ✅ Easy to extend

Ready for implementation following this structure!