# Trailer: seven environments resident in one player (ps-hgs.1, ps-7mx.2)

The player holds crypt, desert, snow, forest, lava, neon and blossom at once
and switches the drawn one within a frame ([trailer.md, Environments in one
player](../../../../docs/tech-demo-3d/trailer.md#environments-in-one-player)).
ps-hgs.1 made the first four resident; ps-7mx.2 added lava, neon and blossom
and re-measured all seven. Everything here is from the IL2CPP player on the
Mac (Apple M5 Max, Metal), windowless at fixed time, built at staging
`9d47842a` plus ps-7mx.2.

**Verdict:** the seven scenes overlap at the origin with no offset. Each
environment looks exactly as it does alone, the new ones' special materials
included, and a switch costs about 0.13 ms of CPU. The trailer now cuts
through all seven: shots 5, 6 and 7 moved to lava, blossom and neon.

| file | what |
|---|---|
| `switch-*.jpg` | stills at and after each switch, all seven (below) |
| `trailer-*.jpg` | the trailer's own frames in lava, blossom and neon (below) |
| [look-lava.md](look-lava.md), [look-neon.md](look-neon.md), [look-blossom.md](look-blossom.md), [look-forest.md](look-forest.md), [look-crypt.md](look-crypt.md), [look-desert.md](look-desert.md), [look-snow.md](look-snow.md) | each environment with all seven resident against alone, still by still |
| [normal-vs-final.md](normal-vs-final.md) | ps-hgs.1's normal run (no `--resident`) against [look/final](../../look/final/README.md) |
| [timing.md](timing.md) | frame times before, at and after each switch |
| [residency.py](residency.py) | the comparisons, the timing table and the JPEGs |

## The debug entry point

Two development flags exercise switching without the shot list:

- `--resident crypt,desert,snow,forest,lava,neon,blossom` loads those
  environments beside `--environment`'s. That one is active first, and the
  timeline plays its layout.
- `--switch-every <s>` cycles the drawn environment through them every s song
  seconds from `--start-at`.

The switch stills come from one run:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile $PWD/captures/residency/switch/player.log --fixed-time \
  --resident crypt,desert,snow,forest,lava,neon,blossom --switch-every 2 --start-at 140 \
  --capture-stills $PWD/captures/residency/switch --stills-format png \
  --stills-at 141.983,142,142.5,144,144.5,146,146.5,148,148.5,150,150.5,152,152.5,154,154.5 --seconds 15
```

(The player resolves relative paths elsewhere, so pass absolute ones.)

| still | environment | |
|---|---|---|
| switch-141.983 | forest | the frame before the first switch |
| switch-142.000 / 142.500 | crypt | the switch frame, and half a second on |
| switch-144.000 / 144.500 | desert | at and after |
| switch-146.000 / 146.500 | snow | at and after |
| switch-148.000 / 148.500 | lava | at and after |
| switch-150.000 / 150.500 | neon | at and after |
| switch-152.000 / 152.500 | blossom | at and after |
| switch-154.000 / 154.500 | forest | back to the first |

Every switch frame shows only the new environment: its own ground, kit,
moon, fog, grade and vignette, with nothing of the old one. Lava's glowing
cracks, neon's holograms and dark wet street, and blossom's pale stone are
all there on the switch frame itself. The horde and the hero walk the
forest's layout in every environment, since the timeline is the forest's.

The trailer's own frames (`--trailer`, its full look on) are
`trailer-018.000` and `019.500` (shot 5, lava: its first frame and mid-shot),
`021.080` and `021.800` (shot 6, blossom), and `022.630` and `023.400`
(shot 7, neon).

## Arrangement: overlap at the origin

The test is the look requirement: with all seven resident, each environment
must match a normal single-environment run of it within 1 L*.

For each environment, two look sets were rendered (`demo3d look SLUG --trial`):

- one alone: `--player-args "--environment E"`;
- one resident: `--player-args "--environment E --resident crypt,desert,snow,forest,lava,neon,blossom --switch-every 0.14285"`.

Seven slots of 0.14285 s make a cycle a hair under 1 s, so every song still
(5 s after its start) and beauty shot (3 s after) is taken on the very frame
that switches back to E, after the player has drawn every other environment
several times. That is the hardest case: no frame for state to settle. The
player logs show it (`DemoMain: lava at 25.000 s, switched in 0.11 ms`, on
song-025's still frame). A slot of exactly 1/7 s does not work: the
fixed-time clock runs a hair under 25.000 s on that frame, so the switch fell
one frame later and the still showed blossom.

| environment | stills | largest median or mean difference | largest mean per-pixel abs dL* |
|---|---:|---:|---:|
| lava | 28 | 0.01 L* | 0.03 |
| neon | 28 | 0.00 L* | 0.02 |
| blossom | 28 | 0.01 L* | 0.11 |
| forest | 28 | 0.01 L* | 0.03 |
| crypt | 28 | 0.00 L* | 0.03 |
| desert | 28 | 0.01 L* | 0.15 |
| snow | 28 | 0.01 L* | 0.11 |

The 28 stills are each set's song stills, beauty shots, measure stills (no
effects) and empty shots. The pass line is 1 L*. The look sets have no
trailer look in them (no per-shot grade, grain, lens dirt or lens hits),
which is what `--no-trailer-look` leaves out of a trailer run.

The new environments' special materials hold up with the rest resident:

- **Lava:** the ground's emission map and the kit's baked lava glow.
  Its empty stills sit at L* 4 to 6, so any glow lost or light leaking
  in from another scene would show; none differs by more than 0.01.
- **Neon:** the kit's emission (signs, windows, holograms) and the wet
  street's smoothness.
- **Blossom:** the pale stone under a bright sky fill.

What makes the overlap work is unchanged from four environments:

- **Lightmaps** need nothing: every loaded scene's lightmaps join one list,
  and each renderer indexes its own.
- **Light probes** are swapped. Every scene bakes the same 882-probe grid,
  and after seven additive loads Unity still holds only 882 probes (each
  scene's `ready` log line says `882 light probes (882 loaded)`). A switch
  sets `LightmapSettings.lightProbes` to the new scene's own
  (`LightProbes.GetSharedLightProbesForScene`), checked by the EditMode test
  `ResidentArenasSwitchEveryGlobalTheirSetUpWrote`.

## Switch cost

Each switch is timed and logged (`DemoMain: lava at 148.000 s, switched in
0.13 ms`), and `--gpu-probe-at` writes each counted frame in order with its
switch time. Each scene sits on its own layer (8 to 14 for seven; the
project names none of 8 to 31, and `ArenaSet` refuses more than those 24),
and the camera's culling mask picks the drawn one. A switch touches only the
two scenes' realtime lights and post volumes and puts back the new one's
globals: 0.12 to 0.20 ms across all 42 measured switches.

[timing.md](timing.md) has six probe runs, each covering all seven switches,
each switch checked against the frames before and after it:

| run | switch frame against its neighbours' mean (ms), crypt, desert, snow, lava, neon, blossom, forest |
|---|---|
| 1 | -0.81, -0.73, -0.37, +1.50, +0.16, +0.73, -0.08 |
| 2 | -0.59, -0.71, -1.24, +0.11, +1.01, +1.14, +0.08 |
| 3 | +0.71, -0.70, -0.54, +0.96, -0.17, -0.31, +2.22 |
| 4 | -0.20, +1.04, +0.01, +0.36, +0.24, +0.49, -0.20 |
| 5 | -1.21, -0.04, +0.13, +1.45, +0.80, -0.44, +1.27 |
| 6 | +0.62, -0.34, -1.07, -0.23, +1.08, -1.42, +1.54 |

41 of 42 switch frames are within the 2 ms bar. The one over, run 3's
switch back to forest at +2.22 ms, is noise: its switch took 0.13 ms like
every other, the same switch came out -0.08, +0.08, -0.20, +1.27 and +1.54
in the other runs, and its neighbours drew in 4.3 ms against 7 ms in runs 1
and 2. The Mac's load average was 17 to 21 throughout, from other sessions'
builds, so draw times swing by 3 ms from run to run. 20 of 42 switch frames
were faster than their neighbours.

The trailer itself logs each cut (switch and shot reset, then the draw beside
the frame before's). In a windowless `--trailer` run every cut's switch and
reset took 0.11 to 0.13 ms, the first cuts into lava, blossom and neon
included:

```
DemoMain: cut to shot 5 (lava) at trailer 17.967 s: switch and reset 0.12 ms, draw 0.57 ms (the frame before: 0.39 ms)
DemoMain: cut to shot 6 (blossom) at trailer 21.067 s: switch and reset 0.12 ms, draw 0.70 ms (the frame before: 0.35 ms)
DemoMain: cut to shot 7 (neon) at trailer 22.617 s: switch and reset 0.11 ms, draw 0.80 ms (the frame before: 0.56 ms)
```

## Warm-up, failure and memory

- **Warm-up:** once every scene is set up, seven frames each draw one
  environment off-screen at the first frame's time, with the horde, hero and
  effects live, compiling its shaders and pipeline states before the music
  starts. In the trailer the first, crypt's, took 73 ms to submit and the
  other six 2 to 4 ms each, lava, blossom and neon included
  (`DemoMain: warmed lava off-screen (2 ms to submit)`). A first cut into an
  environment draws in well under a millisecond of CPU, as above, so nothing
  is left to compile mid-trailer.
- **Failure:** `--resident neon,atlantis` exits before the first frame with
  `no atlantis arena scene for rung e2 at density 2`. A scene that fails its
  set-up, or hasn't loaded after 30 frames, stops the player the same way,
  naming the environment.
- **Memory:** the player's physical footprint (Metal's buffers and textures
  count in it on Apple silicon), the largest of one-second samples over a
  windowless run:

  | run | environments | peak footprint |
  |---|---:|---:|
  | `--environment forest` (a normal run) | 1 | 1,438 MiB |
  | `--resident crypt,desert,snow,forest` | 4 | 2,277 MiB |
  | `--resident` all seven | 7 | 2,764 MiB |
  | `--trailer` (its ten shots' timelines too) | 7 | 2,850 MiB |

  The three new environments add about 490 MiB to four: each brings its
  scene, lightmap set and 64 MiB static shadow map.

## Normal runs are unchanged

ps-7mx.2 changes no normal-run code: it moves three shots to the new
environments in the trailer file and adds a layer-budget check to `ArenaSet`.
[normal-vs-final.md](normal-vs-final.md) is ps-hgs.1's check and still stands
for the residency code. (Today's forest look set no longer matches
look/final, by up to 21.5 L* at song-175, because of later changes to the
horde's look and choreography. That is not residency's concern.)

## Reproducing

From the worktree's root, after `uv run --project demo3d/tools demo3d build --backend il2cpp`:

```
ALL=crypt,desert,snow,forest,lava,neon,blossom
for e in lava neon blossom crypt desert snow forest; do
  uv run --project demo3d/tools demo3d look single-$e --trial --player-args "--environment $e"
  uv run --project demo3d/tools demo3d look res7-$e --trial --player-args "--environment $e --resident $ALL --switch-every 0.14285"
  uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py compare res7-$e single-$e
done
```

The timing runs add `--gpu-probe-at 141.96,143.96,145.96,147.96,149.96,151.96,153.96 --gpu-probe-out <json>`
to the switching run above (without the stills), then `residency.py timing <json>...`.
Run it from `sh` or bash: zsh passes an unquoted `$FLAGS` holding
`--resident a,b` as one argument.
