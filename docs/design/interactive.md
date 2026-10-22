# Interactive Play: Implementation Plan

## Introduction

The tech demo plays itself. A virtual player presses keys, the
[simulated playthrough](beat-steps.md) runs forward in time, and the
renderer plays the recording back in sync with the music.

This document adds the real game: **play mode**. A human presses the arrow
keys or WASD, and the same game rules react live. A moderately competent
player gets the same experience the scripted demo shows: the same rules and
the same musical set pieces, though never the same frames.

The scripted demo stays exactly as it is. Its plan hash does not change.

### Decisions

These were settled in a design interview before this plan was written:

| Question | Decision |
|---|---|
| What "1:1" means | Same rules and the same scheduled set pieces. A moderately competent player meets the signature checks. Positions and kill counts differ. |
| The world | An open, Vampire Survivors-style field generated in chunks around the hero. There is no corridor, route or pace point. |
| Zone changes | A **transform wave** sweeps out from the hero on the section's first downbeat and re-themes the ground. |
| Landmarks | They rise from the ground centred on the hero, with the transform wave. Props never block movement. |
| The scripted demo | Frozen: the same pipeline and a byte-identical plan hash. |
| Choosing a mode | A title screen with **Play** (the default) and **Watch the demo**. `?mode=play` and `?mode=demo` skip it. |
| Latency | Automatic compensation, plus an optional tap-along calibration. Timing windows are a little looser for humans. |
| "Moderately competent" | A new `moderate` virtual player profile. Automated checks require it to hit the signature moments. |
| Mashing | Every press steps. Only the first press in a grid slot is rated and arms a swing. |
| Controls | Arrows/WASD for gameplay. Menus also take Enter, Space and the mouse. Esc pauses. |
| Starting mid-song | `?start=` fast-forwards with the `moderate` bot, then hands control to the keyboard. |
| End of run | The end card plus a letter grade, the saved best grade, and a deterministic replay of the run. |
| Play-mode tuning | A separate level file, `levels/orpheus-galop.play.yaml`, that extends the demo's level script. |

### Goals

- **The real game.** Every hero movement comes from the keyboard, and every
  rule from [beat-steps.md](beat-steps.md) still holds: steps on presses,
  automatic aim, strikes on the grid, and a groove meter that rewards and
  never punishes.
- **The demo's moments, earned.** A moderately competent player gets the
  tutti-chord cleaves, the kick-line ripple, the archer near misses, the
  boss dying on its chord and the finale blast.
- **Every impact still on the beat.** Live play keeps the demo's sync: sword
  impacts and their sounds land on the grid, with no added audio lag.
- **Responsive.** A press moves the hero on the next displayed frame.
- **The demo untouched.** One engine serves both modes, and golden plan hashes
  prove the demo's output is byte-identical.

### Non-goals

- Touch or gamepad input.
- New enemies, weapons, levels or songs.
- Difficulty settings beyond the separate play-mode tuning.
- Online leaderboards. The best grade is local only.

## Related Information

- [Beat steps and a simulated playthrough](beat-steps.md): the game rules
  this plan makes playable. Read it first. This plan assumes its terms:
  step and free movement, timing grid, groove, cleave, auto-aim, spawn
  director, formations, hit-stop and the virtual player.
- [Tech demo design](tech-demo.md): the song map, time warp, juice, sound
  mixing, assets and the renderer.
- [README](../../README.md): commands, URL parameters and checks.
- [AGENTS.md](../../AGENTS.md): how each task is delivered.
- Key source files:
  - [`src/sim/simulate.ts`](../../src/sim/simulate.ts): the simulation. It
    is already one causal event-queue loop, the basis for live play.
  - [`src/sim/player.ts`](../../src/sim/player.ts): the `Player` interface
    and the `VirtualPlayer`.
  - [`src/planner/plan.ts`](../../src/planner/plan.ts) and
    [`src/planner/emit.ts`](../../src/planner/emit.ts): recording, then
    effects, sounds and HUD events.
  - [`src/plan/evaluate.ts`](../../src/plan/evaluate.ts): the frame
    evaluator, a pure function of the plan and song time.
  - [`src/runtime/app.ts`](../../src/runtime/app.ts) and
    [`src/runtime/audio.ts`](../../src/runtime/audio.ts): the page, the frame
    loop, the audio clock and sound scheduling.
