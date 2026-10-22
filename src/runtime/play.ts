/**
 * Play mode in the browser: the real game, live. The keyboard drives the
 * simulation through the live runner; the evaluator, renderer and HUD draw
 * the live recording like a finished plan, and the audio engine schedules its
 * sounds as they become known (and cancels the ones a rollback took back).
 */
import type { Application } from "pixi.js";
import type { ContentLibrary } from "../content/loader.ts";
import { indexSong, timeOfBeatIndex } from "../core/songmap.ts";
import { KeyboardInput, loadInputOffset } from "../live/input.ts";
import { LiveRunner } from "../live/runner.ts";
import { handOver, liveSim } from "../live/session.ts";
import type { PropInfo } from "../mapgen/mapgen.ts";
import { Evaluator } from "../plan/evaluate.ts";
import type { PlannerInput } from "../planner/context.ts";
import type { Key } from "../sim/movement.ts";
import type { KeyEvent } from "../sim/player.ts";
import { clickToBegin, runBench, type DemoHook, type Overlay, type Params } from "./app.ts";
import type { GameAssets } from "./assets.ts";
import type { AudioEngine } from "./audio.ts";
import { Hud } from "./hud.ts";
import { Renderer } from "./renderer.ts";

export interface LiveEnv {
  app: Application;
  assets: GameAssets;
  input: PlannerInput;
  props: Record<string, PropInfo>;
  audio?: AudioEngine;
  overlay: Overlay;
  progress: (f: number, msg: string) => void;
  hook: DemoHook;
  params: Params;
  frameMs: number[];
  /** The page is ready (for tests): called before waiting for the first click. */
  ready: () => void;
}

const yieldToUi = () => new Promise((r) => setTimeout(r, 0));

/** Every sample a sound palette can play: a live recording's sounds are not known in advance. */
export function paletteSamples(lib: ContentLibrary): Set<string> {
  const out = new Set<string>();
  for (const pal of lib.soundPalettes.values()) for (const list of Object.values(pal.sounds)) for (const x of list) out.add(typeof x === "string" ? x : x.id);
  return out;
}

const fmtTime = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;

export async function runLive(env: LiveEnv): Promise<void> {
  const { app, assets, input, props, audio, overlay, progress, hook, params } = env;
  const startS = params.bench ? benchFrom(input) : params.startS;

  // The simulation; a mid-song start fast-forwards with the level's virtual player, then hands over.
  progress(0.7, startS > 0 ? `Fast-forwarding to ${fmtTime(startS)}` : "Preparing the field");
  const { ctx, recording, sim } = liveSim(input, props, startS > 0);
  for (let t = 0; t < startS; ) {
    t = Math.min(startS, t + 5);
    sim.advanceTo(t);
    progress(0.7 + (0.2 * t) / startS, `Fast-forwarding to ${fmtTime(startS)}`);
    await yieldToUi();
  }
  if (startS > 0) handOver(sim);
  const runner = new LiveRunner(sim, recording);
  const lib = ctx.lib;
  if (audio) {
    progress(0.92, "Loading sounds");
    await audio.loadSounds(assets.sounds, paletteSamples(lib), `${import.meta.env.BASE_URL}assets/sfx/`);
  }

  const evaluator = new Evaluator(recording.plan, lib, recording);
  const renderer = new Renderer(app, assets, recording.plan, lib);
  const hud = new Hud(recording.plan, lib, assets.credits);
  app.stage.addChild(renderer.screen, hud.root);
  let lastStats = { alive: 0, particles: 0 };
  hook.stats = () => ({ frameMs: env.frameMs, ...lastStats });
  const drawAt = (s: number) => {
    recording.refresh();
    const frame = evaluator.evaluate(s);
    renderer.draw(frame);
    hud.draw(frame, renderer.outputRect(), { metronome: false });
    app.renderer.render(app.stage);
    lastStats = { alive: frame.stats.alive, particles: frame.stats.particles };
    return frame;
  };
  // Keys from tests and tools, at exact song times (the next frame applies them).
  const injected: KeyEvent[] = [];
  hook.live = { runner, recording, key: (e) => injected.push(e) };
  hook.plan = recording.plan;
  /** One live frame at song time s: apply the keys that have arrived, run ahead, draw. */
  const liveFrame = (s: number, keys: KeyEvent[]) => {
    const t0 = performance.now();
    keys.push(...injected.splice(0));
    runner.frame(s, keys);
    audio?.scheduleLive();
    drawAt(s);
    env.frameMs.push(performance.now() - t0);
    if (env.frameMs.length > 20000) env.frameMs.splice(0, 10000);
  };
  // Test hook: a virtual clock driven by the test driver (it must only move forward).
  hook.renderAt = async (s: number) => {
    liveFrame(s, []);
    await new Promise((r) => requestAnimationFrame(() => r(undefined)));
  };
  progress(1, "Ready");
  overlay.root.dataset.state = "ready";

  if (params.capture) {
    overlay.root.remove();
    liveFrame(Math.max(startS, params.captureT ?? startS), []);
    env.ready();
    return;
  }
  if (params.bench) {
    // The play-mode benchmark: theme A to the climax with a press on every beat, each arriving a
    // little late (so most of them roll the simulation back).
    overlay.root.remove();
    env.ready();
    const beats = recording.plan.songBeats;
    const toS = ctx.sections.find((x) => x.label === "climax")?.s0 ?? ctx.duration;
    const dirs: Key[] = ["right", "down", "left", "up"];
    const presses: KeyEvent[] = [];
    for (let i = 0; i < beats.length; i++) {
      if (beats[i] <= startS + 0.5 || beats[i] >= toS) continue;
      const key = dirs[i % 4];
      presses.push({ s: beats[i] + 0.004, kind: "press", key }, { s: beats[i] + 0.064, kind: "release", key });
    }
    let next = 0;
    hook.bench = runBench(startS, toS, (s) => {
      const due: KeyEvent[] = [];
      while (next < presses.length && presses[next].s + 0.012 <= s) due.push(presses[next++]);
      liveFrame(s, due);
    }, app);
    return;
  }
  env.ready();

  overlay.status.textContent = "Arrows or WASD to move · headphones recommended";
  if (startS > 0) overlay.status.textContent += ` · starting at ${fmtTime(startS)}`;
  await clickToBegin(overlay);
  await audio!.startLive(() => recording.plan.events.sounds, startS);
  hook.audio = () => ({ state: audio!.ctx.state, contextSongTime: audio!.contextSongTime(), stats: { ...audio!.stats } });
  const keyboard = new KeyboardInput((perfMs) => audio!.songTimeAt(perfMs), loadInputOffset);
  keyboard.attach();
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
    const s = audio!.songTimeAtPresentation(ts, frameDur);
    liveFrame(s, keyboard.drain());
    hook.lastS = s;
    hook.frames = (hook.frames ?? 0) + 1;
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}

/** The play-mode benchmark starts at theme A. */
function benchFrom(input: PlannerInput): number {
  const sm = input.songMap;
  const sec = sm.sections.find((x) => x.label === "theme-a");
  return sec ? timeOfBeatIndex(sm, indexSong(sm).barStart[sec.bars[0]]) : 0;
}
