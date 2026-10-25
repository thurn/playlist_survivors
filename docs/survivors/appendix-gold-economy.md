# Appendix: Gold and Meta Costs

Part of [Vampire Survivors: Numerical Progression](progression.md). Gold is
the only resource that survives a run. This appendix covers where it comes
from and what it buys.

## Earning gold

Every gold source passes through one function (`PlayerOptions.AddCoins`):

```text
gold gained = base value × stage gold multiplier × Greed
```

Gold is added to the save immediately, so a run that ends in death keeps
everything it collected.

### Sources

| Source | Base value | Notes |
|---|---|---|
| Gold Coin | 1 | |
| Coin Bag | 10 | |
| Big Coin Bag | 25 | |
| Rich Coin Bag | 100 | rare (rarity 1) |
| Treasure chest, tier 1 | 100-199 | `TreasureFactory`, plus filler items |
| Treasure chest, tier 2 | 300-599 | |
| Treasure chest, tier 3 | 500-999 | |
| Chest slot with nothing left to level | the filler item's value | |
| Weapon already at max level, picked again | 10 | `WeaponsFacade.AddWeapon` |
| Surviving the full stage | 500 | `GameOverPage.EnterStageReward` |
| Remaining revivals at the end | 100, 200, 300, 400 for successive revivals | only when quitting after 30 minutes |

Chests are the largest single source. In Mad Forest, 17 of the 30 minutes
give their boss a chest. From minute 10 most of those chests are at least
tier 1 (chance 100 × Luck), with 10% and 3% chances of tiers 2 and 3 before
Luck.

### Multipliers

| Multiplier | Range |
|---|---|
| Greed, base | 1.0 |
| Stone Mask, 5 levels | +0.5 |
| Shop Greed, 5 ranks | +0.5 |
| Gallo, Divano | Greed 0.5 |
| Stage gold multiplier | 1.0 (Mad Forest) to 1.5 (The Bone Zone) |
| Hyper mode | +0.5 on most stages |
| Inverse mode | +2.0 |

A maxed Greed build (2.0) on Inverse Mad Forest (3.0) earns 6× base gold,
before Hyper.

## Spending gold

### Power-ups

The shop price of a power-up rank (`PlayerStats.GetPrice`) has two parts:

```text
price = base × (1 + ranks of this power-up already owned)
      + floor(20 × 1.1 ^ total ranks owned of all power-ups)
```

The first part is linear per power-up: Might costs 200, then 400, 600, 800,
1000. The second part is a global markup that grows 10% with every rank of
anything. The very first purchase has no markup.

| Power-up | Per rank | Ranks | Base price | All ranks at base |
|---|---|---|---|---|
| Might | +5% damage | 5 | 200 | 3,000 |
| Armor | −1 damage taken | 3 | 600 | 3,600 |
| Max Health | +10% | 3 | 200 | 1,200 |
| Recovery | +0.1 HP/s | 5 | 200 | 3,000 |
| Cooldown | −2.5% | 2 | 900 | 2,700 |
| Area | +5% | 2 | 300 | 900 |
| Speed | +10% projectile speed | 2 | 300 | 900 |
| Duration | +15% | 2 | 300 | 900 |
| Amount | +1 projectile | 1 | 5000 | 5,000 |
| Move Speed | +5% | 2 | 300 | 900 |
| Magnet | +25% | 2 | 300 | 900 |
| Luck | +10% | 3 | 600 | 3,600 |
| Growth | +3% | 5 | 900 | 13,500 |
| Greed | +10% | 5 | 200 | 3,000 |
| Curse | +10% | 5 | 1666 | 24,990 |
| Revival | +1 | 1 | 10000 | 10,000 |
| Omni | +2% Might, Speed, Duration, Area | 5 | 1000 | 15,000 |
| Charm | +20 enemies | 5 | 10000 | 150,000 |
| Defang | 3% of enemies harmless | 5 | 10 | 150 |
| Reroll | +2 | 5 | 500 | 7,500 |
| Skip | +2 | 5 | 100 | 1,500 |
| Banish | +2 | 5 | 100 | 1,500 |
| Recycle | 10% to keep a used reroll, skip or banish | 5 | 500 | 7,500 |

The markup added to the next purchase:

| Ranks already owned | Markup on the next rank |
|---|---|
| 1 | 22 |
| 5 | 32 |
| 10 | 51 |
| 20 | 134 |
| 30 | 348 |
| 40 | 905 |
| 50 | 2,347 |
| 60 | 6,089 |
| 70 | 15,794 |
| 82 | 49,571 |

Total markup paid after buying N ranks:

| Ranks bought | Total markup |
|---|---|
| 10 | 295 |
| 20 | 1,117 |
| 30 | 3,257 |
| 40 | 8,814 |
| 50 | 23,235 |
| 60 | 60,648 |
| 70 | 157,696 |
| 83 (all) | 545,023 |

Buying all 83 ranks costs 806,263 gold: 261,240 in base prices and 545,023
in markup. Below about 40 ranks the markup is small next to the base prices.
Past 60 ranks it dominates, and each rank costs thousands of gold whatever
its base price. The total markup depends only on how many ranks are bought,
so the order of purchases does not matter.

The shop can refund everything (`PlayerStats.GetRefundAmount` returns base
prices plus markup), so players can rearrange their power-ups freely.

### Characters

A character costs (`CharacterItemUI`):

```text
price = ceil(base price × (1 + 0.1 × characters bought so far))
```

Antonio is free and does not count. Unlike the power-up markup, this one
multiplies the base price, so the order matters. Buying the 19 other base-game
characters from the table in [the main document](progression.md#characters):

| Order | Total |
|---|---|
| Sticker prices, no markup | 18,110 |
| Most expensive first | 26,450 |
| Cheapest first | 42,373 |

Buying the expensive characters first saves about 16,000 gold, because the
large prices then carry the small multipliers.

### Merchant goods and seals

`ITEM_DATA` prices the merchant's one-run items:

| Item | Price |
|---|---|
| +1 Skip | 100 |
| +1 Banish | 500 |
| +1 Reroll | 1,000 |
| +1 Revival | 10,000 |
| Extra Arcana | 20,000 |

The shop also sells Seals I-IV, at 2,000, 4,000, 6,000 and 8,000 base gold
per rank, which remove items from the level-up pool.

## Takeaways

- **Gold scales with the run, not the player.** The stage multiplier,
  Greed and Hyper or Inverse set the rate. Chests are the biggest source.
- **Power-up prices grow exponentially in the number of ranks owned.** The
  first 40 ranks cost about 9,000 gold in markup. The last 23 cost over
  536,000.
- **The shop's bonuses are small next to a run's.** Maxed shop Might is
  +25% while Spinach gives +50%, so the shop mostly makes early minutes
  easier.
- **Character prices punish buying cheap characters first**, by about 60%
  for the base-game roster.