- [Vampire Survivors](https://poncle.itch.io/vampire-survivors): the
  reference for the open field and spawning around the player.

## Proposed Design

Both modes share one simulation engine. They differ in three places:

- **Who presses the keys:** the virtual player, or the keyboard.
- **The world:** the demo's forest corridor, or play mode's open field.
- **When the simulation runs:** all at once at load time, or live, a frame
  at a time.

```text
demo:  level.yaml ─► Sim(CorridorWorld, VirtualPlayer) ─run to end─► Plan ─► Evaluator ─► renderer
play:  level.play.yaml ─► Sim(OpenWorld, keyboard) ─live, per frame─► LiveRecording ─► Evaluator ─► renderer
```

### One engine, a frozen demo

Today `simulate()` is a single function that runs to the end of the song.
It becomes a steppable class, and `simulate()` becomes a thin loop over it:

```ts
class Sim {
  constructor(ctx: Ctx, sch: Schedule, world: World, rules: Rules, rec: Recorder);
  /** Run game rules up to song time s (exclusive of events after s). */
  advanceTo(s: number): void;
  /** Apply a key event at its exact song time (>= the current time). */
  key(e: KeyEvent): void;
  observe(): Observation;
  /** A deep copy for rollback; shares nothing mutable with this one. */
  clone(): Sim;
  readonly s: number;
}

export function simulate(ctx, sch, map, opts): SimResult {
  const sim = new Sim(ctx, sch, new CorridorWorld(map), DEMO_RULES, new BatchRecorder());
  // Same order of events as today: decide, push keys, advance.
  ...
}
```

- **`World`** is the interface for everything the map decides: the edge
  distance, spawn points, recycling, the camera lead and the observation's
  pace point. `CorridorWorld` wraps today's corridor code unchanged.
  `OpenWorld` is new. See [The open field](#the-open-field) below.
- **`Rules`** holds the play-mode differences: one step per grid slot, the
  timing windows and the hero overrides. `DEMO_RULES` is today's behavior.
- **`Recorder`** receives sim events (samples, hits, swings, inputs, groove,
  warp breaks). `BatchRecorder` builds today's `SimResult`.
  `LiveRecording` grows as play goes on, and can be truncated for rollback.
- **Emission becomes per event.** `emit()` today loops over the whole
  `SimResult`. Its body splits into per-event functions (`emitSwing`,
  `emitDeath`, `emitLanding` and so on). The batch `emit()` calls them in
  the same order as today, and `LiveRecording` calls them as events happen.

**Golden hashes keep the demo frozen.** The first commit records
`planHash` for seeds 1858, 1859, 7 and 2024 under the `perfect`, `skilled`
and `casual` profiles in a test fixture. Every later commit must reproduce
them exactly. A refactor that changes event order, RNG draws or float
rounding fails this test. Fix the refactor; never update the fixture.

### The live loop

In play mode the simulation runs on the main thread, inside the frame loop.
Sim cost is about 1/60 of real time (3 s for the 3-minute song), so a frame
of play costs well under 1 ms. The renderer's frame budget is unchanged.

Two problems shape the loop:

- **Presses arrive late.** A key event is delivered at the next task, up to
  a frame after its `timeStamp`. The game rules want it at its exact time.
- **Sounds must be scheduled early.** The audio scheduler queues sounds up
  to 150 ms ahead, plus each sample's attack lead. A kill sound on a strike
  must be scheduled before the strike resolves.

Both problems are solved by **rollback with speculation**:

- **Settled time:** `sSettled = now - 40 ms`. No key event can still arrive
  for earlier times, so the sim's state there is final.
- **Speculation:** each frame the sim runs ahead to `now + 200 ms`,
  assuming no new presses. The renderer and the audio scheduler read this
  speculative recording.
- **Snapshots:** the sim is cloned at each frame's `now`. Snapshots older
  than `sSettled` are dropped, except the newest one before it.
- **A new key event at time t:** restore the newest snapshot at or before
  `t`, truncate the recording there, apply the key at `t`, and run ahead to
  `now + 200 ms` again.
- **A very late event** (a main-thread stall) with `t < sSettled` is applied
  at `sSettled` instead. This is rare and only affects responsiveness.

```ts
function frame(now: number): void {
  for (const e of input.drain()) {          // key events, in song time
    const t = Math.max(e.s, settled);
    const snap = snapshots.latestAtOrBefore(t);
    sim = snap.clone();
    recording.truncate(snap.s);
    audio.cancelAfter(snap.s);              // only unstarted sources
    sim.advanceTo(t);
    sim.key({ ...e, s: t });
  }
  sim.advanceTo(now);
  snapshots.push(sim.clone());
  sim.advanceTo(now + SPECULATE_S);         // assumes no new presses
  settled = now - SETTLE_S;
  snapshots.dropBefore(settled);
}
```

- **Causal by construction:** speculation only assumes the absence of
  presses. Game rules still read only presses that have happened.
- **Equivalent to a replay:** the settled recording equals a batch run fed
  the same key events. The [live/replay check](#automated-checks) enforces
  this.
- **Cost:** one clone per frame (about 520 enemies), plus a re-run of up to
  240 ms of sim after each press, roughly 4 ms. Presses come at most a few
  times a second, so the p95 frame budget holds. The benchmark gets a
  play-mode run to prove it.

### Live recording and the evaluator

The evaluator reads a `Plan`, whose tracks are fixed-length typed arrays,
plus precomputed active-entity buckets. It gets a read-only `PlanView`
interface instead:

- **`Plan`** implements `PlanView` as today.
- **`LiveRecording`** implements it with growable typed arrays (doubling
  capacity, a logical length) and incremental buckets. `truncate(s)` cuts
  every track, event list and bucket back to song time `s`.
- **The warp** gains `append()` and `truncate()`. `Warp.toWorld` already
  works on a breakpoint list.
- **Evaluator caches** such as `walkedCache` are keyed by enemy id and
  sample count, so a truncated enemy's prefix sums are rebuilt.

### Sounds

- **Cancellable sources:** `AudioEngine` keeps each scheduled but unstarted
  source with its event's identity: sample, song time and source entity. On
  a rollback it calls `stop()` on any that the new run no longer contains.
  It then schedules what the new run adds.
- **Swing and impact sounds never change:** a strike is armed at least 90 ms
  ahead, so its swing and impact sounds are known before they are
  scheduled. Only outcome sounds (kills, flashes, near misses) can be
  revised, and only by a press in the last 150 ms before them.
- **Voice budget:** still enforced as sounds are emitted. Rollback re-emits
  from the snapshot, so the budget is recomputed for the new run.

### Input and latency

- **Keys:** arrows and WASD map to `up`, `left`, `down`, `right`. Repeat
  events (`e.repeat`) are ignored. Key-ups are releases.
- **Song time of a press:**
  `sPress = songTimeAtPerformanceNow(e.timeStamp) - inputOffset`.
  `songTimeAtPerformanceNow` uses the same mapping as
  `songTimeAtPresentation`, including `outputLatency` and `?avOffsetMs`.
- **`inputOffset`** comes from calibration, default 0, stored in
  `localStorage` under `ps.inputOffsetMs`. All storage access is wrapped in
  try/catch.
- **Calibration:** an optional title-screen step. The player taps any arrow
  along to 8 clicks at 120 BPM. The offset is the median tap error, after
  dropping the first two taps. It can be redone at any time.
- **Blur:** losing focus releases all held keys at the blur time, then
  pauses. See [Pause and restart](#pause-and-restart).

## Play-Mode Rules

Play mode runs the [beat-steps.md](beat-steps.md) rules, with the changes
below. Each change lives in `Rules`, set from the play level file, so the
demo never sees it.

### A calm prelude, not a slow one

The demo plays its prelude in slow motion (`timeScale: 0.5`). Played, that
halves the hero's walking speed, and walking through molasses feels awful.
Play mode keeps the world at full speed there and gives the music's calm to
the enemies instead:

- **`calm`** (a section field, 0 to 0.9, default 0, not inherited) scales
  every enemy's speed by `1 - calm` and turns its approach up to 60° off
  the line to the hero (`calm × 60°`), half of them each way, so they
  spiral in lazily rather than charge.
- The play level sets the prelude to `timeScale: 1` and `calm: 0.5`: the
  hero walks at its full 80 px/s, and even a bat closes more slowly than
  the hero walks.
- **`autoSwingBeats`** (a section field, default 2, not inherited) sets how
  often the sword swings by itself in free movement. The play level's
  prelude swings on every beat, twice the demo's rate, so the calm stretch
  still keeps the field clear.

### One step per grid slot

In step movement, every press belongs to its nearest grid time. Only the
first press for each grid time is rated and arms a swing, but every press
steps: dropping a movement press feels like lost input.

- **Extra presses:** a second press for the same grid time steps like any
  other and arms nothing. Rated Great or better against the half-way point
  to the neighbouring grid time (an eighth note on a beat grid), it shows
  that rating on the key display and lifts the groove past full, up to +25%
  damage (`offbeatBonus`; on-grid presses alone top out at +20%). Otherwise
  it flashes grey and leaves the groove alone: a loose extra press never
  costs anything.
- **Diagonals:** a perpendicular key within 40 ms bends the step, as
  today. It is part of the same step, not a second press.
- **Off-beat presses are limited, never punished:** a press halfway between
  two beats belongs to one of them. A player can step at most once per grid
  time, about 3 steps a second in theme A.
- **Free movement is unchanged:** held keys walk.

### Looser timing windows

Starting values, to be tuned:

```yaml
# levels/orpheus-galop.play.yaml
hero:
  timing: { perfectMs: 45, greatMs: 90, goodMs: 140, floor: 0.1 }
```

Cleaves still need a press rated Great or better.

### The open field

Play mode has no corridor and no route. The hero can walk anywhere.

- **Chunks:** the ground is generated in 512 px chunks around the camera.
  Each chunk is a deterministic function of the seed, its coordinates and
  its zone. Chunks more than 3 views away are dropped from memory and
  regenerated identically when needed.
- **Props never block:** the hero and enemies pass through everything. Tall
  props are placed only where they would not hide the action. When the hero
  comes within 80 px of one, it fades to 30% opacity.
- **No walls:** steps are never shortened. `World.edge` returns `Infinity`.
- **Heading:** the hero's average step or walk direction over the last two
  beats. When the hero stands still it keeps its last value, starting with
  the direction to the right. Several rules use it in place of the route
  direction.

### The transform wave

A zone change happens on the music, wherever the hero is:

- **When:** on the first downbeat of a section whose zone differs from the
  previous section's.
- **How:** a ring expands from the hero's position in sixteenth-note bands
  until it leaves the screen, like the finale blast. Ground and props
  re-theme as it passes, with a soft flash of the new zone's color.
- **Afterwards:** every point reached by the wave, and every point once the
  wave has left the screen, uses the new zone. A chunk's zone at song time
  `s` is the zone of the newest wave that has reached it.
- **Sound:** a low sweep on the downbeat from the section's palette, under
  the music.

```ts
function zoneAt(p: Vec, s: number): ZoneId {
  for (let k = waves.length - 1; k >= 0; k--) {
    const wv = waves[k];
    if (s >= wv.s && (wv.offscreenAt <= s || dist(p, wv.origin) <= wv.radiusAt(s))) return wv.zone;
  }
  return firstZone;
}
```

### Landmarks

- **Placement:** each landmark is stamped centred on the hero's position on
  its section's first downbeat, at the same moment as the transform wave.
- **Entrance:** it rises from the ground over one bar, with dust on the
  downbeat.
- **The boss arena:** the standing stones frame the climax around where the
  fight starts. The boss's landing point is chosen relative to the hero, as
  today.
- **The ruined shrine** rises on the finale chord with the blast.

### Spawning and recycling

The spawn director's counts, types and timing do not change. Only the
placement does:

- **Where:** just outside the camera view. 60% of spawns come from the
  forward half, centred on the hero's heading. The rest come from anywhere
  around.
- **Recycling:** an enemy more than 1.5 view widths from the hero, in any
  direction, moves to a new spawn point in the forward half.
- **Formations** enter from their side of the screen, relative to the camera
  at that moment, as today.

### Camera

- **Follows the hero** with the same critically damped spring.
- **Lead:** the section's `camera.lead` is measured along the hero's heading
  instead of the route.
- **Kick line:** while a formation is on screen, the lead points toward the
  formation's entry side, so the line stays in view.

### Accent telegraphs

A first-time player must be able to see the cleaves coming. The song owns
the accents, so the HUD may show them in advance:

- **Accent ticks:** on the beat track, ticks for accents that will cleave
  (above the section's `heavyOnAccents` threshold and label) are large and
  gold.
- **"Press here":** the grid tick one grid step before each such accent has
  a gold ring. A Great press there arms the cleave.
- **The heart glows** over the beat before the accent.
- **Demo:** these telegraphs are off in demo mode, so the demo's HUD stays
  unchanged.

### Level-ups, the boss and the finale

The rules are unchanged. Play mode only tunes the numbers:

- **XP requirements** in the play level file are set so the `moderate` bot
  fills each bar a little before its scheduled level-up.
- **Boss health** is shared with watch mode. A boss the player hasn't
  killed is caught by the finale blast.
- **The finale blast** still clears the screen on the final chord, whatever
  the player has done.

## The Play Level File

`levels/orpheus-galop.play.yaml` extends the demo's level script. It
restates only what differs:

```yaml
extends: orpheus-galop
world: open                 # the demo's default is corridor
rules: { oneStepPerSlot: true, accentTelegraphs: true }
hero:
  timing: { perfectMs: 45, greatMs: 90, goodMs: 140, floor: 0.1 }
player: moderate            # the bot used for ?start= and for checks
sections:
  - section: climax
    camera: { lead: 0 }
events:
  - at: { section: galop-opening, point: start }
    xp: 180
```

- **Level `extends` is new.** Today only content definitions support
  `extends`. The loader gains it for level scripts:
  - top-level fields replace the parent's;
  - `sections` merge by `section` label, field by field;
  - `events` merge by their `at` plus the event kind.
- **Validation:** the merged level runs through the existing schema, with
  line-numbered errors pointing at the file that set each value. A broken
  fixture covers an unknown parent and an unknown section label.
- **`pace`** has no route to follow in the open field. Play mode
  reinterprets it as the bot's roam speed (see below). The pace-reachability
  check still runs.
- **`hero:` overrides** merge over `content/hero.yaml` for this level only.

## The `moderate` Virtual Player

The `moderate` profile is the automated stand-in for a moderately competent
human. It drives play mode through the same `KeyEvent` path as the
keyboard.

```text
profile    timing error          presses                                  reaction
moderate   normal, sd 30 ms,     85% of the section's pattern slots       250 ms
           clamped to +/-100 ms  (seeded), plus 80% of accent setups
```

- **Pattern:** each pattern slot is pressed with probability 0.85, and
  each accent setup press with probability 0.8, from seeded random draws.
- **Threats:** it reacts to an arrow or slam marker 250 ms after it becomes
  visible, instead of 150 ms.
- **Steering:** the open field has no pace point, so the cost function's
  first term follows a **roam point** instead. The roam point starts at the
  hero, moves at the section's `pace` along a heading that turns slowly
  (seeded, at most 45 degrees per bar), and is pulled toward the crowd's
  centre of mass when the crowd is more than 200 px away.
- **Sloppiness:** with probability 0.15 per press, it picks its
  second-best direction instead of the best.
- **`?player=moderate`** also works in demo mode for comparison, but its
  checks are only required in play mode.

## Modes, Menus and Flow

### Title screen

- **Choices:** **Play** (the default), **Watch the demo**, and **Calibrate**.
- **Controls:** Up and Down (or W and S) move the selection. Enter, Space,
  Right or a click chooses. The click or key also unlocks audio, as today.
- **The best grade** for the level is shown beside Play, if there is one.
- **URL parameters:**
  - `?mode=play` and `?mode=demo` skip the title screen choice. The audio
    unlock click is still needed.
  - `?player=`, `?capture` and `?bench` imply demo mode, so all existing
    tooling and URLs keep working.
  - `?mode=play&player=moderate` precomputes a play-mode run with the bot
    and plays it back like the demo. This is for captures, QA and tuning.

### Starting mid-song

`?mode=play&start=1:10`:

- **Fast-forward:** during loading, the `moderate` bot plays the
  simulation headless up to the start time. The progress bar covers it.
- **Hand-over:** the bot stops, its held keys are released, and the
  keyboard takes over at the start time.
- **A one-bar count-in** precedes the start: the music starts one bar early
  with the beat track's count marks, and input begins on the bar line.
- **The recording** marks the bot's part, so the end card notes "started at
  1:10" and the grade counts only the player's part.

### Pause and restart

- **Esc** (or losing focus) pauses. The audio context is suspended, so song
  time stops. Key events while paused are ignored, and held keys are
  released.
- **The pause menu:** Resume, Restart section, Restart song, Quit to title.
- **Resume** continues after a one-bar count-in. The music rewinds to one
  bar before the pause point and replays that bar with count marks, while
  the game stays frozen and its sounds stay silent. The game resumes
  exactly at the pause point.
- **Restart section:** the sim keeps a snapshot at each section's start.
  Restarting restores it, truncates the recording, and resumes with a
  count-in from the section's first bar.

### End of run

- **The end card:** kills, Perfect, Great and Good counts, average groove,
  and a letter grade, then the credits roll.
- **Grade:** from the average groove over step sections. Starting
  thresholds are S at 0.85 or more, A at 0.7, B at 0.5 and C below that.
  They are tuned so the `moderate` bot earns an A.
- **Best grade:** saved in `localStorage` under `ps.best.<level>`.
- **Menu:** Play again, Watch my run, Watch the demo.
- **Watch my run:** the recorded key events are replayed through the
  engine in batch mode, producing a `Plan` that plays back like the demo,
  with the play-mode world and HUD. The replay must be identical to the
  live run (see [Automated checks](#automated-checks)).

## HUD Changes

- **Accent telegraphs** on the beat track, as described above. These are
  play mode only.
- **Extra presses** flash grey on the key display.
- **Count marks** for resume and mid-song starts reuse the step-section
  count-in.
- **The pause menu and end-card menu** are drawn by the HUD, in the demo's
  type and colors.
- **Everything else** (key display, groove meter, callouts, end card) is
  shared with the demo.

## Automated Checks

### The frozen demo

- **Golden hashes:** `planHash` for seeds 1858, 1859, 7 and 2024 under
  every demo profile matches the fixture recorded before any refactor. This
  runs in `npm run test`.
- **Existing checks:** `check:plan`, `check:determinism` and `check:seeds`
  are unchanged and still pass.

### Play mode (`npm run check:play`)

These checks run the `moderate` bot through play mode headless, on seeds
1858, 1859, 7 and 2024. Every failure names the time and entity involved.

- **Shared checks:** impacts on the grid, the sound budget, causality,
  every movement has an input, no skating, auto-aim, damage adds up,
  determinism, off-screen spawns, population, spacing and a clean ending.
  These run with the same code as the demo's checks.
- **One step per slot:** no two rated presses share a grid time.
- **Signature moments for `moderate`:**
  - At least 80% of cleave-eligible accents produce a cleave.
  - Every kick-line skeleton dies within two beats of the release.
  - At least 75% of threats end as near misses.
  - Every scheduled level-up happens on its scheduled moment.
- **Zone waves:** every transform wave starts on a downbeat and has left
  the screen within one bar.
- **The reward spectrum:** the `casual` bot in play mode reaches the
  finale, and the blast clears the screen.
- **Report:** kills, average groove and grade for `moderate` and `casual`,
  side by side.

### Live play

- **Live equals replay** (unit test): drive a `LiveRunner` with a scripted
  frame clock and key events delivered up to 16 ms late. Include presses
  that land inside a speculation window and during a hit-stop. The settled
  recording's hash must equal a batch run fed the same key events.
- **Rollback hygiene** (unit test): after a rollback, the recording, the
  warp, the buckets and the evaluator's caches equal a fresh run's at every
  sampled time.
- **Cancelled sounds** (unit test against a fake audio context): every
  scheduled, unstarted source missing from the new run is stopped. No
  source is scheduled twice.
- **Latency mapping** (unit test): `timeStamp` to song time, including
  `outputLatency`, `?avOffsetMs` and the calibration offset.
- **End to end** (Playwright, `tests/e2e/play.spec.ts`): open
  `?mode=play&start=1:10&test`. Send synthetic arrow presses on the beat for
  four bars. Check that the hero moved one step per press, that swings
  happened, that a mashed press was rated once, and that Esc pauses and
  resumes.
- **Benchmark:** `npm run bench -- --play` plays theme A to the climax with
  synthetic presses on every beat. It must stay within the existing frame
  budgets: 8 ms JavaScript and 10 ms GPU at p95, no frame over 33 ms.

## Implementation Plan

Each task passes `scripts/ci` and is delivered through its own Tollgate
worktree, as described in [AGENTS.md](../../AGENTS.md). Tasks marked
**Gate** end with a human review in the browser.

1. **Golden demo hashes.**
   - Record `planHash` for every seed and demo profile in a fixture, and
     add the test.
   - No behavior changes.
2. **A steppable engine.**
   - `Sim` class with `advanceTo`, `key`, `observe` and `clone`.
     `simulate()` becomes a loop over it.
   - The `World`, `Rules` and `Recorder` interfaces, with `CorridorWorld`,
     `DEMO_RULES` and `BatchRecorder`.
   - Per-event emission functions behind the batch `emit()`.
   - Golden hashes unchanged.
3. **Play level file and rules.**
   - Level `extends` in the loader, with broken fixtures.
   - `levels/orpheus-galop.play.yaml`, the `world`, `rules` and `hero`
     overrides.
   - One step per slot, the looser timing windows, and the `moderate`
     profile, with unit tests.
4. **The open field.**
   - `OpenWorld`: chunks, heading, spawning, recycling and the camera lead.
   - Transform waves and landmarks around the hero.
   - Chunked terrain in the renderer, with prop fading and wave re-theming.
   - The bot's roam steering, and `?mode=play&player=moderate` as a
     precomputed run.
   - `check:play`, with its checks passing or reported for tuning.
   - **Gate:** watch `?mode=play&player=moderate` from the start, and at
     `?start=1:10` and the climax.
5. **Live play.**
   - `LiveRecording`, `PlanView`, the growable warp and truncatable
     buckets.
   - `LiveRunner`: snapshots, speculation and rollback.
   - Cancellable sounds in `AudioEngine`.
   - Keyboard input and the latency mapping.
   - The live-equals-replay, rollback, sound and latency tests, and the
     play-mode benchmark.
   - **Gate:** play theme A by hand at `?mode=play&start=1:10` with
     headphones. Steps feel instant, and impacts are on the beat.
6. **Flow and menus.**
   - The title screen, `?mode=`, calibration, pause and resume with
     count-ins, restart section and song, blur handling, and mid-song
     starts with the bot.
   - The end card's grade, best grade, and Watch my run.
   - The Playwright play spec.
7. **Telegraphs and HUD.**
   - Accent ticks, "press here" rings and the heart glow, play mode only.
   - Grey extra presses on the key display.
8. **Tuning, QA tooling and documentation.**
   - Tune the play file (XP, boss health, timing windows, the bot's
     weights and grade thresholds) until `check:play` passes on all seeds.
   - Add play-mode items to `npm run qa` and the QA report.
   - Update the README (modes, `?mode=`, controls, `check:play`) and link
     this document from [beat-steps.md](beat-steps.md).
   - **Gate:** the full playthrough in [Manual QA](#manual-qa).

## Risks

- **The engine refactor drifts the demo.** Float order or RNG draw order can
  shift silently. The golden hashes catch it on the first commit that
  changes them. Keep the refactor mechanical, and land it before any
  play-mode rule.
- **Clone cost.** If cloning 520 enemies per frame is too slow, snapshot
  every other frame instead. Rollback then re-runs up to 33 ms more sim.
  Moving enemy state to typed arrays is the larger fallback.
- **Speculation churn.** Every press re-runs up to 240 ms of sim and may
  revise outcome sounds. If revised kill sounds are audible as dropouts,
  shorten the outcome-sound lookahead to 60 ms. Their attack leads are
  short.
- **The open field loses the demo's sense of travel.** The transform waves
  and landmarks carry the progression instead. Judge this at the task 4
  gate.
- **Humans are not bots.** The `moderate` profile is a guess. The task 5 and
  task 8 gates are where it gets calibrated against real play. Expect to
  retune its sd, press rate and reaction time.

## Manual QA

Run the dev server from a worktree (`npm run dev`, port from `npm run port`),
open it in desktop Chrome with wired headphones, and choose **Play**.

### Feel

- **A press steps at once.** There is no perceptible delay between a key
  and the hop.
- **Impacts are on the beat** to the ear, however sloppy the presses.
- **Mashing** moves the hero at most once per beat, and the key display
  greys the extra presses.
- **Diagonals:** pressing Up and Right together steps diagonally the same
  44 px.
- **Holding a key** in step movement does nothing after the first step.

### The set pieces, played

- **Prelude:** held keys walk smoothly, at full speed, across the open
  field. The sword swings by itself, and the enemies drift in calmly.
- **Count-in** before the galop, then the first transform wave sweeps out
  on the downbeat.
- **Galop opening:** the gold "press here" rings line up the tutti chords,
  and a Great press cleaves with a hit-stop.
- **Theme A:** a step and a swing on every beat. Enemies pour in around the
  hero, mostly from the direction of travel.
- **Kick line:** the line stays on screen, dances, and charges. A cleave on
  the release accent ripples through it.
- **Archers:** arrows are telegraphed. A sidestep earns a near miss and
  "PERFECT".
- **Boss:** the standing stones rise around the hero. Slam markers can be
  stepped off. With moderate play, the boss dies on its chord.
- **Finale:** the shrine rises and the blast clears the screen. The end card
  shows a grade.

### Flow

- **Esc** pauses and resumes with a count-in. Restart section returns to the
  section's first bar with the same world.
- **Switching tabs** pauses the game.
- **`?mode=play&start=2:30`** starts mid-song in a believable world.
- **Calibrate** with a known offset (for example, Bluetooth headphones),
  and Perfect ratings become reachable.
- **Watch my run** replays the run just played, identically.
- **Watch the demo** plays the unchanged scripted demo.
