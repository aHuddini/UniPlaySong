// Drums vs stems: scores the bench's drum detector (detectDrums, lifted from fountain-bench.html) on full mixes against
// an answer key - the same detector run on the song's isolated drums stem (C:\Projects\audiostemtool, Demucs), where no
// bass, synth or voice can pass for a hit. That measures what the rest of the mix costs: bass notes taken for kicks,
// guitars and voices for snares and hats. (The key shares the detector's own blind spots; it can't measure those.)
// On the stem every low hit counts as a kick (kickSustain 1): there is no bass to tell it from.
//   node drums_vs_stems.mjs <song out dir> ...           score the bench's current defaults
//   node drums_vs_stems.mjs --tune <song out dir> ...    and search each class's two settings for the best mean F1
// A song out dir holds mix.f32 and drums.f32 (mono 48 kHz), as audiostemtool's stems.py writes them.
import fs from "fs";
import path from "path";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const cut = (a, b) => { const i = html.indexOf(a), j = html.indexOf(b, i); if (i < 0 || j < 0) throw new Error(`no ${a}`); return html.slice(i, j); };
// detectDrums split in two: the spectra (slow, once per song) and the picking (fast, once per setting tried)
const det = cut("async function detectDrums(x, sr, o, progress) {", "// Hits found vs the known ones");
const split = det.indexOf("  const pick = ");
if (split < 0 || !det.trimEnd().endsWith("return out;\n}")) throw new Error("detectDrums changed shape - update this tool");
const head = det.slice(det.indexOf("{") + 1, split), tail = det.slice(split, det.trimEnd().lastIndexOf("}"));
const src = cut("function makeFFT", "async function mapBuffer") + cut("const DRUM_N", "async function detectDrums") + cut("function scoreHits", "// A 32 s drum test")
  + `async function prep(x, sr) { const progress = null; ${head} return { flux, E, frames, fr, hop, n, sr }; }
     function picks(st, o) { const { flux, E, frames, fr, hop, n, sr } = st; ${tail} }`;
const lib = new Function(`${src}; return { prep, picks, scoreHits };`)();
// the bench's current defaults, from its PARAMS
const def = k => { const m = new RegExp(`\\["${k}", "[^"]*", "[^"]*", ([0-9.]+)`).exec(html); if (!m) throw new Error("no default " + k); return +m[1]; };
const DEF = { kickDelta: def("DET_KICK"), kickSustain: def("DET_KICK_SUSTAIN"), snareDelta: def("DET_SNARE"), hatDelta: def("DET_HAT"), split: def("DET_SPLIT"), hatSplit: def("DET_HAT_SPLIT") };

const args = process.argv.slice(2), tune = args.includes("--tune"), dirs = args.filter(a => a !== "--tune");
const read = f => { const b = fs.readFileSync(f); return new Float32Array(b.buffer, b.byteOffset, b.length / 4); };
const songs = [];
for (const d of dirs) {
  const name = path.basename(d), mix = await lib.prep(read(path.join(d, "mix.f32")), 48000), drums = await lib.prep(read(path.join(d, "drums.f32")), 48000);
  songs.push({ name, mix, key: lib.picks(drums, { ...DEF, kickSustain: 1 }) });
}
const CLASSES = ["kick", "snare", "hat"];
const f1 = r => { const p = r.hit / Math.max(1, r.hit + r.extra), q = r.hit / Math.max(1, r.total); return p + q > 0 ? 2 * p * q / (p + q) : (r.total ? 0 : 1); };
const score = o => songs.map(s => { const found = lib.picks(s.mix, o); return Object.fromEntries(CLASSES.map(k => [k, lib.scoreHits(found[k], s.key[k])])); });
const report = (o, title) => {
  console.log(`\n${title}: ${JSON.stringify(o)}`);
  const all = score(o), mean = {};
  for (const k of CLASSES) mean[k] = all.reduce((a, r) => a + f1(r[k]), 0) / all.length;
  all.forEach((r, i) => console.log(`  ${songs[i].name.slice(0, 34).padEnd(34)} ` + CLASSES.map(k => `${k} ${String(r[k].hit).padStart(3)}/${String(r[k].total).padEnd(3)} +${String(r[k].extra).padEnd(4)} F1 ${f1(r[k]).toFixed(2)}`).join("   ")));
  console.log(`  mean F1: ` + CLASSES.map(k => `${k} ${mean[k].toFixed(3)}`).join("   "));
  return mean;
};
const base = report(DEF, "bench defaults");
if (tune) {
  // each class has its own two settings (kick: threshold, sustain; snare: threshold, split; hat: threshold, split)
  const grid = (lo, hi, step) => { const v = []; for (let x = lo; x <= hi + 1e-9; x += step) v.push(+x.toFixed(3)); return v; };
  const space = { kick: [["kickDelta", grid(0.04, 0.4, 0.02)], ["kickSustain", grid(0.4, 1, 0.05)]],
    snare: [["snareDelta", grid(0.06, 0.8, 0.04)], ["split", grid(0.1, 1.5, 0.1)]],
    hat: [["hatDelta", grid(0.06, 0.8, 0.04)], ["hatSplit", grid(0.05, 1.2, 0.05)]] };
  const best = { ...DEF };
  for (const k of CLASSES) {
    const [[pa, va], [pb, vb]] = space[k]; let top = -1;
    for (const a of va) for (const b of vb) {
      const o = { ...best, [pa]: a, [pb]: b }, m = score(o).reduce((s, r) => s + f1(r[k]), 0) / songs.length;
      if (m > top + 1e-9) { top = m; best[pa] = a; best[pb] = b; }
    }
  }
  const tuned = report(best, "tuned");
  console.log(`\n  change in mean F1: ` + CLASSES.map(k => `${k} ${(tuned[k] - base[k] >= 0 ? "+" : "")}${(tuned[k] - base[k]).toFixed(3)}`).join("   "));
}
