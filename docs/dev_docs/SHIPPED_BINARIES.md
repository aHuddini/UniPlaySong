# Shipped Binaries

Every `.dll` inside the UniPlaySong `.pext`, where it comes from, and under what licence.

**Why this file exists.** The `.pext` is a zip of binaries that users install into Playnite. Five of
them are checked into this repository rather than restored from a package feed - and those five are
exactly the native ones, **all unsigned**, three built by hand by the maintainer. Without a
manifest there is no way to answer "where did this DLL come from" without reverse-engineering the
packaging script, and no way to tell a tampered artefact from a genuine one.

Verified against `UniPlaySong.a1b2c3d4-e5f6-7890-abcd-ef1234567890_1_8_8.pext` (**v1.8.8**).

---

## The three ways a DLL gets into the package

| Class | Meaning | Reproducible from this repo alone? |
|---|---|---|
| **Built here** | Compiled from `src/` by `dotnet build`. | Yes |
| **Committed** | A binary checked into this repository and copied verbatim. | Yes - the bytes are in git |
| **NuGet** | Restored from nuget.org at build time via `PackageReference`. | Yes, at the versions pinned in `src/UniPlaySong.csproj` |

Nothing is downloaded at *runtime*, and nothing is fetched from an unpinned source at build time -
**with one exception, documented immediately below.**

---

## WARNING: SDL2 is sourced from the build machine, not from this repository

`scripts/package_extension.ps1` looks for `SDL2.dll` and `SDL2_mixer.dll` in **three** places and
takes the first hit. This repository's own copy is checked **last**:

1. `../src/PlayniteSound/bin/Release/net4.6.2` - a *sibling project's* build output
2. `<AppData>/Playnite/Extensions/*Sound*` or `*9c960604*` - **another installed extension's folder**
3. `lib/` - this repository

So on a machine where Playnite Sound (or a fork of it) is installed, the SDL2 that ships is
**whatever that other extension carries**, not what is committed here. Two people building the same
commit can produce `.pext` files containing different SDL2 binaries, and neither would be told.

**State as of v1.8.8:** paths 1 and 2 held no SDL2 on the maintainer's machine, so `lib/` won and the
shipped bytes match the committed ones (hashes below). That is luck, not design.

**Recommended fix:** make `lib/` the only search path and fail the build when it is missing. The
other two paths are historical, from when UPS was developed alongside PlayniteSound. Until that
changes, **verify the SDL2 hashes below before publishing a release.**

---

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

`UniPlaySong.dll` is **excluded** — the C# compiler embeds a fresh MVID and timestamp on every
build, so its hash changes even when no source does. Verified: two consecutive `dotnet build -c
Release` runs with an unchanged tree produced `5905376a...` then `a687b643...`. Checking it would
fail every time, and a check that always fails is a check nobody runs. Its provenance is the commit
it was built from, not a digest.

The remaining 23 are third-party or native binaries that do not change unless deliberately updated.

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

---

## Where each committed binary is picked up

Confirmed from a live `package_extension.ps1` run, not from reading the script:

| Committed path | Ships? | Picked up by |
|---|---|---|
| `lib/SDL2.dll`, `lib/SDL2_mixer.dll` | Yes | packaging script, **third** in a three-path search - see the warning above |
| `src/Audio/Native/RetroChiptune/{gme,z}.dll` | Yes | packaging script, direct path |
| `src/Audio/Native/SpotifyLoopback.dll` | Yes | csproj copy to build output, then packaged |

### Removed in v1.8.8: two unused duplicates

`lib/gme.dll` and `lib/z.dll` were byte-identical copies of the `RetroChiptune` ones and were picked
up by nothing. Deleted. A duplicate that agrees today is a duplicate nobody checks tomorrow: update
one and forget the other, and the repository would *show* a new GME while *shipping* the old one -
with the LGPL source archive in `lib/source/` then matching neither.

Not guarded by a test. Re-adding one would be a deliberate act, and the `DLL-README.md` rule below
would make whoever did it document the file anyway - a test asserting that a past cleanup stays done
is not testing a behaviour.

### Removed in v1.8.8: `lib/dll/`

