# GPU monster shatter (ps-msz.3.9)

Deaths break into the crystal's own chunks on the impact frame, entirely on the GPU
([the design](../../../docs/design/unity-game.md), Shatter). The code is
game/Assets/Game/Client/Crowd/: `MonsterShatter` (the seed, the integrator and the draws), its
shaders `MonsterShards.compute` and `MonsterShard.shader`, the bench `ShatterBench`, and the cut,
`CrowdShardBaker`, in game/Assets/Game/Editor/Crowd/ (part of `game unity BakeCrowd`).

## How it was run

On the Mac (jarvis, Apple M5 Max), the Mono player, fixed time (frame n is song time n/60),
house-128 through the plugin, in the baked arena with the look's camera, from the worktree's root:

```sh
python3 game/evidence/shatter/make_feed.py   # captures/game/shatter/single.jsonl and mass.jsonl
S="$PWD/public/reactive/house-128/house-128.ogg"; C="$PWD/captures/game/shatter"; L="uv run --project demo3d/tools game look"
$L single --backend mono --evidence shatter --at 6.083333 --frames 12 --strip 4 --crop 480,40,768,432 --player-args "--file $S --events $C/single.jsonl"
$L heavy --backend mono --evidence shatter --at 7.966667 --frames 12 --strip 4 --crop 760,0,864,486 --player-args "--file $S --events $C/single.jsonl"
$L single-life --backend mono --evidence shatter/single --at 6.5,7.0,7.5,7.9 --player-args "--file $S --events $C/single.jsonl"
$L mass --backend mono --evidence shatter --at 7.983333 --frames 12 --strip 4 --crop 520,140,880,495 --player-args "--file $S --events $C/mass.jsonl"
$L mass-life --backend mono --evidence shatter/mass --at 7.95,8.25,8.6,9.2,9.9 --player-args "--file $S --events $C/mass.jsonl"
"game/Builds/mac/mono/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -force-metal \
  -logFile "$PWD/captures/game/shatter/bench/player.log" --fixed-time --shatter-bench "$PWD/game/evidence/shatter/bench.json"
```

