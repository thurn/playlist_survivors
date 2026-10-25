# Appendix: Damage Versus Enemy Health

Part of [Vampire Survivors: Numerical Progression](progression.md). This
appendix compares how fast the player's damage grows with how fast enemy
health grows, using Mad Forest as the reference stage.

All damage and HP figures are in internal units. The game shows both ×10, so
a Whip hit of 1 appears as 10 and a Big Mummy with 50 HP appears as 500.

## The two formulas

Player damage (`Weapon.PPower`, `Weapon.StandardCritical`):

```text
hit    = weapon power × Might
crit   = hit × critMul, with chance critChance × Luck
volley = amount hits every (interval × Cooldown) ms
```

Enemy maximum HP, fixed when the enemy spawns (`EnemyController.OnRecycleEnemy`):

```text
normal:        maxHp × stage HP × Curse × time HP
level-scaled:  maxHp × player level × stage HP × time HP
```

On Mad Forest with no Curse, stage HP and time HP are both 1. A normal enemy
therefore has exactly its `maxHp`, and a level-scaled enemy has `maxHp` ×
the player's level.

## Enemy health over time

### The Mad Forest schedule

Each minute sets a roster, a minimum number of enemies to keep alive, and a
spawn tick. "HP pool" is the total HP of the minimum crowd, assuming the
roster's enemies appear in equal numbers. `L` is the player's level.

| Minute | Minimum alive | Tick (ms) | Enemies (raw HP) | Bosses (raw HP) | HP pool at minimum |
|---|---|---|---|---|---|
| 0 | 15 | 1000 | BAT3 (0.5) |  | 7.5 |
| 1 | 30 | 1000 | ZOMBIE (1), BAT1 (0.1) | BAT4 (5×L) | 16.5 |
| 2 | 50 | 500 | BAT1 (0.1), BAT2 (0.5), BAT3 (0.5) |  | 18.3 |
| 3 | 40 | 250 | SKELETON (1.5) | BAT4 (5×L) | 60 |
| 4 | 30 | 500 | SKELETON (1.5), GHOST (1) |  | 37.5 |
| 5 | 10 | 1000 | MUDMAN2 (15) | XLMANTIS (50) | 150 |
| 6 | 20 | 500 | ZOMBIE (1), MUDMAN2 (15) |  | 160 |
| 7 | 80 | 500 | BAT2 (0.5), BAT3 (0.5), MUDMAN1 (7) | BAT4 (5×L) | 213.3 |
| 8 | 100 | 1500 | ZOMBIE (1) | XLBAT (27) | 100 |
| 9 | 30 | 500 | XLBAT (27), ZOMBIE (1) | BAT5 (5×L) | 420 |
| 10 | 10 | 500 | MUDMAN1 (7), MUDMAN2 (15) | BOSS_XLMANTIS (15×L) | 110 |
| 11 | 300 | 100 | SKELETON (1.5) |  | 450 |
| 12 | 20 | 250 | WEREWOLF (18), GHOST (1), SKELETON (1.5) | BAT4 (5×L) | 136.7 |
| 13 | 150 | 500 | WEREWOLF (18), GHOST (1), GHOST (1) |  | 1000 |
| 14 | 20 | 100 | XLBAT (27), WEREWOLF (18) | BAT5 (5×L) | 450 |
| 15 | 100 | 100 | WEREWOLF (18), XLBAT (27), MUDMAN2 (15) | BOSS_WEREWOLF (20×L) | 2000 |
| 16 | 100 | 100 | XLMANTIS (50), MUDMAN1 (7), MUDMAN2 (15) | BAT4 (5×L) | 2400 |
| 17 | 20 | 1000 | XLMUMMY (50) |  | 1000 |
| 18 | 60 | 500 | XLMUMMY (50), MUDMAN1 (7) | BAT5 (5×L) | 1710 |
| 19 | 100 | 500 | XLMUMMY (50), MUDMAN1 (7) |  | 2850 |
| 20 | 100 | 100 | XLMUMMY (50), MUDMAN2 (15), XLBAT (27) | BOSS_XLMUMMY (25×L) | 3066.7 |
| 21 | 300 | 100 | FLOWER (3×L) | XLFLOWER (50) | 900×L |
| 22 | 200 | 100 | FLOWER (3×L), XLMUMMY (50) | BAT4 (5×L) | 5000 + 300×L |
| 23 | 300 | 100 | FLOWER (3×L), XLMUMMY (50) | BAT5 (5×L) | 7500 + 450×L |
| 24 | 300 | 100 | FLOWER (3×L), XLMUMMY (50) | XLFLOWER (50) | 7500 + 450×L |
| 25 | 100 | 100 | XLFLOWER (50) | BOSS_XLFLOWER (15×L) | 5000 |
| 26 | 150 | 100 | XLFLOWER (50), FLOWER (3×L) |  | 3750 + 225×L |
| 27 | 300 | 100 | XLMUMMY (50), MUDMAN1 (7), MUDMAN2 (15) | BAT4 (5×L) | 7200 |
| 28 | 300 | 100 | XLBAT (27), BAT4 (5×L) |  | 4050 + 750×L |
| 29 | 300 | 100 | BAT4 (5×L), BAT5 (5×L) | BAT4 (5×L) | 1500×L |
| 30 | 1 | 10000 |  | BOSS_XLDEATH (65535×L) |  |

