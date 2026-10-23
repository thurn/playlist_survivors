# Playlist Survivors

A browser-based rhythm game level set to Offenbach's *Orpheus in the
Underworld* overture (the slow passage, then the Infernal Galop). You press the
arrow keys (or WASD) to the beat; in play mode the hero strides at one steady
speed however you press, and the sword and every other weapon fire by
themselves on the beat, growing with the tier your groove reaches. Every strike
and impact sound lands on the music, through a horde of up to ~500 LPC
monsters.

It has two modes:

- **Play** — the real game. The keyboard drives the game rules live, in an
  open field that re-themes itself in transform waves on the music. Designed in
  [docs/design/interactive.md](docs/design/interactive.md), with today's rules in
  [docs/design/gameplay-redesign.md](docs/design/gameplay-redesign.md).
- **Watch the demo** — a recorded run of the skilled virtual player under the
  same rules, in the same open field.

The original tech demo's design is in [docs/design/tech-demo.md](docs/design/tech-demo.md)
(its step gameplay, [docs/design/beat-steps.md](docs/design/beat-steps.md), is gone: the gameplay
redesign replaced it). Both modes run one **simulated playthrough**
engine: the game rules run forward in time, driven by key presses, and never
read a press before it happens. The demo records its simulation at load, then
plays it back (each frame a pure function of the audio clock's song time).
Play mode runs it live on the main thread, a frame at a time, ahead of the
music with rollback when a press comes in, so sounds can be scheduled early
and every impact still lands on the beat.

## Running it

Requirements: Node 24+, and [uv](https://docs.astral.sh/uv/) with Python 3.12
for the audio analysis tool.

```bash
npm install
npx playwright install chromium   # for captures, the benchmark and e2e tests
npm run dev                        # http://localhost:5199
```

The main checkout always serves on port 5199 (preview on 5198). Each git
worktree gets its own stable port in 5200–5999 instead, hashed from its path, so
it never touches the main server and rarely meets another worktree; `npm run port`
prints it, and `PORT` overrides it.

Open the page in desktop Chrome, wait for the progress bar, then choose
**Play** (the default), **Watch the demo** or **Calibrate** (the choice also
unlocks audio). Use wired headphones or speakers.

### Controls

| Key | In play | In menus |
|---|---|---|
| Arrows or WASD | Move in fixed-speed strides, 140 px/s however you press: every press strides at once with the ground built up since the last stride (a full stride, about 45 px, after a beat's pause), and a held key strides on every beat (every eighth note when the beat is slow). Each press is classed against the beat: the first for its beat is counted and rated (Perfect, Great, Good or loose), an eighth note on the off-beat after a Great one adds a little, a quick double tap is grey and changes nothing, and any other extra press is stray, shown red: it still strides but takes a chunk off the groove. The groove (0 to 1) drains a bar after the last press that raised it, and its tier (0 to 3, at 0.45, 0.60 and 0.85) chimes when it changes. In free sections held keys walk. Two perpendicular keys within 40 ms make one diagonal stride. | Up/Down (W/S) move the selection; Right chooses |
| Space, or two opposite keys within 40 ms | Stomp: press without moving, rated like any press. Opposite keys rock the first key's stride out and back. While a direction is held, a stomp skips the next held stride (the next two when the beat is slow), so holding and stomping on every beat stands still. | (Space chooses, below) |
| Enter, Space, a click | — | Choose |
| Esc | Pause (losing focus pauses too) | Resume |

The pause menu has **Resume**, **Restart section**, **Restart song** and **Quit
to title**; play resumes after a one-bar count-in. Enemies that touch the hero
bite, and arrows and slams hurt: when the HP bar runs out, the game stops on a
**YOU FELL** menu (Restart section, Restart song, Quit to title). After a hurt
the hero blinks, safe for half a second; a level-up heals 20, and chickens
(dropped by half the elites, and two by the boss) heal 30. The end card shows a letter
grade from the average groove (S, A, B, C), the best grade so far (saved in
this browser), and **Play again**, **Watch my run** (an exact replay) and
**Watch the demo**.

**Calibrate** (on the title screen) plays 8 clicks at 120 BPM: tap any arrow
along with them. The median tap error, after the first two taps, becomes the
input offset (`ps.inputOffsetMs` in local storage), so presses are judged as
you hear them. Redo it any time.

### Timing log

Play mode logs every gameplay key from the DOM event to what the game did with
it: the audio clock it was timed by, when the live runner applied it (and any
rollback), and the simulation's outcome and reason (a stride with its class and
rating, a stomp, a bend, a walk, or a key thrown away by a count-in), with the
stride's length and the budget left, the groove before and after, and the tier.
Frames, sound scheduling, the groove and tier over time, the meter's values,
the sounds moved to a later slot, gem pickups per sixteenth and vacuums, and the
hero's path are logged too. See [`src/live/log.ts`](src/live/log.ts).

- The dev server saves it to `logs/play/<session>.json` on pause, at the end,
  on restart, every 15 s while playing and on leaving the page.
- F8 downloads it (any build).
- `npm run log:play` summarizes the newest log: outcome counts, presses by class
  and time at each tier per section, the hero's speed over each second, timing
  offsets from the grid and the beat, the input path, the audio clock and
  frames, then one row per press. It also re-rates the log's presses under the
  current groove rules (grid times from the song map, so logs from before press
  classes re-rate too) and prints the classes and tiers beside the log's own, and
  lists the sounds moved to a later slot (known less than 60 ms before their own),
  and the gems collected against dropped, with each vacuum.
  Pass a file, `--from`/`--to` (song seconds), `--no-table` or `--no-rerate`.

### URL parameters

| Parameter | Effect |
|---|---|
| `?mode=play` / `?mode=demo` | Skip the title screen's choice (the one click to unlock audio is still needed). Watch the demo is the skilled virtual player's run under play mode's rules, in the open field (one level script, `levels/orpheus-galop.yaml`, drives both; the play level only sets the moderate player). |
| `?mode=play&player=moderate` | Plan a play-mode run with a virtual player and play it back like the demo (captures, QA, tuning). |
| `?metronome` | Full-screen flash on every beat plus a click track over the music (the sync check). |
| `?mode=play&debug=moves` | Play mode with a movement overlay: the hero's speed over the last second, the stride budget, the groove and tier, and the last eight presses with their class and offset (for the Manual QA of docs/design/gameplay-redesign.md). |
| `?start=1:35` | Start from a song time, in seconds (`?start=95.5`) or `m:ss`. In play mode the level's virtual player plays up to there headless, then hands over after a one-bar count-in. |
| `?capture&t=88.31` | Render one frozen frame at song time 88.31 s, without audio. |
| `?avOffsetMs=-40` | Shift the picture relative to the audio (e.g. Bluetooth latency). |
| `?seed=1859` | Re-simulate, and regenerate the map, with a different seed. |
| `?player=casual` | Re-simulate with another virtual player: `perfect`, `skilled` (the demo level's default), `casual` (downbeats only), `moderate` (play mode's stand-in for a moderately competent human), `eighths` (every eighth note, 12 ms deviation), `holder` (holds the direction it steers, changing it at most once a bar, and never taps), or `mash2`, `mash4` and `mash8` (random presses at 2, 4 or 8 a second). |
| `?test` | Expose the `window.__demo` test hook (`ready`, `renderAt(s)`, `stats()`; in play mode also `live` and `play`). |
| `?bench` | Play the whole song (Watch the demo) on a virtual clock and record frame times (`&mode=play`: live play from theme A to the climax). |

`?player=`, `?capture`, `?bench`, `?test` and `?metronome` imply the demo, so
existing tools and URLs keep working.

## Checks

Every important property is checkable by a command:

| Command | What it checks |
|---|---|
| `npm run check` | Lint, type-check and unit tests (Vitest). |
| `npm run check:songmap` | The song map against the beat-grid acceptance thresholds. |
| `npm run check:content` | Every content file and level script against its schema, plus the deliberately broken fixtures in `tests/fixtures/broken/` (each must fail with a useful, line-numbered message). |
| `npm run check:assets` | Licences, credits, atlas manifests, and that every sprite/animation/sound named in content exists. |
| `npm run check:plan` | Runs the simulated playthrough with all its checks, including causality (re-simulating with the presses after a cut removed must change nothing before it), then the casual player's too, and prints a one-line summary of each (enemies, kills, presses, groove, near misses) with the two players' kills and groove side by side. `-- --seeds=1859,7` for other seeds, `-- --player=perfect` for another profile. |
| `npm run check:determinism` | The level's seed simulated twice; plan and map hashes must match. |
| `npm run check:seeds` | The level's seed with causality and determinism, then the checks on three more seeds (`?seed=` must still pass every check). In CI. |
| `npm run check:play` | Play mode, headless: the `moderate` and `casual` virtual players and mashers at 4 and 8 presses a second through the open field on four seeds, and `perfect`, `skilled`, `eighths`, `holder` and `mash2` on two (`-- --seeds=` and `--extra-seeds=` pick others), with the checks the demo shares plus play mode's own (every gameplay sound on its subdivision's slot within 1 ms, as the table in the design's Sound on the Grid says; no dead presses, the speed cap, holding keeps full speed, transform waves off screen within a bar, the finale, and the moderate player's signature moments: 80% of eligible accents cleaved (a shockwave its press on the chord launched), the kick line down within two beats, 75% near misses, the boss on its chord, level-ups on time with an orb walked into at each, 60% of the experience dropped collected). Mashers must earn a C and fewer kills than `moderate`; every player passes the movement checks and the sounds on the grid; the honest players (`perfect`, `skilled`, `eighths`, `moderate`, `casual`) must survive, and the holder and mashers must fall between 1:30 and 2:10 (the design's difficulty curve; the honest players' end health, averaged over the seeds, is printed against its band but not gated, as the design's note on the curve records). The groove checks: after the galop vamp `moderate` is at tier 2 or higher for 70% of pulsed time, the holder and mashers at tier 0 for 80% of it, and `eighths` averages within 0.05 of `skilled`'s groove. Prints kills, groove, grade, time at tier 0 and at tier 2 or higher, and lowest health side by side. In CI. |
| `npm run capture -- --times 12,35.2,88.31,141.5` | Headless-Chrome screenshots at exact song times into `captures/`. `--sections` captures the start of every section. |
| `npm run bench` | Headed Chrome, whole song at real-time speed: fails if JavaScript frame time exceeds 8 ms or GPU time 10 ms at p95, or any frame exceeds 33 ms. `-- --play` benchmarks live play instead (theme A to the climax, a late-arriving press on every beat, so it includes the rollbacks). |
| `npm run test:e2e` | Playwright: Watch the demo is the skilled player's run in the open field with tiers and upgrade orbs, capture hook, metronome flash on the beat, broken level script shows an error and never starts, reload uses the cached plan, `?player=` re-simulates with another profile, `?mode=play&player=holder` and `mash4` plan and play back; and play mode at `?start=1:10` (a stride per press, the sword on every beat, a mashed press rated once, Esc pauses and resumes). |
| `npm run qa` | Playback in Chromium over the Manual QA items a machine can judge: simulation time, the skilled and casual players' sounds on the grid (every row of the design's Sound on the Grid table), impacts on the grid and the reward spectrum, audio clock rate, picture/audio offset, frame rate, hidden-tab recovery (a 3 s main-thread stall), resize letterboxing, click track, finale clearance, and a listening pass over the offline-rendered mix (clipping, effects level, music in front, no wall of noise, effects on time); then live play mode with synthetic key presses (a press strides at once, impacts on the grid, mashing, diagonals, held keys striding on the beat, stomps and rock steps, the same 140 px/s holding, tapping quarter or eighth notes and mashing, frame time, Esc and losing focus, Watch my run identical across a level-up, a mid-song start); writes `captures/qa/report.md` and review frames. `-- --play` runs only the play-mode items. |
| `npm run check:reactive` | Reactive mode ([design](docs/design/reactive-audio.md)): the causality check (on `causality-mix`, every stream entry known after a cut is changed and the song map, simulation, sounds and frames must match the true run before it, with reactive-moderate's and mash4's presses; a negative control that reads the evidence a second ahead must fail), determinism, fresh streams (each shipped stream's `toolVersion` is the tool's), stream quality on `house-128` and `dnb-174`, the gate on the Galop's prelude, sounds on committed beats, and every shipped track end to end; prints committed-beat accuracy per track. `-- --full` adds the causality check on every shipped track, a cut every 10 s. In CI. |
| `npm run check:analysis` | The analysis tool's pytest suite and ruff. |
| `npm run check:all` | All of the above except captures, QA and the benchmark. |

`scripts/ci` (the Tollgate gate, about a minute) runs `check`, `check:content`,
`check:assets`, `check:seeds`, `check:play`, `check:reactive` and the build; `check:analysis` and
`check:songmap` only when `tools/analysis/` or `songs/` differ from master. The
golden demo hashes (`tests/golden/`) are part of `check`: the demo's plan for
every seed and demo profile must stay byte-identical.

## How it fits together

```text
recording ──ps-analysis (crop, analyze, report, edit, check, click)──► songs/orpheus-galop/songmap.json
audio ──ps-analysis stream (npm run reactive:streams, reactive:add)──► public/reactive/<id>/stream.json (reactive mode)
LPC packs ──tools/baker (npm run bake)──► public/assets/ (atlases, effects, sounds, credits)
content/*.yaml + levels/orpheus-galop.yaml + song map ──simulation + virtual player (web worker)──► recorded plan
audio clock → song time s → evaluatePlan(plan, s) → PixiJS renderer + HUD

play: levels/orpheus-galop.play.yaml (extends the demo's) ──Sim(open field, keyboard), live, per frame──►
      live recording (rewindable) → the same evaluator, renderer and HUD; sounds scheduled as they become final
```

- `tools/analysis/` — the Python audio analysis tool (madmom, librosa). The
  beat-map workflow, edit list and report live in `songs/orpheus-galop/`.
- `public/reactive/` — reactive mode's tracks ([design](docs/design/reactive-audio.md)):
  each track's belief stream (what a causal analysis knows of the music, every
  entry stamped with the time it becomes known), the synthetic tracks' Ogg audio
  (`ps-analysis synth`) and `index.json`. `npm run reactive:streams` regenerates
  them all whenever the stream code changes. `npm run reactive:add -- <audio file>
  [--id <id>] [--title <title>]` adds your own track under the gitignored
  `reactive-local/`, which the dev server serves at `/reactive-local/`.
