# VirusTotal Audit

Scan reports for the `.dll` files in this folder. Audited **2026-09-13**.

| File | SHA-256 | Report |
|---|---|---|
| `SDL2.dll` | `ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef` | [VirusTotal](https://www.virustotal.com/gui/file/ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef) |
| `SDL2_mixer.dll` | `077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a` | [VirusTotal](https://www.virustotal.com/gui/file/077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a) |

Reports are addressed by SHA-256, so each link shows current engine results. No export is kept - a
snapshot goes stale as engines update their definitions.

## Verify a file yourself

```bash
sha256sum SDL2.dll
```

Compare against the hash above, then open that file's report. A different hash means the report does
not describe the binary you have.

## Notes

These binaries are **unsigned** - no Authenticode certificate. What each one is and where it came
from: [`DLL-README.md`](DLL-README.md). Full package manifest:
[`SHIPPED_BINARIES.md`](../docs/dev_docs/SHIPPED_BINARIES.md).

Re-submit and update this file whenever one of these binaries is rebuilt: a new build has a new hash,
which leaves the report describing a file that is no longer shipped.
