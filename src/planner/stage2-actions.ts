/** Stage 2: choose hero actions, weapon firings and threats, still in song time. */
import type { MoveDef } from "../content/schema.ts";
import { beatIndexAt, energyAt, timeOfBeatIndex } from "../core/songmap.ts";
import { PlanError, sectionIndexAt, subdivisionBeats, type Ctx, type HeroAction, type ActionKind, type Slot } from "./context.ts";
import { resolveRef, sectionPhrases, phraseFinalAccent } from "./stage1-musical.ts";
import { barOfBeatIndex } from "../core/songmap.ts";

function move(ctx: Ctx, id: string): MoveDef {
  const m = ctx.lib.moves.get(id);
  if (!m) throw new PlanError(`unknown move "${id}"`);
  return m;
}

/** The strongest accent within 40 ms of song time s (0 if none). */
function accentAt(ctx: Ctx, s: number): { strength: number; label?: string } {
  let best = { strength: 0 } as { strength: number; label?: string };
  for (const a of ctx.song.accents) if (Math.abs(a.time - s) <= 0.04 && a.strength > best.strength) best = { strength: a.strength, label: a.label };
  return best;
}

function makeAction(ctx: Ctx, s: number, m: MoveDef, kind: ActionKind, extra: Partial<HeroAction> = {}): HeroAction {
  const section = sectionIndexAt(ctx, s);
  const bi = beatIndexAt(ctx.song, s);
  return {
    id: -1,
    s,
    bi,
    move: m,
    kind,
    section,
    energy: energyAt(ctx.song, s),
    accent: 0,
    w: 0,
    dir: 0,
    x: 0,
    y: 0,
    x0: 0,
    y0: 0,
    combo: true,
    ...extra,
  };
}

/** Pick each slot's move from the section's moveset rules. */
function patternActions(ctx: Ctx): HeroAction[] {
  const out: HeroAction[] = [];
  for (const sec of ctx.sections) {
    const ms = sec.moveset;
    const slots = ctx.slots[sec.index];
    if (sec.song.feel === "free") {
      const anchorSlots = slots.filter((s) => s.kind === "anchor" && ms.onAnchor && (s.anchorStrength ?? 0) >= ms.onAnchor.minStrength);
      for (const sl of slots) {
        if (sl.kind === "continuous" && ms.continuous) {
          if (anchorSlots.some((a) => Math.abs(a.bi - sl.bi) < 1)) continue;
          out.push(makeAction(ctx, sl.s, move(ctx, ms.continuous.move), "attack", { section: sec.index, energy: sl.energy }));
        } else if (sl.kind === "anchor" && anchorSlots.includes(sl)) {
          out.push(makeAction(ctx, sl.s, move(ctx, ms.onAnchor!.move), "attack", { section: sec.index, energy: sl.energy, accent: sl.anchorStrength ?? 0 }));
        }
      }
      continue;
    }
    const rules = ms.attacks ?? [];
    const counters = rules.map(() => 0);
    const chosen = new Map<Slot, MoveDef>();
    // Pattern rules, later rules overriding earlier ones.
    for (const sl of slots) {
      let pick: MoveDef | undefined;
      rules.forEach((r, ri) => {
        if (!("pattern" in r)) return;
        const bars = r.pattern.split("|");
        const barIdx = (sl.bar - sec.song.bars[0]) % bars.length;
        if (bars[barIdx][sl.slotInBar] === "x") {
          pick = move(ctx, r.moves[counters[ri] % r.moves.length]);
          counters[ri]++;
        }
      });
      if (pick) chosen.set(sl, pick);
    }
    // Accent rules replace (or add to) that slot's move.
    for (const sl of slots) {
      if (sl.accent <= 0) continue;
      for (const r of rules) {
        if (!("onAccent" in r)) continue;
        const a = r.onAccent;
        if (sl.accent >= a.minStrength && (a.label === undefined || a.label === sl.accentLabel)) chosen.set(sl, move(ctx, a.move));
      }
    }
    // onSectionEnd: the section's last beat.
    for (const r of rules) {
      if (!("onSectionEnd" in r)) continue;
      const lastBeat = sec.b1 - 1;
      const sl = slots.find((x) => Math.abs(x.bi - lastBeat) < 1e-6);
      if (sl) chosen.set(sl, move(ctx, r.onSectionEnd));
    }
    for (const sl of slots) {
      const m = chosen.get(sl);
      if (m) out.push(makeAction(ctx, sl.s, m, "attack", { section: sec.index, energy: sl.energy, accent: sl.accent, accentLabel: sl.accentLabel }));
    }
  }
  return out;
}

