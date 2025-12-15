# GitHub Release Checklist

This checklist ensures the project is ready for GitHub release.

## Pre-Release Checklist

### Documentation
- [x] README.md is complete and accurate
- [x] CHANGELOG.md is up to date with latest version
- [x] LICENSE file contains MIT license text
- [x] All user-facing documentation is clear and accurate
- [x] Developer documentation is organized in `docs/dev_docs/`

### Code Quality
- [x] Code builds without errors
- [x] Code compiles in Release configuration
- [x] No obvious bugs or issues
- [x] Error handling is in place
- [x] Logging is comprehensive

### Version Management
- [x] `version.txt` contains current version (1.0.6)
- [x] `extension.yaml` will be updated by package script
- [x] `AssemblyInfo.cs` will be updated by package script

### Packaging
- [ ] Build project in Release configuration
- [ ] Run `package_extension.ps1` to create `.pext` file
- [ ] Verify `.pext` file is created correctly
- [ ] Test installing `.pext` file in Playnite

### GitHub Preparation
- [ ] Create `.gitignore` file (if not exists)
- [ ] Verify sensitive information is not committed
- [ ] Review files to be committed
- [ ] Prepare release notes from CHANGELOG.md

## Release Notes Template

### Version 1.0.6 - Fullscreen Controller Support & Audio Normalization

**Major Features:**
- 🎮 Complete Xbox controller support for fullscreen music management
- 🔊 Audio normalization with FFmpeg (EBU R128 standard)
- 🎵 Song randomization options
- 🎛️ Optimized native music suppression

**Improvements:**
- Native music suppression optimized for consistency
- Extended monitoring window for theme compatibility
- Faster suppression detection (50ms polling)

**Full Details:**
See [CHANGELOG.md](docs/CHANGELOG.md) for complete version history.

## Files to Commit

### Required Files
- All source code (`*.cs` files)
- UI files (`*.xaml`, `*.xaml.cs`)
- Project files (`*.csproj`, `*.sln`)
- Configuration files (`extension.yaml`, `version.txt`)
- Documentation (`docs/`, `README.md`)
- License (`LICENSE`)
- Build scripts (`package_extension.ps1`)
- SDL2 DLLs (`lib/SDL2.dll`, `lib/SDL2_mixer.dll`)

### Files to Exclude (via .gitignore)
- `bin/` directory
- `obj/` directory
- `*.pext` files (build artifacts)
- User-specific files
- Temporary files
- IDE-specific files (`.vs/`, `.idea/`, etc.)

## Post-Release

### After Publishing
- [ ] Verify release notes are accurate
- [ ] Check that download link works
- [ ] Monitor for user feedback
- [ ] Update any external documentation links

### Future Releases
- Follow semantic versioning (major.minor.patch)
- Update `version.txt` before creating release
- Update CHANGELOG.md with new version section
- Tag releases in git with version number
