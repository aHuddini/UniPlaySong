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
  publish "frame being heard" ──────────────────────── <channel>_Audio.played           │ key pass (GPU) + readback
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
  draws one frame per signal and **nothing without one**. Free-running both sides at 60 Hz aliased badly (25–117
  skipped frames in 8 s; on demand: 1). Drawing anyway on a missed signal cost a third of a core while Playnite was
  minimized.
- **Idles when there is nothing to show.** The element stops signalling after 1.5 s of silence (paused, stopped,
  between songs) and while Playnite isn't the active window (a game, another app); the last frame stays up. Silence
  comes from the audio itself (`VisualizerAudioFeed.LastSoundTick`, anything above −80 dBFS): the player's clock keeps
  running through a pause, because the always-on mixer plays silence.
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

A key pass on the GPU (`KeyPass` in `renderer.cpp`) flips the frame and writes premultiplied BGRA with coverage = the
brightest channel (luma key), then it is read back and uploaded as is: black is see-through, bright is solid. The same
per-pixel loop on the CPU cost ~1.5 ms a frame at 1728×712 and ~3 ms at 2560×1054; the pass puts back the GL state
projectM keeps across frames. The presets are written for it — every style fades out before the frame edge and crushes faint haze
to black, because any non-black pixel is partly visible and a faint tint over the whole frame shows the rectangle.

## Presets — [`native/visualizer/presets/make_presets.py`](../../../native/visualizer/presets/make_presets.py)

UniPlaySong's own presets (MIT); community packs carry no licence and are never bundled. One generator, six styles
(LED Bars, Glow Bars, Spark Bars, Aurora, Halo Ring, Fountain); **Glow Bars and Fountain ship** — `build.cmd` copies
them, and the **Style** setting under Music Visualizer for Themes picks one.

Each preset: per-frame code turns last frame's spectrum into log-spaced bars (auto-gain, 30 dB window, time-based
rise/fall, peak hold) and packs them into `q1..q32`; the warp shader draws the bars per pixel with trails; the comp
shader adds bloom from projectM's blur textures, a bass-driven backlight and the edge fade. A hidden spectrum wave
copies the FFT bins into `gmem` for the next frame. Every style frames a centre **art slot** (0.30 of the height,
wings start 0.19 out) so a page can put its cover art in the middle.

**Fountain** is inspired by Tekken 8's music jukebox visualizer (particle model per Lucid's
`docs/tekken8-visualizer-spec.md`).

**Layout.** One line of 96 columns, low to high across the frame: lows at the far left, highs at the far right, split
around the art slot. Unlike the bar styles, its wings start under the slot's rim (0.11 of the height out, against the
slot's 0.15) and run to 0.46 of the width: bigger columns, and the line passes behind the cover. The levels are packed
three to a `q` at 8 bits each.

**Particles** (30 per column):
- **No flight.** Each dot sits at a fixed random fraction `k = u·p` of its column's level: two signed uniforms, so
  |k| ≤ 0.5. The product peaks at 0, so a column is dense on the line and thins to its tips, mirrored above and below.
  A full level at |k| = 0.5 reaches 18 column widths.
- **Birth level → live level.** A dot's level eases from the level it was born with to the live one along the game's
  curve B, so older dots follow a hit at once and young ones lag. That is the game's "lava-lamp pulse".
- **Lifetimes** are 1–5 s, drawn as `L = sqrt(1 + 24h)` to match the game's live population. Opacity follows curve A,
  which peaks at 0.6 at mid-life.
- **Colour** is a random blend per dot between magenta (6, 1, 2) and violet (6.5, 4, 10), not set by loudness.
- **Streaks.** A dot is stretched vertically by how fast its level is changing, up to 5×: hits flash as streaks,
  steady tones stay round.
- **On the axis.** There is no horizontal jitter. Dots are 0.5–0.8 of the column spacing across, so a pixel only ever
  meets its own column's dots.

**History corner.** MilkDrop has no data textures, so the birth levels live in a hidden 96×130-pixel corner of the main
texture. The warp shader's output is the next frame's `sampler_main`, so those pixels persist: 128 rows of per-column
levels shifted every 1/60 s, plus a mean-level row and a per-column-peak row (used to cull pixels no dot can reach).
The comp shader never shows the corner and keeps it out of the bloom.

