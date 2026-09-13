# Shipped Binaries

Every `.dll` in the UniPlaySong `.pext`: what it is, where it comes from, its licence and its hash.

Current for **v1.8.8**.

## How a DLL gets into the package

| Class | Meaning |
|---|---|
| **Built here** | Compiled from `src/` by `dotnet build`. |
| **Committed** | Checked into this repository and copied verbatim. |
| **NuGet** | Restored from nuget.org at build time, at the versions in `src/UniPlaySong.csproj`. |

Nothing is fetched at runtime. Managed dependencies come from the NuGet restore only — do not commit
a copy of a restored assembly; `tests/Services/DllDocumentationTests.cs` fails if one appears.

## WARNING: SDL2 is taken from the build machine

`scripts/package_extension.ps1` searches three locations for `SDL2.dll` and `SDL2_mixer.dll` and uses
the first hit. This repository is checked **last**:

1. `../src/PlayniteSound/bin/Release/net4.6.2` — a sibling project's build output
2. `<AppData>/Playnite/Extensions/*Sound*` or `*9c960604*` — another installed extension
3. `lib/`

With Playnite Sound installed, the SDL2 that ships is whatever that extension carries. Two builds of
the same commit can contain different SDL2 binaries with no warning.

**Verify the SDL2 hashes below before publishing a release.** The fix is to make `lib/` the only
search path and fail when it is missing.

## Manifest

| File | Size | Provenance | Source | Licence |
|---|---:|---|---|---|
| `UniPlaySong.dll` | 2,033,152 | Built here | This repository (`src/`) | MIT |
| `SpotifyLoopback.dll` | 106,496 | Committed (first-party) | `src/Audio/Native/SpotifyLoopback.dll`, built from `native/SpotifyLoopback/` | MIT |
| `gme.dll` | 226,304 | Committed | `src/Audio/Native/RetroChiptune/gme.dll` | LGPL-2.1-or-later |
| `SDL2.dll` | 2,531,840 | Committed | `lib/SDL2.dll` — see the sourcing warning below | zlib |
| `SDL2_mixer.dll` | 306,688 | Committed | `lib/SDL2_mixer.dll` — see the sourcing warning below | zlib |
| `z.dll` | 78,336 | Committed | `src/Audio/Native/RetroChiptune/z.dll` | zlib |
| `FuzzySharp.dll` | 39,936 | NuGet | `FuzzySharp` 2.0.2 | MIT |
| `HtmlAgilityPack.dll` | 169,472 | NuGet | `HtmlAgilityPack` 1.11.46 (`lib/Net45/`) | MIT |
| `MaterialDesignColors.dll` | 302,592 | NuGet | `MaterialDesignColors` 2.1.0 | MIT |
| `MaterialDesignThemes.Wpf.dll` | 9,461,760 | NuGet | `MaterialDesignThemes` 4.7.0 | MIT |
| `NAudio.dll` | 513,536 | NuGet | `NAudio` 1.10.0 | MIT |
| `Newtonsoft.Json.dll` | 701,992 | NuGet | `Newtonsoft.Json` 13.0.1 | MIT |
| `NVorbis.dll` | 80,896 | NuGet | `NVorbis` 0.10.4 | MIT |
| `SkiaSharp.dll` | 806,464 | NuGet | `SkiaSharp` 2.88.9 | MIT |
| `TagLibSharp.dll` | 500,224 | NuGet | `TagLibSharp` 2.3.0 | LGPL-2.1 |
| `WindowsMediaController.dll` | 23,552 | NuGet | `Dubya.WindowsMediaController` 2.5.6 | MIT |
| `Microsoft.Extensions.Logging.Abstractions.dll` | 48,528 | NuGet (transitive) | via `Dubya.WindowsMediaController` | MIT |
| `Microsoft.Win32.Primitives.dll` | 21,216 | NuGet (transitive) | BCL shim, via `System.Net.Http` | MIT |
| `Microsoft.Xaml.Behaviors.dll` | 145,288 | NuGet (transitive) | via `MaterialDesignThemes` | MIT |
| `netstandard.dll` | 98,616 | NuGet (transitive) | .NET Standard facade | MIT |
| `System.Buffers.dll` | 20,856 | NuGet (transitive) | BCL shim, via `SkiaSharp`/`NAudio` | MIT |
| `System.Memory.dll` | 142,240 | NuGet (transitive) | BCL shim, via `SkiaSharp` | MIT |
| `System.Numerics.Vectors.dll` | 115,856 | NuGet (transitive) | BCL shim | MIT |
| `System.Runtime.CompilerServices.Unsafe.dll` | 16,768 | NuGet (transitive) | BCL shim | MIT |

