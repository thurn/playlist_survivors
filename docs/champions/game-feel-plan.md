# Combat-feel restoration: ps-ueo

The later November 3 [walking-sword contract](walking-sword-plan.md) supersedes
this restoration snapshot for F impact, rhythm windows, debris/cohorts,
ordinary elites, pickup reach, boss schedule and run logging.

Approved 2026-11-03. [design.md](design.md) defines behavior;
[style.md](style.md) defines presentation. This plan maps the approved interview
to code inspected at staging base `e284d3f38484db7ad94261c34ec7940cb1e69899`.
The audit tables retain that baseline for comparison. The delivery sections
below record the shipped implementation and scenario limits; the assembled
validation report is in `rhythm_champions/evidence/game-feel/README.md`.

## Reference and preserved diagnostics

The exact local motion reference is
`/Users/dthurn/Downloads/trailer-action-neon-edge.mp4`, generated in this
project with `just demo3d --trailer`; ffprobe reports **60.516667 seconds**.
Compare ordered motion: multicolored groups, the held sword and natural body
swing, a broad slash opening substantial space, impact/shatter/sound together.
Still images cannot establish timing. Captures and vendor-derived outputs
stay local and out of git. Frozen sources are read-only references; copy
needed owned code into `rhythm_champions/`, never add a build dependency on
`demo3d/` or `unity-packages/`.

Before any new player run, ps-ueo.1 copied the latest log from
`/Users/dthurn/Library/Logs/Rhythm Champions/Rhythm Champions/Player.log` to
`/Users/dthurn/Library/Caches/playlist-survivors/evidence/ps-ueo/pre-restoration-2026-11-03/Player.log`.
SHA-256: `6abc0e5bf5388a8597f07a321415c00fec3ff13751b91a6b9c6bbb4ccebd54bf`.
The previously inspected session died at run clock 339.5 s, 1,180 kills,
level 14, with touch 43 versus boss 3 hits; no reported presenter faults or
audio stalls. These are diagnostics, not visual or feel validation. Preserve
this copy even if later sessions overwrite the source.

## Approved behavior and resolved semantics

- Keep automatic combat, free movement, six weapons/six supports, levels
  1–8 and existing forms. Sword is permanent, held in the hero's hand, and
  occupies one weapon slot; five other weapons support it. Ordinary Sword
  uses movement direction and last facing when stationary, never enemy aim.
- Starting Sword one-hits the weakest fodder. Tougher types take multiple
  blows. At least double baseline automatic attack rate across the roster,
  preserving identities and per-hit clearing power; Sword starts about
  0.5 s instead of 1 s. Use musical subdivisions and seconds fallback,
  natural clip speeds and no catch-up burst. Do not cancel the improvement
  with an inverse damage multiplier.
- Every dash, on or off beat, ends in a full-circle hero sword sweep at the
  legal endpoint, including obstruction. No target/support/beat wait. Clear
  ordinary weak fodder locally, with tougher enemies surviving. Sword levels
  and applicable supports improve the sweep. Immediate movement and existing
  invulnerability remain; dash-exit body choreography overrides conflicting
  auto choreography without duplicate hits.
- A single on-beat dash immediately grants a full three-second yellow aura,
  including in empty space. Refresh expiry, never stack; off-beat dash leaves
  it alone. Without reliable beats every dash grants it. Duration uses active
  combat time, frozen by cards/pause, with deterministic expiry and replay
  hash state. Active before expiry, inactive exactly at expiry.
- All attacks have 2× coverage AREA during the aura, including the triggering
  dash sweep; no hit-damage bonus. Multiply circular radii by sqrt(2), not 2.
  Apply reach to target eligibility as well as hits. Persistent contact and
  pulses sample shared state and shrink at expiry; discrete launched attacks
  keep their launch footprint. New weapons benefit immediately while active.
  Visual fade communicates expiry without prolonging damage coverage.
- Retire/rework Landing Blast as a distinct optional enhancement; no duplicate
  baseline sweep, recursive proc or prerequisite for dash damage. Record the
  chosen content/replay migration in the implementing bead.
