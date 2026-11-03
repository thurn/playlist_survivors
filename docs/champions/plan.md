# Rhythm Champions: Implementation Plan

## Introduction

This plan retains the technical foundations for [Rhythm Champions](design.md)
and records the implementation consequences of the **2026-11-02 combat
redesign**. As of 2026-11-03, ps-3wc.1–.13 deliver the survivor contracts,
eight weapon identities/four forms, broad supports, movement dash, pickups,
immediate cards and three bosses. The approved November 3 restoration in
[game-feel-plan.md](game-feel-plan.md) supersedes banked empowerment,
movement-only dash and retained-horde boss behavior; ps-ueo implements it. See the
[assembly evidence](../../rhythm_champions/evidence/survivor-assembly/README.md)
for actual full-run results and replay coverage. Full-build load/readability
(ps-3wc.14), the final Warden (ps-3wc.15) and operator checks remain separate.

The combat redesign is filed as **ps-3wc**, a light Enact epic with 14
implementation tasks and one final Warden. The light run has reached assembly.
The existing **ps-6y2** epic continues independently: its remaining structure
meshes, performance, look pass and final review are still wanted. **Neither
epic depends on the other.** Reviews cover their own delivered scope and must
preserve concurrent improvements rather than reinstate superseded combat.

The original build order and epic map remain in the
[archived plan](../archive/champions-dash-combat-2026-11-02/plan.md).
The earlier design revision changed no beads; this subsequent filing adds the
new epic and clarifies the old epic's scope without moving or closing its work.

Operator-only checks remain in `rhythm_champions/evidence/operator-pending.md`.
Old evidence records what was built and tested at the time, not acceptance of
the new combat. Rendering and audio evidence remain useful where applicable.

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

These retained technical decisions were settled on 2026-11-01 and are not
reopened by the combat revision. The current design governs gameplay; the
implementation details below are guidance for adapting the existing project.

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
      Beat/       beat state, judgement, cadence, fractional strike budgets
      Hero/       movement dash, empowerment, health
      Attacks/    weapons and fixed forms; Mods/ broad supports and compatibility
      Horde/      types, spawner, steps, separation, elites, spores
      Bosses/     arrival clears, controlled reinforcements, rhythmic patterns
      World/      chunks, structures, zones
      Progression/ dropped XP, pickups, immediate offers, boss rewards
      Run/        level timeline, start, end
      Session/    inputs, records, replay, hashes
    Client/       Champions.Client: loop, music, input, presenters, UI, sound
    Render/       Champions.Render: crowd, the edge look, shards, world, effects, post, tiers
    Editor/       Champions.Editor: bakers, variant stripper, build tasks
    Tests/        EditMode tests
  Content/        JSON: hero, movement dash, weapons, supports, enemies, bosses, level1,
                  structures, progression, palette
  plugin/         Cargo workspace: crates/{plugin,analysis,audio,capture,stream}, models/
  dotnet/         Champions.Sim, Champions.Sim.Tests, Champions.Replay
  tools/          uv project: the `champions` CLI, monster mesh recipes
  evidence/       operator-pending.md, per-bead evidence
  manifest.json   vendor assets: root, path, SHA-256, GUID
  README.md