### SHA-256

`UniPlaySong.dll` is excluded: the compiler embeds a fresh MVID per build, so its hash changes with
no source change. Its provenance is the commit it was built from.

```
d2212dbf28f2553525d8aac5579914928a62a8896ccf6242182511170a7b6f34  FuzzySharp.dll
187ab395972797f5629f4a9e61e88e75c31f6c5640776ca1b1bbc00f273f432a  gme.dll
37e643b9ef95d1fb21de79ad0b19825fc15aaaf43232c15e030e4c3bdba07714  HtmlAgilityPack.dll
8b336f71d5db105d77ddd9d86a52f2c894843d81b0c5883d0cb22aec85eb808a  MaterialDesignColors.dll
bbfdea24c6af2f8d9b57fdcf8873f136967c79c2a939c13427f46a27473326e3  MaterialDesignThemes.Wpf.dll
b4927848e511f089668b08671876a859ddf3561a4aaefa7a7d083ca472c7f215  Microsoft.Extensions.Logging.Abstractions.dll
5eaa2e82a26b0b302280d08f54dc9da25165dd0e286be52440a271285d63f695  Microsoft.Win32.Primitives.dll
b864da9d88414877cea9b1a016146265a5fb9d0e12f4dbb1dccc0cc998119a54  Microsoft.Xaml.Behaviors.dll
bc4bacc3b8b28d898f1671b79f216cca439f95eb60cd32d3e3ecafbecac42780  NAudio.dll
8be4a2270f8b2bea40f33f79869fdcca34e07bb764e63b81ded49d90d2b720dd  netstandard.dll
b624949df8b0e3a6153fdfb730a7c6f4990b6592ee0d922e1788433d276610f3  Newtonsoft.Json.dll
cf47c2fa6f44866b6001f006bdef491176163e8e14faf09b65964222483ebde1  NVorbis.dll
ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef  SDL2.dll
077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a  SDL2_mixer.dll
e70c3c876fc7fcb33a35738431076490eae47d348133bad0808d6abf5a716056  SkiaSharp.dll
2244f2f9df8c61b66d3145aeaf69c9e10a959b94266c1e6dd2f747ec16993a28  SpotifyLoopback.dll
accccfbe45d9f08ffeed9916e37b33e98c65be012cfff6e7fa7b67210ce1fefb  System.Buffers.dll
bf3fb84664f4097f1a8a9bc71a51dcf8cf1a905d4080a4d290da1730866e856f  System.Memory.dll
1d3ef8698281e7cf7371d1554afef5872b39f96c26da772210a33da041ba1183  System.Numerics.Vectors.dll
66409f670315afe8610f17a4d3a1ee52d72b6a46c544cec97544e8385f90ad74  System.Runtime.CompilerServices.Unsafe.dll
b1833a41ab1e933f7b006e5db15300b7223bfccc2c3b6689d49a9171dd27de1d  TagLibSharp.dll
f50a551e39e7714e1662ccf78f4e03b9be49e2704860fcbc4513d3d0d1879866  WindowsMediaController.dll
b1f8e01096a7a0585a7a3738e3f371e332d8de4ddd88d8dab1ab51dfd8432560  z.dll
```

## Where committed binaries are picked up

| Path | Picked up by |
|---|---|
| `lib/SDL2.dll`, `lib/SDL2_mixer.dll` | packaging script — third in the three-path search above |
| `src/Audio/Native/RetroChiptune/{gme,z}.dll` | packaging script, direct path |
| `src/Audio/Native/SpotifyLoopback.dll` | csproj copy to build output, then packaged |

## Binary transparency

**All five native DLLs are unsigned** — no Authenticode certificate, so Windows cannot attribute them
to a publisher. Three are compiled by the maintainer:

