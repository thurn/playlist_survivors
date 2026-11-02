# The crowd's weapon grips (ps-9rw)

The operator, playing `just game`: characters "holding their weapons wrong".
demo3d fixed this for its crowd in ps-b5z.2
([demo3d/evidence/grips](../../../../demo3d/evidence/grips/README.md)), but the
game's copy of the roster predated it: the two weapons roster.json attaches,
the big orc's Slayer and the troll's club, rode the hand bone as Synty made
them, blades out of the palm. So the elites carried them sideways and swung
them behind their backs. Each now has demo3d's `grip` (`palm` 0.02, `yaw` 90,
`pitch` 20), which `Roster.Spawn` applies through `Fist`, the hand's fist from
its finger bones. The crowd bake spawns through it, so the grip is baked into
the crowd's mesh. The hero's blade sits in the same `Fist`
([the design](../../../../docs/archive/design/unity-game.md), The crowd, Weapons).

## Close-ups

`game unity GripStills` renders each armed type as the crowd bake spawns it.
The poses are its idle, two walk phases, its attack's wind-up and impact (from
roster.json's annotated impact) and its hit. Each pose is shown from a little
right of its front (top row) and from its right side (bottom row). The body
draws grey and the rigid pieces (the roster's weapons) orange, so the line of
the weapon through the fist reads. The stills come from the warm editor,
windowless (2026-10-31).

- `before-bigork.jpg`, `before-troll.jpg`: the weapons on the bare hand bone.
  The Slayer lies level out of the side at the idle and walk and swings over
  the head and back at the attack. The club is held level across the body and
  behind the head.
- `after-bigork.jpg`, `after-troll.jpg`: with their grips. The handle sits in
  the fist and the weapon leaves it on the thumb's side. It is carried forward
  and down at the idle and walk and raised over the head at the wind-up. At
  the impact it is driven down and forward toward the target. These match
  demo3d's `after-bigork.jpg` and `after-troll.jpg`.
- `after-skeleton.jpg`, `after-goblin.jpg`: the game roster's other armed
  types. Their swords and shields come skinned into their packs' meshes
  (hence grey), are placed by the pack, and are unchanged and held. No other
  type in the game's roster carries a weapon.

`GripTests` (EditMode) checks the elites' grips. For each one, the weapon's
+Y lies across the fist (square to the palm, out on the thumb's side) and its
pivot sits within 6 cm of the knuckles.

To reproduce, from a worktree's root:

```
uv run --project demo3d/tools game unity GripStills -out "$PWD/captures/game/grips/after" -types bigork,troll,skeleton,goblin
```

To render the before sheets, run the same command with roster.json's two
`grip` entries removed.