```

The layout above describes responsibilities; names such as `Attacks/` and
`Mods/` may remain during migration. Do not infer current implementation from
the proposed responsibilities.

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
  hold), resolving dash movement, invulnerability and any supported landing
  explosion against the world's recent history. Nothing is re-simulated: no rollback. The client draws
  trailing by the lead, so the dash appears on the key-down frame.
- Measured as in `game/evidence/press-latency`: key to the first moved frame
  within one 60 Hz frame of the key-down frame.

### Beat services

`Sim/Beat` answers every rhythm question in one place:

- the reliable-beat state, the next beats, half beats and downbeats, and
  strong onsets, all from known entries;
- heard-time judgement: binary, default 80 ms on beats, 60 ms on half beats
  and strong played onsets; all dashes empowered without a reliable beat;
- strict musical activation grids and deterministic fractional extra-strike
  accumulation, with weapon-specific seconds fallbacks;
- stable cadence changes and comparable long-run output across tempos;
- telegraphs retaining their promised reaction time across tempo changes,
  music-confidence loss and card holds; safe remapping on resume.

No quiet-moment service controls upgrade presentation in the new design.

### Systems

In tick order: inputs, hero (movement, dash), attacks, horde (spawner,
steps or glide, separation, contact, elite moves, spores), bosses, world
(chunks, structures), progression (XP drops, collection, immediate offers,
boss rewards), run (horde-time schedule, combat time, end).

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
| MonsterEdge, MonsterEdgeShard | The trailer's edge look for monsters and the bosses, and every shard (the hero's too): feature-edge lines over a dark glass core, fresnel rim, core glow, beat flare (ps-6y2.28) |
| HeroEdge | The trailer's knight: its feature lines, rim and chest core in white, the crystal blade's every edge and spine light, on a figure posed on the CPU (ps-6y2.29) |
| StructureEdge, StructureEdgeShard | Structures in the edge look's lines over their zone's dim fill, and their crack-outlined shards on PhysX (ps-6y2.30) |
| GridFloor | The grid, beat wave, ripple, impulse rings, zones |
| Slash | Hovl's shapes, ordinary white / empowered yellow |
| Particle | Compute sparks |
| Telegraph | Ground telegraphs, orange/red; yellow reserved for player rhythm |

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
  - the trailer's post grade (its neon-bloom preset's scene stage,
    `Render/World/Grade.cs`): before URP's post on every camera, the
    exposure, a void toe sinking what lies under a knee toward black (the
    glass cores, the faint rim), a wide saturated halo and a filmic tonemap
    that keeps a saturated emitter's hue; URP's tonemapper is off and its
    bloom (0.63), contrast and saturation (+10) and vignette follow. The
    floor undoes the grade (`Ungraded`), so the grid reads as calibrated.
  - As the trailer had them: the look package at git 138684f2, the build
    that captured it. The later outline hull and small-body rim are not the
    trailer's, and its line widths were render pixels at its capture's 2160
    rows (MonsterLines.hlsl measures in those).
  - As landed (Render/Crowd, Editor/Monsters): `MonsterCrowd`,
    `MonsterShatter`, `EdgeLook` and `EdgeGains`, the ten types' vendor
    models in `Editor/Monsters/monsters.json`. Two deliberate changes from
    the trailer: the world's beat wave stands in for its global beat pulse
    (the core glow and line lift travel out with the wave), and a weapon's
    or shield's chunks are flung with the body (the trailer's kick left a
    sword hanging by the feet standing where it fell).
- **`--look trailer`**: a player flag, honoured by stills and reviews too,
  that draws exactly the trailer's look: vendor meshes, `MonsterEdge.hlsl`
  and its gains as ported. The default look starts identical; later work
  changes only the default, and a test pins the trailer look
  (`MonsterLookTests`: the trailer's shader files and `EdgeGains.Trailer`; a
  change to the default gets its own shader).
- **Monster meshes** (ps-6y2.31-40): one bead per monster runs the
  [monster-mesh-design skill](../../.agents/skills/monster-mesh-design/SKILL.md).
  - `champions monster bake <type>` bakes from
    `rhythm_champions/tools/recipes/monster/<type>.py` when it exists, else
    from the vendor mesh, into ignored `Assets/Baked/Monsters/<type>/`.
    A recipe may flag which edges draw: every edge of its low-poly mesh (a
    true wireframe, design.md, The aim) or only creases over 24 degrees, the
    vendor meshes' default (`Mesh.edges` per triangle, or `Mesh.edge_angle`;
    a mesh that chooses its edges is not decimated for distance).
  - Recipes may run Blender 5.2 LTS headless (`Blender --background
    --python`). Blender files and exports stay in ignored folders; the guard
    refuses `.blend`, `.fbx` and `.obj` in tracked paths.
- **Review sheets**: `champions monster review <type> [--look trailer]`
  draws the type at the game camera in its palette colour, at three screen
  sizes, in idle, walk and attack, its shatter, and a crowd cell over the
  grid, with a side-by-side sheet against `--look trailer`. Small stills and
  a README go to `rhythm_champions/evidence/monsters/<type>/`. The sheets
  have no pass or fail numbers.
- **The bosses** (ps-6y2.29) are crowd types, written into the crowd by
  their presenter with their animator's clips and broken by its shatter.
  **The hero** is the trailer's knight as it drew it: HeroEdge (the look
  package's at 138684f2) on the vendor knight holding demo3d's crystal
  blade, a figure posed on the CPU from a rig its bake keeps
  (`MonsterFigure`, `RigPose`; monsters.json's `figure`), `HeroGains`
  pinned for `--look trailer` beside `EdgeGains`. **Structures** (ps-6y2.30)
  draw the crowd's lines over their zone's fill: `champions structure bake
  <kind>` makes each Synty model a style mesh, static, and cuts its chunks as
  the monsters' are; `Editor/Structures/structures.json` may give a kind its
  own crease angle in the default look, judged by eye at game scale with why
  (`--look trailer` draws the models unsimplified); `champions structure
  review all` writes each kind's sheets and a field of every kind into
  `rhythm_champions/evidence/structures/`.
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
  out 20% for boss fights, punches and shakes only on special moments.

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

The redesigned full-build load is measured in
[ps-3wc.14 evidence](../../rhythm_champions/evidence/full-build/README.md).
Explicit `full-*` still scenes exercise two six-weapon/six-support maximum
builds against 1,000 retained monsters and each boss; they are excluded from
`all` and CI. The initial M5 simulation measurements exceeded the 1 ms proxy,
and synchronized offscreen rendering does not prove the GPU proxy. Concrete
repeat-queue/visibility fixes land with that evidence; neither target hardware
performance nor full-build visual acceptance is declared complete. The
[final Warden evidence](../../rhythm_champions/evidence/warden-review/README.md)
records hold/empty-target repairs, hue-preserving default hit feedback and a
six-case rerun; remaining peak player hierarchy and cost gaps stay actionable
under ps-3wc. The [collision-planning follow-up](../../rhythm_champions/evidence/collision-planning/README.md)
reduces repeated neighbor/collision work while preserving exact replay output.
Its six-case Master IL2CPP sweep still exceeded the 1 ms proxy. The subsequent
[full-build proxy repair](../../rhythm_champions/evidence/full-build-proxy/README.md)
records all six actual Master IL2CPP Low cases at 0.906–0.976 ms sim p99,
with 1,001 alive, zero faults and unchanged complete-build event/flight output.
Those observations meet the unchanged M5 simulation proxy with limited
shared-host headroom; M1 hardware performance and GPU-only timing remain
unverified. No timing-threshold test or automatic sweep enters CI.

## Look Metrics

- `champions look` ports the archived look harness: the value ladder, the
  colour key (`key60`), families, palette conformance, the floor's p99,
  background light share, flash frames and swarm separation, with a type-ID
  pass rendered with the stills. **A diagnostic**: it finds frames worth a
  second look and decides nothing, and never fails a run (ps-6y2.41).
  Black share is gone.
- `champions stills` renders fixed-time stills of scripted scenes in one
  launch: a crowd at minute 4, a loud surge, a structure field, the beat
  wave at three phases, all three boss encounters' telegraphs amid the horde, a card moment, the gallery
  of effects.
- Guide values: `docs/champions/style-thresholds.json`, written by the
  palette bead from the archived thresholds.

## Build Policy

- **Mono by default** for `just champions` and CI; `--backend il2cpp` for
  perf. Non-development IL2CPP builds use Master / OptimizeSpeed (full LTO on
  this Mac), trading a longer explicit perf build for shipping optimizations.
  Development builds retain the configured compiler settings; the build
  restores the previous settings afterward.
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
- SETUP.md and `docs/champions/style.md` document the existing project.
  Their combat references follow the current design; older snapshots are archived.

## Combat Redesign: Migration Scope

These responsibilities originated in **ps-3wc** below and are updated by
**ps-ueo** where November 3 decisions supersede that baseline. Preserve working audio, deterministic timing, rendering and vendor
pipelines. Reconcile current code before deciding file boundaries or effort.

1. **Content and scaling contracts.** Six weapon slots, six shared support
   slots; weapon levels 1–8 and forms at 3/5/8; explicit compatibility
   inherited across forms; additive stat families that multiply each other;
   bounded split/echo triggers and per-target hit rules. Old ranks, dash
   replacements and evolution recipes are not carried forward.
2. **Automatic combat.** Weapons activate on the grid; attack speed creates
   additional strikes with deterministic fractional carry. Preserve natural
   body animation through spectral strikes. Add varied martial/magical
   weapons and persistent-weapon pulse semantics. Verify cadence across
   tempos and silence without rounding away small upgrades.
3. **Dash, held sword and aura.** Permanent held Sword in one of six slots;
   movement-directed ordinary aim, weakest-fodder one-hit baseline, at least
   twice the prior base cadence (Sword about 0.5 s), preserving hit damage and
   natural body speed. Every immediate dash ends with a full-circle sword
   sweep at the legal endpoint, without waiting for the grid or a support.
   One timed dash grants a refreshed three-second shared 2× area aura; no
   per-weapon banking, no hit-damage bonus, off-beat does not cancel it.
   Freeze duration during holds; define persistent contact and replay expiry.
   Retire/rework Landing Blast without duplicate base sweep damage.
4. **Growth and recovery.** Persistent dropped XP, attraction and collection;
   healing drops; capped supports; 25–35 immediate normal choices, no quiet
   waits or rerolls. Deterministic eligible offers, full-slot and exhausted
   pool handling. No automatic level-up healing. Pickups survive streaming
   and boss arrivals; merge visuals without losing stored value.
5. **Clock and bosses.** Separate horde time from combat time. Freeze horde
   time at 5/10/15 minutes, halt ordinary spawns, shatter ordinary enemies
   in the visible fight area, preserve structures/existing drops and prevent
   outside horde refill. Allow only small timed reinforcements with finite XP.
   Keep three always-damageable encounters using timed positioning and occasional mandatory dash-through
   waves, without Break. First two kills heal and offer one owned
   upgrade; last kill wins. Pause/resume must preserve telegraph safety.
6. **Presentation and play tuning.** Brief cards, bottom-left animated health orb, top-center horde/BOSS
   elapsed clock, green filled animated XP, yellow player aura, orange/red
   threats, no held-level dot or Break bar; recognizable weapon forms, pickups and empowered
   activations. Boss threats stay readable under full builds. Tune collection,
   choice frequency, enemy ramp and recovery together through hand-play.

### Obsolete acceptance to replace

Tests and scripted reference builds asserting five dashes, offensive dash
investment, three attacks/four mods, three-rank evolutions, instant kill XP,
quiet-held levels, six level-ups, two cleared duels,
Break, or a single clock continuing through bosses describe the archived
design. Retain them as historical evidence where needed; do not use them to
reject the revised behavior. Update implementation and regression tests together
in current survivor tests. Historical encounter tests explicitly load archived
fixtures; recorded attack/duel/level fixtures use current survivor behavior.

The existing epic map is [archived](../archive/champions-dash-combat-2026-11-02/plan.md#beads).
No entry there is silently redefined by this document. November 2 tests for
retained hordes, movement-only dash, one-use per-weapon empowerment and
white-only player effects are also superseded by ps-ueo. Arrival shatter
returns with three damageable bosses and controlled reinforcements, not Break.

## Combat Epic: ps-3wc (shipped baseline)

Use existing owned Unity assets first, reskinned and composed for the retro
3D neon wireframe design. The form table specifies gameplay identities and
breakpoints; exact actions must be grounded in inspected clips, meshes and
effects. Spectral choreography is an acceptable alternative to an unavailable
character animation. No asset purchases or hidden manual approval gates.
Never commit vendor-derived outputs. Asset mapping and family runtime previews have landed; all 32 forms use the
verified mappings and authored actions documented in design.md and family evidence.

| Bead | Work | Prerequisites within ps-3wc |
| --- | --- | --- |
| ps-3wc.1 | Owned-asset mapping and feasible forms for eight weapons/three bosses | None |
| ps-3wc.2 | Six-slot contracts, fixed forms, support compatibility and scaling | None |
| ps-3wc.3 | Beat cadence, fractional strikes and shared mechanical support execution | .2 |
| ps-3wc.4 | Movement dash, per-weapon empowerment and Landing Blast | .2 |
| ps-3wc.5 | Persistent XP/healing pickups, attraction and defensive supports | .2 |
| ps-3wc.6 | Immediate choices, legal offers and boss upgrade rewards | .2, .5 |
| ps-3wc.7 | Sword and Hammer, four forms each | .1, .2, .3 |
| ps-3wc.8 | Spear and Bow, four forms each | .1, .2, .3 |
| ps-3wc.9 | Orbit Blades and Sentinel, four forms each | .1, .2, .3 |
| ps-3wc.10 | Lightning and Ember Field, four forms each | .1, .2, .3 |
| ps-3wc.11 | Three rhythmic bosses, retained hordes and separate clocks | .1, .2, .4, .6 |
| ps-3wc.12 | Brief cards, minimal HUD and updated input/flow | .2, .4, .6 |
| ps-3wc.13 | End-to-end assembly, tuning, replays and shipped docs | .1–.12 |
| ps-3wc.14 | Full-build performance and visual readability | .13 |
| ps-3wc.15 | Single final Warden | .1–.14 |

Only actual output prerequisites create ordering. Shared manifests, registries,
presenters and content files are merged normally; they do not serialize the
four weapon-family tracks. The eventual invocation chooses concurrency.
There are **no cross-epic blocking edges**, including final verification.
The new epic checks its expanded combat load independently of ps-6y2's baseline
performance and art work. No Mason, Sage or Archivist; assembly updates docs.
Operator-only checks remain recorded in operator-pending rather than gates.

## Whole-Run Acceptance

Acceptance remains broader than automated verification. Assembly records
actual deterministic wins/deaths, choices and replays; it does not claim
speaker balance, subjective fun, target-hardware performance or full-build
visual approval. ps-3wc.14 and .15 follow assembly.

- `just champions --file <song>` plays level 1 from the starting weapon through
  three bosses to the victory card; a scripted run (`--walk` with picks) reaches the victory
  card headless, and a stationary script without defensive inputs dies under elite/boss pressure.
- Delayed listening plays an app's audio 3 s late (an operator-pending row
  confirms on the speakers).
- Every current design decision holds. Rule checks include inherited support
  compatibility across all forms, fractional attack-speed gains, shared three-second
  aura expiry/refresh, 2× coverage area and baseline dash sweep, persistent/merged XP conservation, legal offers,
  bounded proc chains, separate clocks, arrival clears with preserved drops/controlled re-entry and
  fair telegraph resume. Stills and hand-play verify the visual and experiential
  requirements; old reference-run hashes alone do not establish these.
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

- **Dash spam replacing positioning.** Any timed dash grants empowerment by
  design. Tune coverage bonus and encounter pressure so constant dashing is
  optional and baseline weapons remain sufficient for horde combat.
- **Unreadable boss fights.** Timed reinforcements, pickups, persistent fields and
  six weapons coexist with required rhythmic dodges. Test at full density;
  a still is insufficient: inspect the clear, re-entry prevention and required
  dodges with reinforcements in motion.
- **Too many slots for too few picks.** A 25–35-choice run cannot maximize
  six eight-level weapons and six supports. Deep and broad investment must
  both work; test the actual offer pool and XP curve together.
- **Tempo-dependent build power.** Strict beat attacks and extra strikes need
  explicit cadence and accumulation tests, not just 120 BPM demonstrations.
- **Content scope.** Several animated forms per weapon and a third boss are
  real implementation work. The proposed content needs visual evaluation;
  do not imply existing assets already supply every action.

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


## Combat-feel Restoration: ps-ueo

The [game-feel plan](game-feel-plan.md) records the approved November 3
contract, actual frozen trailer paths, current implementation gaps, bead map
and checks. It is the implementation guide for this run. ps-3wc's table above
records historical deliveries; it does not authorize their superseded behavior.
ps-6y2 look/performance work remains independent and must be preserved.

F cleave (ps-uyc): appended deterministic key code, shared dash cooldown/buffer,
judgement, shield and landing sweep; no movement command or travel presentation.
Keyboard and scripted inputs record/replay through the same input stream.
