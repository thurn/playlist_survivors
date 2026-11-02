# rhythm_champions

The Unity project of [Rhythm Champions](../docs/champions/design.md), built
by the Enact epic in [the plan](../docs/champions/plan.md). Unity
6000.6.7f1, URP 17.6, the Input System and UI Toolkit; it depends on no
other project in the tree.

So far it is the skeleton, the world and the UI: the Boot scene opens on the
title (Play, Calibrate, Quit), whose music picker starts a run on an app's
audio or a file; with a music source the music plays 3 s late
through the plugin while its beats feed the sim, whose beats send the beat
wave across the grid and whose section changes move the zone key. The
edge-cut pipeline and the edge crowd draw monsters (the skeleton so far) in
the crowd's test scene. The hero's weapon effects draw Hovl's slash shapes in
white (`--gallery` shows them all), and the hero's sounds are baked and
scheduled, for the assembly to wire to the sim. A run shows the hero's
indicator, the HUD and the card moment's cards; Esc pauses it.

## Commands

From the repository root ([SETUP.md](../SETUP.md#building-and-playing) has
the first-time steps):

| Command | What |
| --- | --- |
| `just champions [flags]` | Set up if needed, build the Mono player and play it at 1080p |
| `just champions-build [--backend il2cpp]` | Set up if needed and build; nothing if nothing changed (`--force`) |
| `just champions-play [flags]` | Play the last build |
| `just champions-tool <cmd>` | The `champions` CLI below |

The CLI (`rhythm_champions/tools`, a uv project; `champions --help`):

| `champions` | What |
| --- | --- |
| `setup` | Vendor files from `manifest.json`, the baked and Library caches, the plugin, the pre-push guard, the Unity licence |
| `build` | The Mac player (Mono by default) into `Builds/mac/<backend>/` |
| `play` | The last build; `--headless` runs it in batch mode and waits; other flags go to the player |
| `test` | EditMode tests |
| `editor [start\|stop\|status]` | A warm editor for this worktree, which build, test and unity then use |
| `check` | The guard, the EditMode tests and the variant budget (`--guard`, `--tests`, `--variants` for one) |
| `stills` | Fixed-time stills of the scripted scenes, with their type-ID passes and views, into `captures/stills/` |
| `look [DIR]` | The look metrics ([style.md](../docs/champions/style.md)) on stills: every threshold key, the palette's rules; `--out` writes evidence |
| `unity <Method>` | A batch-mode task method of `Champions.EditorTools.Tasks` |
| `edgecut <model>` | Bake the model's edge cut from its recipe (below) into `Assets/Baked/EdgeCuts/`; only the steps whose inputs changed (`--force`: all) |
| `edgecut review <model>` | Bake if needed, then the cut's review sheets and README in `evidence/edgecuts/<model>/`; exits 1 when a threshold fails |
| `guard`, `sync`, `manifest find\|add`, `library save` | The vendor guard, the vendor sync, curating the manifest, saving a warm Library |

Player flags so far: `--file PATH` (an audio file or M3U playlist) or
`--app NAME` (Delayed listening: the app playing audio that NAME names, a
pid or part of its name, tapped, muted and played 3 s late), `--seconds N`,
`--fixed-time` (exactly 1/60 s per frame; with `--file` the frames drive
the music silently), `--capture-stills DIR --stills-at T1,T2` (tagged 1080p
JPEGs at those times, then quit) or `--capture-stills DIR --stills-scenes
NAMES|all` (the scripted scenes' PNG stills, ID passes and views, as
`champions stills` runs them; `world` is the world's: 120 BPM to 30 s with a
section change at 12 s, then beatless with a dash at 34 s), and `--gallery`
(the weapon effects' gallery: every effect in turn at the hero's spot, looping
in a window; with `--capture-stills DIR`, a still of each, as `--stills-scenes
gallery`; with `--bench-out PATH` too, each effect's GPU time), and `--tier
low|medium|high` (the quality tier; without it the pause menu's last choice,
or the GPU's guess: an M-series Max or Ultra plays High, a Pro Medium,
anything else Low). With music, the log's `Music:` lines show
the delay, each beat the sim reaches with when it became known, and a
summary. `--edge-crowd N` is the edge crowd's test scene: N baked edge cuts
(`--edge-type ID`, default skeleton) walking in a field, 20 a second
breaking into shards; headless it times every frame and writes the
percentiles to `--bench-out PATH`. `--calibration FILE` keeps the calibrations
in FILE instead of the player's data folder. `champions play --headless` runs the player in
batch mode on Metal; given to the player itself, `--headless` makes Unity
draw with its Null device, so no still could be kept. For example, a still
of the first second, and 20 s of a song headless:

```sh
just champions-play --headless --fixed-time --capture-stills captures/boot --stills-at 1
just champions-play --headless --file public/reactive/house-128/house-128.ogg --seconds 20
```

## Layout

- `Assets/Champions/`: `Sim/` (Champions.Sim, no Unity), `Client/`
  (Champions.Client: Boot, launch options, `Music/` (the plugin's bindings,
  the music clock, the player and its sources), `Loop/` (the frame loop's
  clocks, the sim driver and the keys), `UI/` (the screens' flow, the
  menus, the cards, the HUD, the indicator's model; [below](#the-ui)),
  `Calibration/` (the video and input screens and their store), `World/` (the world presenter: sim events to
  the world), `Scenes/` (the stills' scripted scenes, the effects' gallery), `Sound/`
  (the hero's sounds: the bank, the scheduler, the beat's aggregation and the
  presenter from the sim's events)), `Render/`
  (Champions.Render: stills and the type-ID pass, `Look/`: the palette,
  `Effects/`: the weapon effects, their sparks and their shaders,
  `Crowd/`: the edge crowd, its shatter and their shaders, `World/`: the
  grid, the beat wave, event rings, the zone key, the camera, the post and
  the quality tiers, with the `World.hlsl` globals every world-reacting
  shader reads, `Shaders/`), `Editor/` (Champions.Editor: build tasks, the
  warm editor, the variant budget, `EdgeCuts/`: the edge cuts' export, bake
  and review, `Effects/` and `Sound/`: the effect library's and sound bank's
  bakes, run before every build), `Tests/` (EditMode), `Scenes/`, `Settings/` (the URP asset,
  its renderer and the post profile).
- `plugin/`: the music plugin, a Cargo workspace copied from the frozen
  `native/` (crates plugin, analysis, audio, capture, stream; the analyzer's
  models in `models/`). `champions setup` builds `libps_plugin.dylib` into the
  ignored `Assets/Plugins/macOS/` and the models into `Assets/StreamingAssets/ps/`;
  `cargo test` and `cargo clippy` run in it.
- `Content/`: the game's JSON content, the palette (`palette.json`,
  [style.md](../docs/champions/style.md)) among it.
- `manifest.json`: the vendor files the game uses, synced into the ignored
  `Assets/Vendor/`. Bakes write the ignored `Assets/Baked/`.
- `tools/`: the CLI and its tests; `tools/recipes/edgecut/`, the edge cuts'
  recipes; `tools/hooks/pre-push`, the guard setup installs for every checkout
  of the clone.
- `evidence/`: [operator-pending.md](evidence/operator-pending.md) and each
  bead's evidence.

## Edge cuts

Every model is redrawn by a committed recipe (design.md, The Look > Edge
cuts; plan.md, Rendering > Edge cuts), `tools/recipes/edgecut/<model>.py`:
its `MODEL` names the vendor prefab and clips by GUID, its `ROLE` (fodder,
elite, boss, hero) picks the thresholds, and `cut(source)` builds the cut with
`champions_tools.edgecut` (numpy and scipy; Blender 5.2 headless is allowed):
convex-hull body parts and flat discs, feature lines chosen from the
source's silhouette creases with keep, drop and cap lists and a count,
circles and strokes for replaced parts, and the shard pieces.
`champions edgecut <model>` runs three steps, each only when its inputs
changed: Unity exports the vendor model in root space (`EdgeCutSource`), the
recipe cuts it, and Unity bakes the cut (`EdgeCutBaker`): body and line
meshes, a 30 Hz bone texture of the clips over the entries the cut uses, the
pieces, and the library in `Assets/Baked/EdgeCuts/Resources/`. Everything it
writes stays in the ignored `Assets/Baked/`; only recipes, code and the
review's small tagged stills are tracked. The reference cut is the
skeleton's ([its review](evidence/edgecuts/skeleton/README.md)).

The edge crowd (`Render/Crowd/`, ported from game/'s bone-texture crowd)
draws each type with two indirect draws, its bodies (EdgeBody: near-black,
from both sides) and its lines (EdgeLines: constant pixel width, additive in
the instance's tint, the less important lines fading as the monster gets
small, the beat wave's flare); `EdgeShatter` breaks a kill into the cut's
pieces on the GPU on its impact frame. `EdgeLook` holds the look's inputs.

## Weapon effects and sounds

The hero's weapon effects (design.md, The Look > Weapon effects) are Hovl's
slash meshes and magic sword drawn through our own shaders
(`Render/Effects/`): `Slash.shader` sweeps an arc's head fast in and slow
out, the band a thin white core at the outer edge over a faint white-blue body,
coming to a point at the head and narrowing to nothing behind it, streaked and
dissolved by Hovl's noise; sparks are compute (one seed dispatch per frame
with new bursts, one procedural draw). `Content/effects.json` holds each
cue's layers; `Effects` takes commands (`IEffectCommands`: `Fire(cue, shot)`,
`Orbit(...)`) and draws one instanced call per mesh in use. The build bakes
`Assets/Baked/Effects/Resources/Effects.asset`, which references the synced
Hovl files and copies nothing. [The gallery's evidence](evidence/effects/README.md)
has a still and the cost of every cue.

The hero's sounds (design.md, Sound) are game/'s scheduler and aggregation,
ported (`Client/Sound/`): `Content/sounds.json` names each category's vendor
clips, the build bakes the bank (`Assets/Baked/Sounds/Resources/`, onsets
measured), and `SoundPresenter.Take` turns the sim's events into sounds the
moment the sim knows them: swings on their impacts, a dash's whoosh in heard
time with the on-beat accent under it, Leap Slam's landing, hits, each kill's
shatter and the hero's hurt.

## The UI

The UI (design.md, UI; [its evidence](evidence/ui/README.md)) is UI Toolkit
built in code (`Client/UI/`) on panels made from the editor's
`Client/Resources/UI/Panel.asset` (scaled from 1080p) with one stylesheet,
`Champions.uss`; a batch-mode player's panels draw into targets of their own
that the stills draw over the frame (`Stills.Overlays`). Without music on the
command line `Flow` leads from the title through the music picker (the apps
playing audio, then File..., the Mac's open panel) to a run, the pause menu
over it (Esc or the window losing focus) and the calibration screens; its
`ShowEnd` shows the end card. A run (`GameLoop`) owns the keys (`KeyInput`:
WASD or the arrows, Space, Enter, 1-3, each heard at keySong), the left
button's click targets (`PointerInput`: the press's ground point under the
pointer through the camera, then one a frame while the button is held, given
as the sim's `Target`; never a press over an element that takes the pointer,
`UiPanel.TakesPointer`, nor while a menu is over the run or the cards are up)
and the run's UI (`RunUi`): the hero's indicator in the world (`Render/UI/HeroIndicator`, a
camera-facing quad that Indicator.shader fills with the strip, the beats'
ticks, the dash's pip, the held level's dot and the health bar), the HUD (the
experience hairline, the boss and Break bars in a duel) and the cards, which
the sim's Offer, CardFocus and Pick events show, focus and put away; a card's
click taps its number key, so the sim picks it as it picks a key. Card faces
(names, lines per rank, icons) are `Content/cards.json` and the line-art SVGs
in `Content/icons/`.

## The lean pipeline

Forward, HDR, bloom and neutral tonemapping; shadows, SSAO, lightmaps,
decals, cookies, lens flares, reflection probes and light layers off; at most
four unshadowed per-pixel additional lights; no shader always included
(plan.md Decision 4). EditMode tests hold these settings.

**The variant budget**: a player build keeps at most 300 compiled shader
variants. `ShaderVariantBudget` (Champions.Editor) strips the variants of
the disabled features and the shaders of unused ones, counts the rest per
shader, pass and stage, writes `shader-variants.json` beside the player and
fails a build over budget. An incremental build that reuses its shaders
counts them as the last build left them; `champions check --variants`
builds with a clean build cache and prints the count and the largest
shaders.
