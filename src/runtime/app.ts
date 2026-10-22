/**
 * The demo app: loads everything, plans (or loads a cached plan), shows the
 * title screen, then draws evaluatePlan(plan, s) every animation frame.
 */
import { Application } from "pixi.js";
import { ContentError, levelSongName, loadContent, loadLevel, type ContentLibrary } from "../content/loader.ts";
import { Evaluator } from "../plan/evaluate.ts";
import type { Plan } from "../plan/types.ts";
import type { PlannerInput } from "../planner/context.ts";
import { checkPlan } from "../planner/checks.ts";
import { planHash } from "../planner/plan.ts";
import { loadGameAssets, type GameAssets } from "./assets.ts";
import { AudioEngine, VirtualClock, type SongClock } from "./audio.ts";
import { Hud } from "./hud.ts";
import { cacheKey, loadCachedPlan, storePlan } from "./planCache.ts";
import { Renderer } from "./renderer.ts";
import { runLive } from "./play.ts";
import { calibrate, loadBestGrade, titleScreen } from "./title.ts";
import { contentFiles, landmarks, levelFile, levelResolver, songMap } from "./sources.ts";

export interface Params {
  metronome: boolean;
  capture: boolean;
  captureT?: number;
  avOffsetMs: number;
  /** Song time in seconds to start playback from (`?start=95` or `?start=1:35`). */
  startS: number;
  seed?: number;
  /** The virtual player's skill profile (`?player=casual`). */
  player?: "perfect" | "skilled" | "casual" | "moderate";
  test: boolean;
  bench: boolean;
  checks: boolean;
  /** Test hook: a level script name (levels/ or the broken fixtures). */
  level?: string;
  /** `?mode=play` plays the real game (the play level file); `?mode=demo` the scripted demo. */
  mode?: "play" | "demo";
}

/** The level a mode plays. */
export const PLAY_LEVEL = "orpheus-galop.play";

export function readParams(search: string): Params {
  const q = new URLSearchParams(search);
  const num = (k: string) => (q.has(k) && q.get(k) !== "" ? Number(q.get(k)) : undefined);
  return {
    metronome: q.has("metronome"),
    capture: q.has("capture"),
    captureT: num("t"),
    avOffsetMs: num("avOffsetMs") ?? 0,
    startS: parseTimestamp(q.get("start")),
    seed: num("seed"),
    player: (["perfect", "skilled", "casual", "moderate"] as const).find((p) => p === q.get("player")),
    test: q.has("test") || q.has("capture") || q.has("bench"),
    bench: q.has("bench"),
    checks: q.has("checks"),
    level: q.get("level") ?? undefined,
    mode: (["play", "demo"] as const).find((m) => m === q.get("mode")),
  };
}

/** Seconds from "95.5", "1:35" or "1:02:03"; 0 when missing or malformed. */
export function parseTimestamp(v: string | null): number {
  if (!v) return 0;
  const parts = v.split(":").map(Number);
  if (parts.length > 3 || parts.some((n) => !Number.isFinite(n) || n < 0)) return 0;
  return parts.reduce((acc, n) => acc * 60 + n, 0);
}

export interface Overlay {
  root: HTMLDivElement;
  sub: HTMLDivElement;
  status: HTMLDivElement;
  bar: HTMLDivElement;
  prompt: HTMLDivElement;
  error: HTMLPreElement;
}

