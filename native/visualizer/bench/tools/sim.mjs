// Runs the bench's level engine (lifted from fountain-bench.html) over a real song at 60 fps, feeding it what the
// browser's AnalyserNode would, and measures how the fling moves against the tracked beats.
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const cut = (a, b) => html.slice(html.indexOf(a), html.indexOf(b));
const params = cut("const PARAMS = [", "const P = {};") + cut("const YOUR_TUNE_3 =", "document.getElementById(\"presetYours3\")");
const fft = cut("function makeFFT", "const smooth =");
const beats = cut("// ---- beat tracking", "// ---- end beat tracking");
const eng = cut("// ---- level engine", "// ---- WebGL");
const mapsrc = cut("const smooth =", "async function mapBuffer");
const layersrc = cut("// ---- layers", "// ---- end layers");
const factory = new Function("MOCK", `
  let { sourceSel, analyser, actx, buffer, playing, startAt, synthOn, songMap, synthMapKey, beatInfo } = MOCK;
  const synth = () => {}, mapSynthSong = () => {};
  let layers = null, layersOf = null, analyserL = null, analyserR = null;
  const beatIndexAt = t => { if (!beatInfo) return -1; const bs = beatInfo.beats; let lo = 0, hi = bs.length; while (lo < hi) { const m = (lo + hi) >> 1; if (bs[m] <= t) lo = m + 1; else hi = m; } return lo - 1; };
  ${params}
  let P = {}; const Q = () => P;
  let mapStatus = "";
  ${fft} ${mapsrc} ${beats} ${layersrc} ${eng}
  return { engine, trackBeats, buildMap, beatStrengths, analyseLayers, setLayers: (l, b) => { layers = l; layersOf = b; }, setMap: m => { songMap = m; }, setP: o => { P = {}; for (const p of PARAMS) P[p[0]] = p[3]; Object.assign(P, o); }, presets: { YOUR_TUNE_2, YOUR_TUNE_3 },
    setBeat: b => { beatInfo = b; }, state: () => ({ fl, flv, kv, kc, ez, beatEnv, dropPulse, dropC, heatC, tensionC, phraseC, sectionC, actC, harmE, padE, settleC }) };`);
