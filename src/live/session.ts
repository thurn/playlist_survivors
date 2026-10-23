/**
 * A live play session: the schedule, the world, the recording and the runner,
 * ready for the keyboard. A mid-song start fast-forwards with the level's
 * virtual player, then hands over.
 */
import type { PropInfo } from "../mapgen/mapgen.ts";
import type { Ctx, PlannerInput } from "../planner/context.ts";
import { playerFor, prepare } from "../planner/plan.ts";
import type { KeyEvent, Player } from "../sim/player.ts";
import type { Schedule } from "../sim/schedule.ts";
import { Sim, worldFor } from "../sim/simulate.ts";
import { LiveRecording } from "./recording.ts";
import { LiveRunner } from "./runner.ts";

export interface LiveSession {
  ctx: Ctx;
  recording: LiveRecording;
  runner: LiveRunner;
  /** Song time the keyboard took over (0 from the start). */
  startS: number;
}

export interface LiveOptions {
  props?: Record<string, PropInfo>;
  /** Start mid-song: the level's virtual player plays up to here (headless), then hands over. */
  startS?: number;
  /** Key events known in advance (a replay): applied at their times. */
  keys?: readonly KeyEvent[];
  /** The virtual player before the hand-over (default: the level's; reactive mode's needs the track's reference). */
  bot?: BotFor;
}

/** The virtual player for a session's level. */
export type BotFor = (ctx: Ctx, sch: Schedule) => Player;

/**
 * A live simulation and its recording, before play starts. With `bot`, a virtual player drives it
 * until the hand-over: the level's, or `botFor`'s.
 */
export function liveSim(input: PlannerInput, props: Record<string, PropInfo> | undefined, bot: boolean, botFor?: BotFor): { ctx: Ctx; recording: LiveRecording; sim: Sim } {
  const { ctx, sch, map } = prepare(input);
  const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", props);
  const sim = new Sim(ctx, sch, worldFor(), recording, bot ? { player: botFor ? botFor(ctx, sch) : playerFor(ctx, sch, ctx.level.player) } : {});
  return { ctx, recording, sim };
}

/** The hand-over at the simulation's time: the bot stops, and its held keys are released. */
export function handOver(sim: Sim, recording: LiveRecording): void {
  sim.detachPlayer();
  for (const key of sim.heldKeys()) sim.key({ s: sim.s, kind: "release", key });
  recording.playerFrom = sim.s;
}

export function liveSession(input: PlannerInput, opts: LiveOptions = {}): LiveSession {
  const startS = Math.max(0, opts.startS ?? 0);
  const { ctx, recording, sim } = liveSim(input, opts.props, startS > 0, opts.bot);
  if (startS > 0) {
    sim.advanceTo(startS);
    handOver(sim, recording);
  }
  for (const k of opts.keys ?? []) if (k.s >= sim.s) sim.key(k);
  return { ctx, recording, runner: new LiveRunner(sim, recording), startS };
}