Four managed assemblies - HtmlAgilityPack, MaterialDesignColors, MaterialDesignThemes.Wpf,
Microsoft.Xaml.Behaviors - were committed under `lib/dll/` and the packaging script copied them from
there **in preference to the build output**. They were present in the very first commit, alongside a
csproj that already used `PackageReference`, so they were never a pre-NuGet fallback; the folder's
own README described it as a "one-stop-shop" for eyeballing that dependencies were present.

The cost of that convenience was real: **bumping a `PackageReference` did not change what shipped.**
Raise MaterialDesignThemes to 5.x, rebuild, package - and the `.pext` still carried 4.7.0, silently,
because the committed copy won.

Deleted, along with the `lib\dll`-first branch in `scripts/package_extension.ps1`. Verified by
packaging with and without the folder and diffing every hash: **byte-identical, all 24 DLLs.** The
restore was always producing the same assemblies; the committed copies did nothing but create a
second answer to the same question.

Guarded by `tests/Services/DllDocumentationTests.cs`, which fails if an assembly restored from NuGet
is also found committed anywhere in the tree.

---

## Binary transparency: unsigned and self-built native code

**All five native DLLs in the `.pext` are unsigned.** Verified with `Get-AuthenticodeSignature`:
`SDL2.dll`, `SDL2_mixer.dll`, `gme.dll`, `z.dll` and `SpotifyLoopback.dll` all report `NotSigned`.
There is no Authenticode certificate on any of them, so Windows cannot attribute them to anyone and
SmartScreen has no publisher to check.

**Three** of them were **built by the maintainer**, not downloaded from the project that wrote the
source. Only the two SDL2 libraries are upstream release builds:

| File | Built by | From | Reproducible? |
|---|---|---|---|
| `SpotifyLoopback.dll` | Maintainer | `native/SpotifyLoopback/` in this repo (C++/WinRT) | Yes - `msbuild native/SpotifyLoopback/SpotifyLoopback.vcxproj /p:Configuration=Release /p:Platform=Win32` |
| `gme.dll` | Maintainer | libgme @ `1815b97`, CMake, `GME_YM2612_EMU=Nuked` | Yes - [`features/CHIPTUNE_GME_DLL_BUILD.md`](features/CHIPTUNE_GME_DLL_BUILD.md), source archived at `lib/source/gme-source-1815b97.tar.gz` |
| `z.dll` | Maintainer | zlib @ tag `v1.3.2`, CMake, Win32 | Yes - commands in [`../../src/Audio/Native/RetroChiptune/DLL-README.md`](../../src/Audio/Native/RetroChiptune/DLL-README.md). **No source archive is kept for zlib** (its licence does not require one), so reproduction depends on the upstream tag remaining available. |
| `SDL2.dll`, `SDL2_mixer.dll` | *Upstream* | Official libsdl-org release builds (2.30.5 / 2.8.0) | n/a - downloaded, not built |

### These have not been submitted to a malware scanner by the maintainer

No scan report accompanies this project. Nobody has uploaded these binaries to VirusTotal or any
equivalent and published the result. That is stated plainly because the alternative - saying nothing -
lets a user assume a check happened that did not.

**What this means in practice.** `SpotifyLoopback.dll` is the one most likely to alarm a scanner, and
for a reason worth understanding rather than dismissing: it calls `ActivateAudioInterfaceAsync` to
capture another process's audio output by PID. Reading audio out of a named process is genuinely
what some spyware does. Here it exists because Live Effects, Calm Down and the visualizer cannot run
on Spotify audio otherwise, and managed .NET cannot perform the async COM handshake the API requires.
A heuristic engine cannot tell those apart, so a flag is plausible and would not necessarily be wrong
about the *behaviour* - only about the intent.

**How to verify any of this yourself, without trusting the maintainer:**

1. **Check the hash** against the SHA-256 block above, then upload the file to VirusTotal yourself.
   The hash also tells you whether the binary you have is the one this document describes.
2. **Rebuild from source.** Both self-built DLLs have committed sources and published build
   instructions. A rebuild will not be byte-identical - neither toolchain is reproducible - but the
   exports and behaviour are inspectable.
