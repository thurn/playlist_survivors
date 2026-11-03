# Combat-feel restoration: ps-ueo

Approved 2026-11-03. [design.md](design.md) defines behavior;
[style.md](style.md) defines presentation. This plan maps the approved interview
to code inspected at staging base `e284d3f38484db7ad94261c34ec7940cb1e69899`.
It is a source audit and implementation contract, not evidence that the new
behavior has shipped. ps-ueo.1 changes documentation only. Later executors
update exact implementation choices and evidence here as they land.

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
  summon farming. Retain 5/10/15-minute horde schedule, three damageable bosses,
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

## Live implementation gaps and integration risks

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
