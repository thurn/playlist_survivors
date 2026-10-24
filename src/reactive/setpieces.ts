/**
 * Reactive mode's set pieces (stage 6; docs/design/reactive-audio.md, "Stage 6: Set Pieces"): when
 * the music triggers each of play mode's set pieces, in the schedule's terms, from the belief stream
 * alone. Each starts on the effective downbeat after its trigger is known, never within `gapS` of
 * another's start; when two would start together, the boss comes first, then a ring, a stampede, a
 * formation and volleys. Whether a set piece starts depends only on stream entries known by its
 * trigger. Open-ended parts are known later: a ring's close starts at its `kClose` and a formation's
 * release at its `sRelease`, and the simulation reads them only from then on.
 */
import type { Director } from "../content/schema.ts";
import { detectedSections, type DetectedSection } from "./sections.ts";
import { DOWNBEAT_NOTICE, effectiveDownbeat, evidenceAt, tickIndexAt, type ReactiveStream, type TickLevel } from "./stream.ts";

type SetPieces = NonNullable<Director["setPieces"]>;

export interface ReactiveRing {
  s: number;
  /** When the close starts (its trigger's knownAt) and the effective downbeat it ends on. */
  kClose: number;
  sClose: number;
  enemy: string;
  count: number;
  gaps: number;
}

export interface ReactiveFormation {
  def: string;
  sStart: number;
  /** When it releases (a committed beat, or a strong onset's knownAt for the kick line). */
  sRelease: number;
}

export interface ReactiveBoss {
  type: string;
  sEnter: number;
  slams: { sMark: number; sLand: number }[];
  /** When it retreats off the field if the finale has not killed it by then. */
  sRetreat: number;
  landmark?: string;
}

export interface ReactiveSetPieces {
  rings: ReactiveRing[];
  stampedes: { s: number; enemy: string; count: number; rows: number }[];
  formations: ReactiveFormation[];
  volleys: { sDraw: number; sRelease: number; sArrive: number; archers: number; group: number }[];
  archerGroups: { s: number; archers: number; until: number }[];
  boss?: ReactiveBoss;
  finale?: ReactiveFinale;
  /** Every set piece's start, in order (the check's and the parity report's). */
  starts: { kind: SetPieceKind; s: number }[];
}

export type SetPieceKind = "boss" | "ring" | "stampede" | "wedge" | "kick-line" | "volleys";

/** A stretch with loud ticks under this share still counts as not loud (for a stampede). */
const QUIET_BLIP = 0.05;
/** The boss drops in this long before it lands (the simulation's bossSpawn). */
const BOSS_DROP_S = 0.55;
/** How long the loud, clear stretch must have been over before another volley group can start. */
const VOLLEY_REARM_S = 4;

const PRIORITY: Record<SetPieceKind, number> = { boss: 0, ring: 1, stampede: 2, wedge: 3, "kick-line": 3, volleys: 4 };

export interface ReactiveFinale {
  s: number;
  blast: string;
  landmark?: string;
  swell: number;
  swellS: number;
  /** The run's end: `epilogueS` after the finale. */
  endS: number;
}

/** The level of the latest tick at or before s (medium before the first, or without levels). */
function levelAt(stream: ReactiveStream, s: number): TickLevel {
  const i = tickIndexAt(stream, s);
  return (i >= 0 ? stream.ticks[i].level : undefined) ?? "medium";
}

/** The committed beat a time falls on or after (its index), or -1 past the last. */
function beatAtOrAfter(stream: ReactiveStream, t: number): number {
  const i = stream.beats.findIndex((b) => b.t >= t - 1e-6);
  return i;
}

/**
 * The finale: on the first candidate ending known within the last `windowS` of the audio (whose
 * length is known apart from the stream), or at the audio's end; once.
 */
const finaleMemo = new WeakMap<ReactiveStream, Map<string, ReactiveFinale | undefined>>();

export function reactiveFinale(stream: ReactiveStream, d: Director, knownLength: number): ReactiveFinale | undefined {
  const f = d.setPieces?.finale;
  if (!f) return undefined;
  let memo = finaleMemo.get(stream);
  if (!memo) finaleMemo.set(stream, (memo = new Map()));
  const key = `${knownLength}|${JSON.stringify(f)}`;
  if (memo.has(key)) return memo.get(key);
  const out = finaleOf(stream, f, knownLength);
  memo.set(key, out);
  return out;
}

