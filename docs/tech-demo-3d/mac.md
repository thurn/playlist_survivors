# 3D Tech Demo: Running on the Mac

The demo was built and measured on Cygnus ([Working on Cygnus](windows.md)).
Since integration it also sets up, bakes, builds and plays on the operator's
Mac (Apple silicon), for watching and for work that doesn't need Cygnus's
numbers. Every measurement in the plan stays Cygnus's: the Mac's player draws
with Metal on another GPU, and nothing here is a performance claim.

## What the Mac needs

- **Unity 6000.6.7f1** with Mac Build Support (IL2CPP), and Xcode for
  IL2CPP's compiler. It has no Windows module, so the Mac builds only its own
  player: IL2CPP as on Cygnus, or Mono with `--backend mono` (a faster build).
- **The .NET SDK 10, `uv` and ffmpeg**, from Homebrew.
- **The vendor sources and the song**, at the defaults setup writes to
  `~/Library/Caches/playlist-survivors/demo3d/roots.json`: `~/synty`,
  `~/unity_assets/UnityAssets` and
  `~/Downloads/Alessia Labate, Conrad Taylor - Action [NCS Release].mp3`.

Homebrew's ffmpeg has no libvorbis, so setup writes the song as `action.wav`
in place of `action.ogg`: the same decoded samples, with the MP3's encoder
delay trimmed, so sample 0 is still the song map's time 0. Unity's importer
encodes either to streamed Vorbis.

## Commands

From a worktree's root:

```
uv run --project demo3d/tools demo3d setup        # vendor files, the song, a warm Library
uv run --project demo3d/tools demo3d unity BakeAll    # build runs it if this worktree hasn't
uv run --project demo3d/tools demo3d build        # Builds/mac/il2cpp/PlaylistSurvivors3D.app
uv run --project demo3d/tools demo3d test         # EditMode tests
```

The first setup imported the project in about a minute, and saved the
`Library` to the cache for the next worktree. `BakeAll` took 2.5 minutes, both
in batch mode ([evidence](../../demo3d/evidence/mac/README.md)).

That saved `Library` holds only the import. The first full player build compiles
every shader variant the baked scenes use, over 20 minutes cold (ps-e8r.9), so
`build` saves the `Library` again after its first success for each set of
packages and vendor files, and later worktrees start with its shader cache.
A later build that compiles over 100 variants (new keywords, such as a post
effect turned on) saves it again, so the next worktree doesn't repeat them. A
player started before `BakeAll` logs the missing asset and, in batch mode, quits
with exit 1.

To watch the demo, windowed at 1080p with sound (the operator's view; agents
stay windowless):

```
open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --overlay
```

