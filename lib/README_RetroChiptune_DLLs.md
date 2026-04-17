# Retro Chiptune Native DLLs (GME + zlib)

The DLLs in this folder enable UniPlaySong to play retro game music formats (`.vgm` Sega Genesis, etc.) via [Game Music Emu](https://github.com/libgme/game-music-emu).

## Files

- `gme.dll` — Game Music Emu (~221 KB, x86, LGPL v2.1+)
- `z.dll` — zlib 1.3.2 (~77 KB, x86, zlib license) — required by GME for VGZ decompression

## Two Locations — Source of Truth

These DLLs exist in **two places** in the repo:

1. **[`src/Audio/Native/RetroChiptune/`](../src/Audio/Native/RetroChiptune/) — source of truth**
   - The folder name documents purpose and lives next to the code that uses it ([`GmeNative.cs`](../src/Audio/GmeNative.cs), [`GmeReader.cs`](../src/Audio/GmeReader.cs))
   - The `UniPlaySong.csproj` copies these into `bin/Release/net4.6.2/` automatically on build via `<CopyToOutputDirectory>`
   - The packaging script ([`scripts/package_extension.ps1`](../scripts/package_extension.ps1)) pulls from here into the `.pext`

2. **`lib/gme.dll` + `lib/z.dll` — mirror copy for discoverability**
   - Keeps all native DLLs visible alongside `SDL2.dll` and `SDL2_mixer.dll` for parity with existing conventions

**When updating GME or zlib:** Update both copies. The canonical build instructions live in [`src/Audio/Native/RetroChiptune/README.md`](../src/Audio/Native/RetroChiptune/README.md).

## Why x86?

Playnite runs as a 32-bit process, so all P/Invoke DLLs must also be 32-bit. A 64-bit build would fail with `BadImageFormatException` at runtime.

## Why Two DLLs?

`gme.dll` dynamically links to zlib at build time for VGZ (gzip-compressed VGM) support. We ship `z.dll` separately rather than statically linking it into `gme.dll` so it stays transparent and swappable for security updates.

## LGPL Compliance

Game Music Emu is LGPL v2.1+. UniPlaySong is MIT. The combination is legal because we use **dynamic linking** — `gme.dll` is a separate, user-replaceable binary loaded via P/Invoke. See the top-level [LICENSE](../LICENSE) for full attribution.

Our build uses the **Nuked OPN2** YM2612 core (LGPL-safe). The MAME YM2612 core is NOT used (it would make the library GPL v2+).
