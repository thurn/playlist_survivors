# Appendix: Movement, Weapons and Mad Forest Events

Part of [Vampire Survivors: Numerical Progression](progression.md). This fills
in the numbers needed by [the survivors-run design](../design/survivors-run.md).
Source: Steam build 1.16.111, macOS, extracted 2026-10-25. Data keys refer to
`WEAPON_DATA`, `ENEMY_DATA`, `CHARACTER_DATA`, `STAGE_DATA` and `ITEM_DATA`;
class/method names refer to `VampireSurvivors.Runtime.dll`, except `Body`, which
is in `PhaserPort.dll`. The private extraction is in `~/Documents/vs-extract`.
Only selected numerical tables and descriptions are kept here, not raw game
files. [Source hashes and audit](../../native/evidence/sf1/source-audit.json)
identify the inputs.

## Units and movement

Damage and enemy HP below are internal (shown ×10). Player HP, armor, recovery
and contact damage are unscaled. Weapon timing fields are milliseconds;
velocities are per second; area/speed and most character stats are multipliers.

VS has two coordinate conventions. Unity world coordinates use units (U).
The legacy pixel-shaped physics API converts a radius or size by **0.01 U per
legacy pixel** (`Body.setCircle/setSize`, `worldSpace=false`); velocity setters
already take U/s (`Body.setVelocity`). A legacy pixel is not a display pixel:
camera zoom and sprite scaling still apply. Use one common conversion factor
when importing positions/radii and speeds into Playlist Survivors.

| Quantity | Value before bonuses | Source / interpretation |
|---|---|---|
| Player speed constant | 0.82500005 U/s (about 82.5 legacy px/s) | `GameManager.BASE_PLAYER_PX_SPEED`; `CharacterController.Speed`, `FrameWalk` |
| Antonio MoveSpeed | 1 | `CHARACTER_DATA.ANTONIO[0].moveSpeed`; `PMoveSpeed` |
| Mad Forest normal player speed multiplier | 1.1 | `STAGE_DATA.FOREST[0].mods.PlayerPxSpeed`; `Stage` initialization |
| Antonio on normal Mad Forest | 0.9075 U/s (about 90.75 legacy px/s) | constant × 1.1 × MoveSpeed, no Wings/shop/Hyper |
| Enemy speed constant | 0.231 U/s | `GameManager.BASE_ENEMY_SPEED` |
| Mad Forest normal enemy speed multiplier | 1.1 | `FOREST[0].mods.EnemySpeed`; `Stage.CalculateEnemySpeed` |
| An enemy's velocity magnitude | 0.231 × 1.1 × (`speed` / 100) U/s | `EnemyController.CalculateDirectionAndVelocity`; also multiplied by Curse, stage speed modifier and Slow when present |
| Projectile speed constant | 1.6500001 U/s | `GameManager.ProjectileSpeed`; `Projectile.ProjectileSpeed` multiplies weapon PSpeed and projectile `_speed` (default 1) |
| Base pickup magnet radius | 30 legacy px = 0.3 U | `MagnetZone.Radius`, `RefreshSize` and `Body.setCircle`; before transform scaling |
| Antonio magnet bonus | 0 | `CHARACTER_DATA.ANTONIO[0].magnet`; additions enlarge that base radius |
| Normal damage lockout | 120 ms + InvulTimeBonus (bonus capped at 1000 ms) | `CharacterController.GetDamaged` → `TakeDamage` → `OnGetDamaged`; `_receivingDamage` rejects subsequent contacts until its timer ends; `PInvulTime` |
| Chicken pickup invulnerability | 300 ms | `Roast.GetTaken`; distinct from the damage lockout |

The important speed conversion for the demo is a **ratio**, independent of
Mad Forest's shared 1.1 modifier:

```text
enemy speed / Antonio speed = (0.231 / 0.825) × data speed / 100
                            ≈ 0.28 × data speed / 100
```

For example a BAT1 with data speed 140 moves at 0.392 of Antonio's speed;
it is not moving at 140 px/s. A demo hero at 140 px/s therefore implies a
54.88 px/s BAT1. An equivalent common scale starting from normal Forest's
legacy units is `px_per_vs_unit = 140 / 90.75 ≈ 1.5427`; this would give a
base pickup radius of about 46.28 demo px. These are conversion examples,
not a retune of the design's knobs. SF4 chooses and records its actual scale.

Antonio's other relevant bases are 120 HP, armor 1, regen 0, Might/Area/
projectile Speed/Duration/Cooldown/Luck/Growth/Greed/Curse 1, and Amount bonus 0
(`CHARACTER_DATA.ANTONIO[0]`). His own level-based Might bonuses and purchased
power-ups are excluded by the demo's fixed design.