Three things stand out:

- **Per-enemy HP grows about 100×**, from 0.5 (bats) to 50 (Big Mummy,
  Venus, Mantichana) by minute 16.
- **The crowd grows about 20×**, from 15 to 300. Together the HP pool grows
  about 1000×, from 7.5 to about 7,500.
- **The curve is not smooth.** Quiet minutes (5, 10, 17) with a few tough
  enemies alternate with swarm minutes (11, 13, 21-29). Minute 8 brings 100
  zombies with 1 HP each, a breather after minute 7.

### The enemies

| Id | Name | Raw max HP | HP × player level | Contact damage | Speed | xp |
|---|---|---|---|---|---|---|
| BAT1 | Pipeestrello | 0.1 |  | 5 | 140 | 1 |
| BAT2, BAT3 | (bats) | 0.5 |  | 5 | 140 | 1 |
| ZOMBIE | Zombie | 1 |  | 10 | 100 | 1 |
| GHOST | Ghost | 1 |  | 5 | 200 | 1.5 |
| SKELETON | Skeleton | 1.5 |  | 10 | 100 | 2 |
| MUDMAN1 | Mudman | 7 |  | 10 | 100 | 2.5 |
| MUDMAN2 | (big mudman) | 15 |  | 10 | 100 | 2.5 |
| WEREWOLF | Werewolf | 18 |  | 14 | 130 | 2 |
| XLBAT | Giant Bat | 27 |  | 10 | 140 | 2.5 |
| XLMANTIS | Mantichana | 50 |  | 20 | 80 | 3 |
| XLMUMMY | Big Mummy | 50 |  | 20 | 80 | 3 |
| XLFLOWER | Venus | 50 |  | 20 | 80 | 3 |
| FLOWER | Flower Wall | 3 | yes | 1 | 20 | 2 |
| BAT4, BAT5 | (boss bats) | 5 | yes | 10 | 140 | 30 |
| BOSS_XLMANTIS | (boss) | 15 | yes | 20 | 160 | 50 |
| BOSS_WEREWOLF | (boss) | 20 | yes | 20 | 130 | 30 |
| BOSS_XLMUMMY | (boss) | 25 | yes | 20 | 80 | 25 |
| BOSS_XLFLOWER | (boss) | 15 | yes | 30 | 160 | 50 |
| BOSS_XLDEATH | The Reaper | 65535 | yes | 65535 | 1200 | 0 |

## Player damage over time

### One weapon

The Whip's damage per hit grows 4× over its eight levels, and its amount
doubles. Might multiplies every hit:

| Might source | Bonus | Running total |
|---|---|---|
| Base | | 1.0 |
| Spinach, 5 levels | +0.5 | 1.5 |
| Shop Might, 5 ranks | +0.25 | 1.75 |
| Antonio at level 50 | +0.5 | 2.25 |

That gives a ladder of per-hit damage values:

