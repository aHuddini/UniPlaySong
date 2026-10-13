# Fountain bench tools

Offline checks used to tune the bench (`../fountain-bench.html`). Each script lifts its code straight out of the bench
page, so it always tests what the page runs. None of this ships.

Decode a song first (48 kHz mono float), into a `songs/` folder next to these scripts:

```
ffmpeg -i "TroyLawman.mp3" -ac 1 -ar 48000 -f f32le songs/troy.f32
```

| Script | What it measures |
|---|---|
| `sim.mjs` | Runs the bench's level engine at 60 fps over a decoded song, fed as the browser's analyser would be. Scores a preset: how far the line falls back between beats, when the throw peaks against the beat, saturation, whether big beats look bigger. Env `CJ2=1`: largest one-frame change of every colour input (the "colour never snaps" check). Env `COOL=1`: average cool tint by position along a column. |
| `exp.mjs <presets.json>` | Runs `sim.mjs` for each preset in a JSON file (`c4.json`, `c5.json`) on Troy and Doom. |
| `exp4.mjs`, `cool.mjs` | The same for the user's tune #5. |
| `motion.mjs <song.f32> <start s> <out.ppm>` | Space-time picture of the beat presets (across = the line, down = time) - see `../beat-presets-motion.png`. |
| `beats_check.mjs` | The bench's beat tracker against librosa's beats (`ref_librosa.py` writes them). |
| `tempo_change.mjs` | The beat tracker on synthetic tempo changes (a jump, a ramp, steady). |
| `drops_all.mjs` | Song-map drops on the decoded songs and the synthetic song. |
| `drums_harness.mjs` | The drum detectors against the known-hit drum test track. |
| `layers.py` | librosa reference for the layers (HPSS pads, chroma, chord changes) - draws `../layers-*.png`. Needs a venv with `librosa soundfile matplotlib`. |
| `compare.py` | The old band-rise drum detection against librosa onsets and beats. |