export async function run(file, preset, from = 30, len = 40, label = "", record = false) {
  const b = fs.readFileSync(file), x = new Float32Array(b.buffer, b.byteOffset, b.length / 4), sr = 48000;
  const n = 1024, F = (() => { const m = new Function(fft + "; return makeFFT;")(); return m(n); })();
  const win = new Float32Array(n).map((_, i) => 0.42 - 0.5 * Math.cos(2 * Math.PI * i / n) + 0.08 * Math.cos(4 * Math.PI * i / n));
  const re = new Float32Array(n), im = new Float32Array(n);
  const mock = { sourceSel: { value: "file" }, actx: { currentTime: 0, sampleRate: sr }, buffer: { duration: x.length / sr }, playing: true, startAt: 0, synthOn: false, songMap: null, synthMapKey: "", beatInfo: null,
    analyser: { getFloatFrequencyData(db) { const end = Math.floor(mock.actx.currentTime * sr); for (let i = 0; i < n; i++) { const j = end - n + i; re[i] = (j >= 0 && j < x.length ? x[j] : 0) * win[i]; im[i] = 0; } F(re, im); for (let i = 0; i < n / 2; i++) db[i] = 20 * Math.log10(Math.hypot(re[i], im[i]) / n + 1e-12); } } };
  const E = factory(mock);
  E.setP(preset);
  const bi = await E.trackBeats(x, sr);
  E.beatStrengths(bi);
  E.setBeat(bi);
  E.setLayers(await E.analyseLayers(x, sr, bi.beats), mock.buffer);
  if (preset.ANALYSIS >= 0.5) {   // the song map, as mapBuffer builds it
    const mh = 1024, mre = new Float32Array(mh), mim = new Float32Array(mh), mag = new Float32Array(mh / 2), mF = new Function(fft + "; return makeFFT;")()(mh);
    const hw = new Float32Array(mh).map((_, i) => 0.5 - 0.5 * Math.cos(2 * Math.PI * i / (mh - 1)));
    const m = await E.buildMap(Math.floor((x.length - mh) / mh), sr / mh, sr / mh, f => { const o = f * mh; for (let i = 0; i < mh; i++) { mre[i] = x[o + i] * hw[i]; mim[i] = 0; } mF(mre, mim); for (let i = 0; i < mh / 2; i++) mag[i] = Math.hypot(mre[i], mim[i]); return mag; }, "song");
    E.setMap(m); globalThis.__drops = m.drops;
  }
  let maxFl = 0; const dt = 1 / 60, rows = [], frames = [], cj = { a: 0, b: 0 }, cj2 = {}, cool = {}; globalThis.__pv = null; globalThis.__pa = null; globalThis.__pb = null;
  for (let t = from - 5; t < from + len; t += dt) {
    mock.actx.currentTime = t; E.engine(dt, t);
    if (t >= from) { const s = E.state(); if (record) frames.push(Float32Array.from(s.fl));
      for (let c = 0; c < 96; c++) maxFl = Math.max(maxFl, Math.abs(s.fl[c]));
      if (process.env.COOL) { const Pp = preset, ss = (a, b, x) => { const u = Math.min(1, Math.max(0, (x - a) / (b - a))); return u * u * (3 - 2 * u); };
        for (const [ti, tip] of [0.2, 0.5, 0.8, 1.0].entries()) { let cw = 0, mv = 0;
          for (let c = 0; c < 96; c++) { const T = Math.pow(Math.min(1, s.heatC[c] * 1.5), Pp.COLOR_CURVE) * (1 + (ss(0.1, 0.9, tip) - 1) * Pp.COLOR_TIPS);
            const moving = s.settleC[c] * ss(0.2, 0.8, tip);
            { const cT = 1 - ss(0, 0.45, T); cw += Math.min(1, 0.85 * Pp.COOL_AIR * (cT + (1 - cT) * (Pp.MOVE_COOL || 0) * moving)); } mv += moving; }
          (cool[ti] ??= []).push([cw / 96, mv / 96]); }
        (cool.calm ??= []).push(Pp.CALM_COLOR * Pp.DYNAMICS * (1 - Math.max(s.sectionC, s.actC))); }
      if (process.env.CALM && Math.abs(t * 2 - Math.round(t * 2)) < 0.009 && Math.round(t * 2) % 6 === 0) console.log(`  ${t.toFixed(0).padStart(3)} s  breakdown colour before ${(1 - s.sectionC).toFixed(2)}  now ${(1 - Math.max(s.sectionC, s.actC)).toFixed(2)}`);
      if (process.env.CJ2) { const v = { heat: Math.max(...s.heatC), heatMin: Math.min(...s.heatC), kick: Math.max(...s.kc), drop: s.dropC, buildup: s.tensionC, phrase: Math.sin(s.phraseC * 2.39996), breakdown: s.sectionC, harmony: s.harmE, pads: s.padE };
        const cols = Array.from(s.heatC).concat(Array.from(s.kc), Array.from(s.settleC));
        if (globalThis.__pv) { for (const k in v) cj2[k] = Math.max(cj2[k] || 0, Math.abs(v[k] - globalThis.__pv[k])); let m = 0; for (let i = 0; i < cols.length; i++) m = Math.max(m, Math.abs(cols[i] - globalThis.__pc[i])); cj2.anyColumn = Math.max(cj2.anyColumn || 0, m); }
        globalThis.__pv = v; globalThis.__pc = cols; }
      if (process.env.CJ) { const K = preset.KICK_COLOR ?? 0.8, D = preset.DYNAMICS ?? 0;
        for (let c = 0; c < 96; c++) { const wa = Math.min(1, K * s.kv[c] + D * 0.6 * s.dropPulse), wb = Math.min(1, K * s.kc[c] + D * 0.6 * s.dropC);
          if (globalThis.__pa && cj.ready) { cj.a = Math.max(cj.a, Math.abs(wa - globalThis.__pa[c])); if (Math.abs(wb - globalThis.__pb[c]) > cj.b) { cj.b = Math.abs(wb - globalThis.__pb[c]); cj.at = [t.toFixed(2), c, globalThis.__pb[c].toFixed(2), wb.toFixed(2), s.kc[c].toFixed(2), s.dropC.toFixed(2)]; } }
          (globalThis.__pa ??= new Float32Array(96))[c] = wa; (globalThis.__pb ??= new Float32Array(96))[c] = wb; } cj.ready = true; } let m = 0, sat = 0; for (let c = 0; c < 96; c++) { m += Math.max(0, s.fl[c]); if (s.fl[c] > 1) sat++; } rows.push([t, m / 96, sat / 96]); }
  }
  const bs = bi.beats.filter(t => t >= from && t < from + len - 1), depth = [], lag = [], peaks = [], strs = [];
  for (let i = 0; i + 1 < bs.length; i++) {
    const seg = rows.filter(r => r[0] >= bs[i] - 0.1 && r[0] < bs[i + 1] - 0.1);
    if (!seg.length) continue;
    let pk = seg[0], tr = seg[0]; for (const r of seg) { if (r[1] > pk[1]) pk = r; if (r[1] < tr[1]) tr = r; }
    depth.push((pk[1] - tr[1]) / Math.max(1e-3, pk[1])); lag.push((pk[0] - bs[i]) * 1000); peaks.push(pk[1]); strs.push(bi.strength[bi.beats.indexOf(bs[i])]);
  }
  const med = a => [...a].sort((p, q) => p - q)[a.length >> 1];
  const mean = a => a.reduce((p, q) => p + q, 0) / a.length;
  const corr = (a, c) => { const ma = mean(a), mc = mean(c); let s = 0, sa = 0, sc = 0; for (let i = 0; i < a.length; i++) { s += (a[i] - ma) * (c[i] - mc); sa += (a[i] - ma) ** 2; sc += (c[i] - mc) ** 2; } return s / Math.sqrt(sa * sc + 1e-12); };
  if (process.env.DBG) { const w = strs; const mu = mean(w); console.log("   strengths in window: mean", mu.toFixed(2), "sd", Math.sqrt(mean(w.map(v => (v - mu) ** 2))).toFixed(2), " drops:", (globalThis.__drops || []).map(d => d.toFixed(1)).join(",")); }
  if (process.env.MAXFL) console.log(`  ${label}: largest throw ${maxFl.toFixed(2)}`);
  const out = { label, bpm: bi.bpm, depth: med(depth), lag: med(lag), sat: mean(rows.map(r => r[2])), level: mean(rows.map(r => r[1])), follow: corr(peaks, strs) };
  if (process.env.COOL) { const avg = a => a.reduce((p, q) => p + q, 0) / a.length;
    for (const [ti, tip] of [0.2, 0.5, 0.8, 1.0].entries()) console.log(`  ${label} dots at ${tip} of the way out: cool tint ${avg(cool[ti].map(r => r[0])).toFixed(2)} on average, 'settling' ${avg(cool[ti].map(r => r[1])).toFixed(2)}`);
    console.log(`  ${label} breakdown colour on average ${avg(cool.calm).toFixed(2)}`); }
  if (process.env.CJ2) console.log(`${label}: biggest one-frame change of each colour input -`, Object.entries(cj2).map(([k, x]) => `${k} ${x.toFixed(3)}`).join(", "));
  if (process.env.CJ) console.log(`${label}: biggest one-frame colour jump on the outer dots - throw-driven (before) ${cj.a.toFixed(2)}, smoothed (now) ${cj.b.toFixed(2)} at ${JSON.stringify(cj.at)}`);
  console.log(`${label.padEnd(26)} ${bi.bpm.toFixed(0)} BPM  dip between beats ${(100 * out.depth).toFixed(0).padStart(3)}%  peak ${out.lag.toFixed(0).padStart(4)} ms after beat  saturated ${(100 * out.sat).toFixed(0).padStart(3)}%  mean throw ${out.level.toFixed(2)}  big beats look bigger r=${out.follow.toFixed(2)}`);
  return { out, E, frames, beats: bi.beats, barPhase: bi.barPhase, from };
}
if (process.argv[2] === "baseline") {
  const probe = factory({ sourceSel: { value: "file" } });
  for (const [name, key] of [["Your tune #2", "YOUR_TUNE_2"], ["Your tune #3", "YOUR_TUNE_3"]])
    for (const [song, f] of [["Troy", "songs/troy.f32"], ["Doom", "songs/doom.f32"]]) await run(f, probe.presets[key], 30, 40, `${name} / ${song}`);
}