[make_feed.py](make_feed.py) writes the feeds (seeded, so reruns match): four monsters 4 m north
of the hero, stepping on every beat, each dying alone in a different pose (the troll at its lunge's
contact at 6.12 s, the skeleton on a footplant at 7.06 s, the 2x troll of xlmummy to a heavy blow
at 8.0 s, the demon mid-flinch at 8.93 s); and 300 monsters of every model closing in, the 50
nearest dying together to a heavy blow on the beat at 8.0 s. Give `--at` exact frame times
(6.083333, not 6.0833): two still times inside one frame draw it twice, and the second draw has
no queued crowd (the stills' existing behaviour).

## Results

- **A single death breaks on its impact frame from the body's exact pose**
  ([single.jpg](single.jpg)). The troll dies at 6.12 s, at the contact of its lunge: 6.117 is the
  last frame with its body, and 6.133, the first frame at or after the death, shows the body gone
  and its 36 chunks in exactly that lunge pose, flashing white; from 6.150 they are visibly apart,
  and by 6.267 they are tumbling outward and down with their gem colour back. Every other monster
  keeps stepping.
- **A heavy blow throws harder** ([heavy.jpg](heavy.jpg)): xlmummy's chunks, on 8.000 in its pose,
  are scattered across a body's width within eight frames.
- **The lifetime** ([single/](single/)): the chunks land, bounce and slide into a scattered pile
  (6.5, 7.0), the next deaths join them (7.5), and each set sinks under the floor from 1.2 s after
  its death and is gone by 1.8 s (7.9: the troll's set, dead at 6.12, has sunk).
- **Fifty deaths at once** ([mass.jpg](mass.jpg), [mass/](mass/)): on 8.000 the 50 monsters
  nearest the hero turn to white chunks in their poses, all on the same frame, and over the next
  ten frames they fly out over the surviving horde, clearing a disc round the hero; by 9.9 s their
  piles have sunk.
- **The bench** ([bench.json](bench.json)): each part's wall time with the GPU drained before and
  after it, 30 measured trials per case (one seed each) and about 3,600 frames of integration and
  drawing, 1080p with MSAA 4x. The 10th percentiles are the least disturbed by other work (the
  machine ran other agents' Unity builds, load 16):

  | Deaths at once | Shards | Seed frame (ms) | Integration frame (ms) | Shard draws (ms) | The same bodies alive (ms) |
  |---|---|---|---|---|---|
  | 0 | 0 | | 0.195 | | |
  | 1 | 26 | 0.342 | 0.243 | 0.10 | 0.46 |
  | 50 | 1,547 | 0.631 | 0.272 | 1.11 | 1.80 |
  | 300 | 9,300 | 0.550 | 0.237 | 2.01 | 2.72 |

  The seed and integration frames include the drain itself (the 0-death frame's 0.195 ms).
  **Fifty deaths seed in what three hundred do**: the cost is per model, not per death (one upload
  and two dispatches for each model that has deaths that frame: one model for the single death,
  all nine for 50 or 300), about 0.4 ms over the drain at most. Integrating every live shard adds
  under 0.08 ms. The shards' draws cost about what the same monsters' bodies did alive (they are
  the same triangles, skinned the same way, plus inside faces). Two reruns under heavier load
  agreed within about 0.3 ms (seed 0.27 to 0.34 / 0.57 to 0.63 / 0.55 to 0.56 ms; shard draws
  0.08 to 0.10 / 0.80 to 1.11 / 2.0 to 2.1 ms). The Mac's Metal has no GPU recorder, so the
  shatter's own GPU markers (MonsterShatter.SeedSampler and IntegrateSampler) read nothing here;
  a development player on Cygnus's D3D12 records them in the same JSON.
- The EditMode tests (`ShatterTests`, on the editor's GPU, with a made-up one-bone monster cut by
  the real baker): the cut gives 20 to 40 closed chunks covering every skin triangle once; every
  chunk's pivot starts exactly on the posed body (placement, yaw and scale) and moves from the
  next frame; shards stay above the ground while they linger, fly along the blow, sink under the
  floor and end at 1.8 s; a heavy blow throws farther; fifty deaths in one frame each break from
  their own body; a full ring gives up its oldest and a seek back clears; each break raises its
  burst at the hit point. `CrowdTests` check that a death records its blow and a recycled
  re-entry does not shatter.
- The bake (`game unity BakeCrowd`): 26 to 40 chunks per model, 1,850 (slime) to 14,176 (goblin)
  triangles with their inside faces, in about 6 s for all nine models with the rest of the crowd.

## Decisions

- **The seed reads the crowd's own palette** on the death's frame: CrowdView writes the dying
  monster once more, posed at the death's exact time, as `CrowdFlags.Dying`; the cull pass flags
  it for the palette pass wherever it is (on screen or not) and lists it for no draw. The shatter
  then freezes that palette into its ring slot, so the chunks are skinned by the very matrices the
  body would have been, and the swap can't show.
- **Shards are skinned at draw time** from the frozen palette (the chunk mesh keeps the style
  mesh's corners, bones and weights) and then moved rigidly: each chunk's state is its pivot's
  place and a rotation, 80 bytes, so 300 deaths need 0.7 MB of state and the frozen palettes,
  rather than posed vertices per death.
- **One instance per death**: a model's chunks are one mesh, each corner tagged with its chunk, so
  a model's live deaths are one indirect draw, and a death is a ring slot. Every death lives the
  same 1.8 s, so the ring ends them in order; 320 slots per model, and a full ring gives up its
  oldest.
- **The integrator** steps on the song clock at most 1/120 s at a time (24 steps a frame at most,
  so a hitch drops time rather than stalling): 2.2 g, tumble from a world-space spin, ground
  contact at half a chunk's size with 0.35 restitution, bounces under 0.6 m/s settling, 0.6 of
  ground speed and spin kept per bounce and a 4/s slide friction; no shard-to-shard collisions
  (the PhysX path had them; at 9,300 shards the piles read without them). The kick is demo3d's:
  outward from the hit point, along the blow, up, with wide per-chunk variance (a quarter slump,
  an eighth fly far), scaled by the root of the death's force over the strike's 1.85 m, so a heavy
  blow's shards fly about as much farther as the sim throws a body.
- **The burst hook** is `MonsterShatter.Broke` (reach it as `CrowdView.Shatter`), raised on the
  break frame with the death's time, hit point, blow, force, height, gem colour, role and chunk
  count. The effects bead (ps-msz.3.11) draws the chips and dust from it; sounds should schedule
  from the Death event itself, which arrives a lead ahead.
- **The bench drains the GPU around each part** rather than reading GPU timestamps, since Unity's
  GPU recorder is unsupported on the Mac's Metal; the drain adds a constant (the 0-death row), and
  the GPU's clock ramping and other work on the machine land in the upper percentiles.
