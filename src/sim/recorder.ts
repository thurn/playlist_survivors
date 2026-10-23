/**
 * The recording side of the simulation. The simulation never reads what it
 * has recorded: it reports each event to a Recorder, which owns the data.
 * BatchRecorder builds the demo's SimResult; play mode's live recording grows
 * as play goes on and can be rewound for rollback.
 */
import type { WarpBreak } from "../core/warp.ts";
import type { Chain, EnemyRecord, Fate, HeroTrack, Hit, PlanInput, PlanSwing, UpgradeRecord } from "../plan/types.ts";
import { CameraRecording } from "../planner/camera.ts";

export interface Landing {
  s: number;
  w: number;
  x: number;
  y: number;
  /** A stomp (play mode's strides): both feet stamp with a ring of dust, at the press, without a sound. */
  stomp?: boolean;
}

export interface ArrowRecord {
  sRelease: number;
  wRelease: number;
  sArrive: number;
  wArrive: number;
  x0: number;
  y0: number;
  /** Where it was aimed: the hero's hurtbox at the release. */
  ax: number;
  ay: number;
  /** End of the drawn flight. */
  x1: number;
  y1: number;
  w1: number;
  outcome: "near-miss" | "touch" | "miss";
}

export interface SlamRecord {
  sMark: number;
  wMark: number;
  sLand: number;
  wLand: number;
  x: number;
  y: number;
  radius: number;
  outcome: "near-miss" | "touch" | "miss";
}

export type Threat = { s: number; w: number; kind: "arrow" | "slam"; outcome: "near-miss" | "touch" | "miss"; x: number; y: number };
export type BossInfo = { id: number; landS: number; x: number; y: number; deathS?: number; killHit?: number; health: { s: number; frac: number }[] };
/**
 * A gem: dropped at (x0, y0) at world time w0, arriving at the hero at song and world time s1, w1.
 * Gems on the field (rules.fieldGems) wait (s1 and w1 Infinity) until the hero's pickup radius
 * reaches them; they take off at world time wT.
 */
export type GemRecord = { w0: number; x0: number; y0: number; s1: number; w1: number; value: number; big: boolean; enemy: number; wT?: number };
/** A vacuum (rules.fieldGems): dropped at (x, y) at song and world time s, w; touched at sTouch, wTouch, sending `gems` gems in view. */
export type VacuumRecord = { s: number; w: number; x: number; y: number; sTouch?: number; wTouch?: number; gems?: number };
export type FormationRecord = { def: string; sStart: number; sRelease: number; members: number[] };
/** The open field: a transform wave sweeps out from the hero on a section's first downbeat. */
export type ZoneWaveRecord = { s: number; w: number; x: number; y: number; zone: string };
/** The open field: a landmark rises centred on the hero. */
export type LandmarkRecord = { id: string; s: number; w: number; x: number; y: number };
/**
 * What a key event did in the simulation, and why (play mode's timing log).
 * A rollback runs a press again, so its latest trace is the one that counts.
 */