- Ordinary hero/attacks white, aura/empowered effects yellow, enemy hazards
  orange/red, enemy bodies retain cyan/blue/magenta/amber variety. Filled green
  XP has its own animated shader and readable shape, distinct from healing
  and enemy shots. Preserve stored pickup value and persistence.
- Bottom-left health orb replaces the thin player health bar: animated red
  liquid/energy, clear fill boundary, readable near empty through bloom,
  optional compact numbers. Top-center displays horde progress time, switching
  to BOSS plus elapsed fight time. Cards/pause freeze clocks; boss health and
  beat cue stay legible.
- Boss arrival shatters ordinary enemies in the visible fight area; preserve
  existing XP/healing drops and structures. Prevent immediate outside-horde
  refill with bounded fight/re-entry behavior that cannot be bypassed by
  walking a few steps. Only small deliberately timed reinforcements with clear
  telegraph lanes and finite rewards; no arrival-wipe XP jackpot or renewable
  summon farming. The subsequent ps-oli decision replaces this with a 3/6/9-minute horde schedule; retain three damageable bosses,
  paused horde progression during fights, rewards and no Break meter.

Sweep radius/damage, exact boss bounds/re-entry rule, reinforcement counts and
reward budgets are implementation tuning, authorized unattended. Record exact
chosen values with actual motion evidence; do not imply this planning bead
already measured them. No new purchases, accounts, dependencies or human gates.

## Actual trailer path and what to recover

Paths below are relative to the repository root and refer to frozen sources.

| Source | Inspected behavior | Transfer into the live project |
| --- | --- | --- |
| `demo3d/Assets/Demo3D/Runtime/Playback/DemoMain.cs` | Builds `Choreographer` timeline; evaluates crowd and hero on world time, calls `HeroBlade.Pose`, publishes heard-time beat. Holds sword-idle arms over unarmed walk/run. | Recover coherent held blade, sword-ready locomotion and one impact clock. Do not port offline playback or scripted hero control. |
| `demo3d/Assets/Demo3D/Runtime/Playback/DemoMain.Trailer.cs` (`LoadTrailer`, `BuildShots`, `PrepareShots`) | Per-shot timelines/evaluators, blow feel, effect driver, camera/post feel and mesh break moments are prepared together; shatter events carry time, position, direction and tint. | Recover matching slash, directional break and local impact accents. Trailer shot cuts and grading are not gameplay timing. |
| `demo3d/Assets/Demo3D/Runtime/Crowd/HeroBlade.cs` | Right-hand attachment; posed crystal blade flares on `slash-trail`, `thrust-trail`, `spin-trail`, `slam-shock` times. | Reuse hand alignment and impact flare concepts through current hero renderer. Check current blade first; no need to replace it or copy vendor-derived mesh output. |
| `demo3d/Assets/Demo3D/Runtime/Crowd/StockCrowd.cs` (`HoldArms`) | Humanoid arms/fingers mask keeps sword-ready arms over unarmed walk/run; prewarms pooled figures. | Retain current locomotion layering and hand pose. Do not replace current crowd/rendering work with stock prefab rendering. |
| `unity-packages/com.playlistsurvivors.choreography/Runtime/Sim.cs` (combo scheduling) | Fits clip start/speed before impact, reserves follow-through and yields to authored accents; timeline impact carries clip timing. Uses path heading for moving strikes, otherwise scripted/dense/nearest aim. | Recover advance notice and follow-through, not dense aim or root-motion movement. Player movement decides Sword aim and is never locked by an attack. |
| Same package, `ClipLibrary.cs` and `ClipSchedule.cs`; `demo3d/Assets/Demo3D/Runtime/Playback/TimelineEvaluator.cs` | Annotated impact/end, fitted clip timing, frame-independent pose sampling; hit hold 50 ms, flash 120 ms and optional heavy slowdown/catch-up. Shatter uses pose at killing impact rather than waiting for death clip. | Reuse timing principles and current local equivalents. Whole-world hit-stop is not mandatory; avoid disrupting immediate dash or beat timing. |
| `demo3d/Assets/Demo3D/Runtime/Effects/EffectDriver.cs` and `Playback/BlowFeel.cs` | Trail/shock variants and mirroring track swing direction/weight; shatter/death flourishes fire at killing blow, tinted per type. | Broad directional slash aligned to held blade and instant forceful local shatter, with local light restraint. Keep existing copied effects and vendor import pipeline. |
| `demo3d/Assets/Demo3D/Tests/Choreography/PresentationTests.cs` | Checks first-hit kills, hero contribution (at least half of drop kills) and at least one swing/s outside the traveling build, knockback at impact and acted boss ending. | Inspiration for behavior checks, not permission to run/extend frozen tests. Trailer one-hit/shape-scale/kill-budget simulation is cinematic scripting, not a gameplay balance contract for all enemies. |

