/**
 * Play mode's recording: it grows as play goes on and can be rewound for
 * rollback. It is a Recorder (the simulation reports its events to it), an
 * emitter (each event's effects, sounds and HUD events are emitted as it
 * happens) and a PlanIndex (the evaluator reads it like a finished plan).
 *
 * Every change goes through an undo journal, so `rewind(mark)` restores the
 * recording exactly as it was at `mark`: a snapshot of the simulation plus its
 * mark is a complete rollback point.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { hash32 } from "../core/rng.ts";
import { beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import { Warp, type WarpBreak } from "../core/warp.ts";
import { DEFAULT_PROPS, type PropInfo, type TiledMap } from "../mapgen/mapgen.ts";
import { stampLandmark, WAVE_BANDS, type ZoneWave } from "../mapgen/openfield.ts";
import { grooveAt } from "../plan/evaluate.ts";
import type { CameraTrack, Chain, EffectEvent, EnemyRecord, Fate, GemEvent, HeroTrack, Hit, MapLayout, Plan, PlanInput, PlanSwing, Projectile, SlamMarker, SoundEvent, UiEvent, UpgradeRecord } from "../plan/types.ts";
import { beatIndexIn, type PlanIndex } from "../plan/view.ts";
import { beatDurationAt, sectionIndexAt, type Ctx } from "../planner/context.ts";
import { budgetSlot, chimeDegree, Emitter, lastAtOrBefore, soundSlot, VOICE_BUDGET, type EmitSink } from "../planner/emit.ts";
import { ACTIVE_BUCKET, DEATH_ANIM, planHeader } from "../planner/plan.ts";
import type { ArrowRecord, Attack, BossInfo, Cleave, Dance, FormationRecord, GemRecord, Landing, LandmarkRecord, PressTrace, Recorder, SlamRecord, Threat, VacuumRecord, ZoneWaveRecord } from "../sim/recorder.ts";
import type { Schedule } from "../sim/schedule.ts";
import { ENEMY_RATE, HERO_ANIMS, HERO_RATE, SLAM_RADIUS } from "../sim/simulate.ts";

/** Effects are drawn at most this long (world seconds) after they start; decals have their own list. */
const EFFECT_WINDOW = 4;
/** A sixteenth-note slot's sounds are final this long after the slot ends (late emissions get in). */
const SLOT_SETTLE = 0.06;
/** Kills are grouped into one sound per slot this long after the slot ends. */
const KILL_SETTLE = 0.03;

/** A growable typed array. */
class Grow<T extends Float32Array | Uint8Array> {
  buf: T;
  len = 0;
  constructor(private readonly make: (n: number) => T) {
    this.buf = make(1024);
  }
  push(v: number): void {
    if (this.len === this.buf.length) {
      const b = this.make(this.buf.length * 2);
      b.set(this.buf);
      this.buf = b;
    }
    this.buf[this.len++] = v;
  }
  view(): T {
    return this.buf.subarray(0, this.len) as T;
  }
}

type Provisional = { provisional?: boolean };

export class LiveRecording implements Recorder, PlanIndex, EmitSink {
  /** The recording, shaped like a finished plan (its arrays grow as play goes on). */
  readonly plan: Plan;
  /** The time warp so far (it reads the live list of breaks). */
  readonly timeWarp: Warp;
  readonly emitter: Emitter;
  /** Bumped by every rewind. */
  version = 0;
  /** The song and world time the recording has reached. */
  s = 0;
  w = 0;

  private journal: (() => void)[] = [];
  private jBase = 0;
  /** Starts with a placeholder, which the simulation's first break replaces. */
  private readonly breaks: WarpBreak[] = [{ s: 0, w: 0, rate: 1 }];
  private placeholderBreak = true;
  private readonly hx = new Grow((n) => new Float32Array(n));
  private readonly hy = new Grow((n) => new Float32Array(n));
  private readonly hz = new Grow((n) => new Float32Array(n));
  private readonly hFacing = new Grow((n) => new Uint8Array(n));
  private readonly hAnim = new Grow((n) => new Uint8Array(n));
  private readonly hFrame = new Grow((n) => new Uint8Array(n));
  private readonly cx = new Grow((n) => new Float32Array(n));
  private readonly cy = new Grow((n) => new Float32Array(n));
  private readonly cz = new Grow((n) => new Float32Array(n));
  private heroViewLen = -1;
  /** World beats before this index are final. */
  private worldBeatsFinal = 0;
  private camViewLen = -1;
  private readonly hud: { kills: number[]; perfect: number[]; xpTimes: number[]; xpFill: number[]; levelUps: number[]; weaponFires: Record<string, number[]> };
  private readonly grooveSeries: { s: number[]; v: number[] };
  private readonly tierSeries: { s: number[]; v: number[] };
  private readonly healthSeries: { s: number[]; v: number[] };
  private readonly arrows: ArrowRecord[] = [];
  private readonly arrowProjectile: Projectile[] = [];
  private readonly slamMarkers = new Map<number, SlamMarker>();
  private nWaves = 0;
  private bossState?: { info: BossInfo; bar: Extract<UiEvent, { kind: "bossBar" }> };
  private readonly decalIdx: number[] = [];
  private readonly decalRecipes: Set<string>;
  private readonly activeIds: number[] = [];
  private effFirst = 0;
  private effLastW = 0;
  private readonly effOut: number[] = [];
  private gemFirst = 0;
  private readonly gemsLive: GemEvent[] = [];
  private readonly arrivals = new Set<number>();
  /** Gems on the field: the latest chime's sixteenth-note slot and scale degree (a run climbs from it). */
  private lastChime?: { slot: number; degree: number };
  /** Emission waiting for its slot to close: kills grouped per slot, sounds per slot (voice budget), weapon hits (until their fates are in). */
  private readonly pendingKills = new Map<number, { s: number; x: number; n: number; big: boolean; end: number; movedFrom?: number }>();
  private readonly pendingSounds = new Map<number, SoundEvent[]>();
  private readonly closedSlots = new Map<number, number>();
  private readonly pendingWeaponHits: Hit[] = [];
  private readonly caught = new Set<number>();
  private accentIdx = 0;
  private blastSeen?: number;
  private readonly propsInfo: Record<string, PropInfo>;

