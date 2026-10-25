# Appendix: Experience and Leveling

Part of [Vampire Survivors: Numerical Progression](progression.md). This
appendix covers how much XP each level costs, how much XP enemies give, and
what that means in kills.

## The level curve

`LevelUpFactory.CalculateXpFactor` sets the XP total the player must reach to
leave level `L`:

```text
total(L) = factor(L) × L²
factor(L) = min(5 + 1.5 × floor(L / 20), 8)
```

So the factor is 5 for levels 1-19, 6.5 for levels 20-39, and 8 from level 40
on. XP is never spent: the bar fills from `total(L − 1)` to `total(L)`.

Every base character also has two entries in `CHARACTER_DATA` that add
`growth: 1` at levels 20 and 40 and remove it at levels 21 and 41. While at
level 20 or 40, the character gains double XP. The last column divides the
level's XP by that bonus.

| Level | Factor | Total XP to leave level | XP for this level | Effective XP after the growth bonus |
|---|---|---|---|---|
| 1 | 5 | 5 | 5 | 5 |
| 2 | 5 | 20 | 15 | 15 |
| 3 | 5 | 45 | 25 | 25 |
| 4 | 5 | 80 | 35 | 35 |
| 5 | 5 | 125 | 45 | 45 |
| 10 | 5 | 500 | 95 | 95 |
| 15 | 5 | 1125 | 145 | 145 |
| 19 | 5 | 1805 | 185 | 185 |
| 20 | 6.5 | 2600 | 795 | 397 |
| 21 | 6.5 | 2866 | 266 | 266 |
| 25 | 6.5 | 4062 | 318 | 318 |
| 30 | 6.5 | 5850 | 383 | 383 |
| 39 | 6.5 | 9886 | 500 | 500 |
| 40 | 8 | 12800 | 2913 | 1456 |
| 41 | 8 | 13448 | 648 | 648 |
| 50 | 8 | 20000 | 792 | 792 |
| 60 | 8 | 28800 | 952 | 952 |
| 80 | 8 | 51200 | 1272 | 1272 |
| 100 | 8 | 80000 | 1592 | 1592 |

Inside each band, each level costs 2 × factor more XP than the one before:
10 more per level up to level 19, 13 more in levels 20-39, 16 more after
that. The cost per level grows linearly and the total grows with the square
of the level.

At levels 20 and 40 the factor change reprices the whole total at once. Level
20 alone costs 795 XP, more than four times level 19. The growth bonus halves
that to 397. Level 40 costs 2,913, which the bonus halves to 1,456, still
nearly three times level 39. The bonus softens the spikes but does not remove
them. Players feel levels 20 and 40 as slow, and afterwards the curve
continues from a steeper base.

Cumulative effective XP, counting the growth bonus:

| Reach level | Effective XP |
|---|---|
| 10 | 500 |
| 20 | 2,203 |
| 30 | 5,453 |
| 40 | 10,946 |
| 50 | 18,146 |
| 60 | 26,946 |

## XP drops

When an enemy dies, it drops one gem (`EnemyController.GiveReward`):

```text
gem value = floor((random 0..1 + 0.5) × enemy xp)
```

The value is uniform between 0.5× and 1.5× the enemy's `xp`, rounded down. If
it rounds to 0, no gem drops. The rounding costs about half a point per kill,
which matters a lot for weak enemies: an enemy with `xp: 1` drops a 1-point
gem only half the time.

| Enemy xp | Gem value range | Expected XP per kill | Examples |
|---|---|---|---|
| 1 | 0-1 | 0.5 | bats, zombies |
| 1.5 | 0-2 | 1 | ghosts |
| 2 | 1-2 | 1.5 | skeletons, werewolves, flower walls |
| 2.5 | 1-3 | 2 | mudmen, giant bats |
| 3 | 1-4 | 2.5 | big mummies, Venus, Mantichana |
| 25 | 12-37 | 24.5 | Big Mummy boss |
| 30 | 15-44 | 29.5 | boss bats, Werewolf boss |
| 50 | 25-74 | 49.5 | Mantichana and Venus bosses |

