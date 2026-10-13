import { run } from "./sim.mjs";
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const t4 = new Function(html.slice(html.indexOf("const YOUR_TUNE_4 ="), html.indexOf("const whiteToMagenta")) + "; return YOUR_TUNE_5;")();
for (const [song, f, from] of [["Troy", "songs/troy.f32", 30], ["Troy drop", "songs/troy.f32", 40], ["Doom", "songs/doom.f32", 30]]) await run(f, t4, from, 30, `tune #4 / ${song}`);