  constructor(
    readonly ctx: Ctx,
    readonly sch: Schedule,
    map: MapLayout,
    lib: ContentLibrary,
    profile: string,
    props?: Record<string, PropInfo>,
  ) {
    this.propsInfo = { ...DEFAULT_PROPS, ...(props ?? {}) };
    this.hud = { kills: [], perfect: [], xpTimes: [0], xpFill: [0], levelUps: [], weaponFires: {} };
    this.grooveSeries = { s: [0], v: [0] };
    this.tierSeries = { s: [0], v: [0] };
    this.healthSeries = { s: [0], v: [1] };
    const header = planHeader(ctx, sch, map, profile);
    this.plan = {
      ...header,
      warp: this.breaks,
      worldBeats: Float64Array.from(header.songBeats),
      hero: this.heroTrack(),
      camera: this.makeCamera(),
      hits: [],
      enemies: [],
      enemySamples: new Float32Array(0),
      enemySampleRate: ENEMY_RATE,
      events: { effects: [], sounds: [], gems: [], ui: [] },
      projectiles: [],
      slams: [],
      chains: [],
      activeBucket: ACTIVE_BUCKET,
      activeEnemies: [],
      activeEffects: [],
      hud: this.hud,
      weapons: [],
      swings: [],
      inputs: [],
      groove: this.grooveSeries,
      tier: this.tierSeries,
      ...(ctx.level.rules.pressCleaves ? { cleaves: [] } : {}),
      ...(ctx.level.rules.fieldGems ? { vacuums: [], pickupRadius: ctx.lib.hero.pickupRadius ?? [40] } : {}),
      ...(ctx.level.rules.upgradePool ? { upgrades: [] } : {}),
      health: this.healthSeries,
      nearMisses: [],
      threats: [],
      formations: [],
      stats: { peakPopulation: 0, killsPerSection: {}, totalHitStop: 0, enemies: 0, hits: 0, swings: 0, presses: 0, ratings: { perfect: 0, great: 0, good: 0, none: 0 }, averageGroove: 0, minHealth: 1, planMs: 0, simMs: 0 },
      ...(ctx.level.world === "open" ? { open: { firstZone: Math.max(0, map.zones.findIndex((z) => z.id === ctx.sections[0].level.zone)), waves: [], landmarks: [] } } : {}),
    };
    this.timeWarp = new Warp(this.breaks);
    this.decalRecipes = new Set([...lib.effects.values()].filter((r) => r.layers.some((l) => l.kind === "decal")).map((r) => r.id));
    this.emitter = new Emitter(ctx, this.timeWarp, () => this.cameraTrack(), this, true, { grid: sch.gridTimes, lastKeyAt: (s) => lastAtOrBefore(this.plan.inputs, s) });
    // Banners and the title card belong to the song: they are known before play starts.
    this.emitter.scriptUi();
  }

  // ------------------------------------------------------------------ the journal

  private undo(fn: () => void): void {
    this.journal.push(fn);
  }

  private add<T>(arr: T[], v: T): void {
    arr.push(v);
    this.undo(() => {
      arr.pop();
    });
  }

  private set<T extends object, K extends keyof T>(obj: T, k: K, v: T[K]): void {
    const had = k in obj;
    const old = obj[k];
    obj[k] = v;
    this.undo(() => {
      if (had) obj[k] = old;
      else delete obj[k];
    });
  }

  /** A point the recording can be rewound to. */
  mark(): number {
    // Samples after a mark start a new batch, so rewinding to the mark undoes them.
    this.sampleBatch = undefined;
    return this.jBase + this.journal.length;
  }

  /** Song time the player took over from the level's virtual player (a mid-song start): the grade counts from here. */
  playerFrom = 0;

  /** Undo everything recorded since `mark`. */
  rewind(mark: number): void {
    if (mark < this.jBase) throw new Error(`cannot rewind to ${mark}: the journal starts at ${this.jBase}`);
    while (this.jBase + this.journal.length > mark) this.journal.pop()!();
    this.version++;
    this.worldBeatsFinal = Math.max(0, Math.min(this.worldBeatsFinal, Math.floor(beatIndexIn(this.plan.songBeats, this.s)) - 2));
    this.effFirst = Math.min(this.effFirst, this.plan.events.effects.length);
    // (Gems on the field live long and arrive out of order: a rewind can take an arrival back.)
    this.gemFirst = this.ctx.level.rules.fieldGems ? 0 : Math.min(this.gemFirst, this.gemsLive.length);
  }