The shared sim also adjusts shape scale against kill-rate budgets and has
one-hit-kill tuning. Copying those hidden feedback rules would obscure live
balance. Recover the visible clearing power with explicit Sword damage and
geometry instead. Existing shader/effect ownership rules still apply.

## Baseline implementation gaps and integration risks

All paths here are under `rhythm_champions/Assets/Champions/`.

| Anchor | Baseline actually present | Required change / risk |
| --- | --- | --- |
| `Sim/Attacks/MeleeWeapons.cs` | Sword radius 3 m, Hammer 2.6 m; shared dense-cone candidate scoring; Sword gains full circle at higher forms; `HasTarget` uses banked 1.5× area. A strike ledger already deduplicates retained/added footprints. | Movement-directed Sword aim, shared aura reach, weakest-fodder kill and baseline dash circle with upgrade integration. Reuse ledger; avoid double damage from overlapping auto/landing actions. Preserve Hammer identity. |
| `Sim/Attacks/WeaponScheduler.cs` and `Sim/Beat/Cadence.cs` | Sword/default 1 s, Hammer 1.5 s, Spear/Lightning .75 s, Orbit .5 s. Uses integer beat counts `{1,2,3,4,6,8}`, fractional extra-strike carry, fallback/held spending and interval/target cadence damage normalization. | Halving target seconds alone cannot provide half-beat attacks on slower songs. Add subdivision scheduling and test all tempos; normalization must not undo doubled clearing power. Preserve fractional carry and no resume backlog. |
| `Sim/Hero/Dashes.cs` and build state | Immediate judged dash calls `Build.Empower`; optional Landing Blast alone supplies endpoint damage, placed via actual walked endpoint. Archived forms remain for opted-in historical fixtures. | Reuse actual endpoint, buffering and invulnerability, add unconditional single landing sweep, replace banked flags with shared expiry. Keep live and archived fixture semantics explicit. |
| `Client/Hero/HeroMotion.cs`, `Client/Hero/HeroPresenter.cs`, `Client/Loop/RunPresentation.cs` | HeroPresenter already draws the baked held blade and layers sword-idle arms over running legs (capsule fallback if bake is absent). Knight slash A impact .350 s/end .717 s, B .267/.450 s; spin 1.433/1.650 s. `MeleeAction` calls `Hero.Swing` at `e.T`, always `Slash` for ordinary nonspectral Sword. `Swing` refuses entry before last shown time/previous follow-through and may enter partway; max speed 1.15×. | Damage/VFX currently happen before a newly requested wind-up can be seen. Give automatic attacks presentation notice or a justified aligned entry; do not simply accelerate clips. Dash exit has only .15 s travel notice, so the 1.433 s spin needs an intentional late entry/short authored phase and explicit body priority. At .5 s auto cadence, A's follow-through can exceed the next impact; inspect alternation and interruptions, not only event counts. |
| `Client/Hero/EffectCues.cs` and effect rendering | Melee events select form VFX, `WeaponActivation` emits Encore for enlarged area; magic rest radius still divides by sqrt(1.5). Legacy SwordSlash/Spin routing exists separately. | Use one live Sword/landing visual path; propagate aura state/color/area consistently across all families. Audit hard-coded 1.5 and target predicates; no duplicate VFX/hit event or permanently enlarged persistent objects. |
| `Client/Crowd/PickupPresenter.cs` | Limit 384 previews, low hovering hollow diamonds/crosses sharing dim white-blue material; sim owns unseen value. | Filled animated green XP shader and distinct healing; retain preview bound and all sim value, no collectible loss or per-drop material explosion. |
| `Client/UI/Hud.cs`, `Client/UI/RunUi.cs` | HUD receives XP fraction and boss bar only; indicator owns world hero cues. | Pass authoritative health/horde/fight elapsed state; orb and clock must use held sim time, not advancing music. Keep beat cue/boss bar readable. |
| `Sim/Bosses/`, spawn/run clocks and crowd events | Three always-damageable encounters, retained horde and no renewable Conductor summons; separate horde/combat time already exists. | Explicit clear cause and presentation shatter without ordinary kill reward/proc jackpot; preserve stored pickups. Gate outside entry for the full fight, handle streaming and moving away, bound reinforcement and rewards. Preserve schedule and telegraph promises. |

