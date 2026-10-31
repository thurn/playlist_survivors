# The crowd's weapon grips (ps-b5z.2)

The operator's note on the trailers: "A bunch of the enemy weapons seem
attached to the hands wrong; they're all rotated in crazy ways." The two
weapons the roster attaches, the big orc's Slayer and the troll's club,
rode the hand bone as Synty made them, their blades out of the palm, so the
elites carried them sideways and swung them behind their backs. Each now
has a `grip` in roster.json that puts its handle through the fist
([crowd.md](../../../docs/tech-demo-3d/crowd.md#common-requirements),
Weapons), baked into the crowd's mesh.

## Close-ups

`demo3d unity GripStills` renders each armed type as the crowd baker spawns
it, at its idle, two walk phases, its attack's wind-up and impact and its
second attack's impact (the annotated impacts), from the front (top row)
and its right side (bottom row). Windowless, in the warm editor
(2026-10-31).

- `before-bigork.jpg`, `before-troll.jpg`: the weapons on the bare hand
  bone. The Slayer sticks out sideways at the idle and the walk and hides
  behind the back at the thrust; the club is held level out of the side and
  across the back of the head.
- `after-bigork.jpg`, `after-troll.jpg`: with their grips (`palm` 0.02,
  `yaw` 90, `pitch` 20). The handle sits in the fist and the weapon leaves
  it on the thumb's side, carried forward and down at the idle and walk,
  raised over the head at the wind-up, the Slayer driven down and forward
  at the impact and up and forward at the thrust, the club swept across
  the body.
- `after-skeleton.jpg`, `after-goblin.jpg`: the default roster's other
  armed types, whose swords and shields come in their prefabs, placed by
  the pack: unchanged, and held.
- `forest-skeleton.jpg`, `forest-goblin.jpg` (the Wererat's dagger, the
  Treasure Goblin's daggers) and `heroic-skeleton.jpg`, `heroic-goblin.jpg`,
  `heroic-mushroom.jpg`, `heroic-bigork.jpg` (the Skeleton Knight's and
  Goblin's swords and shields, the Kobold's axe and shield, the Fat Ogre's
  mace): every armed type of the roster sets, all prefab weapons, unchanged
  and held. No other slot of the three rosters carries a weapon.

## In the player

Windowless stills from the Mac's IL2CPP player in fixed time, the
bone-texture crowd with the grips baked in (2026-10-31). Each trailer grid
is the middle of the frame at 0.8, 1.6, 2.4 and 3.2 trailer seconds: three
stills of the duel (two big orcs and a troll), then the first frame of the
next shot.

- `trailer-duel-before.jpg`: the default cut from the build before the
  fix. The troll's club lies along its body; the orcs' Slayers hang at
  their sides or point back.
- `trailer-duel-after.jpg`: the same frames. The club and the Slayers come
  forward out of the fists toward the hero.
- `crystal-duel-after.jpg`: the crystal cut's duel, under its crystal
  style, the same.
- `normal-176.jpg`: a normal run at 176 s (the final peak) at
  `--view-width 14`: orcs reach for the hero with their Slayers, and the
  log holds no errors over the 12 s.

```
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$P" -batchmode -force-metal --trailer --fixed-time --capture-stills "$PWD/captures/duel" --stills-at 0.8,1.6,2.4,3.2 --seconds 4
"$P" -batchmode -force-metal --fixed-time --view-width 14 --start-at 170 --capture-stills "$PWD/captures/normal" --stills-at 176 --seconds 12
```

## A note on editor stills

The editor's skinned meshes keep their first skinning of an editor frame,
so a still of several poses showed every body a pose behind its rigid
weapons and helmets, which then floated off the hands (TD2's and the sets'
clip annotator contact sheets have this). GripStills sets
`forceMatrixRecalculationPerRender`, so body and weapon match.

To reproduce, from a worktree's root:

```
uv run --project demo3d/tools demo3d unity GripStills -out "$PWD/captures/grips/default" -types bigork,troll,skeleton,goblin
uv run --project demo3d/tools demo3d unity GripStills -out "$PWD/captures/grips/heroic" -roster heroic -types skeleton,goblin,mushroom,bigork
```

(`-roster forest|heroic` needs the set's packs synced: `demo3d setup
--roster NAME`.) The before sheets are the same command with roster.json's
two `grip` entries removed.