## How combat fields become behavior

`Weapon.PPower/PInterval/PArea/PSpeed/PDuration/PAmount` read the cumulative
weapon values and final character stats:

```text
power     = weapon power × Might                   (no Bloodline in demo)
interval  = weapon interval × Cooldown
amount    = weapon amount + min(10, Amount bonus)
area      = weapon area × Area
speed     = weapon speed × min(5, character projectile Speed)
duration  = weapon duration × min(5, character Duration)
```

Cooldown is clamped to at least 0.1; Might and Area to at most 10 by the
character's final-stat accessors (see [progression](progression.md#how-stats-combine)).
Burst repeats use `repeatInterval / max(0.001, PSpeed)` through
`Weapon.PSpeedRepeatInterval`, not the volley cooldown.

The raw fields need these projectile-specific rules:

- **Whip / Bloody Tear:** the Whip's 0.2 crit chance and ×2 multiplier are
  present in data, but normal Whip enables crit only with Slash Arcana
  (`WhipWeapon.CheckArcanas`, `Weapon.StandardCritical`). The demo has no
  Arcanas. Bloody Tear's overlap override rolls 0.1 × Luck and deals ×2;
  on a critical overlap it heals **8 player HP**, at most once per **1000 ms**
  (`VampiricaWeapon.OnBulletOverlapsEnemy`, `_healDelay`). This is not healing
  on every ordinary hit. Whip's animation grows over 100 ms, then fades over
  another 100 ms (`WhipProjectile.InitProjectile`); generic duration is unused.
  The demo's 360-degree spin, 80 px starting radius, tempo-normalized Whip DPS
  and subdivision spins are deliberate adaptations, not VS geometry.
- **Magic Wand / Holy Wand:** a projectile aims at the nearest enemy on spawn,
  radius 8 legacy px × Area, then consumes its pierce budget on surviving
  targets (`MagicMissileProjectile.InitProjectile/OnHasHitAnObjectLogic`).
  Unmodified speed is 1.65 U/s for Wand and 3.3 U/s for Holy Wand. Holy Wand's
  raw interval is **500 ms**; Empty Tome rank 5 makes it **300 ms** (500 × 0.6).
  Generic duration is not its lifespan. The demo's homing presentation is its
  own choice; source behavior does not continuously reacquire the target.
- **King Bible / Unholy Vespers:** radius is `0.5 × min(4.5, PArea)` U;
  angular velocity is `35 × 0.0625 × PSpeed` radians/s (2.1875 × PSpeed).
  The book hit shape has radius 8 legacy px and grows to scale
  `1 + 0.3 × PArea` over 0.5 s. Each book clears its per-target hit memory
  every **1700 ms**, independent of projectile Speed; it expires after
  PDuration (`HolyBookProjectile.InitProjectile/InternalUpdate`). Bible has
  `intervalDependsOnDuration=true`: `Weapon.ResetFiringTimer` uses raw
  **Duration + PInterval**, e.g. 3000 + 3000 ms at rank 1. Vespers sets this
  false, recasting on PInterval while books last PDuration, allowing continuous
  overlap. Nominal power×amount/interval comparisons in the older damage
  appendix are not this per-target runtime DPS. The demo's phase-locked
  musical orbit is specified separately.
- **Garlic / Soul Eater:** radius is 16 legacy px at scale `2 × PArea`
  (`GarlicProjectile.InitProjectile`, `VortexProjectile`), i.e. 32 × PArea
  legacy px. Each aura projectile remembers a target once; it expires and is
  recreated on PInterval, so interval is the ordinary repeat-hit cadence,
  not the table's generic 1000 ms hitBoxDelay or 1300 ms duration.
  Garlic's own knockback is 0 but each surviving hit increases an eligible
  enemy's knockback multiplier by 0.3 if it is still below maxKnockback and
  decreases positive freeze resistance by 0.1. `ResDebuffs >= 1` blocks this
  (`GarlicProjectile.OnHasHitAnObject`).
- **Soul Eater healing/growth:** on a kill, chance of a Little Heart is
  `0.15 × (PPower / 7)` (`VortexWeapon.OnBulletOverlapsEnemy`). A Little Heart
  recovers `1 × (1 + Recovery)` player HP (`LittleHeart.GetTaken`,
  `CharacterController.RecoverHp`), with healing capped by missing HP.
  Soul Eater's power adds `min(6, actual HP recovered / 600)` after evolution
  (`VortexWeapon.PPower/VortexUpdate`). The bonus is added after Might scales
  the base power, so Might does not multiply this bonus. The recovery callback
  counts actual healing, not overheal. There is no fixed every-hit heal field in WEAPON_DATA.
  The design's "heals per hit" is a demo adaptation, not a claim about VS.