Gem colour follows its value (`Gem.SetValue`): blue for 1-2, green for 3-9,
red for 10 or more. At most 400 gems can lie on the ground. Past that, new
XP is added to the gem farthest from the player, which is why big red gems
appear in long runs (`GameManager.SpawnGems`).

Picking up a gem gives:

```text
XP gained = gem value × Growth × stage XP bonus
```

## Kills per level

Dividing a level's effective XP by the expected XP per kill gives the number
of kills it takes. The three columns match the Mad Forest rosters: bats and
zombies early, skeletons and werewolves in the middle, mummies and giant bats
late.

| Level | XP for this level | Growth bonus | Kills at 0.5 XP | Kills at 1.5 XP | Kills at 2.5 XP |
|---|---|---|---|---|---|
| 1 | 5 | 1× | 10 | 4 | 2 |
| 5 | 45 | 1× | 90 | 30 | 18 |
| 10 | 95 | 1× | 190 | 64 | 38 |
| 15 | 145 | 1× | 290 | 97 | 58 |
| 19 | 185 | 1× | 370 | 124 | 74 |
| 20 | 795 | 2× | 795 | 265 | 159 |
| 21 | 266.5 | 1× | 533 | 178 | 107 |
| 30 | 383.5 | 1× | 767 | 256 | 154 |
| 39 | 500.5 | 1× | 1001 | 334 | 201 |
| 40 | 2913.5 | 2× | 2914 | 972 | 583 |
| 41 | 648 | 1× | 1296 | 432 | 260 |
| 50 | 792 | 1× | 1584 | 528 | 317 |
| 60 | 952 | 1× | 1904 | 635 | 381 |

Per-enemy XP grows only about 5×, from 0.5 to 2.5, over a Mad Forest run.
Level cost grows about 10× from level 10 to level 60. The difference is made
up by kill volume: the minimum crowd grows from 15 to 300 and the spawn tick
shortens from 1000 to 100 ms. Leveling therefore depends on clearing crowds,
not on killing tough enemies. A 50-HP Big Mummy gives 2.5 XP, five times a
0.1-HP bat for 500 times the HP.

Bosses are the exception. A level-scaled boss bat gives 29.5 XP, about as much as
60 bats, and most bosses also carry a treasure chest.

## Growth sources

| Source | Growth |
|---|---|
| Base | 1.0 |
| Crown, 5 levels | +0.4 |
| Shop Growth, 5 ranks | +0.15 |
| Imelda's bonus at levels 5, 10, 15 | +0.3 |
| Gallo's bonus | +0.1 every 5 levels |
| Levels 20 and 40, every character | +1.0 for that level only |

The stage XP bonus is a separate multiplier (`GameManager.ExperienceMultiplier`):

| Stage or mode | XP bonus |
|---|---|
| Most stages | 1.0 |
| Hurry mode | +0.25 |
| The Coop | 0.35 |
| Bat Country | 0.25 |

## Skips

A Skip at the level-up screen grants 20% of the gap between the player's XP
and the current level-up threshold (`GameManager.GetLevelUpSkipXpToGrant`).
It ignores Growth but not the stage XP bonus.

## Takeaways

- **Level cost grows with the square of the level**, with repricing spikes
  at 20 and 40 that a one-level +100% Growth bonus halves but does not
  remove.
- **XP per kill barely grows.** Late enemies have 100× the HP of early ones
  but only about 5× the XP. Leveling speed comes from the size of the crowd.
- **Rounding halves the value of the weakest enemies.** A 1-XP enemy drops
  0.5 XP on average.
- **Growth is a small lever.** The shop and Crown together give +55%, while
  kill volume varies by 20× or more over a run.
