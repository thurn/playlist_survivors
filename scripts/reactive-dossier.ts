/**
 * Feel dossiers (docs/design/reactive-feel.md, "The Dossier"):
 *
 *   npm run reactive:dossier -- --track notjam-breakbeat-chips --round 0
 *   npm run reactive:dossier -- --split iteration --round 0     (every iteration track, one after another)
 *   npm run reactive:dossier -- --anchor --round 0              (the authored Galop, play mode)
 *
 * Writes captures/feel/round-<n>/<id>/ (gitignored): metrics.json and metrics.md, frames.json (the
 * evaluator's frame state at 240 Hz), timeline.png, up to 4 section sheets, a phrase sheet, an
 * honest-against-masher sheet, a moments sheet, and mix.ogg (the track with the honest run's sounds,
 * for the user: raters never get audio). Seed 1858, damage off. `--no-mix` skips the mix. Held-out
 * tracks are refused: their dossiers are a milestone's (docs/design/reactive-feel.md, "Held-out Protocol").
 */
import { execFileSync } from "node:child_process";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import { closeBrowser, openGame, sheet, tile, type GamePage, type Hook } from "./lib/capture.ts";
import { FEEL_SEED, metricsMarkdown, sampleFrames, trackMetrics, trackRuns, type FrameSeries, type TrackMetrics, type TrackRuns } from "./lib/feel.ts";
import { renderTimeline } from "./lib/feel-timeline.ts";
import { setPieceTimes } from "../src/planner/parity.ts";
import type { Plan } from "../src/plan/types.ts";
import { IDENTITY_BY_S } from "../src/plan/reactive-look.ts";
import { ROOT } from "./lib/files.ts";
import { propsFromAtlas } from "./lib/plan-node.ts";
import { corpusTrackIds } from "./lib/reactive-node.ts";
import { withDevServer } from "./lib/server.ts";

const { values } = parseArgs({
  options: {
    track: { type: "string" },
    split: { type: "string" },
    anchor: { type: "boolean" },
    round: { type: "string", default: "0" },
    rate: { type: "string", default: "240" },
    "no-mix": { type: "boolean" },
  },
});
const heldOut = new Set(corpusTrackIds("held-out"));
const ids = values.track ? values.track.split(",") : values.split === "iteration" ? corpusTrackIds("iteration") : [];
if (values.split && values.split !== "iteration") {
  console.error("dossiers cover the iteration split (held-out dossiers are a milestone's)");
  process.exit(2);
}
if (ids.some((id) => heldOut.has(id))) {
  console.error("held-out tracks get no dossier outside a milestone");
  process.exit(2);
}
if (!ids.length && !values.anchor) {
  console.error("usage: npm run reactive:dossier -- (--track <id>[,<id>…] | --split iteration | --anchor) [--round <n>]");
  process.exit(2);
}
const roundDir = join(ROOT, `captures/feel/round-${values.round}`);
const rate = Number(values.rate);
if (!(rate >= 60)) {
  console.error("--rate takes a frame rate of at least 60 Hz");
  process.exit(2);
}
const props = propsFromAtlas();
const t = (s: number) => `${Math.floor(s / 60)}:${(s % 60).toFixed(2).padStart(5, "0")}`;

/** The beats of a musical bar from `down` (the reference's meter, else 4). */
function barBeats(beats: readonly number[], down: number, meter: number): number[] {
  const k = beats.findIndex((b) => Math.abs(b - down) < 1e-6);
  return k < 0 ? [] : beats.slice(k, k + meter + 1);
}

interface Moment {
  s: number;
  label: string;
}

/** The track's bars (downbeats when the meter is confident, else every 4 reference beats), its meter, and its sections. */
function structureOf(r: TrackRuns): { meter: number; downs: number[]; sections: { label: string; s0: number; s1: number }[] } {
  const ref = r.ref;
  const confident = ref.meterConfidence === "high" && ref.downbeats?.length;
  const meter = confident ? ref.meter! : 4;
  const downs = confident ? ref.downbeats! : ref.beats.filter((_, i) => i % 4 === 0);
  const dur = r.run.stream.audio.duration;
  const sections = ref.sections?.length ? ref.sections : [0, 1, 2, 3].map((i) => ({ label: `quarter ${i + 1}`, s0: (i * dur) / 4, s1: ((i + 1) * dur) / 4 }));
  return { meter, downs, sections };
}

