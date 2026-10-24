/**
 * A gameplay trailer from a spec (trailer/<name>.yaml): films a planned run in headless Chrome
 * as the game draws it (its camera and HUD), one frame per output frame of the edit, and cuts the
 * run's mix (music and sound effects) to the same edit.
 *
 *   npm run trailer -- trailer/galop.yaml                  (captures/trailer/galop/galop.mp4, and a 720p copy under 30 MB)
 *   npm run trailer -- trailer/galop.yaml --draft          (30 fps, quicker to encode)
 *   npm run trailer -- trailer/galop.yaml --stills 1.4,12  (frames at output seconds)
 *   npm run trailer -- trailer/galop.yaml --fresh          (films again, ignoring the cache)
 *
 * Captured frames and the run's audio are cached in captures/trailer/<name>/cache, keyed by the
 * game's sources (committed and working tree), the query and each frame's song time: changing the
 * edit films only the song time it adds, and the mix or the fades film nothing.
 */
import { execFileSync, spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { closeBrowser, openGame, type GamePage } from "./lib/capture.ts";
import { ROOT, readSongMap } from "./lib/files.ts";
import { withDevServer } from "./lib/server.ts";
import { Timeline, editAudio, readTrailerSpec, readWav, wavBytes, type Stereo } from "./lib/trailer.ts";

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

const timeline = new Timeline(spec, readSongMap(spec.song));
const fps = draft ? 30 : spec.fps;
const frames = Math.round(timeline.duration * fps);

// The run's identity: the game's sources as the dev server serves them (the committed trees, the
// working tree's changes and untracked files) and the query.
const GAME = ["src", "content", "levels", "songs", "public", "index.html", "vite.config.ts", "package-lock.json"];
const git = (...a: string[]) => execFileSync("git", a, { cwd: ROOT, encoding: "utf8", maxBuffer: 1 << 30 });
const sources = [git("rev-parse", ...GAME.map((p) => `HEAD:${p}`)), git("diff", "HEAD", "--binary", "--", ...GAME), git("ls-files", "--others", "--exclude-standard", "-s", "--", ...GAME)];
const hash = (...parts: unknown[]) => createHash("sha1").update(JSON.stringify(parts)).digest("hex").slice(0, 16);
const runKey = hash(sources, spec.query, spec.width, spec.height);

console.log(`${spec.title}: ${timeline.duration.toFixed(2)} s, ${frames} frames at ${fps} fps`);
for (const c of timeline.cuts) console.log(`  ${c.t0.toFixed(2).padStart(6)}  song ${c.from.toFixed(2)}–${c.to.toFixed(2)}`);

type Want = { t: number; s: number; file: string };
const want: Want[] = (stills ?? Array.from({ length: frames }, (_, i) => i / fps)).map((t) => {
  const s = timeline.songAt(t);
  return { t, s, file: join(cache, "frames", `${hash(runKey, s.toFixed(5))}.jpg`) };
});
const songFile = join(cache, `song-${runKey}.txt`);
const mixFile = join(cache, `mix-${runKey}.wav`);
const musicFile = join(cache, `music-${runKey}.wav`);
const missing = want.filter((w) => fresh || !existsSync(w.file));
const needAudio = !stills && (fresh || !existsSync(mixFile) || !existsSync(musicFile));

if (missing.length > 0 || needAudio || !existsSync(songFile)) {
  await withDevServer(async (base) => {
    const game = await openGame(base, spec.query, { width: spec.width, height: spec.height });
    const song = await game.page.evaluate(() => (window as unknown as { __demo: { plan: { song: string } } }).__demo.plan.song);
    if (song !== spec.song) throw new Error(`the spec's song is ${spec.song}, but the run filmed (${spec.query}) plays ${song}`);
    writeFileSync(songFile, song);
    if (needAudio) {
      console.log("rendering the run's mix and music");
      writeFileSync(mixFile, wavBytes(await renderMix(game, { music: true, sfx: true })));
      writeFileSync(musicFile, wavBytes(await renderMix(game, { music: true, sfx: false })));
    }
    const t0 = performance.now();
    for (const [i, w] of missing.entries()) {
      await game.page.evaluate((s) => (window as unknown as { __demo: { renderAt(s: number): Promise<void> } }).__demo.renderAt(s), w.s);
      writeFileSync(w.file, await game.page.locator("canvas").screenshot({ type: "jpeg", quality: 95 }));
      if (i % 60 === 59) console.log(`filmed ${i + 1}/${missing.length} (${((performance.now() - t0) / (i + 1)).toFixed(0)} ms a frame)`);
    }
    await closeBrowser();
  });
}

if (stills) {
  mkdirSync(join(out, "stills"), { recursive: true });
  for (const w of want) {
    const file = join(out, "stills", `t-${w.t.toFixed(2).padStart(6, "0")}.jpg`);
    copyFileSync(w.file, file);
    console.log(`still ${file} (song ${w.s.toFixed(2)})`);
  }
} else {
  const audioFile = join(out, `${name}.wav`);
  writeFileSync(audioFile, wavBytes(editAudio(timeline, readWav(readFileSync(mixFile)), readWav(readFileSync(musicFile)))));
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
    if (!ff.stdin.write(readFileSync(w.file)))
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