- **Lightning Ring / Thunder Loop:** interval is the volley cadence, amount
  the strike count. Thunder Loop repeats each strike after a **1500 ms** charge,
  clearing hit memory for the second strike
  (`LoopProjectile.InitProjectile/Strike`). The demo lands strikes on grid
  subdivisions and supplies its own sky-bolt visuals. Neither weapon uses
  the generic duration as a damage window or the speed field as travel speed.

### Knockback and resistance

Source: `Weapon.Knockback`, `EnemyController.InitialiseLocalData/GetDamaged/
CalculateDirectionAndVelocity/PlayVFXFlash/RestoreTint`, `GarlicProjectile`.
A missing weapon knockback defaults to 5, not zero. During an enemy's damage
reaction, its normal movement velocity is multiplied by **−enemy.KnockBack ×
weapon.Knockback**. The hit flash/reaction lasts 120 ms for a living enemy
(60 ms for a dead one); another hit can refresh it. This is a temporary reverse
velocity, not an instantaneous fixed-distance displacement. `deathKB` belongs
to the separate death movement. The demo applies its recoil on the next
sixteenth, as specified in the design.

Enemy `knockback` is susceptibility: zero means no initial recoil; a small
positive value resists it. `maxKnockback` bounds eligibility for Garlic's +0.3
increments (the source tests before adding, so an increment can overshoot).
The nullable data field `res_Knockback` is **not** a generic `(1-resistance)`
factor in this path: this build assigns it into `ResDebuffs` after assigning
`res_Debuffs`; Garlic checks that property. The table below preserves both
source fields rather than inventing a second resistance equation.

## The six passives

Source: `WEAPON_DATA` keys below; `Accessory.Apply/ApplyToCharacter` and
`CharacterController.PlayerStatsUpgrade`. These are per-rank **deltas**;
passive effect totals add except Hollow Heart, which applies each +20% to the
current maximum and heals the gained maximum. Other than Duplicator's two
ranks, all have five. Evolution partners need the passive owned, not maxed
(`requires`, no `requiresMax` on these five evolutions).

| Passive / data key | Rank 1 | Rank 2 | Rank 3 | Rank 4 | Rank 5 | Result from Antonio's base |
|---|---|---|---|---|---|---|
| Spinach / POWER | +0.1 Might | +0.1 | +0.1 | +0.1 | +0.1 | Might 1.5 |
| Hollow Heart / MAXHEALTH | ×1.2 Max HP | ×1.2 | ×1.2 | ×1.2 | ×1.2 | 120 × 1.2⁵ = 298.5984 HP |
| Pummarola / REGEN | +0.2 HP/s | +0.2 | +0.2 | +0.2 | +0.2 | Recovery 1 HP/s |
| Empty Tome / COOLDOWN | −0.08 | −0.08 | −0.08 | −0.08 | −0.08 | Cooldown 0.6 |
| Spellbinder / DURATION | +0.1 | +0.1 | +0.1 | +0.1 | +0.1 | Duration 1.5 |
| Duplicator / AMOUNT | +1 | +1 | — | — | — | Amount bonus +2 |

Evolution pairs: Hollow Heart → Bloody Tear; Pummarola → Soul Eater;
Empty Tome → Holy Wand; Spellbinder → Unholy Vespers; Duplicator → Thunder
Loop. Spinach is a standalone passive in this demo (its VS Fire Wand partner
is outside the roster). No Wings, Attractorb, armor item or Antonio level
bonus is introduced by this appendix.

## Full weapon level tables

These are **cumulative combat values**, before character/passive modifiers.
`WEAPON_DATA` starts with a complete rank 1; later numeric fields add and other
fields replace (`DataHelper.UpgradeJsonData`). Evolutions start their own rank
1 and do not add their row to the maxed base weapon. All five base weapons
have eight ranks; each evolution here has one. Rank 8 unlocks the corresponding
`addEvolvedWeapon`. Price, unlock state, text, textures and icons are not combat
parameters; no game text or raw JSON is reproduced.

Columns: **P** = internal power (screen damage ×10), **N** = amount,
**I/R/D/H** = interval / repeat interval / duration / hitBoxDelay in ms,
**A/S** = area / speed multipliers, **pierce** = `penetrating` count,
**KB** = knockback multiplier. `D=1000`, `H=1000`, `KB=5`, and `pierce=0`
when absent are runtime defaults (`Weapon.Duration`, `HitBoxDelay`,
`Knockback`, `WeaponData.penetrating`). A default is not proof the projectile
uses it: Whip ignores duration/speed, Garlic ignores amount/duration/speed,
Lightning ignores duration/speed. Whip/aura/area attacks do not consume the
Wand projectile's pierce budget, so their zero is **not zero targets**.


