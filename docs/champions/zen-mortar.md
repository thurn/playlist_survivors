# Rhythm Champions: Downbeat Mortars

## Introduction

**Zen mode, with a reason to move on the music.** `--mortar` is an
experimental option, played as `just champions --zen --mortar`. Every few
seconds a mortar shell is fired at the hero. It lands on a downbeat. A dash
on the last beat before it lands gets you out of the way.

Nothing grades the player. There is no timing window, combo or HUD meter. The
shell lands where you were standing. If you've moved, it misses you and hits
the horde. Rhythm comes in through the world (*diegetic*): moving with the
music is how you dodge.

It's an experiment. It stays forgiving on purpose:

- Horde survivors are relaxing to play. This must not turn zen into a rhythm
  game with required inputs.
- One shell at a time, a few seconds apart, with a long and visible warning.
- A hit costs a little health and nothing else.

Designed with the operator on **2026-11-04**. Epic: **ps-6ak**
(see [Beads](#beads)). [design.md](design.md) governs everything this
document doesn't change. Plain `--zen` stays exactly as it is.

### A shell, as the player sees it

At 120 BPM, two bars (4 s) per shell:

- **Bar 1:** nothing.
- **Bar 2, beats 1–3:** a faint orange ring sits under the hero and eases
  after them, ticking on each beat.
- **Beat 4 (the lock):** the ring flares solid and stops following. A low
  thump sounds, and a glowing shell rises out of the dark at the edge of the
  view in a high arc. The ring starts filling.
- **The next downbeat:** the shell lands as an orange burst with a small
  camera punch and a boom under the music's kick. Monsters inside it
  shatter. If the hero is still inside, they take 5 damage.

Then the next cycle starts. The player who dashes on 4 never gets hit. The
player who ignores it loses a little health every few seconds and still
plays a normal horde run.

## Decisions

Settled with the operator on 2026-11-04. Numbers are starting points for
hand-play tuning, kept in `Content/mortar.json`.

1. **Positional, not an invulnerability check.** Dash invulnerability lasts
   only about 0.25 s (0.15 s of travel plus 0.1 s). A shell dodged *through*
   invulnerability would be a hidden ±125 ms timing check, stricter than the
   real judge. So the shell lands where the hero stood at the lock. Any way
   of leaving the blast counts: a dash, a brisk walk or invulnerability.
2. **One shell per cycle.** A cycle is the number of bars (1, 2 or 4) whose
   length is nearest **3.5 s** at the local tempo. Shells always land on a
   downbeat. Examples:

   | Tempo | Bar | Cycle | Shell every |
   | --- | --- | --- | --- |
   | 70 BPM | 3.43 s | 1 bar | 3.4 s |
   | 90 BPM | 2.67 s | 1 bar | 2.7 s |
   | 120 BPM | 2.00 s | 2 bars | 4.0 s |
   | 174 BPM | 1.38 s | 2 bars | 2.8 s |

   The cycle length is picked again at the start of each cycle, so tempo
   changes are followed between shells, never during one.
3. **The lock leaves at least 0.4 s.** The lock is the last beat before the
   landing that leaves at least 0.4 s. That is one beat up to 150 BPM and two
   beats above it. The escape window is therefore 0.4–0.9 s, and the lock is
   always on a beat.
4. **Blast radius 2.5 m.** The 4 m dash always clears it. Walking (6 m/s)
   clears it only at slow tempos, so the dash is the natural answer without
   being the only one.
5. **A hit costs 5 health** through the normal hit rules (`HeroHealth.Hurt`
   with a new `HitSource.Mortar`): hit invulnerability, dash invulnerability
   and zen's perfect dodge all apply. No other penalty. Zen's aura stays on.
6. **Shells hurt monsters.** Monsters in the blast take the baseline dash
   landing sweep's damage, unscaled by upgrades. Weak fodder dies and elites
   are chipped. Kills drop normal XP. Structures in the blast break as boss
   attacks break them. Dodging out leaves the pack chasing you to take the
   shell.
7. **Horde time only.** Shells never fire during boss fights, in Boss Rush
   or in a lone `--fight`.
8. **Cancel, never freeze.** A card, a pause, a boss warning or arrival, or
   the hero's death cancels the current shell, even one already in the air
   (it fizzles out). When horde time resumes, a fresh full cycle starts on
   the next downbeat. A resume can never put the hero in a blast.
9. **Beatless pauses the shells.** A cycle starts only on a reliable
   downbeat (`BeatServices.Reliable`). If the beat goes unreliable before the
   lock, the ring quietly fades. A shell already locked lands at its promised
   time, like every telegraph (`Telegraphs`). Shells resume on the next
   reliable downbeat. A tempo change never shortens a warning in flight.
10. **Steady, no escalation.** No shells in the first **8 bars** of the run.
    After that, one shell per cycle, unchanged until victory. No second
    shell, no faster cadence late in the run.
11. **Quiet tracking, loud landing.** The tracking ring is faint and appears
    only during the **last bar** before the landing (the whole cycle when it
    is one bar). The lock flares, thumps and launches a visible shell from a
    seeded edge of the view. The landing gets the burst, punch and boom.
    Danger is orange/red, as design.md's palette reserves. No HUD, counters
    or text.
12. **An independent `--mortar` flag.** On `LaunchOptions`, `RunInputs` and
    `SimConfig`, like `--zen`. It is recorded in sessions (session version
    10) and replays deterministically. It works with or without `--zen`;
    without zen the rhythm is judged as usual. The `just champions` and
    `just champions-play` help text lists it.

## Behaviour in detail

### The cycle

All times are sim song time; heard time aligns with it, as for telegraphs.

- **Start:** a reliable downbeat D, in horde time, at least 8 bars into the
  run (or at least one bar after a cancel). Pick N bars by Decision 2.
- **Landing L:** the downbeat N bars after D (`BeatServices.NextDownbeat`).
- **Lock K:** the last beat before L leaving at least 0.4 s.
- **Tracking:** from one bar before L (or from D when N is 1) until K. Only
  the ring shows. Its eased drift is cosmetic; the lock reads the hero's true
  position.
- **Lock:** at K the shell's target is fixed to the hero's position. Its
  launch point is seeded (`SimRandom`) from beyond the visible edge, so the
  arc crosses the view.
- **Landing:** at L, resolve the blast once: the hero (overlap of the hero's
  body with the 2.5 m circle), every monster and every structure inside it.
  The next cycle starts at L.

Prefer the existing `Telegraphs` service for planning K and L: it already
keeps promised impact times through tempo changes and beatless spans.

### Fairness

These hold at every tempo and are enforced by sim tests:

- From the lock, a dash in any unobstructed direction, pressed at any moment
  between K and L, leaves the hero unhurt. Dashes pressed just before L are
  safe through invulnerability.
- Walking straight away from K's position for the whole window escapes at
  70 BPM.
- The dash cooldown (0.6 s) is far shorter than the shortest cycle (about
  2.3 s), so a previous dodge never blocks the next.
- No shell lands within one bar of a resume from a card, pause or boss.
- No shell ever lands during a boss warning or fight.

### State, events and replay

- The sim holds one `MortarState`: phase (idle, tracking, locked), D, K, L,
  target, launch point and the cycle's N. It is in the snapshot and the
  replay hash.
- Events: `MortarTrack`, `MortarLock`, `MortarLand` (with hero hit and
  kills) and `MortarCancel`. The presenter and the sound bank read them.
- The scripted player (`ScriptedPlayer`), when `--mortar` is on, dashes away
  from the target on the lock beat, so scripted reference runs stay
  comparable with today's. A scripted option that ignores shells measures
  the worst-case damage.

## Look and sound

- **Ring:** a thin orange circle of the blast's radius under the hero,
  faint while tracking, pulsing on each beat. At the lock it flares and fills
  toward the landing, so its fill shows time to impact.
- **Shell:** a small glowing orange faceted body with a short trail, in a
  high arc from the launch point to the target, arriving exactly at L.
- **Landing:** an orange burst at the blast's radius, a small camera punch
  (below a boss stomp's), shatter of everything killed, broken structures.
- **Sound:** a low launch thump at K, an optional faint whistle on the
  descent, and a boom at L sitting under the music like a kick. A cancelled
  shell fizzles quietly. Mixed below the music, as design.md asks.
- Judge it by eye in the trailer's look: it must read clearly through a
  dense horde and full weapon effects, without becoming the brightest thing
  on screen.

## Validation

- **Sim tests** under `dotnet/`: cadence at 70, 90, 120, 174 BPM; the lock
  rule at 150 and 151 BPM; the fairness rules above; hits through the normal
  rules (hit, dash and zen invulnerability); monster and structure damage;
  cancel on cards, pause, boss and death; no shells in bosses, Boss Rush or
  `--fight`; beatless pause and resume; tempo change mid-cycle; the first 8
  bars; session v10 round trip and replay determinism; `--mortar` without
  `--zen`.
- **Reference runs:** scripted runs at slow, mid and fast tempo and a
  beatless track. Report shells per minute, escape windows, scripted-dodge
  hits (expect none), shell-ignoring damage, and mortar kills. Compare
  pacing with the same seeds without `--mortar`.
- **Stills:** tracking, lock, mid-arc and landing, in a dense late horde.
- **Operator row** in `rhythm_champions/evidence/operator-pending.md`: play
  `just champions --zen --mortar` over a few songs of different tempos. Does
  it pull you into dashing with the music? Does it still feel relaxing? Is
  the shell readable without dominating the screen? Tune `Content/mortar.json`
  by feel (cycle target, radius, lock lead, damage).

## Beads

Filed under ps-6ak, light run:

1. **Sim:** the flag, content, cycle, blast, hit rules, cancel and beatless
   rules, state, events, scripted player and tests.
2. **Look and sound:** ring, shell, burst, punch, sound bank, stills.
   Needs bead 1's state and events.
3. **Reference runs and tuning:** the reference runs, pacing comparison,
   first tuning pass and the operator row. Needs bead 1.
4. **Warden:** the final review.

## Open questions

For hand-play, not blocking:

- Is 5 damage per hit the right stake, or should ignoring shells cost less?
- Should the cycle target follow the music's energy later?
- If it works, should the main game adopt it, or should it stay a zen option?
