# Roster sets: forest (ps-f8a.2)

`--roster forest` (crowd.md, Roster sets) with the Dark and Forest Creatures
packs in all nine slots, drawn by the stock crowd with the packs' own
materials. The look set is `demo3d look rosters-forest --player-args
"--roster forest" --baseline final`: [look.md](look.md), and
[contact-sheet.jpg](contact-sheet.jpg) puts the default's final set on the
left. The song stills use the set's level (18 m at the widest); TD15's beauty
shots are fixed cameras over the arena-wide horde, so there the forest
monsters are much smaller than they are in the song.

## The slots

| slot | creature | colourway | clips (walk, attack, attack2, hit, death, idle) | height |
|---|---|---|---|---|
| skeleton | Wererat Soldier | green | Walk Forward In Place, Attack01, Attack02, TakeDamage, Die, Idle | 1.8 m |
| goblin | Treasure Goblin | green (teal skin) | Walk Forward In Place, SliceAttack, StabAttack, TakeDamage, Die, Idle | 1.4 m |
| slime | Spiderling Venom | blue | Walk Forward In Place, BiteAttack, ClawAttack, TakeDamage, Die, Idle | 0.8 m |
| bat | Forest Bat | red (purple wings) | flyforward, attackbite, attacksoundwave, takedamage, die, idle | 0.5 m, hovering 1.2 m |
| mushroom | Evil Mushroom | red | Walk Forward Without Root Motion, Punch, BreathAttack, TakeDamage, Die, Idle | 1.3 m |
| demon | Demon Wolf | yellow (orange) | **Run Forward In Place**, BiteAttack, ClawAttack, TakeDamage, Die, Idle | 1.2 m |
| bigork (elite) | Dark Treant | purple | Walk Forward In Place, HeadAttack, ProjectileAttack, TakeDamage, Die, Idle | 3.0 m |
| troll (elite) | Forest Golem | blue | Walk Forward WO Root, Punch, DoublePunch, TakeDamage, Die, Idle | 2.9 m |
| dragon (boss) | Fantasy Dragon | red | Walk Forward In Place and the Ground clips: Bite, Projectile, Take Damage, Die, Idle | 4.5 m |

- **In place, never W Root:** every clip is the pack's in-place (or "WO
  Root") variant. The Demon Wolf has no walk, so its walk role is its
  in-place run, retimed to the slot's pace by its stride. The dragon fights
  on the ground (its flying clips would need a hover, and its ground walk
  matches the boss's march).
- **Colourways, not dye:** each slot uses one of the pack's own colourways,
  picked so the types that share a section are told apart by the pack's
  colours: teal goblins, beige wererats, dark blue spiders, purple-winged
  bats, red mushrooms, orange wolves, a magenta-cored treant, a cyan-veined
  golem and a red dragon.
- **Materials:** the packs' Standard materials (gloss and metal 0 in every
  one) become URP Lit copies under `Assets/Baked/Rosters/forest/Materials/`
  with their albedo and glow maps; the vendor .mat files are untouched.
  URP keeps `_EMISSION` on save only for a material flagged emissive, so the
  set's bake now flags them (the foundation's mushroom had lost its glow
  that way). Spiderling Venom has no glow map in its pack. The particle
  children (the Wererat's aura and saliva, the goblin's glitter, the bat's
  tail and the treant's aura) get URP Particles/Unlit copies.
- **Sizes:** heights fit the slot's choreography radius, which a set doesn't
  change: the wolf is long and the spider wide for theirs, so they overlap a
  little in a crowd. The bat was first 0.7 m and read as a field of dark
  balls; at 0.5 m it reads as a bat. Shadows: every creature casts one on the
  ground in the stills, including the hovering bats.
- **Clip annotations:** `demo3d unity AnnotateClips -roster forest` measured
  all 54 clips into `clip-annotations.json`'s `rosters.forest`. The forest
  rigs' fastest "end bone" was often a tail or a helper (`RigHeadGizmo`), so
  the roster names the striking bones per attack (`impactBone`: the wolf's
  head and front paws, the golem's and mushroom's palms, the spider's front
  legs, the dragon's head and jaw).

## Frame rate on the Mac

Agents stay windowless, and a batch-mode player draws no frames (drawing
every frame by hand leaks URP's pooled targets, CaptureTool), so the
measurement has two windowless halves. A windowed run with `--frame-probe` is
the operator's (ps-f8a.4's row).

- **CPU, the whole song:** `--roster forest --stats` in real time. Frames
  stay under 2.6 ms (simulation, evaluation and crowd updates, no drawing)
  to the ending at 198.7 s. The final peak holds 127 alive on screen
  against a target of 158.
- **Drawn frames, at the busiest times:** the new `--gpu-probe-at`
  (BatchGpuProbe.cs) hand-draws three timed frames after a warm-up at each
  time, into a 1080p MSAA 4x target, and times each through GPU completion:
  CPU submission and GPU in series, an upper bound on a pipelined frame.
  The numbers are noisy (three frames each, the GPU's clocks unpinned).

| envelope | 30 s | 60 s | 100 s | 145 s | 160-190 s (peak) |
|---|---|---|---|---|---|
| 150 (kept) | 5.8 ms | 11.8 ms (max 14.9) | 4.3 ms | 5.2 ms (max 12.0) | 4.1-4.8 ms, 103-119 alive |
| 250 | | 4.7 ms | | 6.4 ms | 8.4-13.0 ms, 143-158 alive |
| 350 | | 6.2 ms | | 7.3 ms | 4.6-9.6 ms (max 13.0), 123-185 alive |

([gpu-probe-150.json](gpu-probe-150.json), [250](gpu-probe-250.json),
[350](gpu-probe-350.json).) The envelope stays at 150: about 3x under the
16.7 ms budget through the peak, where 250 and up reach 13 ms before a window
presents anything. The 175 s still at 150 is already packed at 18 m.

## The default is unchanged

`demo3d look ps-f8a2-default --baseline final --trial`: all 122 L*
statistics match demo3d/evidence/look/final/, the largest difference 0.1
(the opening's effects p98).

## Commands

After `demo3d setup --roster forest`:

```
uv run --project demo3d/tools demo3d unity AnnotateClips -roster forest -evidence ../captures/annotate-forest
uv run --project demo3d/tools demo3d unity BakeRoster -roster forest
uv run --project demo3d/tools demo3d unity BakeAll
uv run --project demo3d/tools demo3d build
uv run --project demo3d/tools demo3d look rosters-forest --player-args "--roster forest" --baseline final
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal --roster forest --fixed-time \
  --gpu-probe-at 30,60,100,145,160,170,175,180,190 --gpu-probe-out captures/gpu-forest.json --seconds 192
```

## For the operator

- Watch `--roster forest` windowed with `--frame-probe` to confirm 60 fps
  with presenting, and judge the motion: the wolf's run used as a walk, and
  the Wererat's sword, which the annotator's contact sheet showed leaving
  its hand during TakeDamage (the pack's clip moves its weapon bone).
