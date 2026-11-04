# Boss Rush act 1 kit: the Overgrown Grid (ps-8z4.5)

Act 1's environment ([boss-rush.md, Environments](../../../../docs/champions/boss-rush.md#environments)): a wireframe
forest at night in the teal key, designed by eye against Synty's NatureBiomes EnchantedForest and MeadowForest and
built from scratch by `tools/recipes/kit/overgrownGrid.py` (numpy only, no vendor input). Rendered on jarvis (M5 Max),
2026-11-03, Mono player, 1920x1080 scaled to 1280.

```sh
just champions-tool kit bake overgrownGrid               # the pieces (champions build runs it too)
just champions-tool stills --scene kit-overgrownGrid     # every kit prop in a show arena
just champions-tool play --headless --file <120 BPM click track> --mode bossrush --boss 1 --walk reference \
  --picks reference --capture-stills DIR --stills-at 2.4,10,20
```

## What it is

| Piece | Role | Footprint, height, breaking | Triangles |
| --- | --- | --- | --- |
| `brokenOak` | dresses `pillar` | r 0.9, 3.2 m, unbreakable (grey) | 71 |
| `crystalCluster` | dresses `crate` | 1.6 x 1.6, 1.4 m, 2 blows (key) | 129 |
| `lantern` | dresses `post` | r 0.35, 1.6 m, 1 blow (key) | 66 |
| `fallenLog` | dresses `slab` | 4.0 x 0.8, 1.1 m, unbreakable (grey) | 66 |
| `giantMushroom` | prop | r 0.5, 3.0 m, 2 blows (key) | 54 |
| `runeStone` | prop | 1.0 x 0.6, 2.4 m, unbreakable (grey) | 74 |
| `stump` | prop | r 0.6, 0.7 m, 1 blow (key) | 60 |
| `fernTree` | prop | r 0.3, 3.4 m, 1 blow (key) | 196 |
| `roots`, `moss` | floor treatment | never solid; scattered clear of walls, props, zones and starts | 96, 27 |
| `treeline`, `hills` | backdrop rings | outside the walls where the outline faces away from the camera | 210, 36 |

No act 1 fight has its own arena yet, so the kit dresses the stand-in arena every act-1 slot uses (its pillars,
crates, posts and slabs drawn as the kit's kinds, same footprints, so the sim is unchanged) and its floor and
backdrop; each fight's arena picks the kit up from its act when it lands.

## Stills

| Still | Shows |
| --- | --- |
| [boss1-forming](boss1-forming.jpg) | act 1, boss 1 (the Treasure Goblin's slot, the Fat Ogre standing in): the arena and the forest rising round the hero |
| [boss1-fight](boss1-fight.jpg), [later](boss1-fight-later.jpg) | the fight: two broken oaks, a crystal cluster, roots and moss patches on the floor, pines framing the top corners |
| [boss5-fight](boss5-fight.jpg) | boss 5 (Moonhowl's slot, the dragon standing in); the dragon has already broken an oak |
| [kit-standing](kit-standing.jpg), [kit-forming](kit-forming.jpg) | `--scene kit-overgrownGrid`: all eight props round a show arena (the world's first key, cobalt, since no act holds the zone there) |
| [pieces](pieces.jpg) | an offline preview of every piece from two angles (drawn edges over a dark fill) |

## Blind critiques

Each by a fresh agent given only these images and the game's aim, no docs or recipe.

### 1. First kit

> No. It reads as a Tron grid with a white stadium ring and a few props scattered on it. [...] **Toadstools**: at
> game scale they are tiny dot clusters, easy to take for XP gems, bullets or dust. [...] **Trees outside the ring**
> [...]: octagon "wheels" with radial spokes. They read as spider webs, gears or turrets, not trees. [...] **Fallen
> log** and **fern**: these read correctly. [...] Mostly it stays out of the way.

What changed: an earlier round's ferns (they read as spiders) and the toadstools were cut from the floor; the
backdrop's round-crowned trees became pines only, spaced apart; the hills became one row of large facets; the
oak's five buttress roots became three low ones; the treeline reaches further down the flanks. Not taken (the
critique also asked for them): the trees aren't see-through (they occlude, as a crop of the frame showed); a
forest on every side, which the high camera would put between the player and the near walls; a moon, which is
Moonhowl's fight's.

### 2. After the changes

> **Fallen log and rune stone (white):** both clear. The log reads best of everything. [...] The log, broken oak,
> stump, rune stone and mushroom are closed. [...] Mostly neutral to supportive: the props stay dim and the orange
> boss and white hero dominate. [...] In the fight shots, the faceted pines in the top corners read as a dark
> forest edge, but they are cropped clumps, not a skyline. [...] **Moss patches:** [...] They read as gems or XP
> shards, and that is a real pickup confusion.

What changed: the moss became one broad flat patch 3.4 m across, its outline only, over a dark fill (the stills
above). Its "spiked disc" and spoked octagons in the forming shot were the props and pines mid-rise (the height
scaled toward the ground). Left as they are: the grey reserved for unbreakable props (the arenas' rule), the
grid's hue (the zones'), the crystal reading as something to hit (it is breakable).

## Limits

- The camera is pitched 60 degrees and holds the whole ring, so the far-side backdrop sits just past the frame's top
  edge; what shows is the forest at the top corners. A larger arena that scrolls shows more of it.
- The lantern is small at game scale; it dresses the post, whose footprint is r 0.35 m.
