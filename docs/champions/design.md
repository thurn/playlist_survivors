# Rhythm Champions: Game Design

## Introduction

**Traditional horde survivor gameplay with rhythm elements layered on top.**
Move through a neon horde, make room with automatic weapons, collect the
experience left behind, and build a combination whose scaling becomes
spectacular. The music synchronizes the action. It does not turn ordinary
horde survival into a sequence of required button presses.

Boss fights are the exception: they demand rhythmic defensive play amid
small, deliberately timed reinforcement groups. Most patterns test timed positioning; occasional
clearly signaled waves require a dash through danger. Your weapons keep
attacking throughout.

The vision remains "the game that doesn't need a trailer": every moment of
play is cinematic. Retro 3D neon wireframe, the white hero, the endless dark
grid, the beat wave and the trailer's visual baseline remain the direction.

The November 3 restoration is implemented; its exact tuning and validation
limits are in [game-feel-plan.md](game-feel-plan.md) and
[assembled evidence](../../rhythm_champions/evidence/game-feel/README.md).
The subsequent ps-oli pacing change uses bosses after 3/6/9 horde minutes,
a 3.5 m starting collection radius and contact-only big ork/troll elites.
Three normal scripted builds finish in 10–12 minutes with 28–30 normal choices;
first choices range from 15–33 seconds and fights from 8–100 seconds.
These are deterministic observations, not operator hand-play approval; see
[three-minute pacing evidence](../../rhythm_champions/evidence/three-minute-pacing/README.md).

Combat and progression were redesigned with the operator on **2026-11-02**.
The combat-feel decisions approved on **2026-11-03** below supersede conflicting
November 2 behavior. This document also supersedes the dash-centered design
locked on 2026-11-01.
The [previous design, plan and style guide](../archive/champions-dash-combat-2026-11-02/README.md)
are historical. [The implementation plan](plan.md) records shipped survivor
combat and the remaining verification. The ps-3wc implementation now supplies
the eight weapon identities, broad supports, pickups, immediate choices and
three bosses. The [combat-feel restoration plan](game-feel-plan.md) distinguishes
that shipped baseline from the approved changes still to implement in ps-ueo. [Assembly evidence](../../rhythm_champions/evidence/survivor-assembly/README.md)
separates deterministic run results from pending hand-play and visual judgement.

### The game, as the player sees it

- Pick an app playing music, or a local file or playlist. The game plays
  its audio 3 seconds late so it can anticipate the beats.
- Move a white knight through neon monsters. Weapons attack automatically
  on the beat; your position determines what they reach and where it is
  safe to go next.
- Kill enemies, return through the space you cleared, collect XP, and
  choose one of three upgrades. Choices are frequent and immediate.
- Combine up to six weapons and six supports. Damage, extra strikes, area,
  projectiles, duration and mechanical interactions compound into a build.
- Weapons visibly change form as they level: a hammer's swing becomes a
  ground slam, then more elaborate actions. These are fixed progressions;
  supports supply mechanics such as piercing, splitting and repetition.
- Space dashes immediately and always ends in a full-circle sword sweep,
  clearing a landing pocket of weak fodder. One on-beat dash immediately
  grants a yellow aura: all attacks cover twice the area for three seconds.
- Every three minutes of horde time, a boss arrives. Ordinary spawning and
  the progression clock stop; ordinary enemies in the visible fight area
  shatter and outside hordes cannot immediately refill it. Survive rhythmic
  patterns while your weapons fight. Kill three bosses to finish level 1.

### Principles

- **Positioning is the core input.** Horde combat is winnable with good
  builds and movement without deliberately timing inputs to music.
- **Build choices are frequent and consequential.** Small statistical
  gains compound; new weapons, supports and form changes alter the play.
- **Dash makes room.** Automatic weapons sustain combat; every dash also
  ends in a sword sweep without requiring a support or a rhythm judgement.
- **Rhythm is visible and playable.** Weapons attack on the beat, enemies
  step to music, timed dashes empower weapons, and bosses require timing.
- **Every moment cinematic.** Natural animation, readable threats and dark
  space survive even when a build produces many effects.
- **Silence breaks nothing.** Every musical timing has a seconds fallback.
  Boss patterns remain readable without reliable music.

## Decisions

These are settled design decisions. Numerical values outside this section,
weapon/support names and the proposed content roster are starting points for
hand-play tuning, not claims of tested balance.

1. V1 is one level with bosses after **3, 6 and 9 minutes of horde time**.
   The progression clock pauses during each boss, cards and player pauses.
   Actual run duration includes the boss fights.
2. Boss arrival **shatters ordinary enemies in the visible fight area**.
   Existing XP, healing drops and structures remain. Ordinary spawning stops;
   bounded arena/re-entry behavior prevents immediate refill by outside hordes.
   Only small, deliberately timed reinforcements are allowed, with clear lanes
   and a finite XP budget; the arrival wipe is not an XP jackpot.
3. Bosses remain damageable throughout. Most patterns permit timed walking;
   occasional waves require dash invulnerability. Mistakes cost health.
   There is **no Break bar**, dodge-gated damage or required stagger cycle.
4. Up to **six automatic weapons and six supports**. The permanent held Sword
   occupies one weapon slot; the other five weapons support it. Stat passives and
   mechanical supports share those six support slots and apply to every
   compatible weapon automatically. No prescribed weapon/support recipes.
5. Weapons have **eight levels**, changing to fixed new forms at **3, 5 and
   8**. Each form changes the action and animation. Existing support
   interactions survive; a new form may add compatibility.
6. Different stat families multiply together; ordinary bonuses within one
   family add. Small upgrades must have real value. Supports can add
   mechanics as well as numbers.
7. Automatic attacks stay **strictly on the musical grid**. Attack-speed
   upgrades add strikes, rather than forcing faster character animation or
   requiring the player to press attacks. Fractional extra strikes accumulate.