| Damage per hit | Example |
|---|---|
| 1 | Whip level 1 |
| 2 | Fire Wand level 1 |
| 4 | Whip level 8 |
| 6 | Whip level 8 with Might 1.5 |
| 9 | Whip level 8 with Might 2.25, or Fire Wand level 8 |
| 13.5 | Fire Wand level 8 with Might 1.5 |

### Hits to kill

Hits needed to kill each normal enemy at each rung of the ladder:

| Enemy | Raw HP | 1 per hit | 2 per hit | 4 per hit | 6 per hit | 9 per hit | 13.5 per hit |
|---|---|---|---|---|---|---|---|
| Pipeestrello | 0.1 | 1 | 1 | 1 | 1 | 1 | 1 |
| Bat | 0.5 | 1 | 1 | 1 | 1 | 1 | 1 |
| Zombie | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| Ghost | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| Skeleton | 1.5 | 2 | 1 | 1 | 1 | 1 | 1 |
| Mudman | 7 | 7 | 4 | 2 | 2 | 1 | 1 |
| Big mudman | 15 | 15 | 8 | 4 | 3 | 2 | 2 |
| Werewolf | 18 | 18 | 9 | 5 | 3 | 2 | 2 |
| Giant Bat | 27 | 27 | 14 | 7 | 5 | 3 | 2 |
| Big Mummy | 50 | 50 | 25 | 13 | 9 | 6 | 4 |

