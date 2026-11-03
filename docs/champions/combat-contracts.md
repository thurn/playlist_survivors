# Survivor combat contracts (ps-3wc.2)

These are the interfaces for ps-3wc.3–14. `design.md` remains authoritative.
The contracts are implemented; weapon actions, cadence, pickups, offers,
bosses and presentation are downstream work. Values are starting tuning.

## Content and identities

`GameContent.Combat` loads `Content/combat.json` strictly. `SimConfig.Combat`
and `RunInputs.Build` retain this immutable object. `CombatRules.Default`
matches the shipped content; `ToJson` produces canonical invariant JSON and
`Read` rejects missing/unknown fields, IDs, bad caps and lost compatibility.
Arrays use reserved enum order, rejecting reordered or duplicate identities.
`CombatRules.Id`, `TryWeapon` and `TrySupport` map exact case-sensitive IDs.
Do not reuse the old `AttackKind`/`ModKind` numeric values for these IDs.

| Enum | IDs in stable numeric order |
| --- | --- |
| `WeaponKind` | sword, hammer, spear, bow, orbitBlades, lightning, emberField, sentinel |
| `SupportKind` | might, tempo, reach, multishot, persistence, pierce, split, echo, attraction, stride, vitality, armor, regeneration, landingBlast |

Weapon acquisition is level 1, maximum 8. `WeaponDefinition.Form(level)`
returns the fixed form beginning at 1/3/5/8. `FormId` is `sword:form1` through
`sword:form4` (and equivalent IDs for the other weapons); form indices in
events are zero-based. The family tasks supply each form's action and visual
choreography under these identities. Every level above 1 adds the authored
`damagePerLevel` to the damage family, including ordinary levels 2/4/6/7.

Each form explicitly declares `WeaponCapabilities`. Later forms must include
every capability of the prior form; supporting a new projectile/ground/summon
component can add capabilities. Executors must retain actual compatible
components and coverage, not merely their flags. `SupportDefinition.Requires`
is an all-of capability mask. `None` denotes a hero-wide support, not a weapon
stat. Supports share six slots, cap at five in the shipped content, and apply
to compatible secondary attacks too. `WeaponBuild.BenefitsBuild` checks new
support eligibility against owned forms (or Landing Blast); future acquisitions
receive already-owned compatible supports automatically.

## Build and scaling

`SimState.Build` is the run's `WeaponBuild`, initially Sword level 1.
`WeaponBuild.Set` initializes explicit levels for loadouts/tests; it validates
levels and slot capacity. `Upgrade` and `CanUpgrade` implement one-level
progression and new-support eligibility. There are six weapon and six support
slots; raising an owned item uses no new slot. There is no evolution recipe.

`Stats(kind)` adds weapon-level and equipped compatible support bonuses.
`Stats(capabilities)` serves secondary components, excluding weapon-level
bonuses; use the owning weapon's level bonus when appropriate. `CombatStats.Add`
adds within a family. `DamageFactor * StrikeFactor` is connected single-target
output accounting: +50% each gives 2.25, another +50% damage gives 3.0.
Area, duration, pierce and projectile count do not enter that direct product.
Coverage radius uses `sqrt(AreaFactor)`, not the area factor itself.

| Family | `perLevel` unit / consumer |
| --- | --- |
| Damage, Strikes, Area, Duration | Additive fractional bonus; factor is 1 + sum |
| Projectiles, Pierce | Additional volley members / targets; shared target-hit rules still apply |
| Split, Echo | Secondary child/repeat damage share, +0.1 per support level; split children at most two, echo at most one per node |
| Attraction, Stride, Vitality | Additive fractional pickup-radius, walking-speed, max-health bonus |
| Armor | Additive rating; downstream incoming damage scales by 1 / (1 + rating) |
| Regeneration | Health per active combat second |
| LandingBlast | Endpoint explosion base damage, +0.2 per support level |

`HeroBonus(family)` returns the hero-wide sum. Dash distance is unaffected by
Stride. `LandingBlastCapabilities` has only Damage and Area: Might and Reach
apply, while Tempo, Echo, Split and projectiles cannot create extra dashes or
explosions. Its own support level supplies its base damage.

## Activations, persistent weapons and empowerment

Each owned `WeaponState` retains `StrikeCarry`, `ActivationSequence`,
`PulseSequence`, `PersistentActive`, `PersistentUntil` and `Empowered` across
level/form changes. No consumer should replace it when a form changes.
Cadence (.3) owns fractional carry and pulse scheduling; it preserves carry
through tempo changes/holds and schedules no backlog after a hold.

`Empower()` sets one nonstacking flag on every currently equipped weapon.
Newly acquired weapons start unempowered. `BeginActivation(kind, hasTarget)`
consumes only that weapon's flag and advances its activation sequence only
when a target exists. It returns an **area** multiplier, 1 or 1.5, to capture
for the whole activation, including extra strikes and split/echo children.
It is separate from ordinary area scaling and does not multiply damage.
Persistent weapons consume at their next authored damage activation/pulse,
not every frame that their visuals exist. The scheduler must capture the
returned multiplier for every strike belonging to that activation.

New event kinds are appended to preserve legacy wire values:
`WeaponUpgrade`, `SupportUpgrade`, `WeaponActivation`, `WeaponPulse`.
Their factory comments in `SimEvent.cs` specify every field. Downstream
systems emit these as they implement actual behavior; this foundation does
not emit duplicate combat events alongside legacy attacks.

## Mechanical lineage and hit windows

Secondary execution order for simultaneous mechanics is split, then echo,
then kill effects (`EffectTrigger` order). `EffectLineage.TryChild` shares a
32-node budget over one proc tree. A trigger cannot recur along its branch;
each node permits two split children, one echo and one kill child. Depth is
at most three, and sibling fan-out spends the shared budget. Use the returned
child for further triggers, never a new root; create roots for primary
strikes/projectiles. Normal stat growth and independent primary attacks are
not restricted by the proc budget. This helper allocates bounded tree nodes;
full-build execution may pool them if measurement requires it.

`TargetHitLedger` belongs to an activation or persistent effect. Call
`BeginWindow(index)` then `TryHit(targetGenerationId)` for each candidate.
A target includes spawn generation to avoid hitting recycled enemy slots as
the old enemy. No overlapping shape/volley member can hit the same target
again within a window. Repeating `BeginWindow` with the same index does not
reset it; going backward fails. Memory is reused and bounded by the targets
in the current window.

| `HitWindow` | Shared window |
| --- | --- |
| Activation | Entire activation, including overlapping volley members (Bow/Lightning initially) |
| Strike | Each authored independent strike (Sword/Hammer/Spear initially) |
| Pulse | Each authored damage pulse, never contact/render frames (Orbit/Field/Sentinel initially) |

Split and echo execution must carry the parent's target policy and explicit
window ownership; they cannot silently reset a shared ledger to farm a boss.
If a weapon deliberately permits independent repeated contact, author a new
strike/pulse window. This avoids accidental unlimited damage on large targets.

## Transitional boundary

`Arsenal`, `Upgrade`, old content and existing replay/loadout encodings remain
compiling adapters for the archived combat. Their three/four-slot rank API is
kept intact until consumers migrate; it is not the survivor build. New code
uses `SimState.Build` and the contracts above. This foundation deliberately
leaves legacy attacks/replay hashes unchanged. As downstream systems begin
executing the survivor build they must migrate loadout/session encoding,
state hashing/snapshots, offer cards and presentation together, then retire
the obsolete adapters. No asset mapping, boss or visual completion is claimed
by these schema tests.