  /** Nothing before `mark` will be rewound: forget how to undo it. */
  commit(mark: number): void {
    const n = mark - this.jBase;
    if (n < 4096) return;
    this.journal.splice(0, n);
    this.jBase = mark;
  }

  // ------------------------------------------------------------------ tracks

  private heroTrack(): HeroTrack {
    return { rate: HERO_RATE, w0: 0, x: this.hx.view(), y: this.hy.view(), z: this.hz.view(), facing: this.hFacing.view(), anim: this.hAnim.view(), frame: this.hFrame.view(), animNames: HERO_ANIMS, trail: new Float32Array(this.hx.len) };
  }

  private makeCamera(): CameraTrack {
    return { rate: HERO_RATE, w0: 0, x: this.cx.view(), y: this.cy.view(), zoom: this.cz.view() };
  }

  private cameraTrack(): CameraTrack {
    if (this.camViewLen !== this.cx.len) {
      this.camViewLen = this.cx.len;
      this.plan.camera = this.makeCamera();
    }
    return this.plan.camera;
  }

  /** Bring the plan view up to date before evaluating a frame. */
  refresh(): void {
    if (this.heroViewLen !== this.hx.len) {
      this.heroViewLen = this.hx.len;
      this.plan.hero = this.heroTrack();
    }
    this.cameraTrack();
    // World beats: exact up to the recording's time (final once it is well past them), extrapolated beyond it.
    const wb = this.plan.worldBeats as Float64Array;
    const sb = this.plan.songBeats;
    for (let k = this.worldBeatsFinal; k < sb.length; k++) wb[k] = this.timeWarp.toWorld(sb[k]);
    this.worldBeatsFinal = Math.max(this.worldBeatsFinal, Math.floor(beatIndexIn(sb, this.s)) - 2);
  }

  /** The latest hero position. */
  private heroNow(): { x: number; y: number } {
    const n = this.hx.len;
    return n > 0 ? { x: this.hx.buf[n - 1], y: this.hy.buf[n - 1] } : { x: 0, y: 0 };
  }

  /** World time of a song time: exact up to now, extrapolated at the current rate beyond. */
  private predictW(s: number): number {
    return this.timeWarp.toWorld(s);
  }

  // ------------------------------------------------------------------ Recorder

  warp(b: WarpBreak, replaceLast: boolean): void {
    const br = this.breaks;
    if (this.placeholderBreak) {
      // The first real break replaces the placeholder.
      this.placeholderBreak = false;
      const old = br[0];
      br[0] = b;
      this.undo(() => {
        br[0] = old;
        this.placeholderBreak = true;
      });
      return;
    }
    if (replaceLast) {
      const i = br.length - 1;
      const old = br[i];
      br[i] = b;
      this.undo(() => {
        br[i] = old;
      });
    } else this.add(br, b);
  }

  hero(x: number, y: number, z: number, facing: number, anim: number, frame: number): void {
    this.hx.push(x);
    this.hy.push(y);
    this.hz.push(z);
    this.hFacing.push(facing);
    this.hAnim.push(anim);
    this.hFrame.push(frame);
    this.undo(() => {
      this.hx.len--;
      this.hy.len--;
      this.hz.len--;
      this.hFacing.len--;
      this.hAnim.len--;
      this.hFrame.len--;
    });
  }

  camera(x: number, y: number, zoom: number): void {
    this.cx.push(x);
    this.cy.push(y);
    this.cz.push(zoom);
    this.undo(() => {
      this.cx.len--;
      this.cy.len--;
      this.cz.len--;
    });
  }

  spawn(rec: EnemyRecord): void {
    rec.samples = new Float32Array(64);
    rec.sampleCount = 0;
    rec.sampleOffset = 0;
    this.add(this.plan.enemies, rec);
    this.add(this.activeIds, rec.id);
  }

  sample(id: number, x: number, y: number): void {
    const e = this.plan.enemies[id];
    const n = e.sampleCount;
    if ((n + 1) * 2 > e.samples!.length) {
      const b = new Float32Array(e.samples!.length * 2);
      b.set(e.samples!);
      e.samples = b;
    }
    e.samples![n * 2] = x;
    e.samples![n * 2 + 1] = y;
    e.sampleCount = n + 1;
    // One undo for a whole tick of samples (hundreds of enemies at 30 Hz): it keeps the journal small
    // enough to hold a whole section, for restarting it.
    const at = this.jBase + this.journal.length;
    if (this.sampleBatch && this.sampleBatchAt === at) this.sampleBatch.push(e);
    else {
      const batch = [e];
      this.sampleBatch = batch;
      this.undo(() => {
        for (const x of batch) x.sampleCount--;
        this.sampleBatch = undefined;
      });
      this.sampleBatchAt = at + 1;
    }
  }

  private sampleBatch?: EnemyRecord[];
  private sampleBatchAt = -1;