- `tools/baker/`, `assets-src/` — the asset baker: ULPC character compositing,
  the scripted kick animation, white hit-flash frames, terrain and props,
  effects, loudness-normalized sounds, the licence manifest and credits.
- `content/` — the hero (movement, timing, groove, swings, aim), enemies, weapons, formations, effect recipes,
  sound palettes, zone themes and Tiled landmarks (`content/landmarks/*.tmj`).
- `levels/` — level scripts (bars and beats, never seconds). `orpheus-galop.play.yaml`
  extends the demo's level with the moderate player; both modes share one set of rules.
- `src/reactive/` — reactive mode: the belief stream's types, the reactive song map built from its
  committed beats, the director's rules, the reactive virtual players (pressing on a track's
  reference beats) and the track indexes. `levels/reactive.yaml` is its level;
  `src/planner/reactive-checks.ts` its causality check.
- `src/core/` — song map maths, the time warp, geometry, seeded randomness.
- `src/content/` — schemas and YAML loaders with line-numbered errors.
- `src/sim/` — the simulation: the song schedule, the movement model, timing and groove, auto-aim, the
  virtual players and the steppable, cloneable game loop (`Sim`), behind the `World` (the open
  field) and `Recorder` interfaces.
- `src/live/` — live play: the rewindable live recording, the runner (snapshots, speculation and
  rollback), keyboard input and the latency mapping, and cancellable sound scheduling.
