# Assembled combat validation — ps-ueo.5, 2026-11-03

Validated staging base `7d16fe9c8657eece3693230527b52d7149120b95` plus
this bead's sound correction. No balance, mesh or vendor-asset changes here.
The [approved contract](../../../docs/champions/game-feel-plan.md) records
the exact Sword, aura, HUD and encounter choices delivered by ps-ueo.2–4.
This report distinguishes ordinary gameplay from their earlier stress fixtures.

## Local evidence and reproduction

All paths below are relative to this **local, untracked** folder:
`/Users/dthurn/Library/Caches/playlist-survivors/evidence/ps-ueo/assembly/`.
It contains vendor-derived pixels/audio and must never be committed.

- `recipes/` retains the local capture/generation recipes. Update their player
  path to the current worktree before rerunning. `ps-ueo5-capture.swift` uses
  macOS ScreenCaptureKit with an application-process filter, the window's
  source rectangle, 1280×720 video and 48 kHz stereo audio. AVAssetWriter keeps
  the original shared media timestamps. No microphone or other apps are mixed.
  Captures include window chrome; inspect the gameplay at native scale too.
- `trailer-loop.wav` is the exact supplied trailer's soundtrack, looped locally
  for a long normal game. It is an input to the real music plugin, not a
  substituted trailer video. `beatless-tone.wav` is a 220 Hz test tone.
- The real-time runs use the built Mono player, current content and normal
  spawns, progression, pickup collection and boss thresholds. No health,
  toughness, reward, clock or spawn overrides. The long `live-*` run uses
  `--walk reference --picks reference --seed 31 --record .../live-session`.
- `idle-low-health.mov` is **corrected-build actual app audio/video**, 75 s.
  `idle-session/` records the normal idle run's death at song 25.225 s,
  health 0, 200 kills, level 1, 34 touch hits. Inspect movie 19–26 s: 28, 22,
  13, 7, 4, 1 and then 0 health, moving red fill, white ordinary swings,
  green XP and death shatter. The end card follows; it is not 75 s of combat.
- `controlled-aura.mov` and `controlled-session/`: soundtrack-driven normal
  gameplay with key schedules, including a qualified **half-beat** at 9.25 s.
  Its renewal is correct; this is not the off-beat test.
- `offbeat-control-aura.mov` and `offbeat-control-session/`: the same route,
  with that press changed to 9.16 s, outside the beat/played-half-beat/onset
  windows. Inspect the moving exit spins and the ring fading near 13.22 s.
- `beatless-aura.mov` and `beatless-session/`: same route against the tone.
  Every dash qualifies through the fair beatless fallback. Each short movie
  has 21 s of actual audio/video, followed by a completed 35 s session record.
- `live-opening.mov` is the initial 40 s audio/video trial, using a whole
  display application filter: the window is small inside black surroundings.
  `live-cropped.mov` corrects that framing. Both precede the sound fix.
  The long run also precedes the sound fix; corrected-build idle/controlled
  captures and tests validate that correction. Its long ScreenCaptureKit
  recordings failed to finalize (SCStreamError -3808); they were renamed
  `*.unfinalized.mov` and are **not usable motion/audio evidence**.
  `live-dragon.mov` is a valid 32 s recapture after the fight had already ended,
  so it shows the victory card, not a Dragon battle. Use the complete recorded
  run and the actual player replay frames below for that battle.

The short sessions' `event-timing.csv` and `state-timing.csv` were extracted
by replaying their recorded inputs and plugin entries against current content.
The original session files remain unchanged. Replays can be repeated with:

```sh
just champions-tool play --headless --replay /absolute/path/to/idle-session --replay-speed 8
```

`idle-replay.log` reports Mono hash `5BA90CD3386BFAFF`, 1,612 events,
**matches**, presenter faults **none**, exit 0. The same session also matches
in .NET. The first controlled and beatless sessions independently match
`AF7E0C5935BE0FB6`/1,772 and `BDD8DECFF1568F7C`/1,613 events in .NET.
The actual off-beat control matches `20C8ED82819F09C7`/1,480 events too:
press 8.28 s grants expiry 11.28 s; off-beat 9.16 s leaves it at 11.28 s;
beat 10.22 s refreshes to 13.22 s. All three exits emit exactly one sweep
at press + .15 s, at their legal endpoints, radius sqrt(2) × 3 m and damage 1.
The off-beat sweep still benefits from the already-active aura. Area is 2 just
before expiry and 1 after it; the yellow ring visibly fades. The beatless
session instead refreshes on every press, as required.