**The look is the game's lens.** In the game the line sits far behind the focus, so every dot is a large bokeh disc
under 10× bloom.
- Each dot here is carried mostly by projectM's blur, with a little of the sharp dot left in its core
  (`FOUNTAIN_DEFOCUS`; 0 = sharp dots).
- Bloom is wide and tinted violet (0.80, 0.27, 0.96), with the red/blue fringe.
- The magenta-red level light follows the mean of all columns.
- ACES tone mapping at exposure 0.6.
- The line's colour comes on over half a second.

**Levels.** Linear magnitude, as the game's, which is what makes it dynamic: a hit towers over what's around it.
- Point-sampled, 50 Hz to 16 kHz on a pow 1.5 curve, tilted up 4× toward the top.
- Each column scaled to its own recent peak (half-life 3 s), never below 0.03 of the loudest column, on a 0.7 curve.
- Then the game's per-tick smoothing (rise 80%, fall 50%, a two-tick peak), then an ease of 0.10 s up / 0.30 s down.

Why not Lucid's decibels-from-200 Hz against a per-column average: on identical kicks over changing music, the kick's
body sat below 200 Hz (kick columns rose 3%), and the average, itself raised by the kicks, made the same kick land
anywhere from small to full (±47%). Peak-scaled linear: kicks rise 3× higher, within ±23%, and none are missed.

**Drawing.** Dots are stateless (the clock, a per-dot hash and the history), so the warp shader draws them per pixel.
Full size, 2560×1188: ~7 / ~12 ms render / p95 frame with Playnite drawing too.

**Experiments (generated, not shipped):** `Fountain Lessons` adds exact history steps (the history moves by however
many 1/60 s steps really passed, from a step index kept in the corner), streaks from a per-column change envelope
that relaxes over ~0.12 s (the per-step version flickered between frames on a kick), and a faster 0.035 s ease in so a
kick lands within 3–5 frames. `Fountain Lessons (default ease)` is the same without the faster ease.

Shader gotcha: MilkDrop shaders already define `rad` and `ang` (the pixel's polar coordinates). Declaring a local
`rad` fails to compile ("expected identifier near '('"), and projectM then draws nothing.

## Frame size: 1:1, never stretched

The frame is the element's size in device pixels, at most 2560×1440 (each side capped on its own), shown **pixel for
pixel** and centred. Past the cap the rest of the element stays empty. The image is sized in code to the frame in
device pixels, so at 200% display scaling a 2560-px frame isn't shown at twice its size.

This is deliberate. A round of rendering Fountain smaller and stretching it to fill the element (0.6 scale, 1728 px
over 2880) looked blurry all over. Filling the width also made every column wider than the page was laid out for.

**Device pixels are measured through every transform, not DPI alone.** Playnite Fullscreen shows its whole view through
a `Viewbox` (laid out 1080 units tall, stretched to the window), which `CompositionTarget.TransformToDevice` doesn't
include: sizing by DPI drew a 2560 px frame into 2133 px on a 2880×1800 screen at 200%. `MusicVisualControl.ToDevice`
walks `TransformToAncestor` to the root instead. Layout rounding is **off** on the image (it rounds to the DPI grid, not
the pixel grid: a 988 px frame came out 988.33) and a `TranslateTransform` puts its corner on a whole pixel each frame.
The debug log records where it lands (`[Visualizer] Shown at x,y, WxH device px for a WxH frame`) - equal sizes and
whole-pixel corners mean 1:1. Checked with a 1 px test pattern against a screen capture: 0 pixels off.

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
                                    and the shipped presets
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
average render / key+readback / upload times and the p95 frame. `VIS_PCM=<file>` feeds raw 48 kHz stereo float (make one
with `ffmpeg -i song.mp3 -f f32le -ac 2 -ar 48000 test.pcm`), 800 frames per rendered frame; `VIS_DUMP=<file.bmp>`
saves the last frame composited over grey, as a theme would show it.

## Jukebox Fountain — the native style

The default style is drawn natively, not by projectM: `presets/UPS - Jukebox Fountain.vis` (a `.vis` preset switches
the renderer to native mode) runs [`native/visualizer/fountain.h`](../../../native/visualizer/fountain.h). It is a port
of the WebGL bench [`native/visualizer/bench/fountain-bench.html`](../../../native/visualizer/bench/fountain-bench.html):
the level engine line for line, and the bench's own shaders lifted as they are. **Parity with the bench is the rule** -
change the bench, regenerate, port engine changes to `fountain.h`.

The droplet style is inspired by Tekken 8's music jukebox visualizer - "T8" in the bench and its tools.

- **The look:** the bench's **Your tune #8**, frozen in `bench/standard-tune8.json`. `node bench/tools/export_t8.mjs`
  writes the `.vis` from it (parameters, palette uniforms, the three shaders); copy it to `src/Audio/Native/Visualizer/presets/`.
  The bench's other looks (Your tune #4-#7, the T8 standard - `standard-t8.json`, `standard-tune6.json`, `standard-tune7.json` - Showcase,
  Spectrum) are kept as reference; so are the MilkDrop test variants in `native/visualizer/presets/reference/`.