async function dossier(base: string, r: TrackRuns, metrics: TrackMetrics, dir: string, play: boolean): Promise<string[]> {
  const written: string[] = [];
  const queryFor = (player: string) => (play ? `mode=play&player=${player}&nodamage` : `mode=reactive&track=${r.id}&player=${player}&nodamage`);
  const honest = await openGame(base, queryFor(play ? "moderate" : "reactive-moderate"), { seed: String(FEEL_SEED) });
  sameRun(honest, r.plans["reactive-moderate"], `${r.id} honest`, metrics);
  const beats = r.ref.beats;
  const { meter, downs, sections } = structureOf(r);
  const phaseOf = (s: number) => {
    const k = beats.findIndex((b, i) => b <= s && (beats[i + 1] ?? Infinity) > s);
    return k < 0 ? 0 : (s - beats[k]) / ((beats[k + 1] ?? beats[k] + 0.5) - beats[k]);
  };
  const sectionOf = (s: number) => sections.find((x) => s >= x.s0 && s < x.s1)?.label ?? "";
  const caption = (s: number, what = "") => `${t(s)} φ${phaseOf(s).toFixed(2)} ${sectionOf(s)}${what ? ` ${what}` : ""}`;
  const out = (name: string) => {
    const f = join(dir, name);
    written.push(f);
    return f;
  };
  // Section sheets: the 4 longest sections, a bar from each one's middle, beat by phase.
  const chosen = [...sections]
    .sort((a, b) => b.s1 - b.s0 - (a.s1 - a.s0))
    .slice(0, 4)
    .sort((a, b) => a.s0 - b.s0);
  for (const [i, sec] of chosen.entries()) {
    const mid = (sec.s0 + sec.s1) / 2;
    const down = downs.reduce((best, d) => (Math.abs(d - mid) < Math.abs(best - mid) ? d : best), downs[0] ?? mid);
    const bar = barBeats(beats, down, meter);
    if (bar.length < 2) continue;
    const tiles: Buffer[] = [];
    for (let b = 0; b + 1 < bar.length; b++)
      for (const ph of [0, 0.25, 0.5, 0.75]) {
        const s = bar[b] + ph * (bar[b + 1] - bar[b]);
        tiles.push(await tile(await honest.shoot(s), `beat ${b + 1} ${caption(s)}`));
      }
    await sheet(
      tiles,
      4,
      `${r.id} — section sheet ${i + 1}: ${sec.label} (${t(sec.s0)}–${t(sec.s1)}), one bar from ${t(down)}; rows: beats of the bar, columns: phases 0, .25, .5, .75`,
      out(`section-${i + 1}.png`),
    );
  }
  // The phrase sheet: beat 1 of 8 consecutive bars from the loudest section.
  const ticks = r.run.stream.ticks;
  const energyIn = (a: number, b: number) => {
    const xs = ticks.filter((k) => k.t >= a && k.t < b).map((k) => k.energy);
    return xs.length ? xs.reduce((p, q) => p + q, 0) / xs.length : 0;
  };
  const loudest = [...sections].sort((a, b) => energyIn(b.s0, b.s1) - energyIn(a.s0, a.s1))[0];
  const from = downs.findIndex((d) => d >= loudest.s0 - 1e-6);
  const phrase = downs.slice(Math.max(0, from), Math.max(0, from) + 8);
  if (phrase.length) {
    const tiles: Buffer[] = [];
    for (const [i, d] of phrase.entries()) tiles.push(await tile(await honest.shoot(d), `bar ${i + 1} ${caption(d)}`));
    await sheet(tiles, 4, `${r.id} — phrase sheet: beat 1 of ${phrase.length} bars from the loudest section (${loudest.label})`, out("phrase.png"));
  }
  // Moments: the identity, detected and reference section starts, set pieces, the finale (0.25 s after each).
  const plan = r.plans["reactive-moderate"];
  const moments: Moment[] = [];
  if (!play) {
    const firstSection = plan.open.waves.find((w) => w.section !== undefined);
    moments.push({ s: firstSection && firstSection.s < IDENTITY_BY_S ? firstSection.s : IDENTITY_BY_S, label: "identity (stand-in)" });
    for (const w of plan.open.waves) if (w.section !== undefined) moments.push({ s: w.s, label: `detected section ${w.section}` });
  }
  for (const sec of sections.slice(1)) moments.push({ s: sec.s0, label: `reference ${sec.label}` });
  for (const [name, ts] of Object.entries(setPieceTimes(plan))) if (ts.length) moments.push({ s: ts[0], label: name });
  // (Up to 12: the identity and the finale always, then the rest in time order.)
  const inSong = moments.filter((m) => m.s + 0.25 < r.run.stream.audio.duration);
  const must = inSong.filter((m) => m.label === "identity (stand-in)" || m.label === "the finale");
  const kept = [
    ...must,
    ...inSong
      .filter((m) => !must.includes(m))
      .sort((a, b) => a.s - b.s)
      .slice(0, 12 - must.length),
  ].sort((a, b) => a.s - b.s);
  if (kept.length) {
    const tiles: Buffer[] = [];
    for (const m of kept) tiles.push(await tile(await honest.shoot(m.s + 0.25), `${t(m.s + 0.25)} ${m.label}`));
    await sheet(tiles, 4, `${r.id} — moments (0.25 s after each)`, out("moments.png"));
  }
  // Honest against masher: the same 4 moments, side by side (left honest, right mash4).
  {
    const masher = await openGame(base, queryFor("mash4"), { seed: String(FEEL_SEED) });
    sameRun(masher, r.plans.mash4, `${r.id} mash4`, metrics);
    const dur = r.run.stream.audio.duration;
    const at = [0.2, 0.4, 0.6, 0.8].map((f) => beats.reduce((best, b) => (Math.abs(b - f * dur) < Math.abs(best - f * dur) ? b : best), beats[0]) + 0.03);
    const tiles: Buffer[] = [];
    for (const s of at) {
      tiles.push(await tile(await honest.shoot(s), `${play ? "moderate" : "reactive-moderate"} ${caption(s)}`));
      tiles.push(await tile(await masher.shoot(s), `mash4 ${caption(s)}`));
    }
    await sheet(
      tiles,
      2,
      `${r.id} — honest (left) against mash4 (right), damage off: tier ${pct(metrics.reward.honestTier2)} at 2+ vs mash4 ${pct(metrics.reward.mash4Tier0)} at 0`,
      out("honest-vs-masher.png"),
    );
    await masher.close();
  }
  if (!values["no-mix"]) written.push(...(await mix(honest, dir)));
  await Promise.all(checks.splice(0));
  await honest.close();
  return written;
}

