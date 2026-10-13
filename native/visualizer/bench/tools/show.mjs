import { run } from "./sim.mjs";
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const { SHOWCASE_BASE, SHOWCASE } = new Function(html.slice(html.indexOf("const YOUR_TUNE_4 ="), html.indexOf("const whiteToMagenta")) + "; return { SHOWCASE_BASE, SHOWCASE };")();
for (const id in SHOWCASE) await run("songs/troy.f32", { ...SHOWCASE_BASE, ...SHOWCASE[id].values }, 25, 20, SHOWCASE[id].name);