export interface PressTrace {
  /** The key event's id (KeyEvent.id), when it has one. */
  id?: number;
  s: number;
  w: number;
  kind: "press" | "release";
  key: string;
  /** The section's label and movement at the press. */
  section: string;
  movement: "steps" | "free";
  /**
   * extra: a step for a grid time that already had its rated step (unrated, arming nothing);
   * stumble: a second extra press for one grid time (no step; a swing is lost and the groove drops);
   * ignored: a later extra press for that grid time (it does nothing);
   * bend: a diagonal's second key bent the step; step: a new step (rated, and maybe arming a swing);
   * walk: free movement (held keys walk); release: a key went up.
   * With strides every press strides, a stumble and an ignored press too (they keep their names for
   * what they did to the groove and the swings); stride: a new stride (in step movement rated, and
   * maybe arming a swing, as a step; in free movement unrated, with a walk after it while the key is held);
   * stomp: Space, a press without moving; rock: the second of two opposite keys, which made the first
   * one's stride a rock step back (the pair is one stomp, timed and rated by the first).
   */
  outcome: "extra" | "stumble" | "ignored" | "bend" | "step" | "stride" | "stomp" | "rock" | "walk" | "release";
  /** The nearest grid time (index and song time), and the press's offset from it (song seconds). */
  gridIndex?: number;
  gridS?: number;
  delta?: number;
  rating?: string;
  accuracy?: number;
  /** Extra presses: the song time of the press that took this grid time's step. */
  stepS?: number;
  /** Extra presses: the offset from the half-way point between grid times, and its rating there. */
  offbeatDelta?: number;
  offbeat?: string;
  /**
   * Strides: the length of the stride the press started (a bend keeps the stride's), the stride
   * budget left after it (px), and the ground an interrupted stride gave back to the budget first.
   */
  stride?: number;
  budget?: number;
  returned?: number;
  /** The press cut a hit-stop short. */
  cutHitStop?: boolean;
  /** The previous step had not landed yet (this one starts from where the hero was). */
  midStep?: boolean;
  grooveBefore?: number;
  grooveAfter?: number;
  /**
   * Press classes (play mode): partner, offbeat, counted, double (a double tap) or stray; with
   * `rating` (loose beyond Good for a counted press), `delta` and the tier after the press.
   */
  class?: string;
  tier?: number;
  /** The swing it armed: the strike's song time and whether it is a heavy cleave. */
  strikeS?: number;
  heavy?: boolean;
  /** No swing armed: past the last grid slot, the slot already had a swing, or the finale blast ends the sword's routine. */
  noSwing?: "no-slot" | "slot-taken" | "blast";
  /** The slot it would arm (even when it did not), and that slot's accent. */
  slotS?: number;
  accent?: number;
}

/** A tier change's chime: the grid time it sounds on, whether the tier rose, and the world time of the change. */
/** A tier change's chime: its song time (a grid time), up or down, and the change's world and song time. */
/**
 * A cleave (rules.pressCleaves): a counted press of Great or better on an eligible accent launched
 * its shockwave. The accent's song time, the press's, and the first band's (the eighth after the
 * accent, or a sixteenth later when the press came too late for it).
 */
export interface Cleave {
  s: number;
  press: number;
  band0: number;
}

export type TierChime = { s: number; up: boolean; w: number; at: number };

export type Attack = NonNullable<EnemyRecord["attacks"]>[number];
export type Dance = NonNullable<EnemyRecord["dance"]>;

export interface SimResult {
  warp: WarpBreak[];
  hero: HeroTrack;
  camera: CameraRecording;
  hits: Hit[];
  enemies: EnemyRecord[];
  enemySamples: Float32Array;
  swings: PlanSwing[];
  inputs: PlanInput[];
  groove: { s: number[]; v: number[] };
  /** Press classes (play mode): the tier after each change; it starts at 0. */
  tier: { s: number[]; v: number[] };
  tierChimes: TierChime[];
  /** The hero's health (fraction of its maximum) after each change; it starts full. */
  health: { s: number[]; v: number[] };
  landings: Landing[];
  weaponsGranted: { id: string; s: number }[];
  /** Song times each weapon fired. */
  weaponFires: { weapon: string; s: number }[];
  chains: Chain[];
  hitStops: [number, number][];
  blastS?: number;
  /** Song time the simulation ended. */
  endS: number;
  arrows: ArrowRecord[];
  slams: SlamRecord[];
  /** Every threat (arrow or slam) and how it ended. */
  threats: Threat[];
  boss?: BossInfo;
  /** Gems: drop (world time, place) and arrival at the hero (song and world time). */
  gems: GemRecord[];
  /** Gems on the field: the vacuums that follow set pieces. */
  vacuums: VacuumRecord[];
  /** The upgrade pool: each level-up's offers and the one taken. */
  upgrades: UpgradeRecord[];
  /** The XP bar after each change (fraction of the next level-up's requirement). */
  xp: { s: number; fill: number }[];
  /** Cleave shockwaves: one hit per band (with press cleaves, the cleave that launched it). */
  waves: ({ hits: number[] } & Partial<Cleave>)[];
  /** Formation releases (song time) and their members. */
  formations: FormationRecord[];
  /** The open field's transform waves and landmarks. */
  zoneWaves: ZoneWaveRecord[];
  landmarks: LandmarkRecord[];
}