ps-6y2 art/performance work continues independently; preserve current mesh,
foreground hero, edge, warning layering and renderer improvements. The main
checkout's uncommitted `rhythm_champions/ProjectSettings/ProjectSettings.asset`
is operator work and must remain untouched. Never commit vendor-derived
meshes/bakes/Blender files, captures or the preserved log.

## Delivery map and verification

| Bead | Deliverable | Focused checks and required evidence |
| --- | --- | --- |
| ps-ueo.1 | This contract; consistent design/plan/style; source audit; preserved local log. | Docs diff, source/path checks, fast checks and normal CI delivery. No gameplay completion claim. |
| ps-ueo.2 | Held Sword, movement aim, faster cadence, one-hit weak fodder, unconditional dash sweep, shared aura, body/VFX/sound/shatter timing. | Sim/replay cases below and captured ordinary/empowered/rapid-dash motion against trailer; record exact content migration/tuning. |
| ps-ueo.3 | Health orb, green XP, clock and semantic colors. | Low/medium/full health in bloom, XP versus healing/shots, aura expiry and pause/card clocks in actual Unity captures. |
| ps-ueo.4 | Arrival shatter, preserved drops, arena/re-entry prevention and controlled reinforcements. | Arrival with outside mobs, walking away/streaming, finite summon rewards, all bosses and safe required waves in motion. Record exact bounds/counts/budgets. |
| ps-ueo.5 | Complete-run assembly, capture comparison, tuning and limitations. | Actual gameplay captures and replay wins/deaths; inspect timing, color, clearing, low health and boss transitions. Update evidence/operator-pending; fast/slow CI to master. |
| ps-ueo.6 | Single final Warden for this light run. | Confirm contract end to end, verify meaningful regression checks and inspect actual motion evidence; repair confirmed defects. |

Implementation checks must cover:

- On/off-beat, buffered, beatless and obstructed dashes all land one sweep at
  the actual endpoint, with no next-beat wait; weak/tough targets, movement
  during attacks, last facing and auto/dash conflicts behave as designed.
- One qualifying dash grants all three seconds immediately, refresh does not
  stack, off-beat does not cancel; test exact expiry, acquisition/form changes,
  holds/resume, no-target interval, persistent overlap and frame-partitioned
  replay equality. Verify 2× area rather than radius or damage.
- Faster base activations at slow/fast/changed tempos and silence; no catch-up
  burst, lost fractional carry, inverse hit-damage reduction or unreadable
  body playback. Match impact timestamps to actual body/VFX/shatter/sound.
- XP value conservation under preview culling/merging, attraction, streaming
  and boss arrival. Health orb fill and clocks derive from correct sim state.
- Three-boss timing/rewards remain intact; no arrival reward jackpot,
  renewable reinforcement farming, instant refill or bypass by a short walk.
  Required dash waves retain valid warning/recovery during holds and tempo loss.

