# Walking-sword assembly — ps-oli.5, 2026-11-03

Fresh worktree starts at `5636475f`: F impact `10e629b9`, log isolation
`a210898f`, cohorts/debris `b6027cff`, judgement/diagnostics `d59fc06f` and
`c29053af`, pacing `ec28b76e` and `6c8eed74`, regenerated level fixture
`5636475f`. This assembly reconciles the current design/style/implementation
plan; it does not change gameplay balance, frozen projects or vendor assets.

## Reproduction and provenance

Local untracked artifacts are under
`~/Library/Caches/playlist-survivors/evidence/ps-oli5/`. They include proprietary
vendor-derived pixels and local audio, and must not enter public git.
`live.py` and `launch.json` retain the exact fresh Mono launch: Low,
1280×720 window, seed 31, normal content, reference card preferences, small
square keyboard movement, F every 0.8 s after controlled opening inputs,
Space occasionally for repositioning. The trailer soundtrack loop is input
to the real music plugin/analyzer. No health, damage, XP, spawn, boss-call,
clock or loadout override is used. This is automated real-time gameplay,
**not operator hand-play**. The launch has a 750-second ceiling; it was closed
normally after 555.883 song seconds, after Ogre defeat and a substantial Conductor sample. Its
fixed square does not pursue bosses or react to their waves. It is not a
full-run victory or evidence of human win rates.

ScreenCaptureKit captures only the application window and its audio with
shared timestamps, not microphone or other apps. Movies are 24-second excerpts
at actual gameplay speed; no trailer video is substituted. Failed captures
are excluded: the first began before the window registered; overlapping
capture streams later caused SCStreamError -3808. `early.mov`, `boss-1.mov` and
`boss-2.mov` have finalized video and audio tracks. Recipes/logs retain failed
attempts as diagnostics, not validation. The successful boss recordings are
sequential. `early.mov` covers song
111.10–135.33 (it is a dense horde excerpt despite its filename), `boss-1.mov`
183.31–207.36 and `boss-2.mov` the Conductor opening. The failed `dense.mov`
has no finalized movie header and is not evidence.

## Integrated input and logging checks

The independent diagnostic pair begins
`20261103T200423719-c35410d54efa41fbb1d439f46721309d.log` under
`champions/run-diagnostics/`; `diagnostics-path.txt` retains the exact location.
The engine log is explicitly `live.log`, preserving other worktrees' evidence.
At heard 14.890/frontier 15.060 seconds, measured lead was 169.7 ms, effective
output 102.0 ms, input/video calibration zero, with zero analyzer stalls,
keyboard drops or sound scheduler drops. Later snapshots and final totals
remain in the retained pair; snapshots are not an exact aura integral.

The same live path gives distinct diagnoses:

| Heard input | Result | Candidate / signed delta / window |
| --- | --- | --- |
| F 1.720 | Accepted with fallback aura | Beatless, no reliable beat ahead |
| F 4.930 | Accepted HalfBeat | 4.990 / −60 ms / ±60.8 ms; no onset required |
| F 8.280 | Accepted Beat | 8.280 / 0 ms / ±78.0 ms |
| F 8.410 | Cooldown drop | Off, nearest half 8.475 / −65 ms / ±62.4 ms |
| F 9.160 | Accepted Off; attack still executes | Half 9.250 / −90 ms / ±60.8 ms |
| Space 10.220 | Accepted Beat, travels | 10.220 / 0 ms / ±76.0 ms |
| F 10.740 | Buffered, then fired | Half 10.795 / −55 ms / ±62.4 ms retained |

The corresponding impact rows occur at 1.870, 5.080, 8.430, 9.310, 10.370
and 10.970. The cooldown drop has no second impact. These are script-heard
inputs through the same sim path, not measurements of physical keyboard
latency. Controlled early/late, genuine quarter-beat misses, tempo changes,
late reveals and frame-lag buffering are additionally exercised by the
passing judgement fixtures and [rhythm report](../rhythm-diagnostics/README.md).

