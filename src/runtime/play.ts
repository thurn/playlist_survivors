/**
 * Play mode in the browser: the real game, live. The keyboard drives the
 * simulation through the live runner; the evaluator, renderer and HUD draw
 * the live recording like a finished plan, and the audio engine schedules its
 * sounds as they become known (and cancels the ones a rollback took back).
 *
 * The flow around it: a one-bar count-in before a mid-song start and after
 * each resume; Esc (or losing focus) pauses, with Resume, Restart section,
 * Restart song and Quit to title; the end card's grade and menu; and Watch my
 * run, which replays the recorded key events through the engine.
 */
import type { Application } from "pixi.js";
import type { ContentLibrary } from "../content/loader.ts";
import { indexSong, timeOfBeatIndex } from "../core/songmap.ts";
import { KeyboardInput, keyOf, loadInputOffset } from "../live/input.ts";
import { PlayLog } from "../live/log.ts";
import type { LiveRecording } from "../live/recording.ts";
import { LiveRunner } from "../live/runner.ts";
import { handOver, liveSim } from "../live/session.ts";
import type { PropInfo } from "../mapgen/mapgen.ts";
import { Evaluator, type Frame } from "../plan/evaluate.ts";
import { gradeFor, gradeRank, type Grade } from "../plan/grade.ts";
import { beatIndexIn } from "../plan/view.ts";
import type { PlannerInput } from "../planner/context.ts";
import type { Key } from "../sim/movement.ts";
import type { KeyEvent } from "../sim/player.ts";
import { clickToBegin, PLAY_LEVEL, runBench, type DemoHook, type Overlay, type Params } from "./app.ts";
import type { GameAssets } from "./assets.ts";
import type { AudioEngine } from "./audio.ts";
import { DebugMovesPanel, movesReadout } from "./debug-moves.ts";
import { Hud, type EndCardInfo, type HudMenu, type HudOptions } from "./hud.ts";
import { Renderer } from "./renderer.ts";
import { loadBestGrade, saveBestGrade } from "./title.ts";

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
  /** The title screen's choice already unlocked audio: no "Click to begin". */
  begun?: boolean;
}

const yieldToUi = () => new Promise((r) => setTimeout(r, 0));

/** Every sample a sound palette can play: a live recording's sounds are not known in advance. */
export function paletteSamples(lib: ContentLibrary): Set<string> {
  const out = new Set<string>();
  for (const pal of lib.soundPalettes.values()) for (const list of Object.values(pal.sounds)) for (const x of list) out.add(typeof x === "string" ? x : x.id);
  return out;
}