Run relevant focused sim/replay/Unity checks when behavior changes, plus the
normal Enact fast/slow CI workflow. Record capture paths, scenarios, inspected
intervals, fixes and limitations in tracked textual evidence; media stays
local. Automated event counts and stills do not certify satisfying timing.
Human subjective hand-play, speaker/Bluetooth judgement and target-hardware
feel remain explicit, non-blocking operator-pending checks. Agents must still
capture, inspect and iterate; pending human judgement is not a substitute.

## ps-ueo.2 implementation (2026-11-03)

The survivor build reserves Sword even when a scripted preset omits it. Existing
hand attachment and sword-ready locomotion are retained. Sword's ordinary cone
uses movement/last facing; Hammer keeps crowd aim. Sword radius is 3 m and base
hit damage 1: initial weakest fodder health is at most .65 (bat .325), while
8-toughness elites survive. Levels and Might increase ordinary and landing damage;
Reach increases their area. Movement remains available during body swings.

Baseline fallback periods are Sword/Bow/Ember/Sentinel .5 s, Hammer .75 s,
Spear/Lightning .375 s and Orbit .25 s. Survivor cadence uses quarter-beat grid
units with counts 1/2/3/4/6/8/12/16/24/32; archived cadence keeps whole beats.
Quantization may increase per-hit damage for a slower chosen interval, but never
reduces it for a faster interval. Tempo fractional carry and held grid spending
remain. There is no accumulated resume volley.

Every survivor dash emits exactly one `LandingBlast` event at its .15 s legal
endpoint (the old wire ID is retained). It is now the baseline sword sweep,
not an additional optional blast: radius 3 m times Sword's radius factor, damage
Sword's damage factor. Landing Blast support keeps its content/card ID and adds
20% base sweep damage per level; it creates no second hit, Echo or Tempo sweep.
Both on/off-beat landings clear weak fodder; a timed landing uses radius sqrt(2)
times ordinary, rather than increased hit damage. Archived non-survivor sessions
do not gain this baseline sweep. Survivor replay fixtures were deliberately
re-recorded for the faster cadence, shared aura and changed boss-fight inputs.

`WeaponBuild.AuraUntil` uses active combat seconds. A qualifying dash sets it to
start + 3; a later qualifying dash replaces that deadline, and off-beat dashes
leave it unchanged. Beatless judgement qualifies every dash. All acquisitions and
forms share the state. Exact expiry is inactive. Cards/pause freeze combat time;
expiry, cadence prediction and aura state participate in deterministic hashes.
Target eligibility and actual footprints use the same shared area factor.
Persistent pulses and their echoes sample current area; idle orbit/field/sentinel
visuals follow current area even between pulses. Launched arrows/thrusts and
ordinary discrete echoes retain captured footprints.

Automatic Sword sends up to .35 s advance notice on its predicted cadence rather
than requesting a windup on impact. A/B clip scheduling retains the existing
1.15x speed ceiling and natural partial entry when follow-through overlaps.
Movement can retarget the actual impact direction. Dash start cancels conflicting
body swings; its immediate .15 s travel intentionally enters the final authored
spin windup at natural speed, with the spin impact at dash end and no lingering
dodge layer over the exit. Sword auto damage is suppressed until .22 s after
the exit to protect that follow-through. Holds preserve pending dash/spin timing
and discard automatic windups whose grid points were spent while held.

Ordinary Sword uses the existing trailer-like Slash3 broad crescent recipe and
its held blade; detached rigid Sword meshes are reserved for spectral actions.
Landing uses the existing full-circle SwordSpin recipe, with same-time heavy hit
sound and existing instantaneous enemy shatter. Yellow shader flags, launch tint
and a small yellow ring communicate empowerment; the ring fades in its final
.3 s and does not prolong coverage. The remaining enemy/HUD color work belongs
to ps-ueo.3.

### Local motion evidence and limits

