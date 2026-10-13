import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const fftSrc = html.slice(html.indexOf("function makeFFT"), html.indexOf("const smooth ="));
const block = html.slice(html.indexOf("// ---- drum detection"), html.indexOf("// ---- end drum detection"));
const api = new Function(fftSrc + block + "; return { detectDrums, scoreHits, makeDrumTest };")();
const sr = 48000, d = api.makeDrumTest(sr);
const grid = JSON.parse(process.argv[3] || "{}");
const base = { kickDelta: 0.3, snareDelta: 0.3, hatDelta: 0.3, kickSustain: 0.6, split: 0.5, ...grid };
const t0 = Date.now(); const r = await api.detectDrums(d.x, sr, base); const ms = Date.now() - t0;
const fmt = s => `${s.hit}/${s.total} miss ${s.miss} extra ${s.extra} ${(s.mean * 1000).toFixed(1)}±${(s.sd * 1000).toFixed(1)}ms`;
console.log(JSON.stringify(base), `${ms} ms for 32 s`);
for (const k of ["kick", "snare", "hat", "bass"]) console.log(k.padEnd(6), fmt(api.scoreHits(r[k], d.truth[k])));
console.log("bass taken as kick:", api.scoreHits(r.kick, d.truth.bass).hit, "  kicks taken as bass:", api.scoreHits(r.bass, d.truth.kick).hit);
if (process.env.LIST) for (const k of ["kick","snare","hat","bass"]) { const tr = d.truth[k]; const miss = tr.filter(t => !r[k].some(f => Math.abs(f - t) <= 0.03)); const extra = r[k].filter(f => !tr.some(t => Math.abs(f - t) <= 0.03)); console.log(k, "missed", miss.map(v=>v.toFixed(3)).join(" "), "| extra", extra.map(v=>v.toFixed(3)).join(" ")); }
