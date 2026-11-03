# Three-minute pacing — ps-oli.4, 2026-11-03

Built from staging `b6027cff` plus this change. Boss calls are **180/360/540
horde seconds**. Warning/arrival time and boss fights do not advance horde
time; pauses and cards freeze both clocks. Defeating Dragon ends the level.
The density/toughness ramps and first-half type join times use 60% of their
previous times (600 target bodies / 3.5× toughness reached at 7:12). Existing
cohort weights, cap, speeds, XP curve and boss health/moves remain unchanged.
The Ogre still has 150 HP, Conductor 350, Dragon 650. No hidden build scaling.

Starting pickup reach is 3.5 m, up from 3 m (+17% radius). Attraction remains
20% of baseline per level: 4.2 m after one level, 7 m at maximum. A direct
collection test receives XP at 3.4 m without moving, leaves 4 m XP stored,
then receives that remaining value after one Attraction level. Existing
merge/streaming/flight-cap/arrival tests retain every XP unit. Local cell
lookups rise from 98 to 128 per tick at the stationary origin; work remains
bounded by nearby cells rather than accumulated world loot.

Big ork and troll **ordinary elite** cast rows have no move. They still
approach, survive ordinary blows and deal contact damage, verified independently
for both bodies. No charge/slam telegraph, move or associated hidden damage is
created. Their move implementations and archived content remain available for
historical fixtures. Mushrooms are a separate ranged role: their visible spore
warning/projectile remains. All three bosses retain their warnings and attacks.

## Natural full runs

`LevelRunTests.Run` drives normal movement, ordinary offers, collection and
reactive boss dodges from starting Sword through the real clock thresholds.
There are no health, damage, spawn, XP, loadout or early-encounter overrides.
These are deterministic scripted players with synthetic musical beliefs or
silence, **not operator hand-play or analyzer/audio evidence**.

| Build / BPM / seed | Result, combat seconds | Normal choices / first | Kills | Minimum HP | Boss fight seconds: Ogre / Conductor / Dragon |
| --- | --- | --- | --- | --- | --- |
| Focused / silent / 31 | Victory, 733.8 | 28 / 29.1 s | 12,112 | 74 | 33.4 / 58.4 / 95.9 |
| Broad / 80 / 31 | Victory, 609.2 | 29 / 44.6 s | 10,838 | 89 | 8.7 / 19.4 / 29.4 |
| Defensive / 174 / 31 | Victory, 737.1 | 28 / 20.6 s | 11,292 | 74 | 28.3 / 60.2 / 100.4 |

All finish at exactly 540 horde seconds and receive two boss rewards. Boss
arrival horde clocks are 180/360/540; entry comes 2–4 seconds after the call,
with horde time already frozen. Recorded per-arrival levels are 22/27/29,
18/26/30 and 21/27/29 respectively. Final collected/outstanding XP:
11,296/2,813.5; 11,621.5/660; 10,708/1,375. Outstanding value remains stored,
not silently deleted. All three complete input/stream records replay exactly
in .NET: `2F06C7581C356942`, `189A94B740505D3B`, `0DA474DC46AA8C52`.
A fourth normal suite case (focused / 128 / seed 32) wins at 707.0 s with 29
choices and first choice 20.8 s, with exact replay too.

The natural idle / 128 / seed 31 case dies at 202.0 combat seconds during
the Ogre, horde clock 180, 4,487 kills, level 15 and 14 choices. It replays
exactly as `60A6A990654DBD0D`. The idle script chooses cards but supplies no
movement or defensive inputs. This verifies a real failure path, not a forced
health reduction. Skilled scripted wins do not establish human win rates.
Broad's first choice and very short Ogre remain outside the older 20–30 s
choice / 1–2 minute fight goals; no compensating damage nerf was introduced.
The shorter run already preserves the 25–35 normal-choice target.

## Built player and limits

Local untracked artifacts: `/Users/dthurn/Library/Caches/playlist-survivors/evidence/ps-oli4/`.
No vendor-derived frames or media enter git. `focused/`, `broad/`,
`defensive/`, `idle/` contain complete records, synthetic entries and
`outcome.txt` with per-entry HP/level/XP and pattern counts.

`defensive-motion/` contains 900 ordered JPEGs from a fresh Mono player's
natural defensive replay at Low. Song windows 179–194, 393–410, 636–655 and
688–697 cover each arrival, Ogre lane/stomp, Conductor fan lanes/marks and
Dragon breath, tail/dive and expanding waves. Replay speed is 4; filenames
are Unity time (song time approximately four times the filename). Sampling
is 15 song-time frames/second. Inspect `motion-sheet.jpg` for the index and
the ordered sequences at gameplay speed. These are silent actual runtime
renders; they do not prove sound sync. Inspection confirms the 03:00/06:00/
09:00 horde clocks switch to BOSS elapsed time, the body wipe leaves open
lanes, green drops survive entry, and red warnings remain visible beside
white/yellow weapon attacks. Defensive Ogre emits its wave; Dragon emits ten
wave marks and all its signature move types during this naturally reached
fight. Reinforcements remain finite (at most three groups, nine weak bodies)
and their warning/pause/conservation tests remain green.

A separate complete Mono replay is logged in `defensive-complete-player.log`;
it exits 0 with exact hash `0DA474DC46AA8C52`, 106,256/106,256 events
and presenter faults **none**. The capture launch exits at the last requested
frame and is not used as proof of a complete replay. An initial attempt with
Unity's unsupported Null graphics device could not initialize compute crowd
shaders; it was stopped, its repeating log bounded, and rerun with Metal.
This invocation failure is not counted as gameplay evidence.

Reproduce records with the explicit test
`SurvivorAssemblyTests.RecordThreeMinutePacingEvidence`, setting
`CHAMPIONS_PACING_EVIDENCE` to a local output folder. Replay via
`just champions-tool play --headless --replay <folder>/defensive --replay-speed 8`.
The helper preserves Metal; do not pass Unity `-nographics` directly.

Verification: 511 sim tests passed; the final threshold/collection-focused
rerun passes 48 tests, including nested cards/pause immediately before the
first threshold. Unity EditMode passes 147/147 with no skips; fresh Mono
build succeeds. Human balance, sound perception and M1/GPU costs remain
unverified and are not inferred from these results. Final fast checks and
staging delivery are recorded on the bead; the parent run owns slow CI.
