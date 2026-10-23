/**
 * Reactive mode's virtual players (docs/design/reactive-audio.md, "Virtual Players"): they press
 * against the track's reference beats, as a person hears the music, never against committed
 * beats. Play mode's moderate and human profiles press every reference beat; reactive-half the
 * moderate profile on every other one, reactive-offbeat on every off-beat. On a track whose
 * reference has free sections (the Galop's prelude) they press only in its pulsed sections and roam
 * elsewhere, as play mode's players do. The holder and mashers steer like reactive-moderate.
 */
import type { SkillProfileName as PlayerName } from "../content/schema.ts";
import type { Ctx } from "../planner/context.ts";
import { HolderPlayer, MasherPlayer, VirtualPlayer, type Player, type PlayerSection, type SkillProfile } from "../sim/player.ts";
import type { GridSlot } from "../sim/schedule.ts";
import type { Reference } from "./stream.ts";

interface Spec {
  profile: SkillProfile;
  /** Press every beat ("x") or every other ("x."), on the beats or on the off-beats. */
  presses: string;
  offbeat?: boolean;
}

const SPECS: Partial<Record<PlayerName, Spec>> = {
  "reactive-moderate": { profile: "moderate", presses: "x" },
  "reactive-human": { profile: "human", presses: "x" },
  "reactive-half": { profile: "moderate", presses: "x." },
  "reactive-offbeat": { profile: "moderate", presses: "x", offbeat: true },
};

/** The reference's sections as the players know them: pulsed ones press, free ones roam. */
function referenceSections(ctx: Ctx, ref: Reference, presses: string): PlayerSection[] {
  const pace = ctx.sections[0].level.pace;
  const secs = ref.sections?.length ? ref.sections : [{ label: "all", s0: 0, s1: ctx.duration }];
  return secs.map((s, i) => ({ steps: s.feel !== "free", presses, s0: i === 0 ? 0 : s.s0, s1: i === secs.length - 1 ? Math.max(s.s1, ctx.duration) : s.s1, pace }));
}

/** Grid slots on the reference beats (or their off-beats) of the pulsed sections. */
function referenceSlots(ref: Reference, sections: PlayerSection[], offbeat: boolean): GridSlot[] {
  const beats = ref.beats;
  const out: GridSlot[] = [];
  const sectionAt = (t: number) => Math.max(0, sections.findLastIndex((x) => t >= x.s0 - 1e-9));
  for (let i = 0; i < beats.length; i++) {
    const s = offbeat ? (i + 1 < beats.length ? (beats[i] + beats[i + 1]) / 2 : NaN) : beats[i];
    if (!Number.isFinite(s)) continue;
    const section = sectionAt(s);
    if (!sections[section].steps) continue;
    out.push({ s, bi: offbeat ? i + 0.5 : i, bar: Math.floor(i / 2) + 1, slot: i % 2, section, accent: 0, kind: "grid" });
  }
  return out;
}

function referencePlayer(ctx: Ctx, ref: Reference, spec: Spec): { player: VirtualPlayer; sections: PlayerSection[] } {
  const sections = referenceSections(ctx, ref, spec.presses);
  const slots = referenceSlots(ref, sections, spec.offbeat ?? false);
  return { player: new VirtualPlayer(ctx.lib.hero, ctx.seed, slots, sections, ref.beats, spec.profile), sections };
}

/** Is this a player for reactive levels (one that needs a reference)? */
export const isReactivePlayer = (name: PlayerName): boolean => SPECS[name] !== undefined;

/** The player a name stands for on a reactive track: a reference player, the holder, or a masher (mash2, mash4, mash8). */
export function reactivePlayerFor(ctx: Ctx, ref: Reference, name: PlayerName): Player {
  const spec = SPECS[name];
  if (spec) return referencePlayer(ctx, ref, spec).player;
  const steer = referencePlayer(ctx, ref, SPECS["reactive-moderate"]!);
  const mash = /^mash(\d+)$/.exec(name);
  if (mash) return new MasherPlayer(steer.player, steer.sections, ctx.seed, Number(mash[1]));
  if (name === "holder") {
    const bars = ref.beats.filter((_, i) => i % 2 === 0);
    return new HolderPlayer(steer.player, steer.sections, ctx.seed, [...bars, ctx.duration]);
  }
  throw new Error(`player ${name} does not play reactive levels (use ${Object.keys(SPECS).join(", ")}, holder or a masher)`);
}