- **The palette comes from a picture:** the game's background by default; the theme's element `Tag` can pick `Cover`
  or `Disc` (the now-playing art, `NowPlayingAlbumArtPath`), each falling back to the others. UniPlaySong takes three colours from it the bench's way (`VisualizerCoverPalette`: k-means into five colours at 64x64,
  weighted to saturated bright pixels; the three covering the most pixels with colour in them, greys only to fill in) and sends them
  through `VisSong.cover*`. The renderer builds the "Cover art" scheme from them (`fountain::Colours::FromCover`:
  darkest = cool end, lightest = hot end, the third the middle stop, most vivid = kick, nearest blue = cool shade; calm,
  glint and bloom mixed from those) and eases to it over ~2 s (0.8 s time constant). A one-colour cover gives shades of
  that colour. Each picture is taken apart once and kept in `VisualAnalysis\palettes.json` (beside Games); each session
  checks a picture's size and time once and takes it again only if it changed (`StoreVersion` retakes them all). No art,
  or an image that won't decode: the style's own palette (White to magenta). The bench's "Cover
  art" picks and builds the same way, so it previews what Playnite shows.
- **The music player's spectrum strip** (`UPS_MusicPlayerControlVisual`): `presets/UPS - Seek Spectrum.vis`, written by
  hand - the Jukebox Fountain's `P`/`U` lines (the engine reads its parameters by name) with a neutral grey-white
  palette for no art, and its own three shaders: 32 bars rising from the strip's bottom edge, each the live level of one
  right-wing column (48-95, evenly low to high), blending over its own height from a bottom colour to a top colour; no
  bloom. The two colours are what the eye reads in the cover, nothing boosted (`VisualizerStripColors`): a hue histogram
  weighted saturation x brightness^2; the strongest family and the strongest at least 60 degrees from it (none =
  one-hue, darker-to-brighter shades); each family's colour from its brightest third, at the cover's own saturation,
  only brightness lifted; a cover with little vivid colour toned toward neutral (A Plague Tale near-white, Quake III
  keeps its red). The darker colour at the foot. Boosting saturation invented colours and made pairs harsh; the desktop
  extractor's Vivid pick rotated hues. Sent as the cover's first two colours, read as sent (`uCoverA`, `uCoverB`, eased).
  Also shown alone: `UPS_MusicVisualStrip`. It needs nothing new from the renderer.
  Bench check: `VIS_COVER` with `--bench` and `VIS_DUMP`.
