// Analysis parity: runs the bench's own song analysis (buildMap as mapBuffer feeds it, trackBeats, beatStrengths - lifted
// from fountain-bench.html, as sim.mjs does) on a mono 48 kHz song and compares it with the renderer's port
// (UniPlaySongVisualizer.exe --analyze <pcm> <json>) on the same samples.
//   node analysis_parity.mjs <mono48k.f32> <renderer.json>
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const cut = (a, b) => html.slice(html.indexOf(a), html.indexOf(b));
const src = cut("function makeFFT", "async function mapBuffer") + cut("// ---- beat tracking", "// Each beat's strength") + cut("function beatStrengths", "// How long the spring")
  + cut("// ---- layers", "// ---- end layers");
const lib = new Function(`let mapStatus = ""; ${src}; return { makeFFT, buildMap, trackBeats, beatStrengths, analyseLayers };`)();
const [file, cppFile] = process.argv.slice(2);
const b = fs.readFileSync(file), x = new Float32Array(b.buffer, b.byteOffset, b.length / 4), sr = 48000;

// the song map, exactly as mapBuffer builds it (both channels the same mono)
const n = 1024, hop = 1024, fft = lib.makeFFT(n), frames = Math.floor((x.length - n) / hop);
const re = new Float32Array(n), im = new Float32Array(n), mag = new Float32Array(n / 2);
const win = new Float32Array(n).map((_, i) => 0.5 - 0.5 * Math.cos(2 * Math.PI * i / (n - 1)));
const map = await lib.buildMap(frames, sr / hop, sr / n, f => {
  const o = f * hop;
  for (let i = 0; i < n; i++) { re[i] = (x[o + i] + x[o + i]) * 0.5 * win[i]; im[i] = 0; }
  fft(re, im);
  for (let i = 0; i < n / 2; i++) mag[i] = Math.hypot(re[i], im[i]);
  return mag;
}, "song");
const beats = lib.beatStrengths(await lib.trackBeats(x, sr));
const cpp = JSON.parse(fs.readFileSync(cppFile, "utf8"));

const maxDiff = (a, c) => { let m = 0, at = -1; if (a.length !== c.length) return `LENGTH ${a.length} vs ${c.length}`; for (let i = 0; i < a.length; i++) { const d = Math.abs(a[i] - c[i]); if (d > m) { m = d; at = i; } } return `${m.toExponential(2)}${at >= 0 ? " at " + at : ""}`; };
console.log("song map:   bench vs renderer");
console.log(`  frames ${map.frames} / ${cpp.map.frames}, beat ${map.beat} / ${cpp.map.beat}, offset ${map.offset} / ${cpp.map.offset}`);
console.log(`  section ${maxDiff(map.section, cpp.map.section)}, tension ${maxDiff(map.tension, cpp.map.tension)}`);
console.log(`  drops ${JSON.stringify(map.drops.map(d => +d.toFixed(3)))} / ${JSON.stringify(cpp.map.drops.map(d => +d.toFixed(3)))}`);
console.log("beats:");
console.log(`  bpm ${beats.bpm} / ${cpp.beats.bpm}, barPhase ${beats.barPhase} / ${cpp.beats.barPhase}, period ${beats.period} / ${cpp.beats.period}`);
console.log(`  onset envelope ${maxDiff(beats.env, cpp.beats.env)}`);
console.log(`  ${beats.beats.length} / ${cpp.beats.beats.length} beats, times ${maxDiff(beats.beats, cpp.beats.beats)}, strengths ${maxDiff(beats.strength, cpp.beats.strength)}`);

// the layers, on the bench's own beats (as runLayers passes them)
const ly = await lib.analyseLayers(x, sr, beats.beats, null, beats.barPhase), cl = cpp.layers;
console.log("layers:");
console.log(`  fr ${ly.fr} / ${cl.fr}, pad ${maxDiff(ly.pad, cl.pad)}, melody pitch ${maxDiff(ly.melF, cl.melF)}, melody strength ${maxDiff(ly.melS, cl.melS)}`);
// the renderer stores the harmony as f32 in the file; here both are full precision
console.log(`  harmony ${maxDiff(ly.harm, cl.harm)}, clarity ${maxDiff(ly.clarity, cl.clarity)}`);
const fmt = ss => ss.map(s => `${"ABCDEF"[s.label]}@${s.start.toFixed(2)}`).join(" ");
console.log(`  sections ${fmt(ly.sections)}\n         / ${fmt(cl.secStart.map((st, i) => ({ start: st, label: cl.secLabel[i] })))}`);
console.log(`  section ends ${maxDiff(ly.sections.map(s => s.end), cl.secEnd)}`);
