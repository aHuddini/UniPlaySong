// Hits vs stems: band "hit" envelopes (the Synesthesia/MilkDrop way - transient strength per band, no drum labels) from
// the full mix, against the same envelopes from the song's isolated drums stem (C:\Projects\audiostemtool, Demucs).
// An envelope is what a visual would show: the band's spectral flux (the bench detector's, lifted from
// fountain-bench.html) over its own running mean (~2 s), instant attack, 60 ms release. Scores per band:
//   corr  - how closely the mix's envelope follows the drums' (Pearson, 1 = the same shape)
//   false - of the mix's big punches (above half its 95th percentile), the share where the drums' envelope is low
//           (below a fifth of its 95th) - the hits a viewer would see with no drum behind them
// Variants for the low band: raw, and gated by the beat grid (tracked on the mix) - on beats, or on any eighth note.
//   node hits_vs_stems.mjs <song out dir> ...     (mix.f32 and drums.f32, mono 48 kHz, as stems.py writes them)
import fs from "fs";
import path from "path";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const cut = (a, b) => { const i = html.indexOf(a), j = html.indexOf(b, i); if (i < 0 || j < 0) throw new Error(`no ${a}`); return html.slice(i, j); };
const det = cut("async function detectDrums(x, sr, o, progress) {", "// Hits found vs the known ones");
const head = det.slice(det.indexOf("{") + 1, det.indexOf("  const pick = "))
  .replace("const B = { kick: band(40, 150),", "const B = { sub: band(30, 70), click: band(2500, 6000), kick: band(40, 150),");
if (!head.includes("sub: band(30, 70)")) throw new Error("detector bands changed - update this tool");
const lib = new Function(`let mapStatus = ""; ${cut("function makeFFT", "async function mapBuffer") + cut("const DRUM_N", "async function detectDrums")
  + cut("// ---- beat tracking", "// Each beat's strength") + cut("function beatStrengths", "// How long the spring")}
  async function prep(x, sr) { const progress = null; ${head} return { flux, fr }; }
  return { prep, trackBeats, beatStrengths };`)();
const read = f => { const b = fs.readFileSync(f); return new Float32Array(b.buffer, b.byteOffset, b.length / 4); };

// flux -> displayed envelope: over its running mean (2 s), then instant attack / 60 ms release
function envelope(fx, fr) {
  const w = Math.round(2 * fr), out = new Float32Array(fx.length), rel = Math.exp(-1 / (0.06 * fr));
  let sum = 0, e = 0;
  for (let t = 0; t < fx.length; t++) {
    sum += fx[t]; if (t >= w) sum -= fx[t - w];
    const v = fx[t] / (sum / Math.min(t + 1, w) + 1e-3);
    e = Math.max(v, e * rel); out[t] = e;
  }
  return out;
}
const pct = (a, p) => { const s = Float32Array.from(a).sort(); return s[Math.floor(p * (s.length - 1))]; };
function corr(a, b) {
  const n = Math.min(a.length, b.length); let ma = 0, mb = 0;
  for (let i = 0; i < n; i++) { ma += a[i]; mb += b[i]; } ma /= n; mb /= n;
  let s = 0, da = 0, db = 0; for (let i = 0; i < n; i++) { s += (a[i] - ma) * (b[i] - mb); da += (a[i] - ma) ** 2; db += (b[i] - mb) ** 2; }
  return s / Math.sqrt(da * db || 1);
}
function falsePunch(mix, drums) {
  const hm = 0.5 * pct(mix, 0.95), lo = 0.2 * pct(drums, 0.95); let big = 0, bad = 0;
  for (let i = 1; i < Math.min(mix.length, drums.length) - 1; i++)
    if (mix[i] > hm && mix[i] >= mix[i - 1] && mix[i] > mix[i + 1]) { big++; if (drums[i] < lo) bad++; }
  return bad / Math.max(1, big);
}
// a gate from the beat grid: 1 on a grid point (beats, or beats and the eighths between), falling to `floor` 40 ms off
function gate(beats, fr, n, eighths, floor = 0.3) {
  const g = new Float32Array(n).fill(floor), pts = [];
  for (let i = 0; i < beats.length; i++) { pts.push(beats[i]); if (eighths && i + 1 < beats.length) pts.push((beats[i] + beats[i + 1]) / 2); }
  for (const t of pts) { const c = Math.round(t * fr); for (let d = -8; d <= 8; d++) { const f = c + d; if (f >= 0 && f < n) g[f] = Math.max(g[f], floor + (1 - floor) * Math.exp(-0.5 * (d / fr / 0.02) ** 2)); } }
  return g;
}
const BANDS = [["low", "kick"], ["mid", "noise"], ["high", "hat"]];
const rows = [], tot = {};
for (const d of process.argv.slice(2)) {
  const mixX = read(path.join(d, "mix.f32")), drumX = read(path.join(d, "drums.f32"));
  const mix = await lib.prep(mixX, 48000), drums = await lib.prep(drumX, 48000), fr = mix.fr;
  const beats = lib.beatStrengths(await lib.trackBeats(mixX, 48000)).beats;
  const r = { name: path.basename(d) };
  for (const [b, k] of BANDS) {
    const em = envelope(mix.flux[k], fr), ed = envelope(drums.flux[k], fr);
    r[b] = { corr: corr(em, ed), fals: falsePunch(em, ed) };
    if (b === "low") {
      const es = envelope(mix.flux.sub, fr), ec = envelope(mix.flux.click, fr), ex = em.map((x, i) => Math.sqrt(x * ec[i]));
      r["sub"] = { corr: corr(es, ed), fals: falsePunch(es, ed) };
      r["low×click"] = { corr: corr(ex, ed), fals: falsePunch(ex, ed) };
    }
    if (b === "low") for (const [v, eighths] of [["low·beats", false], ["low·8ths", true]]) {
      const g = gate(beats, fr, em.length, eighths), eg = em.map((x, i) => x * g[i]);
      r[v] = { corr: corr(eg, ed), fals: falsePunch(eg, ed) };
    }
  }
  rows.push(r);
}
const cols = ["low", "low·beats", "low·8ths", "sub", "low×click", "mid", "high"];
console.log(`${"song".padEnd(34)} ` + cols.map(c => `${c.padEnd(9)} corr false`).join("   "));
for (const r of rows) console.log(`${r.name.slice(0, 34).padEnd(34)} ` + cols.map(c => `${"".padEnd(9)} ${r[c].corr.toFixed(2).padStart(4)} ${(100 * r[c].fals).toFixed(0).padStart(4)}%`).join("   "));
console.log(`${"mean".padEnd(34)} ` + cols.map(c => `${"".padEnd(9)} ${(rows.reduce((a, r) => a + r[c].corr, 0) / rows.length).toFixed(2).padStart(4)} ${(100 * rows.reduce((a, r) => a + r[c].fals, 0) / rows.length).toFixed(0).padStart(4)}%`).join("   "));
