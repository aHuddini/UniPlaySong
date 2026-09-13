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

> ⚠️ **Not guaranteed to be what ships.** `scripts/package_extension.ps1` searches a sibling
> PlayniteSound build output and other installed Playnite extensions before falling back here.
> Verify the SDL2 hashes before publishing a release — see
> [`SHIPPED_BINARIES.md`](../docs/dev_docs/SHIPPED_BINARIES.md).

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

| File | SHA-256 | Report |
|---|---|---|
| `SDL2.dll` | `ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef` | [VirusTotal](https://www.virustotal.com/gui/file/ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef) |
| `SDL2_mixer.dll` | `077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a` | [VirusTotal](https://www.virustotal.com/gui/file/077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a) |

Addressed by SHA-256, so each link shows current engine results. Hash your copy (`sha256sum`)
and compare — a different hash means the report does not describe the file you have.
