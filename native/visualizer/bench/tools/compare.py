import json, numpy as np
ref = json.load(open("songs/ref.json")); mine = json.load(open("songs/mine.json"))
names = dict(l.strip().split(" ", 1) for l in open("songs/names.txt"))
tol = 0.035
def near(a, b):  # for each a: signed distance to nearest b
    b = np.asarray(b); return np.array([ (b - x)[np.argmin(abs(b - x))] * -1 if len(b) else 9 for x in a])
for s in sorted(ref):
    r = ref[s]; beats = np.array(r["beats"]); per = np.median(np.diff(beats)); grid = np.sort(np.concatenate([beats, beats[:-1] + np.diff(beats) / 2]))
    ons = np.array(r["onsets"]); allmine = np.sort(np.concatenate([mine[s][k] for k in ("kick", "snare", "hat")])) if any(mine[s][k] for k in ("kick","snare","hat")) else np.array([])
    print(f"\n{names[s]}  ({r['tempo']:.0f} BPM by librosa; on-grid by chance: {min(1, 2*tol*2/per):.0%})")
    for k in ("kick", "snare", "hat", "bass"):
        m = np.array(mine[s][k]); 
        if not len(m): print(f"  {k:6s} 0 found"); continue
        g = near(m, grid); o = near(m, ons)
        hit = abs(o) <= tol
        print(f"  {k:6s} {len(m)/90:5.2f}/s  on beat grid {np.mean(abs(g)<=tol):4.0%}  matches a librosa drum onset {np.mean(hit):4.0%}  offset vs it {1000*np.mean(o[hit]) if hit.any() else 0:+5.1f} ms")
    rec = np.mean(abs(near(ons, allmine)) <= tol) if len(allmine) else 0
    print(f"  librosa drum onsets caught by any of ours: {rec:.0%}   ({len(ons)/90:.1f}/s)")