### Whip (`WHIP`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 1 | 1 | 1350 | 100 | 1 | 1 | 1000 | 0 | 1000 | 5 |
| 2 | 1 | 2 | 1350 | 100 | 1 | 1 | 1000 | 0 | 1000 | 5 |
| 3 | 1.5 | 2 | 1350 | 100 | 1 | 1 | 1000 | 0 | 1000 | 5 |
| 4 | 2 | 2 | 1350 | 100 | 1.1 | 1 | 1000 | 0 | 1000 | 5 |
| 5 | 2.5 | 2 | 1350 | 100 | 1.1 | 1 | 1000 | 0 | 1000 | 5 |
| 6 | 3 | 2 | 1350 | 100 | 1.2 | 1 | 1000 | 0 | 1000 | 5 |
| 7 | 3.5 | 2 | 1350 | 100 | 1.2 | 1 | 1000 | 0 | 1000 | 5 |
| 8 | 4 | 2 | 1350 | 100 | 1.2 | 1 | 1000 | 0 | 1000 | 5 |

### Magic Wand (`MAGIC_MISSILE`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 1 | 1 | 1200 | 100 | 1 | 1 | 1000 | 1 | 1000 | 5 |
| 2 | 1 | 2 | 1200 | 100 | 1 | 1 | 1000 | 1 | 1000 | 5 |
| 3 | 1 | 2 | 1000 | 100 | 1 | 1 | 1000 | 1 | 1000 | 5 |
| 4 | 1 | 3 | 1000 | 100 | 1 | 1 | 1000 | 1 | 1000 | 5 |
| 5 | 2 | 3 | 1000 | 100 | 1 | 1 | 1000 | 1 | 1000 | 5 |
| 6 | 2 | 4 | 1000 | 100 | 1 | 1 | 1000 | 1 | 1000 | 5 |
| 7 | 2 | 4 | 1000 | 100 | 1 | 1 | 1000 | 2 | 1000 | 5 |
| 8 | 3 | 4 | 1000 | 100 | 1 | 1 | 1000 | 2 | 1000 | 5 |

### King Bible (`HOLYBOOK`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 1 | 1 | 3000 | 0 | 1 | 1 | 3000 | 0 | 1700 | 5 |
| 2 | 1 | 2 | 3000 | 0 | 1 | 1 | 3000 | 0 | 1700 | 5 |
| 3 | 1 | 2 | 3000 | 0 | 1.25 | 1.3 | 3000 | 0 | 1700 | 5 |
| 4 | 2 | 2 | 3000 | 0 | 1.25 | 1.3 | 3500 | 0 | 1700 | 5 |
| 5 | 2 | 3 | 3000 | 0 | 1.25 | 1.3 | 3500 | 0 | 1700 | 5 |
| 6 | 2 | 3 | 3000 | 0 | 1.5 | 1.6 | 3500 | 0 | 1700 | 5 |
| 7 | 3 | 3 | 3000 | 0 | 1.5 | 1.6 | 4000 | 0 | 1700 | 5 |
| 8 | 3 | 4 | 3000 | 0 | 1.5 | 1.6 | 4000 | 0 | 1700 | 5 |

### Lightning Ring (`LIGHTNING`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 1.5 | 2 | 4500 | 50 | 1 | 1 | 1000 | 0 | 1000 | 5 |
| 2 | 1.5 | 3 | 4500 | 50 | 1 | 1 | 1000 | 0 | 1000 | 5 |
| 3 | 2.5 | 3 | 4500 | 50 | 2 | 1 | 1000 | 0 | 1000 | 5 |
| 4 | 2.5 | 4 | 4500 | 50 | 2 | 1 | 1000 | 0 | 1000 | 5 |
| 5 | 4.5 | 4 | 4500 | 50 | 3 | 1 | 1000 | 0 | 1000 | 5 |
| 6 | 4.5 | 5 | 4500 | 50 | 3 | 1 | 1000 | 0 | 1000 | 5 |
| 7 | 6.5 | 5 | 4500 | 50 | 4 | 1 | 1000 | 0 | 1000 | 5 |
| 8 | 6.5 | 6 | 4500 | 50 | 4 | 1 | 1000 | 0 | 1000 | 5 |

