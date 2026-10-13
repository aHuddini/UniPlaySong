import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const fftSrc = html.slice(html.indexOf("function makeFFT"), html.indexOf("const smooth ="));
const blk = html.slice(html.indexOf("// ---- beat tracking"), html.indexOf("// ---- end beat tracking"));
const api = new Function(fftSrc + blk + "; return { trackBeats };")();
const sr = 44100;
// a drum loop (kick on beats, snare on 2 and 4, hats on 8ths) whose tempo follows plan: [[seconds, bpm], ...]
function loop(plan, ramp) {
  let seed = 3; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647 * 2 - 1;
  const dur = plan.reduce((a, p) => a + p[0], 0), x = new Float32Array(Math.round(dur * sr)), truth = [];
  const add = (t0, len, fn) => { const s0 = Math.round(t0 * sr); for (let i = 0; i < len * sr && s0 + i < x.length; i++) x[s0 + i] += fn(i / sr); };
  let t = 0, beat = 0, seg = 0, segStart = 0;
  while (t < dur) {
    while (seg < plan.length - 1 && t >= segStart + plan[seg][0]) { segStart += plan[seg][0]; seg++; }
    let bpm = plan[seg][1];
    if (ramp && seg > 0) bpm = plan[seg - 1][1] + (plan[seg][1] - plan[seg - 1][1]) * Math.min(1, (t - segStart) / plan[seg][0]);
    truth.push(t); const p = 60 / bpm;
    let ph = 0; add(t, 0.3, s => { ph += 2 * Math.PI * (50 + 100 * Math.exp(-s * 30)) / sr; return 0.8 * Math.sin(ph) * Math.exp(-s * 10); });
    if (beat % 2 === 1) add(t, 0.2, s => 0.4 * rnd() * Math.exp(-s * 20));
    add(t + p / 2, 0.05, s => 0.15 * rnd() * Math.exp(-s * 70)); add(t, 0.05, s => 0.15 * rnd() * Math.exp(-s * 70));
    t += p; beat++;
  }
  return { x, truth };
}
const cases = [["120 -> 150 BPM jump at 30 s", [[30, 120], [30, 150]], false],
               ["100 -> 140 BPM gradual over 30 s", [[20, 100], [30, 140], [10, 140]], true],
               ["steady 128 BPM", [[60, 128]], false]];
for (const [name, plan, ramp] of cases) {
  const { x, truth } = loop(plan, ramp), r = await api.trackBeats(x, sr);
  const parts = [[0, 30], [30, 60]].map(([a, b]) => { const tr = truth.filter(t => t >= a + 2 && t < b - 2); return `${a}-${b}s: ${(100 * tr.filter(t => r.beats.some(m => Math.abs(m - t) <= 0.035)).length / tr.length).toFixed(0)}%`; });
  console.log(`${name.padEnd(34)} tempo picked ${r.bpm.toFixed(1)} | real beats caught ${parts.join("  ")}`);
}