/**
 * Do the page's plan and the Node plan the metrics read show the same run? (Their version strings
 * differ, so not by hash: by the counts of swings, presses and enemies.) A mismatch is noted in the
 * metrics and printed.
 */
async function sameRunCheck(game: GamePage, plan: Plan): Promise<string | undefined> {
  const page = await game.page.evaluate(() => {
    const p = (window as unknown as { __demo: { plan: Plan } }).__demo.plan;
    return { swings: p.swings.length, inputs: p.inputs.length, enemies: p.enemies.length };
  });
  const node = { swings: plan.swings.length, inputs: plan.inputs.length, enemies: plan.enemies.length };
  return JSON.stringify(page) === JSON.stringify(node) ? undefined : `page ${JSON.stringify(page)} vs metrics ${JSON.stringify(node)}`;
}
const checks: Promise<void>[] = [];
function sameRun(game: GamePage, plan: Plan, what: string, metrics: TrackMetrics) {
  checks.push(
    sameRunCheck(game, plan).then((d) => {
      if (!d) return;
      metrics.notes.push(`the sheets' ${what} run differs from the metrics' (${d})`);
      console.warn(`warning: the sheets' ${what} run differs from the metrics': ${d}`);
    }),
  );
}

const pct = (x: number | null) => (x === null ? "n/a" : `${(100 * x).toFixed(0)}%`);