- `src/planner/` — the pipeline (schedule, map, simulation, emission), the recording checks and the
  causality check.
- `src/mapgen/` — the open field (chunks, zones, props, transform waves, landmarks stamped around
  the hero).
- `src/plan/` — the plan format and stateless frame evaluation.
- `src/runtime/` — audio clock and sound scheduling, renderer, HUD, the title screen and calibration,
  the app shell and play mode's flow (count-ins, pause, restarts, the end card, Watch my run).
- `scripts/` — the check, capture and benchmark commands.

## Human review gates

These need eyes and ears; the commands above get everything ready.
`docs/qa/qa-report.md` maps every Manual QA item to its automated evidence
and lists what is still open.

1. **Song map** — listen to `tools/analysis/work/orpheus-galop-click.wav`
   (`npm run analysis -- click songs/orpheus-galop/songmap.json`) and confirm the
   beats and section boundaries. `songs/orpheus-galop/form.md` lists the bar
   counts that could not be confirmed against the score.
2. **Metronome** — open `/?metronome`, click, and confirm the flash and heart
   stay locked to the music from first beat to last. Optionally film screen and
   speaker in slow motion during a loud accent: within one frame (~17 ms).
3. **Baked art** — `npm run bake:contact-sheet`, then review
   `captures/contact-sheet*.png`, including the skeletons' can-can kick frames.
