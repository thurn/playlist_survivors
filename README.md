# Playlist Survivors — tech demo

A non-interactive, browser-based rhythm game level that plays itself to the end
of Offenbach's *Orpheus in the Underworld* overture (the slow passage, then the
Infernal Galop). A virtual player presses the arrow keys to the beat; the hero
steps one pace per press while the sword swings itself at the crowd, and every
strike and impact sound lands on the music, through a horde of up to ~500 LPC
monsters.

The design is in [docs/design/tech-demo.md](docs/design/tech-demo.md), with the
gameplay in [docs/design/beat-steps.md](docs/design/beat-steps.md), which
supersedes parts of it. The level is a **simulated playthrough**: the game rules
run forward in time, driven by the virtual player's key presses, and never read
a press before it happens. The simulation is recorded at load, then played
back: each frame is a pure function of the audio clock's song time.

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

Open the page in desktop Chrome, wait for the progress bar, then click
**Click to begin** (browsers need one click before audio can play). Use wired
headphones or speakers.

### URL parameters

| Parameter | Effect |
|---|---|
| `?metronome` | Full-screen flash on every beat plus a click track over the music (the sync check). |
| `?start=1:35` | Start playback from a song time, in seconds (`?start=95.5`) or `m:ss`. |
| `?capture&t=88.31` | Render one frozen frame at song time 88.31 s, without audio. |
| `?avOffsetMs=-40` | Shift the picture relative to the audio (e.g. Bluetooth latency). |
| `?seed=1859` | Re-simulate, and regenerate the map, with a different seed. |
| `?player=casual` | Re-simulate with another skill profile: `perfect`, `skilled` (the level's default) or `casual`. |
| `?test` | Expose the `window.__demo` test hook (`ready`, `renderAt(s)`, `stats()`). |
| `?bench` | Play the whole song on a virtual clock and record frame times. |

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
| `npm run capture -- --times 12,35.2,88.31,141.5` | Headless-Chrome screenshots at exact song times into `captures/`. `--sections` captures the start of every section. |
| `npm run bench` | Headed Chrome, whole song at real-time speed: fails if JavaScript frame time exceeds 8 ms or GPU time 10 ms at p95, or any frame exceeds 33 ms. |
| `npm run test:e2e` | Playwright: capture hook, metronome flash on the beat, broken level script shows an error and never starts, reload uses the cached plan, `?player=` re-simulates with another profile. |
| `npm run qa` | About 45 s of playback in Chromium over the design's Manual QA items a machine can judge: simulation time, stillness between steps, the skilled and casual players' impacts on the grid and the reward spectrum, audio clock rate, picture/audio offset, frame rate, hidden-tab recovery (a 3 s main-thread stall), resize letterboxing, click track, finale clearance, and a listening pass over the offline-rendered mix (clipping, effects level, music in front, no wall of noise, effects on time); writes `captures/qa/report.md` and review frames. |
| `npm run check:analysis` | The analysis tool's pytest suite and ruff. |
| `npm run check:all` | All of the above except captures, QA and the benchmark. |

`scripts/ci` (the Tollgate gate, about 30 s) runs `check`, `check:content`,
`check:assets`, `check:seeds` and the build; `check:analysis` and `check:songmap`
only when `tools/analysis/` or `songs/` differ from master.

## How it fits together

```text
recording ──ps-analysis (crop, analyze, report, edit, check, click)──► songs/orpheus-galop/songmap.json
LPC packs ──tools/baker (npm run bake)──► public/assets/ (atlases, effects, sounds, credits)
content/*.yaml + levels/orpheus-galop.yaml + song map ──simulation + virtual player (web worker)──► recorded plan
audio clock → song time s → evaluatePlan(plan, s) → PixiJS renderer + HUD
```

- `tools/analysis/` — the Python audio analysis tool (madmom, librosa). The
  beat-map workflow, edit list and report live in `songs/orpheus-galop/`.
- `tools/baker/`, `assets-src/` — the asset baker: ULPC character compositing,
  the scripted kick animation, white hit-flash frames, terrain and props,
  effects, loudness-normalized sounds, the licence manifest and credits.
- `content/` — the hero (movement, timing, groove, swings, aim), enemies, weapons, formations, effect recipes,
  sound palettes, zone themes and Tiled landmarks (`content/landmarks/*.tmj`).
- `levels/` — level scripts (bars and beats, never seconds).
- `src/core/` — song map maths, the time warp, geometry, seeded randomness.
- `src/content/` — schemas and YAML loaders with line-numbered errors.
- `src/sim/` — the simulation: the song schedule, the movement model, timing and groove, auto-aim, the
  virtual player and the forward game loop.
- `src/planner/` — the pipeline (schedule, map, simulation, emission), the recording checks and the
  causality check.
- `src/mapgen/` — the forest generator (route, corridor, zones, props, landmarks).
- `src/plan/` — the plan format and stateless frame evaluation.
- `src/runtime/` — audio clock and sound scheduling, renderer, HUD, app shell.
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
7. **Step movement** — open `?start=1:10` (theme A) and confirm the first
   assembled check in [beat-steps.md](docs/design/beat-steps.md#first-assembled-check):
   still between steps, quick firm steps, swings toward the crowd, every impact on the beat.
8. **Reward spectrum** — compare `?player=skilled` with `?player=casual` (and `?player=perfect`).
9. **Full QA** — the checklists in the *Manual QA* sections of both design documents.

## Licences

Code is under the Apache License 2.0 (`LICENSE`). Art and sound come from CC0,
CC-BY, OGA-BY and CC-BY-SA packs; see `public/assets/credits.json` (also rolled
on the end card). Baked atlas pages that contain share-alike art are CC-BY-SA.
The development recording is tagged CC0 on Wikimedia Commons but its status is
unverified: it is fine for a private demo and must be replaced before any
public release (only a new song map is needed — level scripts use bars and beats).
