# Restore the Musical Power Fantasy

## Purpose and authority

Restore the combat, feedback and musical choreography lost while implementing
[Survivors Run](survivors-run.md). Vampire Survivors (VS) supplies a baseline
for balance, progression and monster density. It does not prescribe our attack
shapes, animation, HUD, sounds or relationship to music. The project's higher
priority remains: every moment of gameplay should be cinematic.

This is a plan, not an implementation report. Decisions were settled with the
user on 2026-10-27. They supersede conflicting retirements and fixed decisions
in survivors-run.md. Keep that document as the history and reference for the
run, progression, freezes, chests and existing weapons.

The audit compared the native game before the plan (`6a00a47f`) with landed
`2538811`. Read the earlier code through Git when recovering behavior; do not
revert the repository or replace the current engine with an older copy.

This plan explicitly includes restoring full legacy judging alongside the new
run. That is a scoped exception to the older instruction that legacy gets no
new work. It does not authorize unrelated legacy features or renewed TS work.

## Decisions settled in the interview

1. VS numbers are starting values, not requirements for exact equivalence.
2. Start with the sword alone, with omnidirectional damage.
3. The hero faces nearby enemies and performs real sword attacks: wind-up,
   strike and follow-through. A radial hit is compatible with a targeted,
   character-driven attack animation.
4. Starting sword reach is **190 px**. Each main beat delivers a full-strength
   hit based on the weapon's per-hit power. Remove tempo-based weakening:
   faster songs are allowed to produce more sword damage per second.
5. Keep Garlic, Magic Wand, King Bible, Lightning Ring and their evolutions.
   Restore chain lightning and fire trail as additional upgrade choices.
   Bible supplies the orbiting-blades role; do not add a duplicate weapon.
6. All enemy-controlled travel happens in sharp steps on **main beats only**.
   This especially includes bats. No offbeat, eighth-note or sixteenth-note
   travel. Slower enemies may skip beats; faster enemies travel farther per
   step. There must be a clear stationary hold between steps.
7. Without a reliable detected beat, enemies hold position. Do not invent a
   fallback pulse for enemy travel and do not switch them to smooth chasing.
8. Knockback and death launches may react immediately between beats. These
   are forced reactions, not permission for enemies to chase between beats.
9. Zen is unjudged. Its groove display follows the music's pulse and intensity.
   Legacy fully judges inputs, including groove gain/drain and gameplay effects.
10. Keep level-up card pauses. Restore the beat track, overhead beat marker and
    fixed health display, retaining useful inventory and run information.
11. Restore musical formations and early boss spectacle, but **not archers or
    projectile-dodging encounters**.
12. Keep the music's volume unchanged. Restore audible game audio, replace the
    severe effects clamp with a tunable mix and retain sensible voice budgets.
    Music ducking stays off by default.
13. Music influences enemy pressure as well as presentation: quiet passages
    provide slower, curling approaches; loud passages bring sharper advances
    and formations, within VS-based progression and density limits.

The following implementation defaults resolve remaining details. They are not
additional claims about what the user explicitly selected. Change a default
only with a recorded gameplay or measurement reason, without weakening the
decisions above.

## Combat that belongs to the hero

### Sword and accents

Use the existing `aim.rs` and old swing animation path as references. Choose a
direction from the present, hittable enemies at wind-up; keep the useful
crowd/boss weighting and nearest-target tie-break. With nobody nearby, retain
facing. Do not choose a direction from a future enemy pose or input.

Publish one attack identity that drives the character animation, 360-degree
damage, arc, afterimages, contact sounds and camera response. The hero must
visibly swing, rather than idle behind an independently spinning effect. The
radial hit and its visible radius must agree, including Area upgrades.

Use the current sword rank table's per-hit power and Might as the initial
normal-hit calculation. Delete the `period / 1.35` damage factor. Do not move
that compensation into enemy HP, a tempo multiplier, or another hidden path.
Keep the guaranteed main-beat swing. Existing Amount subdivisions can remain
additional attacks; the enemy main-beat rule does not forbid weapon subdivisions.

Restore an accent's stronger oversized slash and outward travelling cleave.
Start from the earlier heavy/light relationship and cleave recipe, expressed
relative to the current per-hit power. Define which hit replaces the ordinary
swing and which wave hits are additional; do not accidentally apply both old
and new primary damage. Keep per-attack/per-target deduplication.

