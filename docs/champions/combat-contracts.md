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

## Executable cadence and supports (ps-3wc.3)

`Simulation.Weapons` now owns survivor timing. Before play, each family binds
its weapon with `Bind(kind, hasTargetAtSongTime, activate, optionalBaseSeconds)`.
Only bound, owned weapons execute, so unported families do not double-fire
alongside the legacy Arsenal. The simulation drives committed beats,
confidence changes and fallback pulses; family code does not drive a second
clock. Bind identical handlers on live/replay construction. Final assembly
still owns survivor loadout/session serialization and removing legacy actions.

Starting seconds cadences: Sword/Bow/Ember Field/Sentinel 1, Hammer 1.5,
Spear/Lightning .75, Orbit Blades .5. Nearest allowed beat counts are
1/2/3/4/6/8 (ties longer). Adding 3 beats avoids the old 174 BPM choice of
.69 s versus the authored 1 s. Existing 8% hysteresis and downbeat-only
reselection apply. Beatless uses each weapon's own seconds cadence. Due
activations during holds or without targets are discarded; fractional Tempo
carry and empowerment remain. No deferred backlog is accumulated.

`WeaponActivation` captures level, sequence, stats, empowerment and strike
count. +25% strikes gives 1/1/1/2 repeatedly; +10% gives a bonus on the tenth
successful activation. Only strike zero may animate the body; remaining
strikes are simultaneous spectral actions. Persistent weapons treat every
strike as a damage pulse (with `WeaponPulse` sequence events), never damage
per render/contact frame. The handler decides when to create/refresh its
construct, then uses `WeaponSupports.Active` in its target predicate if its
lifetime must gate pulses. A pulse does not automatically extend a lifetime.

Grid quantization is explicitly compensated in per-activation base damage:
`CadenceDamageFactor = chosenBeatCount * currentBeatPeriod / baseSeconds`
(or 1 beatless). Use `activation.DamageFactor`, which includes this factor
and Might/weapon levels. This is cadence tuning, separate from support
scaling; `Stats.ConnectedOutputFactor` is unchanged. It keeps 80/120/174 BPM
sustained output comparable without adding body animations or moving launches
off-grid. It is recorded in `WeaponActivation` event D5. Tempo only changes
fractional strike counts. This replaces the starting proposal's uncorrected
nearest-interval output, whose slower/faster songs differed substantially.

`Simulation.Weapons.Supports.Launch(activation, baseDamage, hitPolicy, launch,
authoredProjectiles, authoredTargets)` executes Multishot and independent
spectral strikes, capturing evaluated support levels. The launch callback
supplies geometry/flight or performs a melee/pulse query. Every action is a
`WeaponEffect`. At each actual collision call `Impact(effect, generationId,
songTime)`; apply `effect.Damage` only when it returns true. Pierce spends a
bounded distinct-target allowance per projectile; overlapping volley members
share a target ledger. Extra strikes/pulses have independent windows, even
for volley weapons; Multishot alone cannot multiply one boss hit.

Impact launches at most two Split children immediately through the same
callback. `ImpactTarget` tells geometry which collision seeded the branch;
children retain the original hit ledger and cannot re-hit its source target.
Echo is queued at launch and executes on the next reliable committed beat,
or .5 s later beatless. Echo deliberately owns one independent repeat window
per strike, shared by all of that strike's volley/split branches: one target
can take one ordinary hit and one reduced Echo hit, not recursive boss farms.
Split/Echo children carry captured empowerment, stats, damage shares, target
allowance and the same bounded `EffectLineage`. Never manufacture new roots
for secondary actions. Queued echoes due during holds are dropped, and
confidence changes rebase their fallback delay without catch-up. Projectile
collisions and split impact times remain continuous.

`VolleyCount`, `TargetCount`, `Duration`, `Refresh` and `Active` are the shared
parameter/lifetime helpers. Families provide base counts/durations. Persistence
scales only compatible durations; refreshing a construct does not consume
empowerment. Geometry must pause flight while held and retain its action's
ledger until flight/child work has ended. Use generation IDs, not recycled
slot indices. These callbacks are simulation actions; presentation should
observe events and must not call them to deal damage a second time.

## Persistent pickups and defensive supports (ps-3wc.5)