The real-time soundtrack run wins at **1,031.4 active seconds** (song 1,031.458),
28,179 kills, level 35, final health 105. Its log reports no presenter faults
and zero audio stalls. Ogre entry/death: 302.2/333.7; Conductor: 636.3/688.5;
Dragon: 991.0/1031.4. There is no synthetic early boss call. `live-session/`
records Mono hash `4B2162FA28B20018`, 245,926 events, 79,101 plugin entries and
12,092 inputs. `live-replay/` renders that exact normal record in the corrected
player, covering all three actual entry/fight windows. This recovery is silent
and sampled at 7.5 song-time fps (`--replay-speed 8`), so use the 15 fps matrix
sequences for finer animation inspection. Complete replay verification has its
own `live-replay-complete.log`, separate from the capture launch's early quit.
That full Mono replay exits 0 with the same **245,926 events and hash
`4B2162FA28B20018`**, presenter faults **none**. The recovered entry/fight
sequence contains 1,028 ordered frames. Unlike the failed long movies, it is
usable motion evidence and derives entirely from the actual soundtrack run.

## Full normal-run matrix, without health/spawn overrides

Generated with the existing `LevelRunTests.Run` and `ConfigureBuild`, explicitly
enabling horde rhythm inputs as well as the routine's reactive crowd/boss
dashes. Actual current content, normal offers and normal pickup routes; no
Landing Blast required. The 128 BPM case uses the playlist with silent gaps;
the others use steady synthetic beliefs or silence. These are deterministic
sim inputs, not claims that an audio analyzer recognized synthetic beats.
`summary.json`, each `run-*/outcome.txt`, `inputs.bin` and `entries.jsonl`
retain the results and complete replay data.

| Build / BPM / seed | End, active seconds | Kills | Normal choices | First choice | Boss fight seconds (Ogre / Conductor / Dragon) | Replay hash |
| --- | --- | --- | --- | --- | --- | --- |
| broad / 80 / 31 | Victory, 961.5 | 25,181 | 34 | 14.4 s | 7.9 / 14.7 / 27.2 | `9ECC9A8B32BE2979` |
| broad / 174 / 31 | Victory, 959.6 | 26,776 | 34 | 13.3 s | 7.7 / 15.5 / 27.7 | `FCEAE8E7A71F118C` |
| focused / beatless / 31 | Victory, 1023.9 | 25,285 | 34 | 12.3 s | 28.9 / 49.0 / 40.0 | `4897F733FA634180` |
| defensive / 128 / 32 | Victory, 1010.5 | 26,583 | 33 | 13.9 s | 17.4 / 29.3 / 53.9 | `3101D6CC94FB2A5B` |

Each completed all three bosses, retained 900 s of horde progression, and
replayed exactly in .NET. Bosses grant two rewards in addition to normal
choices; the focused capped build takes a max-health fallback for one reward.
Minimum health ranged from 53 to 68. These skilled deterministic routines are
not an estimate of human win rates or evidence that all builds are equally fun.

First choices and bosses are faster than the older pacing targets. This is
recorded in design/operator-pending rather than silently cancelling the
requested clearing-power increase with reduced hit damage. No hidden adaptive
health scaling, XP wipe jackpot or renewable summon economy was introduced.

## Motion and trailer comparison

`replay-broad-80-31/` contains 1,090 ordered player frames from the normal-run
record, covering opening 6–12 s, upgraded dense horde 230–236 s, and the
three boss entry/fight windows 302–313, 613–633 and 932–962 s. Two other
sequences add 150 fast-beat and 180 beatless opening/Ogre frames. Rendering
uses `--replay-speed 4`; **filenames are Unity time, about one quarter of song
time**. Capture every Unity frame, then play that sequence at 15 fps for normal
song-time motion. Small replay startup offset means use the HUD/event logs for
precise encounter times. These sequences are silent, not audio-sync evidence.
Each render launch exited 0; still capture quits at its last requested time,
so that exit alone is not a complete replay-hash check.

Inspected ordered opening arcs, movement through cleared pockets, hand/body
swings, dash exit circles, upgraded five-additional-weapon pressure, and all
three entry transitions. Cyan/blue/magenta groups remain distinct; later orange
and amber bodies appear. Green pickups retain their positions across entry;
the wipe clears bodies, then the shard burst fades and open floor remains.
Normal boss attack lanes, health strip and elapsed fight clock are visible
through the weapon effects. The XP carpet can still be visually busy: taste
and target-scale judgement remain explicitly pending.

Compared ordered frames from the **exact** supplied
`/Users/dthurn/Downloads/trailer-action-neon-edge.mp4`, especially 8–10 s.
The shared successful sequence is a held-body swing, broad crescent, immediate
colored shatter and a substantial cleared pocket. The trailer's much denser
scripted crowds, camera cuts, grading, grouped spawn staging and offline
choreography are not normal gameplay. No frozen playback was used as live-run
evidence. The source trace remains in the plan: trailer EffectDriver/HeroBlade,
copied live EffectCues/Slash3 recipes, and the existing TrailerCrowd →
MonsterShatter GPU path. Frozen projects were only read.

