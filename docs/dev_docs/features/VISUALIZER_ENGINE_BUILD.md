# Music Visualizer — engine, transport and build

How the experimental MilkDrop visualizer (`UPS_MusicVisualControl`, setting **Music Visualizer for Themes**) works,
and how to rebuild its native parts. Theme authors want the
[theme guide](../Theme%20Support/THEME_INTEGRATION_GUIDE.md#music-visualizer--ups_musicvisualcontrol-experimental-next-release) instead.

## The shape of it

```
Playnite (x86)                                         UniPlaySongVisualizer.exe (x64, own process)
───────────────────────────────────────────            ─────────────────────────────────────────────
NAudio player ─► OutputClock.Read ─► VisualizerAudioFeed ══ <channel>_Audio ═══►  AudioRing ─► projectM
                                       (audio thread)            (ring)                  │
MusicVisualControl (UI thread, per WPF frame):                                    WGL pbuffer (GL 3.3)
  publish "frame being heard" ──────────────────────── <channel>_Audio.played           │ glReadPixels + luma key
  SetEvent ───────────────────────────────────────────── <channel>_Tick ──────►  one frame per signal
  read newest index ◄──────────────────────────────────── <channel>_Frames ◄──── 3 shared D3D11 textures
  bridge: D3D9Ex opens the shared texture, StretchRect ─► D3DImage (real transparency)
```

- **Why a separate process.** projectM needs an OpenGL context and is happiest with a 64-bit address space; neither
  belongs inside Playnite's x86 WPF process. The renderer lives in `Visualizer\` beside the plugin, so nothing ever
  tries to load its x64 binaries into Playnite. It exits on the `stop` flag, or when Playnite's process exits (it is
  given the pid) — so a crash never leaves it running.
- **Why shared textures + D3DImage.** WPF composes a `D3DImage` with real per-pixel alpha, so the theme shows through
  the visualizer. The x86 bridge opens the renderer's legacy-shared D3D11 textures as D3D9Ex textures (legacy DXGI
  shared handles are global and fit 32 bits, so the x86 side can open what the x64 side shared) and `StretchRect`s
  the newest into one render target — GPU to GPU.
- **Pacing is on demand.** The element signals `<channel>_Tick` from `CompositionTarget.Rendering`; the renderer
  draws one frame per signal. Free-running both sides at 60 Hz aliased badly (25–117 skipped frames in 8 s; on demand:
  1).
- **One element at a time.** The bridge holds one D3D9Ex device and one set of textures.
- **Starts and stops with visibility.** The renderer runs only while the element is visible *and* the setting is on.
  A collapsed element stays loaded, so `IsVisibleChanged`, not `Loaded`, drives it. The frame size is fixed per
  renderer, so a settled resize (400 ms debounce) restarts it; a restart waits for the old renderer to exit, because a
  new one opening the old shared memory would reset its stop flag and leave it running.
- **Names are per instance:** `Local\UniPlaySong_Visualizer_<playnite pid>_<n>` + `_Frames` / `_Tick` / `_Audio`.

## Shared memory — [`native/visualizer/visualizer_shared.h`](../../../native/visualizer/visualizer_shared.h)

Fixed-width fields only; the C# side mirrors the offsets (`MusicVisualControl.VisualizerBridge`, `VisualizerAudioFeed`).

**Frames (`VisFrames`, created by the renderer):** magic `VTX1`, width, height, the three shared handles, `latest`
(index of the newest finished buffer), `frame` (counter, written after `latest`), per-buffer QPC timestamps, `stop`.

**Audio (`VisAudio`, created by UniPlaySong before it starts the renderer):** magic `AUD1`, sample rate, capacity
(65,536 stereo frames ≈ 1.4 s), `written`, `played`, then interleaved stereo float.

- Frames are indexed by the player's own `OutputClock` count, so `written` (newest frame handed to the device) and
  `played` (frame being heard) share units. The output queue holds ~0.35 s; the renderer feeds projectM **up to
  `played`**, so the visuals follow the sound, not audio that hasn't been heard yet — the same correction as the
  experimental visualizer sync.
- Counters are 32-bit and compared with unsigned differences, so they survive wrapping (and a 64-bit field would tear
  between the x86 writer and the x64 reader).
- Only the clock of the **audible** player writes (`VisualizationDataProvider.Current`: game music, the library
  dashboard or a jingle), so two players never interleave. The renderer starts fresh a short window back after a
  pause, a seek or a player switch instead of replaying a backlog.
- The setting forces the NAudio engine, exactly like the spectrum visualizer and the equalizer: SDL2 has no output
  tap. Toggling it at runtime swaps engines only when nothing else already needs NAudio.

There is **no speaker capture** any more (the spike used WASAPI loopback). That captured games, browsers and
notifications too, and showed up to External Audio as "another app playing", pausing the music every half second.

## Transparency

`glReadPixels` → flip → premultiplied BGRA with coverage = the brightest channel (luma key): black is see-through,
bright is solid. The presets are written for it — every style fades out before the frame edge and crushes faint haze
to black, because any non-black pixel is partly visible and a faint tint over the whole frame shows the rectangle.

## Presets — [`native/visualizer/presets/make_presets.py`](../../../native/visualizer/presets/make_presets.py)

UniPlaySong's own presets (MIT); community packs carry no licence and are never bundled. One generator, five styles
(LED Bars, Glow Bars, Spark Bars, Aurora, Halo Ring); **only Glow Bars ships** for now — `build.cmd` copies it.

