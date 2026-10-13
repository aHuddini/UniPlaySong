import json, sys, numpy as np, librosa
out = {}
for i in range(1, 7):
    y, sr = librosa.load(f"songs/s{i}.wav", sr=44100, mono=True)
    tempo, beats = librosa.beat.beat_track(y=y, sr=sr, units="time")
    yp = librosa.effects.percussive(y)
    on = librosa.onset.onset_detect(y=yp, sr=sr, units="time", backtrack=False)
    out[f"s{i}"] = {"tempo": float(np.atleast_1d(tempo)[0]), "beats": beats.tolist(), "onsets": on.tolist()}
    print(i, round(float(np.atleast_1d(tempo)[0]), 1), len(beats), "beats", len(on), "perc onsets")
json.dump(out, open("songs/ref.json", "w"))