Restore strong-onset cleaves with the existing causal scheduling and nearby
accent deduplication. A forecast accent and its onset must not create two
celebrations of the same attack. Legacy's judged accent rewards must retain
their existing timing requirements; zen launches its accents automatically.

Restore light/heavy character animations, different impact sounds, modest
attack-specific camera punches and readable follow-through. Ordinary movement
must not erase the strike pose. Camera motion is bounded, with the existing
general musical camera effects retained.

Keep beat-aligned world time in zen. Heavy-hit hitstop was already absent in
the previous default zen experience; adding world freezes is not required to
recover impact. Retain legacy's existing judged-hit behavior where compatible
with its original rules. Do not reintroduce broad time warps that make strides
or enemy footfalls miss the heard beat.

### Restored weapons and growth

Integrate chain lightning and fire trail into the survivors inventory, offer
table, snapshots, recorder, sounds, damage recap, icons and cards. Reuse their
existing implementations where suitable; do not run a second hidden loadout
alongside survivors slots. Audit hardcoded five-weapon lists and exhaustive
matches, not just the weapon enum.

Keep six weapon slots and six passive slots. Seven base weapon choices now
compete for six slots; a run need not own every weapon. Keep all five existing
evolution pairs. New weapons do not need invented evolutions in this plan.

Offer chain lightning and fire trail during the opening five minutes on
alternative scripted paths, rather than hiding them behind a late fallback.
Keep the first offer's established options; vary later offers. Verify every
weapon and evolution is reachable on at least one legal choice path, including
paths that take a restored weapon and paths that fill six slots. Replace the
old claim that one prescribed build must acquire all five evolutions.

Restore Magnet and Armour as meaningful passive choices. Hollow Heart and
Spinach already cover the old Max Health and Might roles; do not add duplicate
names for those effects. Keep existing evolution partners and verify that
support passives do not crowd them out of every reachable offer path.

Give the hero an appealing aura from the start. Let normal weapon/passive
growth strengthen it, with evolutions adding distinct layers. An unevolved
build must not lose its presence for the first ten minutes. Zen's musical
groove may modulate presentation, but must not secretly grade or penalize inputs.

## Enemies visibly move on the music

### One travel rule, including bats and bosses

Use only actual committed main beats known at `knownAt`, with the existing
reliability gate. Centralize admission so normal chase, swarm crossings, ring
closure, boss charges and lunges cannot each invent exceptions. Main beats
mean the committed beat entries, not every other entry or the native two-beat
bar, and not the hero's subdivided stride grid.

Use brief beat-anchored motion with the footplant/contact peak on the beat and
a stationary hold for the rest of the interval. As an initial animation
budget, move for at most `min(100 ms, 20% of the beat period)` immediately before
the footplant. A beat learned late shortens that window or defers the step;
it never retroactively moves an enemy. This is a sharp animated step, not an
instantaneous teleport or a glide spread across most of the beat.

Compute travel distance from the role's VS-based average speed and its chosen
beat cadence. Fast bats take longer steps, not extra subdivision steps. Slow
brutes can step every second main beat. Quiet-section slowing reduces travel
distance or skips admitted beats; curling changes the next step's direction.
Neither creates motion during the stationary hold.

Bosses cannot use the present 70%-of-a-cycle charge as a loophole. Break a
charge into sharp beat steps; telegraph with an in-place pose and deliver
contact/slam damage on a main beat. Preserve swept-contact safety for long
steps so a fast bat cannot pass through the hero without a coherent attack.

Separation/collision correction must be resolved as part of the admitted step,
not continuously shove the crowd between beats. Rendering and interpolation
must hold world roots outside step/reaction windows. In-place wings, bob,
squash and anticipation can continue without becoming hidden translation.

Tag forced recoil separately. Hits can cause immediate knockback; death
launches and debris can move freely. Recoil cannot retain or add chase velocity,
and it resumes pursuit only on a later admitted main beat.

### Missing beats, freezes and song changes

If no reliable beat is available, stop enemy travel and contact lunges. Do not
extrapolate the last tempo, reuse stale evidence indefinitely, or route enemy
travel through `free_period_s`. Retain the player's control and the existing
weapon/menu behavior for beatless passages; this decision only freezes enemies'
own travel and attacks, not the entire run clock whenever tracking is unclear.