function finaleOf(stream: ReactiveStream, f: NonNullable<SetPieces["finale"]>, knownLength: number): ReactiveFinale {
  const end = stream.structure?.ends.find((e) => e.knownAt >= knownLength - f.windowS - 1e-9 && e.knownAt <= knownLength + 1e-9);
  const s = end ? end.knownAt : knownLength;
  return { s, blast: f.blast, ...(f.landmark ? { landmark: f.landmark } : {}), swell: f.swell, swellS: f.swellS, endS: s + f.epilogueS };
}

/**
 * When a reactive run's end card shows: just after the audio ends, or once the finale's epilogue
 * has played out, whichever is later.
 */
export function reactiveRunEnd(stream: ReactiveStream, d: Director | undefined, knownLength: number, duration: number, delay: number): number {
  const f = d ? reactiveFinale(stream, d, knownLength) : undefined;
  return Math.max(duration + delay, f?.endS ?? 0);
}

interface Candidate {
  kind: SetPieceKind;
  s: number;
  /** Fills in the set piece once it is accepted; false if it adds nothing (a once-only one already taken). */
  accept: (out: ReactiveSetPieces) => boolean;
}

/** A formation's entrance, and (released after a number of beats) its beats before release. */
export type FormationBeats = (id: string) => { entranceBeats: number; releaseBeats?: number };