function makeOverlay(): Overlay {
  const root = document.createElement("div");
  root.style.cssText =
    "position:fixed;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:18px;background:#07060b;color:#e8e0ff;font:16px/1.4 ui-monospace,Menlo,monospace;z-index:10;text-align:center;padding:16px;";
  const title = document.createElement("div");
  title.textContent = "PLAYLIST SURVIVORS";
  title.style.cssText = "font-size:clamp(28px,6vw,64px);letter-spacing:0.12em;color:#ffe080;text-shadow:0 0 24px #ff6a3c88;";
  const sub = document.createElement("div");
  sub.textContent = "a tech demo";
  sub.style.cssText = "color:#a8a0c8;";
  const barWrap = document.createElement("div");
  barWrap.style.cssText = "width:min(420px,80vw);height:6px;background:#1c1830;border-radius:3px;overflow:hidden;";
  const bar = document.createElement("div");
  bar.style.cssText = "height:100%;width:0;background:#60ffc0;transition:width .15s;";
  barWrap.appendChild(bar);
  const status = document.createElement("div");
  status.style.cssText = "color:#8880a8;font-size:13px;";
  const prompt = document.createElement("div");
  prompt.textContent = "Click to begin";
  prompt.style.cssText = "display:none;font-size:22px;color:#fff;padding:10px 26px;border:1px solid #ffe08066;border-radius:6px;cursor:pointer;animation:pulse 0.65s ease-in-out infinite alternate;";
  const style = document.createElement("style");
  style.textContent = "@keyframes pulse{from{opacity:.55}to{opacity:1}}";
  const error = document.createElement("pre");
  error.style.cssText = "display:none;max-width:min(900px,92vw);white-space:pre-wrap;text-align:left;color:#ff9090;background:#1a0a10;border:1px solid #ff606066;padding:12px 16px;border-radius:6px;font-size:13px;";
  root.append(style, title, sub, barWrap, status, prompt, error);
  document.body.appendChild(root);
  return { root, sub, status, bar, prompt, error };
}

/** The title screen's prompt: one click (or key) unlocks audio; then the overlay fades out. */
export async function clickToBegin(overlay: Overlay): Promise<void> {
  overlay.prompt.style.display = "block";
  await new Promise<void>((resolve) => {
    const go = () => {
      window.removeEventListener("pointerdown", go);
      window.removeEventListener("keydown", go);
      resolve();
    };
    window.addEventListener("pointerdown", go);
    window.addEventListener("keydown", go);
  });
  overlay.root.style.transition = "opacity .6s";
  overlay.root.style.opacity = "0";
  setTimeout(() => overlay.root.remove(), 700);
}

export interface DemoHook {
  ready: Promise<void>;
  renderAt(s: number): Promise<void>;
  stats(): { frameMs: number[]; alive: number; particles: number };
  plan?: Plan;
  planHash?: string;
  planSource?: "cache" | "planner";
  planMs?: number;
  checks?: () => { name: string; failures: string[] }[];
  bench?: Promise<BenchResult>;
  /** QA: the song time of the last drawn frame, and audio state. */
  lastS?: number;
  frames?: number;
  audio?: () => { state: string; contextSongTime: number; stats: { scheduled: number; skippedLate: number; startedLate: number; clicks: number; ducks: number; ticks: number } };
  /** Play mode's flow: its phase, the menu on screen, pausing, and choosing a menu item by its label. */
  play?: { phase(): string; pause(): void; menu(): { items: string[]; selected: number } | undefined; choose(label: string): void; compareReplay(): string[] };
  /** Play mode: the hero where the last frame drew it. */
  lastHero?: { s: number; x: number; y: number };
  /** Play mode: the live runner and recording, and a way to press keys at exact song times. */
  live?: { runner: import("../live/runner.ts").LiveRunner; recording: import("../live/recording.ts").LiveRecording; key(e: import("../sim/player.ts").KeyEvent): void };
  /** QA listening pass: the level's mix rendered offline through the live mixing graph. */
  renderMix?: (parts: { music: boolean; sfx: boolean }) => Promise<AudioBuffer>;
}

export interface BenchResult {
  frames: number;
  jsMs: number[];
  gpuMs: number[];
  intervalMs: number[];
}