## Actual F-primary outcome

Normal close exits 0 and writes **outcome=aborted**, not victory. Final combat
555.875 s, horde 360, level 31, 14,945 kills, 14,822 collected XP, 468 damage
over the run and 100/100 final HP after normal healing. Replay sampling every
0.1 s finds minimum HP 70.3 and peak 833 alive. First pick is 20.350 s; there
are 30 normal choices plus one Ogre reward. Build: Sword 8, Might/Tempo/Armor/
Regeneration 5 and Reach 4. This is a focused build, not six weapons.

The detail file is 209,441 bytes with zero omitted rows; its final summary
includes 666 immediate F accepts, one buffered/fired F and 15 F cooldown
drops; 15 Space accepts and 27 Space cooldown drops. There are 667 F impacts
and 15 Space impacts. Of immediate F accepts, 178 are Off and still attack.
The last audio snapshot reports 0 analyzer stalls, late entries, keyboard
losses or scheduler drops; 1,665 sounds used late/as-soon-as-possible scheduling
out of 8,699 scheduled. Late playback is distinct from missing sound or bad
rhythm grading. Sampled aura uptime is about 98%, not an exact interval sum.
`EliteTelegraph/EliteMove` counters include the retained mushroom ranged path;
their nonzero totals do not imply the removed big ork/troll specials returned.

Ogre arrives at 182.860 / horde 180, dies at 310.900 / horde 180. Conductor
arrives at 493.620 / horde 360 and remains alive at normal close. Ogre's
128.04-second fight is longer than the pursuing reference builds; the square
route repeatedly cuts empty space away from the boss. No hidden damage boost
or boss relocation was added to force a win. Dragon coverage comes from the
complete natural-clock defensive record below, not this partial live run.

The actual file/analyzer record independently replays exactly in .NET and
the fresh Mono player (no presenter faults):
`336C8F66E17D53DF`, **94,466 events**, 42,659 entries and 1,879 inputs.
`live-outcome.txt`, complete input/entry files and the final diagnostic pair
remain local; diagnostic output survives the worktree's later removal.

## Natural clocks and complete replay checks

Fresh full suite: **512 simulation tests pass**. Independently replayed all
four normal-clock pacing records against current content, without updating
the records:

| Scenario | Current .NET hash | Events | Outcome in source record |
| --- | --- | ---: | --- |
| Focused / silence | `2F06C7581C356942` | 97,085 | Victory |
| Broad / 80 BPM | `D253AB92A6A6ADB3` | 128,982 | Victory |
| Defensive / 174 BPM | `78C13CDB861E73E3` | 108,011 | Victory |
| Idle / 128 BPM | `60A6A990654DBD0D` | 20,503 | Death |

These source runs use synthetic beliefs/silence, not the real analyzer.
Their natural 180/360/540 horde-second calls, 28–30 normal choices, two boss
rewards, full heals, finite reinforcements, conserved stored XP and differing
fight lengths are detailed in [pacing evidence](../three-minute-pacing/README.md).
Its complete fresh Mono replay independently rerun here also matches
`78C13CDB861E73E3`, 108,011 events, victory and no presenter faults. Both full
replays finish with exit 0; `complete-replays.log` retains the results. Starting
collection at 3.4 m, remaining value at 4 m, and Attraction's later collection
are covered by focused tests. Uncollected XP remains stored.

## Short white-whiff and hit capture

`whiff.py`, `whiff.mov` and `whiff-session/` add a second actual file/analyzer
run at Low, starting without a prior rhythm aura: walking F at 9.160 s hits
empty ground at 9.310, radius **4.2 m**, with no body-contact cue. Space at
10.220 travels; F at 10.740 buffers and fires at readiness. F impacts at
16.800/17.500 hit four/six bodies. These retain the aura and 5.94 m reach.
The 18-second movie has 531 frames and 900 captured audio buffers, finalized
successfully. The normal 28-second bounded player exits 0, retaining the
27.275-second sim record, 53 kills, no earned choice and an aborted summary.
It matches .NET as `06A736FDF60E64AE`, 1,044 events; the separate Mono replay
matches too, with no presenter faults.

