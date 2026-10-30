# Roster sets: the foundation (ps-f8a.1)

The plumbing for `--roster forest` and `--roster heroic` (crowd.md, Roster
sets), proved with one creature per set: Evil Mushroom (red colourway) in
forest's mushroom slot and the heroic Goblin with sword and shield in
heroic's goblin slot. Every other slot is still today's type, now drawn by
the stock crowd with the Synty prefabs' own materials, which is why they look
flatter than the default's dyed bone-texture crowd. The set beads fill in the
rest.

Stills: fixed-time, windowless, Mac IL2CPP player, at 60 s (drop-1, goblins)
and 100 s (breakdown, mushrooms). Each set's camera is its level variant's
(18 m at the widest), the default's the level script's (30 m).

| still | default | forest | heroic |
|---|---|---|---|
| 60 s | default-060.jpg | forest-060.jpg | heroic-060.jpg |
| 100 s | default-100.jpg | forest-100.jpg | heroic-100.jpg |

Commands, after `demo3d setup --roster forest --roster heroic`,
`demo3d unity BakeAll`, `demo3d unity BakeRoster -roster forest` (and
heroic) and `demo3d build`:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile "$PWD/captures/rosters/forest/player.log" --roster forest \
  --fixed-time --capture-stills "$PWD/captures/rosters/forest" --stills-at 25,60,100,145,175
```

Results:

- **Both sets play the whole song windowless** (`--fixed-time --stats`, to
  the ending at 198.5 s), exit 0, no exceptions. The final peak holds 126
  alive on screen against a target of 158 (the envelope's 150 times 1.05),
  at 18 m.
- **Failures are loud:** `--roster swamp`, `--roster forest --monster-style
  painted` and `--roster heroic --crowd bone` each stop the player at
  start-up with exit 1 and the reason.
- **Vendor materials:** the mushroom keeps its hand-painted albedo and its
  glow map (its Standard material converted to URP Lit); the goblin draws
  with the pack's own URP Lit materials, its daggers and slingshot removed
  by the roster's `hide`.
- **No flag is unchanged:** `demo3d look ps-f8a1-default --baseline final
  --trial` matches demo3d/evidence/look/final/ on all 122 L* statistics,
  the largest difference 0.1 (the glowing tree's stage ground, 65.2 against
  65.1).
- **Not yet:** the set's clips use the default slot's clip annotations
  (impact and end times), and the frame probe at 60 fps is the set beads'
  to measure.