8. Dash is immediate on key-down with consistent movement and invulnerability.
   **Every dash ends in a full-circle sword sweep** at its actual legal endpoint,
   including when obstructed, without waiting for a beat. Weak fodder dies;
   tougher enemies survive. Sword upgrades improve both ordinary swings and
   landing cleave. Movement remains free during attacks. Dash-exit body spin
   takes precedence over conflicting automatic choreography without duplicate damage.
9. **One on-beat dash** immediately grants the full **three-second yellow aura**:
   **2× coverage area for all attacks**, including that dash's landing sweep,
   with no damage-per-hit bonus. Refresh duration, never stack. An off-beat
   dash does not cancel it. No streak or per-weapon banked flags. Ordinary
   Sword aims along movement, retaining last facing when stationary; never
   toward the nearest enemy or densest crowd. Starting Sword one-hits the
   weakest fodder. At least double the baseline automatic attack rate across
   the roster, preserving identities and hit damage; Sword starts near 0.5 s.
10. Enemies drop XP. Collection has a generous baseline radius, upgradeable
    by a support. Routing through cleared ground is part of getting stronger.
11. Target **25–35 normal level-up choices** in a completed run, first around
    20–30 seconds. Each immediately holds combat for **three cards, pick one**.
    No rerolls, banishes or waits for quiet music.
12. Healing comes from occasional drops, optional defensive supports and a
    full heal after each boss. Level-ups do not heal automatically.
13. The first two bosses also grant one three-card choice upgrading an
    equipped item. It can cross a normal form breakpoint. The third ends
    the level; there is no separate evolution system.
14. Cards and the combat HUD stay brief. Icons and a little extra information
    may clarify a choice; dense build spreadsheets and a permanent build
    strip are not the default UI.
15. The weapon roster expands to martial and magical behaviors: melee,
    projectiles, orbits, ground effects, lightning and summons.
16. Free movement uses WASD, arrows or click-to-move. Enemies do not body-block
    the hero; overlap hurts outside invulnerability. Dashing through packs
    must be a viable escape or collection route.
17. The visual direction, music sources, calibration, Mac target and
    technology remain as described below and in the implementation plan.

## Terms

| Term | Meaning |
| --- | --- |
| Run | One playthrough; V1 contains level 1. |
| Horde time / progression clock | Time advancing the spawn ramp and 3/6/9-minute boss schedule; frozen during bosses, cards and pauses. |
| Combat time | Time actually playing, including boss fights; excludes cards and pauses. |
| Weapon | An equipped automatic attack, occupying one of six weapon slots. |
| Support | A passive stat or mechanic occupying one of six support slots; affects everything compatible. |
| Upgrade | A card granting a weapon, support or level in one already owned. |
| Weapon level | 1–8; acquisition grants level 1. |
| Form | A fixed action/animation at levels 1, 3, 5 or 8, retaining prior support interactions. |
| Empowerment | A refreshed three-second yellow aura granting all attacks double coverage area, without extra hit damage. |
| Beat / downbeat | Analyzer-committed beat / first beat of a bar. |
| Heard time | The time the player hears, used for input judgement. |
| Reliable beat | A musical grid sufficiently confident to use; otherwise use fallback timing. |
| Fodder / elite / boss | Common contact threats / tougher contact threats / rhythmic encounters. |

## The Run

### Core loop

Find a lane, let your weapons open it, collect XP from kills, choose an
upgrade, and exploit the resulting build. Moving away indefinitely gives up
uncollected growth. Diving for every gem risks health. Weapons with different
coverage change which routes are productive: frontal weapons cut corridors,
orbits reward maintaining a distance, ground effects reward looping back,
and projectiles reach threats outside the immediate pack.

Start with the permanent held Sword, level 1, and the dash with its baseline
landing sweep. Sword always occupies one of the six weapon slots. No mandatory opening
upgrade menu: the first meaningful choice comes from early XP. All roster
items are available in the run's pool in V1; between-run unlocks and permanent
power are not part of this revision.

### Level 1 schedule

The three encounter slots and clock behavior are fixed. This roster and the
minute-by-minute mix are the current starting content set; the second boss
reuses the existing troll asset with distinct scale, presentation and moves.

| Horde time | Event |
| --- | --- |
| 0:00 | Skeleton, slime and goblin groups, with occasional bats. |
| 2:24 | Big ork elites join as durable contact threats. |
| 2:42 | Demons join. |
| 3:00 | Fat Ogre: first rhythmic boss. Clock and ordinary spawns stop. |
| Ogre dies | Full heal, equipped-item upgrade; resume horde time from 3:00. |
| 3:00–6:00 | Troll elites and ranged mushrooms join; density and mixed pressure grow. |
| 6:00 | Troll Conductor: alternating fan lanes, falling marks and a traveling wall. |
| Conductor dies | Full heal, equipped-item upgrade; resume horde time from 6:00. |
| 6:00–9:00 | Densest mixed horde; developed builds demonstrate their combinations. |
| 9:00 | Dragon: final rhythmic encounter with controlled reinforcement groups. |
| Dragon dies | Victory, regardless of remaining fodder. |

Stop ordinary spawning and freeze the progression clock exactly at the
threshold. Give a short arrival warning, then bring the boss in on a nearby
downbeat (default within 4 seconds; fallback after 4 seconds). Existing
combat continues during the warning. Do not wait up to a minute for a strong
section. Boss duration does not advance the next encounter or density ramp.
The current Conductor uses alternating marked fan lanes and a traveling wall,
with at most three groups of three weak reinforcements (finite XP budget).
Bosses are tuned to 150/350/650
health; stronger broad builds can finish much faster than focused builds.
Target fights of about 1–2 minutes each, the dragon up to 3; a completed run
now includes nine horde minutes plus fights; measured scripted combat lasts
about 10–12 minutes, plus time choosing cards (see [pacing evidence](../../rhythm_champions/evidence/three-minute-pacing/README.md)).

