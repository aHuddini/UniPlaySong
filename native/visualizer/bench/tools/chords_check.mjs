// Chords check: synthesises progressions with known chords and key (harmonic tones, a bass root, 120 BPM, a chord per
// bar), runs the bench's own analyseLayers on them and scores its chords and key. With a song file it prints what it
// finds instead (no ground truth): the key, the chords bar by bar, and how many chord changes land on a bar's first beat.
//   node chords_check.mjs                       synthetic progressions, scored
//   node chords_check.mjs <mono48k.f32>         a real song
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const cut = (a, b) => html.slice(html.indexOf(a), html.indexOf(b));
const src = cut("function makeFFT", "async function mapBuffer") + cut("// ---- beat tracking", "// Each beat's strength") + cut("function beatStrengths", "// How long the spring")
  + cut("// ---- layers", "// ---- end layers");
const lib = new Function(`let mapStatus = ""; ${src}; return { trackBeats, beatStrengths, analyseLayers };`)();
const sr = 48000, NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];
const pcts = a => { const v = Array.from(a).sort((p, q) => p - q); return [0.1, 0.5, 0.9].map(f => v[Math.floor(f * (v.length - 1))].toFixed(2)).join("/"); };
const name = c => c < 0 ? "-" : NAMES[c % 12] + (c >= 12 ? "m" : "");
const parse = s => { const m = /^([A-G][#b]?)(m?)$/.exec(s); return NAMES.indexOf(m[1]) + (m[2] ? 12 : 0); };

function synth(prog, beat = 0.5) {
  const x = new Float32Array(Math.round(prog.length * 4 * beat * sr));
  prog.forEach((c, bar) => {
    const root = c % 12, tones = [root, root + (c >= 12 ? 3 : 4), root + 7];
    const t0 = Math.round(bar * 4 * beat * sr), n = Math.round(4 * beat * sr);
    const notes = [...tones.map(p => 48 + ((p - 48) % 12 + 12) % 12 + 12), 36 + root];   // chord around C4, bass in C2..B2
    for (const m of notes) {
      const f = 440 * Math.pow(2, (m - 69) / 12);
      for (let i = 0; i < n; i++) {
        const t = i / sr, env = Math.min(1, t / 0.01) * Math.exp(-t * 0.6);
        let v = 0; for (let h = 1; h <= 6; h++) v += Math.sin(2 * Math.PI * f * h * t) / h;
        x[t0 + i] += 0.08 * env * v;
      }
    }
  });
  return { x, beats: Array.from({ length: prog.length * 4 }, (_, i) => i * beat) };
}

const file = process.argv[2];
if (!file) {
  const tests = [
    { title: "C major: I vi IV V", key: "C", prog: "C Am F G C Am F G" },
    { title: "A minor: i iv V i VI VII", key: "Am", prog: "Am Dm E Am F G Am E" },
    { title: "Eb major: I IV ii V", key: "Eb", prog: "Eb Ab Fm Bb Eb Ab Fm Bb" },
    { title: "F# minor: i VI III VII", key: "F#m", prog: "F#m D A E F#m D A E" },
  ];
  let ok = 0, all = 0;
  for (const t of tests) {
    const prog = t.prog.split(" ").map(parse), { x, beats } = synth(prog);
    const ly = await lib.analyseLayers(x, sr, beats, null, 0);
    let hit = 0;
    for (let i = 0; i < beats.length; i++) if (ly.chord[i] === prog[Math.floor(i / 4)]) hit++;
    ok += hit; all += beats.length;
    const key = name(ly.key.root + 12 * ly.key.minor), want = parse(t.key);
    // relative keys (A minor / C major) share every chord and the same home on the circle of fifths - the look can't
    // tell them apart, and some progressions (i VI III VII = vi IV I V) are honestly both
    const relMaj = c => c >= 12 ? (c + 3) % 12 : c, rel = relMaj(ly.key.root + 12 * ly.key.minor) === relMaj(want);
    console.log(`${t.title}: chords ${hit}/${beats.length} beats, key ${key} (want ${t.key}${key === t.key ? "" : rel ? " - its relative, same home" : " - WRONG"}), conf ${ly.key.conf.toFixed(2)}`);
    console.log(`  found ${Array.from({ length: prog.length }, (_, b) => name(ly.chord[b * 4 + 1])).join(" ")}`);
    console.log(`  held share 10/50/90%: ${pcts(ly.heldShare)}`);
    console.log(`  major/minor strength (signed) 10/50/90%: ${pcts(ly.mode)}`);
  }
  console.log(`overall ${ok}/${all} beats (${(100 * ok / all).toFixed(1)}%)`);
  // drums only: noise bursts on every beat (a kick-ish low thump and a hat-ish hiss) - no chord should be found
  { const n = 32, x = new Float32Array(n * 0.5 * sr); let seed = 1; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647 - 0.5;
    for (let b = 0; b < n; b++) { const t0 = b * 0.5 * sr; for (let i = 0; i < 0.25 * sr; i++) { const t = i / sr; x[t0 + i] += 0.5 * Math.sin(2 * Math.PI * 55 * t * (1 - t)) * Math.exp(-t * 18) + 0.15 * rnd() * Math.exp(-t * 30); } }
    const ly = await lib.analyseLayers(x, sr, Array.from({ length: n }, (_, i) => i * 0.5), null, 0);
    const none = ly.chord.filter(c => c < 0).length, best = Array.from(ly.chordConf).filter(v => v > 0).sort((p, q) => q - p)[0];
    console.log(`drums only: no chord on ${none}/${n} beats${none < n ? ` - found ${[...new Set(Array.from(ly.chord).filter(c => c >= 0).map(name))].join(" ")}, best match ${best?.toFixed(2)}` : ""}, held share 10/50/90%: ${pcts(ly.heldShare)}`); }
} else {
  const b = fs.readFileSync(file), x = new Float32Array(b.buffer, b.byteOffset, b.length / 4);
  const beats = lib.beatStrengths(await lib.trackBeats(x, sr));
  const ly = await lib.analyseLayers(x, sr, beats.beats, null, beats.barPhase);
  const n = ly.chord.length, none = ly.chord.filter(c => c < 0).length;
  let changes = 0, onOne = 0;
  for (let i = 1; i < n; i++) if (ly.chord[i] !== ly.chord[i - 1]) { changes++; if ((i - beats.barPhase) % 4 === 0) onOne++; }
  console.log(`key ${name(ly.key.root + 12 * ly.key.minor)} (conf ${ly.key.conf.toFixed(2)}), ${beats.beats.length} beats at ${beats.bpm.toFixed(0)} BPM`);
  console.log(`no chord on ${none}/${n} beats; ${changes} changes, ${onOne} on a bar's first beat (${(100 * onOne / Math.max(1, changes)).toFixed(0)}%, chance 25%)`);
  const bars = [];
  for (let i = ((beats.barPhase % 4) + 4) % 4; i < n; i += 4) bars.push(Array.from(ly.chord.slice(i, i + 4)).map(name).join(","));
  console.log("bars (4 beats each):");
  for (let i = 0; i < bars.length; i += 8) console.log(`  ${String(i + 1).padStart(3)}: ${bars.slice(i, i + 8).map(s => s.padEnd(15)).join(" ")}`);
  const conf = Array.from(ly.chordConf).filter(v => v > 0).sort((p, q) => p - q);
  console.log(`held share 10/50/90%: ${pcts(ly.heldShare)}`);
  { const m = Array.from(ly.mode).filter((_, i) => ly.chord[i] >= 0).map(Math.abs); console.log(`major/minor strength |mode| 10/50/90%: ${pcts(m)}; root changes ${Array.from(ly.chord).filter((c, i) => i && c >= 0 && ly.chord[i - 1] >= 0 && c % 12 !== ly.chord[i - 1] % 12).length}`); }
  console.log(`match score (chord beats): median ${conf[conf.length >> 1]?.toFixed(2)}, 10% ${conf[Math.floor(conf.length * 0.1)]?.toFixed(2)}`);
}
