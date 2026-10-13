import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const fftSrc = html.slice(html.indexOf("function makeFFT"), html.indexOf("const smooth ="));
const api = new Function(fftSrc + html.slice(html.indexOf("// ---- beat tracking"), html.indexOf("// ---- end beat tracking")) + "; return { trackBeats };")();
const ref = JSON.parse(fs.readFileSync("songs/ref.json")), names = Object.fromEntries(fs.readFileSync("songs/names.txt", "utf8").trim().split("\n").map(l => [l.split(" ")[0], l.slice(l.indexOf(" ") + 1)]));
const out = {};
for (let i = 1; i <= 6; i++) {
  const b = fs.readFileSync(`songs/s${i}.f32`), x = new Float32Array(b.buffer, b.byteOffset, b.length / 4);
  const t0 = Date.now(), r = await api.trackBeats(x, 44100), ms = Date.now() - t0, R = ref[`s${i}`];
  const match = R.beats.filter(t => r.beats.some(m => Math.abs(m - t) <= 0.035)).length;
  const offs = R.beats.map(t => { let d = 9; for (const m of r.beats) if (Math.abs(m - t) < Math.abs(d)) d = m - t; return d; }).filter(d => Math.abs(d) <= 0.035);
  console.log(`${names["s" + i].padEnd(32)} ours ${r.bpm.toFixed(1)} BPM ${r.beats.length} beats | librosa ${R.tempo.toFixed(1)} BPM ${R.beats.length} | librosa beats matched ${(100 * match / R.beats.length).toFixed(0)}%  offset ${(1000 * offs.reduce((a, v) => a + v, 0) / Math.max(1, offs.length)).toFixed(1)} ms  (${ms} ms)`);
  out[`s${i}`] = r.beats;
}
fs.writeFileSync("songs/beats_ours.json", JSON.stringify(out));