### Garlic (`GARLIC`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 0.5 | 1 | 1300 | 0 | 1 | 1 | 1300 | 0 | 1000 | 0 |
| 2 | 0.7 | 1 | 1300 | 0 | 1.4 | 1 | 1300 | 0 | 1000 | 0 |
| 3 | 0.8 | 1 | 1200 | 0 | 1.4 | 1 | 1300 | 0 | 1000 | 0 |
| 4 | 0.9 | 1 | 1200 | 0 | 1.6 | 1 | 1300 | 0 | 1000 | 0 |
| 5 | 1.1 | 1 | 1100 | 0 | 1.6 | 1 | 1300 | 0 | 1000 | 0 |
| 6 | 1.2 | 1 | 1100 | 0 | 1.8 | 1 | 1300 | 0 | 1000 | 0 |
| 7 | 1.3 | 1 | 1000 | 0 | 1.8 | 1 | 1300 | 0 | 1000 | 0 |
| 8 | 1.5 | 1 | 1000 | 0 | 2 | 1 | 1300 | 0 | 1000 | 0 |

### Bloody Tear (`VAMPIRICA`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 4 | 2 | 1350 | 100 | 1.3 | 1 | 1000 | 0 | 1000 | 5 |

### Holy Wand (`HOLY_MISSILE`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 3 | 4 | 500 | 100 | 1 | 2 | 1000 | 2 | 1000 | 5 |

### Unholy Vespers (`VESPERS`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 3 | 4 | 3000 | 0 | 1.75 | 1.5 | 3000 | 0 | 1700 | 4 |

### Thunder Loop (`LOOP`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 6.5 | 6 | 4500 | 0 | 4 | 1 | 1000 | 0 | 1000 | 5 |

### Soul Eater (`VORTEX`)

| Rank | P | N | I | R | A | S | D | pierce | H | KB |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 2 | 1 | 1000 | 0 | 3 | 1 | 1300 | 0 | 1000 | 0 |

### Constant weapon fields

| Weapon key | Crit chance | Crit × | Interval adds duration | Hits walls | Pool limit | Rarity |
|---|---|---|---|---|---|---|
| WHIP | 0.2 | 2 | no | no | 30 | 100 |
| MAGIC_MISSILE | 0 | 0 | no | yes | 60 | 100 |
| HOLYBOOK | 0 | 0 | yes | no | 50 | 80 |
| LIGHTNING | 0 | 0 | no | no | 50 | 80 |
| GARLIC | 0 | 0 | no | no | 50 | 70 |
| VAMPIRICA | 0.1 | 2 | no | no | 30 | 1 |
| HOLY_MISSILE | 0 | 0 | no | yes | 60 | 1 |
| VESPERS | 0 | 0 | no | no | 50 | 1 |
| LOOP | 0 | 0 | no | no | 70 | 1 |
| VORTEX | 0 | 0 | no | no | 50 | 1 |

## Enemy speeds and knockback

Source: `ENEMY_DATA` rank 1 for every Mad Forest roster/boss plus the two
swarm types; the Reaper is intentionally omitted from this demo. The HP,
contact power and XP roster tables remain in the [damage appendix](appendix-damage-vs-health.md);
the event-only swarm combat rows are below.
`speed` is a data scalar relative to 100, **not px/s**. Hero-speed ratios
below are `0.28 × speed / 100`, with shared Forest speed modifiers cancelling.
KB/maxKB/deathKB are dimensionless. Missing resistances become zero through
`GetValueOrDefault()` in `EnemyController.InitialiseLocalData`.


