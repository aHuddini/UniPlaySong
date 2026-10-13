import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const src = html.slice(html.indexOf("function makeFFT"), html.indexOf("async function mapBuffer"));
const synth = html.slice(html.indexOf("let songPart"), html.indexOf("// ---- song map"));
const api = new Function("let mapStatus = ''; const sourceSel = { value: 'song' }; const Q = () => ({ BPM: 120, KICK_LEVEL: 1 });" + synth + src + "; return { makeFFT, buildMap, synth };")();
const names = Object.fromEntries(fs.readFileSync("songs/names.txt", "utf8").trim().split("\n").map(l => [l.split(" ")[0], l.slice(l.indexOf(" ") + 1)]));
for (let i = 1; i <= 6; i++) {
  const b = fs.readFileSync(`songs/s${i}.f32`), a = new Float32Array(b.buffer, b.byteOffset, b.length / 4), sr = 44100;
  const n = 1024, hop = 1024, fft = api.makeFFT(n), re = new Float32Array(n), im = new Float32Array(n), mag = new Float32Array(n / 2);
  const win = new Float32Array(n).map((_, k) => 0.5 - 0.5 * Math.cos(2 * Math.PI * k / (n - 1)));
  const m = await api.buildMap(Math.floor((a.length - n) / hop), sr / hop, sr / n, f => { const o = f * hop; for (let k = 0; k < n; k++) { re[k] = a[o + k] * win[k]; im[k] = 0; } fft(re, im); for (let k = 0; k < n / 2; k++) mag[k] = Math.hypot(re[k], im[k]); return mag; }, "song");
  console.log(names["s" + i].padEnd(32), "drops:", m.drops.map(d => d.toFixed(1)).join(", ") || "none");
}
const mm = new Float32Array(512), fr = 46.875;
const m = await api.buildMap(Math.round(48 * fr), fr, 46.875, f => { api.synth(f / fr, mm, 46.875); return mm; }, "synthetic song");
console.log("synthetic song (drop at 34 s)".padEnd(32), "drops:", m.drops.map(d => d.toFixed(1)).join(", "));