On relock, use a future known main beat, preserve the existing contact notice,
and discard missed steps. No distance or attack debt accumulates during the
gap. A spawn deadline can wait for admission; limit refills by the normal
density cap and do not release a catch-up flood. The existing music-stopped
world freeze, explicit pause and modal freezes remain in force.

## Readable musical HUD in both modes

Restore the compact beat track/heart and overhead beat gem with its closing
ring and downbeat distinction. When there is no reliable beat, show a clear
waiting state rather than a false metronome. Retain movement/stomp key feedback
as a compact optional display. Restore the Delayed-listening status tag.

Bring back a fixed top-left health display and the local hero health display,
including a strong low-health beat pulse and damage flash. Keep health and the
beat marker above combat effects. Keep XP, level, kills, timer, inventory ranks,
evolution partners and damage labels, but reduce empty-slot prominence. The
timer and twelve empty boxes must not dominate the opening scene.

Use the earlier compact visual hierarchy as the reference. New icons and font
atlases may stay where they improve clarity; a font migration is not a reason
to discard identity or enlarge the entire UI. Inspect at ordinary window sizes
as well as 1080p, in quiet, dense, hurt, card and end-card states.

Separate two meanings of groove:

- **Zen:** a causal presentation value from heard musical pulse/intensity.
  Its label and treatment must not imply earned accuracy. No input ratings,
  score losses, groove drain from missed inputs or timing-based damage penalty.
- **Legacy:** the original judging, Perfect/Great/Good feedback, press marks,
  groove gains/drain, tier effects, calibration, settling, beatless fairness
  and judged combat rewards. Showing ratings without their gameplay effects
  is incomplete restoration.

Audit survivors' bypasses of tier damage, accent rewards and judging. Keep a
single input/meter implementation; project the appropriate mode into the HUD
and combat. The user explicitly requested this work despite the earlier frozen
legacy scope. Preserve old session replay where its recorded content applies.

## Audible game audio under untouched music

Restore stride/footstep cues and the full fire, hit, heavy, cleave, kill,
pickup, heal, level-up and important UI palette. Keep spatial character and
variation. Do not replace a missing sound with only a louder generic cue.

Remove the survivors waveform clamp to music RMS minus 12 dB. Use a controllable
effects bus with sensible defaults, short transients, existing compression and
output protection. Add an accessible effects-level control distinct from music
volume; preserve its setting through mode changes/restarts. Music PCM and music
gain must remain unchanged by gameplay events. Ducking stays off by default.

Use voice caps and coalesced multi-kills, with priority/reservation for the
player's attack, hero damage and important UI over distant repetitive hits.
Verify this priority against actual overlapping sample lengths. A dense crowd
must not consume every feedback voice. Essential cues must remain audible in
quiet or beatless music; beat confidence must not silence the entire game.

Keep sample-accurate scheduling and rollback cancellation. Project sounds from
the same attack/impact identities as animation and damage. Avoid both the
old swing cue and the new weapon cue sounding the same attack twice.

Validate real mixed PCM against representative quiet, loud and dynamic tracks,
and keep short listening exports when source licensing permits. Voice-start
counts, arbitrary fixed-RMS test music and silent captures cannot establish
that the mix sounds good. System/speakers-on checks remain operator-pending
until actually performed. The recent session log's `snd` events record Play
commands, not proof of audible output.

## Encounters, collection and presentation

Retain the Mad Forest-inspired minute progression and **300-enemy cap** as the
initial density envelope. Restore bounded quiet/loud pressure variation around
it. Use existing scheduled enemies for formations where possible; if a set
piece needs extra actors, account for them inside the cap and a documented
population budget. Do not layer an unbounded second director over the spawner.

Restore goblin stampedes, skeleton kick lines, bee wedges and the old ring
choreography where distinct from the new flower walls. Music chooses a
formation's entrance and motion; the run progression bounds eligibility and
frequency. All their travel obeys the main-beat rule. Keep readable exits from
rings and avoid overlapping formations that remove every escape route.

