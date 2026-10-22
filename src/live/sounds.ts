/**
 * Live sound scheduling. A live recording's sounds can still change after
 * they are scheduled: a press in the last moments before a strike can turn a
 * kill into a miss. Each scheduled source keeps its event's identity; a source
 * that has not started and is no longer in the recording is stopped, and
 * whatever the new run adds is scheduled.
 */
import type { SoundEvent } from "../plan/types.ts";

/** Live sounds are scheduled this far ahead (the frame loop schedules every frame). */
export const LIVE_LOOKAHEAD = 0.1;
/** Matches the planner's MAX_SOUND_LEAD. */
const MAX_LEAD = 0.25;

/** Where sounds play: an audio context, or a fake one in tests. */
export interface SoundOutput {
  /** Context time now. */
  readonly currentTime: number;
  /** Start a sound at context time `when`, `offset` seconds into its sample. */
  start(ev: SoundEvent, when: number, offset: number): { stop(): void } | undefined;
}

/** A sound event's identity: its sample, song time, level and place. */
export function soundKey(ev: SoundEvent): string {
  return `${ev.sample}@${Math.round(ev.s * 1e4)}@${Math.round(ev.gain * 100)}@${Math.round(ev.pan * 100)}`;
}

function lowerBound(sounds: readonly SoundEvent[], s: number): number {
  let lo = 0;
  let hi = sounds.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (sounds[mid].s < s) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

export class LiveSoundScheduler {
  private scheduled = new Map<string, { when: number; s: number; handle?: { stop(): void } }>();
  readonly stats = { scheduled: 0, cancelled: 0, startedLate: 0, skippedLate: 0 };

  /** @param t0 Context time of song time 0. */
  constructor(
    private readonly out: SoundOutput,
    public t0: number,
  ) {}

  /** Schedule what is due from the recording's sounds (sorted by song time), and cancel what it no longer has. */
  update(sounds: readonly SoundEvent[], lookahead = LIVE_LOOKAHEAD): void {
    const now = this.out.currentTime;
    const songNow = now - this.t0;
    const present = new Set<string>();
    for (let i = lowerBound(sounds, songNow - 0.3); i < sounds.length && sounds[i].s <= songNow + lookahead + MAX_LEAD; i++) {
      const ev = sounds[i];
      const key = soundKey(ev);
      present.add(key);
      if (this.scheduled.has(key)) continue;
      const start = this.t0 + ev.s - (ev.lead ?? 0);
      if (start > now + lookahead) continue;
      if (this.t0 + ev.s < now - 0.03) {
        // Its onset has passed (a stall, or a sound the recording only now has): never late.
        this.scheduled.set(key, { when: start, s: ev.s });
        this.stats.skippedLate++;
        continue;
      }
      // A slow attack that should already have started starts now, partway in: its onset stays on time.
      const offset = Math.max(0, now - start);
      if (offset > 0) this.stats.startedLate++;
      const handle = this.out.start(ev, Math.max(start, now), offset);
      this.scheduled.set(key, { when: Math.max(start, now), s: ev.s, handle });
      this.stats.scheduled++;
    }
    for (const [key, sc] of this.scheduled) {
      if (sc.when > now && !present.has(key)) {
        sc.handle?.stop();
        this.scheduled.delete(key);
        this.stats.cancelled++;
      } else if (sc.s < songNow - 2) this.scheduled.delete(key);
    }
  }

  /** Stop everything that has not started (a pause, a restart). */
  cancelAll(): void {
    const now = this.out.currentTime;
    for (const [key, sc] of this.scheduled) {
      if (sc.when > now) sc.handle?.stop();
      this.scheduled.delete(key);
    }
  }
}