The game is tuned so that nothing in the first five minutes has more than
1.5 HP: one or two hits from a level-1 weapon (three for the Knife's 0.65).
A level-1 weapon cannot handle the minute-5 big mudman (15 hits),
which forces the player to have leveled by then. By minute 16 the 50-HP
enemies take 4 to 13 hits even from a strong single weapon. They are meant to
be handled by several weapons hitting at once.

### The whole build

Single-target damage per hit grows only about 9 to 14×. The rest of the gap
comes from multipliers that stack:

| Multiplier | Source | Factor on throughput |
|---|---|---|
| Weapon levels | Whip level 1 → 8 (power 1 → 4, amount 1 → 2) | 8× |
| Might | 1.0 → 2.25 | 2.25× |
| Cooldown | Empty Tome −40%, shop −5% → interval × 0.55 | 1.8× |
| Amount | Duplicator +2, shop +1 → Whip amount 2 → 5 | 2.5× |
| More weapons | up to six | up to 6× |
| Area, pierce, duration | Candelabrador, evolutions | more targets per hit |

The Whip alone goes from 0.74 to about 80 raw damage per second
(8 × 2.25 × 1.8 × 2.5 ≈ 82×). With six weapons and area effects that hit
many enemies at once, the build's total output grows by well over 1000×,
which matches the HP pool's growth.

Raw throughput (power × amount ÷ interval) for the twelve base weapons, at
level 1, level 8 and evolved:

| Weapon | Level 1 | Throughput | Level 8 | Throughput | Evolution | Evolved | Throughput |
|---|---|---|---|---|---|---|---|
| Whip | 1 × 1 / 1.35s | 0.74 | 4 × 2 / 1.35s | 5.93 | Bloody Tear | 4 × 2 / 1.35s | 5.93 |
| Magic Wand | 1 × 1 / 1.2s, pierce 1 | 0.83 | 3 × 4 / 1s, pierce 2 | 12 | Holy Wand | 3 × 4 / 0.5s, pierce 2 | 24 |
| Knife | 0.65 × 1 / 1s, pierce 1 | 0.65 | 1.65 × 6 / 1s, pierce 3 | 9.9 | Thousand Edge | 1.65 × 6 / 0.35s, pierce 3 | 28.29 |
| Axe | 2 × 1 / 4s, pierce 3 | 0.5 | 8 × 3 / 4s, pierce 7 | 6 | Death Spiral | 6 × 9 / 4s, pierce 1000 | 13.5 |
| Cross | 0.5 × 1 / 2s | 0.25 | 3.5 × 3 / 2s | 5.25 | Heaven Sword | 7.7 × 1 / 3.3s | 2.33 |
| King Bible | 1 × 1 / 3s | 0.33 | 3 × 4 / 3s | 4 | Unholy Vespers | 3 × 4 / 3s | 4 |
| Fire Wand | 2 × 3 / 3s | 2 | 9 × 3 / 3s | 9 | Hellfire | 10 × 2 / 3s | 6.67 |
| Garlic | 0.5 × 1 / 1.3s | 0.38 | 1.5 × 1 / 1s | 1.5 | Soul Eater | 2 × 1 / 1s | 2 |
| Santa Water | 1 × 1 / 4.5s | 0.22 | 4 × 4 / 4.5s | 3.56 | La Borra | 4 × 4 / 4s | 4 |
| Runetracer | 1 × 1 / 3s | 0.33 | 3 × 3 / 3s | 3 | NO FUTURE | 3 × 1 / 1s | 3 |
| Lightning Ring | 1.5 × 2 / 4.5s | 0.67 | 6.5 × 6 / 4.5s | 8.67 | Thunder Loop | 6.5 × 6 / 4.5s | 8.67 |

Throughput is only a rough guide. It ignores crits, area, pierce, how long
an effect lingers (Garlic, Santa Water and King Bible hit every enemy inside
them), and whether projectiles hit anything at all. It does show that
evolutions often keep or even lower raw throughput (Heaven Sword, Hellfire)
and win instead through faster intervals, unlimited pierce, bigger area or
crits. Pentagram deals no damage; it clears the screen.

## Level-scaled bosses

Boss HP grows with the player's level instead of the clock:

| Enemy | HP per level | Level 10 | Level 20 | Level 30 | Level 40 | Level 60 |
|---|---|---|---|---|---|---|
| BAT4 | 5 | 50 | 100 | 150 | 200 | 300 |
| BOSS_XLMANTIS | 15 | 150 | 300 | 450 | 600 | 900 |
| BOSS_WEREWOLF | 20 | 200 | 400 | 600 | 800 | 1200 |
| BOSS_XLMUMMY | 25 | 250 | 500 | 750 | 1000 | 1500 |
| FLOWER | 3 | 30 | 60 | 90 | 120 | 180 |
| BOSS_XLFLOWER | 15 | 150 | 300 | 450 | 600 | 900 |

This works as automatic difficulty adjustment. A player who levels quickly
has more damage and meets tougher bosses. A player who falls behind meets
softer ones. Level-scaled HP ignores Curse, so Curse makes the crowd tougher
but not the bosses.

The flower walls in minutes 21-26 also scale with level. Late in a run the
swarm itself grows with the player, which keeps the last third of the stage
from becoming trivial.

## Difficulty multipliers

These multiply enemy health or numbers on top of the schedule:

| Multiplier | Enemy HP | Enemy count and spawn rate | Other |
|---|---|---|---|
| Curse 1.5 (Skull O'Maniac maxed) | ×1.5, normal enemies only | minimum ×1.5, tick ÷1.5 | enemy speed ×1.5 |
| Curse 2.0 (plus shop Curse) | ×2, normal enemies only | minimum ×2, tick ÷2 | enemy speed ×2 |
| Green Acres, Boss Rash | ×1.5 | | |
| The Bone Zone | ×(1 + 0.15 per minute), 5.5× at minute 30 | | |
| Inverse mode | ×3, then ×(1 + 0.05 per minute), 7.5× at minute 30 | | gold ×3 |
| Endless, per 30-minute cycle | +1 to the time multiplier | minimum +1× Curse, tick +0.5× Curse | damage taken +25% |

Curse is the player's own difficulty dial. It raises enemy HP, count and
speed together. Since more enemies drop more gems, it also speeds up leveling,
which raises the level-scaled bosses in turn.

## Takeaways

- **Enemy toughness and enemy count both grow, and they multiply.** The HP
  on screen grows about 1000× over 30 minutes.
- **No single player stat grows that fast.** The player keeps pace only by
  stacking multipliers: weapon levels, Might, Cooldown, Amount, more weapons,
  and area.
- **The pacing is lumpy on purpose.** Minutes with a few tough enemies test
  single-target damage. Swarm minutes test area and pierce.
- **Bosses track the player, not the clock.** Their HP is a fixed amount per
  player level, so a boss is roughly equally hard whenever it appears.