Restore an early minotaur/standing-stones signature encounter, starting from
the old roughly 90-second placement, with warnings, entrance, beat slams and
death spectacle. Keep the later scheduled bosses/chests. Budget this encounter
against early pressure and avoid duplicating the scheduled early boss event
on the same beat. Restore boss staging, not an unrelated new damage system.

Keep zones/camera/lighting reacting to music. Restore a one-minute zone fallback
for the opening five minutes as the initial presentation default; retain the
four-minute fallback later. Keep the forced infernal victory. Use small
section/track-transition punctuation during the continuing run, not an ending,
reward farm or world-time warp at each song boundary.

Restore a **120 px starting pickup radius** and its visible collection cue as
the initial comfort default, with Magnet progression. Keep the accelerated gem
pull, colors, pentatonic chain, food and useful newer pickup presentation.
Keep the VS-derived XP curve/drop economy and 400-gem cap initially, but merge
overflow into a nearby existing gem rather than hiding earned XP in the farthest
gem. Zero-XP kills still get death, impact and audio feedback. Do not create
fake collectible gems or silently inflate XP to make kills look rewarding.

Keep immediate card freezes, clear choices and chained level-ups; do not bring
back walk-into-orb selection or section-delayed upgrades. Restore a world-space
level-up/upgrade celebration on resolution/resume, including a bounded clearing
pulse that cannot break formation geometry or grant undocumented immunity.
Pending inputs still require release and cannot move/stomp the hero after a
menu. Resume on a known musical beat without accumulated attack bursts.

Retain the VS-derived health, contact formulas, 120 ms damage lockout, recovery
and food as initial balance values. Do not automatically restore 200 HP, 500 ms
immunity, or healing on every level: enemy attack cadence and the enlarged
arsenal are changing too. Change these only against evidence of actual unfair
damage or recovery problems, recording both before/after values and the reason.

Keep density budgets, including coalescing and boss protection. Restore strong
near-player hit flashes, readable deaths and some persistent battle aftermath;
use a bounded decal pool instead of deleting all relevant marks after one
second. Preserve full important reactions at low density and protect signature
attacks at high density. Do not restore unlimited effects or raise the enemy
cap merely to recover spectacle.

## Disposition of the audit

Every identified removal/replacement has an explicit destination. Similar
features are grouped; no executor may silently drop a row as polish.

| Audit item | Disposition | Work |
|---|---|---|
| Target-facing sword; real swing poses | Restore with radial damage | FR2 |
| Heavy accents; onset cleaves; outward waves | Restore, deduplicate, preserve legacy requirements | FR2 |
| Starting reach; weaker hits at faster tempos | 190 px; full per-hit power every beat | FR2 |
| Attack-specific camera punches | Restore bounded light/heavy responses | FR2 |
| Old rich starting arsenal | Deliberately keep sword-only start, user selected | FR2/FR5 |
| Chain lightning; fire trail | Restore as actual upgrade choices | FR5 |
| Orbiting blades | Keep Bible as its replacement, user selected | FR5 |
| Groove/tier UI and presentation | Musical zen groove; fully judged legacy; baseline aura | FR4/FR9 |
| Beat track/heart; overhead gem/ring | Restore with reliable-beat/waiting states | FR4 |
| Fixed/local health and danger pulse | Restore | FR4 |
| Input key lights; Delayed tag | Restore compactly, key display optional | FR4 |
| Compact layout; fonts; empty slots | Restore hierarchy; retain useful new assets/info | FR4 |
| Footsteps; attack/impact/pickup audio | Restore audible distinct feedback | FR3 |
| Severe effects clamp | Replace with tunable bus and real mix validation | FR3 |
| Music ducking | Keep off, user selected | FR3 |
| Voice limits/coalescing | Keep, prioritize important feedback | FR3 |
| Loudness population/section roster/calm | Restore bounded pressure and formation variation | FR7 |
| Stampede; kick line; bee wedge | Restore within density budget | FR7 |
| Goblin rings; flower walls | Restore useful choreography; avoid duplicates | FR7 |
| Archers/ranged projectiles | Keep out, user selected | FR7 |
| Early minotaur, slams, standing stones | Restore early staged signature encounter | FR8 |
| Zone fallback | Faster opening tour, slower later fallback | FR8 |
| Song-ending finale/slowdown | Keep 30-minute continuity; restore smaller punctuation | FR8 |
| Upgrade orbs/section-delayed upgrades | Keep card pauses, user selected | FR6 |
| World-space upgrade celebration/pulse | Restore alongside cards | FR6 |
| Small pickup reach; missing pickup ring | 120 px comfort default and visible cue | FR6 |
| Magnet/Armour; old Might/Max Health | Restore first two; keep current counterparts for latter | FR5 |
| Gem rewards and farthest overflow | Keep XP economy; nearby merge and full kill feedback | FR6 |
| Level-up heal; HP/immunity changes | Keep current baseline pending balance evidence | FR10 |
| Late-only aura; weak/dropped impacts; short decals | Restore baseline presence and bounded feedback | FR9 |
| 520-enemy cap | Keep 300 as initial budget; spectacle comes from choreography | FR7/FR10 |
| Hitstop/dips/rate steps | No broad zen time warps; preserve valid legacy behavior | FR2/FR4 |
| Enemy glide/subdivision gaits/long charges | Replace with main-beat steps; explicit recoil exception | FR1 |
| New weapons/evolutions, chests, recap, replay, resumable pause | Keep and integrate; no wholesale rollback | FR5/FR10 |