  fate(id: number, fate: Fate): void {
    const e = this.plan.enemies[id];
    this.add(e.fates, fate);
    if (!this.caught.has(fate.hit)) {
      this.caught.add(fate.hit);
      this.undo(() => this.caught.delete(fate.hit));
    }
    const kill = this.emitter.fate(e, fate, this.plan.hits[fate.hit]);
    if (!kill) return;
    this.add(this.hud.kills, kill.s);
    const free = this.emitter.sectionOf(kill.s).level.movement === "free";
    const soundS = kill.soundS ?? kill.s;
    const bi = beatIndexAt(this.ctx.song, soundS);
    // (With grid sounds a group's kills all come at or before its slot, so it closes right after it:
    // half a beat later would be past the simulation's lead in a slow free section.)
    const end = kill.soundS !== undefined ? soundS : timeOfBeatIndex(this.ctx.song, free ? Math.round(bi) + 0.5 : (kill.slot + 0.5) / 4);
    const g = this.pendingKills.get(kill.slot);
    if (g) {
      const before = { n: g.n, big: g.big, movedFrom: g.movedFrom };
      g.n++;
      g.big ||= kill.big;
      if (kill.movedFrom !== undefined) g.movedFrom = Math.min(g.movedFrom ?? Infinity, kill.movedFrom);
      this.undo(() => {
        g.n = before.n;
        g.big = before.big;
        g.movedFrom = before.movedFrom;
      });
    } else {
      this.pendingKills.set(kill.slot, { s: soundS, x: kill.x, n: 1, big: kill.big, end, ...(kill.movedFrom !== undefined ? { movedFrom: kill.movedFrom } : {}) });
      this.undo(() => this.pendingKills.delete(kill.slot));
    }
  }

  death(id: number, w: number, recycled: boolean): void {
    const e = this.plan.enemies[id];
    this.set(e, "deathW", w);
    if (recycled) this.set(e, "recycled", true);
  }

  attack(id: number, a: Attack): void {
    const e = this.plan.enemies[id];
    if (!e.attacks) this.set(e, "attacks", []);
    const list = e.attacks!;
    // The attack replaces its provisional wind-up.
    const i = list.findLastIndex((x) => (x as Provisional).provisional && x.kind === a.kind);
    if (i >= 0) {
      const old = list[i];
      list[i] = a;
      this.undo(() => {
        list[i] = old;
      });
    } else this.add(list, a);
  }

  dance(id: number, d: Dance): void {
    this.set(this.plan.enemies[id], "dance", d);
  }

  hit(h: Hit): void {
    this.add(this.plan.hits, h);
    if (h.source === "weapon") this.add(this.pendingWeaponHits, h);
    if (h.source === "ring" && this.blastSeen === undefined) this.finaleBlast(h);
  }

  swing(sw: PlanSwing): void {
    this.add(this.plan.swings, sw);
    const rating = sw.press !== undefined ? this.plan.inputs[sw.press]?.rating : undefined;
    if (this.emitter.swing(sw, this.plan.hits[sw.hit], grooveAt(this.grooveSeries, sw.s), rating)) this.add(this.hud.perfect, sw.s);
  }

  input(i: PlanInput): void {
    this.add(this.plan.inputs, i);
  }

  /** Play mode's timing log: what each key did in the simulation (outside the journal: a rollback's re-run replaces it). */
  pressLog?: (t: PressTrace) => void;

  press(t: PressTrace): void {
    this.pressLog?.(t);
  }

  groove(s: number, v: number): void {
    this.add(this.grooveSeries.s, s);
    this.add(this.grooveSeries.v, v);
  }

  tier(s: number, w: number, tier: number, soundS: number): void {
    const up = tier > this.tierSeries.v[this.tierSeries.v.length - 1];
    this.add(this.tierSeries.s, s);
    this.add(this.tierSeries.v, tier);
    this.emitter.tierChime(soundS, up, w, s);
  }

  health(s: number, frac: number): void {
    this.add(this.healthSeries.s, s);
    this.add(this.healthSeries.v, frac);
  }

  landing(l: Landing): void {
    this.emitter.landing(l);
  }

  weapon(id: string, s: number): void {
    const g = { id, s };
    this.add(this.plan.weapons, g);
    // (With the upgrade pool, the level-up is its upgrade's, and the starting loadout has none.)
    if (this.plan.upgrades) return;
    this.add(this.hud.levelUps, s);
    this.emitter.levelUp(g, this.heroNow());
  }

  upgrade(u: UpgradeRecord): void {
    this.add(this.plan.upgrades!, { ...u, offers: u.offers.map((o) => ({ ...o })), ...(u.orbs ? { orbs: u.orbs.map((p) => ({ ...p })) } : {}) });
    this.add(this.hud.levelUps, u.s);
    this.emitter.levelUp({ id: u.taken >= 0 ? u.offers[u.taken].id : "", s: u.s }, this.heroNow());
  }

  upgradeTaken(i: number, k: number, s: number, w: number, sApply: number, walked: boolean): void {
    const u = this.plan.upgrades![i];
    this.set(u, "taken", k);
    this.set(u, "sTaken", s);
    this.set(u, "wTaken", w);
    this.set(u, "sApply", sApply);
    this.set(u, "walked", walked);
    this.emitter.orbTaken(sApply, w, s);
  }

  fire(weapon: string, s: number): void {
    const wf = this.hud.weaponFires;
    if (!wf[weapon]) this.set(wf, weapon, []);
    this.add(wf[weapon], s);
  }

  chain(p: { w: number; x: number; y: number }): void {
    const c: Chain = { points: [p] };
    this.add(this.plan.chains, c);
  }

  chainPoint(chain: number, p: { w: number; x: number; y: number }): void {
    this.add(this.plan.chains[chain].points, p);
  }

  hitStop(): void {}

