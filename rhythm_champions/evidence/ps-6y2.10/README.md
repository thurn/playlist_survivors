# ps-6y2.10: the demon's and the goblin's edge cuts

Measured on jarvis (M5 Max), 2026-11-02, with other beads' sessions building and testing on the same machine
(load average 5-6).

## The cuts

The recipes are [tools/recipes/edgecut/demon.py](../../tools/recipes/edgecut/demon.py) and
[goblin.py](../../tools/recipes/edgecut/goblin.py); `champions edgecut review` wrote their sheets and numbers in
[edgecuts/demon/](../edgecuts/demon/README.md) and [edgecuts/goblin/](../edgecuts/goblin/README.md).

| Model | Black share at game scale (>= 65%) | Line segments (<= 120) | Body triangles (300-800) | Shard pieces |
| --- | --- | --- | --- | --- |
| demon (1.9 m) | 79.1% (idle 78%, walk 79%, attack 80%) | 44 | 308 | 22 |
| goblin (1.2 m) | 77.9% (idle 79%, walk 78%, attack 76%) | 32 | 314 | 22 |

- **Demon**: its name is its horns and claws. The two tall pairs of horns on the crown are strokes along each
  horn's spine (the small face horns go); the fingers have no body, only a stroke from the palm to each of the three
  long claws (inside the hand's hull they were hidden). Feature lines go to the long arms; the spiky greaves, which
  took 25 of the first 60 lines, are capped.
- **Goblin**: the shield is an empty circle over a dark disc, the sword two strokes (pommel to tip, the guard), and
  the big ears a stroke each from the head to the point. Feature lines are only the helmet's, the chest's, the
  pauldrons' and the forearms' (14 segments): each costs about 0.6 points of black share at the goblin's real size.
- The review draws every model at the same height on screen (113 px at game scale), so a 1.2 m goblin is judged
  half again larger than it stands in the game. The slime-and-bat bead (ps-6y2.9) is changing the review to stand
  every model at the same camera distance; under that review, applied locally, the goblin is 69.6% at game scale
  (idle 72%, walk 70%, attack 67%, 54-69 px tall) and the demon 80.2%, both passing. The goblin's line budget was
  set for that stricter case.
- Clips and annotations are game/'s roster's (frozen, by review): the demon's idle, walk (stride 1.477 m), attack1
  (impact 0.35 s) and hit reaction from Demons_LP.fbx; the goblin's Idle, Walk, Attack1 (impact 1.017 s) and
  GotHit from their own files, the walk's 1.015 m stride at the model's native 1.5 m scaled to 0.812 m at 1.2 m.

## In the edge crowd

`just champions-play --headless --edge-crowd 1000 --edge-type <model> --fixed-time --seconds 30 --bench-out ...` on
the Mono player (release): 1,000 alive in 1,058 slots, walking with their clips, 20 a second breaking into shards,
300 breaks per run. The difference is the crowd's cost with the CPU's submission included (Metal has no GPU
recorder; ps-6y2.8's README explains the method).

| Model | Frame with the crowd p50 / p99 | Empty frame p50 / p99 | Difference p50 | CPU submission p50 (crowd, empty) |
| --- | --- | --- | --- | --- |
| [demon](bench-demon.json) | 1.71 / 5.44 ms | 0.84 / 4.21 ms | 0.87 ms | 0.58, 0.28 ms |
| [goblin](bench-goblin.json) | 0.89 / 2.12 ms | 0.60 / 1.66 ms | 0.30 ms | 0.49, 0.25 ms |

Within the skeleton's 0.4-0.9 ms (ps-6y2.8); the p99s are the shared machine's noise.

Stills (40 alive close up, shrunk to 960 px): [shards-demon-3.05.jpg](shards-demon-3.05.jpg) and
[shards-goblin-3.30.jpg](shards-goblin-3.30.jpg): a kill flashing white on its break frame, its pieces (lines and
dark chunks) thrown away from the blow.
