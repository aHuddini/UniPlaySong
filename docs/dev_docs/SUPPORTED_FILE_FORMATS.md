# Supported File Formats

## Standard Audio

`.mp3` `.wav` `.ogg` `.flac` `.m4a` `.aac` `.wma` `.aif` `.mid`

Play on either backend (SDL2 default, NAudio when Live Effects/Visualizer is enabled).

## Retro Chiptune (v1.4.0+)

Played through [Game Music Emu (GME)](https://github.com/libgme/game-music-emu) — requires NAudio backend (auto-switches on load).

| Extension | System | Status |
|-----------|--------|--------|
| `.vgm` | Sega Genesis / Mega Drive | **Tested** |
| `.vgz` `.gym` | Sega Genesis (compressed / alt) | Pipeline ready |
| `.spc` | Super Nintendo | Pipeline ready |
| `.nsf` `.nsfe` | Nintendo NES / Famicom | Pipeline ready |
| `.gbs` | Nintendo Game Boy | Pipeline ready |
| `.hes` | NEC TurboGrafx-16 / PC Engine | Pipeline ready |
| `.kss` | MSX | Pipeline ready |
| `.sap` | Atari | Pipeline ready |
| `.ay` | ZX Spectrum / Amstrad CPC | Pipeline ready |

"Pipeline ready" = wired through the same code path as `.vgm`, but individual test files have not been verified yet.

## Implementation

- Extensions registered in [Constants.cs](../../src/Common/Constants.cs)
- Native DLLs: `gme.dll` (~221 KB, LGPL v2.1+), `z.dll` (~77 KB, zlib license) — both x86, stored in [`src/Audio/Native/RetroChiptune/`](../../src/Audio/Native/RetroChiptune/)

See [POTENTIAL_ISSUES.md](POTENTIAL_ISSUES.md) for the GME 1.5x gain boost rollback info.
