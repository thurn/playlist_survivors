# rhythm_champions

The Unity project of [Rhythm Champions](../docs/champions/design.md), with retained architecture and migration scope in
[the plan](../docs/champions/plan.md). Unity
6000.6.7f1, URP 17.6, the Input System and UI Toolkit; it depends on no
other project in the tree.

**Implementation status (2026-11-03):** the survivor combat redesign is playable:
eight automatic weapons with four forms, six weapon/support slots, persistent
XP/healing drops, immediate choices and bosses at 5/10/15 minutes of horde time.
Combat time includes boss fights; the horde clock pauses. The Conductor uses
the existing troll asset with alternating fan lanes and a traveling wall;
there is no Break or boss-entry horde wipe. Family evidence documents actual
spectral choreography rather than assuming unavailable body clips.

[Assembly evidence](evidence/survivor-assembly/README.md) records deterministic
runs, tuning and replays. Full-build performance/readability and the final
Warden remain ps-3wc.14/.15; hand-play, speakers and target hardware remain
[operator checks](evidence/operator-pending.md). The original ps-6y2 continues
independently. [Archived runtime overview](../docs/archive/champions-dash-combat-2026-11-02/runtime-readme.md)
and earlier combat captures describe historical behavior.

`--walk positioning --picks reference` follows cleared XP, uses reactive
movement dashes and crosses boss waves, without deliberate horde beat dashes.
`--walk reference` also adds voluntary rhythm inputs. These are deterministic
tuning scripts, not hand-play or balance approval. Session v6 records scripted
survivor presets; versions 1–5 remain readable.

## Commands