/**
 * Receives every simulation event, in the order it happens. Objects handed to
 * the recorder belong to it: the simulation never touches them again.
 */
export interface Recorder {
  /** A warp break: appended, or replacing the last one (a rate change at the same song time). */
  warp(b: WarpBreak, replaceLast: boolean): void;
  /** One hero sample (HERO_RATE per world-second) and its camera sample. */
  hero(x: number, y: number, z: number, facing: number, anim: number, frame: number): void;
  camera(x: number, y: number, zoom: number): void;
  /** A new enemy (its id is the next index); its first sample follows. */
  spawn(rec: EnemyRecord): void;
  sample(id: number, x: number, y: number): void;
  fate(id: number, fate: Fate): void;
  death(id: number, w: number, recycled: boolean): void;
  attack(id: number, a: Attack): void;
  dance(id: number, d: Dance): void;
  hit(h: Hit): void;
  swing(sw: PlanSwing): void;
  input(i: PlanInput): void;
  groove(s: number, v: number): void;
  /** The tier after a change at song time s (world time w), and the grid time its chime sounds on. */
  tier(s: number, w: number, tier: number, soundS: number): void;
  /** The hero's health (fraction of its maximum) after a change. */
  health(s: number, frac: number): void;
  landing(l: Landing): void;
  weapon(id: string, s: number): void;
  fire(weapon: string, s: number): void;
  /** A chain-lightning firing starts at a point; its jumps follow. */
  chain(p: { w: number; x: number; y: number }): void;
  chainPoint(chain: number, p: { w: number; x: number; y: number }): void;
  hitStop(s0: number, s1: number): void;
  /** A press ends the latest hit-stop early, at song time s. */
  hitStopCut(s: number): void;
  arrow(rec: ArrowRecord): void;
  arrowEnd(arrow: number, end: Pick<ArrowRecord, "wArrive" | "outcome" | "x1" | "y1" | "w1">): void;
  slam(rec: SlamRecord): void;
  threat(t: Threat): void;
  boss(info: BossInfo): void;
  bossHealth(s: number, frac: number): void;
  bossDeath(s: number, killHit: number): void;
  gem(g: GemRecord): void;
  gemArrive(gem: number, w: number): void;
  /** Gems on the field: a gem takes off for the hero at world time w, to arrive at song time s1. */
  gemFly(gem: number, w: number, s1: number): void;
  /** Gems on the field, past the cap: a death adds its value to the nearest gem, which grows. */
  gemGrow(gem: number, value: number, big: boolean): void;
  vacuum(v: VacuumRecord): void;
  vacuumTouch(i: number, s: number, w: number, gems: number): void;
  /** The upgrade pool: a level-up's offers and the one taken. */
  upgrade(u: UpgradeRecord): void;
  xp(s: number, fill: number): void;
  /** A cleave shockwave starts (with press cleaves, the accent's cleave that launched it); its band hits follow. */
  wave(cleave?: Cleave): void;
  waveHit(wave: number, hit: number): void;
  formation(f: FormationRecord): void;
  zoneWave(z: ZoneWaveRecord): void;
  landmark(l: LandmarkRecord): void;
  /**
   * Optional, for a live recording that is shown while it grows: the clock has moved to (s, w)
   * (`between` the loop's own stops: a live frame);
   * and things that will resolve later began (a slam marker, an archer's draw, a formation's dance).
   */
  clock?(s: number, w: number, between: boolean): void;
  slamMark?(k: number, m: { x: number; y: number; sMark: number; wMark: number; sLand: number; boss: number; dir: number }): void;
  drawStart?(id: number, w0: number, sRelease: number, dir: number): void;
  danceStart?(id: number, sFrom: number, anim: string, alternate: boolean): void;
  /** Optional, for play mode's timing log: what a key event did, and why. */
  press?(t: PressTrace): void;
}

