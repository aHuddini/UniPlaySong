# Version 1.0.3.2 - Material Design Integration & UI Improvements

**Release Date**: 2025-12-01  
**Status**: ✅ Production Ready

## Overview

This version successfully integrates Material Design 4.7.0 into UniPlaySong dialogs, providing a modern, polished user interface. All Material Design dependency issues have been resolved, and the extension now includes comprehensive UI improvements.

## Major Features

### Material Design Integration

- **Material Design 4.7.0** - Successfully integrated with all dependencies
- **Modern Dialog UI** - Polished, professional appearance
- **Dark Theme** - DeepPurple primary, Lime secondary colors
- **Compact Design** - Optimized for efficient space usage

### UI Improvements

1. **Fullscreen Window Transparency Fixed**
   - Added dark background color (RGB 33,33,33) to all dialogs
   - Dialogs now properly visible in fullscreen mode

2. **Search Field Visibility**
   - Fixed text color to use MaterialDesignBodyLight
   - Text now clearly visible on dark backgrounds

3. **Back Button Navigation**
   - Added back button in song selection dialog
   - Users can navigate: Source → Album → Songs → (Back) → Album
   - Improved workflow efficiency

4. **Performance Optimizations**
   - Changed search binding from PropertyChanged to LostFocus
   - Added ListBox virtualization for large result sets
   - Reduced desktop lag issues

### Download & Preview Fixes

1. **KHInsider Preview Working**
   - Fixed temp path generation for relative paths
   - Improved URL handling for absolute/relative URLs
   - Preview now works for both KHInsider and YouTube songs

2. **YouTube Search Enhancement**
   - Automatically appends "OST" to game title
   - Better soundtrack-specific results
   - Reduced random video results

## Technical Implementation

### Assembly Loading

**Pre-loading Strategy**:
```csharp
private static void PreloadMaterialDesignAssemblies()
{
    // Loads Material Design assemblies before XAML parsing
    // Ensures they're available when XAML parser needs them
    string[] assembliesToLoad = new[]
    {
        "Microsoft.Xaml.Behaviors.dll",
        "MaterialDesignColors.dll",
        "MaterialDesignThemes.Wpf.dll"
    };
    // ... loads in dependency order
}
```

**Assembly Resolution Handler**:
```csharp
static UniPlaySong()
{
    AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
    {
        // Helps Playnite locate Material Design DLLs
        // Searches extension directory for missing assemblies
    };
}
```

### DLL Management

**Centralized DLL Folder** (`lib\dll\`):
- One-stop-shop for all extension DLLs
- Primary source for packaging script
- Easy to verify and update dependencies

**Required DLLs**:
- MaterialDesignThemes.Wpf.dll (9,240 KB)
- MaterialDesignColors.dll (295.5 KB)
- Microsoft.Xaml.Behaviors.dll (141.88 KB)
- HtmlAgilityPack.dll (165.5 KB)

### Window Styling

**Background Color Fix**:
```csharp
window.Background = new System.Windows.Media.SolidColorBrush(
    System.Windows.Media.Color.FromRgb(33, 33, 33));
```

Applied to all dialogs:
- Source selection dialog
- Album selection dialog
- Song selection dialog

## Files Modified

### Core Files
- `UniPlaySong.cs` - Assembly resolution handler
- `DownloadDialogService.cs` - Pre-loading, window backgrounds, back button
- `DownloadDialogViewModel.cs` - Preview fixes, back button support
- `DownloadDialogView.xaml` - Material Design styling, UI improvements

### Downloaders
- `KHInsiderDownloader.cs` - URL absolute path handling
- `YouTubeDownloader.cs` - OST search query

### Navigation
- `GameMenuHandler.cs` - Navigation loop for back button

### Packaging
- `package_extension.ps1` - DLL management improvements
- `lib/dll/` - New centralized DLL folder

## Testing Results

✅ Material Design loads successfully  
✅ All dialogs display correctly in fullscreen  
✅ Search field text is visible  
✅ Back button navigation works  
✅ KHInsider preview works  
✅ YouTube search returns better results  
✅ Performance improved (reduced lag)  

## Known Issues

- Desktop version may still have minor lag with very large result sets (mitigated by virtualization)
- Material Design 4.7.0 is the latest version compatible with .NET Framework 4.6.2

## Migration Notes

No migration required - this is a feature update. Existing installations will work with the new version.

## Next Steps

Future improvements could include:
- Additional Material Design components
- More dialog customization options
- Further performance optimizations

