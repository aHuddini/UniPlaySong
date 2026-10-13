import { run } from "./sim.mjs";
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const t5 = new Function(html.slice(html.indexOf("const YOUR_TUNE_4 ="), html.indexOf("const whiteToMagenta")) + "; return YOUR_TUNE_5;")();
await run("songs/troy.f32", t5, 20, 40, "Troy 0:20-1:00");