export async function runApp(container: HTMLElement, params: Params): Promise<void> {
  const overlay = makeOverlay();
  const progress = (f: number, msg: string) => {
    overlay.bar.style.width = `${Math.round(f * 100)}%`;
    overlay.status.textContent = msg;
  };
  const fail = (msg: string) => {
    overlay.error.style.display = "block";
    overlay.error.textContent = msg;
    overlay.prompt.style.display = "none";
    overlay.status.textContent = "Cannot start: the level failed to load.";
    overlay.root.dataset.state = "error";
  };

  let readyResolve!: () => void;
  let readyReject!: (e: unknown) => void;
  const ready = new Promise<void>((res, rej) => {
    readyResolve = res;
    readyReject = rej;
  });
  ready.catch(() => undefined);
  const frameMs: number[] = [];
  const hook: DemoHook = { ready, renderAt: async () => undefined, stats: () => ({ frameMs, alive: 0, particles: 0 }) };
  if (params.test) (window as unknown as { __demo: DemoHook }).__demo = hook;

  try {
    // 1. Content and the song map. ?player=, ?capture, ?bench (and the test hooks ?test, ?level=
    // and ?metronome) imply the demo, so existing tools and URLs keep working; otherwise the title
    // screen chooses, unless ?mode= has.
    progress(0.02, "Loading content");
    let mode = params.mode ?? (params.player || params.test || params.level !== undefined || params.metronome ? "demo" : undefined);
    const levelOf = (m: "play" | "demo" | undefined) => levelFile(params.level ?? (m === "play" ? PLAY_LEVEL : undefined));
    const loadFor = (m: "play" | "demo" | undefined): PlannerInput => {
      try {
        const lf = levelOf(m);
        const sm = songMap(levelSongName(lf, levelResolver));
        const level = loadLevel(lf, lib, sm, levelResolver);
        overlay.sub.textContent = m === "demo" ? `${level.title.piece} — a tech demo` : level.title.piece;
        return { songMap: sm, level, content: lib, seed: params.seed ?? level.seed, profile: params.player ?? level.player, landmarks: landmarks(), version: __BUILD_VERSION__ };
      } catch (e) {
        throw e instanceof ContentError ? new Error(`Level script or content error:\n${e.message}`) : e;
      }
    };
    let lib: ContentLibrary;
    try {
      lib = loadContent(contentFiles());
    } catch (e) {
      throw e instanceof ContentError ? new Error(`Level script or content error:\n${e.message}`) : e;
    }
    // A chosen mode loads its level now, so a broken one fails before anything else loads.
    let input = loadFor(mode);

    // 2. Renderer and assets.
    const app = new Application();
    await app.init({ resizeTo: window, background: "#07060b", antialias: false, autoStart: false, preference: "webgl", resolution: 1, powerPreference: "high-performance" });
    container.appendChild(app.canvas);
    app.canvas.style.imageRendering = "pixelated";
    const assets: GameAssets = await loadGameAssets((f) => progress(0.05 + f * 0.3, "Loading art"));
    const heroAnims = Object.fromEntries([...assets.chars.get("hero")!.entries()].map(([k, a]) => [k, { frames: a.count }]));
    // Impact frames come from the atlas manifest.
    const charsJson = await (await fetch(`${import.meta.env.BASE_URL}assets/characters.json`)).json();
    for (const [k, a] of Object.entries(charsJson.sprites.hero.animations as Record<string, { impactFrame?: number }>)) if (heroAnims[k]) (heroAnims[k] as { frames: number; impactFrame?: number }).impactFrame = a.impactFrame;
    input.heroAnims = heroAnims;
    input.soundOnsets = Object.fromEntries(Object.entries(assets.sounds.sounds).map(([k, v]) => [k, v.onsetS ?? 0]));
    const props = Object.fromEntries([...assets.props.entries()].map(([k, v]) => [k, { tall: v.tall, radius: 10 }]));
    const worldJson = await (await fetch(`${import.meta.env.BASE_URL}assets/world.json`)).json();
    for (const [k, v] of Object.entries(worldJson.props as Record<string, { radius: number }>)) if (props[k]) props[k].radius = v.radius;

    // 3. Audio.
    const audio = params.capture || params.bench ? undefined : new AudioEngine(params.avOffsetMs, params.metronome);
    if (audio) {
      progress(0.4, "Loading music");
      await audio.loadMusic(`${import.meta.env.BASE_URL}audio/${input.songMap.audio.file}`, (f) => progress(0.4 + f * 0.25, "Loading music"));
    }

    // The title screen: Play, Watch the demo, or Calibrate (then the title again).
    let begun = false;
    while (!mode) {
      progress(1, "");
      overlay.root.dataset.state = "title";
      const choice = await titleScreen(overlay, loadBestGrade(PLAY_LEVEL));
      if (choice === "calibrate") {
        await calibrate(overlay, audio!);
        continue;
      }
      mode = choice;
      // The choice was a click or a key: it unlocks audio, so play starts without another.
      begun = true;
      const chosen = loadFor(mode);
      input = { ...chosen, heroAnims: input.heroAnims, soundOnsets: input.soundOnsets };
    }

    // Play mode: the real game, live (`?mode=play&player=` plays back a precomputed run instead).
    if (mode === "play" && !params.player) {
      await runLive({ app, assets, input, props, audio, overlay, progress, hook, params, frameMs, ready: readyResolve, begun });
      return;
    }

    // 4. Plan (cached by input hash and build version).
    progress(0.68, "Planning the choreography");
    const key = await cacheKey([input, props]);
    const tPlan = performance.now();
    let plan = await loadCachedPlan(key);
    hook.planSource = plan ? "cache" : "planner";
    if (!plan) {
      plan = await new Promise<Plan>((resolve, reject) => {
        const worker = new Worker(new URL("./planWorker.ts", import.meta.url), { type: "module" });
        worker.onmessage = (e: MessageEvent<{ ok: boolean; plan?: Plan; error?: string }>) => {
          worker.terminate();
          if (e.data.ok) resolve(e.data.plan!);
          else reject(new Error(`Planner error: ${e.data.error}`));
        };
        worker.onerror = (e) => reject(new Error(`Planner crashed: ${e.message}`));
        worker.postMessage({ input, props });
      });
      await storePlan(key, plan);
    }
    hook.plan = plan;
    hook.planMs = performance.now() - tPlan;
    hook.planHash = planHash(plan);
    hook.checks = () => checkPlan(plan!, lib);
    if (audio) {
      progress(0.9, "Loading sounds");
      await audio.loadSounds(assets.sounds, new Set(plan.events.sounds.map((e) => e.sample)), `${import.meta.env.BASE_URL}assets/sfx/`);
      const p = plan;
      hook.renderMix = (parts) => audio.renderOffline(p, parts);
    }

    const evaluator = new Evaluator(plan, lib);
    const renderer = new Renderer(app, assets, plan, lib);
    const hud = new Hud(plan, lib, assets.credits);
    app.stage.addChild(renderer.screen, hud.root);

    const drawAt = (s: number) => {
      const t0 = performance.now();
      const frame = evaluator.evaluate(s, { metronome: params.metronome });
      renderer.draw(frame);
      hud.draw(frame, renderer.outputRect(), { metronome: params.metronome });
      app.renderer.render(app.stage);
      const dt = performance.now() - t0;
      frameMs.push(dt);
      if (frameMs.length > 20000) frameMs.splice(0, 10000);
      lastStats = { alive: frame.stats.alive, particles: frame.stats.particles };
      return frame;
    };
    let lastStats = { alive: 0, particles: 0 };
    hook.stats = () => ({ frameMs, ...lastStats });

    // Test hook: a virtual clock driven by the test driver.
    const virtual = new VirtualClock();
    hook.renderAt = async (s: number) => {
      virtual.s = s;
      drawAt(s);
      await new Promise((r) => requestAnimationFrame(() => r(undefined)));
    };

    progress(1, "Ready");
    overlay.root.dataset.state = "ready";
    if (params.capture) {
      overlay.root.remove();
      drawAt(params.captureT ?? 0);
      readyResolve();
      return;
    }
    if (params.bench) {
      overlay.root.remove();
      readyResolve();
      hook.bench = runBench(0, plan.duration, drawAt, app);
      return;
    }
    readyResolve();

    // Title screen: one click unlocks audio.
    overlay.status.textContent = params.metronome ? "Metronome mode: beat flash and click track" : "Headphones recommended";
    if (params.startS > 0) overlay.status.textContent += ` · starting at ${params.startS.toFixed(1)} s`;
    if (params.player) overlay.status.textContent += ` · ${params.player} player`;
    if (begun) overlay.root.remove();
    else await clickToBegin(overlay);
    await audio!.start(plan, params.startS);
    hook.audio = () => ({ state: audio!.ctx.state, contextSongTime: audio!.contextSongTime(), stats: { ...audio!.stats } });
    const clock: SongClock = audio!;
    // The display's refresh period: a median of recent frame intervals, so a single long
    // frame (or a return from a hidden tab) does not shift the picture ahead of the audio.
    let lastTs = 0;
    const intervals: number[] = [];
    let frameDur = 1000 / 60;
    const loop = (ts: number) => {
      if (lastTs > 0) {
        const dt = ts - lastTs;
        if (dt > 2 && dt < 100) {
          intervals.push(dt);
          if (intervals.length > 31) intervals.shift();
          frameDur = [...intervals].sort((a, b) => a - b)[intervals.length >> 1];
        }
      }
      lastTs = ts;
      // Song time keeps running after the music ends, so the credits roll on.
      const s = clock.songTimeAtPresentation(ts, frameDur);
      drawAt(s);
      hook.lastS = s;
      hook.frames = (hook.frames ?? 0) + 1;
      requestAnimationFrame(loop);
    };
    requestAnimationFrame(loop);
  } catch (e) {
    console.error(e);
    fail(e instanceof Error ? e.message : String(e));
    readyReject(e);
  }
}