  hitStopCut(): void {}

  arrow(rec: ArrowRecord): void {
    this.add(this.arrows, rec);
    // Until it arrives, the arrow flies on past its aim point at the speed it will have if nothing changes.
    const wArrive = this.predictW(rec.sArrive);
    const len = Math.hypot(rec.ax - rec.x0, rec.ay - rec.y0) || 1;
    const speed = len / Math.max(1e-3, wArrive - rec.wRelease);
    const ux = (rec.ax - rec.x0) / len;
    const uy = (rec.ay - rec.y0) / len;
    const pr: Projectile = { kind: "arrow", w0: rec.wRelease, w1: wArrive + 260 / speed, x0: rec.x0, y0: rec.y0, x1: rec.ax + ux * 260, y1: rec.ay + uy * 260, wMiss: wArrive };
    this.add(this.arrowProjectile, pr);
    this.add(this.plan.projectiles, pr);
    this.emitter.arrowRelease(rec);
  }

  arrowEnd(arrow: number, end: Pick<ArrowRecord, "wArrive" | "outcome" | "x1" | "y1" | "w1">): void {
    const rec = this.arrows[arrow];
    const pr = this.arrowProjectile[arrow];
    const before = { ...rec };
    const prBefore = { ...pr };
    Object.assign(rec, end);
    Object.assign(pr, { w1: rec.w1, x1: rec.x1, y1: rec.y1, wMiss: rec.wArrive });
    this.undo(() => {
      Object.assign(rec, before);
      Object.assign(pr, prBefore);
    });
    this.emitter.arrowShatter(rec);
  }

  slamMark(k: number, m: { x: number; y: number; sMark: number; wMark: number; sLand: number; boss: number; dir: number }): void {
    const wLand = this.predictW(m.sLand);
    const marker: SlamMarker = { w0: m.wMark, w1: wLand, x: m.x, y: m.y, radius: SLAM_RADIUS };
    this.add(this.plan.slams, marker);
    this.slamMarkers.set(k, marker);
    this.undo(() => this.slamMarkers.delete(k));
    // The boss winds up for it.
    const boss = this.plan.enemies[m.boss];
    if (!boss.attacks) this.set(boss, "attacks", []);
    const a: Attack & Provisional = { w0: Math.max(m.wMark, wLand - 0.6), w: wLand, kind: "slam", dir: m.dir, provisional: true };
    this.add(boss.attacks!, a);
  }

  slam(rec: SlamRecord): void {
    this.emitter.slam(rec);
    const marker = [...this.slamMarkers.values()].find((x) => Math.abs(x.w0 - rec.wMark) < 1e-9);
    if (marker) {
      const old = marker.w1;
      marker.w1 = rec.wLand;
      this.undo(() => {
        marker.w1 = old;
      });
    } else this.add(this.plan.slams, { w0: rec.wMark, w1: rec.wLand, x: rec.x, y: rec.y, radius: rec.radius });
  }

  drawStart(id: number, w0: number, sRelease: number, dir: number): void {
    const e = this.plan.enemies[id];
    if (!e.attacks) this.set(e, "attacks", []);
    const a: Attack & Provisional = { w0, w: this.predictW(sRelease), kind: "shoot", dir, provisional: true };
    this.add(e.attacks!, a);
  }

  danceStart(id: number, sFrom: number, anim: string, alternate: boolean): void {
    this.set(this.plan.enemies[id], "dance", { w0: this.predictW(sFrom), w1: Infinity, anim, alternate, phase: 0 });
  }

  threat(t: Threat): void {
    this.add(this.plan.threats, { s: t.s, kind: t.kind, outcome: t.outcome });
    if (t.outcome !== "near-miss") return;
    this.add(this.plan.nearMisses, { s: t.s, w: t.w, kind: t.kind });
    this.add(this.hud.perfect, t.s);
    this.emitter.nearMiss(t);
  }

  boss(info: BossInfo): void {
    this.set(this.plan, "boss", { id: info.id, deathS: undefined });
    this.emitter.bossLand(info);
    const bar = this.emitter.bossBar(info, this.sch.blast?.s ?? this.ctx.duration) as Extract<UiEvent, { kind: "bossBar" }>;
    this.ui(bar);
    const before = this.bossState;
    this.bossState = { info, bar };
    this.undo(() => {
      this.bossState = before;
    });
  }

  bossHealth(s: number, frac: number): void {
    const b = this.bossState;
    if (!b) return;
    this.add(b.info.health, { s, frac });
    this.add(b.bar.health, [s, frac]);
  }

  bossDeath(s: number, killHit: number): void {
    const b = this.bossState;
    if (!b) return;
    this.set(this.plan.boss!, "deathS", s);
    this.set(b.bar, "until", s);
    this.emitter.bossKill(this.plan.hits[killHit]);
  }

  gem(g: GemRecord): void {
    // Until it arrives, a gem flies on its schedule as if nothing changes (a gem on the field waits).
    const ev = this.emitter.gem({ ...g, w1: Number.isFinite(g.s1) ? this.predictW(g.s1) : Infinity });
    this.add(this.gemsLive, ev);
    this.add(this.plan.events.gems, ev);
  }