## Delivery sequence

These are bounded work packages, not beads already filed or claimed. Create
beads under a new restoration epic before implementation. Do not reopen or
rewrite the completed survivors-run epic as if its evidence proved this design.

| Key | Work and likely touch points | Depends on |
|---|---|---|
| FR1 | Main-beat-only enemy travel and gap holds. `engine/{foes,enemy_rhythm,survivor_events,freeze}.rs`, gaits, roster data, live interpolation. Include bats, swarms, bosses and collision separation. | None |
| FR2 | Aimed animated 190 px sword, full hits, accent/onset cleaves, impact camera. `engine/{combat,weapons,hero}.rs`, arsenal, eval records, `slice/{sword,scene,tier}.rs`. Preserve evolution behavior. | FR1 |
| FR3 | Audible sound routing/mix and effects control. `slice/sounds*`, `audio/{mixer,follow}`, output/settings UI, palettes. Cover restored sword/cleave and footsteps first. | FR2 |
| FR4 | Musical HUD and complete legacy judging. `slice/{hud,scene,lane}`, sim judging/combat bypasses, view projection. Keep zen and legacy meanings separate. | FR2, FR3 |
| FR5 | Chain lightning/fire trail, Magnet/Armour, offer reachability and complete UI/audio/recap support. Catalogs, inventory/offer lists, weapon implementation, cards/icons. | FR2, FR3, FR4 |
| FR6 | Generous pickups, nearby overflow and world-space upgrade celebration, retaining card pauses and replay safety. `engine/{pickups,offers,freeze}`, `slice/{gems,pickups,cards,scene}`. | FR4, FR5 |
| FR7 | Music pressure and restored formations in one population budget. Director/spawner integration, set pieces, quiet curls, main-beat formation travel. | FR1, FR2, FR5 |
| FR8 | Early boss/landmark, musical entrances, opening zone tour and transition punctuation. Existing boss presentation, plan/pieces, atmosphere/scene. | FR7 |
| FR9 | Aura, full nearby reactions, density hierarchy and bounded persistent aftermath. Effects/impact budget/tier/scene. | FR3, FR4, FR5, FR6, FR8 |
| FR10 | Integrated balance/feel qualification in both modes, all weapon-choice paths, dense performance and actual mixed-audio evidence; fix confirmed regressions. | FR1–FR9 |

