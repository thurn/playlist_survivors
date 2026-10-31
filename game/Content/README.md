# The game's content

The run's data, as JSON the game owns: native's 30-minute run converted once into metres
(docs/design/unity-game.md, [Content Data and Units](../../docs/design/unity-game.md#content-data-and-units)).
The sim loads it with `GameContent.Load` (game/Assets/Game/Sim/Content/), and its tests read the
same files under .NET and Unity.

| File | Holds |
|---|---|
| enemies.json | Enemy roles: speed, health, power, xp, radius, knockback, gait |
| cast.json | demo3d's nine models with their gem colours, and each role's model, scale and crystal colour |
| weapons.json | Weapons and their five evolutions: grid, geometry, every rank's numbers |
| passives.json | The eight passives, their ranks and per-rank stat changes |
| offers.json | The scripted offer table (levels 2 to 70), its fallback and the "any" scan order |
| progression.json | Slots, the hero's stats and caps, the experience curve, gems and chests |
| schedule.json | The run: its minutes (spawns, bosses, swarms, treasure), set pieces, the signature minotaur, the finale and zones |

## Units

- **27 px per metre.** Native's sword reach of 190 px is 7.04 m, its hero speed of 140 px/s is
  5.19 m/s, its 120 px pickup radius is 4.44 m.
- One Vampire Survivors world unit is native's 154.27 px (stats.rs's `PX_PER_VS_UNIT`), 5.71 m.
- Times are seconds (native's milliseconds divided by 1000).
- Health, power (contact damage), experience, knockback and the stats stay in Vampire Survivors
  units.
- `refSpeedMps` is native's actual enemy speed, VS's `0.231 x 1.1 x speed / 100` U/s in metres:
  0.28 of the hero's speed at `speed` 100. The design doc's example, 3.7 m/s for the skeleton,
  had read VS's data speed as pixels per second; native never moved enemies that fast.

## Changing it

Edit the JSON: the loaders refuse unknown and missing fields and references to roles, models,
weapons or passives that don't exist, naming the file and field. The sim's tests run on every
change to these files (scripts/check-fast).

The one-shot converter is `uv run --project demo3d/tools game content` (demo3d/tools/src/
demo3d_tools/content.py). It reads native's content/*.yaml, levels/survivors.yaml and the numbers
native's Rust sim keeps in code, and wrote these files. `game content --check` says whether they
still match its output; once the game edits its JSON by hand, the converter is history and can be
deleted.

## From native

Every native role and weapon, and its game entry. Roles drop native's `vs-` prefix.

### Enemy roles

| Native role | VS role | Game role | Model x scale | Gait | Notes |
|---|---|---|---|---|---|
| vs-bat1, vs-bat2, vs-bat3 | BAT1-3 | bat1, bat2, bat3 | bat x1 | x | |
| vs-bat4, vs-bat5 | BAT4, BAT5 | bat4, bat5 | bat x2 | x | level-scaled boss bats |
| vs-xlbat | XLBAT | xlbat | bat x2 | x | native's dark tint, #5a3dcc |
| vs-bat-swarm | (swarm) | bat-swarm | bat x1 | x | BAT_SWARM's role |
| vs-zombie | ZOMBIE | zombie | slime x1 | x | |
| vs-ghost | GHOST | ghost | demon x1 | x | |
| vs-ghost-swarm | (swarm) | ghost-swarm | demon x1 | x | GHOST_SWARM's role |
| vs-skeleton | SKELETON | skeleton | skeleton x1 | x | |
| vs-mudman1 | MUDMAN1 | mudman1 | goblin x1 | x. | |
| vs-mudman2 | MUDMAN2 | mudman2 | bigork x1 | x. | |
| vs-werewolf | WEREWOLF | werewolf | troll x1 | x | |
| vs-xlmantis | XLMANTIS | xlmantis | bigork x2 | x. | brute |
| vs-xlmummy | XLMUMMY | xlmummy | troll x2 | x. | brute; pale #b8c8ff against the werewolf boss |
| vs-flower | FLOWER | flower | mushroom x1 | x. | level-scaled; FLOWER_WALL's role |
| vs-xlflower | XLFLOWER | xlflower | mushroom x2 | x. | brute |
| vs-boss-xlmantis | BOSS_XLMANTIS | boss-xlmantis | dragon x1 | x | minute 10 |
| vs-boss-werewolf | BOSS_WEREWOLF | boss-werewolf | troll x2 | x | minute 15 |
| vs-boss-xlmummy | BOSS_XLMUMMY | boss-xlmummy | bigork x3 | x | minute 20 |
| vs-boss-xlflower | BOSS_XLFLOWER | boss-xlflower | mushroom x3 | x | minute 25 |
| goblin | | goblin | goblin x1 | x | the stampede and the ring |
| skeleton-ember | | skeleton-ember | skeleton x1 | x | the kick line |
| bee | | bee | demon x1 | x | the wedge |
| minotaur | | minotaur | troll x2.5 | x | the signature boss, from 90 s |
| slime, bat, eyeball, orc, orc-elite, orc-ember, skeleton, skeleton-archer | | dropped | | | the classic roster; the run never spawns them |

- Each role keeps its model's gem colour (cast.json) except the two noted.
- The four classic roles take native's survivors stats for them (`EnemyStats::for_art`, stats.rs)
  and content/enemies.yaml's health and radius. Their classic eighth-note gaits become `x`: enemies
  travel only on main beats.
- Dropped role fields: sprite atlas, hover, walk cycle, hop, death recipe and debris (native's 2D
  presentation, replaced by the cast); `group`, `size`, `behavior` and `contactDamage` (classic
  mode, or the same as `power`); `resDebuffs` (always 0, and nothing debuffs); the gait's `share`
  (the doc's step rule replaces it).
- Radii are native's, unscaled by the model's size, as native's collisions were.

### Weapons and passives

| Native | VS | Game | Notes |
|---|---|---|---|
| sword | WHIP | sword | shape `sword`: 7.04 m reach, heavy strike at 1.3x reach and 2.5x damage, cleave wave 5.93 m past it in 0.93 m bands |
| bloody-tear | VAMPIRICA | bloody-tear | sword + hollow-heart |
| garlic | GARLIC | garlic | aura, 1.83 m |
| soul-eater | VORTEX | soul-eater | garlic + pummarola |
| magic-wand | MAGIC_MISSILE | magic-wand | missile, 0.46 m, 9.43 m/s |
| holy-wand | HOLY_MISSILE | holy-wand | magic-wand + empty-tome |
| king-bible | HOLYBOOK | king-bible | orbit: 2.86 m ring (Area up to 4.5x), 0.46 m blades, 2.19 rad/s |
| unholy-vespers | VESPERS | unholy-vespers | king-bible + spellbinder |
| lightning-ring | LIGHTNING | lightning-ring | strike, 0.46 m |
| thunder-loop | LOOP | thunder-loop | lightning-ring + duplicator |
| chain-lightning | (restored) | chain-lightning | chain, 9.63 m jumps |
| fire-trail | (restored) | fire-trail | trail, 0.74 m |
| finale-blast (content/weapons.yaml) | | schedule.json `finale` | 33.3 m, 1000 damage |
| orbiting-blades, and the classic chain-lightning and fire-trail (content/weapons.yaml) | | dropped | classic mode |
| hollow-heart, empty-tome, spellbinder, duplicator, pummarola, spinach, magnet, armour (offers.rs) | | passives.json | in native's tie-break order |
| max-health, magnet, might, armour (content/passives.yaml) | | dropped | classic mode |

- Ranks are resolved: native's rank patches add numbers and replace the rest
  (`WeaponCatalog::parse`), so each rank row holds every number. Milliseconds became seconds.
- Dropped weapon fields: `source` (in the table above) and `implemented` (all true).
- A weapon's `shape` comes from native's code (engine/weapons.rs, combat.rs, bible.rs,
  missiles.rs). The sword's numbers are native's zen mode at the groove's top tier, survivors'
  only mode; classic-mode tiers are dropped.

### The schedule

- Minutes, minimums, intervals (native's `frequency`, in seconds), rosters, bosses, swarm events
  and treasure are levels/survivors.yaml's, with role ids mapped.
- A swarm event names its role (BAT_SWARM: bat-swarm, GHOST_SWARM: ghost-swarm, FLOWER_WALL:
  flower), as native's validator does. A chance of 0, or none, is `null`: no gate.
- Treasure's three chances are named by tier (native's list rolls tier 3 first); `evolves` marks
  minutes 10, 15, 20 and 25's chests, the only ones native lets evolve. VS's `level`,
  `prizeTypes` and `fixedPrizes` are dropped: native never read them.
- Set pieces, their start times (stampede 120 s, kick line 180 s, wedge 240 s, ring 300 s), the
  group cap and spacing, and the signature minotaur come from engine/musical.rs, signature.rs and
  pieces.rs with levels/survivors.yaml's `director.setPieces` and content/formations.yaml.
- Zones: native's five (twilight glade, galop woods, mushroom grove, ember hollow, infernal grove)
  become demo3d's palettes neon, blossom, snow, crypt and lava, cool to hot; the victory is in
  lava, as native's is in the infernal grove.
- Dropped: the older short level and the other levels, the reactive director's population,
  rosters, palettes and camera, the archers' volleys, the standing-stones and shrine landmarks,
  and the hero's classic swings, groove meter and timing windows (content/hero.yaml).
