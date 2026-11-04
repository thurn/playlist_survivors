# Boss Rush arenas (ps-8z4.2)

The arenas every Boss Rush fight builds on ([boss-rush.md, Arenas](../../../../docs/champions/boss-rush.md#arenas)).
Stills from `champions stills --scene arenas --scene arena-scroll` (Mono player, fixed time, 1920x1080, scaled to
1280 here), 2026-11-03.

## What landed

- **Data**: `Content/arenas.json` (`ArenaRules`): an arena's outline (`radiusM` circle, `sizeM` box or `pointsM`),
  floor zones (`safe`, `hazard`, `breakable`, `moving` with a loop and speed; starting `open`, `raised` or `sunk`), prop
  placements, the hero's and boss's starts and the camera's `maxScale`/`marginM`. Content is checked on load: zones and
  props inside the walls, both starts on clear open floor.
- **Placeholder props**: `pillar` and `slab` (the hero can't break them; grey), `crate` (2 hits) and `post` (1 hit),
  drawn in the zone key's edge colour since grey is reserved for the unbreakable.
- **Arenas**: `standin` (a 32 m 32-gon, two pillars and two crates: the stand-in for fights and the mode shell before
  their own arenas land), `testbed` (every zone kind and change) and `long-hall` (72 x 22 m: the camera scrolls).
- **Sim** (`Sim/World/BossArena.cs`, `sim.Arena` when `SimConfig.Arenas` is set): forms round the hero over 4 beats
  (the beatless telegraph beat without music), the arena placed so its hero start lands on the hero; it is the run's
  `IStructures`, so the hero's walk and dashes, the horde's adds, spores and now boss bodies all stop at walls, blocked
  zones, wall obstacles and props, sliding along them. A body already on ground that changes under it walks out freely
  (never trapped). `Cast` answers shots: walls, raised floor, wall obstacles and shot-blocking props stop them; pits,
  holes, webs and lava don't. Changes (`Raise`, `Sink`, `Level`, `Break`, `Grow`, `Clear`, `Crumble`, `Shatter`,
  `Retract`) emit `ArenaForm`/`ArenaRetract`/`ArenaZone`/`ArenaObstacle`/`ArenaProp` events. Broken props and floor stay
  broken until the arena retracts.
- **Framing** (`Sim/World/ArenaFraming.cs`): a small arena is held whole and still, never nearer than the normal camera;
  past its `maxScale` the view scrolls with the hero, short of the walls, always holding the hero, the boss and its live
  telegraphs (easing out further if they need it).
- **Mode shell**: each bossrush.json slot names its `arena` (default `standin`, checked against arenas.json); a Boss
  Rush run's forming stage forms it round the hero, and the next boss's forming replaces it.
- **Client**: `ArenaPresenter` draws it in the edge look (`ArenaEdge.shader`: one pass, float-driven, no keywords, so
  one variant set) and drives `WorldCamera.Frame`; `RunPresentation` wires it whenever a run has arenas.
- **Tests**: `dotnet/Champions.Sim.Tests/ArenaTests.cs` (17): content checks, forming time, walls and slides, props
  and crumbling, dashes into walls, shots, raised/sunk/broken floor, grown obstacles, moving zones, a stand-in duel kept
  inside the walls, a Boss Rush run forming each slot's arena round the hero, replay hash, and both framing modes.

## Stills

The test bed (`arenas`), 120 BPM, formed at 1 s; changes on the beats from 5 s:

| Still | Shows |
| --- | --- |
| [forming](arenas-forming.jpg) | 1.9 s, mid-form: the walls drawing in round the outline and rising; the props not yet up |
| [standing](arenas-standing.jpg) | 4.0 s: grey walls, pillars and slab; the breakable crate and posts in the key's colour; the hazard hex in the warning colour; the open tiles, shelves and raft as faint outlines |
| [tile-raised](arenas-tile-raised.jpg) | 5.6 s: the west tile raised into a block |
| [pit-sunk](arenas-pit-sunk.jpg) | 6.1 s: the centre tile sunk into a void pit with its step |
| [shelf-broken](arenas-shelf-broken.jpg) | 6.6 s: the east shelf broken away to void |
| [web-and-wall](arenas-web-and-wall.jpg) | 7.6 s: a web (spokes) and a wall grown from their centres |
| [crate-crumbled](arenas-crate-crumbled.jpg) | 8.4 s: the crate gone on its crumbling's frame |
| [retracting](arenas-retracting.jpg) | 10.9 s: retracting, props sinking first |

The long hall (`arena-scroll`): [forming](arena-scroll-forming.jpg), [west end](arena-scroll-west-end.jpg) (the view
eased out to its farthest, short of the west wall), [scrolling](arena-scroll-scrolling.jpg) and
[east end](arena-scroll-east-end.jpg) as the hero walks east.

A real Boss Rush run (`champions play --headless --file <120 BPM click track> --mode bossrush --boss 1 --walk reference
--picks reference --capture-stills ... --stills-at 1.2,12`): [forming](bossrush-forming.jpg) under act 1's card, and
[the stand-in Ogre's fight](bossrush-fight.jpg) inside the stand-in arena, the camera holding it whole.

## Left to others

- Act kits dress arenas with their own prop kinds and floor; fights author their arenas in `arenas.json` and drive the
  changes from their modules (the scripted `ArenaScript` is for stills and tests).
- Web slowing, lava burning and falling into a pit are the fights' rules: the arena marks the floor (`ZoneAt`,
  `ObstacleAt`) and blocks bodies; it never hurts.
- The sandstorm's visibility radius and the Mirror's axis are fight-level.
- The look in motion is an operator check ([operator-pending](../../operator-pending.md)).
