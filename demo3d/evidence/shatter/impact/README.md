# Crystal shatter at the moment of impact

The crystal shatter reworked after the operator's verdict on the delayed version: the break must
come at the moment of impact, the cracks-then-shatter build-up goes, and the twinkling particle
burst was "a mess of random lights blinking". The design is in
[crowd.md](../../../../docs/tech-demo-3d/crowd.md#monster-styles) (the shatter),
[effects.md](../../../../docs/tech-demo-3d/effects.md#the-look) (the burst) and
[camera-and-sound.md](../../../../docs/tech-demo-3d/camera-and-sound.md#sound) (the sound).

All strips were rendered on 2026-10-30 by the Mac's batch-mode IL2CPP player in fixed time, at
the showcase preset (`--monster-style crystal --envelope 24 --view-width 12`), **with effects on**.

| file | what | command (`demo3d filmstrip ...`) |
|---|---|---|
| [lone.jpg](lone.jpg) | a lone fodder skeleton broken at 37.95 s by a blow from the hero on its right, from the pose it was in; every 3rd frame, 1 s | `lone --at 37.88 --frames 30 --every 3 --columns 10 --scale 0.6 --crop 100,0,700,450 --player-args "<preset>" --evidence shatter/impact` |
| [lone-vs-before.jpg](lone-vs-before.jpg) | the same frames, the build of master before this change (34d0ebdf) above: there the skeleton plays its death clip, lies through the hold while the cracks spread and only breaks on the beat at about 38.8 s, with the twinkling glints; here it breaks on the blow | the same with `--baseline before` (the before strip: `--app <saved master build>`) |
| [elite.jpg](elite.jpg) | a big orc (elite) broken at 183.5 s, every 2nd frame | `elite --at 183.45 --frames 24 --every 2 --columns 6 --scale 0.7 --crop 100,50,800,560 --player-args "<preset>" --evidence shatter/impact` |
| [crowd.jpg](crowd.jpg) | the hero's slam at 195.9 s breaking ten monsters at once (skeletons, goblins, mushrooms and others), every 2nd frame | `crowd --at 195.85 --frames 24 --every 2 --columns 4 --scale 0.6 --crop 240,60,1500,900 --player-args "<preset>" --evidence shatter/impact` |
| [burst.jpg](burst.jpg) | the lone skeleton again with `--only-effects crystal-burst` (no hero effects), to judge the spray on its own | `lone2fx --at 37.93 --frames 24 --every 2 --columns 6 --scale 0.7 --crop 100,0,700,450 --player-args "<preset> --only-effects crystal-burst" --evidence shatter/impact` |

`--shatter-log` lists each break's time and place in the view.

## Method

- **No death clip.** A crystal death plays no clip (`TimelineEvaluator.BreaksAtImpact`), so up to
  the blow the monster keeps walking, attacking or flinching exactly as it would have. On the
  blow's frame it is `Broken`: the crowd hides it and `CrystalShatter.Breaking` takes over.
- **Posing the chunks at the live pose.** The bake no longer poses the death clip's last frame. It
  cuts each type's style mesh on its **rest pose** and keeps every corner's bones and weights
  (an apex gets its patch's blended weights). At the break the shatter skins every chunk corner on
  the CPU (`CrowdPose`) from the same bone-texture rows, crossfade and placement as the crowd's
  palette and `SkinVertex`, then makes each chunk rigid in the frame of the bone that carries most
  of it. Chosen over skinning in the shard shader because the colliders and the kick need the
  posed chunks on the CPU anyway, it runs once per death, and the shards stay plain MeshRenderers
  whose shadow and motion-vector passes need nothing new. `TheShatterPosesItsChunksWhereTheCrowdDrawsTheMonster`
  checks the CPU's bones against the GPU's palette (worst row error under 1e-5) and every chunk
  corner against the drawn mesh, for a walk, an attack crossfading out of a walk, and a flinch.
  Colliders are boxes fitted to each posed chunk in that frame, at 85%.
- **Removed:** the seams, hairline cracks, swell, tremble, the release pop and its 4-frame freeze,
  the hold shadow, the baked convex hulls, the bake's seam marks (UV2) and the shader's
  `_ShardOpen`, `_Crack` and `_CrackHeat`. The spark converter's `twinkle` option went with the
  glints.

## Tuning, and why

- **Kick** for a standing body breaking mid-motion: outward from the hit point 2.8 m/s, along the
  blow 5.0, up 1.6 (for a 1.8 m monster), against 2.4 / 3.0 / 3.0 for the old lying body. A
  standing body starts its shards up to 1.8 m high, so they need little lift, and the blow should
  read as the thing that broke it: the shards are thrown away from the hero. The variance stays:
  a quarter slump (the legs fold where they stood), one in eight flies far (1.6 to 2.4 times the
  kick), large chunks about half the smallest's share, small ones spinning fastest. The hit point
  (`CrystalShatter.HitPoint`) is 0.6 of the height up and 0.12 of it toward the hero. Weight,
  bounce, the 5 s linger and the sink are unchanged.
- **Flash.** The shards carry the crowd's white hit flash for 0.12 s, so the break frame pops
  without a pause.
- **The burst** is two spark layers thrown forward along the blow (the spawn's yaw) from the hit
  point, through and past the body with the shards: 34 bright chips (4 to 10 m/s, 7 cm, HDR 8,
  slightly stretched, cone 38 degrees, pitched 18 up) and 80 dust grains (1.5 to 6 m/s, 3 cm, HDR
  2.2, a 60 degree cone), slowed by drag (limit 3 and 1 m/s), shrinking and fading out linearly
  from half their life, gone by 0.4 to 0.7 s. Nothing blinks. The first pass (26 chips at 5 cm,
  life 0.3 to 0.55 s, fading from a third of it) was gone within 0.15 s and never got past the
  body, so it read as a puff on the front of the monster rather than a spray: longer lives, less
  drag and bigger chips carry it through.
- **Light**: the gem-coloured flash dropped from intensity 3.5 at 4.5 m to 2.2 at 4 m, 0.25 s,
  0.4 m above the burst (which now sits at chest height, not on a body lying on the floor). At 3.5
  the first frames washed the ground pale around a lone death. One blow's breaks still share the
  flash, 1/sqrt(n) each.

## Checks

- `demo3d test`: 303 of 306 pass, 3 skipped (the windowed LOD preview).
- `--fixed-time` with `--no-effects`: 30 of 30 frames of the lone break match pixel for pixel
  between two runs (`demo3d filmstrip ... --check`).
- The look set without the flag (`demo3d look --baseline final --trial`): every still's L*
  p2/p50/p98 equal to [look/final](../../look/final/look.md)'s.
- The mix (`demo3d mix`, offline): the whole song at its own density under crystal peaks at 1.78
  before the limiter against 1.71 with the timeline's sounds only (3.2% against 2.9% of samples over
  the knee, out peak 0.95); the showcase preset's song 1.46; the crowded slam at 195.85 s 1.06. The
  death cry and the glass now land together without clipping, so the levels stand.
- The full song windowless under `--monster-style crystal`: no errors, 2,409 deaths shattered, at
  most 14,194 shards live; the cost is in crowd.md (the mass kills at the climax and the ending
  spike the CPU, as posing tens of sets in one frame skins hundreds of thousands of corners).
