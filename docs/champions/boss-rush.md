# Rhythm Champions: Boss Rush

## Introduction

**Twenty-five boss fights in a row, and nothing else.** Boss Rush is an
experimental variant of Rhythm Champions. It keeps the main game's hero,
weapons, supports, cards and look. It drops the horde: every minute of play
is a rhythmic boss fight in an arena built for that boss.

It's an experiment. It may become a separate game mode, or it may only feed
boss designs into the main game. Both outcomes shape the rules below:

- Bosses are **self-contained units** (model, moves, arena, effects, sound)
  that the main game could adopt with little rework.
- The player's verbs are **exactly the main game's**, so a boss tuned here
  plays the same there.

Designed with the operator on **2026-11-03**. Epic: **ps-8z4** (see
[Beads](#beads)). [design.md](design.md) governs
everything this document doesn't change; [Differences from the main
game](#differences-from-the-main-game) lists every exception.

### The run, as the player sees it

- Pick Boss Rush on the title screen, then music as usual.
- Act 1 begins: an arena forms around the white knight, and the first boss
  arrives on a downbeat.
- Fight it for two or three minutes. Hazards land on the beat. Walk out of
  most of them; dash through the occasional wave. Your weapons fight for
  you. The boss sheds XP as it takes damage, and you level up mid-fight.
- It shatters. Full heal, an equipped-item upgrade, any queued level-ups,
  and the next arena forms.
- Five bosses make an act. Each act has its own environment, palette and
  sound. Die and the act restarts with the build you entered it with.
- Beat the twenty-fifth boss to win.

## Decisions

Settled with the operator on 2026-11-03. Numbers outside this section are
starting points for tuning.

1. **25 bosses in 5 acts of 5**, in a **fixed, authored order**. Each act has
   one environment family and steps up the difficulty.
2. **Act checkpoints.** Death restarts the current act from a snapshot of the
   build, level and XP the player entered it with. The checkpoint survives
   quitting the game.
3. **XP comes from the boss.** Each time the boss loses about 10% of its
   health, it bursts a cluster of gems on the next beat, some of them into
   danger. Uncollected gems are magnetised to the hero at the kill. Tune
   for about **2 normal level-ups per boss** (a little more early on), plus
   the usual **one equipped-item upgrade** reward.
4. **The build ceiling lands near the finale.** A full build is 77 upgrade
   steps, and 25 × (2 + 1) ≈ 75. Tune so the build completes around bosses
   23–25. After capping, the existing max-health fallback applies. Act 5 is
   the full-build spectacle; there is no new progression system.
5. **Bounded, authored arenas.** Every boss has its own arena, roughly
   30–50 m across. Its shape is part of the fight: rings, lanes, pillars,
   islands, moving floor. Walls are the reserved grey. Each act dresses its
   arenas from its own environment kit.
6. **Danger is always orange/red; flavour is the boss's hue.** The part
   that hurts (the filling telegraph and its impact frame) is orange/red on
   every boss. Trails, particles, ambient arena effects and the body take the
   boss's own hue. Never yellow (the player's rhythm bonus) or white (the
   hero). Green stays the XP's.
7. **Same verbs as the main game.** Walk, Space dash with its invulnerability,
   F cleave; on-beat presses grant the yellow aura. There is no parry or other
   rhythm-judged defence. The rhythm test is that **every hazard's impact lands
   on a beat or half beat**, so dodging well means dodging on the music.
   Evasion stays collision-based.
8. **Phases are optional.** A boss *may* have health-threshold phases, each
   transition a set-piece of at most 4 beats (a form change, an arena
   reshape) during which it can't be damaged. Its bead decides. Otherwise
   every boss is damageable throughout, with no Break bar and no damage gates.
9. **Full heal after every boss.** Each fight is self-contained and tuned in
   isolation. The act checkpoint supplies the run-level stakes.
10. **25 all-new sources.** None of the main game's 10 models (its 7 fodder,
    elite and ranged types and its 3 bosses) is a Boss Rush boss. The main
    game's horde meshes may appear as a boss's summoned adds.
11. **Mean fight length 2–3 minutes**, rising through the run: about 2
    minutes in act 1 and about 3 in act 5. Boss health is scaled to the
    build expected at that boss.