From the repository root ([SETUP.md](../SETUP.md#building-and-playing) has
the first-time steps):

| Command | What |
| --- | --- |
| `just champions [flags]` | Set up if needed, build the Mono player and play it at 1080p |
| `just champions-build [--backend il2cpp]` | Set up if needed and build; nothing if nothing changed (`--force`) |
| `just champions-play [flags]` | Play the last build |
| `just champions-tool <cmd>` | The `champions` CLI below |

Interactive terminal runs of `just champions` and `just champions-play` play with sound by default. Pass `--mute` for a quiet run. Codex, Claude and Enact sessions keep the CLI's muted default.

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
| `structure bake\|review KIND` | Bake a structure kind (or `all`) for both looks; its review sheets into `evidence/structures/` |
| `look [DIR]` | The look metrics ([style.md](../docs/champions/style.md)) on stills: every threshold key, the palette's rules; `--out` writes evidence |
| `unity <Method>` | A batch-mode task method of `Champions.EditorTools.Tasks` |
| `guard`, `sync`, `manifest find\|add`, `library save` | The vendor guard, the vendor sync, curating the manifest, saving a warm Library |

Player flags so far: `--file PATH` (an audio file or M3U playlist) or
`--app NAME` (Delayed listening: the app playing audio that NAME names, a
pid or part of its name, tapped, muted and played 3 s late), `--seconds N`,
`--fixed-time` (exactly 1/60 s per frame; with `--file` the frames drive
the music silently), `--capture-stills DIR --stills-at T1,T2` (tagged 1080p
JPEGs at those times, then quit) or `--capture-stills DIR --stills-scenes
NAMES|all` (the scripted scenes' PNG stills, ID passes and views, as
`champions stills` runs them; `world` is the world's: 120 BPM to 30 s with a
section change at 12 s, then beatless with a dash at 34 s; `level` is level 1
through the run's own presenters and the reference player: the crowd at minute
4, a loud surge and a card moment), and `--gallery`
(the weapon effects' gallery: every effect in turn at the hero's spot, looping
in a window; with `--capture-stills DIR`, a still of each, as `--stills-scenes
gallery`; with `--bench-out PATH` too, each effect's GPU time), and `--tier
low|medium|high` (the quality tier; without it the pause menu's last choice,
or the GPU's guess: an M-series Max or Ultra plays High, a Pro Medium,
anything else Low), `--look trailer` (the monsters exactly as the neon edge
trailer drew them), `--crowd-bench N` (N monsters timed, headless, with
`--bench-out PATH`), `--seed N` (the run's seed; a scripted run's defaults
to 1), `--walk idle|reference|KEYS` and `--picks 1,2,...|reference` (the
scripted player, `Sim/Session/ScriptedPlayer.cs`: `idle` never moves,
`reference` is the tuning's reference player, KEYS held keys like
`right@9-11,space@14-14.1`; picks by card number, or the reference build's
preferences), `--record DIR` (the first run's session: the plugin's entries
and audio, then `inputs.bin` and `run.json`) and `--replay DIR[,DIR...]`
(each recorded session again through the presenters with no music, at
`--replay-speed X`; exits 0 when every one settled its recorded hash, which
it does in Mono as in .NET). With music, the log's `Music:` lines show
the delay, each beat the sim reaches with when it became known, and a
summary; `Run:` lines show the run's picks, bosses and end. A batch-mode run
quits a second after its end card. `--calibration FILE` keeps the calibrations
in FILE instead of the player's data folder. `champions play --headless` runs the player in
batch mode on Metal; given to the player itself, `--headless` makes Unity
draw with its Null device, so no still could be kept. For example, a still
of the first second, and 20 s of a song headless:

```sh
just champions-play --headless --fixed-time --capture-stills captures/boot --stills-at 1
just champions-play --headless --file public/reactive/house-128/house-128.ogg --seconds 20
just champions-play --headless --fixed-time --file playlist.m3u --walk reference --seed 2
just champions-play --headless --replay rhythm_champions/sessions/fixture-level
```

### Player logs

`just champions-tool logs` prints the persistent root and this checkout's latest
play/test/capture/replay engine logs. Each managed launch prints its full path:
`$CHAMPIONS_CACHE/player-logs/<checkout-id>/<kind>/<UTC-time>-<UUID>/engine.log`
(default macOS root: `~/Library/Caches/playlist-survivors/champions/player-logs`;
`$PS_CACHE/champions` also works). Checkout IDs hash the resolved absolute checkout
path, preserving case; symlink aliases resolve to the same checkout. No song paths
or launch arguments are stored in the launch metadata. Logs themselves can contain
private paths, so keep them local. Logs survive deleting the checkout or built app.

`just champions`, `just champions-play`, `champions play --headless`, `champions
stills`, and CI's player/replay/capture runs pass `-logFile` before Unity starts.
Replays take precedence over captures; capture/still/benchmark flags select
`capture`, batch/CI launches select `test`, other launches select `play`.
`Logs/player.log` and `Logs/stills.log` are atomic latest-log links for existing
sequential CI readers. Concurrent callers should use their printed unique paths.
A kind's persistent `latest` link points to its latest allocated run.

`champions build` installs a macOS app launcher, including when the Unity build
is already current. `champions play` installs it into older local builds too.
Finder, `open -n RhythmChampions.app`, and `Contents/MacOS/champions-launch` then
isolate logs before starting the native engine. The launcher ships inside the
app, retains the original checkout identity when the app is copied, and needs
`/usr/bin/python3` (provided by the required Xcode Command Line Tools, Python
3.9 or newer). The launcher itself does not need the tools environment; the game still needs
its usual `rhythm_champions/Content` directory above the app or working directory.
The app is ad hoc signed again after packaging. Raw Unity editor builds or
`champions unity BuildPlayer` bypass this packaging; run `champions build` or
`champions play` before directly launching those apps. Running the native
`Contents/MacOS/Rhythm Champions` binary yourself bypasses the launcher and
uses Unity's shared default unless you supply `-logFile`. Old, unwrapped apps
also retain Unity's default. Editor batch logs still stream to stdout and
`Logs/<task>.log`; warm editor and EditMode report paths are unchanged.

An explicit `-logFile PATH`, `-logFile -` (stdout), or `-logFile ""` (Unity
logging disabled) is passed through without adding or retaining a managed log.
The CLI resolves relative file destinations against the calling directory;
raw/direct callers choose their own working directory. Explicit destinations
are caller-owned and may overwrite if reused. Native stdout remains available.

After the engine exits, `complete.json` records its exit status. Cleanup at
allocation/completion retains at most 100 completed runs per checkout/kind,
1000 completed runs globally, and removes completed runs older than 30 days,
including those from removed worktrees. These limits apply on the next managed
launch. Active runs and interrupted launchers without a completion marker are
never automatically deleted; inspect and remove abandoned runs manually when
needed. No age or PID guess can delete a still-running player. This conservatively
exempts active/crashed runs from the retention limit. Gameplay summaries may
use the directory of `Application.consoleLogPath` when file logging is enabled.

The sim's tests play level 1 too (`dotnet/Champions.Sim.Tests/LevelRunTests.cs`): an idle hero
dies within 90 s, and the explicit tuning tests run the reference player through the whole level
([evidence](evidence/ps-6y2.21/README.md)).

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
  (Champions.Render: stills and the type-ID pass, the stand-ins, `Look/`: the
  palette, `Effects/`: the weapon effects, their sparks and their shaders, `World/`: the
  grid, the beat wave, event rings, the zone key, the camera, the post and
  the quality tiers, with the `World.hlsl` globals every world-reacting
  shader reads, `Shaders/`), `Editor/` (Champions.Editor: build tasks, the
  warm editor, the variant budget, `Effects/` and `Sound/`: the effect
  library's and sound bank's bakes, run before every build), `Tests/` (EditMode), `Scenes/`, `Settings/` (the URP asset,
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
- `tools/`: the CLI and its tests; `tools/hooks/pre-push`, the guard setup
  installs for every checkout of the clone.
- `evidence/`: [operator-pending.md](evidence/operator-pending.md) and each
  bead's evidence.

## Stand-ins

A checkout without the baked vendor files draws stand-ins
(`Render/StandIn.cs`): Unity's primitives at their size, drawn with
`Resources/Champions/StandIn.shader` (a near-black body whose silhouette glows
in its role's colour) and coded for the type-ID pass: the structures
(`StructurePresenter`) as blocks and cylinders on their footprints, gone on
their break's frame, the hero (`HeroPresenter`) as a white capsule its motion
places, turns, lifts and hides, a boss (`BossPresenter`) as a capsule in its
palette colour, the horde as spindles (`StandInCrowd`).

## Structures

The eight structure kinds are Synty models (`Editor/Structures/structures.json`)
in the edge look over a dim fill in the zone's key (ps-6y2.30,
[evidence](evidence/structures/README.md)): `champions structure bake` combines
each prefab at its height on its footprint's middle into a style mesh whose
creases over 24 degrees draw (the monsters' `StyleMesh`), cut into chunks
outlined along their cracks (`MonsterShardBaker`), into the ignored
`Assets/Baked/Structures/`; the build bakes them too. `--look trailer` draws
the models unsimplified. In the default look a kind with a recipe,
`tools/recipes/structure/KIND.py` (ps-6y2.43), draws a designed low-poly mesh
instead, every edge of it: the recipe builds convex polygons with
`champions_tools.structure`'s `Builder` (boxes, prisms, lofts, slabs, wall faces
with inset doors and windows), whose outlines draw and whose triangles'
diagonals never do, and `champions structure bake` runs it first; a kind
without one may get its own crease angle where that looks better at game
scale, saying why in the table. `Render/Structures/`
draws them (`StructureRenderer`, one indirect draw a kind;
`StructureEdge.shader`), flashes them when hit, and on a break's frame throws
their chunks in PhysX (`StructureShatter`, `StructureEdgeShard.shader`); the
presenter (`Client/World/StructurePresenter`) maps the sim's events onto them
through an `IStructureView`.

### Boss Rush act kits

Each Boss Rush act's environment (boss-rush.md, Environments) is a kit in
`Content/arenas.json`: its prop kinds (`"kit"`), the placeholders it dresses,
its floor scatter and its backdrop rings. Its recipe,
`tools/recipes/kit/KIT.py` (ps-8z4.5), designs every piece with the structure
recipes' `Builder`; `champions kit bake KIT` (and the build) writes them into
the ignored `Assets/Baked/Kits/`, and `ArenaPresenter` draws them
(`Render/Arena/KitMeshes.cs`). `champions stills --scene kit-KIT` shows one.

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

This section documents the existing UI. The revised design removes held-level
and Break indicators; it retains brief cards and a minimal HUD.

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

**The variant budget**: a player build keeps at most 1,000 compiled shader
variants. `ShaderVariantBudget` (Champions.Editor) strips the variants of
the disabled features and the shaders of unused ones, counts the rest per
shader, pass and stage, writes `shader-variants.json` beside the player and
fails a build over budget. An incremental build that reuses its shaders
counts them as the last build left them; `champions check --variants`
builds with a clean build cache and prints the count and the largest
shaders.
