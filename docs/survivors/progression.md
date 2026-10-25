# Vampire Survivors: Numerical Progression

## Introduction

This document describes how the numbers in Vampire Survivors grow, both inside
one run and across many runs. Every figure comes from the game itself, not
from a wiki:

- **Data tables.** The game ships its balance data as JSON text assets
  (`WEAPON_DATA`, `ENEMY_DATA`, `STAGE_DATA`, `CHARACTER_DATA`,
  `POWERUP_DATA`, `ITEM_DATA` and others). They were extracted with UnityPy.
- **Formulas.** The game logic lives in `VampireSurvivors.Runtime.dll`, a
  Mono assembly with no obfuscation. It was decompiled with ILSpy. Class and
  method names below refer to that decompiled code.
- **Version.** Steam build 1.16.111 on macOS, files dated 2026-10-21.

The appendices go deeper on three topics:

- [Damage versus enemy health](appendix-damage-vs-health.md)
- [Experience and leveling](appendix-xp-and-leveling.md)
- [Gold and meta costs](appendix-gold-economy.md)

### Units

The tables use the game's internal units:

- **Damage and enemy HP are shown ×10 on screen.** A Whip has `power: 1`
  in the data and shows 10 damage. A Zombie has `maxHp: 1` and takes one Whip
  hit. The weapon recap screen multiplies by 10 (`WeaponRecapUI`), and so do
  the damage numbers.
- **Player HP and enemy contact damage are not scaled.** Antonio has 120 HP
  and a bat hits for 5.
- **Character stats are multipliers that start at 1.** Might 1.0 means 100%
  damage. A Spinach adds 0.1, so Might becomes 1.1.
- **Times are milliseconds** unless a table says otherwise.

## How stats combine

A character's stats are the sum of four layers:

1. The character's base values from `CHARACTER_DATA` (for example Antonio's
   120 HP and 1 armor).
2. Shop power-ups bought with gold, applied at the start of each run
   (`GameManager.ApplyPurchasedPowerUpData`).
3. Character level bonuses gained during the run (`CharacterController.LevelUp`).
4. Passive items picked up during the run (`Accessory`).

Everything adds, with one exception: **Max HP bonuses multiply.** Shop
power-ups and Hollow Heart both call `PlayerStatsUpgrade` with
`multiplicativeMaxHp: true`, so each +20% Hollow Heart rank applies to the
current maximum and compounds. Everything else, including Might, adds to the
multiplier: five Spinach ranks plus five shop Might ranks give
1 + 0.5 + 0.25 = 1.75, not 1.5 × 1.25.

Weapons read the final stats:

```text
damage per hit = weapon power × Might + bloodline bonus
crit hit       = damage × critMul, with chance critChance × Luck
interval       = weapon interval × Cooldown
amount         = weapon amount + Amount
```

The code clamps several stats:

| Stat | Clamp | Where |
|---|---|---|
| Might | at most 10 | `CharacterController.PPowerFinal` |
| Area | at most 10 | `PAreaFinal` |
| Amount bonus | at most 10 | `Weapon.PAmount` |
| Projectile speed | at most 5 | `Weapon.PSpeed` |
| Duration | at most 5 | `Weapon.PDuration` |
| Cooldown | at least 0.1 | `PCooldownFinal` |
| Armor | at most 50 | `PArmor` |

## Within a run

A standard stage lasts 30 minutes. The clock drives everything: each minute
the stage swaps in a new enemy roster, spawn rate and boss list. The player
answers by leveling up, which buys weapon and passive levels.

### The spawn schedule

Each stage has one `STAGE_DATA` entry per minute. Each entry sets:

- **`enemies`**: the roster for that minute.
- **`minimum`**: how many enemies the stage keeps alive.
- **`frequency`**: the spawn tick in milliseconds.
- **`bosses`**: spawned once at the start of the minute, often carrying a
  treasure chest.
- **`events`**: bat swarms, ghost swarms, flower walls.

The spawner (`Stage.HandleSpawning`, `SwarmCheck`) works like this:

- On every tick it spawns one of each enemy in the roster.
- If fewer than `minimum × Curse` enemies are alive, it keeps spawning whole
  rosters until the count reaches that number.
- The tick interval is `frequency / Curse`, so Curse speeds up spawning too.
- At most 300 enemies are alive at once (`_defaultMaximum`). The Charm shop
  power-up raises both the minimum and the cap.