### Starting, pausing and ending

Title: **Play**, **Calibrate**, **Quit**. Play selects an app or **File…**.
Start promptly when audio is ready; silence never holds the game. Esc or
focus loss pauses. Pause offers Resume, Calibrate, Quality and End run.
The music continues while combat is held; resume without a backlog of attacks.

Death shatters the hero. After 1.5 seconds, show time survived, kills, build
icons and Again. Victory uses a lit end card. Report actual combat duration,
including bosses, rather than labeling horde time as total survival time.
Again keeps the same music source. No victory-wide horde wipe is required
before the result; stop combat when victory is determined.

## Controls and Hero

WASD/arrows move; opposing keys cancel and diagonals are normalized. Click
walks toward a ground point; holding follows the cursor. Keyboard movement
cancels the target. There is no manual aiming. F cleaves in place; Space dashes
in the movement direction, or the last facing when stationary. Cards accept
click, 1–3, or arrows and Enter. Ignore held keys until release across a card
transition, so a choice cannot cause an accidental dash. No gamepad in V1.

Starting tuning: 6 m/s movement, 100 health, 0.5 seconds of invulnerability
after taking a hit. Enemies do not block movement; structures do. Touch damage
must not multiply by every overlapping enemy on one frame. Armor, health and
movement supports allow defensive investment but should not erase the need
to read boss patterns.

The hero remains the white Synty knight with a permanent sword in hand.
Ordinary Sword swings use natural body animation and broad slash effects,
aimed along movement (last facing while stationary). Supporting weapons and
bonus strikes can be spectral constructs;
new forms need distinct actions, poses or effect choreography, not only a
larger copy of the original effect. The body never needs to animate faster
than 1.15x. Concurrent weapons and bonus strikes use spectral weapons and
summons so six weapons do not demand six conflicting body animations.

## Dash and Rhythm Reward

Starting tuning: 4 m in 0.15 seconds, 0.6-second cooldown in real seconds,
invulnerability during travel and 0.1 seconds afterward. Buffer a press in
the final 0.15 seconds of cooldown. Dash starts on the input frame; no waiting
for a beat. It crosses enemy bodies and projectiles, but not intact structures.
A dash passing a click target clears it. F starts the same full-circle
cleave with a 0.15-second natural spin windup, while ordinary walking and click
targets continue unchanged. F shares Space's cooldown and final 0.15-second
buffer: the latest eligible buffered press chooses the action, except an
on-beat press is retained over an off-beat replacement. Simultaneous keys
start only the first action received. Held F cannot repeat or select cards.
F retains the same 0.25-second invulnerability window and rhythm reward,
including beatless fallback, with no dash travel, trail or travel sound.
The sweep hits once at the hero's position after its windup; its sword,
support and aura scaling and interruption of automatic body attacks match
the dash's landing sweep.

Timing judgement is binary in heard time: default ±80 ms around a beat,
±60 ms around half beats or strong musical onsets. Off-beat dashes retain all
movement and invulnerability. With no reliable beat, every dash earns
empowerment; the player never loses the bonus because analysis failed.

Every dash ends in a full-circle sword sweep at the actual legal endpoint,
not the intended unobstructed destination. The hero performs the sweep; no
support, target acquisition or beat wait is required. It reliably clears
ordinary weak fodder locally, while tougher enemies can survive. Sword levels,
damage and area upgrades improve both attacks. A landing spin overrides a
conflicting automatic body action; it must not apply the same hit twice.

An on-beat dash or F cleave grants the full yellow aura immediately, even in empty space.
It lasts **three seconds of active combat time** from the judged dash start.
Another qualifying dash resets expiry to three seconds from that start,
without stacking; off-beat dashes leave the expiry alone. Cards and pauses
freeze remaining duration, even while music continues. Replay state and hashes
include the expiry; the state is active before expiry and inactive exactly at
expiry. Newly acquired weapons benefit while the aura is active. With no
reliable beats, every dash grants the same aura.

Empowerment is **2× coverage area**, with no damage-per-hit multiplier.
Circular radii scale by sqrt(2), multiplicatively with ordinary area upgrades.
Each weapon defines its useful coverage measure (melee footprint, projectile
hit footprint, lightning search reach, ground radius or summon attack area).
A narrow projectile widens without losing range. Include the triggering dash
sweep and target selection; an enlarged visual without enlarged hits is wrong.

Persistent attacks must visibly and mechanically return to ordinary coverage
at aura expiry. Sample the shared state for active contact/pulses; do not bank
the bonus for a persistent object's lifetime. Discrete launched attacks retain
their launch footprint, while later attacks use the state at their activation.
Enlarged orbits/summons must not create an inner blind spot. A fading local
yellow aura communicates remaining time without a combo or upkeep meter;
cosmetic fade must not prolong mechanical coverage.

The old **Landing Blast** support is redundant with the baseline sweep. Retire
or rework it as a distinct optional enhancement; it cannot gate the sweep,
duplicate its base damage, add a weapon slot or trigger recursively. Document
the chosen migration and keep saved/replay content interpretation explicit.

## Weapons and Scaling

### Beat timing and extra strikes

Automatic damage activations use the committed grid: beats, half beats and
appropriate subdivisions. The responsive dash-exit sweep is the exception: it
lands at dash end without a beat wait. Base attack cadence is authored per weapon; as a
starting point, Sword uses subdivisions nearest **0.5 seconds** (previously
one second), with at least twice the old baseline activation rate across the
roster and no compensating halving of hit damage. Cadence is held stable
until a downbeat at a meaningful tempo change. No music uses the same weapon's
seconds-based fallback. Flying objects move continuously; their launches and
scheduled damage pulses are rhythmic, not their collision timestamps.