/** The reactive level's set pieces on a stream, up to the run's end. */
export function reactiveSetPieces(stream: ReactiveStream, d: Director, knownLength: number, endS: number, formationBeats: FormationBeats): ReactiveSetPieces {
  const out: ReactiveSetPieces = { rings: [], stampedes: [], formations: [], volleys: [], archerGroups: [], starts: [] };
  const sp = d.setPieces;
  if (!sp) return out;
  const finale = reactiveFinale(stream, d, knownLength);
  if (finale) out.finale = finale;
  // Nothing new starts after the finale.
  const until = Math.min(endS, finale?.s ?? Infinity);
  const beats = stream.beats;
  const gate = stream.params.gate;
  const downbeatAfter = (k: number, notice = 0): number | undefined => {
    const i = effectiveDownbeat(stream, k + notice);
    return i >= 0 ? beats[i].t : undefined;
  };
  const starts = detectedSections(stream, d.sectionRule);
  const cands: Candidate[] = [];
  const add = (kind: SetPieceKind, s: number | undefined, accept: (out: ReactiveSetPieces) => boolean) => {
    if (s !== undefined && s < until) cands.push({ kind, s, accept });
  };
  // Section starts: the boss (once), a ring on a loud one, a bee wedge on any.
  let bossTaken = false;
  for (const st of starts) {
    const loud = levelAt(stream, st.knownAt) === "loud";
    if (loud && st.knownAt >= sp.boss.afterS - 1e-9) {
      // (It starts with the section, and drops in at least landAfterS after the start is known.)
      const sEnter = downbeatAfter(st.knownAt, sp.boss.landAfterS - 0.35);
      add("boss", sEnter === undefined ? undefined : st.s, (o) => {
        // (Not if the finale has come by the time it would drop in.)
        if (bossTaken || sEnter! - BOSS_DROP_S >= until - 1e-9) return false;
        bossTaken = true;
        o.boss = boss(stream, sp, sEnter!, until);
        return true;
      });
    }
    if (loud) add("ring", st.s, (o) => o.rings.push(ring(stream, sp, st)) > 0);
    const w = formationBeats(sp.wedge);
    add("wedge", st.s, (o) => o.formations.push(formation(stream, sp.wedge, st.s, w.entranceBeats + (w.releaseBeats ?? 0))) > 0);
  }
  // Stampedes: a rising jump after at least quietS that were not loud (a blip of loud ticks, under
  // 5% of them, aside), known with the beat clear.
  for (const j of stream.structure?.jumps ?? []) {
    if (j.db < sp.stampede.jumpDb - 1e-9 || j.t < sp.stampede.quietS || evidenceAt(stream, j.knownAt) < gate) continue;
    let n = 0;
    let loud = 0;
    for (let i = tickIndexAt(stream, j.t); i >= 0 && stream.ticks[i].t > j.t - sp.stampede.quietS - 1e-9; i--, n++) if (stream.ticks[i].level === "loud") loud++;
    if (loud > QUIET_BLIP * n) continue;
    add("stampede", downbeatAfter(j.knownAt), (o) => o.stampedes.push({ s: downbeatAfter(j.knownAt)!, enemy: sp.stampede.enemy, count: sp.stampede.count, rows: sp.stampede.rows }) > 0);
  }
  // The kick line (once): after mediumBeats committed beats in a row at medium level, the last with the beat clear.
  let kickTaken = false;
  let run = 0;
  for (let i = 0; i < beats.length; i++) {
    const b = beats[i];
    run = levelAt(stream, b.t) === "medium" ? run + 1 : 0;
    if (run >= sp.kickLine.mediumBeats && evidenceAt(stream, b.t) >= gate) {
      const s = downbeatAfter(b.t);
      add("kick-line", s, (o) => {
        if (kickTaken) return false;
        kickTaken = true;
        o.formations.push(kickLine(stream, sp, s!, formationBeats(sp.kickLine.formation).entranceBeats));
        return true;
      });
      run = 0;
    }
  }
  // Volleys: from the effective downbeat after the level turns loud with the beat clear, while it
  // stays so (a new group only after at least VOLLEY_REARM_S without, so a blip does not restart it).
  let offSince = 0;
  let on = false;
  for (const k of stream.ticks) {
    const now = k.level === "loud" && k.evidence >= gate;
    if (now && !on && k.t - offSince >= VOLLEY_REARM_S - 1e-9) {
      const s = downbeatAfter(k.t);
      add("volleys", s, (o) => volleyGroup(stream, sp, s!, until, o));
    }
    if (!now && on) offSince = k.t;
    on = now;
  }
  // Spacing and priority. Volleys, the least of them, wait the gap after any start but block none
  // (archers keep volleying under a later set piece); turned away by the spacing, they try again on
  // the first downbeat it allows, while the level is still loud with the beat clear (decided then).
  const order = (a: Candidate, b: Candidate) => a.s - b.s || PRIORITY[a.kind] - PRIORITY[b.kind];
  cands.sort(order);
  let last = -Infinity;
  let lastOther = -Infinity;
  for (let i = 0; i < cands.length; i++) {
    const c = cands[i];
    if (c.s < (c.kind === "volleys" ? last : lastOther) + sp.gapS - 1e-9) {
      if (c.kind === "volleys") {
        const s = downbeatAfter(last + sp.gapS - 0.35);
        const k = s === undefined ? -1 : tickIndexAt(stream, s);
        if (s !== undefined && s < until && k >= 0 && stream.ticks[k].level === "loud" && stream.ticks[k].evidence >= gate) {
          const retry: Candidate = { kind: "volleys", s, accept: (o) => volleyGroup(stream, sp, s, until, o) };
          let j = i + 1;
          while (j < cands.length && order(cands[j], retry) <= 0) j++;
          cands.splice(j, 0, retry);
        }
      }
      continue;
    }
    // (A once-only set piece already taken, or a boss the finale forestalls, takes no slot.)
    if (!c.accept(out)) continue;
    out.starts.push({ kind: c.kind, s: c.s });
    last = c.s;
    if (c.kind !== "volleys") lastOther = c.s;
  }
  return out;
}

/**
 * A closing ring on a loud section start: its members hold their radius until the close starts at
 * the knownAt of the first forecast or strong onset at least `afterS` after the ring starts, or at
 * the `maxBeats`-th committed beat after it if that comes first; it ends on the next downbeat known
 * by then (see closeEnd).
 */
function ring(stream: ReactiveStream, sp: SetPieces, st: DetectedSection): ReactiveRing {
  const r = sp.ring;
  const accents = [...(stream.accents?.forecast ?? []).filter((f) => f.confidence >= 0.6 - 1e-9), ...stream.onsets.filter((o) => o.strong)];
  let kClose = stream.beats[Math.min(stream.beats.length - 1, st.beat + r.maxBeats)].t;
  for (const a of accents) if (a.t >= st.s + r.afterS - 1e-9 && a.knownAt >= st.s - 1e-9) kClose = Math.min(kClose, a.knownAt);
  return { s: st.s, kClose, sClose: closeEnd(stream, kClose), enemy: r.enemy, count: r.count, gaps: r.gaps };
}

