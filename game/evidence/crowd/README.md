# The crystal horde (ps-msz.3.8)

The bone-texture crowd driven by sim events, in the crystal style
([the design](../../../docs/design/unity-game.md), The Crowd). The code is
game/Assets/Game/Client/Crowd/: `CrowdView` (the presenter the frame loop subscribes),
`MonsterTrack` (a monster's events to its clips, place and facing), `BoneTextureCrowd` (culling,
palette and indirect draws), the shaders in Crowd/Shaders/ and the baker in
game/Assets/Game/Editor/Crowd/. The crystal surface is shared with the structures
(Client/Arena/Shaders/CrystalSurface.hlsl).

## How it was run

On the Mac (jarvis), the Mono player, fixed time (frame n is song time n/60), house-128 through
the plugin, in the baked arena with the look's camera, from the worktree's root:

```sh
python3 game/evidence/crowd/make_feed.py   # captures/game/crowd/horde-300.jsonl and row-5.jsonl
S="$PWD/public/reactive/house-128/house-128.ogg"; C="$PWD/captures/game/crowd"
uv run --project demo3d/tools game look horde --backend mono --readability --evidence crowd --at 3.317,6.133,9.1 --player-args "--file $S --events $C/horde-300.jsonl"
uv run --project demo3d/tools game look footplants --backend mono --evidence crowd --at 7.417 --frames 12 --strip 4 --crop 1000,200,720,240 --player-args "--file $S --events $C/row-5.jsonl"
```

[make_feed.py](make_feed.py) writes the synthetic feeds (seeded, so reruns match): 300 monsters in
same-type clumps covering all nine models, stepping toward the hero on house-128's own beats (the
beat entries of native/fixtures/entries/house-128-20s.jsonl), lunging when close, struck and
knocked back from 4 s and a dozen dying at 8 s; and five models in a row stepping 0.6 m east on
every beat. The feeds stay in the gitignored captures/.

## Results

- **300 animated crystal monsters** draw from the feed: [still-003.317](still-003.317.jpg),
  [still-006.133](still-006.133.jpg) and [still-009.100](still-009.100.jpg). Each role has its
  cast entry's model, scale and gem colour, so the horde reads as masses of colour; bats hover,
  the XL roles are twice the size, and the struck monsters nearest the hero flash white.
- **Readability passes** on every still ([readability.json](readability.json), the look tools'
  measure on the ID render: monster pixels against a 3 to 10 px ring of ground around them).
  dL* is +52.2, +53.8 and +51.4 (monsters at L* 59 to 64 over a ground ring at 7 to 10), against
  the floor of 17. The ID render draws the crowd alone, so a monster behind a tree still counts
  (none of these is).
- **Footplants land on the beat frame.** [footplants.jpg](footplants.jpg) crops the row across
  house-128's beat at 7.53 s, whose move window starts at 7.436 s: at 7.433 every monster stands
  where the last step left it, frames 7.450 to 7.517 carry the step (the walk's half cycle and the
  0.6 m move), and 7.533, the first frame after the beat, shows every foot planted at the step's
  end, held unchanged through 7.617. That frame also carries the look's accent flash and
  aberration on the same beat.
- The baker's log (`game unity BakeCrowd`): 9 models in about 6 s; LOD0 from 1,428 triangles (the
  slime) to 12,457 (the goblin); bone textures 124 KiB to 1.2 MiB at 30 Hz.
- The EditMode tests (`CrowdTests`) check the mapping on a made-up clip table: a step shows nothing
  before its window, is a quarter cycle in at its middle and half a cycle in, at `to`, on its
  footplant, holds, eases back to idle, and the next step is the other foot; a lunge's clip is at
  its wind-up's start on the notice and at its annotated contact on the impact, reaching 0.4 m
  toward the hero, and is back in idle and in place after its return; a hit flinches from its
  onset and flashes for 0.12 s while a knock carries it back; a step cuts in over a flinch keeping
  the flash; a death ends the body on its frame.

## Decisions

- **Positions come from the events**, not from a copy of the sim's enemy array in the state
  history: the step and knock events carry the same linear moves as EnemyState.PositionAt, and a
  scripted feed has no sim state to copy. The events arrive ahead of frameSong by the lead, so a
  monster keeps its next few and shows each from its start, never earlier.
- **A step is the walk's next half cycle** squeezed into the move window (about 5x the clip's speed
  at the slice's tempos), feet alternating, then a 0.12 s hold at the footplant and a 0.2 s ease
  back into idle. The walk's annotations have no footfall times, so a footfall is taken at the
  cycle's start and middle.
- **A lunge retimes the attack clip** so its annotated contact lands on the impact, sped up to at
  most 4x; its return plays at the same speed. **A hit** plays the hit clip from its onset at 1.5x
  for at most 0.45 s.
- **The cast is baked into the library** (CrowdLibrary.Cast) rather than read at run time: the
  player has no content loader yet, and the library already names the models.
- **One submesh per LOD**: the crystal samples no albedo, so the baker joins a model's submeshes
  and draws each LOD with one material; demo3d's welded meshes, stock baseline and per-submesh
  materials are not copied. The palette and instance buffers keep no previous frame (no motion
  vectors: the game keeps MSAA, not TAA).
- **The ID render** (`--capture-mask`, `game look --readability`) draws the crowd alone on its own
  layer, so it ignores occlusion by the arena; the crowd queues its draws again for it, as a
  hand-drawn camera spends a frame's queued draws.
