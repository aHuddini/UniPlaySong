# `lib/` — committed binaries

Binaries checked into the repository rather than restored from a package feed. Everything here is
copied verbatim into the `.pext`.

Full manifest with SHA-256, provenance and licence for every shipped binary:
[`docs/dev_docs/SHIPPED_BINARIES.md`](../docs/dev_docs/SHIPPED_BINARIES.md).

## This folder

| File | What it is | Source | Licence |
|---|---|---|---|
| `SDL2.dll` | SDL2 core — audio init for `SDL2MusicPlayer`, the default backend | Official x64 release build 2.30.5, from [libsdl-org/SDL releases](https://github.com/libsdl-org/SDL/releases) (`SDL2-2.30.5-win32-x64.zip`) | zlib |
| `SDL2_mixer.dll` | SDL2_mixer — music loading, volume, position, end-of-track callback | Official release build 2.8.0, from [libsdl-org/SDL_mixer releases](https://github.com/libsdl-org/SDL_mixer/releases) | zlib |

Both are **unsigned** (no Authenticode certificate), as shipped by the SDL project. P/Invoke
declarations live in `src/Players/SDL/`.

> ⚠️ **These are not guaranteed to be what ships.** `scripts/package_extension.ps1` searches a
> sibling PlayniteSound build output and other installed Playnite extensions *before* falling back
> to this folder. Verify the SDL2 hashes before publishing a release — see the warning in
> [`SHIPPED_BINARIES.md`](../docs/dev_docs/SHIPPED_BINARIES.md).

## Subfolders

| Path | Contents |
|---|---|
| `source/` | `gme-source-1815b97.tar.gz` — corresponding source for the bundled `gme.dll`, kept for LGPL §6. Not shipped; it exists so the source stays available if upstream does not. |

`lib/dll/` was removed in v1.8.8. It held four assemblies that NuGet already restores, and the
packaging script preferred it over the build output — so a `PackageReference` bump did not change
what shipped. Managed dependencies now come from the restore only. Do not re-add a committed copy of
a NuGet assembly; `tests/Services/DllDocumentationTests.cs` fails if one appears.

## Native DLLs elsewhere in the repo

| Path | Contents |
|---|---|
| [`src/Audio/Native/`](../src/Audio/Native/) | `SpotifyLoopback.dll` — first-party capture shim |
| [`src/Audio/Native/RetroChiptune/`](../src/Audio/Native/RetroChiptune/) | `gme.dll`, `z.dll` — chiptune playback |
