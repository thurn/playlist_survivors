/**
 * Live play: the simulation runs on the main thread, a frame at a time, with
 * rollback and speculation.
 *
 * - Presses arrive late (at the next task, up to a frame after they happen),
 *   but the game rules want them at their exact times.
 * - Sounds must be scheduled early: an outcome sound must be known before its
 *   strike resolves.
 *
 * So the simulation always runs ahead to `now + SPECULATE_S`, assuming no new
 * presses, and is snapshotted every SNAP_DT on the way. A key event at time t
 * restores the newest snapshot before t, rewinds the recording to it, applies
 * the key at t and runs ahead again. Speculation only assumes the absence of
 * presses, so the settled recording equals a run fed the same key events.
 */
import type { PlayLog } from "./log.ts";
import type { LiveRecording } from "./recording.ts";
import type { KeyEvent } from "../sim/player.ts";
import type { Recorder } from "../sim/recorder.ts";
import type { Sim } from "../sim/simulate.ts";

/** No key event can still arrive for times more than this far before now (song seconds). */
export const SETTLE_S = 0.04;
/** The simulation runs this far ahead of now. */
export const SPECULATE_S = 0.2;
/** Snapshot spacing along the simulation's run (song seconds). */
export const SNAP_DT = 1 / 30;

/** Snapshots record nothing: they only wait to be restored. */
const NO_RECORDER: Recorder = new Proxy({} as Recorder, { get: (_t, name) => (name === "clock" ? undefined : () => undefined) });

interface Snapshot {
  s: number;
  sim: Sim;
  /** The recording's mark when the snapshot was taken. */
  mark: number;
  /** How many key events had been applied (the snapshot's queue holds them). */
  applied: number;
}

export class LiveRunner {
  private snaps: Snapshot[] = [];
  /** Song time before which the state is final. */
  settled = -Infinity;
  /** Counters, for checks and the benchmark. */
  readonly stats = { rollbacks: 0, replayedS: 0, lateKeys: 0 };
  /** Every key event as applied (at its time, or the settled time if it came very late). */
  readonly applied: KeyEvent[] = [];
  /** Section start times, and the rollback point at the latest one the settled time has passed (for restarting it). */
  private readonly sectionStarts: number[];
  private sectionSnap?: Snapshot & { section: number };
  /** Play mode's timing log: when each key was applied, and the rollbacks. */
  log?: PlayLog;

  constructor(
    public sim: Sim,
    readonly recording: LiveRecording,
  ) {
    this.sectionStarts = recording.ctx.sections.map((x) => x.s0);
    this.snap();
    this.sectionSnap = { ...this.snaps[0], section: this.sectionAt(sim.s) };
  }

  private sectionAt(s: number): number {
    let i = 0;
    while (i + 1 < this.sectionStarts.length && this.sectionStarts[i + 1] <= s + 1e-9) i++;
    return i;
  }

  private snap(): void {
    this.snaps.push({ s: this.sim.s, sim: this.sim.clone(NO_RECORDER), mark: this.recording.mark(), applied: this.applied.length });
  }

  /** Run the simulation to song time t, snapshotting on the way. */
  private run(t: number): void {
    const sim = this.sim;
    while (sim.s < t - 1e-9 && !sim.done) {
      const last = this.snaps[this.snaps.length - 1];
      const next = Math.min(t, last.s + SNAP_DT);
      const before = sim.s;
      sim.advanceTo(next);
      if (sim.s <= before) {
        // One of the simulation's own stops sits right at `next` (snapshots fall on its fixed steps),
        // and its clock waits just short of it. Step through it, unless `next` is where this run ends.
        if (next >= t) break;
        sim.advanceTo(Math.min(t, next + 1e-6));
        if (sim.s <= before) break;
      }
      if (sim.s >= last.s + SNAP_DT - 1e-6) this.snap();
    }
  }

  /**
   * One frame: apply the key events that have arrived (rolling back when the
   * simulation has already run past them), then run ahead of `now`.
   */
  frame(now: number, keys: readonly KeyEvent[] = []): void {
    for (const e of [...keys].sort((a, b) => a.s - b.s)) {
      // A very late event (a main-thread stall) is applied at the settled time instead.
      const t = Math.max(e.s, this.settled, this.snaps[0].s);
      if (t > e.s) this.stats.lateKeys++;
      const from = this.sim.s;
      const to = from > t ? this.rollBack(t) : undefined;
      this.run(t);
      const k = { ...e, s: Math.max(t, this.sim.s) };
      this.sim.key(k);
      this.applied.push(k);
      if (this.log && e.id !== undefined) this.log.apply({ id: e.id, frameS: now, frameTs: performance.now(), appliedS: k.s, late: t > e.s, rollbackFrom: to !== undefined ? from : undefined, rollbackTo: to });
    }
    this.run(now + SPECULATE_S);
    this.settled = Math.max(this.settled, now - SETTLE_S);
    // Once the settled time is past a section's start, the newest snapshot before it is the section's restart point.
    const sec = this.sectionAt(this.settled);
    if (sec > (this.sectionSnap?.section ?? -1)) {
      let i = this.snaps.length - 1;
      while (i > 0 && this.snaps[i].s > this.sectionStarts[sec]) i--;
      this.sectionSnap = { ...this.snaps[i], section: sec };
    }
    // Snapshots older than the settled time go, except the newest one before it.
    let k = 0;
    while (k + 1 < this.snaps.length && this.snaps[k + 1].s <= this.settled) k++;
    if (k > 0) this.snaps.splice(0, k);
    this.recording.commit(Math.min(this.snaps[0].mark, this.sectionSnap?.mark ?? Infinity));
  }

  /**
   * Restart the current section: restore its rollback point, rewind the recording to it and
   * forget the keys since. Returns the section's start time (play resumes there after a count-in).
   */
  restartSection(): number | undefined {
    const snap = this.sectionSnap;
    if (!snap) return undefined;
    this.recording.rewind(snap.mark);
    this.recording.rebuildActive();
    this.sim = snap.sim.clone(this.recording);
    this.snaps = [{ s: snap.s, sim: snap.sim, mark: snap.mark, applied: snap.applied }];
    this.applied.length = snap.applied;
    this.settled = snap.s;
    return Math.max(snap.s, this.sectionStarts[snap.section]);
  }

  /** Restore the newest snapshot before song time t, and rewind the recording to it. Returns the snapshot's time. */
  private rollBack(t: number): number {
    let i = this.snaps.length - 1;
    while (i > 0 && this.snaps[i].s >= t) i--;
    const snap = this.snaps[i];
    this.stats.rollbacks++;
    this.stats.replayedS += this.sim.s - snap.s;
    this.recording.rewind(snap.mark);
    this.sim = snap.sim.clone(this.recording);
    this.snaps.length = i + 1;
    // Keys applied after the snapshot (they came in out of order) go back in, in the order they were applied.
    for (let k = snap.applied; k < this.applied.length; k++) this.sim.key(this.applied[k]);
    return snap.s;
  }

  /** Run to the end of the song (no more presses). */
  finish(): void {
    this.sim.runToEnd();
  }
}
