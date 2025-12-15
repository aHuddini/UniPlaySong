# Release Preparation Summary

This document summarizes the updates made to prepare UniPlaySong v1.0.6 for GitHub release.

## Documentation Updates

### Main README.md
- ✅ Updated fade duration range from (0.05-2.0s) to (0.05-10.0s)
- ✅ Added "Native Music Suppression" settings section
- ✅ All features and usage instructions are current and accurate

### CHANGELOG.md
- ✅ Added "Native Music Suppression Optimization" section to v1.0.6
- ✅ Documented the improvements: early suppression, extended monitoring window, faster polling
- ✅ Complete version history from 1.0.3.0 to 1.0.6

### LICENSE
- ✅ Added full MIT License text
- ✅ Ready for GitHub (was previously empty)

### Developer Documentation
- ✅ `docs/README.md` - Index of all documentation
- ✅ `docs/dev_docs/README.md` - Developer documentation index
- ✅ `docs/BUILD_INSTRUCTIONS.md` - Build and packaging guide
- ✅ All technical documentation is organized and accessible

### New Files Created
- ✅ `docs/GITHUB_RELEASE_CHECKLIST.md` - Pre-release checklist and guidelines
- ✅ `.gitignore` - Git ignore patterns for build artifacts and temporary files

## Current Version

**Version**: 1.0.6
- Stored in: `version.txt`
- Updated in: `docs/CHANGELOG.md`

## Key Features for Release

### Major Features
1. **Fullscreen Xbox Controller Support** 🎮
   - Complete music management from fullscreen mode
   - Controller-optimized dialogs with Material Design
   - Download, set primary, delete, normalize - all from controller

2. **Audio Normalization**
   - FFmpeg-based two-pass loudnorm normalization
   - EBU R128 standard compliance
   - Space saver and preservation modes
   - Fullscreen menu integration

3. **Native Music Suppression**
   - Optimized for consistent behavior
   - Early suppression in constructor
   - Extended monitoring window (15 seconds)
   - Faster detection (50ms polling)

4. **Song Randomization**
   - Randomize on game selection
   - Randomize when song ends
   - Smart repeat avoidance

## Files Ready for GitHub

### Source Code
- ✅ All `.cs` files (C# source code)
- ✅ All `.xaml` files (UI definitions)
- ✅ Project files (`.csproj`, `.sln`)
- ✅ Configuration files (`extension.yaml`, `version.txt`)

### Documentation
- ✅ `README.md` - Main user documentation
- ✅ `LICENSE` - MIT License
- ✅ `docs/CHANGELOG.md` - Version history
- ✅ `docs/README.md` - Documentation index
- ✅ `docs/BUILD_INSTRUCTIONS.md` - Build guide
- ✅ `docs/dev_docs/` - Developer documentation

### Dependencies
- ✅ `lib/SDL2.dll` - SDL2 core library
- ✅ `lib/SDL2_mixer.dll` - SDL2 audio mixer
- ✅ `lib/dll/` - Third-party DLLs (Material Design, HtmlAgilityPack, etc.)

### Build Scripts
- ✅ `package_extension.ps1` - Packaging script with version management

### Excluded Files (via .gitignore)
- ❌ `bin/` - Build output directory
- ❌ `obj/` - Build intermediate files
- ❌ `*.pext` - Package files (build artifacts)
- ❌ `backup_*/` - Backup directories
- ❌ IDE-specific files

## Pre-Release Checklist

Before creating the GitHub release:

1. **Build Verification**
   - [ ] Build project in Release configuration
   - [ ] Verify no build errors or warnings
   - [ ] Test that extension loads in Playnite

2. **Packaging**
   - [ ] Run `package_extension.ps1`
   - [ ] Verify `.pext` file is created
   - [ ] Test installing `.pext` file

3. **Documentation Review**
   - [ ] Verify README.md accuracy
   - [ ] Verify CHANGELOG.md completeness
   - [ ] Check that all links work

4. **GitHub Repository**
   - [ ] Initialize git repository (if not already)
   - [ ] Commit all files (respecting .gitignore)
   - [ ] Create release tag (e.g., `v1.0.6`)
   - [ ] Write release notes from CHANGELOG.md

5. **Release Notes**
   - [ ] Highlight major features (Controller Support, Normalization)
   - [ ] List improvements (Native Music Suppression optimization)
   - [ ] Include installation instructions
   - [ ] Link to full CHANGELOG.md

## Recommended GitHub Repository Structure

```
UniPlaySong/
├── README.md              # Main documentation
├── LICENSE                # MIT License
├── version.txt            # Current version
├── extension.yaml         # Extension manifest
├── UniPlaySong.csproj     # Project file
├── UniPlaySong.sln        # Solution file
├── package_extension.ps1  # Packaging script
├── .gitignore             # Git ignore patterns
│
├── UniPlaySong.cs         # Main plugin file
├── UniPlaySongSettings.cs # Settings model
├── UniPlaySongSettingsView.xaml
├── UniPlaySongSettingsViewModel.cs
│
├── Common/                # Shared utilities
├── Services/              # Business logic
├── Models/                # Data models
├── Views/                 # UI components
├── Players/               # Audio players
├── Menus/                 # Menu handlers
├── Downloaders/           # Download implementations
├── Monitors/              # Monitoring services
│
├── lib/                   # Dependencies (SDL2, DLLs)
├── docs/                  # Documentation
│   ├── README.md
│   ├── CHANGELOG.md
│   ├── BUILD_INSTRUCTIONS.md
│   └── dev_docs/
└── scripts/               # Utility scripts
```

## Release Notes Template

Use this template for the GitHub release:

```markdown
## UniPlaySong v1.0.6 - Fullscreen Controller Support & Audio Normalization

### 🎮 Major Features

**Fullscreen Xbox Controller Support**
- Complete music management from fullscreen mode using Xbox controller
- Download tracks/albums, set primary songs, delete files, normalize audio
- Controller-optimized Material Design dialogs
- Preview tracks with X/Y buttons

**Audio Normalization**
- FFmpeg-based two-pass loudnorm normalization (EBU R128 standard)
- Normalize all music or selected games
- Space saver mode (replace originals) or preservation mode (backup originals)
- Fullscreen menu integration

### ✨ Improvements

- Optimized native music suppression for consistent behavior
- Extended monitoring window for theme compatibility (ANIKI, etc.)
- Faster suppression detection (50ms polling)
- Song randomization options

### 📋 Requirements

- yt-dlp (for music downloads)
- FFmpeg (for audio normalization)

### 📖 Documentation

- [README.md](README.md) - Installation and usage guide
- [CHANGELOG.md](docs/CHANGELOG.md) - Complete version history

### 🛠️ For Developers

See `docs/dev_docs/` for technical documentation and architecture guides.
```

## Next Steps

1. Review all documentation for accuracy
2. Build and test the extension
3. Create GitHub repository
4. Commit files and create release
5. Publish release notes

---

**Last Updated**: 2025-12-15  
**Version**: 1.0.6
