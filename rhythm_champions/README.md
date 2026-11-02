# rhythm_champions

The Unity project of [Rhythm Champions](../docs/champions/design.md), built
by the Enact epic in [the plan](../docs/champions/plan.md). Unity
6000.6.7f1, URP 17.6, the Input System and UI Toolkit; it depends on no
other project in the tree.

So far it is the skeleton: the Boot scene draws the black void, and with a
music source the music plays 3 s late through the plugin while its beats
feed the sim.

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
| `guard`, `sync`, `manifest find\|add`, `library save` | The vendor guard, the vendor sync, curating the manifest, saving a warm Library |

Player flags so far: `--file PATH` (an audio file or M3U playlist) or
`--app NAME` (Delayed listening: the app playing audio that NAME names, a
pid or part of its name, tapped, muted and played 3 s late), `--seconds N`,
`--fixed-time` (exactly 1/60 s per frame; with `--file` the frames drive
the music silently), `--capture-stills DIR --stills-at T1,T2` (tagged 1080p
JPEGs at those times, then quit). With music, the log's `Music:` lines show
the delay, each beat the sim reaches with when it became known, and a
summary. `champions play --headless` runs the player in
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
  stills), `Editor/` (Champions.Editor: build tasks, the warm editor, the
  variant budget), `Tests/` (EditMode), `Scenes/`, `Settings/` (the URP asset,
  its renderer and the post profile).
- `plugin/`: the music plugin, a Cargo workspace copied from the frozen
  `native/` (crates plugin, analysis, audio, capture, stream; the analyzer's
  models in `models/`). `champions setup` builds `libps_plugin.dylib` into the
  ignored `Assets/Plugins/macOS/` and the models into `Assets/StreamingAssets/ps/`;
  `cargo test` and `cargo clippy` run in it.
- `manifest.json`: the vendor files the game uses, synced into the ignored
  `Assets/Vendor/`. Bakes write the ignored `Assets/Baked/`.
- `tools/`: the CLI and its tests; `tools/hooks/pre-push`, the guard setup
  installs for every checkout of the clone.
- `evidence/`: [operator-pending.md](evidence/operator-pending.md) and each
  bead's evidence.

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