| Enemy | speed | Hero-speed ratio | KB | maxKB | deathKB | res_Knockback | res_Debuffs | res_Freeze |
|---|---|---|---|---|---|---|---|---|
| BAT1 | 140 | 0.392 | 1 | 3 | 2 | 0 | 0 | 0 |
| BAT2 | 140 | 0.392 | 1 | 3 | 2 | 0 | 0 | 0 |
| BAT3 | 140 | 0.392 | 1 | 3 | 2 | 0 | 0 | 0 |
| BAT4 | 140 | 0.392 | 1 | 3 | 2 | 0 | 0 | 0 |
| BAT5 | 140 | 0.392 | 1 | 3 | 2 | 0 | 0 | 0 |
| BATSWARM | 700 | 1.96 | 1 | 3 | 2 | 0 | 0 | 0 |
| BOSS_WEREWOLF | 130 | 0.364 | 0.1 | 1 | 7 | 0 | 0 | 0 |
| BOSS_XLFLOWER | 160 | 0.448 | 0 | 1 | 1 | 0 | 0 | 1.1 |
| BOSS_XLMANTIS | 160 | 0.448 | 0 | 1 | 5 | 0 | 0 | 1.1 |
| BOSS_XLMUMMY | 80 | 0.224 | 0 | 1 | 5 | 0 | 0 | 1.1 |
| FLOWER | 20 | 0.056 | 1 | 3 | 2 | 0 | 0 | 0 |
| GHOST | 200 | 0.56 | 0 | 3 | 0 | 0 | 0 | 0 |
| GHOSTSWARM | 700 | 1.96 | 0 | 3 | 0 | 0 | 0 | 0 |
| MUDMAN1 | 100 | 0.28 | 0.3 | 3 | 2 | 0 | 0 | 0 |
| MUDMAN2 | 100 | 0.28 | 0.3 | 3 | 2 | 0 | 0 | 0 |
| SKELETON | 100 | 0.28 | 1 | 3 | 5 | 0 | 0 | 0 |
| WEREWOLF | 130 | 0.364 | 0.8 | 3 | 7 | 0 | 0 | 0 |
| XLBAT | 140 | 0.392 | 0.1 | 3 | 2 | 0 | 0 | 0 |
| XLFLOWER | 80 | 0.224 | 0 | 3 | 0 | 0 | 0 | 0 |
| XLMANTIS | 80 | 0.224 | 0 | 3 | 0 | 0 | 0 | 0 |
| XLMUMMY | 80 | 0.224 | 0 | 3 | 0 | 0 | 0 | 0 |
| ZOMBIE | 100 | 0.28 | 0.8 | 3 | 4 | 0 | 0 | 0 |

### Swarm combat fields

The damage appendix's roster table omits the two event-only roles. Their
`ENEMY_DATA` rank-1 combat fields are:

| Event role | Raw max HP | HP × player level | Contact power | speed | XP | Fixed direction |
|---|---:|---|---:|---:|---:|---|
| BATSWARM | 0.1 | no | 1 | 700 | 1 | yes |
| GHOSTSWARM | 1 | no | 5 | 700 | 1.5 | yes |

Neither role has the `HP_x_Level` skill; `EnemyController.InitSkills` enables
level scaling only for that skill. Both use `Fixed_Direction`, matching the
crossing-swarm behavior. These are raw source fields; the demo still converts
speed by the hero ratio and applies its level's contact-damage multiplier.
Knockback/resistance fields are in the preceding table. Source hashes and the
selected numerical audit are in [SF1-swarm evidence](../../native/evidence/sf1-swarm/README.md).

## Mad Forest events

Source: `STAGE_DATA.FOREST[*].events`; `StageEventManager.TriggerEvent`,
`TriggerSwitchEvent`, `PlayBatSwarm`, `PlayGhostSwarm`, `PlayCircle`,
`GenerateEnemySwarm`, `GenerateEnemyWall`. A dash for chance means no
probability gate; **explicit zero also means no gate**, not "never".
Positive chance is tested independently per firing as `random × 100 <=
chance / Luck`. Delays and duration are ms. `repeat` is extra firings after
the first (`Timer.Update`), so two repeats means three attempts. With delay
zero `TriggerEvent` calls once immediately and does not schedule repetitions.

Each bat attempt creates 50 BATSWARM; each ghost attempt creates 20
GHOSTSWARM; each flower-wall attempt creates 100 FLOWER. These are counts per
attempt, not counts for the whole minute. Default duration is 10,000 ms for
swarms, 60,000 ms for walls. Minute 30's CYCLE_COMPLETE/Reaper is omitted.


| Minute | Event | Delay ms | Extra repeats | Chance % | Duration ms | Count per attempt |
|---|---|---|---|---|---|---|
| 2 | BAT_SWARM | 5000 | 2 | — | 10000 | 50 |
| 3 | BAT_SWARM | 5000 | 1 | 10 | 10000 | 50 |
| 4 | BAT_SWARM | 5000 | 1 | 10 | 10000 | 50 |
| 5 | FLOWER_WALL | 0 | 0 | 0 | 30000 | 100 |
| 6 | BAT_SWARM | 5000 | 1 | 10 | 10000 | 50 |
| 7 | BAT_SWARM | 5000 | 5 | 80 | 10000 | 50 |
| 8 | BAT_SWARM | 15000 | 2 | 80 | 10000 | 50 |
| 9 | BAT_SWARM | 15000 | 2 | 70 | 10000 | 50 |
| 10 | FLOWER_WALL | 0 | 0 | — | 60000 | 100 |
| 11 | BAT_SWARM | 5000 | 1 | 10 | 10000 | 50 |
| 12 | BAT_SWARM | 5000 | 1 | 10 | 10000 | 50 |
| 13 | GHOST_SWARM | 1200 | 20 | 70 | 10000 | 20 |
| 13 | GHOST_SWARM | 2300 | 20 | 70 | 10000 | 20 |
| 15 | FLOWER_WALL | 0 | 0 | 80 | 60000 | 100 |
| 20 | BAT_SWARM | 1200 | 20 | 70 | 10000 | 50 |
| 20 | BAT_SWARM | 2300 | 20 | 70 | 10000 | 50 |
| 25 | FLOWER_WALL | 10000 | 5 | — | 10000 | 100 |
| 27 | GHOST_SWARM | 0 | 0 | — | 10000 | 20 |
| 27 | GHOST_SWARM | 3000 | 19 | — | 10000 | 20 |
| 29 | BAT_SWARM | 0 | 0 | — | 10000 | 50 |
| 29 | BAT_SWARM | 3000 | 19 | — | 10000 | 50 |