Each preset: per-frame code turns last frame's spectrum into log-spaced bars (auto-gain, 30 dB window, time-based
rise/fall, peak hold) and packs them into `q1..q32`; the warp shader draws the bars per pixel with trails; the comp
shader adds bloom from projectM's blur textures, a bass-driven backlight and the edge fade. A hidden spectrum wave
copies the FFT bins into `gmem` for the next frame. Every style frames a centre **art slot** (0.30 of the height,
wings start 0.19 out) so a page can put its cover art in the middle.

Costs measured at 1080p on an Intel Arc 140V (render / whole frame, p95): Glow Bars 3.8 / 11.1 ms; Halo Ring 5.4 /
13.1; LED Bars (per-pixel) 6.5 / 14.4; Aurora 6.8 / 14.4; Spark Bars 9.0 / 16.7. **Shapes cost ~6 µs each on the
CPU** (each instance runs its expression code and makes a draw call) — draw grids per pixel in a shader, keep shapes
to a few hundred. Bloom is near free.

projectM gotcha: after a preset with a composite shader, projectM leaves its own framebuffer bound for **reading** —
bind `GL_READ_FRAMEBUFFER` 0 before `glReadPixels`, or every such preset reads back black.

## Build

Needs Visual Studio 2022 (C++ workload) and CMake.

```
native\visualizer\build_deps.cmd    GLEW 2.2.0 + projectM 4.1.8 (x64) from lib\source into native\visualizer\obj\deps
native\visualizer\build.cmd         renderer (x64) + bridge (x86) into src\Audio\Native\Visualizer\, plus the libraries
                                    and the shipped preset
```

- **Sources, pinned:** `lib/source/projectm-source-4.1.8.tar.gz` (tag `v4.1.8` + its `projectm-eval` submodule;
  `docs/` and `background.png` left out — not build inputs; SHA-256 `246b8b6c…e748`) and
  `lib/source/glew-source-2.2.0.tgz` (the upstream release tarball, SHA-256 `d4fc8289…53e1`).
- **projectM options:** `BUILD_SHARED_LIBS=ON`, `ENABLE_PLAYLIST=OFF` (one preset, no playlist DLL), SDL UI, docs,
  tests, C++ interface and GLES off.
- **GLEW stays a DLL (`glew32.dll`) — on purpose.** projectM never calls `glewInit()` (only its SDL test app does); it
  relies on the host having initialised GLEW. With one shared `glew32.dll`, the renderer's `glewInit()` fills the
  function table projectM uses. Compile GLEW statically into each binary and projectM's copy is never initialised —
  its first GL call jumps through a null pointer. The only ways out are linking projectM statically into the exe too
  (blurs the LGPL "replaceable DLL" story) or patching projectM; neither is worth one 500 KB file.
- **Static MSVC runtime (`/MT`) everywhere**, so nothing needs the Visual C++ redistributable, which Playnite doesn't
  install. Verify: `dumpbin /dependents` on each file lists only Windows system DLLs.
- **`tar`:** `build_deps.cmd` calls `%SystemRoot%\System32\tar.exe` explicitly; Git's GNU tar, if first on PATH,
  reads `C:\...` as a remote host.
- **Version resource (`version.rc`)** on the renderer and the bridge, compiled once per binary (`/DVIS_BRIDGE` for the
  bridge). It has its own version (1.0.0.0), not UniPlaySong's, so a release does not change either binary. Without
  one, the bridge (almost all static runtime, unsigned) was flagged as a trojan by a couple of VirusTotal engines.
- **Reproducible:** `/Brepro` makes the same source give the same bytes, so a rebuild without code changes keeps
  every hash. When code changes, update
  [`VIRUSTOTAL-AUDIT-UniPlaySongVisualizer-Bridge-projectM-glew32.md`](../../../src/Audio/Native/Visualizer/VIRUSTOTAL-AUDIT-UniPlaySongVisualizer-Bridge-projectM-glew32.md)
  and [`SHIPPED_BINARIES.md`](../SHIPPED_BINARIES.md) (`DllDocumentationTests` fails until the audit matches).

## Bench mode — trying presets without Playnite

```
UniPlaySongVisualizer.exe "<preset.milk>" 1920 760 --bench 5
```

Renders unpaced for the given seconds, touches no named objects (so it can't disturb a running page), and prints
average render / readback / convert times and the p95 frame. `VIS_PCM=<file>` feeds raw 48 kHz stereo float (make one
with `ffmpeg -i song.mp3 -f f32le -ac 2 -ar 48000 test.pcm`), 800 frames per rendered frame; `VIS_DUMP=<file.bmp>`
saves the last frame composited over grey, as a theme would show it.

## Known limits and next steps

- **The frame copy costs more than the rendering:** GL readback + CPU keying + D3D11 upload ≈ 5.3 ms at 1080p.
  Next: render straight into the shared texture through `WGL_NV_DX_interop2` and do the luma key in a shader pass,
  keeping this path as the fallback.
- One element at a time (one bridge device).
- No device-loss handling yet beyond D3DImage's own front-buffer check.
- Spotify: visualized only when Spotify's audio already runs through UPS (Spotify Live Effects or the spectrum
  visualizer on Spotify); otherwise UPS has no Spotify audio to hand over.
