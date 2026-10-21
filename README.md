# Playlist Survivors — tech demo

A non-interactive, browser-based level that plays itself to the end of
Offenbach's *Orpheus in the Underworld* overture (the slow passage, then the
Infernal Galop). A hero hacks through a horde of up to ~500 LPC monsters, and
every hop, slash, kill, flash and sound lands on the music.

The design is in [docs/design/tech-demo.md](docs/design/tech-demo.md). The
whole level is **planned in advance** from a song map and a level script, then
played back: each frame is a pure function of the audio clock's song time.

## Running it

Requirements: Node 24+, and [uv](https://docs.astral.sh/uv/) with Python 3.12
for the audio analysis tool.

```bash
npm install
npx playwright install chromium   # for captures, the benchmark and e2e tests
npm run dev                        # http://localhost:5199
```

Open the page in desktop Chrome, wait for the progress bar, then click
**Click to begin** (browsers need one click before audio can play). Use wired
headphones or speakers.

### URL parameters

| Parameter | Effect |
|---|---|
| `?metronome` | Full-screen flash on every beat plus a click track over the music (the sync check). |
| `?capture&t=88.31` | Render one frozen frame at song time 88.31 s, without audio. |
| `?avOffsetMs=-40` | Shift the picture relative to the audio (e.g. Bluetooth latency). |
| `?seed=1859` | Re-plan, and regenerate the map, with a different seed. |
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
| `npm run check:plan` | Runs the planner with all its output checks and prints peak population, kills per section, total hit-stop time and the longest stretch without a primary event. `-- --seed=1859` for another seed. |
| `npm run check:determinism` | Plans twice and generates the map twice; hashes must match. |
| `npm run capture -- --times 12,35.2,88.31,141.5` | Headless-Chrome screenshots at exact song times into `captures/`. `--sections` captures the start of every section. |
| `npm run bench` | Headed Chrome, whole song at real-time speed: fails if JavaScript frame time exceeds 8 ms or GPU time 10 ms at p95, or any frame exceeds 33 ms. |
| `npm run test:e2e` | Playwright: capture hook, metronome flash on the beat, broken level script shows an error and never starts, reload uses the cached plan. |
| `npm run check:all` | All of the above except captures and the benchmark. |

## How it fits together

```text
recording ──ps-analysis (crop, analyze, report, edit, check, click)──► songs/orpheus-galop/songmap.json
LPC packs ──tools/baker (npm run bake)──► public/assets/ (atlases, effects, sounds, credits)
content/*.yaml + levels/orpheus-galop.yaml + song map ──planner (web worker)──► plan
audio clock → song time s → evaluatePlan(plan, s) → PixiJS renderer + HUD
```

- `tools/analysis/` — the Python audio analysis tool (madmom, librosa). The
  beat-map workflow, edit list and report live in `songs/orpheus-galop/`.
- `tools/baker/`, `assets-src/` — the asset baker: ULPC character compositing,
  the scripted kick animation, white hit-flash frames, terrain and props,
  effects, loudness-normalized sounds, the licence manifest and credits.
- `content/` — moves, movesets, enemies, weapons, formations, effect recipes,
  sound palettes, zone themes and Tiled landmarks (`content/landmarks/*.tmj`).
- `levels/` — level scripts (bars and beats, never seconds).
- `src/core/` — song map maths, the time warp, geometry, seeded randomness.
- `src/content/` — schemas and YAML loaders with line-numbered errors.
- `src/planner/` — the eight planning stages and the output checks.
- `src/mapgen/` — the forest generator (route, corridor, zones, props, landmarks).
- `src/plan/` — the plan format and stateless frame evaluation.
- `src/runtime/` — audio clock and sound scheduling, renderer, HUD, app shell.
- `scripts/` — the check, capture and benchmark commands.

## Human review gates

These need eyes and ears; the commands above get everything ready.

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
   kick-line sweep, boss slams, boss death, finale blast).
6. **Sound** — listen to the whole level.
7. **Full QA** — the checklist in the design document's *Manual QA* section.

## Licences

Code is under the Apache License 2.0 (`LICENSE`). Art and sound come from CC0,
CC-BY, OGA-BY and CC-BY-SA packs; see `public/assets/credits.json` (also rolled
on the end card). Baked atlas pages that contain share-alike art are CC-BY-SA.
The development recording is tagged CC0 on Wikimedia Commons but its status is
unverified: it is fine for a private demo and must be replaced before any
public release (only a new song map is needed — level scripts use bars and beats).
