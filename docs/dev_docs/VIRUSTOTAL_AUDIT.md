# VirusTotal Audit

Scan reports for every `.dll` committed to this repository. Audited **2026-09-13**.

Reports are addressed by SHA-256, so each link always shows current engine results — no export is
kept, because a snapshot goes stale as engines update their definitions.

All five are **unsigned**; three are compiled by the maintainer. Provenance for each:
[`SHIPPED_BINARIES.md`](SHIPPED_BINARIES.md).

## Verify a file yourself

```bash
sha256sum <file>      # must match the hash below
```

Then open that file's report. If the hash differs from the one recorded here, the report does not
describe the binary you have.

## Reports

### `SDL2.dll`

- **Path:** `lib/SDL2.dll`
- **SHA-256:** `ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef`
- **Report:** https://www.virustotal.com/gui/file/ed9bb11fb27ba61c04d0165f299e053626665e9ab2b51afb74ae2c1dcff7ddef

### `SDL2_mixer.dll`

- **Path:** `lib/SDL2_mixer.dll`
- **SHA-256:** `077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a`
- **Report:** https://www.virustotal.com/gui/file/077d3426e56715fea53c8231343aa61ed47f36d6fb6800d54b0b449e37d2c79a

### `gme.dll`

- **Path:** `src/Audio/Native/RetroChiptune/gme.dll`
- **SHA-256:** `187ab395972797f5629f4a9e61e88e75c31f6c5640776ca1b1bbc00f273f432a`
- **Report:** https://www.virustotal.com/gui/file/187ab395972797f5629f4a9e61e88e75c31f6c5640776ca1b1bbc00f273f432a

### `z.dll`

- **Path:** `src/Audio/Native/RetroChiptune/z.dll`
- **SHA-256:** `b1f8e01096a7a0585a7a3738e3f371e332d8de4ddd88d8dab1ab51dfd8432560`
- **Report:** https://www.virustotal.com/gui/file/b1f8e01096a7a0585a7a3738e3f371e332d8de4ddd88d8dab1ab51dfd8432560

### `SpotifyLoopback.dll`

- **Path:** `src/Audio/Native/SpotifyLoopback.dll`
- **SHA-256:** `2244f2f9df8c61b66d3145aeaf69c9e10a959b94266c1e6dd2f747ec16993a28`
- **Report:** https://www.virustotal.com/gui/file/2244f2f9df8c61b66d3145aeaf69c9e10a959b94266c1e6dd2f747ec16993a28

## Scope

Only binaries committed to this repository. The other 19 DLLs in the `.pext` are restored from
nuget.org at build time at the versions pinned in `src/UniPlaySong.csproj` — they are not carried
here and are covered by their package feed.

## When to re-run

Any time one of these binaries is rebuilt or updated. A new build produces a new hash, which makes
the report above describe a file that is no longer shipped. Re-submit, then replace the hash and
link in this file and in [`SHIPPED_BINARIES.md`](SHIPPED_BINARIES.md).
