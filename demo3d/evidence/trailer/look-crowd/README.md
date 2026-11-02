# The crystal cut on the look package's crowd (ps-wnj.22)

The crystal cut (`--trailer crystal`, `--monster-style crystal`) now draws its
monsters and their shards with the look package's `BoneTextureCrowd` and GPU
`MonsterShatter`, in `MonsterLooks.Active`, fed by demo3d's `LookCrowd`
([crowd.md](../../../../docs/archive/tech-demo-3d/crowd.md#monster-styles), The crystal
cut on the look package). demo3d's crystal forks are deleted: `CrystalShatter`
(CPU-posed PhysX shards), `CrowdShards`, `CrowdPose`, its `CrowdShardBaker` and
the `Crystal` and `CrystalShard` style shaders. The action cut keeps demo3d's
own crowd.

## How it was run

On jarvis (Apple silicon), IL2CPP Mac players: before from the base commit
(b8b699ef) in a scratch worktree, after from this change, each `demo3d build`
baking every baker afresh (lightmaps included). Stills, windowless:

```sh
"Playlist Survivors 3D Demo" -batchmode -force-metal --trailer crystal --fixed-time \
  --capture-stills DIR --stills-at 2.0,9.0,16.0,24.0,55.95,56.3 --stills-format png
"Playlist Survivors 3D Demo" -batchmode -force-metal --trailer action --fixed-time \
  --capture-stills DIR --stills-at 2.0,9.0,16.0,24.0 --stills-format png
```

The filmstrip is the climax's mass kill, the dragon's first breath (trailer
55.90 s), from both players:

```sh
uv run --project demo3d/tools demo3d filmstrip look-crowd-before --at 55.6 --frames 24 --every 3 \
  --warmup 3.0 --player-args "--trailer crystal" --app <before player>
uv run --project demo3d/tools demo3d filmstrip look-crowd-after --at 55.6 --frames 24 --every 3 \
  --warmup 3.0 --player-args "--trailer crystal" --baseline look-crowd-before --evidence trailer/look-crowd
```

## Stills

Each `crystal-*.jpg` is before (left) and after (right). Per-pixel RGB
differences (0-255) of the full 1080p stills:

| Still | max | mean | pixels over 8 | pixels over 32 |
| --- | ---: | ---: | ---: | ---: |
| crystal 2 s | 246 | 4.6 | 88,427 | 53,433 |
| crystal 9 s | 255 | 18.5 | 434,038 | 270,956 |
| crystal 16 s | 255 | 3.0 | 85,219 | 38,440 |
| crystal 24 s | 255 | 10.2 | 237,008 | 135,330 |
| crystal 55.95 s (the breath) | 255 | 8.3 | 241,315 | 150,079 |
| crystal 56.3 s (its shards) | 255 | 11.6 | 259,103 | 173,053 |
| action 2 s | 3 | 0.001 | 0 | 0 |
| action 9 s | 7 | 0.001 | 0 | 0 |
| action 16 s | 4 | 0.001 | 0 | 0 |
| action 24 s | 2 | 0.001 | 0 | 0 |

**The action cut is unchanged** ([action-016.000.jpg](action-016.000.jpg)).

**The crystal cut's monsters look the same**: the same gems in the same type
colours, the same inner glow on dark ground, the same beat throb and squash
(carried into the package's crowd as globals only demo3d sets, so the game's
look doesn't move). The crystal cut differed from itself between two runs of
one build by up to 255 on 165,000 pixels at 24 s (PhysX;
[crowd-move](../../../../game/evidence/look/crowd-move/README.md)), so the
numbers above are mostly the shatter, which is now deterministic. Where the
stills differ, it is where the game's version diverged from demo3d's:

- **The shards** are the game's GPU chunks, not PhysX rigid bodies: the same
  kick (demo3d's tuning), but they bounce and slide on the ground without
  colliding with each other, so a kill's shards settle closer to where it died
  in a denser drift instead of scattering and stacking
  ([crystal-009.000.jpg](crystal-009.000.jpg), the green kills round the hero).
- **Every death shatters.** The overdrive feel's cap of 240 live shard sets was
  a PhysX budget; with it gone, deaths out of view and past the cap shatter too
  (2,359 deaths shattered over the trailer, against 2,152; at most 10,595 shards
  live against 7,553), and their bursts and glass sounds play at their blows.
- **LODs.** The package draws LOD1 below 60 projected pixels and LOD2 below 30
  (demo3d's `lod` cut was off); at the trailer's framing few monsters are that
  small, so this barely shows.
- **The hit flash** is the game's, half white and half the gem's colour.

## Deaths break on their impact frame

[look-crowd-after-vs-look-crowd-before.jpg](look-crowd-after-vs-look-crowd-before.jpg):
each pair of rows is before above after, every 3rd frame (0.05 s). The breath's
kills break on its 55.90 s frame in both, the bodies gone and their shards in
their place on that frame, with no build-up. [impact-crop.jpg](impact-crop.jpg)
zooms into the breath's cone on 55.85, 55.90, 55.95, 56.00 and 56.10 s (before
above after).

## Tests and checks

- demo3d EditMode: 470/476, the three CrowdReflection failures that predate
  this change, three skips (SkeletonLodPreview). The package's shatter and
  shard-cutter tests (`PS.Look.Tests.ShatterTests`) pass in it and cover the GPU
  shards; the new `CrowdBakerTests.TheLookLibraryHoldsEveryTypeForThePackagesCrowd`
  checks the library demo3d bakes for them. The trailer's choreography checks
  (`TrailerTests`) pass.
- The trailer's mix checks (`demo3d trailer --out captures/look-crowd-standin.mp4
  --stand-in -- --trailer crystal`): 957 cues, none without a clip, peak 0.950
  after the limiter (ceiling 0.95), every cut's ringing silent 60 ms after it,
  the end fade clean.
- The after run logged no errors: `monster shatter: 2359 deaths, at most 10595
  shards live`, the crowd sized at 1,592 instances a type.
