# Retro Chiptune Native Libraries

These DLLs enable UniPlaySong to play **retro game music** (chiptune formats like `.vgm` from Sega Genesis / Mega Drive) inside Playnite. They are bundled into the `.pext` package during build and loaded via P/Invoke at runtime by [GmeNative.cs](../../GmeNative.cs).

> **This folder is the source of truth** for the retro chiptune DLLs. A mirror copy also exists in [`lib/`](../../../../lib/) alongside the SDL2 DLLs for discoverability. When updating GME or zlib, update both locations. See [`lib/README_RetroChiptune_DLLs.md`](../../../../lib/README_RetroChiptune_DLLs.md).

## What's In This Folder

| File | Purpose | Architecture | License |
|------|---------|--------------|---------|
| `gme.dll` | [Game Music Emu](https://github.com/libgme/game-music-emu) — emulates retro sound chips (YM2612, SN76489, SPC700, NES APU, etc.) | x86 (32-bit) | LGPL v2.1+ |
| `z.dll` | [zlib](https://github.com/madler/zlib) 1.3.2 — decompression for `.vgz` (gzip-compressed VGM) | x86 (32-bit) | zlib license |

## Why x86?

Playnite runs as a **32-bit process**, so all P/Invoke DLLs must also be 32-bit. A 64-bit `gme.dll` would fail to load with `BadImageFormatException` at runtime.

## Why Two DLLs?

`gme.dll` links against zlib at build time for VGZ support (gzip-compressed VGM files). Rather than statically linking zlib into `gme.dll`, we keep them separate so `z.dll` is transparent and swappable for security updates.

## LGPL Compliance

Game Music Emu is LGPL v2.1+. UniPlaySong is MIT. The combination is legal because:

1. **Dynamic linking via P/Invoke** — we do not statically link or incorporate GME source into `UniPlaySong.dll`. `gme.dll` remains a separate, user-replaceable binary.
2. **Nuked OPN2 YM2612 core** — we build with `GME_YM2612_EMU=Nuked` (LGPL-safe). The MAME YM2612 core is NOT used; using it would make the library GPL v2+ and contaminate distribution.
3. **Attribution** — see the top-level [LICENSE](../../../../LICENSE) file for full third-party attribution.

## Build Source

These DLLs were built from source (not downloaded pre-compiled) to guarantee:
- x86 architecture (matches Playnite)
- Nuked OPN2 core (LGPL-safe)
- zlib integration enabled (VGZ support)

Build commands for reproducing (from the respective repo roots):

```bash
# zlib (from C:/Projects/zlib-build, checked out to tag v1.3.2)
mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A Win32 -DCMAKE_INSTALL_PREFIX=../install-x86
cmake --build . --config Release
cmake --install . --config Release

# GME (from C:/Projects/gme-build, main branch post-0.6.4)
mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A Win32 \
  -DGME_BUILD_SHARED=ON -DGME_BUILD_STATIC=OFF \
  -DGME_YM2612_EMU=Nuked \
  -DGME_BUILD_TESTING=OFF -DGME_BUILD_EXAMPLES=OFF \
  -DCMAKE_PREFIX_PATH=C:/Projects/zlib-build/install-x86
cmake --build . --config Release
```

## Packaging Flow

[`scripts/package_extension.ps1`](../../../../scripts/package_extension.ps1) copies these DLLs from this folder into the `.pext` package root, where Playnite loads them alongside `UniPlaySong.dll`.

## Related Files

- [GmeNative.cs](../../GmeNative.cs) — P/Invoke declarations for `gme.dll`
- [GmeReader.cs](../../GmeReader.cs) — `WaveStream + ISampleProvider` wrapper
- [SUPPORTED_FILE_FORMATS.md](../../../../docs/dev_docs/SUPPORTED_FILE_FORMATS.md) — All supported audio formats
- [DEPENDENCIES.md](../../../../docs/dev_docs/DEPENDENCIES.md) — Full dependency reference
