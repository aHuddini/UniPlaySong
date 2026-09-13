# `src/Audio/Native/` — native audio libraries

Loaded via P/Invoke at runtime. Copied into the build output by `src/UniPlaySong.csproj` and then
into the `.pext`.

Full manifest with SHA-256, provenance and licence for every shipped binary:
[`SHIPPED_BINARIES.md`](../../../docs/dev_docs/SHIPPED_BINARIES.md).

## This folder

| File | What it is | Source | Licence |
|---|---|---|---|
| `SpotifyLoopback.dll` | Capture shim that pulls Spotify's isolated PCM via Windows **Process Loopback Capture**, so Live Effects, Calm Down and the visualizer can run on Spotify audio. Managed .NET cannot do the async COM handshake (`ActivateAudioInterfaceAsync` + completion handler) the API requires, so a native shim is the only route. | **First-party** — built from [`native/SpotifyLoopback/`](../../../native/SpotifyLoopback/) in this repo (C++/WinRT) | MIT |

**x86, and it must be.** Playnite is a 32-bit host, so UniPlaySong (AnyCPU) loads as x86 and can only
P/Invoke an x86 DLL. An x64 build fails at `LoadLibrary` with `0x8007000B` (`ERROR_BAD_EXE_FORMAT`).

**Rebuild:**

```
msbuild native/SpotifyLoopback/SpotifyLoopback.vcxproj /p:Configuration=Release /p:Platform=Win32
```

VS 2022 v143, Windows SDK 10.0.26100. Links `ole32.lib` + `mmdevapi.lib`. `SpotifyLoopback.def` keeps
the `__stdcall` exports undecorated so P/Invoke-by-name resolves on x86. Output lands in
`native/SpotifyLoopback/Release/`; copy it here.

Exports three functions — `SpotifyLoopback_Start(pid, callback, user)`, `_Stop()`, `_IsCapturing()` —
called from `src/Common/SpotifyLoopbackClient.cs`.

## ⚠️ Unsigned and self-built

No Authenticode signature. Most likely
binary here to be flagged: reading another process's audio by PID is also spyware behaviour, and a
heuristic engine cannot separate the two.

Verify independently: hash it against the manifest and rescan it yourself, rebuild from the source above, read
`SpotifyLoopbackCapture.cpp`, or delete it — that disables Spotify live effects only (gated on
`OsCapabilities.SupportsProcessLoopback`, fails soft to dry audio).

## Subfolder

| Path | Contents |
|---|---|
| [`RetroChiptune/`](RetroChiptune/) | `gme.dll` + `z.dll` — retro chiptune playback. Both built from source; see its README. |

## Scan reports

[`VIRUSTOTAL-AUDIT.md`](VIRUSTOTAL-AUDIT.md) — SHA-256 and VirusTotal report for each file here.
