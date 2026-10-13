// Space-time picture of each beat preset: across = the line as the screen lays it out (far left .. cover .. far
// right), down = time (4 bars), brightness = how far each column is thrown. Beat ticks on the left; long = the ONE.
import { run } from "./sim.mjs";
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const src = html.slice(html.indexOf("const YOUR_TUNE_3 ="), html.indexOf('document.getElementById("presetYours3")')) +
  html.slice(html.indexOf("const BEAT_LOOK ="), html.indexOf("for (const id in BEAT_PRESETS)"));
const { BEAT_LOOK, BEAT_PRESETS } = new Function(src + "; return { BEAT_LOOK, BEAT_PRESETS };")();
const [file, from] = [process.argv[2], +process.argv[3]], out = process.argv[4];
const panels = [];
for (const id of ["beatPunch", "barStomp", "beatPulse", "beatRipple", "stompRipple"]) {
  const r = await run(file, { ...BEAT_LOOK, ...BEAT_PRESETS[id] }, from, 8.2, id, true);
  panels.push(r);
}
const S = 4, PW = 96 * S, ML = 26, GAP = 22, TOP = 34, H = Math.min(...panels.map(p => p.frames.length)), W = panels.length * (ML + PW) + (panels.length - 1) * GAP;
const img = Buffer.alloc(W * (H + TOP) * 3, 12);
const put = (x, y, r, g, b) => { const o = ((y + TOP) * W + x) * 3; img[o] = r; img[o + 1] = g; img[o + 2] = b; };
panels.forEach((p, k) => {
  const x0 = k * (ML + PW + GAP);
  for (let y = 0; y < H; y++) for (let c = 0; c < 96; c++) {
    const v = Math.max(0, Math.min(1, p.frames[y][c] / 1.1)), r = 255 * Math.min(1, v * 1.3), g = 255 * v * v * 0.9, b = 255 * Math.min(1, v * 1.6);
    for (let i = 0; i < S; i++) put(x0 + ML + c * S + i, y, r, g, b);
  }
  for (let y = 0; y < H; y++) put(x0 + ML + 48 * S, y, 40, 40, 48);   // the cover
  p.beats.forEach((t, i) => { const y = Math.round((t - p.from) * 60); if (y < 0 || y >= H) return; const one = i % 4 === p.barPhase;
    for (let x = one ? 2 : 12; x < ML - 3; x++) for (let d = 0; d < (one ? 3 : 1); d++) if (y + d < H) put(x0 + x, y + d, one ? 255 : 150, one ? 255 : 150, one ? 255 : 150); });
});
fs.writeFileSync(out, Buffer.concat([Buffer.from(`P6 ${W} ${H + TOP} 255\n`), img]));
console.log(W, H + TOP);
