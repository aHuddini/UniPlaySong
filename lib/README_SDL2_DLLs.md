# SDL2 Native DLLs Required

The SDL2MusicPlayer requires native SDL2 DLLs to function. These can be copied from the PlayniteSound project or downloaded.

## Required DLLs

1. **SDL2.dll** - Core SDL2 library
2. **SDL2_mixer.dll** - SDL2 audio mixer library

## Status

✅ **SDL2 DLLs are already downloaded and ready!**

The DLLs have been automatically downloaded from GitHub and placed in this `lib\` directory:
- `SDL2.dll` (from SDL2 release 2.30.5)
- `SDL2_mixer.dll` (from SDL2_mixer release 2.8.0)

## Download Links (for reference)

### SDL2.dll
- **Official Releases**: https://github.com/libsdl-org/SDL/releases
- **Direct Download**: Look for `SDL2-2.x.x-win32-x64.zip` (or latest version)
- Extract `SDL2.dll` from the `lib\x64\` folder

### SDL2_mixer.dll
- **Official Releases**: https://github.com/libsdl-org/SDL_mixer/releases
- **Direct Download**: Look for `SDL2_mixer-2.x.x-win32-x64.zip` (or latest version)
- Extract `SDL2_mixer.dll` from the `lib\x64\` folder

## Installation Steps

1. Download both ZIP files from the GitHub releases
2. Extract the DLLs from the `lib\x64\` folder of each ZIP
3. Place both DLLs in this `lib\` directory:
   ```
   UniPSong/
   └── lib/
       ├── SDL2.dll
       └── SDL2_mixer.dll
   ```

## Verification

After placing the DLLs, the packaging script (`package_extension.ps1`) will automatically include them in the `.pext` package.

If the DLLs are missing, the extension will:
- Log a warning during packaging
- Fall back to WPF MediaPlayer (if SDL2 initialization fails)
- Display an error in Playnite logs if SDL2 is required but unavailable

## Notes

- **Architecture**: Use **x64** (64-bit) DLLs for Windows
- **Version**: Use the latest stable release (2.x.x)
- **License**: SDL2 is zlib-licensed (compatible with commercial use)

## Alternative: Manual Installation

If you prefer not to bundle the DLLs, users can:
1. Download SDL2 DLLs manually
2. Place them in the Playnite installation directory
3. Or place them in the extension's installed directory

However, bundling is recommended for easier distribution.

