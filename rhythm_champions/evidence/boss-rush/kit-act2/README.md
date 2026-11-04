# Boss Rush act 2 kit: the Sunken Sands (ps-8z4.16)

Act 2's environment ([boss-rush.md, Environments](../../../../docs/champions/boss-rush.md#environments)): ruins in a
dark desert in the dim amber key (palette zone 3, ember), designed by eye against Synty's POLYGON Ancient Egypt and
NatureBiomes AridDesert and built from scratch by `tools/recipes/kit/sunkenSands.py` (numpy only, no vendor input).
Rendered on jarvis (M5 Max), 2026-11-04, Mono player, 1920x1080 scaled to 1280.

```sh
just champions-tool kit bake sunkenSands               # the pieces (champions build runs it too)
just champions-tool stills --scene kit-sunkenSands     # every kit prop in a show arena
just champions-tool play --headless --mute --file <120 BPM click track> --mode bossrush --boss 6 --walk reference \
  --picks reference --capture-stills DIR --stills-at 2.4,10,20
```

## What it is

| Piece | Role | Footprint, height, breaking | Triangles |
| --- | --- | --- | --- |
| `lotusColumn` | dresses `pillar` | r 0.9, 3.2 m, unbreakable (grey) | 170 |
| `blockStack` | dresses `crate` | 1.6 x 1.6, 1.4 m, 2 blows (key) | 30 |
| `brazier` | dresses `post` | r 0.35, 1.6 m, 1 blow (key) | 47 |
| `fallenObelisk` | dresses `slab` | 4.0 x 0.8, 1.1 m, unbreakable (grey) | 46 |
| `obelisk` | prop | 0.9 x 0.9, 3.8 m, unbreakable (grey) | 92 |
| `cactus` | prop | r 0.35, 2.6 m, 1 blow (key) | 94 |
| `rockSpire` | prop | r 0.8, 2.6 m, unbreakable (grey) | 73 |
| `datePalm` | prop | r 0.3, 3.4 m, 1 blow (key) | 316 |
| `sandRipples`, `sandDrift` | floor treatment | never solid; scattered clear of walls, props, zones and starts | 104, 30 |
| `pyramids`, `dunes` | backdrop rings | outside the walls where the outline faces away from the camera | 42, 36 |

No act 2 fight has its own arena yet, so the kit dresses the stand-in arena every act 2 slot uses (its pillars,
crates, posts and slabs drawn as the kit's kinds, same footprints, so the sim is unchanged) and its floor and
backdrop. Each fight's arena picks the kit up from its act when it lands (bossrush.json's `acts[1].kit`).

## Stills

| Still | Shows |
| --- | --- |
| [boss6-forming](boss6-forming.jpg) | act 2, boss 6 (the Sandwyrm's slot, the Fat Ogre standing in): the arena and the ruins rising round the hero |
| [boss6-fight](boss6-fight.jpg), [later](boss6-fight-later.jpg) | the fight: lotus columns, a block stack, sand drifts on the floor, pyramids framing the top corners |
| [boss10-fight](boss10-fight.jpg) | boss 10 (the Chimera's slot, the dragon standing in) |
| [kit-standing](kit-standing.jpg), [kit-forming](kit-forming.jpg) | `--scene kit-sunkenSands`: all eight props round a show arena (the world's first key, cobalt, since no act holds the zone there) |
| [pieces](pieces.jpg) | an offline preview of every piece from the game camera's pitch and from low (drawn edges over a dark fill) |

## Blind critiques

Each by a fresh agent given only these images and the game's aim, no docs or recipe.

### 0. By eye, before the critiques

The first fight stills had a 9 m great pyramid filling the top corners and a canopic jar that drew as a small egg
at game scale (a pickup's read). The pyramids came down to 6.2 and 3.6 m, and a date palm replaced the jar.

### 1. First kit

> **Flagstone clusters**: four outlined squares look like UI cards or a pickup or loot marker. [...] The sand ripple
> curves look like motion streaks or a slash projectile trail [...] The corner pyramids are big, bright and orange
> [...] They could be dimmer. [...] The pillars and obelisks are clean.

What changed: the flagstones were cut, and a broad flat sand drift took their place (first a crescent, which drew as
a dark claw mark in the next stills, then a ten-sided patch 4.6 x 2 m, the way act 1's moss is). The ripples became
two long arcs instead of three short ones. The floor share went from 0.35 to 0.25. The pyramids lost a course line
each.

### 2. After the changes

> The big faceted pyramids in the corners and the two pillars say "Egyptian ruins" well. [...] Date palm: a palm
> tree. [...] The lozenges inside the ring are low contrast and mostly fade away. [...] The fallen one looks like a
> plank or a train. [...] The pillars, blocks, obelisk and cactus are clean.

What changed: the fallen obelisk's three evenly spaced cartouches (a carriage's windows) became one long cartouche.
Left as they are:
- The key-coloured block stacks reading close to an orange boss. Breakable props take the act's key (the kit rule),
  and the stand-in bosses are orange; act 2's own bosses bring their own hues.
- The grid's amber against an orange or red boss. That is the act's floor key (bossrush.json's zone, the palette's
  ember), not the kit's. The operator row asks about it.
- The drifts' dark fill reading as a pit. That is the floor treatment's shared look; they are low and dim, and the
  same critique says they fade away.

## Limits

- The camera is pitched 60 degrees and holds the whole ring, so the far-side dunes sit past the frame's top edge;
  what shows is the pyramids at the top corners. A larger arena that scrolls shows more of the horizon.
- The brazier is small at game scale; it dresses the post, whose footprint is r 0.35 m.