/**
 * Where a close starting at k ends, from what is known at k (the close's pace depends on it): the
 * first downbeat (2-beat bars) at least DOWNBEAT_NOTICE after k among the beats committed by k, or,
 * with none committed yet, a bar of the latest committed period after k plus the notice.
 */
function closeEnd(stream: ReactiveStream, k: number): number {
  const beats = stream.beats;
  let last = -1;
  for (let i = 0; i < beats.length && beats[i].knownAt <= k + 1e-9; i++) {
    last = i;
    if (i % 2 === 0 && beats[i].t >= k + DOWNBEAT_NOTICE - 1e-9) return beats[i].t;
  }
  const period = last >= 0 ? beats[last].period : 0.5;
  return k + DOWNBEAT_NOTICE + 2 * period;
}

/** A formation that releases `beats` committed beats after it starts (counted as they arrive). */
function formation(stream: ReactiveStream, def: string, s: number, beats: number): ReactiveFormation {
  const i = beatAtOrAfter(stream, s);
  const j = Math.min(stream.beats.length - 1, i + beats);
  return { def, sStart: s, sRelease: stream.beats[j].t };
}

/**
 * The kick line: its entrance in committed beats (8), then it dances; it releases at the knownAt of the
 * first strong onset after at least `minDanceBeats` dancing beats, or after `maxDanceBeats`.
 */
function kickLine(stream: ReactiveStream, sp: SetPieces, s: number, entranceBeats: number): ReactiveFormation {
  const k = sp.kickLine;
  const i = beatAtOrAfter(stream, s);
  const at = (n: number) => stream.beats[Math.min(stream.beats.length - 1, i + n)].t;
  const from = at(entranceBeats + k.minDanceBeats);
  let sRelease = at(entranceBeats + k.maxDanceBeats);
  const onset = stream.onsets.find((o) => o.strong && o.t >= from - 1e-9);
  if (onset) sRelease = Math.min(sRelease, onset.knownAt);
  return { def: k.formation, sStart: s, sRelease };
}

/** The boss: it lands on sEnter, slams every `slamBeats` committed beats while loud (decided at each mark), and retreats after `retreatS`. */
function boss(stream: ReactiveStream, sp: SetPieces, sEnter: number, until: number): ReactiveBoss {
  const b = sp.boss;
  const i0 = beatAtOrAfter(stream, sEnter);
  const sRetreat = sEnter + b.retreatS;
  const slams: ReactiveBoss["slams"] = [];
  for (let i = i0 + b.slamBeats; i < stream.beats.length; i += b.slamBeats) {
    // (Marked a beat ahead: the landing's beat is committed by then.)
    const sLand = stream.beats[i].t;
    const sMark = stream.beats[i - 1].t;
    // (Decided at the mark: the retreat is known from the entrance; the finale, when it comes, kills it.)
    if (sMark >= until || sLand >= sRetreat) break;
    if (levelAt(stream, sMark) === "loud") slams.push({ sMark, sLand });
  }
  return { type: b.type, sEnter, slams, sRetreat, ...(b.landmark ? { landmark: b.landmark } : {}) };
}

/**
 * Archer volleys from s while the level stays loud: the group enters at s, and each volley is
 * decided at its draw, a beat before its release (nothing after the run's last start).
 */
function volleyGroup(stream: ReactiveStream, sp: SetPieces, s: number, until: number, out: ReactiveSetPieces): boolean {
  const v = sp.volleys;
  const group = out.archerGroups.length;
  const i0 = beatAtOrAfter(stream, s);
  const volleys: ReactiveSetPieces["volleys"] = [];
  for (let i = i0 + v.everyBeats; i + 1 < stream.beats.length; i += v.everyBeats) {
    const sDraw = stream.beats[i - 1].t;
    if (sDraw >= until || levelAt(stream, sDraw) !== "loud" || evidenceAt(stream, sDraw) < stream.params.gate) break;
    volleys.push({ sDraw, sRelease: stream.beats[i].t, sArrive: stream.beats[i + 1].t, archers: v.archers, group });
  }
  out.volleys.push(...volleys);
  out.archerGroups.push({ s, archers: v.archers, until: volleys.length ? volleys[volleys.length - 1].sArrive : s });
  return true;
}