Attack speed supplies extra strikes at those activations. For example, +25%
adds one strike every fourth base activation; +100% adds one every activation.
Keep a deterministic fractional accumulator rather than rounding away small
upgrades. Bonus strikes are spectral simultaneous hits or authored grid-bound
follow-ups, not accelerated body animation. Preserve accumulated progress
across tempo changes; never release a catch-up burst after a pause.

Slow and fast songs must not decide whether a build is viable. Tune base
cadences and strike budgets together to preserve comparable long-run output
across supported tempos. This is a tuning requirement, not permission to move
attacks off-grid. Orbit and ground weapons gain extra damage pulses; summons
gain extra attacks. Attack speed never changes walking or dash speed.

### Multiplicative families

A useful model is base hit × damage factor × effective strikes × targets
reached. Damage bonuses add within damage; strike bonuses add within attack
speed. These families multiply each other. Area, projectile count, duration,
piercing and splitting improve coverage or repeated contact in weapon-specific
ways, not a universal hidden damage multiplier.

Example: +50% damage and +50% extra strikes give 1.5 × 1.5 = 2.25× sustained
single-target output when the strikes connect. Another +50% damage changes
the damage factor to 2.0, not 2.25. Extra projectiles do not automatically
multiply damage against one target: each weapon defines whether overlap can
hit repeatedly. Ground ticks, orbit contact and chains need per-target hit
rules so large enemies cannot take accidental unlimited hits.

Mechanical effects have explicit ordering and bounded generations. A split
projectile cannot recursively split forever; repeated attacks cannot repeat
themselves; triggered damage cannot create unlimited kill chains. These are
rules that make combinations understandable, not caps on ordinary stat growth.

### Weapon forms

Acquisition grants level 1. Levels 2, 4, 6 and 7 provide useful statistical
improvements; 3, 5 and 8 introduce a new action and animation. Later forms
retain prior support behavior and effective coverage: a form is an upgrade,
not a forced trade that invalidates the build. Adding an area component must
not remove a previously supported projectile component.

Shipped V1 forms (2026-11-03). These use owned meshes, authored spectral
choreography and evaluated existing knight poses; they do not imply a separate
humanoid clip for every action. Family evidence records inspected Unity motion:
[Sword/Hammer](../../rhythm_champions/evidence/melee-weapons/README.md),
[Spear/Bow](../../rhythm_champions/evidence/spear-bow/README.md),
[Orbit/Sentinel](../../rhythm_champions/evidence/construct-weapons/README.md),
[Lightning/Ember](../../rhythm_champions/evidence/magic-weapons/README.md).

| Weapon / positioning | Level 1 | Level 3 | Level 5 | Level 8 |
| --- | --- | --- | --- | --- |
| Sword / opens a lane along movement or last facing | Sweeping slash | Cross-cut combo | Spinning cleave | Three advancing spectral blade sweeps |
| Hammer / close crowd clearing | Heavy side swing | Ground slam | Vaulting spectral hammer and rising fissure blades | A procession of hammer falls on successive activations |
| Spear / long narrow lanes | Spectral thrust | Three parallel thrust lanes | Five spear heads sweep into line and thrust | Three advancing staggered ranks of five spears |
| Bow / distant threats | Arrow shot | Fan-shaped volley | Arcing volley that descends into the target area | Eight rotating spectral bows releasing arcing arrows |
| Orbit Blades / manage distance from the pack | Circling blades | Outward sweep and return | Alternating inner guard and outward scythes | A rotating crown opening into sweeping blade arms |
| Lightning / connected groups | Bolt to a nearby enemy | Raised forked lightning gesture | Storm sigil delivering the bolts | Moving articulated storm nodes delivering discharges |
| Ember Field / loop through cleared ground | A timed fire seal | Erupting fissure under the seal | Tilting faceted brazier pouring into the retained seal | Advancing erupting seal on successive activations |
| Sentinel / fight alongside a summon | Spectral sentry strikes nearby foes | Alternating parry and spectral combo lane | Leaping knight with a retained close spectral guard | Three advancing knights with staggered parry/leap poses |

Weapons auto-target according to their identity: nearby groups, nearest
threat, direction of movement or the ground where the player is standing.
Targets are chosen early enough that wind-ups remain legible; no frantic
mid-swing snapping. Bosses participate in normal targeting. Every starting
weapon must remain able to damage bosses, and support combinations should
allow both crowd-specialized and boss-specialized builds.

### Supports

All supports share six slots. Acquisition grants level 1; a recommended
starting cap is five levels. Each level improves its existing stat or
mechanic. No support requires one named weapon. Cards for new supports must
benefit something already equipped; broader compatibility can make them
valuable to later acquisitions too.

| Proposed support | Applies to / purpose |
| --- | --- |
| Might | Weapon damage, including the Sword landing sweep. |
| Tempo | Extra strikes/pulses/volleys for automatic weapons. |
| Reach | Attack coverage area, including the Sword landing sweep. |
| Multishot | Additional projectiles in compatible volleys. |
| Persistence | Duration of fields and temporary summons; not arbitrary melee damage. |
| Pierce | Projectiles continue through additional targets. |
| Split | Eligible projectiles create bounded child projectiles on impact. |
| Echo | Compatible attacks repeat on a later grid point at reduced damage. |
| Attraction | XP and healing pickup radius. |
| Stride | Hero walking speed; dash distance stays fixed. |
| Vitality | Maximum health. |
| Armor | Incoming damage reduction with diminishing returns. |
| Regeneration | Gradual health recovery during active combat. |
| Landing Blast (legacy) | Retire or rework without duplicating the baseline Sword sweep. |