Music alignment gives movie start song 6.0145 s. The pitched air cue on the
white whiff correlates 0.356 at about **107 ms after acceptance**, consistent
with this route's 102 ms reported output latency. This is a weaker noise-cue
match, not a precise perceived-sync measurement. Crowd punches at 16.800 and
17.500 correlate **0.887 / 0.763**, starts about **−3.4 / −0.5 ms** from damage.
The actual mix therefore contains distinct blade-air and contact sounds;
zero-contact impacts do not ask for an armor/body hit. The whiff still moves
the chest, legs and sword while the hero walks. Local waveform recipes retain
the analysis; no soundtrack, clip or vendor audio is committed.

## Motion, mix and limits

[Walking-sword evidence](../walking-sword/README.md) retains before/final
actual mix and motion, white/empowered hits and whiffs, rapid alternation,
replay equality and contact-waveform matches. [Crowd evidence](../crowd-clearing/README.md)
retains the comparative pathing experiment, tier-bound mass-shatter checks
and before/final cohorts. Their old-clock dense records stay explicitly
historical; they are not evidence of today's boss schedule.

`early.mov` contains 696 video frames and 1,207 captured audio buffers;
`boss-1.mov` 699/1,200 and `boss-2.mov` 700/1,208. Their original timestamped
tracks are retained, not a separately dubbed soundtrack. Early/boss-1 video
frame gaps p50 are 33.3 ms, p99 41.7 ms, maxima 50.0/41.7 ms. These are capture
cadence observations at a requested 30 fps, not a 60 fps game/GPU measurement.

Music waveform alignment places the early movie start at song 111.101 s and
Ogre movie at 183.310 s. Removing a fitted copy of the input gives whole-mix
SFX/other residual about −5.2/−8.4 dB relative to music. This residual includes
AAC and concurrent effects, not just F. The actual pitched crowd-punch cue at
111.950 s (23 contacts) matches at correlation 0.717, start about 3.5 ms before
the nominal impact. The two-body/single-body matches are weak (0.10/0.12), so
those captures do not independently establish their exact audible onset.
Whiff air at 185.400 s has a weaker 0.31 match about 105 ms late, consistent
with the reported output lead. Retained raw mix, recipes and sibling controlled
captures support listening; waveform correlation cannot certify perceived
speaker/display sync. No contact cue is requested for the logged zero-contact
F impacts at 184.750–189.550 s.

Inspected ordered cut/clear samples and played the dense horde and current-clock
three-boss motion excerpts at 1×. The three-boss replay movie is silent; its
frames cover each arrival, Ogre lane/stomp, Conductor fan/marks, and Dragon
breath/tail/dive/traveling wave. Red fronts remain visible through the
automatic blade effects; each arrival removes the horde and retains green XP.
The new F-primary runtime and the existing current-clock defensive motion
supply complementary checks: real music/SFX, walking cut and empty space,
then all three natural arrivals and boss attack types. Green XP can remain
visually busy. There is no justified blanket dimming or global speed increase.
The supplied trailer remains denser and more choreographed, with staged
camera/crowds. It is an aesthetic reference, not evidence of live gameplay.

Fresh Mono build succeeds; **147/147 Unity EditMode tests pass, no skips**.
No gameplay change is required by the integrated checks. Subjective F feel, speaker/Bluetooth synchronization and human build
balance remain pending. The existing loaded-M5 crowd benchmark is a
readback measurement, not GPU-only or M1 acceptance; this assembly adds no
performance claim from captures. No intentional display-freeze reproduction.
Final Warden and slow CI are owned by the parent run.