12. **Challenging, not absurd.** Fairness rules, not cruelty, set the
    difficulty: see [Fair fights](#fair-fights).
13. **No memory or tracking games.** No call-and-response, no shell games,
    no remembering which tile is safe. Every test is reading what's in front
    of you and moving in time with the music.

## Differences from the main game

Everything else follows [design.md](design.md).

| Main game | Boss Rush |
| --- | --- |
| Horde time with three bosses at 3/6/9 minutes | 25 bosses back to back; no horde, no horde clock |
| Endless grid; the boss follows you | Bounded arena per boss, with grey walls |
| Boss reinforcements: 3 × 3 skeletons, 9 XP at most | XP shed by the boss; any adds are the boss's own mechanic, inside its XP budget |
| Bosses always damageable | Optional phases with ≤4-beat untouchable transitions |
| Enemy hazards orange/red | Damaging core orange/red; flavour in the boss's hue |
| Four floor keys crossfading on sections | One environment family and palette per act |
| Death ends the run | Death restarts the act from its checkpoint |
| 25–35 normal choices per run | About 50 normal choices plus 25 boss rewards |

The top-centre clock shows **ACT n · BOSS m** and the fight's elapsed time.

## The Run

### Flow

1. **Arena forms** (about 4 beats): the previous arena's lines retract, the
   next one draws itself in around the hero, and its kit's props rise into
   place. The hero keeps its position; the arena is built around it.
2. **Arrival** on a nearby downbeat (main game rule: within 4 s, or a fixed
   fallback). A boss-specific entrance with its own ring.
3. **The fight.** Level-ups hold combat immediately, as in the main game.
4. **Kill**: the boss shatters, remaining gems are magnetised, a full heal,
   the boss reward card, then queued level-ups.
5. Next boss. After the fifth, an act card (the act's name, a few seconds,
   skippable) and the next act's first arena.

The music never pauses for any of this. Checkpoints are written at act start.

### Pause, death and victory

- Pause adds **Restart act** and **Abandon run** to the main game's menu.
- Death shatters the hero, as in the main game. The result card shows the act
  and boss reached, then **Retry act** (from the checkpoint) and **New run**.
- Victory's end card shows the total time, deaths per act and the build.

### Starting points for testing

- `champions play --mode bossrush` starts a run. `--boss N` jumps to boss N
  with a scripted reference build for that point, for development and
  evidence.
- The title screen gets a **Boss Rush** entry. `just champions-bossrush` is
  the human-facing recipe (audio on, like `just champions`).

### The shell, as built (ps-8z4.1)

- `Content/bossrush.json` holds the acts (name, kit, floor key) and the 25
  slots in order, each with `health` (null keeps the fighter's own) and its
  whole XP `experience` budget. Progression filled both (see Progression,
  as built).
- **Landing a fight**: a slot is fought by its own boss once its id is an
  `enemies.json` cast with a `bosses.json` duel (or whatever registration
  the boss framework replaces that with, in `BossRushRules.Read`). Until
  then its `standIn`, one of the main game's three bosses, fights there.
  `"unlanded": "skip"` passes unlanded slots over instead.
- The sequence is the sim's `BossRush` module: forming (4 beats, the
  beatless fallback's without a beat), the call and fight, the reward
  (whatever XP the boss hasn't `Shed` drops at the kill, every gem is
  magnetised, full heal, the reward card, the level-ups) and the act card
  (3 s, Enter skips; Space stays the dash). Each stage is a `RushStage` event with the
  hero's position, for the arenas to form round it.
- Main-game reinforcements are off; the floor key is held per act.
- Checkpoints are `BossRushCheckpoint`s: taken as each act begins, saved by
  the client to `bossrush.checkpoint` in the player's data folder (also
  after a death; removed by victory or Abandon run). A retry is a new sim
  on the same music from the checkpoint, recorded in the session's inputs
  (session v7). `--boss N` and scripted runs never touch the save.

## Progression

- **Shedding.** At 90%, 80% … 10% of health the boss bursts a gem cluster
  on the next beat. Burst geometry is part of the boss's design: a
  ring, a spray in front of it, gems flung across a hazard lane. Collecting
  them should be a routing choice, never a guaranteed hit.
- **The XP budget** is fixed per boss and does not depend on fight length,
  so stalling earns nothing. Any adds a boss summons draw from the same
  budget.
- **The curve.** A Boss Rush XP table (the main game's stays untouched)
  yields about 2–3 normal levels for the early bosses and about 2 later on,
  completing a full build around bosses 23–25.
- **Health scaling.** Each boss's health = its target duration × the
  sustained damage of the reference builds expected at that boss. Both a
  focused and a broad scripted build are measured, as the main game's
  pacing evidence does. Strong builds finish faster; that's fine.
- **The act checkpoint** stores the build, level, XP and pending choices at
  act start.

### Progression, as built (ps-8z4.4)

The tables live in `Content/bossrush.json`; the pacing bead re-tunes them
once real fights exist. Evidence:
[evidence/boss-rush/progression/](../../rhythm_champions/evidence/boss-rush/progression/README.md).

- **Shedding.** The rush reads the boss's health share each tick. Each
  `shedStep` (0.1) of health lost queues a burst for the next reliable beat,
  or one fallback beat later without a beat. Several thresholds passed by one
  blow burst together on that beat. Each burst is a tenth of the budget, so
  nine bursts shed 90% and the kill drops the rest, magnetised.
- **Burst geometry** is per slot (`shed`, falling back to the file's
  default): a `ring` round the boss, a `spray` fan ahead of its facing, or a
  `lane`, a line flung across its front. Each burst is `gems` drops sharing
  its XP, placed so that no two merge into one pickup cell. Each burst logs a
  `RushShed` event (burst, boss, XP, position) next to its pickup drops.
- **Adds** take XP from the same budget through `BossRush.Draw`. Whatever
  they draw, the kill drops that much less, and once the budget is spent an
  add yields nothing.
- **The curve.** The mode's own curve is `experience.levels` and
  `growth`: 50 XP to level 2, then each level 5% more. The main game's
  `progression.json` is untouched. Each slot has `levels`: 3 for act 1's
  bosses and 2 for the rest. Its `experience` budget is exactly the curve's
  XP for those levels, rounded up. Every gem is collected by the kill, so the
  run reaches each boss at a known level. The 77th step comes with boss 24's
  reward, and boss 25's levels go to the max-health fallback.
- **Reference builds.** There are two scripted builds. `focused` is deep:
  each entry is ranked to its top before the next, so the sword comes first.
  `broad` cycles round its order. At boss N (0-based), a build has the level
  the curve expects there and `(level − 1) + N` steps. `--boss N` uses
  `reference.build` (focused).
- **Health.** `health = targetS × sustained damage`. `targetS` rises
  linearly from 120 s at boss 1 to 180 s at boss 25. Sustained damage is the
  mean of the two builds' measurements. Each is the boss health the scripted
  reference player (`positioning`, which dodges telegraphs) takes per second
  over 60 s of the duel. It fights the slot's current fighter, made
  unkillable, at 128 BPM. The fighters are still the stand-ins, so the pacing
  bead re-measures against the real fights.

## Fights

### The common language

Every boss shares this, so 25 different fights still read as one game:

- **Beat-landed hazards.** Telegraphs count whole beats. Impacts resolve on
  beats or half beats. Projectiles launch on the grid and travel
  continuously; the telegraph shows when they reach *you*.
- **A filling telegraph**: an orange/red shape that fills or closes to its
  impact frame. The boss's hue may decorate it, never replace it.
- **One signature mechanic** per boss (the roster's theme), taught alone first
  and then combined with the boss's other moves.
- **At least one dash-through wave**, clearly signalled, that walking can't
  escape (the main game's rule).
- **A boss-specific sound and effect palette**, sitting under the music
  (main game's Sound rules).
- **Silence breaks nothing.** With no reliable beat, the boss uses the main
  game's 0.5 s fallback beat and visible pulses. A boss whose mechanic is
  musical (the Chimera's layers, Anubis's tempo shifts, the finale's
  sections) defines its fallback in its bead.

### Fair fights

"Challenging but not absurd" is held by rules every boss's sim tests check:

- **Reaction time.** Act 1–2 telegraphs give at least 3 beats; later acts at
  least 2 beats, and never less than 0.6 s at any tempo.
- **A way out always exists.** For every attack sequence, a sim test proves
  that a hero walking at the base 6 m/s (no Stride), with one baseline dash
  and its 0.6 s cooldown, has a hit-free path. It must hold from any position
  where the previous attack could have left the hero.
- **Dash waves are spaced** at least the dash cooldown plus one beat apart,
  at the fastest supported tempo.
- **No off-screen hits**: every hazard's source or telegraph is in frame
  before it can hurt.
- **Damage escalates by act**, not by surprise: a clean read of act 5 should
  feel like act 1 at a higher tempo of decisions.
- **Weapon effects never hide a required dodge**: the main game's restraint
  rule, enforced harder because fights are longer.

### Arenas

- An arena definition is data: its outline (polygon or circle), walls,
  floor zones (safe, hazard, breakable, moving), prop placements from its act
  kit, the boss's start and the camera framing.
- Arenas can **change during a fight** through a small set of operations,
  used by the bosses that need them: break a region away, raise or sink tiles,
  grow persistent obstacles (webs, lava), crumble a prop.
- Props block movement and projectiles where the fight says so. Broken props
  stay broken until the fight ends.
- The camera frames the whole arena where it can (the main game eases out
  20% for bosses). A larger arena may scroll, but the boss and every live
  telegraph stay in frame.

#### Arenas, as built (ps-8z4.2)

- `Content/arenas.json`: placeholder prop kinds and the arenas. A fight adds
  its own arena there (outline as `radiusM`, `sizeM` or `pointsM`; zones
  `safe`/`hazard`/`breakable`/`moving`; props; `heroStart`, `bossStart`;
  `camera.maxScale`) and names it in its `bossrush.json` slot's `arena`
  (default `standin`). Loading checks that zones and props sit inside the
  walls and that both starts stand on clear floor.
- The sim's `BossArena` (`sim.Arena`, on in Boss Rush runs) forms round the
  hero at each forming stage, placing `heroStart` on the hero. It is the
  run's structures: the hero, its dashes, adds, spores and boss bodies stop
  at walls, blocked zones, wall obstacles and props. A body whose ground
  changes under it walks out freely; hurting it there is the fight's rule.
- A fight module changes it with `Raise`, `Sink`, `Level`, `Break`
  (breakable zones), `Grow`/`Clear` (wall, web, lava obstacles),
  `Crumble`/`Shatter` (props). It reads it with `ZoneAt`, `ObstacleAt`,
  `Walkable` (spawns, the fairness harness) and `Cast` (shots). Webs and
  lava only mark the floor; slowing and burning are the fight's.
- `ArenaFraming` holds an arena whole when it fits within its `maxScale`,
  and scrolls otherwise, holding the hero, the boss and its live telegraphs.
  The client's `ArenaPresenter` draws it all in the edge look.
- Evidence: [arenas](../../rhythm_champions/evidence/boss-rush/arenas/README.md).
- A fight's own arena also forms under `--fight ID` and its review, from its
  slot's `arena`. The fairness harness can play a fight in its arena
  (`FairnessOptions.Arena`). The reference player steers round props
  (`ScriptedPlayer.Steer`).
- **`goblinGrove`** (boss 1): a 36 m clearing. At its heart is a copse walled
  by six fallen logs (the goblin's stash). Round it runs the goblin's loop:
  12 points from due north, alternately 10.5 and 8.5 m out. The loop weaves
  between stumps inside its outer bends and lanterns outside its inner
  ones, and trees close the edge. You come in from the south, and the goblin
  waits on the loop across the copse. The arena frames whole (it never
  scrolls).

### Environments

One kit per act, built like the structure meshes: committed recipes that
rebuild Synty props as low-poly wireframes. Outputs stay in ignored folders,
under the vendor rule. Kits supply floor treatment, a backdrop silhouette
(the synthwave horizon: mountains, ruins, a skyline) and 6–12 props.

| Act | Name | Environment sources (Synty, `~/synty`) | Floor key |
| --- | --- | --- | --- |
| 1 | Overgrown Grid | NatureBiomes EnchantedForest, MeadowForest | teal |
| 2 | Sunken Sands | Ancient Egypt, NatureBiomes AridDesert | dim amber |
| 3 | The Undercroft | Dungeon, Dungeon Realms, Dark Fantasy | violet |
| 4 | Frost & Fire Peaks | NatureBiomes AlpineMountain, Viking Realm, Snow Kit | ice blue, magma-dim |
| 5 | The Signal | SciFi Worlds, SciFi Space, CyberCity, Kaiju (city, neon signs) | magenta / deep blue |

The Kaiju pack's creatures have no animation clips, so it's used for scenery
only.

#### Kits, as built (ps-8z4.5)

- **Data**: a kit is an entry in `Content/arenas.json`'s `kits`, and its prop
  kinds are ordinary `props` with `"kit"` set, so a fight's arena can place
  them directly. `dress` maps each placeholder kind to a kit kind of the same
  footprint, height, hits and blocking (checked on load), so dressing an arena
  changes nothing the sim does. `floor` scatters pieces over open floor (a
  share of `cellM` cells, `clearM` clear of walls, props, zones and starts);
  each `backdrop` ring stands pieces `gapM` outside the walls every
  `spacingM`, only where the outline faces away from the camera (its outward
  normal's y at least `facing`), lower toward the flanks. Both are decoration,
  never solid, and seeded by the arena's id.
- **Which kit**: the arena's own `kit`, else the Boss Rush run's act's
  (`bossrush.json`'s `acts[].kit`), else the act of the slot that fights in
  that arena (so `champions boss review` shows it too). The stand-in arena
  takes each act's.
- **Recipes**: `tools/recipes/kit/KIT.py`, `PIECES = {id: build}`, designed
  from scratch with the structure recipes' `Builder` and shapes, every edge
  drawn; a prop piece stands its kind's height. `champions kit bake KIT` (and
  `champions build`) writes the ignored `Assets/Baked/Kits/`; without it the
  arenas draw their placeholders. `champions stills --scene kit-KIT` shows a
  kit in a show arena.
- **Look**: unbreakable kit props grey (grey still means a wall), breakable
  ones and the floor and backdrop in the act's key; the floor dim over a
  dimmed fill, the backdrop a dark skyline whose lines fade ring by ring.
- **Act 1, Overgrown Grid**: props `brokenOak` (dresses `pillar`),
  `crystalCluster` (`crate`), `lantern` (`post`), `fallenLog` (`slab`),
  `giantMushroom`, `runeStone`, `stump`, `fernTree`; floor `roots` and `moss`; backdrop `treeline` then `hills`.
  [Evidence](../../rhythm_champions/evidence/boss-rush/kit-act1/README.md).

### Look, effects, shaders and sound

- **Models**: each boss gets a mesh designed with the
  [monster-mesh-design](../../.agents/skills/monster-mesh-design/SKILL.md)
  skill, as the main game's monsters do: a retro 3D neon wireframe, simple
  faceted volumes, closed connected edges over a dark body, moving with the
  vendor rig and clips. Bosses are big and alone on screen, so they can carry
  more drawn detail than fodder, but not so much that the lines merge into a
  fill.
- **Shaders.** Bosses may vary the edge look: a hologram scanline, crystal
  facets, light flowing along the edges, a glitch offset, ghostly fade, an
  ember core. The framework bead adds a shared boss-look shader. Bosses may
  use its keywords (`shader_feature`, so only the materials in use compile)
  or write a shader of their own when a look needs it.
  - **Budget**: the player build allows 1,000 variants and used about 290
    before Boss Rush (raised from 300 on 2026-11-03). Variants cost build and
    CI time, not frame time, so prefer float parameters where they do the
    job, and note each fight's variant count in its evidence.
  - **No arrival hitch**: the first draw with a new variant can stutter on
    Metal, so each boss's shaders and variants are warmed while its arena
    forms, before the boss arrives.
  - The frozen demo3d's crystal, spectrum and hologram styles (ps-a54.3) can
    be copied as references.
- **Effects** are authored geometry through our own shaders, as the weapons'
  are. The owned packs are references for shape and timing, not
  drop-in prefabs (their stock colourful additive particles don't fit the
  look): Hovl Studio (AOE Magic spells, Magic circles, AAA Projectiles),
  KriptoFX, Gabriel Aguiar, the Fantasy Spells Effects Pack, NeatWolf's Magic
  Spells circles and projectiles, and Synty's Particle FX.
- **Sound.** Each boss gets its own small bank (entrance, telegraph tick,
  impact, signature mechanic, death) from the owned libraries already in the
  manifest pipeline: Magic Spells SFX Bundle, RPG Magic Sound Effects Pack 3
  [ELEMENTAL] and Universal Sound FX. Banks follow `Content/sounds.json`'s
  rules: vendor files are referenced by name, never committed, and stay under
  the music. Telegraph ticks fall on the beat and help teach the rhythm.

## The roster

Each boss lists its source (the vendor prefab its mesh is designed from),
the clips that make it viable, its mechanical theme and a starting
direction for its arena, look and sound. Its fight bead designs the full
attack set; this is the identity that keeps 25 fights from feeling the same.

Source roots:
- **H** = Heroic Fantasy Creatures, only in `more_assets`
  (`~/unity_assets/MoreAssets/MoreAssets/Assets/HeroicFantasyCreaturesFullPackVol1/`).
- **IP** = `~/unity_assets/UnityAssets/Assets/Magic Pig Games (Infinity PBR)/Characters/`.
- Unprefixed packs (Dark Creatures, Forest Creatures, Polygonal Creatures,
  Polygonal Aliens) are under `~/unity_assets/UnityAssets/Assets/`. They are
  also in `more_assets`.

### Act 1: Overgrown Grid

A wireframe forest at night, teal key. It teaches the language: one
mechanic at a time, generous telegraphs.

1. **The Treasure Goblin** — *The Chase.*
   - Source: `Forest Creatures Pack/Treasure Goblin/Prefabs/Treasure Goblin - Blue.prefab`.
     Clips: SliceAttack, StabAttack, SpellCast, run.
   - It flees, and you chase it through a winding grove. It drops beat-timed
     bombs behind it and spills extra gems when hit. A gentle opener that
     teaches telegraphs, the beat and shedding. The dash wave is a ring of
     bombs it detonates when cornered.
   - Look: a bright coin-gold body (amber, not yellow); coins spray in arcs.
     Sound: jingling coins, a cackle on the downbeat.
   - Inspiration: Diablo's treasure goblins.
   - **As built (ps-8z4.6)**: `TreasureGoblinModule` in the grove
     `goblinGrove` ([Arenas, as built](#arenas-as-built-ps-8z42)).

     | Move | When | What lands |
     | --- | --- | --- |
     | **Bomb trail** (signature) | While it flees, every other beat (every beat in its panic) | A 1.7 m bomb where it stood, on a 4-beat fuse, popping on its beat |
     | **Coin spray** | Kept 10 to 16 m off | An arc of 7 coins (90°) launched on the beat at 9 m/s, with walking lanes between them. In its panic, a second arc of 6 follows a beat later, into the first's gaps |
     | **Dagger stab** | Caught, within 4.5 m | A 4.5 m line at the hero, 3 beats |
     | **Ring of bombs** (dash wave) | Held within 6 m for 4 beats | 8 bombs round it and a shockwave on a downbeat, 4 beats on. The shockwave runs at 13 m/s across the whole grove (30 m), so the only way through is to dash it. It has 2 fronts in its panic |
     | **Panic** (its one phase) | Below 40% health | A 2-beat cackle, untouchable. Then it runs 15% faster and its moves double up |

     - **How it moves.** It runs a loop round the walled copse at 4.5 m/s
       (the hero walks 6, so it gets caught), always the way that takes it
       farther from you. Cut across the copse and it turns back. Hang back
       past 16 m and it comes round to taunt you.
     - **Taught alone.** Nothing but the trail comes until it has dropped 8
       bombs or fought 32 beats.
     - **Shedding.** Each burst spills behind it, along its trail.
     - **No beat.** It falls back to the 0.5 s beat.
     - **Damage** (the hero has 100): bomb 10, coin 8, stab 15, ring bomb 12,
       shockwave 16.
     - **Reference runs.** Chased by the reference player with the
       reference builds, it lasts 100 to 114 s at 204 health and sheds all
       nine bursts (2 level-ups). Evidence: [treasureGoblin](../../rhythm_champions/evidence/boss-rush/treasureGoblin/README.md).
2. **The Hive Queen** — *Swarm formations.*
   - Source: `Polygonal Creatures Pack/Polygonal Giant Bee/Prefabs/Polygonal Giant Bee Blue.prefab`.
     Clips: Sting Attack, Claw Attack, Cast Spell, Defend.
   - Drones fly in beat-locked formations (lines, Vs, spirals, a closing
     net), and you walk through the gaps. The Queen dives on the downbeat.
     The dash wave is a solid wall of drones.
   - Arena: a honeycomb clearing whose hex cells pulse. Look: drones as tiny
     two-facet darts; the swarm draws shapes across the floor. Sound: a hum
     whose pitch rises to the impact.
   - Inspiration: Just Shapes & Beats, Hollow Knight's swarms.
   - **As built (ps-8z4.8)**: `HiveQueenModule` in her `hiveClearing`, a
     flat-topped hexagon 34 m across with a lantern or stump in each corner.
     The signature is the *sweep*: a row of drones the clearing's width,
     with 3.4 m gaps every 8.5 m, launched from the wall behind her and
     crossing in 6 beats (5 in phase two). It is taught alone twice, then
     as a *chevron* (a V with a gap at its point), then the *dive* (her body
     flies to the hero's marked spot and stings it on a downbeat). After
     that come the *spiral* (rings of five drones out of her, one a beat,
     each turned on), the *net* (six rows round the hero, open at the
     corners, closing in 4 beats) and the dash wave, a gapless *wall* row
     on a 4-beat telegraph faster than walking. Below half health she
     swarms to the centre, untouchable for 4 beats, then combines: a sweep
     under a dive, two sweeps crossing two beats apart, the net round a
     dive. Drones are magenta two-facet darts that stream out of her while a
     row's telegraph fills; the honeycomb cells pulse on every beat; her
     edges carry the flow and a faint scan. Her bank is the Universal Sound
     FX insects, the tick a buzz that climbs in pitch to each landing
     (`BossModule.TickRise`). Evidence:
     [hiveQueen](../../rhythm_champions/evidence/boss-rush/hiveQueen/README.md).
3. **The Bloom** — *Rotating spirals.*
   - Source: `Forest Creatures Pack/Carnivorous Plant/Prefabs/Carnivorous Plant-Red.prefab`.
     Clips: Bite, BreathAttack, BreathAttackSurround, SpellCast.
   - Rooted at the centre of a ring arena, it fires rotating arms of spores,
     so you circle it. The arms change direction and speed on bars. Its bite
     punishes standing close.
   - Look: petals as faceted fans that open on the beat. Sound: a breathy
     pulse, wet snaps.
   - Inspiration: Cuphead's Cagney Carnation, bullet-hell spirals.
   - **As built (ps-8z4.10)**: `BloomModule` (`Sim/Rush/BloomModule.cs`),
     violet (the accent family), in its own arena `bloom`: a ring 34 m
     across, rooted at its centre, four tree ferns by the wall. Its arms
     (the signature) are lines of spores from the root to the wall, a new
     set landing on every downbeat where the last bar's have turned to,
     each bar's speed and direction its own and the tip never faster than
     the walk. Phase 1 teaches three steady arms alone, then plays the
     spore spiral (rings of shots on consecutive beats, each turned a
     quarter gap further), arms that reverse on the bar and the pulse (the
     dash-through wave, a ring from the root faster than any walk). The
     bite (a cone at whoever stands within 5.5 m, on the half beat) joins
     every move but the lesson and the pulse. Below half health it
     blossoms (four untouchable beats): four arms changing speed and
     direction every bar, denser spirals and a two-front pulse. Its XP
     bursts fling seeds along a spiral arm. Evidence:
     [evidence/boss-rush/bloom/](../../rhythm_champions/evidence/boss-rush/bloom/README.md).
4. **The Rootwarden** — *Echo eruptions.*
   - Source: `Polygonal Creatures Pack/Polygonal Treant/Prefabs/Polygonal Treant Green.prefab`
     (+ its Root and Projectile Seed prefabs). Clips: Root Attack,
     Underground, Spawn, Stomp Attack, Projectile Attack.
   - Roots erupt where you stood a few beats ago, so you must keep moving on
     the beat, and your own trail becomes the hazard. It burrows and
     resurfaces. Later, eruptions chain along your path.
   - Look: roots as long, branching line strokes. Sound: cracking wood,
     rumbles timed to the eruptions.
   - **As built (ps-8z4.12)**: `RootwardenModule` in its `rootHollow`, a
     round clearing 34 m across, its middle open floor and its props (two
     broken oaks, logs, rune stones, stumps, mushrooms, four lanterns)
     standing back by the wall. The signature is the *echo*: for 6 beats
     running each beat marks the hero's spot and a root erupts there 3
     beats later, on its beat, so a hero who keeps moving leaves every
     eruption behind and one who stands still is hit. It is taught alone
     twice, then the *line* (eruptions racing out from it toward the
     hero's spot, one a half beat, walked out of sideways), the *seeds*
     (five lobbed round the hero, sprouting together), the *burrow* (it
     sinks over a beat, travels under the floor to the hero's marked spot
     and bursts up on a downbeat, a ring of roots spreading a beat later;
     `BossModule.Sunk` lowers its body) and the dash wave, the *quake*
     (its stomp sends a ring of roots across the hollow faster than
     walking). Below half health it draws back to the hollow's heart,
     untouchable for 4 beats, then the echo *chains*: 8 marks, each
     eruption a root along the hero's path from the last marked spot to
     this one; lines fan three wide, seeds come seven at a time, the chain
     runs under a line or seeds, and the quake sends two rings. Its hue is
     cyan (the Bloom before it is violet, the Hive Queen magenta; the
     Treant's own Blue variant suits it): a branching stroke crawls from its body to every telegraph
     as it fills and faceted spikes burst out of the ground on the
     landing; its edges carry a slow flow and an ember core. Its bursts
     strew gems along the hero's last steps. Its bank is the Universal
     Sound FX wood (snaps for the echoes, cracking trees for its entrance
     and death, the tick a twig snap climbing to each eruption). Evidence:
     [rootwarden](../../rhythm_champions/evidence/boss-rush/rootwarden/README.md).
5. **Moonhowl** (act finale) — *The moon cycle.*
   - Source: `H/Mythological Creatures Pack/Werewolf/Prefabs/Werewolf.prefab`.
     Clips: Howl, claw combos and their Forward versions.
   - The arena's light cycles over 8 bars. Under a waning moon it stalks and
     swipes. At full moon it frenzies, lunging on every beat, and some lunges
     need a dash through. The cycle makes its tempo visible.
   - Look: a moon disc on the backdrop whose phase is the timer; afterimage
     trails at full moon. Sound: the howl marks the turn of the cycle.
   - Inspiration: Bloodborne's beasts, Hades' Asterius charges.
   - **As built (ps-8z4.14)**: `MoonhowlModule` (`Sim/Rush/MoonhowlModule.cs`),
     cold blue (the key family), in its own arena `moonGlade`: a ring 34 m
     across inside seven standing rune stones, dressed with the act 1 kit.
     Everything lands three beats after it is planned, on a cycle of eight
     bars (six below half health) counted from a downbeat: the howl (a disc
     round it on the downbeat, the bank's signature: the turn of the cycle),
     then the frenzy, eight beats of lunges, each a slash line from where the
     last left it through where the hero stood, run on its beat; on the bar's
     downbeat a *moon lunge* bounds it to the hero's side while a front the
     glade's width sweeps from the far wall at 20 m/s (the dash wave: nowhere
     to walk to). The first frenzy teaches plain lunges alone, on every beat.
     The rest of the cycle is the stalk: it follows the hero (slower than the
     walk), swipes a two-claw combo (the second on the half beat) at a hero in
     reach and leaps onto one keeping 9 m away. The moon disc in the top
     right corner, over the pines, is full from the howl through the frenzy, new halfway through the
     stalk and full again at the next howl, so it times the frenzy; afterimages
     trail the body while it is full. Below half health the hunter's moon (four
     untouchable beats) shortens the cycle to six bars, gives each frenzy two
     moon lunges and the combo a third claw. Its XP bursts fall in a crescent
     behind it. Evidence:
     [evidence/boss-rush/moonhowl/](../../rhythm_champions/evidence/boss-rush/moonhowl/README.md).

### Act 2: Sunken Sands

Ruins in a dark desert, dim amber key. Each boss changes what you can
see or where you can stand.

6. **The Sandwyrm** — *Ripples.*
   - Source: `IP/Giant Worm/_Prefabs/Giant_Worm_LP.prefab`. Clips: Appear,
     Disappear, Hidden, Attack01–03, Cast1.
   - It hunts unseen beneath the sand. Ripple lines on the floor show where
     it travels; it erupts on the downbeat under where it was heading. Later
     it surfaces in arcs that you cross with a dash.
   - Look: the floor grid displaces along its path. Sound: a low sub rumble
     that tightens to the burst.
   - Inspiration: Dune, Tremors, Hollow Knight's Dung Defender.
7. **The Bandaged King** — *Sandstorm.*
   - Source: `H/Living Dead Pack/Mummy/Prefabs/Mummy.prefab` (+ Sarcophagus).
     Clips: BandageWhip combos, Awakening, attacks.
   - A sandstorm drops what you can see to a radius around the hero. The
     boss, its telegraphs and the walls always glow, but the arena's props
     and floor fade. Long bandage-whip lashes reach out of the dark. The storm
     comes and goes in waves.
   - Look: sweeping particle bands of sand. Sound: wind swells, a whip crack
     on the beat.
8. **The Gorgon** — *Line of sight.*
   - Source: `IP/Medusa/_Prefabs/Medusa_LP_Serpent.prefab`. Clips: Snakeheads,
     Attack1–3, Taunt.
   - Her gaze builds over a bar and strikes on the downbeat. Anyone in her
     line of sight is hit unless behind a pillar. The gaze cracks pillars, so
     cover runs out and you keep relocating. Her snake-head volleys punish
     hiding too long.
   - Look: a sightline cone painted on the floor; petrified pillars go grey.
     Sound: hissing that crescendos to a stone crack.
   - Inspiration: Boos and gaze mechanics; Elden Ring's cover fights.
9. **The Judge** — *Tempo shifts.*
   - Source: `IP/Anubis/_Prefabs/Anubis_lp.prefab` (+ Staff). Clips:
     Attack1–4, Cast, Dodge, Taunt.
   - It switches its hazards between half-time and double-time with the
     music. The same staff sweeps and sand-scale blasts land on half notes in
     one passage and on eighth notes in the next. Each switch is announced by
     a scale tipping on the backdrop. It tests your rhythm, not your recall.
   - Look: balance-scale motifs; ankh glyphs on the floor. Sound: a ticking
     metronome that doubles or halves with each shift.
10. **The Chimera** (act finale) — *Polyrhythm.*
    - Source: `H/Mythological Creatures Pack/Chimera/Prefabs/Chimera.prefab`.
      Clips: RamAttack, ClawsAttack, SnakeBiteAttack, RamBiteCombo.
    - Three heads, three rhythmic layers: the lion on downbeats, the ram on
      backbeats, the snake on off-beats. Each head has its own attack shape,
      and the layers stack as the fight goes on until all three play together.
    - Look: each head in its own shade of the boss hue. Sound: each head's
      cue pitched like a drum kit (kick, snare, hat).
    - Inspiration: Hades' Lernie, Crypt of the NecroDancer.

### Act 3: The Undercroft

Crypts and halls, violet key. Bosses that reshape the space.

11. **The Hoard** — *Treasure artillery.*
    - Source: `Polygonal Creatures Pack/Polygonal Treasure Chest/Prefabs/Polygonal Treasure Chest Monster Purple.prefab`.
      Clips: Bite Attack, Lick Attack, Open/Close, Roar.
    - The mimic spits volleys of coins in high arcs that land on beats in
      marked zones, like mortar fire. The volleys walk across the room in
      patterns. When you come close its lid snaps and its tongue lashes.
      The dash wave is a carpet barrage.
    - Look: gold arcs drawn as dotted parabolas. Sound: a cascade of coins
      landing like hi-hats.
12. **The Weaver** — *Web maze.*
    - Source: `H/Fantasy Animals Pack/Darkness Spider/Prefabs/DarknessSpider.prefab`.
      Clips: ThrowSpiderWebNormal, ThrowSpiderWebThreat, Bite3HitCombo.
    - The webs it throws persist and slow the hero, so over the fight the room
      becomes a maze, and the spider races along its own strands. Weapons can
      cut webs, so building the maze is a duel.
    - Look: webs as fine line meshes that shiver on the beat. Sound: taut
      string plucks.
    - Inspiration: Path of Exile's Sirus maze, Diablo's Arachnid Lair.
13. **The Watcher** — *Laser sweeps.*
    - Source: `H/Demonic Creatures Pack/Evil Watcher/Prefabs/EvilWatcher.prefab`.
      Clips: CastSpell, StingerAttack, claw attacks, WakesUp.
    - Beams sweep the room like clock hands, stepping on the beat. Later
      beams cross, reverse and multiply. It wakes in a sealed chamber.
    - Look: thick beam ribbons with a hot core. Sound: a rising charge and a
      sustained buzz that cuts on the off-beat.
    - Inspiration: Path of Exile's Shaper beams, Diablo's Gatekeepers.
14. **The Wraith Council** — *Three at once.*
    - Source: `Dark Creatures Pack/Wraith Pack/Prefabs/` Wraith
      Swordsman-Purple, Wraith Spearman-Purple and Wraith Mage-Purple. Clips:
      MeleeAttack, SpinAttack, ThrowProjectile, SpellCast, Defend.
    - Three wraiths with separate health bars and coordinated moves: the
      swordsman closes in, the spearman lances lanes, the mage zones the room.
      Kill one and the survivors enrage, so kill order is the choice.
    - Look: three hues within the violet family. Sound: a choir that drops a
      voice per kill.
    - Inspiration: Hades' Furies, Dark Souls' Ornstein & Smough.
15. **The Demon Lord** (act finale) — *Collapsing floor.*
    - Source: `H/Demonic Creatures Pack/Demon Lord/Prefabs/DemonLord_PBR.prefab`.
      Clips: SummonSword&Whip, WhipAttack, SwordAttack1–2, Fly, Roar.
    - The arena breaks into islands as the fight goes on. Whip lines rake
      across them and summoned swords rain into marked zones. In its last
      phase it takes flight above the remaining islands.
    - Look: hellfire flavour in deep magenta; the fallen floor drops away as
      shards. Sound: a whip crack, stone collapsing on downbeats.
    - Inspiration: Diablo IV's Lilith, Hades' Hades.

### Act 4: Frost & Fire Peaks

Mountain summits, ice blue and magma. The terrain fights back.

16. **The Rime Golem** — *Split and merge.*
    - Source: `H/Demonic Creatures Pack/Golem/Prefabs/GolemIce_PBR.prefab`
      (+ GolemSplitIce_PBR, IceBlock prefabs). Clips: Explode, Assemble,
      Attack1–5, Roar.
    - At health thresholds it explodes into three small golems. They walk back
      together and reassemble unless you kill them within a few bars, and
      reassembling restores some of its health. Splitting changes the fight
      from one big threat to three small ones.
    - Look: crystal-facet shader; the ice blocks shatter outward. Sound:
      grinding ice, a chord as it reassembles.
17. **The Magma Heart** — *The floor is lava.*
    - Source: `Polygonal Creatures Pack/Polygonal Magma/Prefabs/Polygonal Magma Orange.prefab`.
      Clips: Charge, Charge and Explode, Explode, Punch Attack, Swing Attack.
    - Floor tiles turn to lava in waves, and safe islands move each bar. It
      charges up and explodes, leaving craters that cool over a few beats.
    - Look: an ember-core shader with light flowing along the edges; lava as
      pulsing filled tiles (red where it hurts). Sound: a bubbling bass, a
      hiss as tiles cool.
    - Inspiration: Cuphead, Mario's lava bosses.
18. **The Griffin** — *Wind and dives.*
    - Source: `H/Mythological Creatures Pack/Griffin/Prefabs/Griffin1SidedFeathers.prefab`.
      Clips: Swooping, HitTheGround, FlyStationaryClawsAttack, combos.
    - Mostly airborne: dives are marked by its shadow growing on the floor.
      Its wingbeats send gusts on the beat that push the hero across the
      arena, toward hazards and the edge.
    - Look: wind as streaming line bands; the camera eases out for its dives.
      Sound: a wingbeat on every beat, a shriek at the dive.
19. **The Dragonide** — *Syncopation.*
    - Source: `H/Fantasy Lizards Pack/Dragonide/Prefabs/Dragonide.prefab`.
      Clips: 2-, 3- and 4-hit weapon and unarmed combos, SpinningTailAttack,
      DefenseBlocked.
    - A fast duelist on a narrow mountain path. Its strings of 2–4 hits land
      *off* the beat, on syncopated rhythms announced by a phrase of ticks.
      The only boss that asks you to dodge between beats.
    - Look: crisp slash arcs in its hue. Sound: steel ticks that spell out
      the phrase before it plays.
    - Inspiration: Sekiro, Furi.
20. **The Peak Dragon** (act finale) — *Strafing runs.*
    - Source: `H/Fantasy Lizards Pack/Mountain Dragon/Prefabs/MountainDragon.prefab`.
      Clips: SpitFireBall, SpreadFire, FlyStationarySpreadFire, FlyNormal,
      claw combos.
    - It circles a ring-shaped summit and bombs lanes in strafing runs. Then
      it lands at the centre for melee and breath before taking off again.
      Distinct from the main game's dragon in both model and move language.
    - Look: storm clouds on the backdrop, lightning lighting the lanes.
      Sound: engine-like wing roars, thunder on the downbeat.
    - Inspiration: Monster Hunter's flying wyverns, Elden Ring's dragons.

### Act 5: The Signal

An alien grid and a neon city, magenta and deep blue. The hardest act and
the full build's showcase.

21. **The Brood Mother** — *Pinball.*
    - Source: `Polygonal Aliens Pack/Polygonal Alien Magantis/Prefabs/Polygonal Alien Magantis Purple.prefab`
      (+ Magantee adds). Clips: Rolled Up Spin Fast/Slow, Spawn Bug From
      Mouth, Spit Attack, Sting Attack.
    - It rolls into a ball and ricochets off the walls, bouncing on the beat
      along trajectory lines drawn ahead of it. Between rolls it spits
      brood bugs (adds, inside its XP budget).
    - Look: trajectory lines like pool-table guides. Sound: a hard bounce on
      each wall hit, a pinball-machine ding.
22. **The Strider** — *Colossus legs.*
    - Source: `Polygonal Aliens Pack/Polygonal Alien Ulifo/Prefabs/Polygonal Ulifo Blue.prefab`.
      Clips: Rise Up, Lower Down, Leg Attack, Stomp Attack, slice attacks.
    - A towering stilt-walker. Its legs stomp in sequence; the space under
      its body is safe until it lowers itself. It strides across the arena,
      and its stomp pattern rides the bar.
    - Look: the camera eases out; leg stomps send rings through the grid.
      Sound: deep stomps on the beat.
    - Inspiration: Shadow of the Colossus, Half-Life's Striders.
23. **The Mirror** — *Mirrored movement.*
    - Source: `Polygonal Aliens Pack/Polygonal Alien Longtail/Prefabs/Polygonal Alien Longtail Blue.prefab`.
      Clips: Fly Spin Attack, Fly Stab Attack, Fly Cast Spell, Fly Dodge.
    - It mirrors your movement across the arena's centre line and fires where
      you're heading. You control where it is, so steering it into good
      positions is the fight. Later it switches its mirror axis on a bar.
    - Look: a faint mirror plane across the arena; a hologram-scanline
      shader. Sound: your own dash sounds echoed back, reversed.
    - Inspiration: Furi's mirror duels, Hades' Theseus.
24. **The Telekinetic** — *Gravity.*
    - Source: `Polygonal Aliens Pack/Polygonal Alien Telekinis/Prefabs/Polygonal Alien Telekinis A Purple.prefab`.
      Clips: Mind Control, Defend Force field, Projectile Attack 01–02,
      Meditate, Fly Away.
    - Gravity wells pull the hero in. It lifts the arena's debris into orbit
      around itself and flings it on the beat. Its force field shoves you back
      on the downbeat.
    - Look: orbiting props drawn in its hue; well rings bend the grid. Sound:
      a pitch-bending warble, the snap of a throw.
25. **The Titan** (the finale) — *The Remix.*
    - Source: `H/Must Have Fantasy Villains Pack/Cyclops/Prefabs/Cyclops.prefab`.
      Clips: 2FistsCrushAttack, 2FistsSmashAttack90 Left/Right, combos, roar.
    - A giant that plays back the run. At each musical section change the
      arena reforms into an earlier act's environment, and the Titan fights
      with a remix of that act's signature mechanics (spirals, ripples, lasers,
      lava, gravity) on top of its own fist slams. With no sections, it changes
      every 8 bars. The full build's victory lap, and the hardest fight.
    - Look: its single eye takes the current act's hue. Sound: each
      section's cues borrow from that act's bosses.
    - Inspiration: Path of Exile's Sirus and Maven, Hades' final phase,
      Mega Man's boss refights.

## Implementation outline

The beads hold the detail; this is the shape.

- **The mode shell**: `--mode bossrush`, the title entry, `Content/bossrush.json`
  (acts, boss order, health and XP tables), act and fight sequencing, the
  checkpoint save, rewards, the HUD clock, the result cards and `--boss N`.
  Until the fights land, its slots are filled by the main game's three
  bosses in a stand-in arena.
- **Boss progression**: XP shedding, the Boss Rush XP curve, health scaling
  and the reference builds per boss index.
- **Arenas**: the arena data model, walls and collision, floor zones, the
  arena-change operations, camera framing, and arena forming and retracting.
- **The boss framework**: one sim module per boss over shared building
  blocks (telegraph shapes, projectiles, beat scheduling, the existing moves),
  optional phases, multi-body bosses, boss-hued effect roles, the shared
  boss-look shader with parameters, per-boss sound banks, `champions boss
  review <id>`, and the **fairness harness** that each boss's tests use. Landed
  in ps-8z4.3; its guide for the fight beads is
  [evidence/boss-rush/framework](../../rhythm_champions/evidence/boss-rush/framework/README.md).
- **Act kits**: one environment kit per act.
- **Per boss**: a mesh bead (the monster-mesh-design skill) and a fight bead
  (attacks, arena, effects, sound, tests, evidence). Whichever of the two
  lands first registers the boss's vendor source in `manifest.json` and
  `monsters.json` (clip roles, bake, `champions monster review`); the other
  rebases onto it. There is no separate intake step.
- **Pacing**: deterministic runs through all 25 fights with the reference
  builds, tuning durations, the XP curve and the build ceiling.
- **The final Warden.**

Sim rules stay in the pure C# sim and its dotnet tests. Each fight's evidence
goes in `rhythm_champions/evidence/boss-rush/<boss>/`. Every check only the
operator can make (hand-play difficulty, the look) gets a row in
`rhythm_champions/evidence/operator-pending.md`; no bead waits for the
operator.

## Beads

Filed as a **light** Enact epic (Executor beads, one final Warden), built
for parallelism. Merge conflicts on shared files (`manifest.json`,
`monsters.json`, `bossrush.json`, `sounds.json`) are expected and resolved
on rebase. They are no reason to chain beads.

- **Start immediately, in parallel**: the mode shell, boss progression,
  arenas, the boss framework, the five act kits and all 25 mesh beads.
- **Fight beads** wait only on **arenas** and **the boss framework**. They
  then run all at once. A fight uses its act's kit if it has landed and
  placeholder props otherwise; the kit bead dresses whichever of its act's
  arenas exist by then, and pacing finishes the rest. A fight is reachable
  through `--boss N` and `champions boss review` without the mode shell.
- **The Titan** also waits on the five fights it remixes: the Bloom,
  Sandwyrm, Watcher, Magma Heart and Telekinetic.
- **Pacing** waits on every fight, the mode shell and boss progression; the
  **Warden** on everything.

**Playable early, bosses in order.** Enact takes the highest priority first,
then the lowest bead number. So:

- The mode shell, arenas and the boss framework are **P1** and filed first
  (ps-8z4.1–.3). The shell is playable from the start, with the main game's
  three bosses standing in for unlanded slots, and each fight replaces its
  stand-in as it lands.
- Everything else is **P2**, filed in boss order: progression (.4), then
  per act its kit and each boss's fight followed by its mesh. Act 1 is
  .5–.15, act 2 .16–.26, act 3 .27–.37, act 4 .38–.48 and act 5 .49–.59.
  Earlier bosses' fights therefore win free slots once the foundations land.
- Pacing is .60, the Warden .61. `bd list --parent ps-8z4` shows them all.

## Taking bosses to the main game

A boss proven here moves to the main game by adopting its sim module, mesh,
sound bank and effects as they are. What has to change:

- its arena becomes a short set-piece on the endless grid, or the main game
  gains bounded boss arenas;
- its XP shedding is replaced by the main game's reinforcement budget;
- its health is rescaled to the main game's 3/6/9-minute builds.

None of this is planned yet. It's a reason to keep each boss self-contained.

## Play-testing questions

Hand-play questions for the operator, not claims:

- Is the 2–3 minute mean right, or does a 25-fight run want shorter fights?
- Do two level-ups per boss feel like real progress?
- Does each boss feel distinct within ten seconds of arrival?
- Are the dash waves satisfying rather than punishing at fast tempos?
- Is act 5 hard but fair with a full build on screen?
- Does the act checkpoint feel generous enough, or too generous?