Compatibility is explicit per weapon and inherited by every later form.
Supports also work across compatible secondary attacks, with the same
nonrecursive rules. Any reworked Landing Blast scales with damage and area, but cannot
be echoed into extra dashes or gain attack-speed-triggered explosions.

## Experience, Choices and Recovery

Kills drop XP at the enemy's position. Starting values: fodder 1, bats 0.5,
elites 10. The pickup radius starts at 3.5 m (Attraction adds 20% per level); attracted drops
move visibly to the hero. Merge nearby gems while conserving XP to control
clutter and cost. Uncollected XP persists through boss arrivals and fights;
chunk streaming cannot silently delete it. No forced magnet on boss entry.

Tune the XP curve for 25–35 normal choices over a completed run, including
XP collected during bosses. First level around 20–30 seconds. Killing faster
and taking productive collection routes levels faster; this is not a timed
upgrade dispenser. Boss rewards are two additional choices, not part of that
normal-XP target. Boss summons have a finite XP budget per encounter so a
player cannot indefinitely farm progression by refusing to finish a boss.

At level-up, hold combat immediately, including during a boss fight. Music
and harmless visual bobbing may continue. Freeze projectiles, telegraphs,
cooldowns and invulnerability together; restore the remaining reaction time
on a future musical grid point, never resume onto an already-expired hit.
Several earned levels show sequentially. Cards appear together, immediately
selectable, without roulette or a celebratory animation delaying the choice.

Offer three distinct eligible upgrades, weighted toward something owned when
possible. A weapon at level 8 and a support at its cap cannot be upgraded.
Full slots exclude new acquisitions of that type. No replacing an owned item
or skipping is required in V1; slot commitment is part of building. If fewer
than three legal upgrades remain, show those remaining rather than duplicates
or useless cards. If all equipped items are capped and slots are full, award
a small max-health increase directly; never present an empty menu.

Boss rewards offer upgrades to equipped items only, one choice, using the
same cap and fallback rules. Resolve the full heal and reward before queued
normal level-ups; ordinary spawning resumes after those choices.

Occasional healing drops restore a modest fraction of maximum health (try
15%). Tune their distribution to avoid long unlucky droughts; their locations
should invite a route, not require entering an unavoidable hit. Supports may
trade offensive power for armor, health or regeneration. Boss kills restore
full health. Level-ups have no healing attached.

## The Horde

The existing cast stays: skeleton, slime, bat, goblin and demon fodder;
big ork and troll elites; mushroom ranged enemies. They retain their neon
families and silhouettes. Fodder largely threatens through contact; elites
keep their bodies and contact damage without charge/slam specials or ground
warnings. Mushrooms remain ranged and fire avoidable telegraphed projectiles;
boss warnings and moves remain intact.
Walking and sensible builds must suffice here without deliberate rhythm input.

Starting speed range is 1.2–2.6 m/s against the hero's 6 m/s. Enemies step on
their musical intervals with distance adjusted to preserve average speed;
without reliable beats they glide. Ordinary fodder enters in small single-type
packs along local spawn arcs, preserving the authored mix and body budget;
bats, elites and formed waves retain their distinct roles. Keep dark gaps
between models and steer around structures. Contact damage is resolved by actual overlap, with hero
hit invulnerability preventing multi-enemy burst stacking.

Start below the previous design's immediate 150-enemy crush if needed to make
first collection routes readable; try 50–100 visible, rising toward 600 late
in horde time. Loud sections and formed waves can surge toward 1,000, but must
not remove all readable escape routes. Use horde time, not boss duration, for
ramping. Formed waves arrive on section changes; silence uses the base ramp.

Starting Sword must one-hit the weakest fodder; tougher types need multiple
blows. Difficulty comes from numbers, approaches and tougher types. Weapon
investment expands clearing power. Rapid small-hit and persistent-area builds are
valid. Elites survive longer and test a build's concentrated damage. Bosses
must remain readable among controlled reinforcements; their summons are deliberate
patterns, not the ordinary spawn ramp running in disguise.

## Bosses

### Encounter rules

At each threshold, stop ordinary spawning and pause horde time. Warn, then
introduce the boss and visibly shatter ordinary monsters in the fight area.
Preserve existing XP/healing drops and structures. The arrival removal must
not award a normal mass-kill XP jackpot or recursively trigger kill farming.
Prevent outside hordes immediately entering, including if the hero walks a
few steps; use a bounded encounter population rather than a wall around the player.
At arrival, shatter non-boss bodies in a 42×28 m rectangle centered on the hero
and retire the rest of the old horde without drops. Ordinary spawning stays
stopped everywhere for the encounter, so walking beyond the original view
cannot reintroduce it. Existing pickups and structures stay in place.
Reinforcements have three opportunities, at 12/28/44 active seconds after
arrival, deferred until the current boss move finishes. Each warns one fixed
2 m ground circle for 2 seconds, then admits up to three skeletons in one
sector (10 m sideways and 8 m forward from the hero at warning time). The boss
waits another 2 seconds before attacking. A live group consumes later
opportunities without stacking; missed slots never replenish. Spawn positions
within 6 m of the hero at landing or inside structures are skipped, never
relocated. Maximum nine ordinary kills/nine XP per encounter, no entry XP,
and no endless summon farm. Warnings and opportunities freeze with cards and
pause; boss death cancels a pending warning. Bosses keep following and remain
damageable while a group is announced. Boss attacks may subsequently break
structures they actually hit.
The boss follows across the endless arena; running away cannot advance the
schedule or skip the fight. All ordinary weapons keep attacking.

No Break meter or damage gate. Hits do not stagger or knock back the boss,
and dodges do not unlock damage. Strong builds shorten encounters; tune health
so expected builds encounter the signature rhythmic patterns. Boss fights are
not timed invulnerability phases added solely to force a fixed duration.