  gemArrive(gem: number, w: number): void {
    const ev = this.gemsLive[gem];
    this.set(ev, "w1", w);
    if (this.arrivals.has(ev.s1)) return;
    this.arrivals.add(ev.s1);
    this.undo(() => this.arrivals.delete(ev.s1));
    if (!this.ctx.level.rules.fieldGems) {
      this.emitter.gemArrival(ev.s1, this.heroNow());
      return;
    }
    // One chime per sixteenth with pickups, the next degree of its run.
    const before = this.lastChime;
    const slot = Math.round(beatIndexAt(this.ctx.song, ev.s1) * 4);
    this.lastChime = { slot, degree: chimeDegree(before, slot) };
    this.undo(() => {
      this.lastChime = before;
    });
    this.emitter.gemArrival(ev.s1, this.heroNow(), this.lastChime.degree);
  }

  gemFly(gem: number, w: number, s1: number): void {
    const ev = this.gemsLive[gem];
    this.set(ev, "wT", w);
    this.set(ev, "s1", s1);
    this.set(ev, "w1", this.predictW(s1));
  }

  gemGrow(gem: number, value: number, big: boolean): void {
    const ev = this.gemsLive[gem];
    this.set(ev, "value", value);
    this.set(ev, "big", big);
  }

  vacuum(v: VacuumRecord): void {
    this.add(this.plan.vacuums!, { ...v });
  }

  vacuumTouch(i: number, s: number, w: number, gems: number): void {
    const v = this.plan.vacuums![i];
    this.set(v, "sTouch", s);
    this.set(v, "wTouch", w);
    this.set(v, "gems", gems);
    this.emitter.vacuumTouch(v);
  }

  xp(s: number, fill: number): void {
    this.add(this.hud.xpTimes, s);
    this.add(this.hud.xpFill, fill);
  }

  wave(cleave?: Cleave): void {
    this.nWaves++;
    this.undo(() => this.nWaves--);
    if (cleave && this.plan.cleaves) this.add(this.plan.cleaves, cleave);
  }

  waveHit(_wave: number, hit: number): void {
    this.emitter.waveBand(this.plan.hits[hit]);
  }

  formation(f: FormationRecord): void {
    this.add(this.plan.formations, { def: f.def, sRelease: f.sRelease, members: f.members });
  }

  zoneWave(z: ZoneWaveRecord): void {
    const op = this.plan.open;
    if (!op) return;
    const zones = this.plan.map.zones;
    const zoneIndex = (id: string) => Math.max(0, zones.findIndex((x) => x.id === id));
    const prev = op.waves.length ? op.waves[op.waves.length - 1].zone : op.firstZone;
    const sbi = beatIndexAt(this.ctx.song, z.s);
    const wv: ZoneWave = { s: z.s, w: z.w, x: z.x, y: z.y, sbi, prev, zone: zoneIndex(z.zone), endS: timeOfBeatIndex(this.ctx.song, sbi + WAVE_BANDS / 4) };
    this.add(op.waves, wv);
    this.emitter.zoneWave(z);
  }

  landmark(l: LandmarkRecord): void {
    const op = this.plan.open;
    const tm = this.ctx.input.landmarks?.[l.id] as TiledMap | undefined;
    if (!op || !tm) return;
    const st = stampLandmark(tm, l.x, l.y, this.propsInfo);
    const sbi = beatIndexAt(this.ctx.song, l.s);
    const bpb = this.ctx.idx.beatsPerBar[Math.max(1, Math.min(this.ctx.idx.barCount, Math.floor(sbi)))] ?? 2;
    this.add(op.landmarks, { id: l.id, s: l.s, w: l.w, x: l.x, y: l.y, props: st.props, clearRadius: st.clearRadius, rise: bpb * beatDurationAt(this.ctx, l.s) });
    this.emitter.landmark(l);
  }

  clock(s: number, w: number, between: boolean): void {
    const before = { s: this.s, w: this.w };
    this.s = s;
    this.w = w;
    this.undo(() => {
      this.s = before.s;
      this.w = before.w;
    });
    // Emission only at the simulation's own stops: a live frame between them must change nothing.
    if (between) return;
    // Song-owned flashes on strong accents, as the music passes them.
    const accents = this.ctx.song.accents;
    if (this.accentIdx < accents.length && accents[this.accentIdx].time <= s) {
      const from = this.accentIdx;
      while (this.accentIdx < accents.length && accents[this.accentIdx].time <= s) this.emitter.accent(accents[this.accentIdx++]);
      this.undo(() => {
        this.accentIdx = from;
      });
    }
    // Weapon hits whose fates are all in.
    const wh = this.pendingWeaponHits;
    while (wh.length > 0 && wh[0].s < s) {
      const h = wh[0];
      wh.shift();
      this.undo(() => wh.unshift(h));
      this.emitter.weaponHit(h, this.caught.has(h.id));
    }
    // Kill groups, then the sounds of each closed sixteenth-note slot.
    for (const [slot, k] of [...this.pendingKills]) {
      if (k.end + KILL_SETTLE > s) continue;
      this.pendingKills.delete(slot);
      this.undo(() => this.pendingKills.set(slot, k));
      this.emitter.killSound(k);
    }
    for (const [slot, list] of [...this.pendingSounds]) {
      if (timeOfBeatIndex(this.ctx.song, (slot + 0.5) / 4) + SLOT_SETTLE > s) continue;
      this.pendingSounds.delete(slot);
      this.undo(() => this.pendingSounds.set(slot, list));
      const kept = budgetSlot([...list]);
      for (const snd of kept) this.insertSound(snd);
      this.closedSlots.set(slot, kept.length);
      this.undo(() => this.closedSlots.delete(slot));
    }
  }