`/Users/dthurn/Library/Caches/playlist-survivors/evidence/ps-ueo/sword-feel-visible/`
contains 240 ordered beauty frames, ID frames, view metadata and `motion.mp4`
(30 fps, 8 s). `sword-feel` is a scripted stationary 72-target formation using
the live Simulation, RunPresentation, held hero, VFX and crowd shatter presenters.
Capture it explicitly with `champions stills --scene sword-feel`; the default
`all` set excludes this dense motion sequence, like the peak-load sweeps, to
keep CI's per-image look analysis within its five-minute budget.
Dummy spawn events are given visible skeleton/slime/demon bodies only in the
scene's presenter; this is explicitly not sustained horde AI evidence. Weak
bodies have .65 health, with an 8-health survivor per group. Capture sound is
silent; sound dispatch timing is checked in code/tests, not heard by an agent.

Inspected ordered intervals .267–.733 s (body lead, ordinary white crescent and
shatter), 2.0–2.4, 2.625–3.0 and 3.25–3.6 s (successive legal landing spins,
yellow circles and local clearing), and 4.7–5.2 s (aura fades and ordinary white
coverage returns). The first capture exposed invisible type-zero dummy bodies;
the scene was corrected and the visible formation recaptured. The restored
.45-width, 1.5-brightness crescents replace the baseline .055/.65 thin strokes.
Compared ordered frames from trailer 8–10 s: broad arcs reveal a cleared pocket
and shatter begins on impact in both; the trailer's much denser moving crowd,
multicolored groups, grading and camera choreography remain a different test.

Agent validation establishes deterministic geometry, timing and visible motion,
not subjective fun, speaker sync or target-hardware feel. ps-ueo.5 must still
capture moving-horde assembly; operator hand-play remains non-blocking pending.

Verification at delivery: 481 sim tests and 125 Unity EditMode tests passed;
Mono player built and the 240-frame scene exited 0 without presenter exceptions.
The two survivor fixture sessions were re-recorded by their explicit fixture
recorders, then replayed by the full sim suite. No vendor assets, bakes or media
are included in the commit. Enact fast checks gate staging; the parent run owns
slow-CI convergence and full moving-horde assembly after this executor closes.


## ps-ueo.3 delivery: readable survival

The HUD now draws a 144-reference-pixel bottom-left health orb from a dedicated
192×192 render texture and liquid/energy shader. Its fill boundary tracks health
height; moving waves taper at zero/full so zero is truly empty. A persistent
rim and compact current/max numbers keep 8/100 distinguishable from 0/100.
This UI is composited after world bloom. The overhead health hairline is removed;
the existing overhead beat ticks and dash pip remain. Health shader motion uses
RunClock and freezes with cards/pause. The top-center clock reads HORDE mm:ss
from HordeClock, switches at boss arrival to BOSS mm:ss from active seconds
since arrival, and returns to horde time after defeat. Arrival-clock state is
presentation-only, derived from run time, and does not alter gameplay hashes.
Boss health is a separate 8-pixel strip below the phase clock; no Break bar.

XP keeps its sim value/persistence, nearest-first preview and 384-draw cap.
Its filled green diamond uses one shared animated flow shader, with phase
varied by position and time supplied by the existing presenter. Recovery remains
a hollow cross, now red. Shots remain smaller airborne red-orange beads;
telegraphs use orange/red boundaries, existing dark shoulders and hatching.
No monster-body or floor palette values changed. Palette diagnostics now reject
yellow/green hazards rather than requiring the old yellow hazard role.

The opening spawn table previously admitted only cyan skeletons and slimes.
Existing blue bats (.6 weight) and magenta goblins (3 weight) now join at zero
seconds, retaining their colors, weights, stats and shapes. Later demon/elite
gates, total density, toughness and XP rules remain. The dense minute-five
palette retains cyan/blue/magenta/orange; later tables retain amber/violet too.

### Evidence and limits

Local evidence (media/vendor-derived pixels deliberately outside git):
`~/Library/Caches/playlist-survivors/evidence/ps-ueo/`.

