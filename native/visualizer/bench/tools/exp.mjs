import { run } from "./sim.mjs";
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const t3 = new Function(html.slice(html.indexOf("const YOUR_TUNE_3 ="), html.indexOf('document.getElementById("presetYours3")')) + "; return YOUR_TUNE_3;")();
const base = { ...t3, BEAT_DRIVE: 1, BEAT_LEAD: 1 };
const C = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
for (const [name, o] of Object.entries(C)) for (const [song, f] of [["Troy", "songs/troy.f32"], ["Doom", "songs/doom.f32"]]) await run(f, { ...base, ...o }, 30, 30, `${name} / ${song}`);
