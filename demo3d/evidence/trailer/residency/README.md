# Trailer: four environments resident in one player (ps-hgs.1)

The player can now hold crypt, desert, snow and forest at once and switch the
drawn one within a frame ([trailer.md, Environments in one
player](../../../../docs/tech-demo-3d/trailer.md#environments-in-one-player)).
Everything here is from the IL2CPP player on the Mac (Apple M5 Max, Metal),
windowless at fixed time, built at staging `8161a26c` plus this bead.

**Verdict:** the scenes overlap at the origin with no offset. Each environment
looks exactly as it does alone, and a switch costs about 0.1 ms.

| file | what |
|---|---|
| `switch-*.jpg` | stills at and after each switch (below) |
| [look-forest.md](look-forest.md), [look-crypt.md](look-crypt.md), [look-desert.md](look-desert.md), [look-snow.md](look-snow.md) | each environment resident against alone, still by still |
| [normal-vs-final.md](normal-vs-final.md) | a normal run (no `--resident`) against [look/final](../../look/final/README.md) |
| [timing.md](timing.md) | frame times before, at and after each switch |
| [residency.py](residency.py) | the comparisons, the timing table and the JPEGs |

## The debug entry point

Two development flags exercise switching until trailer playback exists:

- `--resident crypt,desert,snow,forest` loads those environments beside
  `--environment`'s. That one is active first, and the timeline plays its
  layout.
- `--switch-every <s>` cycles the drawn environment through them every s song
  seconds from `--start-at`.

The stills come from one run:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile $PWD/captures/residency/switch/player.log --fixed-time \
  --resident crypt,desert,snow,forest --switch-every 2 --start-at 140 \
  --capture-stills $PWD/captures/residency/switch --stills-format png \
  --stills-at 141.983,142,142.5,144,144.5,146,146.5,148,148.5 --seconds 9
```

(The player resolves relative paths elsewhere, so pass absolute ones.)

| still | environment | |
|---|---|---|
| switch-141.983 | forest | the frame before the first switch |
| switch-142.000 | crypt | the switch frame itself |
| switch-142.500 | crypt | half a second on |
| switch-144.000 / 144.500 | desert | at and after |
| switch-146.000 / 146.500 | snow | at and after |
| switch-148.000 / 148.500 | forest | back to the first |

Every switch frame shows only the new environment: its own ground, kit,
moon, fog, grade and vignette, with nothing of the old one. The horde and the
hero walk the forest's layout in every environment, since the timeline is the
forest's. That is expected here; the trailer gives each shot its own
simulation.

## Arrangement: overlap at the origin

The test was the look requirement: with all four resident, each environment
must match a normal single-environment run of it within 1 L*.

For each environment, two look sets were rendered (`demo3d look SLUG --trial`):

- one alone: `--player-args "--environment E"`;
- one resident: `--player-args "--environment E --resident crypt,desert,snow,forest --switch-every 0.25"`.

A quarter-second cycle through four environments returns to E every second.
So every song still (5 s after its start) and beauty shot (3 s after) is taken
on the very frame that switches back to E, after the player has drawn every
other environment several times. That is the hardest case: no frame for state
to settle.

| environment | stills | largest median or mean difference | largest mean per-pixel abs dL* |
|---|---:|---:|---:|
| forest | 28 | 0.01 L* | 0.03 |
| crypt | 28 | 0.01 L* | 0.03 |
| desert | 28 | 0.02 L* | 0.14 |
| snow | 28 | 0.01 L* | 0.11 |

The 28 stills are each set's song stills, beauty shots, measure stills (no
effects) and empty shots. The pass line is 1 L*.

The per-pixel differences are scattered single pixels across the whole frame.
In the worst still (desert's forest-edge-measure, which has no effects), 2.2%
of pixels differ by more than 1 L* and 0.01% by more than 5. That is the
noise two players show when they have drawn different numbers of frames
([mac.md, Filmstrips](../../../../docs/tech-demo-3d/mac.md#filmstrips)). A
probe or lightmap error would instead shade whole props.

What makes the overlap work:

- **Lightmaps** need nothing: every loaded scene's lightmaps join one list,
  and each renderer indexes its own.
- **Light probes** do need care. Every scene bakes the same 882-probe grid,
  and after four additive loads Unity still holds only 882 probes (each
  scene's `ready` log line says `882 light probes (882 loaded)`). So one
  scene's probes would light every scene's probe-lit props. A switch
  therefore sets `LightmapSettings.lightProbes` to the new scene's own
  (`LightProbes.GetSharedLightProbesForScene`). The EditMode test
  `ResidentArenasSwitchEveryGlobalTheirSetUpWrote` checks the swap with the
  rest of each switch's globals. A build without the swap was not tried, so
  how far the props would have drifted without it is unmeasured.

## Switch cost

Each frame's switch is timed and logged
(`DemoMain: crypt at 142.000 s, switched in 0.10 ms`), and `--gpu-probe-at`
now writes each counted frame in order with its switch time (`frames`).

- **The first version** deactivated the old scene's roots and activated the
  new one's. That cost 1.1 to 1.9 ms of CPU a switch: about 0.5 ms to hide
  and 0.9 ms to show some 2,500 renderers, plus 0.17 ms of globals. With the
  draw's noise, one switch frame came out 2.13 ms over its neighbours.
- **Now** each scene sits on its own layer (8 upwards), and the camera's
  culling mask picks the drawn one. A switch touches only the two scenes'
  realtime lights and post volumes and puts back the new one's globals, for
  0.10 to 0.13 ms.

[timing.md](timing.md) has three probe runs, each covering four switches.
Each switch is checked against the frames before and after it:

| run | switch frame against its neighbours' mean (ms) |
|---|---|
| 1 | -0.50, -0.65, -0.70, -0.72 |
| 2 | -1.13, -0.73, -0.71, +1.25 |
| 3 | -0.02, -0.21, +0.11, -0.12 |

The worst is +1.25 ms, against the 2 ms bar, and 10 of 12 switch frames were
faster than their neighbours, so what's left is noise.

## Warm-up and failure

- **Warm-up:** once every scene is set up, four frames each draw one
  environment off-screen at the first frame's time, with the horde, hero and
  effects live. That compiles its shaders and pipeline states. The music
  starts after. All four use the same linear-fog variants of the crowd and
  effect shaders, so the kit's materials are what each warm frame adds; the
  first, the forest's, took 45 ms to submit and the rest 3 to 5 ms.
- **Start-up:** a windowless start to the first frame takes 3.1 s, against
  3.0 s with one environment.
- **Failure:** `--resident crypt,atlantis` exits 1 before the first frame
  with `no atlantis arena scene for rung e2 at density 2`. A scene that fails
  its set-up, or hasn't loaded after 30 frames, stops the player the same
  way, naming the environment. `--switch-every` without `--resident` is
  refused.

## Normal runs are unchanged

Without `--resident`, the code path is the old one, refactored only to split
DemoMain's frame into its switch, its draw and its capture. The forest look
set from this build matches [look/final](../../look/final/README.md) within
0.11 L* at p50 in every still ([normal-vs-final.md](normal-vs-final.md)).

## Reproducing

From the worktree's root, after `uv run --project demo3d/tools demo3d build`:

```
for e in forest crypt desert snow; do
  uv run --project demo3d/tools demo3d look single-$e --trial --player-args "--environment $e"
  uv run --project demo3d/tools demo3d look res-$e --trial --player-args "--environment $e --resident crypt,desert,snow,forest --switch-every 0.25"
  uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py compare res-$e single-$e
done
uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py final single-forest
```

The timing runs add `--gpu-probe-at 141.96,143.96,145.96,147.96 --gpu-probe-out <json>`
to the switching run above (without the stills), then
`residency.py timing <json>...`.
