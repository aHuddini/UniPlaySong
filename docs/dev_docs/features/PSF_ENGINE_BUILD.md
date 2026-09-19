# PSF Engine (`psf.dll`) Build & Source Pin

UniPlaySong plays PlayStation music (`.psf`, `.minipsf` + `.psflib`) through `psf.dll`, a
first-party engine: the [ares](https://github.com/ares-emulator/ares) R3000 CPU and SPU cores,
driven by [aopsf](https://github.com/kode54/aopsf)'s HLE BIOS glue. Every line shipped is ISC,
BSD-2 or ours. Source in [`native/psf/`](../../../native/psf/).

## Why a first-party engine

A PSF is not a stream: it is a compressed PlayStation executable that must be *run* on an emulated
R3000 to drive an emulated SPU. Every existing PSF player either embeds Sony's BIOS (Highly
Experimental) or is GPL (aopsf's own SPU, SexyPSF, Mednafen, PCSX). The project rejected a GPL core
once already for `gme.dll`, so the engine is assembled from permissive parts instead.

## Source Pin

| Component | Licence | Upstream | Pinned commit | Files |
|---|---|---|---|---|
| ares R3000 interpreter | ISC | `ares-emulator/ares` | `17813a3ccda21ab9bd45f09bfc2f91196dbf50ff` | `native/psf/cpu/{interpreter*,exceptions,delay-slots}.cpp` verbatim; `ps1/cpu/cpu.hpp` from the checkout |
| ares SPU | ISC | `ares-emulator/ares` | same | `native/psf/spu/*.cpp` verbatim except `reverb.cpp` (`spu.patch`, two lines); `ps1/spu/spu.hpp` from the checkout |
| nall | ISC | `ares-emulator/ares` | same | headers only, from the checkout |
| aopsf glue | BSD-2 | `kode54/aopsf` | `f0371dba5bd7a371fd71e7d914a3ad093c399afa` | `native/psf/glue/{psx_hw.c,eng_psf.c,*.h}` verbatim |
| Bridges, stand-ins, harness | MIT (ours) | this repository | — | `native/psf/{spu_bridge,cpu_bridge}.cpp`, `cpu/{cpu,memory}.cpp`, `include/` |

Not shipped, not in the repository: aopsf's SPU (GPL-2) and its old-MAME R3000 (non-commercial).
The glue's `spu/spu.h` and `cpuintrf` interfaces are re-implemented over the ares cores by the
two bridge files.

No source archive is required (nothing is copyleft). ares' checkout is needed at build time only,
for its headers.

## Build Configuration

| Option | Value |
|---|---|
| Architecture | x86 (32-bit, required by Playnite) |
| Compiler | MSVC (Visual Studio 2022), `/std:c++20 /O2`, C for the glue |
| CRT | static — the DLL depends on `KERNEL32.dll` only |
| Link | `/Brepro`: two builds of the same sources produce the same bytes, so the recorded hash means something |
| Exports | `psf.def`: `psx_get_state_size`, `psx_set_refresh`, `psx_get_last_error`, `psf_load_section`, `psf_start`, `psf_gen`, `psf_stop` (Cdecl) |

Output: `psf.dll` (~185 KB) into `src/Audio/Native/RetroChiptune/`.

## Reproducing the Build

```
git clone --filter=blob:none --sparse https://github.com/ares-emulator/ares C:\Projects\ares
cd C:\Projects\ares
git sparse-checkout set ares/ps1 nall ares/ares
git checkout 17813a3ccda21ab9bd45f09bfc2f91196dbf50ff

cd <repo>\native\psf
build.cmd C:\Projects\ares
test\run.cmd
```

`test\run.cmd` renders three synthetic PSFs (manual FIFO upload, DMA upload, IRQ-driven driver)
through the harness and asserts the output. `obj\harness.exe <file.psf> <out.wav> [seconds]`
renders any PSF to WAV for listening.

After a rebuild: new hash in `src/Audio/Native/RetroChiptune/VIRUSTOTAL-AUDIT.md` and
[`SHIPPED_BINARIES.md`](../SHIPPED_BINARIES.md) (`DllDocumentationTests` fails until then), and a
fresh VirusTotal submission.

## Engine notes

- One engine per state buffer: `psx_get_state_size` bytes, caller-owned, constructed in place on
  first use. It holds pointers into itself, so the buffer must not move — `PsfReader` pins it.
- Output level matches hardware (nocash) at both volume stages. Highly Experimental applies the
  voice stage at half, and rips carry `volume` tags written against that; the tag is therefore
  **not** applied, or ares-level output would clip.
- Refresh rate comes from the primary EXE's region marker or the `_refresh` tag, never from a
  library's marker (spec), and must be set before the first section loads.
- The old-MAME core honoured load-delay slots only inside branch delay slots; ares honours them
  everywhere, as hardware does. Compiler output never depends on the difference.
- SPU IRQ (line 9) is not delivered to the CPU, matching aopsf's PS1 glue.