  /** The finale: the blast's flash and sound, the end card's cue and the average groove. */
  private finaleBlast(h: Hit): void {
    this.blastSeen = h.s;
    this.undo(() => {
      this.blastSeen = undefined;
    });
    this.emitter.blast(h.s, this.heroNow());
    const end = timeOfBeatIndex(this.ctx.song, beatIndexAt(this.ctx.song, h.s) + 1) + 1.6;
    this.ui({ kind: "end", s: end });
    let gSum = 0;
    let gN = 0;
    for (const sec of this.ctx.sections) {
      if (sec.level.movement !== "steps") continue;
      for (let t = Math.max(sec.s0, this.playerFrom); t < Math.min(sec.s1, h.s); t += 0.25) {
        gSum += grooveAt(this.grooveSeries, t);
        gN++;
      }
    }
    this.set(this.plan.stats, "averageGroove", gN > 0 ? gSum / gN : 0);
  }

  // ------------------------------------------------------------------ EmitSink

  effect(e: EffectEvent): void {
    const i = this.plan.events.effects.length;
    this.add(this.plan.events.effects, e);
    if (this.decalRecipes.has(e.recipe)) {
      // Kept in world-time order.
      const idx = this.decalIdx;
      let at = idx.length;
      while (at > 0 && this.plan.events.effects[idx[at - 1]].w > e.w) at--;
      idx.splice(at, 0, i);
      this.undo(() => {
        idx.splice(idx.indexOf(i), 1);
      });
    }
  }

  sound(snd: SoundEvent): void {
    const slot = soundSlot(this.ctx, snd.s);
    const closed = this.closedSlots.get(slot);
    if (closed !== undefined) {
      // A late sound for a closed slot gets in if the slot has a voice to spare.
      if (closed >= VOICE_BUDGET) return;
      this.closedSlots.set(slot, closed + 1);
      this.undo(() => this.closedSlots.set(slot, closed));
      this.insertSound(snd);
      return;
    }
    let list = this.pendingSounds.get(slot);
    if (!list) {
      list = [];
      this.pendingSounds.set(slot, list);
      this.undo(() => this.pendingSounds.delete(slot));
    }
    this.add(list, snd);
  }

  ui(u: UiEvent): void {
    const list = this.plan.events.ui;
    let at = list.length;
    while (at > 0 && list[at - 1].s > u.s) at--;
    list.splice(at, 0, u);
    this.undo(() => {
      list.splice(list.lastIndexOf(u), 1);
    });
  }

  get effectCount(): number {
    return this.plan.events.effects.length;
  }

  get soundCount(): number {
    return this.plan.events.sounds.length;
  }

  /** Final sounds stay sorted by song time (then priority), for the audio scheduler. */
  private insertSound(snd: SoundEvent): void {
    const list = this.plan.events.sounds;
    let at = list.length;
    while (at > 0 && (list[at - 1].s > snd.s || (list[at - 1].s === snd.s && list[at - 1].priority > snd.priority))) at--;
    list.splice(at, 0, snd);
    this.undo(() => {
      list.splice(list.lastIndexOf(snd), 1);
    });
  }

  // ------------------------------------------------------------------ PlanIndex

  enemy(id: number): EnemyRecord {
    return this.plan.enemies[id];
  }

  enemiesAt(w: number): ArrayLike<number> {
    // Forget enemies long gone: no rewind reaches back that far.
    const ids = this.activeIds;
    let j = 0;
    for (let i = 0; i < ids.length; i++) {
      const e = this.plan.enemies[ids[i]];
      if (Number.isFinite(e.deathW) && e.deathW + DEATH_ANIM < w - 2) continue;
      ids[j++] = ids[i];
    }
    ids.length = j;
    return ids;
  }

  /** After a rewind far back (a restarted section), rebuild the list of enemies to draw. */
  rebuildActive(): void {
    this.activeIds.length = 0;
    for (const e of this.plan.enemies) this.activeIds.push(e.id);
    this.effFirst = 0;
    this.gemFirst = 0;
  }

  effectsAt(w: number): ArrayLike<number> {
    const fx = this.plan.events.effects;
    if (w < this.effLastW - 0.5) this.effFirst = 0;
    this.effLastW = w;
    while (this.effFirst < fx.length && fx[this.effFirst].w < w - EFFECT_WINDOW) this.effFirst++;
    const out = this.effOut;
    out.length = 0;
    for (let i = this.effFirst; i < fx.length; i++) out.push(i);
    return out;
  }

  decals(): ArrayLike<number> {
    return this.decalIdx;
  }

  gemsAt(w: number): GemEvent[] {
    const gems = this.gemsLive;
    // (Gems on the field wait until picked up: only those that have arrived are behind.)
    if (this.ctx.level.rules.fieldGems) while (this.gemFirst < gems.length && gems[this.gemFirst].w1 < w - 1) this.gemFirst++;
    else while (this.gemFirst < gems.length && gems[this.gemFirst].w0 < w - 6) this.gemFirst++;
    const out: GemEvent[] = [];
    for (let i = this.gemFirst; i < gems.length; i++) {
      const g = gems[i];
      if (w >= g.w0 && w <= g.w1) out.push(g);
    }
    return out;
  }