- `hud-readability-final/`: 120 ordered 30-fps player frames (4 s), real pickup,
  health shaders, spores and palette-crowd rendering. Fixture health is full for
  0–1 s, 8% for 1–2 s, zero for 2–3 s, 60% thereafter; the last second switches
  to a synthetic boss HUD. This is an explicit presentation fixture with a
  stand-in hero and in-place crowd, not a simulated boss encounter. Inspected
  surface/wave changes, the empty endpoint, green diamond flow and orange shot
  travel. The first fixture accidentally passed a transparent boss color;
  fixed and recaptured. A layout check now covers bar/clock separation.
- `hud-opening/`: actual Mono survivor gameplay at 8/16/24 s, reference scripted
  movement/picks and a local 220 Hz test tone through the real music plugin.
  The 25 s launch exited 0; log shows advancing horde time, ~197–213 living
  enemies and normal Sword upgrades. Captures show blue/cyan/magenta groups,
  green XP, white hero, yellow empowered attacks and readable full-health HUD.
- Compared the trailer's 8-second frame: cyan/blue/magenta body grouping is
  retained; pickup and HUD semantic colors intentionally differ. The trailer
  has denser choreography and different camera motion. ps-ueo.5 owns assembled
  moving-horde/boss motion; these captures do not certify fun or speaker sync.

Verification: 482 sim tests (including arrival/defeat, pause and card clock
holds), 131 Unity EditMode tests (including GPU orb readback and HUD layout at
1280×720, 1920×1080, 2560×1440 and 1280×1024), and 161 tool tests. Both new
shaders compiled in the Mono build; observed variant count 287/300. No sim
fixture hashes needed rerecording. Operator hand-play, subjective low-health
urgency, full boss battle readability and target-hardware GPU cost remain
pending rather than being inferred from a green suite.

## ps-ueo.4 delivery: isolated boss arrivals and finite reinforcements

Boss arrival shatters every non-boss body in a 42×28 m rectangle around the
hero. The off-screen remainder retires silently; the ordinary spawner remains
stopped globally until boss death. This bounds the encounter population without
an invisible wall, so walking/dashing beyond the entrance view cannot meet an
old pursuing horde. Arrival does not call progression kill rewards, generate XP
or healing, credit player kills, remove existing pickups, or break structures.
Boss attacks still break structures they actually hit. Boss health, phase HUD
clock semantics remain; ps-oli subsequently changes thresholds to 180/360/540 horde seconds.

Each boss has three reinforcement opportunities at 12/28/44 active seconds
after arrival, deferred to the first gap after its current move. A fixed orange
2 m ground circle warns for 2 s at (hero x ±10, hero y +8), alternating sides.
Up to three starting-toughness skeletons enter that one sector. The boss then
waits 2 s before choosing another move; its body keeps following and remains
damageable throughout. Existing live reinforcements consume later opportunities
without stacking. Spawn slots within 6 m of the hero or inside structures are
spent without relocating. The maximum is nine ordinary kills and nine XP per
encounter; refusing to kill the boss never renews it. Pause/cards freeze warning
and opportunity time; death cancels pending groups. A later boss resets its own
budget. No Break mechanic was restored.

The existing `Kill` → `CrowdPresenter` → `TrailerCrowd.Break` → `MonsterShatter`
path already carries the trailer's crack-outlined shard launch, its easing and
pooled GPU fragments. Inspected the frozen trailer's playback/effect/shatter
sources and reused that live project's path with outward directions and entrance
power 8 (the presenter caps launch at 3.2). No frozen dependency, vendor mesh,
bake or media was added. Environmental arrival events do not count as player
kills in the end summary.

### Evidence and limits