Each package leaves the app playable, records its run command and commit SHA,
and carries focused evidence in `native/evidence/frN/` with a short note in its
own subsection below. Default playable command from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```

Use `--legacy` as well for judged-mode validation. Routine agent checks remain
headless, muted and offscreen. Speaker/System checks go to operator-pending;
do not block every work package on a human playtest or claim those checks passed.

Follow AGENTS.md for fresh Tollgate worktrees, focused checks, final
`scripts/check-fast`, commits, candidates and gate-only promotion waits. Use
the current repository cold-review rules when a change affects determinism or
crosses its review threshold; old epic-specific review exemptions do not
automatically apply to a new restoration epic. Never share Cargo target
directories between worktrees. Keep vendor assets out of Git.

## Acceptance and evidence

### Behavioral checks

- At 70, 128 and 174 BPM, sample positions through each interval. All live
  enemy root displacement must belong to an admitted main-beat step or an
  explicitly tagged forced reaction. Verify stationary holds, especially bats,
  swarms and bosses. Include real timestamped music, not only synthetic beats.
- Remove or delay beat knowledge, lower its confidence, cross a song gap,
  pause during a step, and resume. No invented enemy pulse, hidden glide,
  separation drift, stale-beat attack, retroactive step or catch-up burst.
- Verify the sword really rotates the character toward a present target while
  its hit catches enemies at all angles. Check 190 px boundaries, actual damage
  from catalog power, full per-hit damage at all three tempos, accents and
  onset deduplication, evolved forms and attack-animation/stride arbitration.
- Zen ignores timing quality for score/damage. Legacy distinguishes accurate,
  late, stray and held inputs, applies groove/tier combat consequences, keeps
  calibration/settling and does not judge menu keys. Cover freeze transitions.
- Check all seven base weapons' legal offers, full slots, replacement by
  evolutions, passive partners, alternate builds, damage recap and saved replay.
- Compare music-only and music-plus-effects PCM: gameplay must not change the
  music bus. Check actual cue routing, important-voice admission, cancellation,
  quiet/beatless audibility and independent effects volume. Exported listening
  comparisons must use real tracks where permitted, not just a constant tone.
- Check formations and the early boss against population accounting, openings
  and main-beat admission. A reserved formation cannot starve mandatory bosses
  or duplicate its enemies when a late stream entry triggers rollback.

Tests must assert these behaviors against real catalog data, timestamped stream
entries, emitted records and actual mixed output, not compare the implementation
with a second copy of its own formula. Preserve deterministic, causal recording
and replay with late keys/entries and non-default relevant parameters. Rewrite
obsolete tests that demanded 48 px, tempo compensation or enemy fallback glide.

### Balance and aesthetic checks

Keep the existing positioning/AFK bots as diagnostic starting points. Measure
30-minute survival, minimum HP, kills, XP/level progression, damage sources,
contact bursts and density over several music tempos and legal build paths.
Preserve the easy positioning-player target. Following the operator's 2026-10-27
implementation clarification, AFK minimum-difficulty results remain diagnostics:
report them, but do not make mandatory standing-still losses a release gate or
weaken feel to satisfy them. First check whether an AFK result changed because
of the intentionally restored reach/full hits or genuine inactivity exploitation.
Bots do not prove that combat feels good. Any changed gate needs explicit evidence and rationale;
never disable a red survival gate silently.

Tune documented enemy/contact/progression knobs only after identifying a real
problem. Do not make restored attacks small, quiet, inert or tempo-weakened to
recover the old report. Keep density near the VS-derived minute envelope;
faster songs being stronger is an accepted design choice, not a parity defect.

Keep side-by-side clips/stills of the earlier reference and restored opening,
plus representative five-minute, boss and dense late-run moments. Label old,
new, synthetic and injected-loadout captures honestly. Inspect the hero swinging,
enemy step/hold rhythm, beat visibility, health, attack impact, drops, upgrade
celebrations and aftermath. For sound, keep an actual mixed audition and record
which speaker/System checks remain pending. Do not certify feel using silent
clips, sound-command counts, a kill-time table or bot survival alone.

Retain the repository's current performance gates and purity/causality checks.
Use a representative 300-enemy, 400-gem, evolved/restored-weapon scene to verify
that foreground attacks, hero/health/beat markers and important audio survive
the density budgets. No analyzer change is necessary merely to enforce enemy
movement; if one becomes necessary, follow its corpus-parity requirements.

## Work notes

### FR1 — Enemy movement

Implemented shared committed-main-beat admission, short pre-footplant steps,
stationary holds, baked separation, explicit recoil, swept contact, gap holds and
menu debt cancellation. All catalog roles (bats included), bosses, swarm crossings
and pinned ring/formation travel use it. Free pulses retain player/weapon behavior
without admitting enemy contact. Evidence and playable command:
[native/evidence/fr1](../../native/evidence/fr1/README.md). Visual feel and integrated
qualification remain FR10. The bead records the exact landed commit and candidate.

Dense-CI follow-up removes repeated separation once every beat destination is
fixed; mid-window spawn/recoil/landing still admit steps. Both retained 1080p
runs pass, without establishing a timing improvement. [Evidence](../../native/evidence/fr-ci12/README.md).

### FR2 — Sword and accents

Restored target-facing character swings, 190 px radial reach and full catalog
power per hit at every tempo. Heavy primaries replace ordinary hits; outward
waves are additional, with primary/target and forecast/onset deduplication. Shot
identity carries the actual hit and facing into the visible arc; the shared
swing owns its audio. Bloody Tear crit/heal behavior remains. Evidence, recipe
and playable command: [native/evidence/fr2](../../native/evidence/fr2/README.md).
FR3/FR4 and FR10 retain their sound, legacy tier and qualification scopes. The
bead records the landed commit and candidate. Follow-up
[qualification evidence](../../native/evidence/fr-ci11/README.md) records the
unchanged active-player gate passing all cases after FR2, and the operator's
clarification that standing-still minimum difficulty remains diagnostic.

### FR3 — Sound

Restored footsteps and removed both the music-RMS waveform clamp and beat-
confidence muting. Added persisted independent effects gain (`[` / `]`, F1
shows level). Player attack, hurt and UI voices have separate reservations;
actual sample lengths, compression, cancellation and spatial variation remain.
Default ducking is off in both modes. Effects cannot alter the music limiter's
envelope. Four real-music PCM mixes and a muted Delayed run pass; short WAVs
are kept outside git, with subjective speaker/System checks pending.
[Evidence and playable command](../../native/evidence/fr3/README.md). The bead
records the exact commit and candidate.

### FR4 — HUD and legacy

Restored the shared compact beat/heart and overhead gem/ring HUD, fixed/local
health and damage/low-health pulse, Delayed tag and saved optional key lights
(F4). Empty inventory slots recede to dots. Heard reliable beats drive the
markers; unclear/stale music shows waiting. Zen shows musical energy without
ratings or gameplay penalties; legacy retains earned groove/tiers, ratings,
press marks, settling, cleaves and beatless fairness. Removed Survivors bypasses
of legacy drain, tier damage/reach and valid hitstop/rates. `--legacy` now plays
the same Survivors level by default. Cold review's bar-ordinal and replay-clock
findings are fixed with regressions. [Evidence and commands](../../native/evidence/fr4/README.md)
include960/1080 states, real combat, crowded fixtures and a passing density
benchmark. Human/System feel remains pending. The bead records commit/candidate.

A follow-up [finale cancellation repair](../../native/evidence/fr-ci13/README.md)
removes cancelled blast bands still waiting for unknown beats, preventing a
card pause near victory from delivering an orphaned finale event.

### FR5 — Weapons and passives

Restored Chain Lightning and Fire Trail as actual eight-rank inventory choices,
with captured damage/visual/audio identities, snapshots, recap, icons and cards.
Magnet/Armour are five-rank support passives. All seven bases and five existing
evolutions have legal paths within six slots; early alternatives preserve the
first offer. Chain reaches260px beyond the190px starting sword; trails follow
actual past poses and hold with the modal world clock. A fast-step hit-position
regression is fixed. [Evidence and commands](../../native/evidence/fr5/README.md)
retain catalog/choice/replay/audio checks and dense diagnostics. The bright
injected dense scene explicitly feeds FR9's presentation hierarchy; human feel
remains pending. The bead records review, final checks and landed candidate.

### FR6 — Pickups and upgrades

Restored120px pickup comfort with actual Magnet scaling and a visible ring.
Overflow at400 resting gems merges near the death without changing XP. Resolved
cards/chests coalesce into one world-space celebration and sound on final
resume. The160px pulse gives ordinary foes at most48px of tagged recoil;
bosses/formations, HP, immunity, menu release and weapon debt rules remain.
Run-owned identity/age makes the effect and sound rollback-safe. [Evidence and
commands](../../native/evidence/fr6/README.md) include real modal choices, late
keys, snapshot replay, attraction boundaries and near-drop XP conservation.
Human/System feel remains pending; the bead records review/checks/candidate.

### FR7 — Director and formations

Pending implementation.

### FR8 — Encounters and zones

Pending implementation.

### FR9 — Presentation

Pending implementation.

### FR10 — Integrated qualification

Pending implementation.