4. **Zones** — `npm run capture -- --sections` and review each zone.
5. **Juice** — review captures of the biggest moments (first galop chord,
   the kick line's release cleave, boss slams, boss death, finale blast).
6. **Sound** — listen to the whole level.
7. **Watch the demo** — `?mode=demo`: the skilled virtual player plays the new rules in the open
   field, the prelude at full speed with calm enemies.
8. **Reward spectrum** — compare `?player=skilled` with `?player=casual` (and `?player=perfect`).
9. **Full QA** — the checklist in the *Manual QA* section of
   [gameplay-redesign.md](docs/design/gameplay-redesign.md#manual-qa) (and the flow items of
   [interactive.md](docs/design/interactive.md#flow)); `docs/qa/qa-report.md` says which are
   still open.
10. **Play mode** — `?mode=play&player=moderate` from the start, at `?start=1:10` and at the climax
    (the open field, transform waves, landmarks); then play theme A by hand at
    `?mode=play&start=1:10&debug=moves` with headphones (strides feel instant, the same speed
    however you press, impacts on the beat); then the redesign's full playthroughs.

## Licences

Code is under the Apache License 2.0 (`LICENSE`). Art and sound come from CC0,
CC-BY, OGA-BY and CC-BY-SA packs; see `public/assets/credits.json` (also rolled
on the end card). Baked atlas pages that contain share-alike art are CC-BY-SA.
The development recording is tagged CC0 on Wikimedia Commons but its status is
unverified: it is fine for a private demo and must be replaced before any
public release (only a new song map is needed — level scripts use bars and beats).
