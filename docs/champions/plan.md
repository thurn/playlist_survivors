# Rhythm Champions: Implementation Plan

## Introduction

This plan builds V1 of [Rhythm Champions](design.md): level 1, start to
victory card, on Apple Silicon Macs, in a new Unity project,
`rhythm_champions/`, run with `just champions`.

It is tracked as the Enact epic **ps-6y2** (see [Beads](#beads)), a light run: one final
Warden, no Mason, Sage or Archivist beads. Every bead runs straight through.
Checks only the operator can make (hand-play balance, the speakers, a window
on screen, a low-end Mac) go on the operator-pending list,
`rhythm_champions/evidence/operator-pending.md`, and never block work.

The short version:

- **Fresh project, ported by review.** The audio plugin, the deterministic
  sim's pattern, the crowd renderer and GPU shatter, calibration and the
  setup tooling come across after review. Everything else is new code
  written from the design.
- **Three parts, as before**: a Rust plugin for music, a pure C# sim for the
  rules, a Unity client for everything seen and heard.
- **The look is the neon edge trailer's**, ported back from the frozen look
  package (reopened 2026-11-02). Each monster's mesh is redesigned for it by
  a project skill, and art is accepted by eye, never by look metrics.
- **Builds stay fast**: lean URP with our own dozen shaders and a variant
  budget, Mono by default, warm editors, shared caches.
- **Slow CI under 5 minutes**, Rhythm Champions only. The old projects are
  frozen and leave CI.

## Decisions

Settled with the operator on 2026-11-01, alongside the design's. Executors
treat them as fixed; everything else here is a recommended default.

1. **A new Unity project**, `rhythm_champions/`, on Unity 6000.6.7f1 and URP
   17.6. It depends on no other project or `unity-packages/` package: what
   it needs is copied in.
2. **The plugin is copied**: `native/crates/{plugin,analysis,audio,capture,stream}`
   and `native/models/madmom.bin` become `rhythm_champions/plugin/`, a
   Cargo workspace of its own. `native/` keeps its frozen copy.
3. **The sim is pure C#**, free of Unity, deterministic, on fixed ticks, with
   recorded sessions that replay exactly. Its tests run under plain dotnet.
4. **Lean URP**: no shadows, SSAO, lightmaps, decals, cookies, lens flares
   or reflection probes; only our own unlit and emissive shaders, no URP Lit;
   a few unshadowed point lights. A variant stripper and a check that fails
   a player build above 300 shader variants.
5. **Mono** for development and CI builds; IL2CPP only for perf runs.
6. **A warm editor per worktree and one shared local Unity Accelerator**;
   Library and baked-asset caches keyed by their inputs.
7. **Monster meshes** are committed recipes run by the tools; Blender 5.2
   LTS (`/Applications/Blender.app`, headless) is allowed. Their outputs stay
   in ignored folders, as every vendor-derived file does. (Until 2026-11-02
   this decision was the edge cuts, now deleted.)
8. **Slow CI at most 300 s**, check-fast at most 60 s, Rhythm Champions only.
   `native/`, `game/`, `demo3d/`, `demo2d/`, `stylelab/`, `herocap/`,
   `unity-packages/`, `tools/analysis/` and `tools/baker/` are frozen with no
   CI at all. The vendor guard stays: the repository is public.
9. **Mac only, keyboard and mouse.**
10. **Quality tiers**: the low tier holds 1,000 monsters at 60 fps on an M1
    Air class Mac; until one is measured, a proxy budget on the M5 Max
    ([Performance](#performance)).
11. **Rules are verified by replays of recorded sessions and scripted
    inputs; the look is verified by eye**: stills beside the trailer, blind
    critiques, the operator's look. Look metrics are diagnostics. No bot and no trailer mode in V1; balance is
    hand-play, on the operator-pending list.
12. **Naming**: the game, its project, product name, window title and
    commands are Rhythm Champions; the repository, `$PS_CACHE` paths, the
    relay and the `ps` bead prefix keep their names.

## Related Information

- [The design](design.md): what V1 is. Section names below refer to it.
- [AGENTS.md](../../AGENTS.md): worktrees, Enact delivery, the vendor rule.
- [Vendor assets](../archive/tech-demo-3d/vendor-assets.md): what may never
  be committed, and how the projects sync vendor files at setup.
- The sources this plan ports, all frozen:
  - [The Unity game's design](../archive/design/unity-game.md): the plugin's
    C interface, clocks, the sim lead and heard-time presses, sessions.
  - [The neon look](../archive/design/neon-look.md): the crowd renderer, the
    impulse field and grid floor, structure shatter.
  - [Crowd](../archive/tech-demo-3d/crowd.md),
    [effects](../archive/tech-demo-3d/effects.md) and
    [performance](../archive/tech-demo-3d/performance.md) from the 3D demo.
  - [The monster-mesh-design skill](../../.agents/skills/monster-mesh-design/SKILL.md):
    how each monster's mesh is designed; it draws on
    [the create-lod skill](../../.agents/skills/create-lod/SKILL.md) for
    mesh work and skin transfer.
  - [The archived style guide](../archive/design/neon-look-style.md) and its
    [thresholds](../archive/design/neon-look-thresholds.json): the starting
    point for the new one.

## Architecture

Three parts with strict boundaries, each testable alone:

- **The plugin** (Rust, in-process): captures or decodes audio, delays it
  3 s, analyses it, records it, and hands Unity analysis results and the
  delayed samples. It knows nothing about gameplay.
- **The sim** (C#, no Unity): the deterministic rules. It takes analysis
  results and inputs, and produces state and events. It knows nothing about
  rendering or audio devices.
- **The client** (Unity): input, clocks, rendering, effects, sound, UI.
- **The tools** (Python, uv): setup, build, play, test, monster bakes and
  reviews, look metrics, the vendor guard.

The analysis results form the **belief stream**: append-only entries
(beats, downbeats, onsets with strength, sections, loudness, reliability),
each with a **known-at time**, the song time from which the sim may use it.

One frame on the main thread, as in `game/`:

```text
for each new plugin entry:  sim.Add(entry)               // usable from known_at
for each new input:         sim.Input(input, heardTime)  // keys, click targets
frontier = songAtDsp(dspNow) + margin
sim.StepTo(frontier)                                     // fixed 1/120 s ticks
client.Present(frameSong, sim.EventsSince(cursor), sim.State)
```

Threads: the plugin's capture, analyzer and hindsight threads; Unity's audio
thread pulling delayed samples; the main thread above.

### Project layout

```text
rhythm_champions/
  Assets/Champions/
    Sim/          Champions.Sim: no Unity references
      Core/       Simulation, SimState, SimConfig, SimRandom, events
      Stream/     belief stream entries
      Beat/       beat state, judgement, cadence, quiet moments
      Hero/       movement, dashes, health
      Attacks/    attacks; Mods/ mods and tags
      Horde/      types, spawner, steps, separation, elites, spores
      Bosses/     arrival, duel, Break, Ogre, Dragon
      World/      chunks, structures, zones
      Progression/ experience, held levels, offers, treasure
      Run/        level timeline, start, end
      Session/    inputs, records, replay, hashes
    Client/       Champions.Client: loop, music, input, presenters, UI, sound
    Render/       Champions.Render: crowd, the edge look, shards, world, effects, post, tiers
    Editor/       Champions.Editor: bakers, variant stripper, build tasks
    Tests/        EditMode tests
  Content/        JSON: hero, dashes, attacks, mods, enemies, bosses, level1,
                  structures, progression, palette
  plugin/         Cargo workspace: crates/{plugin,analysis,audio,capture,stream}, models/
  dotnet/         Champions.Sim, Champions.Sim.Tests, Champions.Replay
  tools/          uv project: the `champions` CLI, monster mesh recipes
  evidence/       operator-pending.md, per-bead evidence
  manifest.json   vendor assets: root, path, SHA-256, GUID
  README.md
```

`Assets/Vendor/` and `Assets/Baked/` are ignored, as in every Unity project
here. The dotnet projects compile the Sim folder's files directly, as
`game/dotnet/` does.

### Commands

| Command | What |
| --- | --- |
| `just champions [flags]` | Set up if needed, build (Mono) and play at 1080p |
| `just champions-build`, `just champions-play` | The single steps |
| `just champions-tool <cmd>` | `uv run --project rhythm_champions/tools champions <cmd>` |
| `champions setup` | Sync vendor files from the manifest, restore caches, build the plugin |
| `champions test` | EditMode tests (dotnet sim tests: `dotnet test rhythm_champions/dotnet`) |
| `champions monster bake <type>`, `champions monster review <type> [--look trailer]` | Bake a monster from its recipe or vendor mesh; render its review sheets (ps-6y2.28) |
| `champions stills`, `champions look` | Fixed-time stills of scripted scenes; the look metrics on them |
| `champions perf` | IL2CPP build, scripted peak, GPU and CPU percentiles |
| `champions check --variants` | Fail above the shader variant budget |
| `champions editor` | Keep a warm editor for this worktree; other commands use it |

Player flags carried over in spirit from `game/`: `--file`, `--app`,
`--headless`, `--seconds`, `--fixed-time`, `--walk` (scripted keys),
`--replay`, `--seed`, `--start-minute`, `--tier`, `--debug`.

## What Carries Over

Ported means copied, read line by line and simplified; nothing comes across
unread.

| From | To | What changes |
| --- | --- | --- |
| `native/crates/{plugin,analysis,audio,capture,stream}`, `native/models/` | `plugin/` | Nothing in behaviour; its own workspace, toolchain and tests |
| `game/Assets/Game/Sim/{Stream,Session,Beats}`, `Simulation.cs`, `SimState.cs`, `SimRandom.cs`, `StructList.cs`, `Keys.cs` | `Sim/{Stream,Session,Beat,Core}` | Modes, the grid hero, run rules and the old arsenal dropped |
| `game/Assets/Game/Sim/Hero/{FreeHero,Dash}.cs`, `Sim/Weapons/BeatGrade.cs` | `Sim/Hero`, `Sim/Beat` | Reference only: heard-time re-walk kept; the dash and judgement rewritten to the design |
| `game/Assets/Game/Client/{Music,Calibration,Loop}` | `Client/` | The music clock, plugin interop, presenter fault isolation; calibration slimmed |
| `unity-packages/.../Runtime/Crowd` (BoneTextureCrowd, CrowdPalette.compute, skinning, MonsterShatter, MonsterShards.compute), `Editor/Crowd`, `game/Assets/Game/Editor/Crowd` | `Render/Crowd`, `Editor/` | Superseded 2026-11-02: the first port dropped the crease-flag path for edge cuts, now deleted; ps-6y2.28 ports the crowd again with `MonsterEdge.hlsl`, feature edges and crack-outlined shards, as the trailer drew them |
| `unity-packages/.../Runtime/{Impulse,World,Beat}` (ImpulseField, GridFloor, BeatGlobals) | `Render/World` | One beat ring per beat from the hero, restored; restraint kept |
| `unity-packages/.../Runtime/Crystal/StructureShatter` | `Render/Structures` | PhysX shards in the fill colour |
| `unity-packages/.../Runtime/Palette.cs` and the palette format | `Render/Look` | A new palette with new roles |
| `demo3d/tools/src/demo3d_tools/{setup,sync,unity,paths,guard,look,clone,plugin}.py` | `tools/` | One project's CLI; caches keep Bee; baked cache |
| `game/dotnet/` | `dotnet/` | The new sim's projects |
| `game/Content/sounds.json` and `Client/Sound` | `Content/`, `Client/Sound` | Reviewed; an on-beat accent layer |

Not carried: game modes, finishers, the grid hero, the old run, schedule,
offers, weapons and chests, the crystal, solid, hologram and prism looks,
procedural light-streak effects, the HUD, bots, sweeps, trailer mode, the
demos' choreography.

## The Sim

### Shape

- Fixed ticks of 1/120 s. No wall clock, no Unity, no unseeded randomness:
  one `SimRandom` per run from `--seed`.
- Content comes from `Content/*.json`, loaded into the config. Units are
  metres and seconds.
- Output: state (copied into a snapshot for the client) and an append-only
  event log the client reads with a cursor.
- The sim never reads a stream entry before its known-at time. A test feeds
  entries late and checks nothing changes before they are known.

### Inputs and heard time

- Inputs: movement keys, Space, card keys (1-3, arrows, Enter), click
  targets (ground points), and run controls (start, pause, resume, quit).
  Every input carries the song time it was heard at, and every input is
  recorded.
- The sim runs a small lead ahead of the speakers. An input heard at `h` and
  taken at `F` re-walks the hero from `h` (up to 64 ticks, never across a
  hold), resolving the dash's blows at their own times against the world's
  recent history. Nothing is re-simulated: no rollback. The client draws
  trailing by the lead, so the dash appears on the key-down frame.
- Measured as in `game/evidence/press-latency`: key to the first moved frame
  within one 60 Hz frame of the key-down frame.

### Beat services

`Sim/Beat` answers every rhythm question in one place:

- the reliable-beat state, the next beats, half beats and downbeats, and
  strong onsets, all from known entries;
- the judgement (Decision 6): binary, 80 ms on beats, 60 ms on half beats
  and strong onsets that the music plays, on the beat whenever no beat is
  reliable;
- cadence: an attack's period in beats (nearest 1 s, ties to the longer,
  kept within 8% of its lock tempo, changing only on a downbeat) and its
  fallback in seconds;
- quiet moments: 0.75 s under the silence threshold, or 2 s without a
  reliable beat;
- telegraph timing in whole beats, or 0.5 s per beat without.

### Systems

In tick order: inputs, hero (movement, dash), attacks, horde (spawner,
steps or glide, separation, contact, elite moves, spores), bosses, world
(chunks, structures), progression (experience, held levels, offers,
treasure), run (timeline, end).

- One spatial hash serves separation, hits, contact and steering. Budget:
  the sim at 1,000 alive within 1 ms per frame on the M5 Max (the proxy).
- World chunks of 24 m are generated from the seed and the chunk's
  coordinates only, so the world never depends on the order the hero visits
  it. A chunk's broken structures are remembered after it unloads.

### Sessions and replay

- A session is its inputs, the stream's records and `run.json`. Replay
  reproduces the run and its final state hash, under dotnet and under Unity.
- CI replays a few recorded sessions to their hashes.

## The Client

- **Flow**: Boot, title, music picker, run, end card.
- **The loop**: the frame above, with each presenter isolated (a throw in
  one never stops the others; faults are counted and fail the checks), as
  `game/` does since ps-msz.8.7.
- **Input**: the Input System for keys; the pointer cast to the ground for
  targets. Interactive UI elements never use `PickingMode.Ignore`; an
  EditMode test clicks a card.
- **Presenters**: hero, crowd, bosses, structures, world, effects, sound,
  UI. Sim events map to presenter commands in one place, the
  `EventDispatch`, so presenters can be built against their own small
  command interfaces before the sim's systems land.
- **Clocks**: `songAtDsp`, `frameSong` (with the video offset) and `keySong`
  (with the input offset), from the ported music clock.

## Rendering

### Pipeline

- URP Forward, HDR, bloom, neutral tonemapping. Everything else off
  (Decision 4). At most four unshadowed additional lights per object, read
  only by structure fills and the grid.
- About a dozen shaders, all ours:

| Shader | Draws |
| --- | --- |
| MonsterEdge, MonsterEdgeShard | The trailer's edge look for monsters, the hero and their shards: feature-edge lines over a dark glass core, fresnel rim, core glow, beat flare (ps-6y2.28) |
| StructureFill, StructureLines | Structures' dim fill and the edge look's lines (ps-6y2.30) |
| Shard, StructureShard | Monster shards on the GPU; structure shards on PhysX |
| GridFloor | The grid, beat wave, ripple, impulse rings, zones |
| Slash | Hovl's shapes in white |
| Particle | Compute sparks |
| Telegraph | Ground telegraphs, red and yellow |

- **Variant budget**: at most 300 compiled variants in a player build. The
  stripper removes URP variants for disabled features; `champions check
  --variants` reads the build's variant report and fails above budget. No
  legacy shaders are always-included.

### The edge look

Reopened on 2026-11-02 (design, [The Look](design.md#the-look)). The edge
cuts that stood here, a recipe pipeline gated by black share and segment
caps, were a visual regression and were deleted. The look is now the
trailer's, ported back:

- **The port** (ps-6y2.28), by review and fresh from the frozen
  `unity-packages/com.playlistsurvivors.look`:
  - the bone-texture crowd;
  - `StyleMesh.FeatureEdges`: creases over 24 degrees and open edges,
    flagged at bake;
  - `MonsterEdge.hlsl` with its MonsterEdge and MonsterEdgeShard shaders and
    `EdgeLook`'s gains;
  - `CrowdShardBaker` with `MonsterShards.compute`, each shard outlined
    along its crack.
- **`--look trailer`**: a player flag, honoured by stills and reviews too,
  that draws exactly the trailer's look: vendor meshes, `MonsterEdge.hlsl`
  and its gains as ported. The default look starts identical; later work
  changes only the default, and a test pins the trailer look.
- **Monster meshes** (ps-6y2.31-40): one bead per monster runs the
  [monster-mesh-design skill](../../.agents/skills/monster-mesh-design/SKILL.md).
  - `champions monster bake <type>` bakes from
    `rhythm_champions/tools/recipes/monster/<type>.py` when it exists, else
    from the vendor mesh, into ignored `Assets/Baked/Monsters/<type>/`.
  - Recipes may run Blender 5.2 LTS headless (`Blender --background
    --python`). Blender files and exports stay in ignored folders; the guard
    refuses `.blend`, `.fbx` and `.obj` in tracked paths.
- **Review sheets**: `champions monster review <type> [--look trailer]`
  draws the type at the game camera in its palette colour, at three screen
  sizes, in idle, walk and attack, its shatter, and a crowd cell over the
  grid, with a side-by-side sheet against `--look trailer`. Small stills and
  a README go to `rhythm_champions/evidence/monsters/<type>/`. The sheets
  have no pass or fail numbers.
- **The hero and bosses** (ps-6y2.29) and **structures** (ps-6y2.30, over
  their fill) use the same drawing.
- **Accepting art** (design, [Judgment over rules](design.md#judgment-over-rules)):
  - a blind critique by an agent given only the images (no docs, no recipe,
    no numbers);
  - side-by-sides against `--look trailer` and the trailer's frames;
  - an operator-pending row for the operator's eye.

  Look metrics never accept or reject art.

### Crowd

- The ported bone-texture crowd: bone textures at 30 Hz, a compute pass
  blending two clips per instance, four-bone skinning in the vertex shader.
- One indirect draw per type, as the trailer's crowd drew them.
- Monster shards on the GPU, as ported, each outlined along its crack,
  thrown forcefully away from the blow, weapons with them.

### World, post and camera

- GridFloor: quiet lines at the bottom of ground light; a ring per beat from
  the hero at 11 m per beat, brightening and rippling, stronger on
  downbeats; the impulse field's event rings; zone crossfades swept by the
  wave; restraint as designed.
- Post: URP bloom, the tier's settings.
- Camera: about 60 degrees down, a lead in the movement direction, easing
  out 20% for duels, punches and shakes only on special moments.

### Quality tiers

| | Low | Medium | High |
| --- | --- | --- | --- |
| Bloom | half resolution | full | full, wider |
| MSAA | off | 2x | 4x |
| Shard pieces live | 600 | 1,500 | 3,000 |
| Slash distortion, line glow halos, chromatic flashes | off | off | on |

The core look (the edge look, beat wave, grid, shatter, bloom) is on
every tier. `--tier` picks one; the pause menu's Quality row changes it.

## Performance

- **Target**: the low tier on an M1 Air class Mac, 1,000 alive, 60 fps at
  1080p.
- **Proxy until measured**: on the M5 Max at the low tier with 1,000 alive,
  GPU p99 at most 1.5 ms and the sim at most 1 ms per frame, assuming an 8x
  gap. An operator-pending row asks for a run on an M1-class Mac; its
  numbers replace the proxy.
- `champions perf`: an IL2CPP build, a scripted peak with 1,000 alive,
  probe percentiles. Never in CI.

## Look Metrics

- `champions look` ports the archived look harness: the value ladder, the
  colour key (`key60`), families, palette conformance, the floor's p99,
  background light share, flash frames and swarm separation, with a type-ID
  pass rendered with the stills. **A diagnostic**: it finds frames worth a
  second look and decides nothing (ps-6y2.41 makes it never fail a run).
  Black share is gone.
- `champions stills` renders fixed-time stills of scripted scenes in one
  launch: a crowd at minute 4, a loud surge, a structure field, the beat
  wave at three phases, both duels' telegraphs, a card moment, the gallery
  of effects.
- Guide values: `docs/champions/style-thresholds.json`, written by the
  palette bead from the archived thresholds.

## Build Policy

- **Mono by default** for `just champions` and CI; `--backend il2cpp` for
  perf.
- **The variant budget** above, checked in CI.
- **Warm editor**: `champions editor` keeps one editor open per worktree;
  build, test and stills go through it when it is up.
- **Unity Accelerator**: one local instance shared by every worktree and CI.
  SETUP.md documents it.
- **Library cache**: keyed by the Unity version, package manifest and lock,
  and the vendor manifest; `Library/Bee` kept, but its build graphs, which
  name the worktree that saved them, dropped on restore; refreshed after each
  passing CI run.
- **Baked cache**: `Assets/Baked/` keyed by the recipes, the vendor hashes
  and the baker code, so a fresh worktree or CI copies it instead of
  re-baking.
- No lightmaps: there is nothing to bake.

## CI and Checks

- **check-fast** (at most 60 s; only the areas changed): conflict markers,
  the vendor guard, the tools' ruff and pytest, the sim's dotnet tests, the
  plugin's clippy and tests.
- **ci-full** (at most 300 s wall), lanes in parallel:
  - plugin: clippy and tests of the five crates (a persistent target);
  - sim: dotnet tests and recorded-session replays to their hashes;
  - tools: ruff and pytest;
  - unity: one launch with a warm Library and the baked cache: EditMode
    tests, the Mono player build, then the built player's stills and a 10 s
    fixed-time replay; then `champions look` on the stills as a report (it
    never fails the run) and the variant budget check;
  - guard: over the tree and the range since `master`.
- Every step logs its seconds; `scripts/ci-full` fails a run over 300 s. A
  Steward fixing a budget failure speeds a step up or moves it to a cheaper
  lane.
- The frozen projects leave both scripts. The Cargo target prune serves the
  plugin's workspace only.

## Docs and the Frozen Projects

- The change that filed this plan archived every earlier doc under
  [docs/archive/](../archive/) with a banner, and rewrote the README, SETUP
  and AGENTS.md around Rhythm Champions.
- Frozen, in the tree, unbuilt by CI: `native/`, `game/`, `demo3d/`,
  `demo2d/`, `stylelab/`, `herocap/`, `unity-packages/`, `tools/analysis/`,
  `tools/baker/`. Their READMEs say so. Nothing in `rhythm_champions/`
  references them at build time.
- The foundation bead adds SETUP.md's Rhythm Champions section; the palette
  bead adds `docs/champions/style.md`.

## Build Order

A thin foundation, then wide parallel tracks, then assembly and polish.
Dependencies exist only where a bead needs another's output; files touched
by several beads are no reason to chain them.

- **Foundation**, two at once: the project and tools (RC1); the sim core
  (RC3). Then the plugin and music (RC2), and CI (RC4) once all three exist.
- **Tracks**, as soon as their inputs land:
  - sim: hero and dashes (RC5), attacks and mods (RC6), the horde (RC7),
    then progression (RC15) and bosses (RC14);
  - art: the edge-cut pipeline (RC8), then the cuts (RC9a-d), the hero
    (RC17) and structures (RC12), all superseded on 2026-11-02 by the
    trailer look's port (RC22), then the hero and bosses (RC23), structures
    (RC24) and the ten monster meshes (RC25a-j) in parallel;
  - look: the palette and style guide (RC10), the world (RC11), weapon
    effects (RC13);
  - UI (RC16).
- **Assembly**: level 1 end to end (RC18), then performance (RC19) and the
  look pass (RC20, after the monster meshes), then the final Warden (RC21).

## Beads

Epic: **ps-6y2**. Priorities: P0 foundation, P1
tracks and assembly, P2 polish.

| Key | Bead | Id | Depends on | P |
| --- | --- | --- | --- | --- |
| RC1 | Project skeleton: the Unity project, the `champions` CLI, `just champions`, lean URP, the variant budget | ps-6y2.1 | - | 0 |
| RC2 | The plugin copied into `rhythm_champions/plugin/`; music: Delayed listening, files, the music clock | ps-6y2.2 | RC1 | 0 |
| RC3 | The sim core: ticks, belief stream, beat services, heard-time inputs, sessions and replay | ps-6y2.3 | - | 0 |
| RC4 | Slow CI under 5 minutes for Rhythm Champions; the frozen projects leave CI | ps-6y2.4 | RC1, RC2, RC3 | 0 |
| RC5 | Hero movement, the five dashes, judgement and health in the sim | ps-6y2.5 | RC3 | 1 |
| RC6 | Attacks and mods in the sim | ps-6y2.6 | RC3 | 1 |
| RC7 | The horde in the sim: ten types, beat steps, spawning, elite moves, spores | ps-6y2.7 | RC3 | 1 |
| RC8 | The edge-cut pipeline, review sheets and the edge crowd renderer, proven on the skeleton | ps-6y2.8 | RC1 | 1 |
| RC9a | Edge cuts: slime and bat | ps-6y2.9 | RC8 | 1 |
| RC9b | Edge cuts: demon and goblin | ps-6y2.10 | RC8 | 1 |
| RC9c | Edge cuts: mushroom, big ork and troll | ps-6y2.11 | RC8 | 1 |
| RC9d | Edge cuts: the Fat Ogre and the dragon | ps-6y2.12 | RC8 | 1 |
| RC10 | The palette and the style guide; `champions look` and `champions stills` | ps-6y2.13 | RC1 | 1 |
| RC11 | The world: the endless grid, the beat wave, impulse rings, zones, post, camera, tiers | ps-6y2.14 | RC1, RC3 | 1 |
| RC12 | Structures: chunked placement, Synty edge cuts with fill, breaking and shatter | ps-6y2.15 | RC3, RC8 | 1 |
| RC13 | Weapon effects in white on Hovl's shapes, and the hero's sounds | ps-6y2.16 | RC1 | 1 |
| RC14 | Bosses: arrival, entrance shatter, duels, Break, the Fat Ogre and the dragon | ps-6y2.17 | RC5, RC7, RC8 | 1 |
| RC15 | Progression in the sim: experience, held levels, offers, treasure | ps-6y2.18 | RC5, RC6 | 1 |
| RC16 | UI: the indicator, HUD, cards that click, menus, music picker, calibration | ps-6y2.19 | RC1, RC2, RC3 | 1 |
| RC17 | The hero: white edge cut, rig, Sword Slash clips at natural speed, dash poses | ps-6y2.20 | RC8 | 1 |
| RC18 | Assemble level 1 end to end: timeline, start and end, wiring, first tuning, CI replays | ps-6y2.21 | RC2, RC4, RC5, RC6, RC7, RC9a, RC9b, RC9c, RC9d, RC10, RC11, RC12, RC13, RC14, RC15, RC16, RC17, RC22, RC23, RC24 | 1 |
| RC19 | Quality tiers and performance: 1,000 alive within the low tier's budget | ps-6y2.22 | RC18 | 2 |
| RC20 | The look pass: level 1 judged by eye against the trailer | ps-6y2.23 | RC18, RC22, RC23, RC24, RC25a-j | 2 |
| RC21 | Warden: final review of the Rhythm Champions V1 run | ps-6y2.24 | all | 1 |
| RC22 | The trailer's edge look ported: crowd, `MonsterEdge`, crack-outlined shards, ten monsters on vendor meshes, `--look trailer`, `champions monster bake` and `review` | ps-6y2.28 | - | 1 |
| RC23 | The hero and the bosses in the edge look, and their shatters | ps-6y2.29 | RC22 | 1 |
| RC24 | Structures in the edge look over their zone fill | ps-6y2.30 | RC22 | 1 |
| RC25a-j | Monster meshes, one per monster with the monster-mesh-design skill: skeleton, slime, bat, demon, goblin, mushroom, big ork, troll, Fat Ogre, dragon | ps-6y2.31-40 | RC22 | 1 |
| RC26 | `champions look` becomes a diagnostic that never fails a run | ps-6y2.41 | - | 2 |

RC8, RC9a-d, RC12, RC14 and RC17 landed edge cuts; their edge-cut work was
deleted on 2026-11-02, and their behaviour (rigs, clips, telegraphs,
breaking) stays. Each bead's description in the graph carries its full
scope, file pointers and acceptance; this table is the map.

## Whole-Run Acceptance

- `just champions --file <song>` plays level 1 from the first offer to the
  victory card; a scripted run (`--walk` with picks) reaches the victory
  card headless, and an idle hero dies within 90 s.
- Delayed listening plays an app's audio 3 s late (an operator-pending row
  confirms on the speakers).
- Every design decision holds, checked by the sim's tests where it is a rule
  and by stills where it is a look.
- check-fast within 60 s and ci-full within 300 s on three consecutive
  runs.
- The low tier within its proxy budget at 1,000 alive; the variant budget
  holds.
- The look holds up by eye against the trailer: the stills set beside the
  trailer's frames and `--look trailer`, a blind critique, and the
  operator's look on the operator-pending list. Look metrics decide
  nothing.
- The operator-pending list holds rows for hand-play balance, dash and
  judgement feel on speakers and on Bluetooth, and an M1-class measurement.

## Risks

- **Numbers standing in for judgment.** The edge cuts passed every
  threshold and looked terrible, because the thresholds were the only
  acceptance and nobody had to look. Now every art bead closes on a blind
  critique and side-by-sides with the trailer.
- **Monster meshes are craft.** A mesh can be worse than the trailer's
  drawing of the vendor model. `--look trailer` keeps that drawing one flag
  away, and keeping the vendor mesh is an accepted outcome of a mesh bead.
- **Hovl in white** may lose the richness that made it better than the
  ribbons. The shader keeps its shapes, streaks and timing; if white alone
  reads flat, the hero's near-white trail colour may tint the edges.
- **Unreliable beats** make rhythmic bosses unfair; every telegraph has its
  fallback in seconds, and judgement is generous.
- **1,000 lit monsters** may cost more in bloom and overdraw than in
  geometry on an M1. The ported edge look is timed at 1,000 monsters
  (RC22) before anything is built on it.
- **A Unity build inside 300 s** needs the warm Library and baked cache to
  hold; CI logs every step so creep shows at once.
- **The endless world and determinism**: chunk generation must depend only
  on the seed and coordinates; a test visits chunks in different orders.

## Alternatives Considered

- **Copy `game/` and prune**: rejected; it brings 33k lines of assumptions
  and the bugs found in the review.
- **No deterministic sim**: rejected; replay tests and recorded sessions are
  CI's cheapest strong checks.
- **A custom render pipeline**: rejected for V1; lean URP is enough, and we
  would own the whole pipeline.
- **Tuning today's crease rule**: rejected; line choice stays bound to the
  vendor topology and bodies stay near 5,700 triangles.
- **Original models built in code**: rejected for V1 as a quality risk;
  recipes over vendor meshes give the shapes and the animation.
- **Deleting the frozen projects**: the operator chose to keep them frozen,
  outside CI.