| File | From | Reproduce |
|---|---|---|
| `SpotifyLoopback.dll` | `native/SpotifyLoopback/` (C++/WinRT, first-party) | `msbuild native/SpotifyLoopback/SpotifyLoopback.vcxproj /p:Configuration=Release /p:Platform=Win32` |
| `gme.dll` | libgme @ `1815b97`, CMake, `GME_YM2612_EMU=Nuked` | [`features/CHIPTUNE_GME_DLL_BUILD.md`](features/CHIPTUNE_GME_DLL_BUILD.md); source at `lib/source/gme-source-1815b97.tar.gz` |
| `z.dll` | zlib @ tag `v1.3.2`, CMake, Win32 | [`RetroChiptune/DLL-README.md`](../../src/Audio/Native/RetroChiptune/DLL-README.md). No source archive is kept — zlib's licence does not require one. |

`SDL2.dll` and `SDL2_mixer.dll` are upstream libsdl-org release builds (2.30.5 / 2.8.0), unsigned as
shipped by that project.

### Scan reports

All five natives have VirusTotal reports, recorded in a `VIRUSTOTAL-AUDIT.md` beside the binaries
(see the per-folder table below). If a signing certificate is ever obtained, record it there too.

`SpotifyLoopback.dll` is the likeliest to be flagged. It calls `ActivateAudioInterfaceAsync` to
capture another process's audio by PID, which is also spyware behaviour; a heuristic engine cannot
separate the two. It exists because Live Effects, Calm Down and the visualizer cannot reach Spotify
audio otherwise, and managed .NET cannot do the async COM handshake the API requires.

To verify without trusting the maintainer: check the hash above and scan the file yourself, rebuild
from the committed source, read `native/SpotifyLoopback/SpotifyLoopbackCapture.cpp` (three exports,
listed in `SpotifyLoopback.def`), or delete it — that disables Spotify live effects and nothing else.

## Licences

Licence texts are in [`NOTICES.txt`](../../NOTICES.txt), bundled into the `.pext`. Every shipped
third-party DLL is named there; `UniPlaySong.dll` and `SpotifyLoopback.dll` are first-party and
covered by `LICENSE`.

Two components are copyleft, both dynamically linked, which is what keeps them compatible with
UniPlaySong's MIT licence:

- **`gme.dll`** — LGPL-2.1-or-later. Source archived for LGPL section 6.
- **`TagLibSharp.dll`** — LGPL-2.1, unmodified NuGet package.

## Per-folder documentation

Every folder containing a `.dll` carries a **`DLL-README.md`** naming each file, its purpose, source
and licence. Separate from `README.md` so a folder can carry both, and so a diff shows which changed.

| Folder | Contents | Docs |
|---|---|---|
| `lib/` | `SDL2.dll`, `SDL2_mixer.dll` | [`DLL-README`](../../lib/DLL-README.md) · [`VIRUSTOTAL-AUDIT`](../../lib/VIRUSTOTAL-AUDIT.md) |
| `src/Audio/Native/` | `SpotifyLoopback.dll` | [`DLL-README`](../../src/Audio/Native/DLL-README.md) · [`VIRUSTOTAL-AUDIT`](../../src/Audio/Native/VIRUSTOTAL-AUDIT.md) |
| `src/Audio/Native/RetroChiptune/` | `gme.dll`, `z.dll` | [`DLL-README`](../../src/Audio/Native/RetroChiptune/DLL-README.md) · [`VIRUSTOTAL-AUDIT`](../../src/Audio/Native/RetroChiptune/VIRUSTOTAL-AUDIT.md) |

`DllDocumentationTests` fails if a folder gains a `.dll` without one. A plain `README.md` does not
satisfy it.

## Verifying a package

```bash
unzip -o -d /tmp/pext pext/UniPlaySong.*_1_8_8.pext
cd /tmp/pext && sha256sum *.dll | grep -v ' UniPlaySong.dll$' | sort -k2
```

Compare against the SHA-256 block above. A mismatch on SDL2 means the packaging script took it from
another extension on the build machine.

After a release, regenerate the hash block and bump the version at the top of this file.