Swarm formation: pick angle θ uniformly around the screen centre; place the
cluster at `(cx + cos θ × 0.8 × width × 0.9, cy + sin θ × 0.8 × height × 0.9)`
and increase θ by π/500 for each member. Flower walls use the hero centre,
the same 0.8 × 0.9 screen-bound ellipse, and angle increments of 2π/count
around the full ring. Event members share an event ID; surviving members
are removed at duration expiry. This is an ellipse relative to viewport bounds,
not a fixed px radius. The demo changes timing to its first loudness jump
(or last-ten-seconds fallback) and uses its gait/ring presentation, as fixed
in the design; VS itself does not snap events to music.


## Boss treasure by minute

Source: `STAGE_DATA.FOREST[*].bosses/treasure` and
`Stage.ResetStageDataForUpdate`, `SpawnBoss`, `SetTreasureLevelFromChance`.
Treasure and bosses reset each minute; **treasure does not carry forward**.
A dash means no normal treasure for that minute. The triple is the raw percent
chances for tier **3 / 2 / 1**, meaning **5 / 3 / 1 prizes**. These are three
independent rolls tried in that order, each multiplied by Luck. At Luck 1:
`P(chest) = p3 + (1-p3)*p2 + (1-p3)*(1-p2)*p1`. A 100 in the last column
therefore guarantees a chest. Only the first successful boss attachment per
minute gets this normal treasure. Arcana holders at 11/21 are separate and
outside the demo. VS's first six beginner treasure checks override the rolls
with tiers 1,1,2,1,1,3 (`BeginnersLuck`); the demo does not need that profile state.

Prize codes below are E = EVOLUTION, W = EXISTING_WEAPON, A = EXISTING_ANY;
order is the five-slot `prizeTypes` array. These are eligibility categories,
not guaranteed evolutions: a suitable maxed weapon and partner are still
required. Minute 1 additionally declares `fixedPrizes: [AMOUNT]`.


| Minute | Bosses | Chance 3/2/1 % | Data level | Prize codes |
|---|---|---|---|---|
| 0 | — | — | — | — |
| 1 | BAT4 | 0 / 0 / 30 | 1 | EAAAA |
| 2 | — | — | — | — |
| 3 | BAT4 | 0 / 5 / 40 | 1 | WAAAA |
| 4 | — | — | — | — |
| 5 | XLMANTIS | 1 / 5 / 100 | 1 | WAAAA |
| 6 | — | — | — | — |
| 7 | BAT4 | 3 / 10 / 50 | 2 | WAAAA |
| 8 | XLBAT | — | — | — |
| 9 | BAT5 | 3 / 10 / 50 | 1 | WAAWW |
| 10 | BOSS_XLMANTIS | 3 / 10 / 100 | 1 | EAAWW |
| 11 | — | — | — | — |
| 12 | BAT4 | 1 / 5 / 50 | 1 | EAAAA |
| 13 | — | — | — | — |
| 14 | BAT5 | 3 / 10 / 100 | 1 | EAAAA |
| 15 | BOSS_WEREWOLF | 3 / 10 / 100 | 1 | EAAAA |
| 16 | BAT4 | 1 / 5 / 50 | 1 | EAAAA |
| 17 | — | — | — | — |
| 18 | BAT5 | 3 / 10 / 100 | 1 | EAAAA |
| 19 | — | — | — | — |
| 20 | BOSS_XLMUMMY | 3 / 10 / 100 | 1 | EAAAA |
| 21 | XLFLOWER | — | — | — |
| 22 | BAT4 | 3 / 10 / 100 | 1 | EAAAA |
| 23 | BAT5 | 3 / 10 / 100 | 1 | EAAAA |
| 24 | XLFLOWER | — | — | — |
| 25 | BOSS_XLFLOWER | 3 / 10 / 100 | 1 | EAAAA |
| 26 | — | — | — | — |
| 27 | BAT4 | 3 / 10 / 100 | 1 | EAAAA |
| 28 | — | — | — | — |
| 29 | BAT4 | 3 / 10 / 100 | 1 | EAAAA |