/** Builds the demo's SimResult, exactly as the simulation used to assemble it. */
export class BatchRecorder implements Recorder {
  readonly breaks: WarpBreak[] = [];
  private hx: number[] = [];
  private hy: number[] = [];
  private hz: number[] = [];
  private hFacing: number[] = [];
  private hAnim: number[] = [];
  private hFrame: number[] = [];
  readonly cam = new CameraRecording();
  readonly hits: Hit[] = [];
  readonly enemies: EnemyRecord[] = [];
  private samples: number[][] = [];
  readonly swings: PlanSwing[] = [];
  readonly inputs: PlanInput[] = [];
  readonly grooveS: number[] = [0];
  readonly grooveV: number[] = [0];
  readonly tierS: number[] = [0];
  readonly tierV: number[] = [0];
  readonly tierChimes: TierChime[] = [];
  readonly healthS: number[] = [0];
  readonly healthV: number[] = [1];
  readonly landings: Landing[] = [];
  readonly weaponsGranted: { id: string; s: number }[] = [];
  readonly weaponFires: { weapon: string; s: number }[] = [];
  readonly chains: Chain[] = [];
  readonly hitStops: [number, number][] = [];
  readonly arrows: ArrowRecord[] = [];
  readonly slams: SlamRecord[] = [];
  readonly threats: Threat[] = [];
  bossInfo?: BossInfo;
  readonly gems: GemRecord[] = [];
  readonly vacuums: VacuumRecord[] = [];
  readonly upgrades: UpgradeRecord[] = [];
  readonly xpSeries: { s: number; fill: number }[] = [{ s: 0, fill: 0 }];
  readonly waves: ({ hits: number[] } & Partial<Cleave>)[] = [];
  readonly formations: FormationRecord[] = [];
  readonly zoneWaves: ZoneWaveRecord[] = [];
  readonly landmarks: LandmarkRecord[] = [];

  warp(b: WarpBreak, replaceLast: boolean): void {
    if (replaceLast) this.breaks[this.breaks.length - 1] = b;
    else this.breaks.push(b);
  }
  hero(x: number, y: number, z: number, facing: number, anim: number, frame: number): void {
    this.hx.push(x);
    this.hy.push(y);
    this.hz.push(z);
    this.hFacing.push(facing);
    this.hAnim.push(anim);
    this.hFrame.push(frame);
  }
  camera(x: number, y: number, zoom: number): void {
    this.cam.push(x, y, zoom);
  }
  spawn(rec: EnemyRecord): void {
    this.enemies.push(rec);
    this.samples.push([]);
  }
  sample(id: number, x: number, y: number): void {
    this.samples[id].push(x, y);
  }
  fate(id: number, fate: Fate): void {
    this.enemies[id].fates.push(fate);
  }
  death(id: number, w: number, recycled: boolean): void {
    const rec = this.enemies[id];
    rec.deathW = w;
    if (recycled) rec.recycled = true;
  }
  attack(id: number, a: Attack): void {
    (this.enemies[id].attacks ??= []).push(a);
  }
  dance(id: number, d: Dance): void {
    this.enemies[id].dance = d;
  }
  hit(h: Hit): void {
    this.hits.push(h);
  }
  swing(sw: PlanSwing): void {
    this.swings.push(sw);
  }
  input(i: PlanInput): void {
    this.inputs.push(i);
  }
  groove(s: number, v: number): void {
    this.grooveS.push(s);
    this.grooveV.push(v);
  }
  tier(s: number, w: number, tier: number, soundS: number): void {
    this.tierChimes.push({ s: soundS, up: tier > this.tierV[this.tierV.length - 1], w, at: s });
    this.tierS.push(s);
    this.tierV.push(tier);
  }
  health(s: number, frac: number): void {
    this.healthS.push(s);
    this.healthV.push(frac);
  }
  landing(l: Landing): void {
    this.landings.push(l);
  }
  weapon(id: string, s: number): void {
    this.weaponsGranted.push({ id, s });
  }
  fire(weapon: string, s: number): void {
    this.weaponFires.push({ weapon, s });
  }
  chain(p: { w: number; x: number; y: number }): void {
    this.chains.push({ points: [p] });
  }
  chainPoint(chain: number, p: { w: number; x: number; y: number }): void {
    this.chains[chain].points.push(p);
  }
  hitStop(s0: number, s1: number): void {
    this.hitStops.push([s0, s1]);
  }
  hitStopCut(s: number): void {
    this.hitStops[this.hitStops.length - 1][1] = s;
  }
  arrow(rec: ArrowRecord): void {
    this.arrows.push(rec);
  }
  arrowEnd(arrow: number, end: Pick<ArrowRecord, "wArrive" | "outcome" | "x1" | "y1" | "w1">): void {
    Object.assign(this.arrows[arrow], end);
  }
  slam(rec: SlamRecord): void {
    this.slams.push(rec);
  }
  threat(t: Threat): void {
    this.threats.push(t);
  }
  boss(info: BossInfo): void {
    this.bossInfo = info;
  }
  bossHealth(s: number, frac: number): void {
    this.bossInfo!.health.push({ s, frac });
  }
  bossDeath(s: number, killHit: number): void {
    this.bossInfo!.deathS = s;
    this.bossInfo!.killHit = killHit;
  }
  gem(g: GemRecord): void {
    this.gems.push(g);
  }
  gemArrive(gem: number, w: number): void {
    this.gems[gem].w1 = w;
  }
  gemFly(gem: number, w: number, s1: number): void {
    this.gems[gem].wT = w;
    this.gems[gem].s1 = s1;
  }
  gemGrow(gem: number, value: number, big: boolean): void {
    this.gems[gem].value = value;
    this.gems[gem].big = big;
  }
  vacuum(v: VacuumRecord): void {
    this.vacuums.push(v);
  }
  vacuumTouch(i: number, s: number, w: number, gems: number): void {
    this.vacuums[i].sTouch = s;
    this.vacuums[i].wTouch = w;
    this.vacuums[i].gems = gems;
  }
  upgrade(u: UpgradeRecord): void {
    this.upgrades.push(u);
  }
  xp(s: number, fill: number): void {
    this.xpSeries.push({ s, fill });
  }
  wave(cleave?: Cleave): void {
    this.waves.push({ hits: [], ...cleave });
  }
  waveHit(wave: number, hit: number): void {
    this.waves[wave].hits.push(hit);
  }
  formation(f: FormationRecord): void {
    this.formations.push(f);
  }
  zoneWave(z: ZoneWaveRecord): void {
    this.zoneWaves.push(z);
  }
  landmark(l: LandmarkRecord): void {
    this.landmarks.push(l);
  }