- **Whole-song analysis** ([`analysis.h`](../../../native/visualizer/analysis.h)), in the 64-bit renderer: the song map
  (sections, build-up tension, drops), a librosa-style beat tracker (tempo changes, beat strengths, the bar's first
  beat), and the layers - held pads, melody, harmony, lettered sections, and per beat the chord (24 triads with
  overtone templates, Viterbi-smoothed, "none" on drum-only beats), how major or minor it sounds, the key, and the
  distance from home on the circle of fifths; and the bass line - per beat the note E1-G3 scored by its harmonics in
  the held part, gated off where the 58-250 Hz share of the held sound shows no bass (a lounge piano: 5%; songs with a
  bass line 34-83%). Against pYIN on Demucs bass stems the note's name is right 74-90% of beats; octaves slip (the
  lowest fundamentals sit below the first bins), so the rise folds notes into the octave round the song's usual bass
  note: the fountain's body lifts as the bass climbs and settles as it falls (eased 0.4 s; #7's walking light along the
  wing, sideways and quick, read as apart from the fountain). UniPlaySong decodes a new song to mono 48 kHz and hands it over
  ([`VisualizerSongAnalysis.cs`](../../../src/Audio/VisualizerSongAnalysis.cs)); the result is cached as
  `<game music folder>\VisualAnalysis\<song>.vab` (header + `MAP `/`BEAT`/`LAYR`/`CHRD`/`BASS` chunks). `VIS_ANALYSIS_VERSION`
  (with `AnalysisVersion` in C#) re-analyses every song once when the format or the results change. `VisFrames.songState`
  tells a theme whether it's live, analysing or following the song.
- **Live, no analysis needed:** the spectrum-driven engine, and the hi-hat and snare reactions - the high (6-16 kHz)
  and mid (1.5-5 kHz) bands' transient strength over its own running mean, eased so colour never snaps. Against Demucs
  drum stems these follow the drums at 0.82 (high) and 0.57 (mid) correlation; the low band only manages 0.29 under a
  bass line, so kicks come from the beat grid, not detection.
- **Dots keep the bench's pixel size** past 2050 px wide (`min(1, 2050 / width)` on the dot and colour fringe): they
  scaled with the frame, so Playnite's 2560 px frame read 25% softer than the bench.

**Parity tools** (`bench/tools/`, run with Node against a decoded song):

| Tool | Checks |
|---|---|
| `analysis_parity.mjs <mono.f32> <json>` | `UniPlaySongVisualizer.exe --analyze` against the bench's analysis: map, beats, layers, chords, key (0 difference on Troy, Doom, Stalker) |
| `engine_parity.mjs <mono.f32> <dump> [frames] --analysis` | the renderer's `VIS_ENGINE_DUMP` (per-column state + the song uniforms' scalars) against the bench engine, through a real `.vab` (~1e-7) |
| `chords_check.mjs` | chords and key on synthesised progressions with known answers; drums-only gives none |
| `drums_vs_stems.mjs`, `chords_vs_stems.mjs`, `hits_vs_stems.mjs`, `bass_vs_stems.mjs` | in-mix detection against Demucs stems from `C:\Projects\audiostemtool` (dev-time only, nothing ships) |

Renderer env for the bench mode: `VIS_ANALYZE=1` (analyse the PCM, through a `.vab`), `VIS_FRAMES=n`,
`VIS_ENGINE_DUMP=<file>`, `VIS_PROFILE=1`, `VIS_DEBUG=1`, `VIS_INTEROP=0` (force the copy path, to compare),
`VIS_DUMP_D3D=1` (read the dumped frame through Direct3D, as the bridge does), `VIS_COVER=r,g,b;r,g,b` (0-1: a cover's
two colours, to see its palette).

## Known limits and next steps

- **Zero-copy hand-off** (done): the key pass draws straight into the shared D3D11 textures through
  `WGL_NV_DX_interop2` - locked for GL while it draws, finished before it's unlocked and announced. At 2560×988 on an
  Arc 140V the hand-off went from 4.52 ms (GL readback 3.72 + D3D11 upload 0.80) to 1.38 ms, the p95 frame from 17.4 to
  12.5 ms. Frames are pixel-identical to the copy path, read through Direct3D as the bridge sees them. The copy path
  stays as the fallback (extension missing, a texture that won't register, a failed lock); `VisFrames.transport` says
  which ran, and the debug log records it (`[Visualizer] Frames by zero-copy (GPU only)` / `copy (through the CPU)`).
- One element at a time (one bridge device).
- No device-loss handling yet beyond D3DImage's own front-buffer check.
- Spotify: visualized only when Spotify's audio already runs through UPS (Spotify Live Effects or the spectrum
  visualizer on Spotify); otherwise UPS has no Spotify audio to hand over.
