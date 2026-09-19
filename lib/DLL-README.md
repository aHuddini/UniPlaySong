# `lib/` — committed binaries

Binaries checked into the repository rather than restored from a package feed. Copied verbatim into
the `.pext`.

Full manifest with SHA-256 and licences:
[`docs/dev_docs/SHIPPED_BINARIES.md`](../docs/dev_docs/SHIPPED_BINARIES.md).

| File | What it is | Source | Licence |
|---|---|---|---|
| `SDL2.dll` | SDL2 core — audio init for `SDL2MusicPlayer`, the default backend | Official x64 release build 2.30.5, [libsdl-org/SDL](https://github.com/libsdl-org/SDL/releases) | zlib |
| `SDL2_mixer.dll` | SDL2_mixer — music loading, volume, position, end-of-track callback | Official release build 2.8.0, [libsdl-org/SDL_mixer](https://github.com/libsdl-org/SDL_mixer/releases) | zlib |

Both **unsigned**, as shipped by the SDL project.
P/Invoke declarations in `src/Players/SDL/`.

Managed dependencies are restored from NuGet and must not be committed here; a test enforces it.

## Subfolder

| Path | Contents |
|---|---|
| `source/` | `gme-source-1815b97.tar.gz` — corresponding source for `gme.dll`, kept for LGPL §6. Not shipped. |

## Native DLLs elsewhere

| Path | Contents |
|---|---|
| [`src/Audio/Native/`](../src/Audio/Native/DLL-README.md) | `SpotifyLoopback.dll` |
| [`src/Audio/Native/RetroChiptune/`](../src/Audio/Native/RetroChiptune/DLL-README.md) | `gme.dll`, `z.dll` |

## Scan reports

[`VIRUSTOTAL-AUDIT-SDL2.md`](VIRUSTOTAL-AUDIT-SDL2.md) — SHA-256 and VirusTotal report for each file here.
