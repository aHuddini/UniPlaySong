# UniPlaySong - Dependencies Documentation

## Overview

This document details all dependencies required for building, running, and using UniPlaySong. Dependencies are categorized into NuGet packages, native DLLs, and external tools.

## NuGet Packages

All NuGet packages are defined in `UniPlaySong.csproj` and automatically restored during build.

### Core Dependencies

#### PlayniteSDK (6.11.0.0)
- **Purpose**: Playnite extension API and SDK
- **Usage**: 
  - Plugin interface (`GenericPlugin`)
  - Playnite API access (`IPlayniteAPI`)
  - Game models and events
  - Settings management
- **Location**: Referenced in `UniPlaySong.csproj`
- **Notes**: This is the primary dependency - all Playnite integration depends on this

#### Microsoft.CSharp (4.7.0)
- **Purpose**: C# language support and dynamic types
- **Usage**: Required for C# 7.0+ features and dynamic type support
- **Location**: Automatically included by .NET Framework 4.6.2

#### System.Net.Http (4.3.4)
- **Purpose**: HTTP client for web requests
- **Usage**: 
  - Downloading music files from web sources
  - YouTube API requests
  - KHInsider web scraping
- **Location**: Used in `DownloadManager`, `YouTubeClient`, `KHInsiderDownloader`

#### HtmlAgilityPack (1.11.46)
- **Purpose**: HTML parsing and DOM manipulation
- **Usage**: 
  - Parsing KHInsider album pages
  - Extracting song lists and download links
  - Web scraping functionality
- **Location**: Used in `KHInsiderDownloader`, `DownloadManager`
- **Notes**: Critical for KHInsider download functionality

### UI Dependencies

#### MaterialDesignThemes (4.7.0)
- **Purpose**: Material Design UI components for WPF
- **Usage**: 
  - Controller-optimized dialogs
  - Modern UI styling
  - Consistent design language
- **Location**: Used in all XAML views (`SimpleControllerDialog`, `ControllerFilePickerDialog`, etc.)
- **Notes**: Provides the modern, controller-friendly UI aesthetic

#### MaterialDesignColors (2.1.0)
- **Purpose**: Material Design color palette
- **Usage**: Color schemes and theming for Material Design components
- **Location**: Used alongside MaterialDesignThemes

#### Microsoft.Xaml.Behaviors (via MaterialDesignThemes)
- **Purpose**: XAML behaviors and interactions
- **Usage**: UI interactions and behaviors in XAML
- **Location**: Included transitively via MaterialDesignThemes

### Utility Dependencies

#### Newtonsoft.Json (13.0.1)
- **Purpose**: JSON serialization/deserialization
- **Usage**: 
  - Settings persistence
  - Search cache storage
  - API response parsing
- **Location**: Used in `SettingsService`, `SearchCacheService`, `YouTubeClient`
- **Notes**: Standard JSON library for .NET Framework

#### System.ValueTuple (4.5.0)
- **Purpose**: Value tuple support for .NET Framework 4.6.2
- **Usage**: Tuple return types and deconstruction
- **Location**: Used throughout codebase for multi-value returns

## Native DLLs

Native DLLs are required for SDL2 audio playback functionality. They are bundled with the extension package.

### SDL2.dll
- **Purpose**: SDL2 core library for audio initialization
- **Version**: 2.30.5 (or latest stable)
- **Architecture**: x64 (64-bit Windows)
- **Location**: `lib/SDL2.dll`
- **Usage**: 
  - Audio system initialization
  - Error handling
  - Platform abstraction
- **P/Invoke**: Defined in `Players/SDL/SDL.cs`
- **License**: zlib license (compatible with commercial use)

### SDL2_mixer.dll
- **Purpose**: SDL2 audio mixer library for music playback
- **Version**: 2.8.0 (or latest stable)
- **Architecture**: x64 (64-bit Windows)
- **Location**: `lib/SDL2_mixer.dll`
- **Usage**: 
  - Music file loading and playback
  - Volume control
  - Position tracking
  - Music finished callbacks
- **P/Invoke**: Defined in `Players/SDL/SDL_mixer.cs`
- **Implementation**: Used by `SDL2MusicPlayer` class
- **License**: zlib license (compatible with commercial use)

### DLL Loading

**Assembly Resolution:**
- DLLs are loaded via P/Invoke at runtime
- No explicit DLL loading required - Windows handles it automatically
- DLLs must be in the same directory as `UniPlaySong.dll` or in system PATH

**Packaging:**
- `package_extension.ps1` automatically includes DLLs in `.pext` package
- DLLs are placed in extension's installed directory by Playnite

**Fallback:**
- If SDL2 initialization fails, extension falls back to WPF `MediaPlayer`
- Fallback is automatic and transparent to user

## External Tools

These tools are **not bundled** with the extension and must be installed separately by users.

### yt-dlp
- **Purpose**: YouTube downloader (fork of youtube-dl)
- **Required For**: YouTube music downloads
- **Installation**: 
  - Download from: https://github.com/yt-dlp/yt-dlp/releases
  - Extract `yt-dlp.exe` to a location of choice
  - Configure path in extension settings
