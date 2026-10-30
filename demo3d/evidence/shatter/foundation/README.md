# Crystal shatter: the foundation (ps-2iq.1)

Baked shards, the death swap, the shard shader and windowless filmstrips (epic ps-2iq). The design
is in [crowd.md](../../../../docs/tech-demo-3d/crowd.md) (Monster styles, The crystal shatter) and
the tool in [mac.md](../../../../docs/tech-demo-3d/mac.md#filmstrips). Every strip was rendered by
the Mac's batch-mode IL2CPP player in fixed time, 2026-10-30.

## The showcase preset

```
--monster-style crystal --envelope 24 --view-width 12 --no-effects
```

A 12 m view on a song capped at 24 monsters on screen, without the effect library, so a death
reads on its own. The motion and burst beads add the effects back (drop `--no-effects`). Deaths
in view at it, from `--shatter-log`:

| song time (hold) | type | where (1080p px) | notes |
|---|---|---|---|
| 41.233 (release 41.617) | skeleton | (916, 293) | a lone death, the strips below |
| 106.883 | goblin | (553, 325) | |
| 57.333 | goblin | (494, 719) | |
| 93.033 | slime | (450, 565) | |
| 113.267 (release 113.667) | bigork | (1035, 430) | at the song's own density and view (`--monster-style crystal` alone) |

## The strips

| file | what | command (`demo3d filmstrip ...`) |
|---|---|---|
| [skeleton.jpg](skeleton.jpg) | the lone skeleton from its hold to 1 s after the release, every 2nd frame | `skeleton --at 41.2 --frames 64 --every 2 --scale 0.5 --crop 600,40,640,540 --player-args "<preset>" --evidence shatter/foundation` |
| [before-after.jpg](before-after.jpg) | today's triangle shatter (each band's top row, the build before this bead) above the shards, the same frames | `vs-before --app <saved build> ...`, then `vs-after --at 41.4 --frames 32 --every 2 ... --baseline vs-before --evidence shatter/foundation` (renamed) |
| [swap.jpg](swap.jpg) | the swap at 1/60 s: before above after, from 2 frames before the hold (41.233) | `swap-before --app <saved build> --at 41.2 --frames 8 --scale 0.75 --crop 780,0,360,280`, then `swap-after ... --baseline swap-before` (renamed) |
| [goblin.jpg](goblin.jpg) | a goblin at the preset | `goblin --at 106.85 --frames 48 --every 2 --scale 0.5 --crop 250,60,640,540 --player-args "<preset>"` |
| [bigork.jpg](bigork.jpg) | an elite at the song's density | `bigork --at 113.2 --frames 48 --every 2 --scale 0.5 --crop 715,150,640,540 --player-args "--monster-style crystal"` |

The mp4s (15 fps, a quarter speed) are in the gitignored `captures/filmstrip/<slug>/`.

## Findings

- **The swap doesn't show.** Against the build before this bead, the 360x280 px around the dying
  skeleton differ in 0 px before the hold and in 3 to 9 px (by at most 39 of 255, single pixels
  on shared shard edges) in each of the six frames after it. Before the hold shadow
  (`CrowdShards.Set.HoldShadow`, the LOD2 shadow proxy at the pose) the shards cast their own LOD0
  shadow and about 2,600 px of shadow edge moved by up to 41 levels.
- **Deterministic.** `skeleton-repeat ... --check skeleton`: 64 of 64 frames match pixel for
  pixel. (Runs split into different `--chunk`s differ by up to about 20 levels for half a second
  after a chunk starts, in the old build too: see mac.md.)
- **The baseline is unchanged.** `demo3d look` without the flag against
  [look/final](../../look/final/look.md): every song still's and beauty shot's L* p2/p50/p98,
  RMS contrast and mean chroma within 0.11.
- **Motion vectors.** Under `--aa taa`, the shards' edge contrast (mean absolute L gradient over
  the falling shards, frames 8 to 22 after 41.5 s) was 2 to 3% higher with the motion-vector twin
  than without it in every frame.
- **The full song** at its own density with `--monster-style crystal`, windowless in fixed time:
  no errors, 2,336 deaths shattered (every type, the dragon once), at most 11,609 shards live, in
  39 s of wall time for 199 s of song (no frames drawn). At that density the floor fills with
  debris; the cost and the linger are the later beads'.
- **The kick.** The first values (1.6, 2.2 and 1.2 m/s outward, along the blow and up) sent a
  skeleton's shards 2.5 m in a quarter second, out of the frame toward the camera; they are now
  1.0, 1.3 and 1.6 m/s for a 1.8 m monster. The motion bead tunes them.