3. **Read the source.** `native/SpotifyLoopback/SpotifyLoopbackCapture.cpp` is a few hundred lines
   and exports exactly three functions: `SpotifyLoopback_Start`, `_Stop`, `_IsCapturing`
   (`SpotifyLoopback.def`).
4. **Delete it.** Removing `SpotifyLoopback.dll` from the installed extension folder disables Spotify
   live effects and nothing else - the feature is gated on `OsCapabilities.SupportsProcessLoopback`
   and fails soft to dry Spotify audio.

**If this project ever ships a scan report or a signing certificate, this section is where it goes.**
Until then it says no, because it is no.

---

## Licences

Licence texts and attributions live in [`NOTICES.txt`](../../NOTICES.txt), bundled into the `.pext`.
Two components carry copyleft terms; both are dynamically linked, which is what keeps them
compatible with UniPlaySong's MIT licence:

- **`gme.dll`** - LGPL-2.1-or-later. Corresponding source archived at
  `lib/source/gme-source-1815b97.tar.gz` for LGPL section 6. Build instructions:
  [`features/CHIPTUNE_GME_DLL_BUILD.md`](features/CHIPTUNE_GME_DLL_BUILD.md).
- **`TagLibSharp.dll`** - LGPL-2.1, consumed unmodified as a NuGet package.

Gaps in `NOTICES.txt` as of v1.8.8:

- **SkiaSharp is not listed.** It ships (`SkiaSharp.dll`, MIT, Microsoft) and needs an entry.
- The TagLib entry names the file `taglib-sharp.dll`; the shipped file is `TagLibSharp.dll`.
- The `System.*` / `Microsoft.*` BCL shims are not listed individually. All are MIT under the
  .NET Foundation; one covering line would be more honest than silence.

---

## Per-folder documentation

Every folder in the repository that contains a `.dll` carries a **`DLL-README.md`** naming each
file, what it is for, and where it came from.

The filename is deliberate. A folder's `README.md` is about the folder; `DLL-README.md` is
specifically the provenance record for binaries in it — so a diff touching a binary's paperwork is
identifiable without opening anything, and a folder can still gain an ordinary README later without
the two colliding.

| Folder | Contents | Doc |
|---|---|---|
| `lib/` | `SDL2.dll`, `SDL2_mixer.dll` | [`lib/DLL-README.md`](../../lib/DLL-README.md) |
| `src/Audio/Native/` | `SpotifyLoopback.dll` | [`src/Audio/Native/DLL-README.md`](../../src/Audio/Native/DLL-README.md) |
| `src/Audio/Native/RetroChiptune/` | `gme.dll`, `z.dll` | [`DLL-README.md`](../../src/Audio/Native/RetroChiptune/DLL-README.md) |

Guarded by `tests/Services/DllDocumentationTests.cs`, which fails if a folder gains a `.dll` without
a `DLL-README.md` beside it. A plain `README.md` does not satisfy it.

---

## Verifying a package

Confirms a built `.pext` contains exactly the binaries listed above and nothing else:

```bash
# 1. Extract
unzip -o -d /tmp/pext pext/UniPlaySong.*_1_8_8.pext

# 2. Hash every DLL except our own, compare against the SHA-256 block above
cd /tmp/pext && sha256sum *.dll | grep -v ' UniPlaySong.dll$' | sort -k2

# 3. Confirm the committed native DLLs are the ones that shipped
sha256sum lib/SDL2.dll lib/SDL2_mixer.dll           src/Audio/Native/RetroChiptune/gme.dll           src/Audio/Native/RetroChiptune/z.dll           src/Audio/Native/SpotifyLoopback.dll
```

A mismatch on SDL2 almost certainly means the packaging script took it from another extension on the
build machine - see the warning above.

**Regenerating this manifest** after a release: repeat step 2 and replace the SHA-256 block. Bump the
version at the top with it, or the hashes describe a build nobody has.

A changed hash on any of the 23 is a real signal. Either someone deliberately updated a dependency —
in which case this file, [`DEPENDENCIES.md`](DEPENDENCIES.md) and [`NOTICES.txt`](../../NOTICES.txt)
all need the same update — or the packaging script picked a binary up from somewhere unexpected, which
for SDL2 it is designed to do.
