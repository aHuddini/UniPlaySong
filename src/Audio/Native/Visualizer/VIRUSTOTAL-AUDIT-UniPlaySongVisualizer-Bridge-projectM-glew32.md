# VirusTotal Audit — music visualizer (`UniPlaySongVisualizer.exe`, `UniPlaySongVisualizerBridge.dll`, `projectM-4.dll`, `glew32.dll`)

Scan record for the visualizer binaries in `src/Audio/Native/Visualizer/`: the first-party renderer and bridge
built from `native/visualizer/`, and projectM 4.1.8 + GLEW 2.2.0 built from the source tarballs in `lib/source/`.

| File | What it is | SHA-256 | Scanned | Report |
|---|---|---|---|---|
| `UniPlaySongVisualizer.exe` | First-party x64 renderer (projectM, offscreen OpenGL, shared D3D11 textures) | `b984353e6cb6de385329fcb886eda083301e0efbc9decc145e38fed3490fa8d6` | not yet - submit before release | [VirusTotal](https://www.virustotal.com/gui/file/b984353e6cb6de385329fcb886eda083301e0efbc9decc145e38fed3490fa8d6) |
| `UniPlaySongVisualizerBridge.dll` | First-party x86 bridge (D3D9Ex, opens the renderer's shared textures) | `37bcaa80976fb2908cd4b228a4c7929b5d3efe94146136ad4be60d7efd18078e` | not yet - submit before release | [VirusTotal](https://www.virustotal.com/gui/file/37bcaa80976fb2908cd4b228a4c7929b5d3efe94146136ad4be60d7efd18078e) |
| `projectM-4.dll` | projectM 4.1.8, x64, static MSVC runtime | `6ec38fa4ca701918fac274f1dfe0ba420f3ae76f16e09d0ae6e9ae1a3c6f3656` | not yet - submit before release | [VirusTotal](https://www.virustotal.com/gui/file/6ec38fa4ca701918fac274f1dfe0ba420f3ae76f16e09d0ae6e9ae1a3c6f3656) |
| `glew32.dll` | GLEW 2.2.0, x64, static MSVC runtime | `763fc6ad9da9948a37f9e7a61e109406e41f3b7e18473796442de394f7bfe60a` | not yet - submit before release | [VirusTotal](https://www.virustotal.com/gui/file/763fc6ad9da9948a37f9e7a61e109406e41f3b7e18473796442de394f7bfe60a) |

Reports are addressed by SHA-256, so each link shows current engine results once the file has been submitted
(until then it reports "not found"). No export is kept - a snapshot goes stale as engines update their definitions.

## Verify a file yourself

```bash
sha256sum UniPlaySongVisualizer.exe UniPlaySongVisualizerBridge.dll projectM-4.dll glew32.dll
```

Compare against the hashes above, then open that file's report. A different hash means the report does not describe
the binary you have.

## Notes

These binaries are **unsigned** - no Authenticode certificate. What each one is and where it came from:
[`DLL-README.md`](DLL-README.md). Full package manifest:
[`SHIPPED_BINARIES.md`](../../../../docs/dev_docs/SHIPPED_BINARIES.md).

Re-submit and update this file whenever one of these binaries is rebuilt: a new build has a new hash, which leaves
the report describing a file that is no longer shipped. (`DllDocumentationTests` fails until the hashes here match.)
