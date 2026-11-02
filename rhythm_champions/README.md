# rhythm_champions

The Unity project of [Rhythm Champions](../docs/champions/design.md), built
by the Enact epic in [the plan](../docs/champions/plan.md). Unity
6000.6.7f1, URP 17.6, the Input System and UI Toolkit; it depends on no
other project in the tree.

So far it is the skeleton: the Boot scene draws the black void.

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

Player flags so far: `--headless`, `--seconds N`, `--fixed-time` (exactly
1/60 s per frame), `--capture-stills DIR --stills-at T1,T2` (tagged 1080p
JPEGs at those times, then quit). For example, a still of the first second:

```sh
just champions-play --headless --fixed-time --capture-stills captures/boot --stills-at 1
```

## Layout

- `Assets/Champions/`: `Sim/` (Champions.Sim, no Unity), `Client/`
  (Champions.Client: Boot, launch options), `Render/` (Champions.Render:
  stills), `Editor/` (Champions.Editor: build tasks, the warm editor, the
  variant budget), `Tests/` (EditMode), `Scenes/`, `Settings/` (the URP asset,
  its renderer and the post profile).
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
