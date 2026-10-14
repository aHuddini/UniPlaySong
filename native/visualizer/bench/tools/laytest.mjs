import { run } from "./sim.mjs";
import fs from "fs";
const html = fs.readFileSync("C:/Projects/UniPSound/UniPlaySong/native/visualizer/bench/fountain-bench.html", "utf8");
const { SHOWCASE_BASE, SHOWCASE } = new Function(html.slice(html.indexOf("const YOUR_TUNE_4 ="), html.indexOf("const whiteToMagenta")) + "; return { SHOWCASE_BASE, SHOWCASE };")();
const r = await run("songs/troy.f32", { ...SHOWCASE_BASE, ...SHOWCASE.showHalo.values }, 30, 3, "halo");
const st = r.E.state(); console.log("LAYOUT", SHOWCASE.showHalo.values.LAYOUT, "layoutA", st.layoutA, "layoutB", st.layoutB, "morphT", st.morphT);
