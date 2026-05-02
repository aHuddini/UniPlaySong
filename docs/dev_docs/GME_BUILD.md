# GME (Game Music Emu) Build & Source Pin

UniPlaySong bundles the unmodified upstream `gme.dll` for retro chiptune playback. This document records which upstream version we ship, how it's built, and how to reproduce it from source — sufficient for LGPL §6 source-availability compliance.

## Source Pin

| Property | Value |
|---|---|
| Upstream repo | https://github.com/libgme/game-music-emu |
| Pinned commit | `1815b97e01e68b16a8f07daef8c71bd52f36d307` |
| Tarball (committed) | `lib/source/gme-source-1815b97.tar.gz` |
| Tarball SHA-256 | `db3aa7842fa8a7b738b8b04acb37327c3daedaa55dcb36a5096205f048b91cc9` |

The tarball is committed alongside this repo so the source remains available to UPS users even if upstream becomes unreachable.

## Build Configuration

| Option | Value |
|---|---|
| Architecture | x86 (32-bit, required by Playnite) |
| `GME_YM2612_EMU` | `Nuked` (LGPL-safe core, not the MAME variant) |
| `BUILD_SHARED_LIBS` | `ON` |
| Compiler | MSVC (Visual Studio 2019+ build tools) |

Output: `gme.dll` (~221 KB) plus `z.dll` (zlib 1.3.2, ~77 KB) for `.vgz` decompression.

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

# 3. Configure + build (x86, Nuked YM2612)
cmake -B build -A Win32 -DBUILD_SHARED_LIBS=ON -DGME_YM2612_EMU=Nuked
cmake --build build --config Release

# 4. Output: build/gme/Release/gme.dll
```

To use your rebuilt DLL: copy it over `src/Audio/Native/RetroChiptune/gme.dll`, rebuild UPS, repackage.

## License Compatibility

GME is **LGPL v2.1+**, UPS is **MIT**. Combined legitimately because:

1. `gme.dll` is **dynamically linked** via P/Invoke — not statically linked into `UniPlaySong.dll`.
2. The LGPL'd source is available: archived in this repo (`lib/source/`) and upstream at the pinned commit.
3. Recipients can replace `gme.dll` with their own build matching the `gme_*` API in `Audio/GmeNative.cs`.
4. License notices preserved in [`NOTICES.txt`](../../NOTICES.txt) (bundled in the `.pext`).
5. The Nuked YM2612 emulator is itself LGPL — explicitly NOT the MAME-derived option (which is GPL and would be incompatible).