- Bosses, events and treasure reset every minute. The roster, minimum and
  tick carry over when a minute omits them.

Mad Forest's schedule shows the shape. The full table, with every enemy's HP,
is in [the damage appendix](appendix-damage-vs-health.md#the-mad-forest-schedule).

| Minute | Minimum alive | Tick (ms) | Roster |
|---|---|---|---|
| 0 | 15 | 1000 | small bats |
| 3 | 40 | 250 | skeletons |
| 5 | 10 | 1000 | big mudmen |
| 11 | 300 | 100 | skeletons |
| 15 | 100 | 100 | werewolves, giant bats, big mudmen |
| 20 | 100 | 100 | big mummies, big mudmen, giant bats |
| 23 | 300 | 100 | flower walls, big mummies |
| 29 | 300 | 100 | level-scaled bats |
| 30 | 1 | 10000 | The Reaper |

Two things grow with the clock: how many enemies are alive (15 → 300) and how
tough each one is (0.5 HP → 50 HP).

At minute 30 the Reaper spawns. It has 65,535 HP per player level and deals
65,535 damage, so it ends the run.

### Enemy health

An enemy's maximum HP is set when it spawns (`EnemyController.OnRecycleEnemy`):

```text
normal enemy:        maxHp × stage HP × Curse × time HP
level-scaled enemy:  maxHp × player level × stage HP × time HP
```

- **Stage HP** is the stage's `EnemyHealthMultiplier`. It is 1 on most
  stages and 1.5 on Green Acres and Boss Rash.
- **Time HP** starts at 1 and grows each minute on stages with `TimeMods`.
  The Bone Zone adds 0.15 per minute, reaching 5.5× by minute 30. Every
  stage in Inverse mode adds 0.05 per minute.
- **Level-scaled enemies** have the `HP_x_Level` skill. Their HP is
  multiplied by the player's level and **ignores Curse**. Most bosses and the
  flower walls are level-scaled.
- **Endless mode** adds 1 to the time HP multiplier each time the 30-minute
  cycle wraps (`Stage.OnCycleComplete`). It also raises spawn counts and the
  damage the player takes.

### Enemy damage and player health

Enemies deal their `power` on contact (`PhysicsManager`):

```text
damage taken = max(1, enemy power − Armor)
```

Endless mode multiplies the damage by 1 + 0.25 per completed cycle before
armor applies. Armor is flat, so it matters most against weak enemies: 5
Armor halves a 10-damage hit but barely dents a 30-damage boss.

| Source | Effect |
|---|---|
| Character base | 50–150 HP (Antonio 120, Poe 70, Clerici 150) |
| Hollow Heart | +20% Max HP per level, compounding, 5 levels |
| Shop Max Health | +10% per rank, 3 ranks |
| Pummarola | +0.2 HP/s per level, 5 levels |
| Shop Recovery | +0.1 HP/s per rank, 5 ranks |
| Armor (passive) | −1 damage per level, 5 levels |
| Shop Armor | −1 damage per rank, 3 ranks |
| Floor Chicken | heals 30 HP |
| Little Heart | heals 1 HP |
| Tiragisú | revives once at 50% HP, 2 levels |

Contact damage grows from 5 (bats) to 10 (zombies, skeletons, mudmen) to 20
(big mummies, mantichana) and 30 (the Venus boss). Maximum HP grows by up to
2.5× from Hollow Heart alone (1.2⁵ ≈ 2.49). Survival therefore depends on
killing enemies before they arrive, more than on outlasting them.

### Experience

Enemies drop XP gems worth their `xp` value, randomized from 0.5× to 1.5× and
rounded down (`EnemyController.GiveReward`). The XP gained from a gem is:

```text
gem value × Growth × stage XP bonus
```

The level-up threshold (`LevelUpFactory.CalculateXpFactor`) is quadratic:

```text
total XP to leave level L = factor × L²
factor = 5 for levels 1-19, 6.5 for levels 20-39, 8 from level 40
```

| Level | XP to finish this level |
|---|---|
| 1 | 5 |
| 10 | 95 |
| 19 | 185 |
| 20 | 795 |
| 21 | 266 |
| 39 | 500 |
| 40 | 2913 |
| 41 | 648 |
| 60 | 952 |

The jumps at levels 20 and 40 come from the factor change, which reprices
the whole L² total at once. Every base character gets +100% Growth while at
levels 20 and 40 (a `growth: 1` entry at level 20, removed at 21), which
halves those spikes. See [the XP appendix](appendix-xp-and-leveling.md) for
the full curve, gem values and kills per level.

### Weapons

Each weapon has eight levels in `WEAPON_DATA`. Level 1 holds the full stat
block, and each later level lists only what it adds: numeric fields add,
other fields replace (`DataHelper.UpgradeJsonData`). The Whip:

| Level | Power | Amount | Area | Interval (ms) |
|---|---|---|---|---|
| 1 | 1 | 1 | 1 | 1350 |
| 2 | 1 | 2 | 1 | 1350 |
| 3 | 1.5 | 2 | 1 | 1350 |
| 4 | 2 | 2 | 1.1 | 1350 |
| 5 | 2.5 | 2 | 1.1 | 1350 |
| 6 | 3 | 2 | 1.2 | 1350 |
| 7 | 3.5 | 2 | 1.2 | 1350 |
| 8 | 4 | 2 | 1.2 | 1350 |

From level 1 to level 8 a base weapon's raw throughput (power × amount ÷
interval) grows about 4 to 20 times. The Whip grows 8×, the Magic Wand 14×,
the Fire Wand 4.5×:

| Weapon | Level 1 | Level 8 | Evolution | Evolved |
|---|---|---|---|---|
| Whip | 1 × 1 / 1.35s | 4 × 2 / 1.35s | Bloody Tear | 4 × 2 / 1.35s, crits, heals |
| Magic Wand | 1 × 1 / 1.2s | 3 × 4 / 1s | Holy Wand | 3 × 4 / 0.5s |
| Knife | 0.65 × 1 / 1s | 1.65 × 6 / 1s | Thousand Edge | 1.65 × 6 / 0.35s |
| Axe | 2 × 1 / 4s | 8 × 3 / 4s | Death Spiral | 6 × 9 / 4s, pierces all |
| Fire Wand | 2 × 3 / 3s | 9 × 3 / 3s | Hellfire | 10 × 2 / 3s, pierces |
| Lightning Ring | 1.5 × 2 / 4.5s | 6.5 × 6 / 4.5s | Thunder Loop | 6.5 × 6 / 4.5s, strikes twice |

An evolution needs the weapon at level 8 and its paired passive item, and is
taken from a treasure chest. Evolutions rarely add raw power. They shorten the
interval, add pierce or area, or add a mechanic such as healing.

### Passive items

A run allows six weapons and six passive items (`_maxWeaponCount`, `_maxAccessoryCount`). Each passive levels up to
five times (Duplicator and Tiragisú twice):

| Passive | Per level | At max | Evolves |
|---|---|---|---|
| Spinach | +10% Might | +50% | Hellfire |
| Armor | −1 damage taken | −5 | NO FUTURE |
| Hollow Heart | +20% Max HP, compounding | ×2.49 | Bloody Tear |
| Pummarola | +0.2 HP/s | +1 HP/s | Soul Eater |
| Empty Tome | −8% cooldown | −40% | Holy Wand |
| Candelabrador | +10% area | +50% | Death Spiral |
| Bracer | +10% projectile speed | +50% | Thousand Edge |
| Spellbinder | +10% duration | +50% | Unholy Vespers |
| Duplicator | +1 amount | +2 | Thunder Loop |
| Wings | +10% move speed | +50% | Valkyrie Turner |
| Attractorb | +50%, 33%, 25%, 20%, 33% pickup radius | +161% | La Borra |
| Clover | +10% luck | +50% | Heaven Sword |
| Crown | +8% growth | +40% | Gorgeous Moon |
| Stone Mask | +10% greed | +50% | Vicious Hunger |
| Skull O'Maniac | +10% curse | +50% | Mannajja |
| Tiragisú | +1 revival | +2 | Phieraggi |

### Treasure chests

Bosses carry chests. A chest opens at tier 1, 2 or 3: 1, 3 or 5 items, plus
gold. The minute's `treasure.chances` gives the percentage for each tier,
multiplied by Luck (`Stage` around line 2280). In Mad Forest the chances grow
from `[0, 0, 30]` at minute 1 to `[3, 10, 100]` by minute 10.

| Tier | Items | Gold before Greed |
|---|---|---|
| 1 | 1 | 100–199 |
| 2 | 3 | 300–599 |
| 3 | 5 | 500–999 |

A chest item levels up a weapon or passive the player already owns, or
evolves a weapon that qualifies. If there is nothing left to level, the slot
becomes gold.