## Floor chickens and gem attraction

### Chickens are weighted destructible loot

VS does not have this demo's generic "brute 5%, boss 100%" chicken rule.
That is an explicit easy knob in the fixed design. In normal Mad Forest:

| Quantity | Value | Source |
|---|---|---|
| Destructible kind | BRAZIER, HP 1 | `STAGE_DATA.FOREST[0]`, `PROPS_DATA.BRAZIER` |
| Destructible attempt interval | 1000 ms | `destructibleFreq`, `Stage` destructible timer |
| Spawn chance per eligible attempt | min(10 × Luck, 50)% | `destructibleChance`, `destructibleChanceMax`; `Stage.SpawnChosenDestructibleInRandomLocation` (tile positions) / walls-check or out-of-sight paths |
| Maximum destructibles | 10 | `FOREST[0].maxDestructibles`; location/cap checks can reject an attempt |
| Floor Chicken item | ROAST, value 30 HP, rarity weight 12, unlock level 0 | `ITEM_DATA.ROAST` |
| Forced Floor Chicken | ALWAYS_ROAST, value 30 HP, rarity 0, unlock 100000 | `ITEM_DATA.ALWAYS_ROAST`; not a random default drop |
| Pickup protection | 300 ms invulnerability | `Roast.GetTaken` |

A destroyed brazier asks `LootManager.GetRandomWeightedItem` for one item
(`Destructible.OnDestroyed`). Eligible item weights are added cumulatively;
a uniform roll over their total selects one. Normal items use `rarity ×
ModifyItemWeight`, rare ones also multiply by Luck; prerequisites, current
level, excluded items, custom stage loot and profile seals affect the table
(`LootManager.MakeDefaultLootTable/GetRandomWeightedItem`). A sealed selection
becomes a coin. Chicken's **weight 12 is not a 12% chance**; its probability is
12 divided by the eligible total when its weight modifier is 1. No single
profile-independent chicken percentage follows from ITEM_DATA alone.

`Roast.GetTaken` recovers `30 × (1 + Recovery)`, capped by missing HP
(`CharacterController.RecoverHp`), with no Arcanas here. Thus the VS passive
Pummarola also amplifies food healing; the demo's fixed recovery/damage/food
rules remain the implementation authority where they deliberately differ.

### Gem pull is accelerated, not a fixed flight time

Source: `Gem.SetData/SetValue`, `Pickup.SetData/Vacuum/InternalUpdate/
GoToThePlayer` and `Body.setVelocity`:

- Gem pickup body radius is 10 legacy px (0.1 U); magnet radius 30 above is
  the magnet's own radius, not the sum of the two colliders.
- Pickup speed resets to **250 legacy px/s**. `GoToThePlayer` sets velocity
  to `Speed × 0.01 × Time` U/s toward the player's position, with a +0.08 U
  vertical target offset, then increments Speed by **1 per pickup update**.
- Pickup updates are throttled to a **60 Hz** interval; in steady operation,
  speed after n updates is `250 + n` legacy px/s (about +60 px/s each second).
  This is per-update acceleration, not an exact elapsed-time law under stalls.
- On attraction, `Vacuum` tweens **Time from −1 to +1 over 500 ms**, linearly,
  and sets GoToPlayer. The negative start gives the outward kick before pull-in.
  Gems initialize Time to +1, but attraction overrides it for this tween.
- A pickup with Speed above 65536 is forcibly collected; normal collection
  happens at collision with the player. There is no constant "gem arrival
  after one beat" rule in VS. The demo's grid-shaped collection presentation
  is its own adaptation.

## What is deliberately not copied

The [fixed decisions](../design/survivors-run.md#decisions) govern the demo:
30:00 victory without a Reaper, beat strides and lunges, snapped unsnapped-ready
weapon schedules, tempo-normalized sword spins, music-responsive event timing,
6+6 slots with five weapons, scripted offers, half contact damage before armor,
brute/boss floor chickens and the maxed-build heal card. VS has no committed
beat grid, music-stop freeze, knownAt, Delayed listening or matching playback
recordings. Its random swarms and treasure, passive synergies and numbers
above supply the source material; they do not authorize new gameplay choices
or retuning outside SW7.
