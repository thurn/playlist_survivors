# The crowd and monster shatter in the look package (ps-wnj.4)

The game's crowd renderer (`BoneTextureCrowd`, `MonsterInstance`, `CrowdSlots`,
`CrowdCulling`, `CrowdLibrary`), its shaders, the GPU monster shatter
(`MonsterShatter`, `MonsterShards.compute`, `MonsterShard.shader`), the crystal
surface includes and the beat globals moved from game/Assets/Game/Client/ into
`unity-packages/com.playlistsurvivors.look/` (namespace `PS.Look.Crowd`;
[neon-look.md](../../../../docs/design/neon-look.md), The Package). Bodies and
shards take their materials from a monster look (`IMonsterLook`); the package's
stand-in, `MonsterLooks.Crystal`, is the crystal as before, so nothing should
change on screen. The bakers' unweld step (`StyleMesh.Unweld`) and the shard
cutter (`CrowdShardBaker`) are the package's, called by both projects' crowd
bakers.

## How it was run

On jarvis (Apple silicon), IL2CPP Mac players: before from the base commit
(56bcfeaa) in a scratch worktree, after from this change, each `game build`
baking what it found stale (the after build rebaked the crowd, the arena and the
structure shards, since their inputs moved). The peak scenario, from the
worktree's root:

```sh
F="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
P="--file $F --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@18,regrow@24,finale@32"
uv run --project demo3d/tools game look after --at 10,18.2,25,32.5,33 --evidence look/crowd-move/after --player-args "$P"
```

## Stills match

[before/](before/) and [after/](after/): about 300 monsters, the structure
break at 18 s and the finale's mass kill at 32.4 s, its shards flying at 33 s.
Per-pixel RGB differences (0-255) of the full 1080p stills:

| Still | max | mean | pixels over 8 | pixels over 32 |
| --- | ---: | ---: | ---: | ---: |
| 10 s | 9 | 0.005 | 4 | 0 |
| 18.2 s (break) | 28 | 0.009 | 221 | 0 |
| 25 s | 12 | 0.005 | 15 | 0 |
| 32.5 s (finale) | 11 | 0.006 | 18 | 0 |
| 33 s (shards) | 15 | 0.010 | 82 | 0 |

The differences are compression and rebaked-lightmap noise, smaller than two
runs of one build differ by (ps-wnj.3 measured up to 228 on a still).

## Perf at the peak is unchanged

`game perf --only peak,break,finale`, then five more peak runs alternating the
two players (the machine was loaded by other sessions' Unity work and slow CI,
load average 19-25):

| Scenario | before: probe p99, main p99 / max (ms) | after: probe p99, main p99 / max (ms) |
| --- | --- | --- |
| peak | 10.4, 2.9 / 6.0 | 11.9, 8.2 / 30.5 |
| break | 12.2, 3.8 / 4.2 | 8.3, 2.2 / 2.6 |
| finale | 12.6, 4.3 / 11.0 | 10.0, 2.7 / 6.5 |
| peak, alternating | 8.0, 1.8 / 3.2; 7.3, 1.9 / 3.8; 15.8, 2.6 / 14.1; 12.7, 2.6 / 14.7 | 10.6, 2.2 / 8.2; 8.5, 2.9 / 11.8; 10.8, 3.0 / 21.1; 8.5, 2.0 / 3.9 |

No frame went over 33 ms in any run. The slow frames on either side spent about
1.6 ms of CPU in 20-30 ms of wall time (preempted), and in the after runs that
were slower every part was slower alike, the untouched sim, pickups and HUD
included; the crowd's own part stayed within 0.14-0.18 ms p50 on both.

## The beat audit passes, deaths break on their frame

`game beat peak` (game/evidence/beat/README.md's peak command) on both players:
2399 frames, 13983 moments, 0 off their frame, 652 deaths, the same counts
before and after.

## demo3d's cuts are unchanged

demo3d changes only in its crowd baker (the package's `StyleMesh.Unweld`, the
same algorithm) and in `StyleBeat` publishing the same globals through
`BeatGlobals`. IL2CPP demo3d players from the base commit and this change, each
`demo3d build` running every baker afresh, lightmaps included:

```sh
"Playlist Survivors 3D Demo" -batchmode -force-metal --trailer crystal --fixed-time \
  --capture-stills DIR --stills-at 2.0,9.0,16.0,24.0 --stills-format png
```

Per-pixel RGB max difference (pixels over 8):

| Still | crystal: two base runs | crystal: two after runs | crystal: after vs base | action: after vs base |
| --- | --- | --- | --- | --- |
| 2 s | 2 (0) | 2 (0) | 2-3 (0) | 4 (0) |
| 9 s | 3 (0) | 3 (0) | 3 (0) | 3 (0) |
| 16 s | 233 (5702) | 244 (3026) | 233-244 (3020-6377) | 4 (0) |
| 24 s | 255 (165531) | 255 (169436) | 255 (166954-169670) | 3 (0) |

The crystal cut's PhysX shatter (16 and 24 s) differs between two runs of one
build as much as between the builds. The default demo (no `--trailer`, the
baseline style) differs by up to 37 on about 2000 pixels at a soft static shadow
under vegetation (two base runs: 7 at most), where the two fresh lightmap bakes
differ; no monster is involved.

## Tests

The shatter's tests moved with it into the package (`PS.Look.Tests.ShatterTests`)
and run in both projects, with new `MonsterLookTests`. Game EditMode: 558/558.
demo3d EditMode: 471/477, the three CrowdReflection failures that predate this
change (ps-wnj.3's notes), three skips.