/** Plays song time `fromS` to `toS` on a virtual clock at real-time speed and records frame times. */
export async function runBench(fromS: number, toS: number, drawAt: (s: number) => unknown, app: Application): Promise<BenchResult> {
  const gl = (app.renderer as unknown as { gl?: WebGL2RenderingContext }).gl;
  const ext = gl?.getExtension("EXT_disjoint_timer_query_webgl2") as { TIME_ELAPSED_EXT: number; GPU_DISJOINT_EXT: number } | null;
  const jsMs: number[] = [];
  const gpuMs: number[] = [];
  const intervalMs: number[] = [];
  const pending: WebGLQuery[] = [];
  return new Promise((resolve) => {
    let start = -1;
    let last = -1;
    const frame = (ts: number) => {
      if (start < 0) start = ts;
      if (last >= 0) intervalMs.push(ts - last);
      last = ts;
      const s = fromS + (ts - start) / 1000;
      let q: WebGLQuery | null = null;
      if (gl && ext) {
        q = gl.createQuery();
        if (q) gl.beginQuery(ext.TIME_ELAPSED_EXT, q);
      }
      performance.mark("frame-start");
      drawAt(Math.min(s, toS));
      performance.mark("frame-end");
      jsMs.push(performance.measure("frame", "frame-start", "frame-end").duration);
      if (gl && ext && q) {
        gl.endQuery(ext.TIME_ELAPSED_EXT);
        pending.push(q);
      }
      while (gl && ext && pending.length > 0 && gl.getQueryParameter(pending[0], gl.QUERY_RESULT_AVAILABLE)) {
        const done = pending.shift()!;
        if (!gl.getParameter(ext.GPU_DISJOINT_EXT)) gpuMs.push(gl.getQueryParameter(done, gl.QUERY_RESULT) / 1e6);
        gl.deleteQuery(done);
      }
      if (s < toS) requestAnimationFrame(frame);
      else resolve({ frames: jsMs.length, jsMs, gpuMs, intervalMs });
    };
    requestAnimationFrame(frame);
  });
}