const fmtTime = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}`;

/** A recording on screen: its evaluator, renderer and HUD. */
interface Stage {
  recording: LiveRecording;
  evaluator: Evaluator;
  renderer: Renderer;
  hud: Hud;
}

/** A fresh live session: a mid-song start fast-forwards with the level's virtual player, then hands over. */
async function newSession(env: LiveEnv, startS: number, log: PlayLog, onProgress?: (f: number) => void): Promise<{ recording: LiveRecording; runner: LiveRunner }> {
  const { recording, sim } = liveSim(env.input, env.props, startS > 0);
  for (let t = 0; t < startS; ) {
    t = Math.min(startS, t + 5);
    sim.advanceTo(t);
    onProgress?.(t / startS);
    await yieldToUi();
  }
  if (startS > 0) handOver(sim, recording);
  // (The virtual player's presses before the hand-over have no key ids: the log skips them.)
  recording.pressLog = (t) => log.press(t);
  const runner = new LiveRunner(sim, recording);
  runner.log = log;
  return { recording, runner };
}

/**
 * Watch my run: the recorded key events replayed through the engine in batch mode (the virtual
 * player's part again first, after a mid-song start). The replay equals the live run.
 */
export function replay(input: PlannerInput, props: Record<string, PropInfo> | undefined, startS: number, keys: readonly KeyEvent[], untilS?: number): LiveRecording {
  const { recording, sim } = liveSim(input, props, startS > 0);
  if (startS > 0) {
    sim.advanceTo(startS);
    handOver(sim, recording);
  }
  for (const k of keys) sim.key(k);
  if (untilS !== undefined) sim.advanceTo(untilS);
  else sim.runToEnd();
  return recording;
}

export async function runLive(env: LiveEnv): Promise<void> {
  const { app, assets, input, audio, overlay, progress, hook, params } = env;
  const startS = params.bench ? benchFrom(input) : params.startS;
  const lib = input.level.hero === input.content.hero ? input.content : { ...input.content, hero: input.level.hero };
  const level = input.level.file.split("/").pop()!.replace(/\.ya?ml$/, "") || PLAY_LEVEL;

  progress(0.7, startS > 0 ? `Fast-forwarding to ${fmtTime(startS)}` : "Preparing the field");
  const log = new PlayLog();
  hook.playLog = log;
  let session = await newSession(env, startS, log, (f) => progress(0.7 + 0.2 * f, `Fast-forwarding to ${fmtTime(startS)}`));
  if (audio) {
    progress(0.92, "Loading sounds");
    await audio.loadSounds(assets.sounds, paletteSamples(lib), `${import.meta.env.BASE_URL}assets/sfx/`);
  }

  let stage: Stage | undefined;
  const show = (recording: LiveRecording): Stage => {
    if (stage) {
      app.stage.removeChildren();
      stage.renderer.destroy();
    }
    const evaluator = new Evaluator(recording.plan, lib, recording);
    const renderer = new Renderer(app, assets, recording.plan, lib);
    const hud = new Hud(recording.plan, lib, assets.credits);
    app.stage.addChild(renderer.screen, hud.root);
    stage = { recording, evaluator, renderer, hud };
    hook.plan = recording.plan;
    return stage;
  };
  show(session.recording);
  let lastStats = { alive: 0, particles: 0 };
  hook.stats = () => ({ frameMs: env.frameMs, ...lastStats });
  // ?debug=moves: movement and the groove by the numbers, from the recording as drawn.
  const moves = params.debugMoves ? new DebugMovesPanel() : undefined;
  const draw = (s: number, opts: Partial<HudOptions> = {}): Frame => {
    const st = stage!;
    st.recording.refresh();
    const frame = st.evaluator.evaluate(s);
    st.renderer.draw(frame);
    st.hud.draw(frame, st.renderer.outputRect(), { metronome: false, ...opts });
    moves?.update(movesReadout(st.recording.plan, s));
    app.renderer.render(app.stage);
    lastStats = { alive: frame.stats.alive, particles: frame.stats.particles };
    hook.lastHero = { s, x: frame.hero.x, y: frame.hero.y };
    return frame;
  };

  // Keys from tests and tools, at exact song times (the next frame applies them).
  const injected: KeyEvent[] = [];
  hook.live = {
    get runner() {
      return session.runner;
    },
    get recording() {
      return session.recording;
    },
    key: (e) => injected.push(e),
  };
  /** One live frame at song time s: apply the keys that have arrived, run ahead, draw. */
  const liveFrame = (s: number, keys: KeyEvent[], opts: Partial<HudOptions> = {}): Frame => {
    const t0 = performance.now();
    keys.push(...injected.splice(0));
    session.runner.frame(s, keys);
    audio?.scheduleLive();
    const frame = draw(s, opts);
    env.frameMs.push(performance.now() - t0);
    if (env.frameMs.length > 20000) env.frameMs.splice(0, 10000);
    return frame;
  };
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
    const beats = session.recording.plan.songBeats;
    const toS = session.recording.ctx.sections.find((x) => x.label === "climax")?.s0 ?? input.songMap.audio.duration;
    const dirs: Key[] = ["right", "down", "left", "up"];
    const presses: KeyEvent[] = [];
    for (let i = 0; i < beats.length; i++) {
      if (beats[i] <= startS + 0.5 || beats[i] >= toS) continue;
      const key = dirs[i % 4];
      presses.push({ s: beats[i] + 0.004, kind: "press", key }, { s: beats[i] + 0.064, kind: "release", key });
    }
    let next = 0;
    hook.bench = runBench(
      startS,
      toS,
      (s) => {
        const due: KeyEvent[] = [];
        while (next < presses.length && presses[next].s + 0.012 <= s) due.push(presses[next++]);
        liveFrame(s, due);
      },
      app,
    );
    return;
  }
  env.ready();

  if (env.begun) overlay.root.remove();
  else {
    overlay.status.textContent = "Arrows or WASD to move · Esc pauses · headphones recommended";
    if (startS > 0) overlay.status.textContent += ` · starting at ${fmtTime(startS)}`;
    await clickToBegin(overlay);
  }
  log.context = () => logContext(env, lib, level, startS, session);
  void new PlayController(env, level, startS, log, () => session, (s) => (session = s), show, () => stage!.hud, draw, liveFrame).start();
}

/** One bar before song time s (a count-in's length), from the song map. */
function barBefore(input: PlannerInput, s: number): { from: number; beats: number; beatLen: number } {
  const sm = input.songMap;
  const idx = indexSong(sm);
  const bi = beatIndexIn(sm.beats, s);
  let bar = 1;
  while (bar + 1 <= idx.barCount && idx.barStart[bar + 1] <= Math.floor(bi + 1e-6)) bar++;
  const beats = idx.beatsPerBar[bar] ?? 2;
  return { from: timeOfBeatIndex(sm, bi - beats), beats, beatLen: (s - timeOfBeatIndex(sm, bi - beats)) / beats };
}

type Phase = "countIn" | "playing" | "paused" | "replay";

/** The live flow: count-ins, the pause menu, restarts, the end card and Watch my run. */
class PlayController {
  private phase: Phase = "countIn";
  /** The game waits at this song time through a count-in (or a pause). */
  private holdS = 0;
  private countFrom = 0;
  private count = { beats: 2, beatLen: 0.3 };
  private keyboard: KeyboardInput;
  private menu?: { menu: HudMenu; actions: (() => void)[] };
  private end?: EndCardInfo;
  private replayRec?: LiveRecording;
  private lastS = 0;
  private frameDur = 1000 / 60;
  /** The audio clock is running for the current count-in (a seek is asynchronous). */
  private clockReady = false;
  /** The hero's health ran out: the game waits on the fallen menu (Esc does not resume). */
  private fallen = false;

  constructor(
    private readonly env: LiveEnv,
    private readonly level: string,
    private readonly startS: number,
    private readonly log: PlayLog,
    private readonly session: () => { recording: LiveRecording; runner: LiveRunner },
    private readonly setSession: (s: { recording: LiveRecording; runner: LiveRunner }) => void,
    private readonly show: (r: LiveRecording) => Stage,
    private readonly currentHud: () => Hud,
    private readonly draw: (s: number, opts?: Partial<HudOptions>) => Frame,
    private readonly liveFrame: (s: number, keys: KeyEvent[], opts?: Partial<HudOptions>) => Frame,
  ) {
    const audio = env.audio!;
    this.keyboard = new KeyboardInput((perfMs) => audio.songTimeAt(perfMs), loadInputOffset);
    this.keyboard.log = log;
    this.keyboard.clock = () => audio.clockProbe();
    audio.soundLog = (e) => log.sound(e);
  }

  /** Keys thrown away outside play (a count-in, a restart). */
  private discardKeys(): void {
    for (const k of this.keyboard.drain()) this.log.drop({ id: k.id, s: k.s, phase: this.phase });
  }

  private get audio(): AudioEngine {
    return this.env.audio!;
  }

  async start(): Promise<void> {
    const hook = this.env.hook;
    hook.audio = () => ({ state: this.audio.ctx.state, contextSongTime: this.audio.contextSongTime(), stats: { ...this.audio.stats } });
    hook.play = {
      phase: () => this.phase,
      pause: () => this.pause(),
      menu: () => this.menu?.menu,
      choose: (label) => {
        const i = this.menu?.menu.items.findIndex((x) => x.startsWith(label)) ?? -1;
        if (i >= 0) this.menu!.actions[i]();
      },
      // QA: the replay of the run so far, part by part against the live recording.
      compareReplay: () => {
        const live = this.session();
        // (Up to where the live run has got.)
        const sim = live.runner.sim;
        const again = replay(this.env.input, this.env.props, this.startS, live.runner.applied, sim.done ? undefined : sim.s);
        const a = live.recording.hashParts();
        const b = again.hashParts();
        return Object.keys(a).filter((k) => a[k] !== b[k]);
      },
    };
    // A mid-song start begins with a one-bar count-in: the music starts a bar early.
    if (this.startS > 0) await this.countIn(this.startS, true);
    else {
      this.phase = "playing";
      await this.audio.startLive(() => this.session().recording.plan.events.sounds, 0);
    }
    this.keyboard.attach();
    window.addEventListener("keydown", this.onKey);
    window.addEventListener("pointerdown", this.onPointer);
    window.addEventListener("blur", this.onBlur);
    document.addEventListener("visibilitychange", this.onVisibility);
    window.addEventListener("pagehide", () => this.log.beacon());
    this.log.event("start", this.startS);
    void this.log.flush("start");
    requestAnimationFrame(this.loop);
  }

  /** Wait at song time s through a one-bar count-in: the music plays the bar before it, the game's sounds stay silent. */
  private async countIn(s: number, first = false): Promise<void> {
    const bar = barBefore(this.env.input, s);
    this.holdS = s;
    this.countFrom = bar.from;
    this.count = { beats: bar.beats, beatLen: bar.beatLen };
    this.phase = "countIn";
    this.clockReady = false;
    this.log.event("countIn", s, { from: bar.from, beats: bar.beats });
    this.discardKeys();
    if (first) await this.audio.startLive(() => this.session().recording.plan.events.sounds, bar.from, s);
    else await this.audio.seekLive(bar.from, s);
    this.clockReady = true;
  }

  private readonly loop = (ts: number) => {
    const audio = this.audio;
    const perf = performance.now();
    const s = audio.songTimeAtPresentation(ts, this.frameDur);
    this.trackFrames(ts);
    const hook = this.env.hook;
    let nKeys = 0;
    if (this.phase === "countIn") {
      // Input begins on the bar line.
      this.discardKeys();
      if (!this.clockReady) this.draw(this.holdS, { countIn: { count: 0, of: this.count.beats, fade: 1, beatPhase: 0 } });
      else if (s < this.holdS) {
        const k = Math.min(this.count.beats, Math.max(1, Math.floor((s - this.countFrom) / this.count.beatLen) + 1));
        const ph = Math.max(0, ((s - this.countFrom) / this.count.beatLen) % 1);
        this.draw(this.holdS, { countIn: { count: k, of: this.count.beats, fade: 1, beatPhase: ph } });
      } else {
        this.phase = "playing";
        this.log.event("playing", s);
      }
    }
    if (this.phase === "playing") {
      const keys = this.keyboard.drain();
      nKeys = keys.length;
      const frame = this.liveFrame(s, keys, this.hudOptions());
      this.lastS = s;
      if (frame.hud.end && !this.end) this.showEnd();
      else if (frame.hud.health <= 0) this.fall();
    } else if (this.phase === "paused") this.draw(this.lastS, this.hudOptions());
    else if (this.phase === "replay") {
      const frame = this.draw(s, this.hudOptions());
      if (frame.hud.end && !this.menu) this.menu = this.endMenu();
    }
    // (The song time drawn: the game waits through count-ins and pauses.)
    hook.lastS = this.phase === "paused" ? this.lastS : this.phase === "countIn" ? Math.min(s, this.holdS) : s;
    hook.frames = (hook.frames ?? 0) + 1;
    if (this.phase === "playing" || this.phase === "countIn") {
      this.log.frame({ ts, perf, s, raw: audio.lastRawPresentation, phase: this.phase, dur: this.frameDur, keys: nKeys, ms: performance.now() - perf, simS: this.session().runner.sim.s });
      if (this.phase === "playing") this.log.maybeFlush();
    }
    requestAnimationFrame(this.loop);
  };

  /** The display's refresh period: a median of recent frame intervals. */
  private lastTs = 0;
  private intervals: number[] = [];
  private trackFrames(ts: number): void {
    if (this.lastTs > 0) {
      const dt = ts - this.lastTs;
      if (dt > 2 && dt < 100) {
        this.intervals.push(dt);
        if (this.intervals.length > 31) this.intervals.shift();
        this.frameDur = [...this.intervals].sort((a, b) => a - b)[this.intervals.length >> 1];
      }
    }
    this.lastTs = ts;
  }

  private hudOptions(): Partial<HudOptions> {
    return { menu: this.menu?.menu, end: this.end };
  }

  // ------------------------------------------------------------------ pause

  private pause(): void {
    if (this.phase !== "playing" && this.phase !== "countIn") return;
    // Held keys are released where the game stops.
    const at = this.phase === "countIn" ? this.holdS : this.lastS;
    this.keyboard.releaseAll(at);
    if (this.phase === "playing") this.session().runner.frame(at, this.keyboard.drain());
    this.keyboard.enabled = false;
    this.lastS = at;
    this.phase = "paused";
    this.log.event("pause", at);
    void this.log.flush("pause");
    void this.audio.pauseLive();
    this.menu = {
      menu: { title: "PAUSED", items: ["Resume", "Restart section", "Restart song", "Quit to title"], selected: 0 },
      actions: [() => this.resume(), () => this.restartSection(), () => this.restartSong(), () => this.quit()],
    };
  }

  /** The hero's health ran out: the game stops there, and the player restarts the section or the song. */
  private fall(): void {
    const at = this.lastS;
    this.keyboard.releaseAll(at);
    this.session().runner.frame(at, this.keyboard.drain());
    this.keyboard.enabled = false;
    this.phase = "paused";
    this.fallen = true;
    this.log.event("fell", at);
    void this.log.flush("fell");
    void this.audio.pauseLive();
    this.menu = {
      menu: { title: "YOU FELL", items: ["Restart section", "Restart song", "Quit to title"], selected: 0 },
      actions: [() => this.restartSection(), () => void this.restartSong(), () => this.quit()],
    };
  }

  /** Resume after a one-bar count-in: the music replays the bar before the pause point; the game resumes exactly there. */
  private resume(): void {
    this.log.event("resume", this.lastS);
    this.menu = undefined;
    this.keyboard.enabled = true;
    void this.countIn(this.lastS);
  }

  private restartSection(): void {
    this.fallen = false;
    const s0 = this.session().runner.restartSection();
    this.log.event("restartSection", s0);
    this.menu = undefined;
    this.keyboard.enabled = true;
    this.end = undefined;
    if (s0 === undefined) return void this.countIn(this.lastS);
    this.lastS = s0;
    void this.countIn(s0);
  }

  private async restartSong(): Promise<void> {
    this.menu = { menu: { title: "RESTARTING", items: [], selected: 0, notes: this.startS > 0 ? [`fast-forwarding to ${fmtTime(this.startS)}`] : [] }, actions: [] };
    this.draw(this.lastS, this.hudOptions());
    this.log.event("restartSong", this.startS);
    void this.log.flush("restartSong");
    await this.audio.pauseLive();
    const session = await newSession(this.env, this.startS, this.log);
    this.setSession(session);
    this.show(session.recording);
    this.menu = undefined;
    this.end = undefined;
    this.fallen = false;
    this.replayRec = undefined;
    this.keyboard.enabled = true;
    this.discardKeys();
    this.lastS = this.startS;
    await this.countIn(this.startS);
  }

  private quit(): void {
    const q = new URLSearchParams(location.search);
    q.delete("mode");
    q.delete("start");
    location.search = q.toString();
  }

  private watchDemo(): void {
    const q = new URLSearchParams(location.search);
    q.set("mode", "demo");
    q.delete("start");
    location.search = q.toString();
  }

  // ------------------------------------------------------------------ the end

  private showEnd(): void {
    const plan = this.session().recording.plan;
    const grade: Grade = gradeFor(plan.stats.averageGroove);
    const best = loadBestGrade(this.level);
    const newBest = !best || gradeRank(grade) < gradeRank(best);
    if (newBest) saveBestGrade(this.level, grade);
    this.end = { grade, best: newBest ? undefined : best, newBest, startedAt: this.startS > 0 ? this.startS : undefined };
    this.log.event("end", this.lastS, { grade, averageGroove: plan.stats.averageGroove });
    void this.log.flush("end");
    this.menu = this.endMenu();
  }

  private endMenu(): { menu: HudMenu; actions: (() => void)[] } {
    return { menu: { items: ["Play again", "Watch my run", "Watch the demo"], selected: 0 }, actions: [() => void this.restartSong(), () => void this.watchMyRun(), () => this.watchDemo()] };
  }

  /** Replay the run just played, identically, and play it back like the demo. */
  private async watchMyRun(): Promise<void> {
    this.menu = { menu: { title: "REPLAYING", items: [], selected: 0 }, actions: [] };
    this.draw(this.lastS, this.hudOptions());
    await yieldToUi();
    this.replayRec ??= replay(this.env.input, this.env.props, this.startS, this.session().runner.applied);
    this.show(this.replayRec);
    this.menu = undefined;
    this.phase = "replay";
    const from = Math.max(0, this.startS > 0 ? barBefore(this.env.input, this.startS).from : 0);
    await this.audio.pauseLive();
    this.audio.stop();
    await this.audio.start(this.replayRec.plan, from);
  }

  // ------------------------------------------------------------------ input

  private readonly onKey = (e: KeyboardEvent) => {
    if (e.key === "F8") {
      // Save the timing log as a file (development builds also save it to logs/play/).
      e.preventDefault();
      this.log.download();
      return;
    }
    if (e.key === "Escape") {
      e.preventDefault();
      if (this.fallen) return;
      if (this.phase === "paused" && this.menu) this.resume();
      else this.pause();
      return;
    }
    const m = this.menu;
    if (!m || m.menu.items.length === 0) return;
    const k = keyOf(e);
    if (k === "up") m.menu.selected = (m.menu.selected + m.menu.items.length - 1) % m.menu.items.length;
    else if (k === "down") m.menu.selected = (m.menu.selected + 1) % m.menu.items.length;
    else if (k === "right" || e.key === "Enter" || e.key === " ") m.actions[m.menu.selected]();
    else return;
    e.preventDefault();
  };

  private readonly onPointer = (e: PointerEvent) => {
    const m = this.menu;
    if (!m) return;
    const i = this.currentHud().menuRects.findIndex((r) => e.clientX >= r.x && e.clientX <= r.x + r.w && e.clientY >= r.y && e.clientY <= r.y + r.h);
    if (i >= 0) m.actions[i]();
  };

  /** Losing focus releases every held key and pauses. */
  private readonly onBlur = () => {
    this.log.event("blur", this.lastS);
    this.pause();
  };

  private readonly onVisibility = () => {
    this.log.event("visibility", this.lastS, document.hidden ? "hidden" : "visible");
    if (document.hidden) this.pause();
  };
}

/** The play-mode benchmark starts at theme A. */
function benchFrom(input: PlannerInput): number {
  const sm = input.songMap;
  const sec = sm.sections.find((x) => x.label === "theme-a");
  return sec ? timeOfBeatIndex(sm, indexSong(sm).barStart[sec.bars[0]]) : 0;
}

const round = (v: number, d = 1e6) => Math.round(v * d) / d;

/** What the timing log carries besides its events: settings, the song's grid and the recording's settled presses and swings. */
function logContext(env: LiveEnv, lib: ContentLibrary, level: string, startS: number, session: { recording: LiveRecording; runner: LiveRunner }): Record<string, unknown> {
  const { recording, runner } = session;
  const { ctx, sch, plan } = recording;
  const hero = lib.hero;
  const ac = env.audio?.ctx;
  return {
    level,
    seed: env.input.seed,
    params: { avOffsetMs: env.params.avOffsetMs, startS, mode: env.params.mode },
    inputOffsetMs: loadInputOffset(),
    audio: ac ? { sampleRate: ac.sampleRate, baseLatency: ac.baseLatency, outputLatency: ac.outputLatency, state: ac.state, stats: env.audio!.stats, live: env.audio!.liveStats() } : undefined,
    hero: { timing: hero.timing, step: hero.step, swings: { minLeadMs: hero.swings.minLeadMs, windupMs: hero.swings.windupMs }, stride: hero.stride, walk: { speed: hero.walk.speed } },
    rules: ctx.level.rules,
    playerFrom: recording.playerFrom,
    runner: { ...runner.stats, settled: runner.settled, simS: runner.sim.s },
    sections: ctx.sections.map((x) => ({ label: x.label, s0: round(x.s0), s1: round(x.s1), movement: x.level.movement })),
    beats: Array.from(plan.songBeats, (v) => round(v)),
    grid: { s: Array.from(sch.gridTimes, (v) => round(v)), accent: sch.gridSlots.map((x) => round(x.accent, 100)), section: sch.gridSlots.map((x) => x.section) },
    inputs: plan.inputs.map((i) => ({ ...i, s: round(i.s), w: round(i.w) })),
    swings: plan.swings.map((w) => ({ s: round(w.s), sArmed: round(w.sArmed), kind: w.kind, press: w.press, accent: w.accent })),
  };
}
