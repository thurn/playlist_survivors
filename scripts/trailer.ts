/**
 * A gameplay trailer from a spec (trailer/<name>.yaml): films planned runs in headless Chrome as
 * the game draws them (its camera and HUD), one frame per output frame of the edit, and cuts the
 * runs' mixes (music and sound effects) to the same edit.
 *
 *   npm run trailer -- trailer/galop.yaml                  (captures/trailer/galop/galop.mp4, and a 720p copy under 30 MB)
 *   npm run trailer -- trailer/galop.yaml --draft          (30 fps, quicker to encode)
 *   npm run trailer -- trailer/galop.yaml --stills 1.4,12  (frames at output seconds)
 *   npm run trailer -- trailer/galop.yaml --fresh          (films again, ignoring the cache)
 *
 * A spec can film several runs (play mode's, a reactive track's) and cut or crossfade between them.
 * Captured frames and each run's audio are cached in captures/trailer/<name>/cache, keyed by the
 * game's sources (committed and working tree), the run's query and each frame's song time:
 * changing the edit films only the song time it adds, and the mix or the fades film nothing.
 */
import { execFileSync, spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { closeBrowser, openGame, type GamePage } from "./lib/capture.ts";
import { ROOT, readSongMap } from "./lib/files.ts";
import { withDevServer } from "./lib/server.ts";
import sharp from "sharp";
import { Timeline, editAudio, readTrailerSpec, readWav, wavBytes, type Layer, type Stereo } from "./lib/trailer.ts";

const args = process.argv.slice(2);
const arg = (k: string) => {
  const i = args.indexOf(k);
  return i >= 0 ? args[i + 1] : undefined;
};
const specPath = args.find((a) => a.endsWith(".yaml"));
if (!specPath) throw new Error("usage: npm run trailer -- trailer/<name>.yaml [--draft] [--stills t,t] [--fresh] [--out <dir>]");
const spec = readTrailerSpec(resolve(ROOT, specPath));
const name = basename(specPath, ".yaml");
const out = resolve(ROOT, arg("--out") ?? join("captures/trailer", name));
const cache = join(out, "cache");
mkdirSync(join(cache, "frames"), { recursive: true });
const draft = args.includes("--draft");
const fresh = args.includes("--fresh");
const stills = arg("--stills")?.split(",").map(Number);

const timeline = new Timeline(spec, Object.fromEntries(Object.values(spec.runs).flatMap((r) => (r.songMap ? [[r.songMap, readSongMap(r.songMap)]] : []))));
const fps = draft ? 30 : spec.fps;
const frames = Math.round(timeline.duration * fps);

// A run's identity: the game's sources as the dev server serves them (the committed trees, the
// working tree's changes and untracked files) and its query.
const GAME = ["src", "content", "levels", "songs", "public", "index.html", "vite.config.ts", "package-lock.json"];
const git = (...a: string[]) => execFileSync("git", a, { cwd: ROOT, encoding: "utf8", maxBuffer: 1 << 30 });
const sources = [git("rev-parse", ...GAME.map((p) => `HEAD:${p}`)), git("diff", "HEAD", "--binary", "--", ...GAME), git("ls-files", "--others", "--exclude-standard", "-s", "--", ...GAME)];
const hash = (...parts: unknown[]) => createHash("sha1").update(JSON.stringify(parts)).digest("hex").slice(0, 16);
const runKey = (run: string) => hash(sources, spec.runs[run].query, spec.width, spec.height);

console.log(`${spec.title}: ${timeline.duration.toFixed(2)} s, ${frames} frames at ${fps} fps`);
for (const c of timeline.cuts) console.log(`  ${c.t0.toFixed(2).padStart(6)}  ${c.run} ${c.from.toFixed(2)}–${c.to.toFixed(2)}${c.half ? ` (crossfade ${(c.half * 2).toFixed(2)} s)` : ""}`);

const frameFile = (l: Layer) => join(cache, "frames", `${hash(runKey(l.run), l.s.toFixed(5))}.jpg`);
type Want = { t: number; layers: Layer[] };
const want: Want[] = (stills ?? Array.from({ length: frames }, (_, i) => i / fps)).map((t) => ({ t, layers: timeline.layersAt(t) }));
const audioFiles = (run: string) => ({ mix: join(cache, `mix-${runKey(run)}.wav`), music: join(cache, `music-${runKey(run)}.wav`) });

const plans = Object.keys(spec.runs).map((run) => {
  const layers = want.flatMap((w) => w.layers.filter((l) => l.run === run));
  const af = audioFiles(run);
  return {
    run,
    missing: [...new Map(layers.filter((l) => fresh || !existsSync(frameFile(l))).map((l) => [frameFile(l), l])).values()],
    needAudio: !stills && (fresh || !existsSync(af.mix) || !existsSync(af.music)),
    checked: join(cache, `checked-${runKey(run)}`),
  };
});

if (plans.some((p) => p.missing.length > 0 || p.needAudio || !existsSync(p.checked))) {
  await withDevServer(async (base) => {
    for (const p of plans) {
      if (p.missing.length === 0 && !p.needAudio && existsSync(p.checked)) continue;
      const r = spec.runs[p.run];
      console.log(`run ${p.run}: ${r.query}`);
      const game = await openGame(base, r.query, { width: spec.width, height: spec.height });
      const song = await game.page.evaluate(() => (window as unknown as { __demo: { plan: { song: string } } }).__demo.plan.song);
      const track = new URLSearchParams(r.query).get("track");
      if (r.songMap && song !== r.songMap && track !== r.songMap) throw new Error(`run ${p.run}'s song map is ${r.songMap}, but its run (${r.query}) plays ${track ?? song}`);
      writeFileSync(p.checked, song);
      if (p.needAudio) {
        console.log("rendering the run's mix and music");
        const af = audioFiles(p.run);
        writeFileSync(af.mix, wavBytes(await renderMix(game, { music: true, sfx: true })));
        writeFileSync(af.music, wavBytes(await renderMix(game, { music: true, sfx: false })));
      }
      const t0 = performance.now();
      for (const [i, l] of p.missing.entries()) {
        await game.page.evaluate((s) => (window as unknown as { __demo: { renderAt(s: number): Promise<void> } }).__demo.renderAt(s), l.s);
        writeFileSync(frameFile(l), await game.page.locator("canvas").screenshot({ type: "jpeg", quality: 95 }));
        if (i % 60 === 59) console.log(`filmed ${i + 1}/${p.missing.length} (${((performance.now() - t0) / (i + 1)).toFixed(0)} ms a frame)`);
      }
      await game.close();
    }
    await closeBrowser();
  });
}

/** A frame of the edit as JPEG: one run's frame, or a dissolve between two. */
async function picture(w: Want): Promise<Buffer> {
  const [base, ...over] = w.layers;
  if (over.length === 0) return readFileSync(frameFile(base));
  const layers = await Promise.all(over.map(async (l) => ({ input: await sharp(frameFile(l)).ensureAlpha(clamp(l.w)).png().toBuffer() })));
  return sharp(frameFile(base)).composite(layers).jpeg({ quality: 95 }).toBuffer();
}
const clamp = (x: number) => Math.min(1, Math.max(0, x));

if (stills) {
  mkdirSync(join(out, "stills"), { recursive: true });
  for (const w of want) {
    const file = join(out, "stills", `t-${w.t.toFixed(2).padStart(6, "0")}.jpg`);
    writeFileSync(file, await picture(w));
    console.log(`still ${file} (${w.layers.map((l) => `${l.run} ${l.s.toFixed(2)}`).join(" under ")})`);
  }
} else {
  const audioFile = join(out, `${name}.wav`);
  const runAudio = Object.fromEntries(Object.keys(spec.runs).map((run) => [run, { mix: readWav(readFileSync(audioFiles(run).mix)), music: readWav(readFileSync(audioFiles(run).music)) }]));
  writeFileSync(audioFile, wavBytes(editAudio(timeline, runAudio)));
  const video = join(out, `${name}${draft ? "-draft" : ""}.mp4`);
  const fades = [spec.fadeIn > 0 ? `fade=t=in:d=${spec.fadeIn}` : "", spec.fadeOut > 0 ? `fade=t=out:st=${timeline.duration - spec.fadeOut}:d=${spec.fadeOut}` : ""].filter(Boolean);
  const ff = spawn(
    "ffmpeg",
    [
      ...["-y", "-loglevel", "error", "-f", "image2pipe", "-c:v", "mjpeg", "-framerate", String(fps), "-i", "-", "-i", audioFile],
      ...(fades.length ? ["-vf", fades.join(",")] : []),
      ...["-c:v", "libx264", "-preset", draft ? "veryfast" : "slow", "-crf", draft ? "23" : "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "256k", "-shortest", "-movflags", "+faststart", video],
    ],
    { stdio: ["pipe", "inherit", "inherit"] },
  );
  let failed: Error | undefined;
  const done = new Promise<void>((res, rej) => {
    ff.on("error", (e) => rej((failed = new Error(`cannot run ffmpeg: ${e.message}`))));
    ff.on("close", (code) => (code === 0 ? res() : rej((failed = new Error(`ffmpeg exited with ${code}`)))));
  });
  // (A write after ffmpeg has gone fails; the close or error above says why.)
  ff.stdin.on("error", () => undefined);
  for (const w of want) {
    if (failed) break;
    if (!ff.stdin.write(await picture(w)))
      await new Promise<void>((r) => {
        const go = () => (ff.stdin.off("drain", go), ff.off("close", go), r());
        ff.stdin.once("drain", go);
        ff.once("close", go);
      });
  }
  ff.stdin.end();
  await done;
  console.log(`wrote ${video}`);
  if (!draft) {
    // A copy small enough to share: 720p at about 6 Mbit/s (about 23 MB for 30 s).
    const share = join(out, `${name}-720p.mp4`);
    execFileSync("ffmpeg", ["-y", "-loglevel", "error", "-i", video, "-vf", "scale=1280:720:flags=lanczos", "-c:v", "libx264", "-preset", "slow", "-b:v", "6M", "-maxrate", "7M", "-bufsize", "14M", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", share]);
    console.log(`wrote ${share}`);
  }
}

/** The run's audio rendered offline through the game's mixing graph, fetched in chunks. */
async function renderMix(game: GamePage, parts: { music: boolean; sfx: boolean }): Promise<Stereo> {
  await game.page.waitForFunction(() => (window as unknown as { __demo: { renderMix?: unknown } }).__demo.renderMix !== undefined);
  const info = await game.page.evaluate(async (p) => {
    const w = window as unknown as { __demo: { renderMix(p: unknown): Promise<AudioBuffer> }; __mix?: Float32Array };
    const b = await w.__demo.renderMix(p);
    const l = b.getChannelData(0);
    const r = b.numberOfChannels > 1 ? b.getChannelData(1) : l;
    const data = new Float32Array(b.length * 2);
    for (let i = 0; i < b.length; i++) {
      data[i * 2] = l[i];
      data[i * 2 + 1] = r[i];
    }
    w.__mix = data;
    return { sampleRate: b.sampleRate, length: data.length };
  }, parts);
  const data = new Float32Array(info.length);
  const CHUNK = 1 << 20;
  for (let i = 0; i < info.length; i += CHUNK) {
    const b64 = await game.page.evaluate(
      ([from, n]) => {
        const d = (window as unknown as { __mix: Float32Array }).__mix.subarray(from, from + n);
        const bytes = new Uint8Array(d.buffer, d.byteOffset, d.byteLength);
        let s = "";
        for (let k = 0; k < bytes.length; k += 0x8000) s += String.fromCharCode(...bytes.subarray(k, k + 0x8000));
        return btoa(s);
      },
      [i, CHUNK] as const,
    );
    const buf = new Uint8Array(Buffer.from(b64, "base64"));
    data.set(new Float32Array(buf.buffer), i);
  }
  return { sampleRate: info.sampleRate, data };
}