Local evidence: `~/Library/Caches/playlist-survivors/evidence/ps-ueo/`.
`champions stills --scene boss-arrival` is an explicit motion-capture tool,
excluded from the default `all` screenshot set.
`boss-arrival/` contains 490 actual Mono-player stills with ID passes; 480 ordered
30-fps frames cover 4–8 s (entry), 10–14 s (attacks) and 17–25 s (first group).
`boss-arrival-motion.mp4` concatenates those three intervals, with time jumps at
the joins. The 48 s fixture exited 0 with no reported presenter exceptions.
Inspected ordered samples: arrival flash followed by outward fragments, clear
floor by the next attack, a warning isolated from attack marks, three cyan
skeletons appearing at its fixed location, then normal approach and weapon hits.
Structures remain visible through entry. Six real automatic weapons continue
throughout (Sword/Hammer/Bow/Orbit/Lightning/Ember). This explicit encounter
fixture calls the Ogre early, holds its health high, and makes the initial 80
mobs very tough to expose the environmental shatter. It is not a normal economy
run, live input performance sample, or a claim that a real build takes 48 s.

Compared the reference trailer's ordered 8–10 s frames: both show clear pockets
and directional shards after impacts, but trailer crowd density, body palette
and camera choreography differ. That comparison cannot certify sound sync or
subjective fun. ps-ueo.5 still owns the assembled normal-run review, and operator
hand-play/target-hardware feel remain pending.

Verification: 490 sim tests, 131 Unity EditMode tests; Mono player build passed.
The full-build Ogre storm, Conductor barrage and Dragon storm scenes also
ran through 48 simulated seconds each (55 additional captures, exit 0). Inspected
entrance, reinforcement and traveling-wave frames with six maximized weapons;
these stress fixtures add a 384-pickup visual overlay and start with 1,000 very
tough mobs. The overlay is not an arrival reward. Their logs and contact sheet
are retained beside `boss-full-build/`; no presenter exceptions were reported.

Tests cover all three thresholds/rewards/resume, retained pickup/structure state,
zero arrival XP and player kill credit, outside re-entry after moving, all three
bosses' finite budgets, no stacking, pause/cards, warning cancellation on death,
warning contact safety, fresh budgets on repeat bosses, and replay through a
paused reinforcement warning and spawn. The synthetic
`fixture-duel` session was intentionally re-recorded: arrival now clears its
placed field and changes its subsequent inputs/events. Fast checks gate staging;
the parent run owns slow-CI convergence.

## ps-ueo.5 delivery: normal-run assembly

[Assembled evidence](../../rhythm_champions/evidence/game-feel/README.md)
records four additional full normal-run wins/replays at 80/174/128 BPM and
beatless, moving-horde and all-boss render sequences, actual short app
audio/video, low-health death, and controlled beat/half-beat/off-beat/fallback
dash timing. A real soundtrack-driven normal run won at 1,031.4 active seconds;
its complete Mono replay matches hash `4B2162FA28B20018`, 245,926 events,
with no presenter faults. Failed long video recordings are explicitly unusable;
their complete run record was recovered into actual player replay motion.

Assembly exposed a sound regression: survivor advance `Swing` notices and
actual `MeleeAction` impacts both scheduled Sword whooshes, including ghost
sounds after a dash cancelled a windup. Survivor notices are now silent; the
actual impact owns the one whoosh. Archived choreography is unchanged. Two new
Unity regressions cover cancellation and a real 60 Hz sim/body/kill/sound
sequence sharing impact 0.500 s. Verification: 490 sim and 133 Unity tests,
fresh Mono build, normal-run replays and Enact fast checks.

Measured first choices (12–14 s) and boss durations (8–54 s across four builds)
are faster than the earlier pacing targets; damage was not inversely reduced
to hide the requested attack-rate increase. Full values, preview/media limits,
and non-blocking hand-play, perceived speaker sync, pacing and target-hardware
judgement are recorded in the report and operator-pending. No media, vendor
derivatives, frozen-project changes or operator ProjectSettings are delivered.

## ps-ueo.6 final review

The final Warden repaired a cancelled Sword windup reserving animation time
across pause/resume: the first resumed strike now gets its visible body impact
on the damage clock. Cancelled clips release their follow-through and combo
carry. The public-motion regression fails before the fix; all 490 sim and
134 Unity tests pass after it. Fresh Mono build and normal opening pause/replay
motion agree, with exact cross-runtime replay equality. The final-review
section in the assembled evidence records inspected trailer/gameplay intervals,
local capture paths, CI and remaining non-blocking operator judgements.