/** mix.ogg: the page's offline render of the music with the run's sounds, fetched in chunks and encoded. */
async function mix(game: GamePage, dir: string): Promise<string[]> {
  const info = await game.page.evaluate(async () => {
    const hook = (window as unknown as { __demo: Hook }).__demo;
    if (!hook.renderMix) return undefined;
    const buf = await hook.renderMix({ music: true, sfx: true });
    const n = buf.length;
    const pcm = new Int16Array(n * 2);
    const l = buf.getChannelData(0);
    const r = buf.getChannelData(buf.numberOfChannels > 1 ? 1 : 0);
    for (let i = 0; i < n; i++) {
      pcm[2 * i] = Math.max(-32768, Math.min(32767, Math.round(l[i] * 32767)));
      pcm[2 * i + 1] = Math.max(-32768, Math.min(32767, Math.round(r[i] * 32767)));
    }
    (window as unknown as { __mix: Int16Array }).__mix = pcm;
    return { frames: n, sampleRate: buf.sampleRate };
  });
  if (!info) return [];
  const bytes = Buffer.alloc(info.frames * 4);
  const CHUNK = 1 << 20;
  for (let at = 0; at < info.frames * 2; at += CHUNK) {
    const b64 = await game.page.evaluate(
      ([a, n]) => {
        const p = (window as unknown as { __mix: Int16Array }).__mix.subarray(a, a + n);
        const u8 = new Uint8Array(p.buffer, p.byteOffset, p.byteLength);
        let s = "";
        for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode(...u8.subarray(i, i + 0x8000));
        return btoa(s);
      },
      [at, CHUNK] as const,
    );
    Buffer.from(b64, "base64").copy(bytes, at * 2);
  }
  const header = Buffer.alloc(44);
  header.write("RIFF", 0);
  header.writeUInt32LE(36 + bytes.length, 4);
  header.write("WAVEfmt ", 8);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(2, 22);
  header.writeUInt32LE(info.sampleRate, 24);
  header.writeUInt32LE(info.sampleRate * 4, 28);
  header.writeUInt16LE(4, 32);
  header.writeUInt16LE(16, 34);
  header.write("data", 36);
  header.writeUInt32LE(bytes.length, 40);
  const wav = join(dir, "mix.wav");
  writeFileSync(wav, Buffer.concat([header, bytes]));
  const ogg = join(dir, "mix.ogg");
  try {
    execFileSync("uv", ["run", "--project", join(ROOT, "tools/analysis"), "ps-analysis", "encode", wav, "--out", ogg], { stdio: ["ignore", "ignore", "inherit"] });
  } finally {
    rmSync(wav, { force: true });
  }
  return [ogg];
}

/** metrics.json and metrics.md (after the sheets, whose plan check may add a note). */
function writeMetrics(metrics: TrackMetrics, dir: string) {
  writeFileSync(join(dir, "metrics.json"), `${JSON.stringify(metrics, null, 1)}\n`);
  writeFileSync(join(dir, "metrics.md"), metricsMarkdown(metrics));
}

/** frames.json: the frame series, one array per field. */
function writeFrames(frames: FrameSeries, file: string) {
  const r4 = (x: number) => Math.round(x * 1e4) / 1e4;
  writeFileSync(file, `${JSON.stringify(Object.fromEntries(Object.entries(frames).map(([k, v]) => [k, Array.isArray(v) ? v.map(r4) : v])))}\n`);
}

await withDevServer(async (base) => {
  for (const id of ids) {
    const t0 = performance.now();
    const dir = join(roundDir, id);
    mkdirSync(dir, { recursive: true });
    const r = trackRuns(id, props);
    const frames = sampleFrames(r.plans["reactive-moderate"], r.lib, rate);
    const metrics = trackMetrics(r, frames);
    writeFrames(frames, join(dir, "frames.json"));
    const plan = r.plans["reactive-moderate"];
    const firstSection = plan.open.waves.find((w) => w.section !== undefined);
    await renderTimeline(r, frames, firstSection && firstSection.s < IDENTITY_BY_S ? firstSection.s : IDENTITY_BY_S, join(dir, "timeline.png"));
    const sheets = await dossier(base, r, metrics, dir, false);
    writeMetrics(metrics, dir);
    console.log(
      `${id}: ${["metrics.json", "metrics.md", "frames.json", "timeline.png", ...sheets.map((f) => f.slice(dir.length + 1))].join(", ")} (${((performance.now() - t0) / 1000).toFixed(0)} s)`,
    );
  }
  if (values.anchor) {
    const { anchorRuns } = await import("./lib/feel-anchor.ts");
    const t0 = performance.now();
    const dir = join(roundDir, "anchor-orpheus-galop");
    mkdirSync(dir, { recursive: true });
    const a = anchorRuns(props);
    const frames = sampleFrames(a.runs.plans["reactive-moderate"], a.runs.lib, rate);
    const metrics = a.metrics(frames);
    writeFrames(frames, join(dir, "frames.json"));
    await renderTimeline(a.runs, frames, undefined, join(dir, "timeline.png"));
    const sheets = await dossier(base, a.runs, metrics, dir, true);
    writeMetrics(metrics, dir);
    console.log(`anchor (the authored Galop, play mode): ${sheets.length + 4} files (${((performance.now() - t0) / 1000).toFixed(0)} s)`);
  }
  await closeBrowser();
});