  /** The finished recording. */
  result(end: { endS: number; blastS?: number; animNames: string[]; heroRate: number }): SimResult {
    // Pack enemy samples.
    let total = 0;
    for (const sm of this.samples) total += sm.length;
    const enemySamples = new Float32Array(total);
    let off = 0;
    this.enemies.forEach((rec, i) => {
      const sm = this.samples[i];
      enemySamples.set(sm, off);
      rec.sampleOffset = off / 2;
      rec.sampleCount = sm.length / 2;
      off += sm.length;
    });
    const hero: HeroTrack = {
      rate: end.heroRate,
      w0: 0,
      x: Float32Array.from(this.hx),
      y: Float32Array.from(this.hy),
      z: Float32Array.from(this.hz),
      facing: Uint8Array.from(this.hFacing),
      anim: Uint8Array.from(this.hAnim),
      frame: Uint8Array.from(this.hFrame),
      animNames: end.animNames,
      trail: new Float32Array(this.hx.length),
    };
    return {
      warp: this.breaks,
      hero,
      camera: this.cam,
      hits: this.hits,
      enemies: this.enemies,
      enemySamples,
      swings: this.swings,
      inputs: this.inputs,
      groove: { s: this.grooveS, v: this.grooveV },
      tier: { s: this.tierS, v: this.tierV },
      tierChimes: this.tierChimes,
      health: { s: this.healthS, v: this.healthV },
      landings: this.landings,
      weaponsGranted: this.weaponsGranted,
      weaponFires: this.weaponFires,
      chains: this.chains,
      hitStops: this.hitStops,
      blastS: end.blastS,
      endS: end.endS,
      arrows: this.arrows,
      slams: this.slams,
      threats: this.threats,
      boss: this.bossInfo,
      gems: this.gems,
      vacuums: this.vacuums,
      upgrades: this.upgrades,
      xp: this.xpSeries,
      waves: this.waves,
      formations: this.formations,
      zoneWaves: this.zoneWaves,
      landmarks: this.landmarks,
    };
  }
}
