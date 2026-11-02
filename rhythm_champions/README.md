# rhythm_champions

The Unity project of [Rhythm Champions](../docs/champions/design.md), built
by the Enact epic in [the plan](../docs/champions/plan.md). Unity
6000.6.7f1, URP 17.6, the Input System and UI Toolkit; it depends on no
other project in the tree.

So far it is the skeleton: the Boot scene draws the black void, and with a
music source the music plays 3 s late through the plugin while its beats
feed the sim. The edge-cut pipeline and the edge crowd draw monsters (the
skeleton so far) in the crowd's test scene.

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
| `unity <Method>` | A batch-mode task method of `Champions.EditorTools.Tasks` |
| `edgecut <model>` | Bake the model's edge cut from its recipe (below) into `Assets/Baked/EdgeCuts/`; only the steps whose inputs changed (`--force`: all) |
| `edgecut review <model>` | Bake if needed, then the cut's review sheets and README in `evidence/edgecuts/<model>/`; exits 1 when a threshold fails |
| `guard`, `sync`, `manifest find\|add`, `library save` | The vendor guard, the vendor sync, curating the manifest, saving a warm Library |

Player flags so far: `--file PATH` (an audio file or M3U playlist) or
`--app NAME` (Delayed listening: the app playing audio that NAME names, a
pid or part of its name, tapped, muted and played 3 s late), `--seconds N`,
`--fixed-time` (exactly 1/60 s per frame; with `--file` the frames drive
the music silently), `--capture-stills DIR --stills-at T1,T2` (tagged 1080p
JPEGs at those times, then quit). With music, the log's `Music:` lines show
the delay, each beat the sim reaches with when it became known, and a
summary. `--edge-crowd N` is the edge crowd's test scene: N baked edge cuts
(`--edge-type ID`, default skeleton) walking in a field, 20 a second
breaking into shards; headless it times every frame and writes the
percentiles to `--bench-out PATH`. `champions play --headless` runs the player in
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
  clocks and the sim driver)), `Render/` (Champions.Render:
  stills, `Crowd/`: the edge crowd, its shatter and their shaders),
  `Editor/` (Champions.Editor: build tasks, the warm editor, the variant
  budget, `EdgeCuts/`: the edge cuts' export, bake and review), `Tests/`
  (EditMode), `Scenes/`, `Settings/` (the URP asset, its renderer and the
  post profile).
- `plugin/`: the music plugin, a Cargo workspace copied from the frozen
  `native/` (crates plugin, analysis, audio, capture, stream; the analyzer's
  models in `models/`). `champions setup` builds `libps_plugin.dylib` into the
  ignored `Assets/Plugins/macOS/` and the models into `Assets/StreamingAssets/ps/`;
  `cargo test` and `cargo clippy` run in it.
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
