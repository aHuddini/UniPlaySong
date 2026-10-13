# What layers can be pulled out of a song cheaply: harmonic/percussive separation (median filtering), then bands and
# behaviour within the harmonic part, plus harmony changes from chroma. Plotted as lanes over time with the beat grid.
import sys, numpy as np, librosa, matplotlib
matplotlib.use("Agg"); import matplotlib.pyplot as plt
path, title, t0, t1, out = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
y, sr = librosa.load(path, sr=22050, mono=True, offset=t0, duration=t1 - t0)
hop = 512; S = np.abs(librosa.stft(y, n_fft=2048, hop_length=hop)) ** 2
H, P = librosa.decompose.hpss(S, margin=2.0)            # harmonic = held across time, percussive = spread across frequency
f = librosa.fft_frequencies(sr=sr, n_fft=2048); t = librosa.frames_to_time(np.arange(S.shape[1]), sr=sr, hop_length=hop) + t0
band = lambda X, lo, hi: X[(f >= lo) & (f < hi)].sum(0)
norm = lambda v: v / (np.percentile(v, 98) + 1e-12)
drums = norm(P.sum(0))
bass = norm(band(H, 30, 250))
mid = band(H, 250, 5000)
pads = norm(librosa.util.normalize(np.convolve(mid, np.ones(43) / 43, "same"), norm=np.inf) * mid.max())   # ~1 s average: what's held
# the lead: harmonic mid energy concentrated in a few strong peaks (a melody line), not spread like a chord
Hm = H[(f >= 300) & (f < 4000)]
peaky = Hm.max(0) / (Hm.mean(0) + 1e-12)
lead = norm(np.clip(peaky / np.percentile(peaky, 50) - 1, 0, None) * np.sqrt(Hm.sum(0)))
# harmony changes: beat-synced chroma of the harmonic part, distance between consecutive beats
yh = librosa.istft(np.sqrt(H) * np.exp(1j * np.angle(librosa.stft(y, n_fft=2048, hop_length=hop))), hop_length=hop)
chroma = librosa.feature.chroma_cqt(y=yh, sr=sr, hop_length=hop)
tempo, beats = librosa.beat.beat_track(y=y, sr=sr, hop_length=hop)
cs = librosa.util.sync(chroma, beats, aggregate=np.median)
cs = cs / (np.linalg.norm(cs, axis=0, keepdims=True) + 1e-9)
chg = np.r_[0, 1 - (cs[:, 1:] * cs[:, :-1]).sum(0)]
bt = librosa.frames_to_time(np.r_[0, beats], sr=sr, hop_length=hop)[: len(chg)] + t0
names = ["Drums (percussive)", "Bass (harmonic < 250 Hz)", "Pads / held chords (harmonic, 1 s average)", "Lead / melody (strong single pitches)"]
cols = ["#ffcc66", "#b9a4ff", "#7fd1ff", "#ff7ad9"]
fig, ax = plt.subplots(6, 1, figsize=(15, 9.5), sharex=True, gridspec_kw={"height_ratios": [1, 1, 1, 1, 1, 1.4]}, facecolor="#101014")
for a in ax: a.set_facecolor("#101014"); a.tick_params(colors="#9a98a4"); [s.set_color("#2a2a31") for s in a.spines.values()]
for i, (v, n, c) in enumerate(zip([drums, bass, pads, lead], names, cols)):
    ax[i].fill_between(t, 0, np.clip(v, 0, 1.2), color=c, alpha=0.85, lw=0); ax[i].set_ylim(0, 1.2); ax[i].set_yticks([])
    ax[i].text(0.005, 0.8, n, transform=ax[i].transAxes, color="#e8e6ee", fontsize=10)
ax[4].vlines(bt, 0, chg, color="#c8ff7a", lw=2); ax[4].set_ylim(0, max(0.05, chg.max() * 1.1)); ax[4].set_yticks([])
ax[4].text(0.005, 0.8, "Harmony change at each beat (chord changed = tall)", transform=ax[4].transAxes, color="#e8e6ee", fontsize=10)
ax[5].imshow(chroma, aspect="auto", origin="lower", extent=[t[0], t[-1], 0, 12], cmap="magma"); ax[5].set_yticks(np.arange(12) + 0.5); ax[5].set_yticklabels(["C","C#","D","D#","E","F","F#","G","G#","A","A#","B"], fontsize=7)
ax[5].text(0.005, 0.85, "Which notes are sounding (chroma)", transform=ax[5].transAxes, color="#ffffff", fontsize=10)
for a in ax[:5]:
    for b in librosa.frames_to_time(beats, sr=sr, hop_length=hop)[::4] + t0: a.axvline(b, color="#ffffff", alpha=0.12, lw=0.8)
ax[5].set_xlabel("seconds", color="#9a98a4")
fig.suptitle(f"{title}: layers found without naming instruments ({float(np.atleast_1d(tempo)[0]):.0f} BPM; faint lines = bars)", color="#e8e6ee", fontsize=13)
plt.tight_layout(); plt.savefig(out, dpi=90, facecolor=fig.get_facecolor())
import json
json.dump({"cs": cs.T.tolist(), "t": t.tolist(), "pads": pads.tolist(), "beats": (librosa.frames_to_time(beats, sr=sr, hop_length=hop) + t0).tolist(), "chg": chg.tolist()}, open(out + ".json", "w"))