## Impact clock correction and verification

Survivor `Swing` is advance body windup notice; `MeleeAction` is the actual
impact. The sound presenter was reacting to both. That doubled ordinary Sword
whooshes and scheduled ghost sounds after a dash cancelled a windup. It now
ignores survivor windup notices; the actual impact owns the whoosh. Archived
choreography keeps its existing `Swing` sound path.

The new regression checks cover an uncommitted/cancelled notice and a real
60 Hz sim/presentation sequence: first weakest-fodder kill, annotated body
impact, single scheduled whoosh and shatter attack all occur at **0.500 s**.
The test uses the live frame frontier/lead, not a zero-lead fake clock.
Actual captured DSP output is retained above; exact test timestamps do not
establish perceived speaker/display synchronization.

Verification: 490 sim tests, including full-run wins/deaths, replay, tempo and
confidence changes, holds, unobstructed/obstructed sweeps, shared area/expiry,
and finite reinforcement budgets; **133 Unity EditMode tests** after the sound
fix; fresh Mono build. The change adds no rendering allocations, shader variants
or sim work and removes duplicate sound requests. Normal live logs are retained
for stalls/faults; they do not replace GPU profiling. Existing target-hardware
and M1/GPU acceptance remains pending. Enact fast checks and the parent run's
slow CI gate delivery; see the bead for the delivered commit and CI result.

Human hand-play, subjective fun, perceived music sync, dense XP/attack taste
and target-hardware cost remain non-blocking checks in
[operator-pending.md](../operator-pending.md).

## Final Warden review — ps-ueo.6, 2026-11-03

Independently reviewed delivered commits c5fa0489, 4c9b41fd, ddc9eba8,
5aecf5c7, a496e32e, 7d16fe9c and a1718675 against the approved contract.
Checked current cadence/subdivision and damage normalization, movement-facing
Sword, legal endpoint sweep and strike ledger, shared aura and persistent
footprints, held clocks, color/shader/HUD changes, zero-reward arrival clear,
finite summons and replay coverage. No frozen or vendor-derived assets changed.
The added tests use simulated time and geometry, with no wall-clock verdicts.
Prior integrated slow CI completed in 281 seconds and promoted a1718675 to
master, within the five-minute budget. The Warden repair also goes through
Enact fast and slow checks; its bead records the final delivery/CI result.

Found and repaired one presentation defect: pausing during an advance Sword
windup cancels that strike, but its shifted follow-through end still delayed
the next swing. A pause at .300 s followed by resume at 10.300 s left the
10.500 s damage without its body impact until the cancelled clip ended.
Scheduling now releases a cancelled swing at its cut time; cancelled windups
also stop carrying combo/twist state into the next swing. The new public-motion
regression failed on staging and passes with the repair. No simulation timing,
damage, spawn, progression or replay fixture changed.

Fresh verification: **490/490 sim tests**, **134/134 Unity EditMode tests, no
skips**, and a successful Mono build. The first Unity invocation preceded local
setup and failed for missing plugin/vendor files; setup supplied those files.
The regression was then observed failing (133/134), repaired, and all passed.

Actual repaired-player evidence lives outside git at
`~/Library/Caches/playlist-survivors/evidence/ps-ueo/warden/`.
`pause-session/` is a normal seed-1 opening with no health/spawn overrides,
no analyzer entries (fair beatless mode), idle movement and recorded pause at
6.300/resume at 8.300 song seconds. `pause-motion/` has **60 ordered player
frames at 60 fps** from 8.250–9.233 Unity seconds: inspected the resumed
windup, body strike/crescent/shatter and following ordinary swing. The first
post-resume MeleeAction is song 8.500 s; horde time excludes the two-second
hold. Capture filenames are Unity time; consult the event record for exact
song time. This replay sequence is silent and cannot establish speaker sync.
A separate complete Mono replay exits 0 and matches .NET hash
`6CE5614C98812A4D`, **345 events**, presenter faults **none**; the capture's
early exit is not used as proof of complete replay equality.

Independently inspected ordered samples from the supplied trailer 8–10 s,
assembly off-beat control 7–13 s, low-health/death 19–26 s, and the normal
80 BPM full-run motion across opening, dense horde and all three boss windows.
The broad held crescents, local clearing/shatter, yellow aura return to white,
red near-empty vessel, green drops retained through arrivals, phase timer and
open boss lanes are visible. The trailer remains denser and more choreographed;
normal gameplay does not reproduce its staged camera or crowd arrangement.
No additional balance or mesh churn was justified. Faster first choices/boss
kills and dense XP clutter remain the explicitly recorded non-blocking operator
judgements above, together with hand-play, perceived audio sync and target GPU
cost. This review does not certify subjective fun or those hardware checks.
