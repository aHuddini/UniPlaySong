// Chords vs stems: the bench's chord layer (analyseLayers, lifted from fountain-bench.html) on the full mix against the
// same analysis of bass + other only (C:\Projects\audiostemtool, Demucs) - the harmony without drums or voice - on the
// same beats (tracked on the mix). How often the mix's chord, its root and its major/minor match the clean version,
// where "none" falls, and whether the key agrees.
//   node chords_vs_stems.mjs <song out dir> ...      (mix.f32, bass.f32, other.f32 - mono 48 kHz, as stems.py writes)
import fs from "fs";
import path from "path";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const cut = (a, b) => { const i = html.indexOf(a), j = html.indexOf(b, i); if (i < 0 || j < 0) throw new Error(`no ${a}`); return html.slice(i, j); };
const lib = new Function(`let mapStatus = ""; ${cut("function makeFFT", "async function mapBuffer") + cut("// ---- beat tracking", "// Each beat's strength")
  + cut("function beatStrengths", "// How long the spring") + cut("// ---- layers", "// ---- end layers")}; return { trackBeats, beatStrengths, analyseLayers, CHORD_NAMES };`)();
const read = f => { const b = fs.readFileSync(f); return new Float32Array(b.buffer, b.byteOffset, b.length / 4); };
const name = c => c < 0 ? "-" : lib.CHORD_NAMES[c % 12] + (c >= 12 ? "m" : "");
const keyName = k => lib.CHORD_NAMES[k.root] + (k.minor ? "m" : "");
const relMaj = k => k.minor ? (k.root + 3) % 12 : k.root;
const tot = { beats: 0, same: 0, root: 0, both: 0, mixNone: 0, cleanNone: 0 };
for (const d of process.argv.slice(2)) {
  const mix = read(path.join(d, "mix.f32")), bass = read(path.join(d, "bass.f32")), other = read(path.join(d, "other.f32"));
  const n = Math.min(mix.length, bass.length, other.length), clean = new Float32Array(n);
  for (let i = 0; i < n; i++) clean[i] = bass[i] + other[i];
  const beats = lib.beatStrengths(await lib.trackBeats(mix, 48000));
  const a = await lib.analyseLayers(mix, 48000, beats.beats, null, beats.barPhase), b = await lib.analyseLayers(clean, 48000, beats.beats, null, beats.barPhase);
  let same = 0, root = 0, both = 0, mixNone = 0, cleanNone = 0;
  const N = a.chord.length;
  for (let i = 0; i < N; i++) {
    const x = a.chord[i], y = b.chord[i];
    if (x < 0) mixNone++; if (y < 0) cleanNone++;
    if (x >= 0 && y >= 0) { both++; if (x === y) same++; if (x % 12 === y % 12) root++; }
  }
  for (const [k, v] of Object.entries({ beats: N, same, root, both, mixNone, cleanNone })) tot[k] += v;
  const pct = (v, of) => `${(100 * v / Math.max(1, of)).toFixed(0)}%`;
  console.log(`${path.basename(d)}: ${N} beats; where both found a chord (${both}): same chord ${pct(same, both)}, same root ${pct(root, both)}; none: mix ${mixNone}, clean ${cleanNone}`);
  console.log(`  key: mix ${keyName(a.key)}, clean ${keyName(b.key)}${a.key.root === b.key.root && a.key.minor === b.key.minor ? "" : relMaj(a.key) === relMaj(b.key) ? " (relative - same home)" : " - DIFFERENT"}`);
  // the first 16 bars side by side
  const bar = i => Array.from({ length: 4 }, (_, j) => name(a.chord[i + j])).join(",") + " | " + Array.from({ length: 4 }, (_, j) => name(b.chord[i + j])).join(",");
  for (let i = ((beats.barPhase % 4) + 4) % 4, c = 0; i + 4 <= N && c < 8; i += 16, c++) console.log(`  bar ${String(c * 4 + 1).padStart(3)}  mix | clean: ${bar(i)}`);
}
console.log(`\nall: same chord ${(100 * tot.same / Math.max(1, tot.both)).toFixed(0)}%, same root ${(100 * tot.root / Math.max(1, tot.both)).toFixed(0)}% of ${tot.both} beats both found a chord; none: mix ${tot.mixNone}, clean ${tot.cleanNone} of ${tot.beats}`);
