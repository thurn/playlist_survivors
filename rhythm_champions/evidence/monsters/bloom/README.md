# Monster: bloom

Written by `champions monster review bloom` and `champions monster review bloom --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws the vendor mesh; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, breath, smash, fireball, stomp, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 2600/1686/896 in the trailer look (the trailer's 2600/1686/896), 40 shards, 100 bones, standing 4.024 m.

## Notes

ps-8z4.10 (the Bloom's fight bead) registered the source so its fight could land before its mesh: the Red prefab and
all sixteen of its clips in manifest.json, the `bloom` type in monsters.json and its boss cast row in enemies.json
(the accent slot, violet in palette.json). Its clip roles are the boss animator's, so its fight's moves play them:
`breath` (BreathAttack: the spore arms), `smash` (Bite), `fireball` (SpellCast: the spore spiral), `stomp`
(BreathAttackSurround: the pulse), `walk` (Walk Forward WO Root: drawing its roots to its spot in the entrance) and
`hit`. Impacts are where its head snaps forward fastest, measured in Blender from the clips (0.6 s in each attack).
There is no recipe yet: both looks draw the vendor mesh until the mesh bead (ps-8z4.11) designs one, so only the
trailer look's sheets are here.
