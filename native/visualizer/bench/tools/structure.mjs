import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const cut = (a, b) => html.slice(html.indexOf(a), html.indexOf(b));
const api = new Function(cut("function makeFFT", "const smooth =") + cut("// ---- beat tracking", "// ---- end beat tracking") + cut("// ---- layers", "// ---- end layers") + "; return { trackBeats, beatStrengths, analyseLayers };")();
const mmss = t => t > 1e8 ? "end" : `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, "0")}`;
const names = Object.fromEntries(fs.readFileSync("songs/names.txt", "utf8").trim().split("\n").map(l => [l.split(" ")[0], l.slice(l.indexOf(" ") + 1)]));
const list = [["Troy", "songs/troy.f32", 48000], ["At Doom's Gate (full)", "songs/doom.f32", 48000], ["Nostalgic Sand Prince", "songs/nsp.f32", 48000]];
for (let i = 3; i <= 6; i++) list.push([names["s" + i] + " (90 s)", `songs/s${i}.f32`, 44100]);
for (const [name, f, sr] of list) {
  const b = fs.readFileSync(f), x = new Float32Array(b.buffer, b.byteOffset, b.length / 4);
  const bi = api.beatStrengths(await api.trackBeats(x, sr));
  const t0 = Date.now(), L = await api.analyseLayers(x, sr, bi.beats, null, bi.barPhase), ms = Date.now() - t0;
  const voiced = L.melS.filter(v => v > 0.3).length / L.melS.length;
  console.log(`${name} (${(x.length / sr).toFixed(0)} s, ${ms} ms): ` + L.sections.map(s => `${"ABCDEF"[s.label]} ${mmss(s.start)}`).join("  ") + `   | melody present ${(100 * voiced).toFixed(0)}% of the time`);
}
