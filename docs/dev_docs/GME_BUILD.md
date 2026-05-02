# GME (Game Music Emu) Build & Source Pin

This document specifies the exact upstream version of [Game Music Emu](https://github.com/libgme/game-music-emu) that ships in UniPlaySong's `.pext`, why we picked it, and how to reproduce the binary from source.

UniPlaySong does **NOT** maintain a fork of GME. We ship the unmodified upstream binary with a specific build configuration. This document exists so anyone (auditor, contributor, end user) can verify that and rebuild it themselves.

## Source Pin

| Property | Value |
|---|---|
| Upstream repo | https://github.com/libgme/game-music-emu |
| Pinned commit | `1815b97e01e68b16a8f07daef8c71bd52f36d307` |
| Short SHA | `1815b97` |
| Position | Last code commit before the `0.6.5` release tag |
| Tarball | `lib/source/gme-source-1815b97.tar.gz` (in this repo) |
| Tarball SHA-256 | `db3aa7842fa8a7b738b8b04acb37327c3daedaa55dcb36a5096205f048b91cc9` |

The tarball is committed alongside this repo so the source remains available to UPS users even if upstream's repo or commit ever becomes unreachable. This satisfies LGPL §6's source-availability requirement without depending on a third party.

## Why This Specific Commit

`1815b97` was selected because it includes the **HES ADPCM emulator** backported from Kode54's fork. This was merged to upstream master before the 0.6.5 tag was cut. Picking this commit (rather than waiting for the 0.6.5 tag) was a deliberate choice when chiptune support was added in v1.4.0.

The 0.6.5 release that came after `1815b97` adds no functional code — only `changes.txt` edits, version-string bumps, and CMake metadata. Our build is functionally identical to released 0.6.5.

See `docs/dev_docs/SUPPORTED_FILE_FORMATS.md` for what formats this build supports and why HES required ADPCM.

## Build Configuration

| Option | Value | Reason |
|---|---|---|
| Architecture | x86 (32-bit) | Required by Playnite's plugin host |
| `GME_YM2612_EMU` | `Nuked` | LGPL-safe Yamaha YM2612 core (not MAME-derived; see "License Compatibility" below) |
| `BUILD_SHARED_LIBS` | `ON` | We P/Invoke the DLL from C# |
| `ENABLE_UBSAN` | `OFF` | Production build |
| Compiler | MSVC (Visual Studio 2019+ build tools) | Matches Playnite's runtime ABI |

Output: `gme.dll` (~221 KB) + `z.dll` (~77 KB, zlib 1.3.2 dependency for `.vgz` decompression).

## Reproducing the Build

```powershell
# 1. Verify the source tarball matches our pinned hash
$expected = "db3aa7842fa8a7b738b8b04acb37327c3daedaa55dcb36a5096205f048b91cc9"
$actual = (Get-FileHash -Algorithm SHA256 lib/source/gme-source-1815b97.tar.gz).Hash.ToLower()
if ($actual -ne $expected) { throw "GME source tarball hash mismatch" }

# 2. Extract
mkdir -Force build/gme
tar -xzf lib/source/gme-source-1815b97.tar.gz -C build/gme
cd build/gme/game-music-emu-1815b97e01e68b16a8f07daef8c71bd52f36d307

# 3. Configure (x86, Nuked YM2612)
cmake -B build -A Win32 `
  -DBUILD_SHARED_LIBS=ON `
  -DGME_YM2612_EMU=Nuked

# 4. Build
cmake --build build --config Release

# 5. Output
#    build/gme/Release/gme.dll  (the binary UPS ships)
```

To swap in your rebuilt DLL: copy `gme.dll` over `src/Audio/Native/RetroChiptune/gme.dll`, rebuild UPS, package.

## License Compatibility

GME is licensed under **LGPL v2.1+**. UPS is MIT. The combination is legitimate when:

1. ✅ GME is **dynamically linked** (not statically). UPS uses P/Invoke — `gme.dll` is loaded at runtime, not compiled into `UniPlaySong.dll`. Verified.
2. ✅ The LGPL'd source is available to recipients. Satisfied by the tarball at `lib/source/gme-source-1815b97.tar.gz` plus this build doc.
3. ✅ Recipients can **replace** the LGPL component. `gme.dll` is a separate file alongside `UniPlaySong.dll` in the user's Playnite extensions folder. Anyone can drop in their own rebuild of GME with the same exported symbols (matching the `gme_*` API in `Audio/GmeNative.cs`).
4. ✅ License notices are preserved. See `NOTICES.txt` at the repo root.
5. ✅ The chosen YM2612 emulator (`Nuked`) is itself LGPL — explicitly NOT the MAME-derived option (which is GPL and would be incompatible).

## When to Update the Pin

Update `1815b97` to a newer upstream commit if:

- Upstream tags **0.6.6** or later with a substantive changelog (currently in development as of May 2026, no tag yet).
- A user reports a real GME bug that newer upstream commits would fix (specific HES ADPCM playback issue, VRC7 NES audio glitch, security disclosure with CVE).
- We need a chip emulator that the pinned commit doesn't support.

Don't update for cosmetic / metadata commits. The current pin is good and tested. See `docs/dev_docs/roadmaps/FEATURE_IDEAS.md` for the deferred "evaluate 0.6.6" item.

## When to Fork (Don't, Yet)

Forking GME under an UPS-controlled repo would mean accepting maintenance ownership: tracking upstream, applying our own patches, responding to security disclosures. None of that is worth doing while:

- We have zero UPS-specific patches needing to be in the binary
- Upstream is still active (last release 2024, recent commits 2025-2026)
- The current binary works for our test files

If those conditions change, revisit the question. For now, "build pin + tarball + this doc" is the cheapest correct answer.
