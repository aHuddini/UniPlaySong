// Bass vs stems: the bench's bass line (analyseLayers' bassNote, lifted from fountain-bench.html) on the full mix,
// against two references on the same beats (tracked on the mix):
//   pYIN  - librosa's pitch tracker on the isolated bass stem (C:\Projects\audiostemtool, bass_pyin.py): independent
//   stem  - the bench's own tracker on the isolated bass stem: what the rest of the mix costs it
// Per beat: the note (MIDI), its pitch class, octave slips; coverage (beats with a note); and how often the bass note is
// the chord's root (the mix's chords) - whether the bass could help the chords.
//   node bass_vs_stems.mjs <song out dir> ...        (mix.f32, bass.f32, bass_pyin.json)
import fs from "fs";
import path from "path";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8").split("\r\n").join("\n");
const cut = (a, b) => { const i = html.indexOf(a), j = html.indexOf(b, i); if (i < 0 || j < 0) throw new Error(`no ${a}`); return html.slice(i, j); };
const lib = new Function(`let mapStatus = ""; ${cut("function makeFFT", "async function mapBuffer") + cut("// ---- beat tracking", "// Each beat's strength")
  + cut("function beatStrengths", "// How long the spring") + cut("// ---- layers", "// ---- end layers")}; return { trackBeats, beatStrengths, analyseLayers };`)();
const read = f => { const b = fs.readFileSync(f); return new Float32Array(b.buffer, b.byteOffset, b.length / 4); };
const pct = (n, of) => of ? `${(100 * n / of).toFixed(0)}%` : "-";
const tot = {};
const add = (k, v) => tot[k] = (tot[k] || 0) + v;
const leads = { pitched: [], unpitched: [] }, shares = { pitched: [], unpitched: [] };
for (const d of process.argv.slice(2)) {
  const mix = read(path.join(d, "mix.f32")), bass = read(path.join(d, "bass.f32"));
  const beats = lib.beatStrengths(await lib.trackBeats(mix, 48000)), B = beats.beats;
  const a = await lib.analyseLayers(mix, 48000, B, null, beats.barPhase), s = await lib.analyseLayers(bass, 48000, B, null, beats.barPhase);
  // pYIN per beat: the median pitch over the beat, where at least 40% of its frames are pitched
  let ref = null;
  const pf = path.join(d, "bass_pyin.json");
  if (fs.existsSync(pf)) {
    const py = JSON.parse(fs.readFileSync(pf, "utf8"));
    ref = B.map((t, i) => {
      const end = B[i + 1] ?? t + 0.5, v = [];
      let n = 0; for (let j = 0; j < py.times.length; j++) if (py.times[j] >= t && py.times[j] < end) { n++; if (py.midi[j] !== null) v.push(py.midi[j]); }
      if (!n || v.length < 0.4 * n) return -1;
      v.sort((p, q) => p - q); return Math.round(v[v.length >> 1]);
    });
  }
  const cmp = (x, y) => { let both = 0, note = 0, pc = 0, oct = 0;
    for (let i = 0; i < B.length; i++) if (x[i] >= 0 && y[i] >= 0) { both++; if (x[i] === y[i]) note++; if ((x[i] - y[i]) % 12 === 0) pc++; if (x[i] !== y[i] && (x[i] - y[i]) % 12 === 0) oct++; }
    return { both, note, pc, oct }; };
  const cov = x => x.filter(v => v >= 0).length;
  const line = (label, r) => `${label}: same note ${pct(r.note, r.both)}, same pitch class ${pct(r.pc, r.both)} (octave slips ${pct(r.oct, r.both)}) of ${r.both} beats`;
  console.log(`${path.basename(d)}: ${B.length} beats; a note on ${pct(cov(a.bassNote), B.length)} (mix), ${pct(cov(s.bassNote), B.length)} (stem)${ref ? `, ${pct(cov(ref), B.length)} (pYIN)` : ""}`);
  if (ref) { const r1 = cmp(a.bassNote, ref), r2 = cmp(s.bassNote, ref); console.log("  " + line("mix vs pYIN ", r1)); console.log("  " + line("stem vs pYIN", r2));
    for (const [k, r] of [["mp", r1], ["sp", r2]]) for (const f of ["both", "note", "pc", "oct"]) add(k + f, r[f]);
    for (let i = 0; i < B.length; i++) { (ref[i] >= 0 ? leads.pitched : leads.unpitched).push(a.bassLead[i]); (ref[i] >= 0 ? shares.pitched : shares.unpitched).push(a.bassShare[i]); }
    const sh = [...a.bassShare].sort((p, q) => p - q); console.log(`  bass share of the held sound: median ${sh[sh.length >> 1].toFixed(3)}`); }
  const r3 = cmp(a.bassNote, s.bassNote); console.log("  " + line("mix vs stem ", r3));
  for (const f of ["both", "note", "pc", "oct"]) add("ms" + f, r3[f]);
  let root = 0, rn = 0; for (let i = 0; i < B.length; i++) if (a.bassNote[i] >= 0 && a.chord[i] >= 0) { rn++; if (a.bassNote[i] % 12 === a.chord[i] % 12) root++; }
  console.log(`  bass note = the chord's root on ${pct(root, rn)} of ${rn} beats`); add("root", root); add("rn", rn);
  add("beats", B.length); add("covMix", cov(a.bassNote)); if (ref) add("covRef", cov(ref));
}
console.log(`\nall ${tot.beats} beats - coverage: mix ${pct(tot.covMix, tot.beats)}, pYIN ${pct(tot.covRef || 0, tot.beats)}`);
if (tot.mpboth) console.log(`  mix vs pYIN : same note ${pct(tot.mpnote, tot.mpboth)}, pitch class ${pct(tot.mppc, tot.mpboth)}, octave slips ${pct(tot.mpoct, tot.mpboth)}`);
if (tot.spboth) console.log(`  stem vs pYIN: same note ${pct(tot.spnote, tot.spboth)}, pitch class ${pct(tot.sppc, tot.spboth)}, octave slips ${pct(tot.spoct, tot.spboth)}`);
console.log(`  mix vs stem : same note ${pct(tot.msnote, tot.msboth)}, pitch class ${pct(tot.mspc, tot.msboth)}`);
console.log(`  bass = chord root: ${pct(tot.root, tot.rn)}`);
const q = (a, p) => { const v = [...a].sort((x, y) => x - y); return v.length ? v[Math.floor(p * (v.length - 1))].toFixed(2) : "-"; };
console.log(`  mix 'lead' (best note's score over the average) where pYIN hears a bass: p10 ${q(leads.pitched, 0.1)} p50 ${q(leads.pitched, 0.5)}; where it doesn't: p50 ${q(leads.unpitched, 0.5)} p90 ${q(leads.unpitched, 0.9)}`);
console.log(`  bass share where pYIN hears a bass: p10 ${q(shares.pitched, 0.1)} p25 ${q(shares.pitched, 0.25)} p50 ${q(shares.pitched, 0.5)}; where it doesn't: p50 ${q(shares.unpitched, 0.5)} p75 ${q(shares.unpitched, 0.75)} p90 ${q(shares.unpitched, 0.9)}`);