function parseEvery(every: string, bpb: number): number {
  const [n, unit] = every.split(" ");
  return Number(n) * (unit.startsWith("bar") ? bpb : 1);
}

/** Remove actions whose beat index lies in [b0, b1]. */
function clearBeats(actions: HeroAction[], b0: number, b1: number): HeroAction[] {
  return actions.filter((a) => a.bi < b0 - 1e-6 || a.bi > b1 + 1e-6);
}

export function stage2(ctx: Ctx): void {
  const { song, idx } = ctx;
  let actions = patternActions(ctx);
  const tAt = (bi: number) => timeOfBeatIndex(song, bi);
  const biAt = (s: number) => beatIndexAt(song, s);
  const bpbAt = (s: number) => idx.beatsPerBar[Math.max(1, barOfBeatIndex(idx, biAt(s)))] ?? 2;

  // Events.
  for (const e of ctx.events) {
    const ev = e.ev;
    if (ev.levelUp) ctx.weaponsGranted.push({ id: ev.levelUp, s: e.s });
    if (ev.timeScale !== undefined) ctx.timeScaleEvents.push({ s: e.s, rate: ev.timeScale });
    if (ev.blast) {
      ctx.blastS = e.s;
      ctx.blastWeapon = ev.blast;
    }
    if (ev.formation) {
      const def = ctx.lib.formations.get(ev.formation)!;
      const section = sectionIndexAt(ctx, e.s);
      let sRelease: number;
      if (def.release.at === "finalAccentOfPhrase") {
        const startBar = barOfBeatIndex(idx, biAt(e.s));
        const phrases = sectionPhrases(idx, ctx.sections[section].label).filter((p) => p[0] >= startBar);
        const ph = phrases[def.release.phrases - 1] ?? phrases[phrases.length - 1];
        if (!ph) throw new PlanError(`${ctx.level.file}:${e.line}: formation ${ev.formation} has no phrase to release on`);
        sRelease = phraseFinalAccent(idx, ph);
      } else {
        sRelease = tAt(biAt(e.s) + def.entranceBeats + def.release.beats);
      }
      ctx.formations.push({ id: ctx.formations.length, def: ev.formation, sStart: e.s, sRelease, section, members: [] });
    }
    if (ev.threats) {
      const until = resolveRef(idx, ev.threats.until);
      const step = parseEvery(ev.threats.every, bpbAt(e.s));
      let k = 0;
      for (let bi = Math.round(biAt(e.s)) + step; tAt(bi + 1.5) < until; bi += step, k++) {
        const side = k % 2 === 0 ? 1 : -1;
        const r = ctx.rng("threat", bi);
        ctx.threats.push({
          id: ctx.threats.length,
          kind: "arrow",
          sRelease: tAt(bi),
          sDodge: tAt(bi + 1),
          sMiss: tAt(bi + 1.5),
          bearing: side * (Math.PI / 2) + r.range(-0.5, 0.5),
          archers: ev.threats.archers,
        });
      }
    }
    if (ev.boss) {
      const sDeath = resolveRef(idx, ev.boss.diesAt);
      ctx.boss = { type: ev.boss.type, sEnter: e.s, sDeath, hits: ev.boss.hits, slams: ev.boss.slams };
    }
  }
  ctx.weaponsGranted.sort((a, b) => a.s - b.s);

  // Arrow volleys: dodge on the beat before the arrow arrives.
  for (const th of ctx.threats) {
    const bd = biAt(th.sDodge);
    actions = clearBeats(actions, bd - 0.01, biAt(th.sMiss) + 0.01);
    const a = makeAction(ctx, th.sDodge, move(ctx, "dodge"), "dodge", { threat: th.id, holdUntilS: tAt(biAt(th.sMiss) + 0.5) });
    actions.push(a);
  }

  // Boss slams, counters and planned hits.
  if (ctx.boss) {
    const b = ctx.boss;
    const bEnter = biAt(b.sEnter);
    const bDeath = biAt(b.sDeath);
    const bpb = bpbAt(b.sEnter);
    const first = bEnter + 4 * bpb;
    const last = bDeath - 4 * bpb;
    const counters: HeroAction[] = [];
    for (let k = 0; k < b.slams; k++) {
      const target = first + ((k + 0.5) / b.slams) * (last - first);
      // Snap to a downbeat an even number of bars after the entrance (the boss lumbers every two bars).
      const bars = Math.max(2, Math.round((target - bEnter) / (2 * bpb)) * 2);
      const slamBi = bEnter + bars * bpb;
      const r = ctx.rng("slam", k);
      const th = {
        id: ctx.threats.length,
        kind: "slam" as const,
        sRelease: tAt(slamBi - 2),
        sDodge: tAt(slamBi),
        sMiss: tAt(slamBi),
        bearing: r.range(-Math.PI, Math.PI),
      };
      ctx.threats.push(th);
      actions = clearBeats(actions, slamBi - 1.01, slamBi + 1.01);
      actions.push(makeAction(ctx, th.sDodge, move(ctx, "dodge"), "dodge", { threat: th.id, holdUntilS: tAt(slamBi + 0.75) }));
      const c = makeAction(ctx, tAt(slamBi + 1), move(ctx, "cleave"), "counter", { threat: th.id, bossHit: true });
      actions.push(c);
      counters.push(c);
    }
    // Death blow: the action on diesAt becomes a finisher.
    actions = actions.filter((a) => Math.abs(a.s - b.sDeath) > 0.04);
    const da = accentAt(ctx, b.sDeath);
    actions.push(makeAction(ctx, b.sDeath, move(ctx, "finisher"), "finisher", { bossHit: true, accent: da.strength, accentLabel: da.label }));
    // The remaining planned hits: the heaviest moves while the boss is present, spread evenly.
    const extra = Math.max(0, b.hits - counters.length - 1);
    const rank = { heavy: 3, medium: 2, light: 1 } as const;
    const candidates = actions
      .filter((a) => a.kind === "attack" && a.bi > bEnter + 2 * bpb && a.bi < bDeath - bpb)
      .filter((a) => !counters.some((c) => Math.abs(c.bi - a.bi) < 2 * bpb));
    const used = new Set<HeroAction>();
    for (let k = 0; k < extra; k++) {
      const target = bEnter + ((k + 0.5) / extra) * (bDeath - bEnter);
      let best: HeroAction | undefined;
      let bestScore = Infinity;
      for (const a of candidates) {
        if (used.has(a)) continue;
        const score = Math.abs(a.bi - target) / bpb - 6 * rank[a.move.weight];
        if (score < bestScore) {
          best = a;
          bestScore = score;
        }
      }
      if (best) {
        best.bossHit = true;
        used.add(best);
      }
    }
  }

  // Formation releases.
  for (const f of ctx.formations) {
    const def = ctx.lib.formations.get(f.def)!;
    if (def.release.at !== "finalAccentOfPhrase") continue;
    const br = biAt(f.sRelease);
    const dur = def.count * subdivisionBeats(def.release.stagger, 2);
    actions = clearBeats(actions, br - 0.6, br + dur + 0.6);
    const a = makeAction(ctx, f.sRelease, move(ctx, def.release.move), "sweep", { formation: f.id });
    actions.push(a);
  }

  // The finale blast ends the hero's routine.
  if (ctx.blastS !== undefined) {
    actions = actions.filter((a) => a.s < ctx.blastS! - 0.04);
    const ba = accentAt(ctx, ctx.blastS);
    actions.push(makeAction(ctx, ctx.blastS, move(ctx, "burst"), "blast", { accent: ba.strength, accentLabel: ba.label }));
  }

  actions.sort((a, b) => a.s - b.s);
  actions.forEach((a, i) => (a.id = i));
  ctx.actions = actions;
  for (const th of ctx.threats) {
    th.dodgeAction = actions.find((a) => a.kind === "dodge" && a.threat === th.id)?.id;
    th.counterAction = actions.find((a) => a.kind === "counter" && a.threat === th.id)?.id;
  }

  // Weapon firings, from each level-up until the blast.
  const end = ctx.blastS ?? ctx.duration;
  for (const g of ctx.weaponsGranted) {
    const wd = ctx.lib.weapons.get(g.id)!;
    if (wd.kind === "blast") continue;
    const startBar = Math.max(1, barOfBeatIndex(idx, biAt(g.s)));
    for (let bar = startBar; bar <= idx.barCount; bar++) {
      const bpb = idx.beatsPerBar[bar];
      const step = subdivisionBeats(wd.fires.every, bpb);
      for (let off = 0; off < bpb - 1e-9; off += step) {
        const bi = idx.barStart[bar] + off;
        if (bi > song.beats.length - 1) continue;
        const s = tAt(bi);
        // Skip the level-up slot itself; the weapon starts on the next one.
        if (s <= g.s + 0.01 || s >= end - 0.01) continue;
        ctx.firings.push({ weapon: g.id, s, bi, step, section: sectionIndexAt(ctx, s), energy: energyAt(song, s), w: 0 });
      }
    }
  }
  ctx.firings.sort((a, b) => a.s - b.s || a.weapon.localeCompare(b.weapon));
}