### Character levels

Each character has its own per-level bonuses in `CHARACTER_DATA`. They are
the main reason characters feel different late in a run:

- Antonio: +10% Might every 10 levels, up to +50% at level 50.
- Imelda: +10% Growth at levels 5, 10 and 15.
- Arca: −5% cooldown every 10 levels, up to −15%.
- Pugnala, Giovanna, Poppea, Concetta: +1% of one stat every level, without
  a cap.
- Mortaccio, Cavallo, Ramba, O'Sole: +1 amount every 20 levels.

## Across runs

Gold is the only currency that carries from run to run. Runs earn gold, and
the gold buys permanent shop power-ups and new characters. Stage modes such as
Hyper and Inverse raise the difficulty and the gold rewards.

### Earning gold

Every gold source goes through one formula (`PlayerOptions.AddCoins`):

```text
gold gained = value × stage gold multiplier × Greed
```

| Source | Base value |
|---|---|
| Gold Coin | 1 |
| Coin Bag | 10 |
| Big Coin Bag | 25 |
| Rich Coin Bag | 100 |
| Treasure chest | 100–999 by tier |
| Weapon at max level picked again | 10 |
| Completing a stage | 500 |

Gold collected during a run is kept even if the player dies. See
[the gold appendix](appendix-gold-economy.md) for the full economy.

### The power-up shop

Each shop power-up has a base price and a number of ranks. A purchase costs
(`PlayerStats.GetPrice`):

```text
price = base price × (1 + ranks owned of this power-up)
      + floor(20 × 1.1 ^ total ranks owned across all power-ups)
```

The first purchase has no markup. The markup grows 10% with every rank of
anything, so the order of purchases does not change the total.

| Power-up | Per rank | Ranks | Base price |
|---|---|---|---|
| Might | +5% | 5 | 200 |
| Armor | −1 damage | 3 | 600 |
| Max Health | +10% | 3 | 200 |
| Recovery | +0.1 HP/s | 5 | 200 |
| Cooldown | −2.5% | 2 | 900 |
| Area | +5% | 2 | 300 |
| Speed | +10% | 2 | 300 |
| Duration | +15% | 2 | 300 |
| Amount | +1 | 1 | 5000 |
| Move Speed | +5% | 2 | 300 |
| Magnet | +25% | 2 | 300 |
| Luck | +10% | 3 | 600 |
| Growth | +3% | 5 | 900 |
| Greed | +10% | 5 | 200 |
| Curse | +10% | 5 | 1666 |
| Revival | +1 | 1 | 10000 |
| Omni | +2% Might, Speed, Duration, Area | 5 | 1000 |
| Charm | +20 enemies | 5 | 10000 |
| Defang | 3% of enemies harmless | 5 | 10 |
| Reroll | +2 | 5 | 500 |
| Skip | +2 | 5 | 100 |
| Banish | +2 | 5 | 100 |
| Recycle | 10% to keep a reroll, skip or banish | 5 | 500 |

The 83 ranks cost 806,263 gold in total. Base prices are 261,240 of that, and
the markup is 545,023.

The shop's gains are modest next to what one run provides. Maxed shop Might
is +25%, while five Spinach levels in a run give +50%. Shop Max Health is
+30%, while Hollow Heart gives +149%. The shop raises the floor at the start
of a run more than the ceiling at its end.

### Characters

A character costs its base price × (1 + 0.1 × characters bought so far,
excluding Antonio) (`CharacterItemUI.CharMarkup`).