  worldBeatIndex(w: number): number {
    return beatIndexIn(this.plan.worldBeats, w);
  }

  blastS(): number | undefined {
    return this.blastSeen;
  }

  // ------------------------------------------------------------------ checks

  /** The section of the song at song time s. */
  sectionAt(s: number) {
    return this.ctx.sections[sectionIndexAt(this.ctx, s)];
  }

  /** A hash of everything recorded: two recordings of the same play are equal. */
  hash(): string {
    return hash32(...Object.entries(this.hashParts()).flat()).toString(16).padStart(8, "0");
  }

  /** Hashes of each part of the recording (a failing comparison names the part that differs). */
  hashParts(): Record<string, string> {
    const parts: Record<string, string> = {};
    let h = 0;
    const mix = (...v: (number | string)[]) => {
      h = hash32(h, ...v.map((x) => (typeof x === "number" ? Math.round(x * 1e4) : x)));
    };
    const part = (name: string, fill: () => void) => {
      h = 0x2f7a11;
      fill();
      parts[name] = (h >>> 0).toString(16).padStart(8, "0");
    };
    const p = this.plan;
    part("warp", () => this.breaks.forEach((b) => mix(b.s, b.w, b.rate)));
    part("hero", () => {
      for (let k = 0; k < this.hx.len; k++) mix(this.hx.buf[k], this.hy.buf[k], this.hz.buf[k], this.hFacing.buf[k], this.hAnim.buf[k], this.hFrame.buf[k]);
    });
    part("camera", () => {
      for (let k = 0; k < this.cx.len; k++) mix(this.cx.buf[k], this.cy.buf[k], this.cz.buf[k]);
    });
    part("enemies", () => {
      for (const e of p.enemies) mix(e.type, e.spawnW, Number.isFinite(e.deathW) ? e.deathW : -1, e.fates.length, e.sampleCount, e.recycled ? 1 : 0);
    });
    part("enemy samples", () => {
      for (const e of p.enemies) for (let i = 0; i < e.sampleCount * 2; i++) mix(e.samples![i]);
    });
    part("attacks and dances", () => {
      for (const e of p.enemies) mix(JSON.stringify(e.attacks ?? []), JSON.stringify(e.dance ?? {}));
    });
    part("hits", () => p.hits.forEach((x) => mix(x.s, x.w, x.what, x.damage)));
    part("inputs", () => p.inputs.forEach((x) => mix(x.kind, x.key, x.s, x.rating ?? "", x.extra ? 1 : 0, x.offbeat ?? "", x.stumble ? 1 : 0)));
    part("swings", () => p.swings.forEach((x) => mix(x.s, x.dir, x.hit)));
    part("effects", () => p.events.effects.forEach((x) => mix(x.recipe, x.s, x.w, x.x, x.y, x.seed)));
    part("sounds", () => p.events.sounds.forEach((x) => mix(x.sample, x.s, x.gain, x.pan, x.rate)));
    part("ui", () => p.events.ui.forEach((x) => mix(x.kind, x.s)));
    part("gems", () => p.events.gems.forEach((x) => mix(x.w0, x.w1, x.s1, x.wT ?? -1, x.value, x.big ? 1 : 0)));
    part("vacuums", () => (p.vacuums ?? []).forEach((v) => mix(v.s, v.w, v.x, v.y, v.sTouch ?? -1, v.wTouch ?? -1, v.gems ?? -1)));
    part("weapons", () => p.weapons.forEach((g) => mix(g.id, g.s)));
    part("upgrades", () =>
      (p.upgrades ?? []).forEach((u) => mix(u.s, u.taken, ...u.offers.flatMap((o) => [o.kind, o.id]), u.w ?? -1, ...(u.orbs ?? []).flatMap((o) => [o.x, o.y]), u.sTaken ?? -1, u.wTaken ?? -1, u.sApply ?? -1, u.walked ? 1 : 0)),
    );
    part("projectiles", () => p.projectiles.forEach((x) => mix(x.w0, x.w1, x.x1, x.y1)));
    part("slams", () => p.slams.forEach((x) => mix(x.w0, x.w1, x.x, x.y)));
    part("chains", () => p.chains.forEach((c) => c.points.forEach((q) => mix(q.w, q.x, q.y))));
    part("groove", () => {
      for (let i = 0; i < this.grooveSeries.s.length; i++) mix(this.grooveSeries.s[i], this.grooveSeries.v[i]);
    });
    part("tier", () => {
      for (let i = 0; i < this.tierSeries.s.length; i++) mix(this.tierSeries.s[i], this.tierSeries.v[i]);
    });
    part("health", () => {
      for (let i = 0; i < this.healthSeries.s.length; i++) mix(this.healthSeries.s[i], this.healthSeries.v[i]);
    });
    part("xp", () => {
      for (let i = 0; i < this.hud.xpTimes.length; i++) mix(this.hud.xpTimes[i], this.hud.xpFill[i]);
    });
    part("hud", () => mix(this.hud.kills.length, this.hud.perfect.length, p.weapons.length, p.threats.length));
    part("open field", () => {
      for (const wv of p.open?.waves ?? []) mix(wv.s, wv.x, wv.y, wv.zone);
      for (const l of p.open?.landmarks ?? []) mix(l.id, l.s, l.x, l.y);
    });
    return parts;
  }
}
