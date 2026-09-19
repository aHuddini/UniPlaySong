# VirusTotal Audit — `gme.dll`, `z.dll`, `psf.dll`

Scan reports for the three retro-music engines in `src/Audio/Native/RetroChiptune/`.

| File | What it is | SHA-256 | Scanned | Report |
|---|---|---|---|---|
| `gme.dll` | Game Music Emu (chiptune: `.vgm`, `.nsf`, `.spc`, `.hes`, …), built by the maintainer | `187ab395972797f5629f4a9e61e88e75c31f6c5640776ca1b1bbc00f273f432a` | 2026-09-13 | [VirusTotal](https://www.virustotal.com/gui/file/187ab395972797f5629f4a9e61e88e75c31f6c5640776ca1b1bbc00f273f432a) |
| `z.dll` | zlib 1.3.2 (`.vgz` decompression for gme.dll), built by the maintainer | `b1f8e01096a7a0585a7a3738e3f371e332d8de4ddd88d8dab1ab51dfd8432560` | 2026-09-13 | [VirusTotal](https://www.virustotal.com/gui/file/b1f8e01096a7a0585a7a3738e3f371e332d8de4ddd88d8dab1ab51dfd8432560) |
| `psf.dll` | PlayStation PSF engine (`.psf`, `.minipsf`), first-party, built from `native/psf/` | `ddddfe7a7f87c87f29419f493d83ccf679bf9e1c276532a268a3687509f62875` | 2026-09-19 | [VirusTotal](https://www.virustotal.com/gui/file/ddddfe7a7f87c87f29419f493d83ccf679bf9e1c276532a268a3687509f62875) |

Reports are addressed by SHA-256, so each link shows current engine results. No export is kept - a
snapshot goes stale as engines update their definitions.

## Verify a file yourself

```bash
sha256sum gme.dll
```

Compare against the hash above, then open that file's report. A different hash means the report does
not describe the binary you have.

## Notes

These binaries are **unsigned** - no Authenticode certificate. What each one is and where it came
from: [`DLL-README.md`](DLL-README.md). Full package manifest:
[`SHIPPED_BINARIES.md`](../../../../docs/dev_docs/SHIPPED_BINARIES.md).

Re-submit and update this file whenever one of these binaries is rebuilt: a new build has a new hash,
which leaves the report describing a file that is no longer shipped.