Telegraphs use whole beats and readable expanding/filling shapes. Most attacks
can be avoided through timed walking. Each boss also has a distinctly signaled
wave whose geometry requires crossing with dash invulnerability. Successful
evasion depends on actual collision and invulnerability, not an invisible
rhythm grade: the separate on-beat judgement grants weapon empowerment.

Required waves need enough warning and spacing for the baseline dash cooldown,
including at high tempos. A previous mandatory dodge must never leave the next
one impossible. Account for structures and controlled reinforcements when placing
safe lanes. Projectiles may launch on beats and travel continuously; communicate
when they reach the hero, not just when they launch. Avoid filling the entire
screen with white weapon effects over the next required dodge.

When beat confidence disappears, retain the current telegraph's promised
impact time and use clear visual pulses with a 0.5-second fallback beat for
new patterns. Do not shorten an in-flight warning when tempo changes. Beatless
play still asks the player to time movement to visible patterns; every dash
gets the usual empowerment bonus.

### Proposed encounters

| Boss | Positioning patterns | Dash-through pattern | Escalation |
| --- | --- | --- | --- |
| Fat Ogre, 3 minutes | Club lanes, marked boulders on successive beats, directional charges | Expanding stomp ring with a clear arrival cue | Chain a lane attack into a ring, retaining a valid recovery interval |
| Troll Conductor, 6 minutes | Alternating projectile fans and gaps; finite summoned groups | A traveling pulse wall spanning the local fight area | Alternate wall and fan patterns, keeping the gap legible amid summons |
| Dragon, 9 minutes | Breath cones, tail sweeps, marked landing zones and fireball sequences | Concentric outward rings crossed on their arrival beats | Combine previously taught patterns without overlapping unavoidable hits |

The Conductor is a proposed use of the existing troll model, not a new vendor
asset requirement. Its boss scale, silhouette treatment and authored animation
must distinguish it from an elite. Encounter identities can be refined during
implementation while keeping three distinct rhythmic tests.

## The World and Music

The arena remains an infinite dark grid with loose clusters of destructible
buildings, trees and rocks. Structures block hero and enemies, but attacks
break them and boss attacks can destroy what they hit. Small structures take
one hit, larger buildings a few. Structures do not drop XP or healing by
default. Broken structures stay broken; seed and coordinates determine chunks.
Grey line art remains reserved for unbreakable structures in later levels.

Four floor keys crossfade at musical section changes: two cool, a violet and
a dim warm. Without sections, change every three minutes of horde time.
The final boss uses the warm key. Monsters retain their palette families.

Delayed listening captures an app, mutes it and plays its audio 3 seconds late.
Local files and playlists follow the same analyzer path. There is no Direct
listening mode. Video and input calibration are stored per output device and
available from the title and pause menu. Nothing pauses because a song ended.

| Music drives | Without a reliable beat |
| --- | --- |
| Automatic attack activation and extra-strike choreography | Weapon-specific seconds cadence |
| Dash empowerment judgement | Every dash earns empowerment |
| Monster steps | Continuous motion at the same average speed |
| Beat-wave light | Event rings only |
| Boss patterns and telegraphs | Readable visual pulses, 0.5-second beat |
| Spawn surges and formed waves | Horde-time ramp |
| Zone transitions | Every three minutes of horde time |
| Boss arrival accent | Short fixed warning then arrival |

Music does not determine upgrade timing, progression-clock speed, movement
speed, dash cooldown or whether a level-up is permitted during a boss.

## The Look

Reopened on 2026-11-02. The first version of this section misread the
reference trailer as broken and replaced its drawing with "edge cuts": a few
chosen lines over a dark body, gated by a black-share threshold. They passed
every number and were a severe visual regression, so they were deleted. This
section starts again from the trailer.

### The aim: retro 3D neon wireframe

The look is **retro 3D neon wireframe**, grounded in synthwave: low-poly 3D
models drawn as glowing lines along their edges on black, like the wireframe
mountains, palm trees and cities of 80s synthwave art. The operator's
references are in `captures/style-references/` in the main checkout. They're
third-party images, so they stay outside the tree:

- `synthwave-mountains-palm.png`: low-poly mountains and a palm tree with
  every facet outlined, over a neon grid floor and a striped sun.
- `wireframe-mountains-grid.png`: triangulated mountains either side of a
  grid road.
- `wireframe-objects-elephant.png`: a low-poly elephant head, a sphere, a
  torus and a terrain patch, every edge drawn.
- `neon-wireframe-plane-tunnel.png`: a paper plane of a dozen glowing edges
  in a neon tunnel.
- `synthwave-city.png`: towers drawn as neon edges of simple faceted solids.

What they share, and what the monsters aim for:

- **A 3D model, not a flat drawing.** Each object is a model with a
  deliberately simple, low-poly topology, and its edges are the lines. The
  lines describe volume: facets, planes, bands around a form.
- **Built from simple shapes.** A mountain is a few big triangles, a palm
  frond a strip of quads, a building a box. A monster is usually best built
  the same way: a few simple faceted volumes (head, torso, limbs, weapon)
  joined together. It's a guideline, and some monsters need more.
- **Every line connects.** Each facet is a closed polygon and lines meet at
  vertices, so the silhouette is closed too.
- **Hidden lines are removed**: a dark body hides the lines behind it, and
  the inside of every facet stays dark.
- **One neon hue per object**, an even line weight and a soft glow.

What misses it:

