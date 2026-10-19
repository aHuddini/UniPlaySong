// Engine parity: runs the bench's own level engine (lifted from fountain-bench.html, as sim.mjs does) on a song with the
// shipped look's values (Your tune #6, standard-tune6.json) and no song analysis, one 1/60 s step per frame over the 1024 samples ending at frame k * 800
// (48 kHz) - exactly what the renderer's --bench mode feeds fountain.h - and compares the packed columns (colA, colB,
// colC per frame) with the renderer's VIS_ENGINE_DUMP.
//   node engine_parity.mjs <mono48k.f32> <renderer-dump.bin> [frames] [--analysis]
// --analysis: both run with the song's beats and map (the bench's own analysis here; the renderer's bench mode with
// VIS_ANALYZE=1), so the comparison covers the beat throws, sections, drops and the layers (melody, pads, harmony) too.
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const spec = JSON.parse(fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/standard-tune6.json", "utf8"));   // the shipped look, as the renderer's style file
const cut = (a, b) => html.slice(html.indexOf(a), html.indexOf(b));
const params = cut("const PARAMS = [", "const P = {};");
const fft = cut("function makeFFT", "const smooth =");
const peak = cut("const peakCache = new Map();", "// ---- end beat tracking");
const anaSrc = cut("function makeFFT", "async function mapBuffer") + cut("// ---- beat tracking", "// Each beat's strength") + cut("function beatStrengths", "// How long the spring")
  + cut("// ---- layers", "// ---- end layers");
const eng = cut("// ---- level engine", "// ---- PSP visualizers");
const factory = new Function("MOCK", `
  let { sourceSel, analyser, actx, buffer, playing, startAt, synthOn } = MOCK;
  let songMap = null, synthMapKey = "", beatInfo = null, layers = null, layersOf = null, analyserL = null, analyserR = null;
  const synth = () => {}, mapSynthSong = () => {}; const fixedDt = 0;
  const beatIndexAt = t => { if (!beatInfo) return -1; const bs = beatInfo.beats; let lo = 0, hi = bs.length; while (lo < hi) { const m = (lo + hi) >> 1; if (bs[m] <= t) lo = m + 1; else hi = m; } return lo - 1; };
  ${params}
  let P = {}; const Q = () => P;
  ${peak} ${eng}
  return { engine, setP: o => { P = {}; for (const p of PARAMS) P[p[0]] = p[3]; Object.assign(P, o); }, colA, colB, colC,
    setSong: (m, b, l) => { songMap = m; beatInfo = b; layers = l; layersOf = buffer; },
    scal: () => [padE, padG, harmE, modeE, awayE, chordHit, chordG, HIT_BANDS[0].hit, HIT_BANDS[1].hit] };`);
const args = process.argv.slice(2), withAnalysis = args.includes("--analysis");
const [file, dumpFile, nArg] = args.filter(a => a !== "--analysis");
const b = fs.readFileSync(file), x = new Float32Array(b.buffer, b.byteOffset, b.length / 4), sr = 48000, n = 1024;
const F = new Function(fft + "; return makeFFT;")()(n);
const win = new Float32Array(n).map((_, i) => 0.42 - 0.5 * Math.cos(2 * Math.PI * i / n) + 0.08 * Math.cos(4 * Math.PI * i / n));
const re = new Float32Array(n), im = new Float32Array(n);
let end = 0;
const mock = { sourceSel: { value: "file" }, actx: { currentTime: 0, sampleRate: sr }, buffer: { duration: x.length / sr, numberOfChannels: 1 }, playing: true, startAt: 0, synthOn: false,
  analyser: { getFloatFrequencyData(db) { for (let i = 0; i < n; i++) { const j = end - n + i; re[i] = (j >= 0 && j < x.length ? x[j] : 0) * win[i]; im[i] = 0; } F(re, im); for (let i = 0; i < n / 2; i++) db[i] = 20 * Math.log10(Math.hypot(re[i], im[i]) / n); } } };
const E = factory(mock);
const vals = {}; for (const k in spec.params) if (typeof spec.params[k] === "number") vals[k] = spec.params[k];
E.setP(vals);
if (withAnalysis) {
  const lib = new Function(`let mapStatus = ""; ${anaSrc}; return { makeFFT, buildMap, trackBeats, beatStrengths, analyseLayers };`)();
  const mn = 1024, mh = 1024, mfft = lib.makeFFT(mn), mre = new Float32Array(mn), mim = new Float32Array(mn), mmag = new Float32Array(mn / 2);
  const mwin = new Float32Array(mn).map((_, i) => 0.5 - 0.5 * Math.cos(2 * Math.PI * i / (mn - 1)));
  const map = await lib.buildMap(Math.floor((x.length - mn) / mh), sr / mh, sr / mn, f => { const o = f * mh; for (let i = 0; i < mn; i++) { mre[i] = (x[o + i] + x[o + i]) * 0.5 * mwin[i]; mim[i] = 0; } mfft(mre, mim); for (let i = 0; i < mn / 2; i++) mmag[i] = Math.hypot(mre[i], mim[i]); return mmag; }, "song");
  const beats = lib.beatStrengths(await lib.trackBeats(x, sr));
  const layers = await lib.analyseLayers(x, sr, beats.beats, null, beats.barPhase);
  E.setSong(map, beats, layers);
  console.log(`with the song's analysis: ${beats.beats.length} beats, ${map.drops.length} drops, ${layers.sections.length} sections`);
}
const SCAL = ["padE", "padG", "harmE", "modeE", "awayE", "chordHit", "chordG", "hitMid", "hitHigh"];
const dump = fs.readFileSync(dumpFile), d = new Float32Array(dump.buffer, dump.byteOffset, dump.length / 4), per = 96 * 12 + SCAL.length;
const frames = Math.min(+(nArg || 600), d.length / per);
const names = ["ez", "kc", "slow", "rate", "fl", "flv", "heatC", "settleC", "melC", "bandPos", "reach"];
const worst = new Float64Array(11), at = new Array(11), sw = new Float64Array(SCAL.length), sat = new Array(SCAL.length);
for (let k = 0; k < frames; k++) {
  end = (k + 1) * 800; mock.actx.currentTime = end / sr;
  E.engine(1 / 60, (k + 1) / 60);
  const js = [E.colA, E.colB, E.colC];
  for (let q = 0; q < 11; q++) {
    const tex = js[q >> 2], ch = q & 3;
    for (let c = 0; c < 96; c++) {
      const a = tex[c * 4 + ch], r = d[k * per + (q >> 2) * 384 + c * 4 + ch], e = Math.abs(a - r);
      if (e > worst[q]) { worst[q] = e; at[q] = `frame ${k} col ${c}: bench ${a.toFixed(5)} renderer ${r.toFixed(5)}`; }
    }
  }
  const sv = E.scal();
  for (let q = 0; q < SCAL.length; q++) { const r = d[k * per + 96 * 12 + q], e = Math.abs(sv[q] - r); if (e > sw[q]) { sw[q] = e; sat[q] = `frame ${k}: bench ${sv[q].toFixed(5)} renderer ${r.toFixed(5)}`; } }
}
console.log(`${frames} frames compared (${(frames / 60).toFixed(1)} s of the song), largest difference per value:`);
for (let q = 0; q < 11; q++) console.log(`  ${names[q].padEnd(8)} ${worst[q].toExponential(2)}  ${at[q] || ""}`);
console.log("song uniforms (scalars):");
for (let q = 0; q < SCAL.length; q++) console.log(`  ${SCAL[q].padEnd(8)} ${sw[q].toExponential(2)}  ${sat[q] || ""}`);
