# VirusTotal Audit — `SpotifyLoopback.dll`

Scan report for the Spotify loopback capture shim in `src/Audio/Native/`: first-party, built from
`native/SpotifyLoopback/`, captures Spotify's audio by process id so Live Effects can apply to it.

| File | What it is | SHA-256 | Scanned | Report |
|---|---|---|---|---|
| `SpotifyLoopback.dll` | First-party C++/WinRT shim, Windows Process Loopback Capture | `2244f2f9df8c61b66d3145aeaf69c9e10a959b94266c1e6dd2f747ec16993a28` | 2026-09-13 | [VirusTotal](https://www.virustotal.com/gui/file/2244f2f9df8c61b66d3145aeaf69c9e10a959b94266c1e6dd2f747ec16993a28) |

Reports are addressed by SHA-256, so each link shows current engine results. No export is kept - a
snapshot goes stale as engines update their definitions.

## Verify a file yourself

```bash
sha256sum SpotifyLoopback.dll
```

Compare against the hash above, then open that file's report. A different hash means the report does
not describe the binary you have.

## Notes

These binaries are **unsigned** - no Authenticode certificate. What each one is and where it came
from: [`DLL-README.md`](DLL-README.md). Full package manifest:
[`SHIPPED_BINARIES.md`](../../../docs/dev_docs/SHIPPED_BINARIES.md).

Re-submit and update this file whenever one of these binaries is rebuilt: a new build has a new hash,
which leaves the report describing a file that is no longer shipped.