| Character | Price | Starts with | Max HP | Notable base stats |
|---|---|---|---|---|
| Antonio | free | Whip | 120 | 1 Armor |
| Imelda | 10 | Magic Wand | 100 | |
| Pasqualina | 100 | Runetracer | 100 | Speed 1.1 |
| Gennaro | 500 | Knife | 120 | +1 Amount |
| Arca | 500 | Fire Wand | 100 | Might 1.1 |
| Porta | 500 | Lightning Ring | 100 | Area 1.3, Cooldown 0.1 rising to 1 by level 4 |
| Lama | 500 | Axe | 110 | Might, Move Speed, Curse 1.1 |
| Poe | 500 | Garlic | 70 | +25% Magnet |
| Clerici | 500 | Santa Water | 150 | 0.5 Regen, Area 5 falling to 1 by level 5 |
| Dommario | 500 | King Bible | 100 | Speed and Duration 1.4, Move 0.6 |
| Krochi | 500 | Cross | 100 | Move 1.3, 1 Revival |
| Christine | 500 | Pentagram | 50 | Might 0.65, Cooldown 0.75 |
| Mortaccio | 500 | Bone | 100 | |
| Pugnala | 1000 | Phiera Der Tuphello | 100 | Move 1.2 |
| Giovanna | 1000 | Gatti Amari | 100 | Move 1.2 |
| Poppea | 1000 | Song of Mana | 100 | Move 1.2 |
| Concetta | 1000 | Shadow Pinion | 100 | Move 1.2, Curse 1.1 |
| Gallo | 2000 | Clock Lancet | 100 | Cooldown 0.85, Greed 0.5 |
| Divano | 2000 | Laurel | 100 | 1 Regen, Luck 1.3, Greed 0.5 |
| Zi'Assunta | 5000 | Vento Sacro | 100 | Move 1.2, Curse 1.1 |
| Exdash | 777 | Ebony Wings | 77 | Luck 2, most stats 0.9 |

### Stages and modes

Each stage sets base modifiers in `STAGE_DATA`. Hyper and Inverse add their
own values on top (`StageModifiers.Add`):

| Stage | Minutes | Enemy speed | Enemy HP | HP per minute | Gold | Hyper gold | Hyper enemy speed |
|---|---|---|---|---|---|---|---|
| Mad Forest | 30 | 1.1 | 1 | | 1 | 1.5 | 2 |
| Inlaid Library | 30 | 1.25 | 1 | | 1 | 1.5 | 1.9 |
| Dairy Plant | 30 | 1.25 | 1 | | 1.2 | 1.7 | 1.9 |
| Gallo Tower | 30 | 1.25 | 1 | | 1.3 | 1.8 | 1.9 |
| Cappella Magna | 30 | 1.4 | 1 | | 1.4 | 1.9 | 2 |
| Il Molise | 15 | 1.25 | 1 | | 1 | 1.5 | 2 |
| Moongolow | 15 | 1.35 | 1 | | 1 | 1.5 | 2 |
| Green Acres | 30 | 1.25 | 1.5 | | 1 | 1.5 | 1.9 |
| The Bone Zone | 30 | 1.25 | 1 | +0.15 | 1.5 | 2 | 1.9 |
| Boss Rash | 15 | 1.25 | 1.5 | | 1 | 1.5 | 1.9 |

- **Hyper** speeds up enemies, the player and projectiles, and adds +50% gold
  on most stages.
- **Inverse** adds +2 to enemy HP and gold, +0.2 Luck, and +0.05 enemy HP
  and +0.005 enemy speed every minute. The per-minute growth is a separate
  multiplier, so on Mad Forest enemies have 3× HP at the start and
  3 × 2.5 = 7.5× by minute 30, and gold is 3×.
- **Hurry** runs the stage clock twice as fast (`GameManager._targetTick`
  0.5) and adds +25% XP.

The gold multipliers rise with each stage, which is the main way later stages
pay for their difficulty.

### Other unlocks

The data holds more long-term systems. They are listed for completeness:

- **Arcanas** (`ARCANA_DATA`, 76 entries): run-wide rule changes, chosen at
  the start of a run or found in chests.
- **Limit Break** (`LIMITBREAK_DATA`): with Limit Break enabled, a weapon
  past level 8 keeps taking small repeating gains. The Whip gets +0.05 power,
  +0.025 area (up to 10 times) or +1 amount (rarity 1, up to 20 times).
- **Seals**: shop items that remove weapons or pickups from the level-up
  pool.
- **Merchant goods**: `ITEM_DATA` values +1 Revival at 10,000 gold, +1
  Reroll at 1,000, +1 Banish at 500 and +1 Skip at 100.

## Summary

- **Inside a run, enemies get tougher about 100× and more numerous about
  20× over 30 minutes.** The player keeps up through multiplication: weapon
  levels (4 to 20× each), Might, Cooldown, Amount and up to six weapons.
- **Bosses scale with the player's level, not the clock.** Leveling faster
  makes bosses tougher, but they never fall behind or race ahead.
- **XP needs grow with the square of the level**, with spikes at levels 20
  and 40 that the +100% Growth bonus partly cancels.
- **Gold flows in proportion to stage difficulty and Greed.** It buys a
  permanent floor of small bonuses whose price grows exponentially with every
  rank bought.