The player takes the same flags as on Cygnus ([Manual QA](qa.md#manual-qa)),
such as `--start-at 160` for the climax or `--click` for the click track.

To compare crowd sizes in the song itself, change `--envelope <count>` (the
final peak holds 1.05 times it on screen; the level script's is 425) and keep
everything else equal. `--view-width <m>` widens the song's camera: it scales
every section's zoom so the widest (the climax, 30 m in the level script) is
`<m>`, and the timeline places the horde for the wider view. For example,
`--start-at 160 --view-width 40 --envelope 300`, then the same with
`--envelope 900`. The hero's route follows the horde, so it differs a little
between counts.

Windowless stills, for agents' checks: a batch-mode player draws no frames,
so it draws the camera by hand for each still it owes.

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile demo3d/Logs/mac-stills.log \
  --fixed-time --capture-stills captures/mac --stills-at 45,170,196 --seconds 200
```

Windowless frame times, for agents' checks: `--gpu-probe-at 60,175`
(with `--gpu-probe-out <json>`) hand-draws three timed frames after a warm-up
at each song time, into a 1080p target, and times each through GPU
completion: CPU submission and GPU in series, an upper bound on a windowed
frame. Keep it to a handful of times, since hand-drawn frames hold URP's
pooled targets. `--stats <csv>` in real time gives every half second's CPU
frame (no drawing) through the song. `--frame-probe` needs a windowed player.

## The look set

The look pass (epic ps-e8r) compares every change with stills and numbers.
One command renders the whole set windowless, from a fresh worktree:

```
uv run --project demo3d/tools demo3d setup && uv run --project demo3d/tools demo3d look <slug> --baseline baseline
```

`look` builds the IL2CPP player if the worktree has none (and runs `BakeAll`
first if it isn't baked): about 4.5 minutes in a fresh worktree with the
saved Library, then about 25 s per set. After a change, pass `--build` (or
run `build`, and `BakeAll` if a baker reads the change) so the stills show
it. `--backend mono` uses a Mono player, whose simulation diverges from
IL2CPP's, so never compare sets made by different players
([baseline README](../../demo3d/evidence/look/baseline/README.md#build-path-and-timings)).

It runs one batch-mode player per still, four at a time, in fixed time:

- **Song stills** at 25, 60, 100, 145 and 175 s (groove, drop-1, groove-2,
  drop-2, the final peak), each from 5 s before, and again with
  `--no-effects` to find the effects' pixels.
- **TD15's six beauty shots** (arena-layout.json's `shots`), three captures
  each as in [beauty.py](../../demo3d/evidence/td15/beauty.py): the horde,
  the measured still (`--no-effects --capture-mask`, with its ID render) and
  `--beauty-empty`.

Full PNGs go to the gitignored `captures/look/<slug>/`. Tagged 960 px JPEGs,
`look.md`, `look.json` and, with `--baseline`, `contact-sheet.jpg` go to
`demo3d/evidence/look/<slug>/`. `look.md` has each still's L* p2/p50/p98, RMS
contrast, mean chroma, near-black and clipped shares, and the largest
connected clipped region (a blown-out blob), and the share in the cool
accents' hues (HSV 180-280 degrees, C*ab > 20; the palette pass caps it at
15% of a song still); for every still, the effects' pixels (against the same
frame with `--no-effects`) and whether their L* p98 is above the other
pixels'; for the beauty shots, the monsters' L* p10/p90 and readability, whose
floor is |dL*| >= 17. The command exits 1 if a shot is below the floor. Every still
also gets the stage (ps-e8r.5): the median L* of the ground within 6 m of the
hero against the frame's outer 15% border, from the `view-<t>.json` the
player writes beside each still (its camera's view-projection and the hero's
feet). The empty beauty shots also get the ground (ps-e8r.6): the median L*
of fighting ground (inside the layout's clearings and paths) and of the edge
bands and forest past them, and the fighting ground's local detail (the mean
standard deviation of L* in 16 px windows).
`--measure-only` re-measures kept captures; `--trial` writes nothing to
evidence; `--player-args` passes flags to every capture. To tell effects apart in a
still, a player's `--only-effects a,b` spawns only those library effects. The baseline set is
[evidence/look/baseline](../../demo3d/evidence/look/baseline/README.md).

### Palettes

`--palette <name>` (ps-ocd) swaps the environment's colours for one of
arena-layout.json's `palettes`, to see how the type colours and the effects
read against other backgrounds without building new environments: `snow`
(light, cool), `desert` (light, warm), `lava` (dark, warm), `neon` (dark,
cool), `blossom` (mid-value pink) and `crypt` (neutral grey, the control).
A palette recolours what the runtime sets: the moon's realtime light,
ambient, fog and clear colour, vignette, the hero's light, a tint on the
ground and the mist. The baked kit, its bounce light and the flames keep the
night's. Render one as a look set with the player args:

```
uv run --project demo3d/tools demo3d look palette-snow --player-args "--palette snow" --trial
```

## Limits on the Mac

- **Sweeps, the song check's verdict and PIX.** The bench player refuses to
  sweep off Windows, and the song check's machine samples are Windows
  counters, so a Mac verdict always says "no machine samples".
- **Video capture** needs a windowed player (on either machine); on the Mac
  it pipes frames into ffmpeg through `System.Diagnostics.Process`, not yet
  run (plan.md, Pending on the operator).
- **Unity rewrites tracked settings on the Mac.** `demo3d unity Configure`
  also rewrites the URP global settings' runtime list, the quality level's
  anti-aliasing and two bench materials' colours. Commit only what a change
  needs, such as the Metal API entry in `ProjectSettings.asset`. A build also
  rewrites the pipeline asset's shader prefilters and the global settings'
  runtime list, but `BuildPlayer` restores both afterwards, so a build leaves
  the checkout clean and Tollgate can still sync local master.