- **Usage**: 
  - Downloads audio from YouTube playlists/tracks
  - Extracts audio from video files
  - Handles YouTube API changes automatically
- **Location**: Path configured in `UniPlaySongSettings.YtDlpPath`
- **Process Execution**: Called via `Process.Start()` in `YouTubeDownloader`
- **JavaScript Runtime Requirement** (yt-dlp 2025.11.12+):
  - **Deno is now recommended** by yt-dlp for YouTube downloads
  - yt-dlp requires an external JavaScript runtime (Deno, Node.js, or QuickJS)
  - **Important**: Place `deno.exe` in the **same folder as yt-dlp.exe** for automatic detection
  - Download Deno from: https://deno.com/ or https://github.com/denoland/deno/releases
  - Alternative runtimes: Node.js (v20+) or QuickJS (if Deno doesn't work)
  - Extension provides helpful error messages if JS runtime is missing
- **Cookies Support**:
  - Extension supports Firefox cookies via `--cookies-from-browser firefox` option
  - When enabled, uses simplified command: `--cookies-from-browser firefox -x --audio-format mp3`
  - Greatly improves download reliability and bypasses YouTube bot detection
  - Configured via `UniPlaySongSettings.UseFirefoxCookies`

**Configuration:**
```csharp
// Settings property
public string YtDlpPath { get; set; }

// Usage in YouTubeDownloader
Process.Start(new ProcessStartInfo
{
    FileName = ytDlpPath,
    Arguments = $"--extract-audio --audio-format mp3 \"{url}\"",
    // ...
});
```

### FFmpeg
- **Purpose**: Audio/video processing and normalization
- **Required For**: 
  - Audio normalization (EBU R128)
  - Audio format conversion
  - Audio analysis
- **Installation**: 
  - Download from: https://ffmpeg.org/download.html
  - Extract `ffmpeg.exe` to a location of choice
  - Configure path in extension settings
- **Usage**: 
  - Normalizes audio files to consistent loudness
  - Converts audio formats
  - Analyzes audio properties (loudness, true peak, etc.)
- **Location**: Path configured in `UniPlaySongSettings.FFmpegPath`
- **Process Execution**: Called via `Process.Start()` in `AudioNormalizationService`

**FFmpeg Commands Used:**
```bash
# Audio normalization (EBU R128)
ffmpeg -i input.mp3 -af "loudnorm=I=-16:TP=-1.5:LRA=11" output.mp3

# Audio analysis
ffmpeg -i input.mp3 -af "loudnorm=print_format=json" -f null -
```

**Configuration:**
```csharp
// Settings property
public string FFmpegPath { get; set; }

// Usage in AudioNormalizationService
Process.Start(new ProcessStartInfo
{
    FileName = ffmpegPath,
    Arguments = $"-i \"{inputFile}\" -af \"loudnorm=I={targetLoudness}:TP={truePeak}:LRA={loudnessRange}\" \"{outputFile}\"",
    // ...
});
```

## Dependency Management

### NuGet Package Restoration

**Automatic Restoration:**
- Visual Studio automatically restores packages on solution open
- `dotnet restore` or `msbuild /t:Restore` can be used manually

**Package Sources:**
- Default: `https://api.nuget.org/v3/index.json`
- Configured in Visual Studio: Tools → NuGet Package Manager → Package Manager Settings

**Version Pinning:**
- All packages use specific versions (no wildcards)
- Versions are pinned in `UniPlaySong.csproj` for reproducibility

### Native DLL Management

**Included in Repository:**
- SDL2 DLLs are committed to `lib/` directory
- Version information in `lib/README_SDL2_DLLs.md`

**Packaging:**
- `package_extension.ps1` checks for DLLs and includes them automatically
- Missing DLLs trigger warnings but don't fail the build

**Updates:**
- To update SDL2 DLLs:
  1. Download new versions from GitHub releases
  2. Replace files in `lib/` directory
  3. Update version info in README
  4. Test thoroughly before committing

### External Tool Validation

**Path Validation:**
- Extension validates tool paths before use
- Shows user-friendly error messages if tools are missing

**Validation Methods:**
```csharp
// In AudioNormalizationService
public bool ValidateFFmpegAvailable(string ffmpegPath)
{
    try
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = "-version",
            // ...
        });
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}
```

## Dependency Versions

### Current Versions (as of v1.0.6)

| Package | Version | Purpose |
|---------|--------|---------|
| PlayniteSDK | 6.11.0.0 | Playnite API |
| Microsoft.CSharp | 4.7.0 | C# language support |
| HtmlAgilityPack | 1.11.46 | HTML parsing |
| System.Net.Http | 4.3.4 | HTTP client |
| MaterialDesignThemes | 4.7.0 | UI components |
| MaterialDesignColors | 2.1.0 | Color palette |
| Newtonsoft.Json | 13.0.1 | JSON serialization |
| System.ValueTuple | 4.5.0 | Tuple support |
| SDL2.dll | 2.30.5 | Audio core |
| SDL2_mixer.dll | 2.8.0 | Audio mixer |

### Version Compatibility

**PlayniteSDK:**
- Compatible with Playnite 9.x and 10.x
- Tested with Playnite 10.x
- May require updates for future Playnite versions

**.NET Framework:**
- Target: .NET Framework 4.6.2
- All NuGet packages must support .NET Framework 4.6.2
- Some packages may require newer frameworks (handled via compatibility shims)

**SDL2:**
- SDL2 2.x.x series (backward compatible within major version)
- SDL2_mixer 2.x.x series (backward compatible within major version)

## Build-Time Dependencies

### Visual Studio / MSBuild
- **Required**: Visual Studio 2019+ or .NET SDK with MSBuild
- **Purpose**: Building the extension
- **Notes**: .NET Framework 4.6.2 Developer Pack required

### PowerShell
- **Required**: PowerShell 5.1+ (Windows PowerShell)
- **Purpose**: Running `package_extension.ps1` packaging script
- **Notes**: PowerShell Core (7.x) may work but not tested

## Runtime Dependencies

### Playnite
- **Required**: Playnite 9.x or 10.x
- **Purpose**: Extension host
- **Notes**: Extension is loaded by Playnite at runtime

### Windows
- **Required**: Windows 7+ (x64)
- **Purpose**: Operating system
- **Notes**: SDL2 DLLs are x64 only

## Optional Dependencies

### yt-dlp (Optional)
- **Required For**: YouTube downloads only
- **Without It**: YouTube download feature unavailable
- **Fallback**: User can still use KHInsider downloads

### FFmpeg (Optional)
- **Required For**: Audio normalization only
- **Without It**: Normalization feature unavailable
- **Fallback**: Music playback works without normalization

## Dependency Troubleshooting

### NuGet Package Issues

**Issue**: "Unable to find package PlayniteSDK"
- **Solution**: Ensure NuGet package source `https://api.nuget.org/v3/index.json` is enabled
- **Check**: Tools → NuGet Package Manager → Package Manager Settings → Package Sources

**Issue**: "Reference assemblies for .NETFramework,Version=v4.6.2 were not found"
- **Solution**: Install .NET Framework 4.6.2 Developer Pack
- **Download**: https://dotnet.microsoft.com/download/dotnet-framework/net462

### Native DLL Issues

**Issue**: "SDL2.dll not found" or "SDL2_mixer.dll not found"
- **Solution**: Ensure DLLs are in `lib/` directory
- **Check**: Verify files exist: `lib/SDL2.dll`, `lib/SDL2_mixer.dll`
- **Fallback**: Extension will use WPF MediaPlayer if SDL2 fails

**Issue**: "BadImageFormatException" when loading SDL2
- **Solution**: Ensure x64 DLLs are used (not x86)
- **Check**: Verify DLL architecture matches Playnite process (x64)

### External Tool Issues

**Issue**: "yt-dlp not found" when downloading from YouTube
- **Solution**: Configure `YtDlpPath` in extension settings
- **Validation**: Extension validates path before use

**Issue**: "FFmpeg not found" when normalizing audio
- **Solution**: Configure `FFmpegPath` in extension settings
- **Validation**: Extension validates path before use

## Dependency Updates

### Updating NuGet Packages

1. **Check for Updates:**
   ```powershell
   # In Visual Studio Package Manager Console
   Update-Package -reinstall
   ```

2. **Test Thoroughly:**
   - Build and test extension
   - Verify all features work
   - Check for breaking changes

3. **Update Version:**
   - Update `version.txt` if needed
   - Update changelog

### Updating Native DLLs

1. **Download New Versions:**
   - SDL2: https://github.com/libsdl-org/SDL/releases
   - SDL2_mixer: https://github.com/libsdl-org/SDL_mixer/releases

2. **Replace Files:**
   - Replace `lib/SDL2.dll`
   - Replace `lib/SDL2_mixer.dll`

3. **Test:**
   - Build and test extension
   - Verify audio playback works
   - Check for API changes (unlikely within major version)

### Updating External Tools

**User Responsibility:**
- Users update yt-dlp and FFmpeg independently
- Extension validates tool versions at runtime
- No extension code changes needed for tool updates

## License Compatibility

All dependencies are compatible with the MIT license:

- **PlayniteSDK**: MIT license (compatible)
- **NuGet Packages**: Various licenses (all compatible with MIT)
  - MaterialDesignThemes: MIT
  - MaterialDesignColors: MIT
  - HtmlAgilityPack: MIT
  - Newtonsoft.Json: MIT
- **SDL2/SDL2_mixer**: zlib license (compatible with MIT)
- **External Tools**: Users install independently (not bundled)
  - yt-dlp: Unlicense
  - FFmpeg: LGPL/GPL (users install separately)

> **Note**: For complete licensing information and attribution requirements, see the [Credits section](../../README.md#-credits) in the main README.

## Security Considerations

### NuGet Packages
- All packages from official NuGet.org source
- No custom package sources
- Versions pinned for reproducibility

### Native DLLs
- Official SDL2 releases from GitHub
- No modifications to DLLs
- Checksums can be verified from GitHub releases

### External Tools
- Users download tools independently
- Extension validates tool paths
- No code execution without user configuration

