# Supported File Formats

## Standard Audio

`.mp3` `.wav` `.ogg` `.flac` `.m4a` `.aac` `.wma` `.aif` `.mid`

Play on either backend (SDL2 default, NAudio when Live Effects/Visualizer is enabled).

## Retro Chiptune (v1.4.0+)

Played through [Game Music Emu (GME)](https://github.com/libgme/game-music-emu) — requires NAudio backend (auto-switches on load).

### Verified Working

| Extension | System | Status |
|-----------|--------|--------|
| `.vgm` | Sega Genesis / Mega Drive | **Tested** |

### Pipeline-Ready (code path works; individual test files not yet verified)

| Extension | System |
|-----------|--------|
| `.vgm` `.vgz` | Sega Master System / Game Gear |
| `.vgz` `.gym` | Sega Genesis / Mega Drive (compressed / alt formats) |
| `.spc` | Super Nintendo |
| `.nsf` `.nsfe` | Nintendo NES / Famicom |
| `.gbs` | Nintendo Game Boy |
| `.hes` | NEC TurboGrafx-16 / PC Engine |
| `.kss` | MSX |
| `.sap` | Atari (POKEY) |
| `.ay` | ZX Spectrum / Amstrad CPC |

### Known Limitation: GME Chip Support Is NOT Universal

VGM and VGZ are generic container formats that reference specific sound chips inside. GME only emulates a subset of the chips a VGM file can contain. If a VGM file references a chip GME doesn't emulate, **GME will "play" the file without erroring** — but the unsupported channels produce silence.

**VGM files GME handles well (the PSG / YM2612 / SPC700 / NES APU / Game Boy DMG family):**
- Sega Genesis / Mega Drive (YM2612 + SN76489)
- Sega Master System / Game Gear (SN76489)
- BBC Micro (SN76489)
- Super Nintendo (SPC700)
- Nintendo NES / Famicom (NES APU + VRC6/Namco106/FME-7)
- Nintendo Game Boy (DMG APU)
- ZX Spectrum / Amstrad CPC / Atari ST (AY8910)
- Atari 8-bit (POKEY)
- MSX (SCC, various Z80-era chips)

**VGM files GME does NOT emulate (these will play silent or near-silent):**

| Chip | Used by |
|------|---------|
| YM2610 | **Neo Geo** (AES/MVS) — arcade + home |
| YM2151 | Many **arcade boards** (Konami, Capcom CPS1, early SEGA arcade) |
| YM2203 | Various arcade, FM Towns |
| YM2608 | **NEC PC-88 / PC-98** |
| YMF262 / OPL3 | Sound Blaster 16 era PC music |
| SCSP | **Sega Saturn** |
| QSound | **Capcom CPS-2 / CPS-3** arcade |
| K051649 / K053260 | Konami arcade |
| Namco C140 / C352 | Namco arcade (System 1/2/21) |
| SegaPCM | Sega arcade (Space Harrier, OutRun, etc.) |

The vast majority of files on sites like [VGMRips](https://vgmrips.net/) are arcade / Neo Geo / PC-88 packs that use these unsupported chips. **Expect most VGMRips tracks to play silent through GME.** VGM files from Sega home consoles (and sites dedicated to Sega home music) will work.

### Detecting Unsupported VGM Files at Load Time

If UPS can detect an unsupported-chip VGM at load time and fail gracefully instead of playing silent, it would skip the track and fall through to default music. This is on the v1.4.1 task list — see `src/Audio/GmeReader.cs` for the VGM header inspection hook.

### Future: Broader Chip Support via libvgm

[libvgm](https://github.com/ValleyBell/libvgm) (by ValleyBell) has the missing chip cores — YM2610, YM2151, YM2608, QSound, etc. — and powers the `in_vgm-libvgm` Winamp plugin. Integrating it would unlock arcade / Neo Geo / PC-88 VGM support. **Not on the near-term roadmap** because:

1. libvgm exposes a C++ API (`PlayerA` class); a thin C wrapper DLL would be needed for P/Invoke.
2. No pre-built DLL releases — build from source, including zlib and chip cores.
3. License status is unclear; chip cores have mixed origins (some MAME-derived). Needs a legal read before redistribution.
4. Coexistence with GME would double the native-code maintenance surface.

Documented as a v1.5+ investigation. For now, the GME feature set is our chiptune ceiling.

## Implementation

- Extensions registered in [Constants.cs](../../src/Common/Constants.cs)
- Native DLLs: `gme.dll` (~221 KB, LGPL v2.1+), `z.dll` (~77 KB, zlib license) — both x86, stored in [`src/Audio/Native/RetroChiptune/`](../../src/Audio/Native/RetroChiptune/)

See [POTENTIAL_ISSUES.md](POTENTIAL_ISSUES.md) for the GME 1.5x gain boost rollback info.