- lit facet patches or glassy fills (the first port's stills);
- broken or blotchy scratches (the deleted edge cuts);
- so many tiny facets that the lines merge into a fill (the trailer's
  goblins).

### The reference: the trailer

The closest the game has come is the neon action trailer in the frozen
demo3d's edge preset, the operator's capture
`captures/trailer-action-edge.mp4` (outside the tree; made by
`just trailer-looks action`). **It looks good.** It is the renderer's
baseline: Rhythm Champions starts from exactly that look and changes it only
where a change looks better. Where it falls short of the aim, the cause is
mostly the vendor meshes. Their dense, uneven topology was made for textured
rendering, so some types scribble or fill in. The monster meshes fix that.

What the trailer does, described rather than prescribed:

- **Monsters** are their vendor meshes drawn by the look package's
  `MonsterEdge.hlsl`. Every crease sharper than 24 degrees is a thin line in
  the type's colour, a little brighter toward the silhouette. Underneath is a
  dark glass core whose facets are barely visible, so the lines carry the
  figure. A fresnel rim traces the outline, and a small inner glow flares on
  the beat.
- **Each type has one hue**, levelled to one luminance. A crowd reads as
  fields of colour, and each monster still reads as a figure: helmets,
  shields, weapons, limbs.
- **The hero** is drawn the same way in white, the brightest thing on screen.
- **The floor** is void under a quiet grid. Light comes from things.
- **Shards** keep the look: each piece is outlined along its crack, hottest
  on the break's frame.

Stills from the capture, in [reference/](reference/):

- [opening-duel.jpg](reference/opening-duel.jpg): three brutes and the hero
  up close, every plate of their armour outlined. The look at its best.
- [dragon-live.jpg](reference/dragon-live.jpg): the dragon in its duel (the
  capture at 0:58), long clean strokes for the wings and body.
- [shield-circles.jpg](reference/shield-circles.jpg) and
  [dense-swarm.jpg](reference/dense-swarm.jpg): the crowd. The skeletons
  read by their shields, and the orks by their bulk. The magenta goblins
  read as filled pink shapes rather than line art (see below).
- [dragon-shatter.jpg](reference/dragon-shatter.jpg): the dragon
  shattering. A reference for the shatter, not for a living monster.

### Changing the look

Changes from the trailer are targeted, and they are judged by looking:
side by side with the trailer's drawing of the same thing, at game scale and
in a crowd. The trailer's own drawing stays one flag away
(`--look trailer`), so any change can be compared directly.

Where to start (things to try, not orders):

- **The goblins** read as filled magenta objects, not retro vector art. Their
  small dense facets light so many creases that at crowd scale the lines
  merge into a fill.
- **Each monster gets a mesh designed for the look** ([Monster
  meshes](#monster-meshes)).
- Detail that turns to noise at game scale can go. Detail that names the
  type stays.

### Monster meshes

Each of the ten monsters gets a low-poly mesh designed as a retro 3D neon
wireframe ([the aim](#the-aim-retro-3d-neon-wireframe)), made from its
vendor model with the project's `monster-mesh-design` skill, one bead per
monster. The goal is a model whose edges draw a coherent, handsome wireframe
figure at game scale, in a crowd and up close, and that still moves with the
vendor rig and clips.

- **Simple faceted shapes joined together** are the usual way, as the
  references are built. It's a guideline, not a rule: some monsters need
  detail kept.
- **The topology is the drawing.** A designed mesh may draw every one of its
  edges (a true wireframe, as the references do) or only its creases over 24
  degrees, as the trailer does. The recipe says which, judged by eye.
- A committed recipe builds each mesh locally from the vendor files, and its
  output is never committed, like every vendor-derived file.
- Until the operator prefers a new mesh, the vendor mesh as the trailer drew
  it stays one flag away.

### Judgment over rules

- Everything in The Look is guidance under aesthetic judgment. When a
  guideline and a better-looking frame disagree, the frame wins.
- Numbers (luminance, lit share, line counts, separation) are diagnostics
  for finding problems. They never accept or reject art.
- Art is accepted by looking:
  - a blind critique of its stills by an agent that hasn't read the docs;
  - side-by-sides with the trailer's drawing of the same thing;
  - in the end, the operator's eye.

Habits the trailer keeps, worth keeping unless a frame says otherwise:

- **Light comes from things, not from the world.** Edges, rims, trails,
  sparks and the grid emit; the floor is void.
- **White is the hero's**: the hero, its effects and its summons. **Grey
  means a wall.**
- **A swarm reads as many figures with dark gaps between them**, not one
  coloured mass. This is what the goblins miss.
- **No monster is brighter than the hero.**
- **A frame tends to have a colour key**, one family holding most of it.
- **Point lights sparingly**, for effect: a slam, a boss entrance.

The archived style guide's value ladder is a rough guide: void for the floor
and the cores, ground light for the grid and fills, enemy light for monsters'
lines, hero light above it, and clipped white only for impact frames and the
blade's core.

### The palette

The trailer's families, with the empty arc given to roles:

- **Key** (the most numerous fodder: skeleton, slime, demon, bat): cyan to
  blue, in the floor's first zone family.
- **Accent** (goblin, mushroom): magenta to violet.
- **Counter** (big ork, troll, Fat Ogre): amber to orange.
- **Boss** (dragon): red, inside the counter family's reach.
- **Warning**: orange/red for spore warnings, shots and boss hazards.
  Yellow is reserved for the player rhythm bonus and empowered effects.
- **Zones**: two cool, a violet, a dim warm.
- **Hero**: white with white ordinary attacks; yellow aura and empowered effects.
- **XP**: green filled shapes with a distinct animated shader; healing remains
  separately readable by shape and motion.
- **Wall**: grey, reserved.
- Roles beyond monsters: warning, enemy shot, hero hurt, pickups and
  structure fill per zone.

The [style guide](style.md) defines the palette. Pickups must read apart
from enemy projectiles by silhouette and motion; tune their treatment by eye.

### The beat wave

- Every beat a ring leaves the hero's feet and travels about 11 m per beat.
- As it passes: grid lines brighten and bend slightly (Geometry Wars'
  ripple), monsters' lines flare and they bob, structures' edges flare.
- Downbeats push harder. An on-beat dash, a slam, a boss entrance and a mass
  kill send stronger rings of their own.
- The grid settles back to ground light within half a beat. Even a
  downbeat's ring never lifts the floor above ground light.
- With no reliable beat, there are no beat rings; event rings still go out.

### Shatter

- **Monsters** break on the impact frame into pieces of their mesh, each
  outlined along its crack as in the trailer, thrown forcefully away from
  the blow and up, tumbling, then contracting away while airborne (at most
  half a second). Weapons break and fly with the body. No settled debris or
  sinking phase: a kill earns actual empty space. No cracks first, no delay,
  no twinkling.
- **Structures** break the same way in their fill colour.
- **Boss entrances** shatter ordinary enemies in the visible fight area,
  preserving existing pickups and structures. A ring and arrival animation
  coordinate the spectacle with the clear.
- **The hero's death**: white shards from the hero.

### Weapon effects

- Hovl's slash shapes (the crescent mesh's taper, its noise streaks, sparks,
  fast-in slow-out timing), imported locally and drawn through our own
  shader: a white core, a faint white-blue edge.
- Empowered attacks: visibly larger, with a local yellow accent and timed aura.
  Ordinary beat-locked attacks retain their normal size.
- Ordinary hero attacks are white; empowered effects and rhythm aura are
  yellow. Enemy hazards use orange/red, preserving varied enemy body colors.

### Camera

- The reference trailer's high angle, about 60 degrees down, following the
  hero with a slight lead the way it moves. It never rotates.
- It eases out about 20% for boss fights.
- Punches and shakes only on special moments.

### Restraint

The look is calm by default. **Special moments**, the only events that may
flash the frame, shake the camera or bend the whole grid: boss entrances,
boss deaths, major weapon-form impacts, mass kills and structure breaks.
Routine empowered attacks and dash-destination explosions stay local;
repeating a common attack must not shake or flash the whole screen. Even
special moments never white out the frame, and their flash is gone within a
quarter second.

### Quality tiers

- **Progressive enhancement**: the look is designed at full quality; tiers
  shed extras, never the core look.
- The core, kept on every tier: the edge look, the beat wave, the grid,
  shatter (with fewer pieces on the low tier), bloom.
- The extras, high tier only or scaled: full-resolution bloom, distortion in
  slashes, glow halos on lines, more shatter pieces, chromatic flashes on
  special moments.
- **Low tier**: an M1 Air class Mac holds 1,000 monsters at 60 fps at 1080p.

## Sound and UI

The music stays foremost. Swings, impacts, shatters, dash whooshes and boss
warnings sit beneath it; the timed-dash accent reinforces empowerment.
Sound follows heard-time events without adding input delay. Music is not
ducked for ordinary combat or choices.
Sword windup notices are silent; the actual impact owns its single whoosh,
so cancelling an automatic windup with a dash cannot leave a ghost attack sound.

The hero's small white beat strip retains upcoming beat ticks and a dash
readiness pip. Replace the thin player health bar with a bottom-left health
orb: moving red liquid/energy, a clear fill boundary readable at low health
and amid bloom, optionally compact numeric health. XP is green and filled,
with a distinct animated shader and readable shape, separate from healing
and airborne orange/red enemy shots. The HUD keeps a hairline XP bar and
legible boss health with no Break bar. Top-center shows horde progress time;
during a fight it switches to **BOSS** plus that boss's elapsed fight time.
Cards and pauses freeze these clocks; music continuing must not advance them.
No permanent inventory strip, floating damage numbers or banners.

Cards remain icon, name and one short description. Prefer wording such as
"Larger attacks" or "Hammer becomes a ground slam" with a compact stat delta
or compatibility icon only when useful. Do not force a six-word limit if it
makes a choice misleading. Optional inspection may explain exact numbers,
compatibility and forms; ordinary selection must work without opening it.
A new form should be recognizable from its card icon and action after selection.

## Difficulty and Play-testing

Aim for learning over a few runs: first runs can reach the first boss;
positioning, build choices and boss timing improve subsequent attempts.
Do not prescribe dash investment as a survival requirement. Verify:

- A build with no dash-damage support can clear the level.
- Horde survival works without deliberate on-beat input; collecting XP and
  escaping through packs feels viable.
- Timed-dash empowerment is satisfying without dominating weapon investment
  or turning every safe moment into compulsory dash spam.
- Small stat upgrades matter; six weapons and supports produce distinct
  strong builds, including focused and broad builds.
- Every form changes action visibly and preserves prior support interactions.
- Level-up pacing restores strategic interest without exhausting the player;
  short cards communicate meaningful choices.
- Bosses teach rhythmic positioning, require occasional dash-throughs, and
  remain legible with the surviving horde and full weapon effects.
- Slow, fast, syncopated and beatless music all play fairly; pause and level-up
  resume never cause an unavoidable hit.
- XP persistence and readable pickups hold in a long, moving run.

These are hand-play questions, not claims that this revision has solved balance.
Keep numerical tuning subordinate to observed game feel and visual judgment.

## Scope and Future Work

V1 remains level 1 on Apple Silicon Macs with keyboard and mouse. Target
1,000 monsters at 60 fps at 1080p on an M1 Air class machine through quality
tiers; design the look at full quality. The Rust audio plugin, pure C# sim,
Unity renderer and tools remain the technology (see [plan.md](plan.md)).

Later levels, additional characters, permanent meta-progression, Windows and
gamepads remain future work. Martial and magical weapons, temporary ground
effects and summons are now part of this combat direction, not excluded
because the previous sword-and-dash prototype lacked them. A wider elemental
status system is not required to make the initial roster work.