`Simulation.Pickups` owns drops independently of structure chunk loading and
boss state. `Progression.Killed(sim,type,t,x,y)` leaves XP at the death
position; `CollectExperience(sim,value,t)` is called only on pickup arrival.
The immediate-offer task owns how earned levels open cards. Level-up picks
no longer heal. Boss kills retain their full-heal hook.

`Content/progression.json.pickups` tunes baseline attraction (3.5 m), travel
speed (14 m/s), healing cadence (every 100 value-bearing kills) and recovery
(10% of current maximum health). Healing cadence does not spend combat RNG.
Attraction multiplies pickup radius by `1 + HeroBonus(Attraction)` for both
kinds. A drop travels to the hero after entering range, even if the hero
leaves its original cell. Cards, pause and ended runs stop pickup motion.

A sparse map merges each kind within a 1 m world cell, retaining its first
death position and adding all value. Departed chunks never evict it. Up to
256 attracted drops travel concurrently; excess stays stored. Preview draws
at most 384 local representations, prioritizing attracted drops, without
reducing their stored value. No time-based expiration or whole-world scan
runs during ticks. This bounds active/render work, not the number of cells
in an arbitrarily long explored world. Replays fold outstanding pickup
positions and values into state hashes.

`PickupDrop`, `PickupAttract`, `PickupCollect`: I0 stable representation ID,
I1 `PickupKind`; D0/D1 world position, D2 value. `PickupDrop` also reports
**total** merged value for the existing ID, not an additional grant. XP uses
experience units; healing uses fractions of maximum health. Collection emits
once and removes ownership before granting XP/healing. `HeroHeal` uses
`HealReason.Pickup` or `Regeneration`; D0 is actual clamped gain.

Vitality adds maximum capacity without restoring health. Armor divides
incoming damage by `1 + rating`; regeneration adds health per active combat
second (including bosses, excluding holds). Stride multiplies walking speed
by `1 + bonus`; dash travel is unaffected. All consume the hero-wide families
from `SimState.Build`, using authored support caps in `combat.json`.

The `pickups` still scene compares hollow pale diamonds and crosses against
solid yellow shots in the palette crowd, with scripted attraction motion.
[Evidence and limitations](../../rhythm_champions/evidence/pickups/README.md).

## Sword and Hammer execution (ps-3wc.7)

`SimConfig.SurvivorWeapons` installs `Simulation.Melee` after the world systems
are ready; it binds Sword and Hammer once. New normal games opt in through
`RunInputs.SurvivorWeapons`; session format 5 persists this switch, while older
sessions keep archived behavior. The legacy Arsenal drains timing queues but
executes no attacks in survivor mode. Other family tasks should reuse this
switch and the existing scheduler for their bindings, not create another clock.

`MeleeAction` carries captured weapon/form, spectral/mirror/sequence flags,
origin, aim, actual coverage radius and advancing distance. Presentation never
queries current build stats to reconstruct an old strike. Sword's main strike
uses the natural knight slash; the four forms and all extra actions have
independent rigid mesh choreography. Hammer's procession advances its landing
over three successive activations; visual upcoming hammers deal no damage.

`Strikes.LandWeapon` is the survivor damage path: captured `WeaponEffect` stats,
shared support hit ledger, no archived mod reapplication. It reaches registered
enemies, bosses and structures. Enemy ledger keys use `Enemy.Generation`, a
monotonic spawn identity, so recycled slots remain valid new targets. Other
receivers occupy a separate key namespace. Nested split queries use separate
reused target buffers. Each added melee component shares its strike ledger;
one target takes at most one base hit and one bounded reduced Echo per strike.
See [runtime evidence](../../rhythm_champions/evidence/melee-weapons/README.md).

Ranged geometry may pass collision `x, y` as optional final arguments to
`Impact`. Split children expose captured `ImpactX/ImpactY`, retained through
Echo delivery; use these instead of looking up an enemy slot that may have
moved or been recycled. Structure impacts use the same positional contract.

`Strikes.LandSupported` is the finite-flight counterpart to `LandWeapon`: it orders collision candidates by distance before spending a projectile's target allowance, skips its Split seed body and supplies captured collision coordinates. Both bypass archived modifiers and use the same generation/receiver key namespaces.
