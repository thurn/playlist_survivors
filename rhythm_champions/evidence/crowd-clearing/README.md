# Airborne shatter and readable cohorts — ps-oli.3

Validated from staging `2d580bad` on 2026-11-03. This bead changes default
monster/hero GPU death debris and ordinary fodder spawn grouping. It does not
change F animation/sound, hero movement, pickups, boss telegraphs or meshes.

## Delivered behavior

Default shards retain the frozen death pose, crack edges, directional launch
and tumble, then contract individually on their flight paths. They disappear
before their first downward contact, with a maximum lifetime of 0.5 s; there
is no bounce, slide, resting or sink. Low launches receive an upward kick.
The previous path lasted 1.2 s before a further 0.6 s sink. Ordinary, elite,
arrival-clear and hero deaths share this default path. The stand-in fallback
already removes bodies immediately and draws no debris. Structure debris is
separate and unchanged. `--look trailer` retains its pinned shard shader and
old trajectory in a separate compute resource for comparison.

The GPU shatter previously ignored the advertised quality-tier piece limits.
It now retires oldest bursts to keep all types together within 600/1,500/3,000
pieces on Low/Medium/High, including mass clears and downward tier changes.
Buffers remain bounded at their original capacity; no GPU readbacks are added
to gameplay. The shorter burst reduces live integration/draw occupancy.

Fodder enters as eight-body, single-type packs along local spawn arcs, while
spending the existing per-body spawn budget. Draw weights account for the
pack size: bats, ranged enemies and elites retain their authored relative
shares. Section-change rings/lines/swarms, speed, separation, contact damage,
health, cap and 45 m despawn distance remain as before. Boss stop/resume drops
an unfinished ordinary cohort. There is no fade of living enemies and no
added black overlay: the visible pocket is space emptied by actual kills.

## Pathing diagnosis

Local `measurement/` contains the standalone probe and exact baseline Spawner
source. Normal seed-31 reference walking/picks, current content, 120 seconds,
without health/spawn overrides. Both beatless and synthetic 128 BPM belief
streams; the latter also sends section changes at 20/60/100 seconds. Samples
are every 10 seconds. These are simulation experiments, not operator play or
claims about the audio analyzer.

Mean across the twelve snapshots:

| Case | Same-type nearest neighbor | Outside spawn ellipse | Mean snapshot p90 age |
| --- | ---: | ---: | ---: |
| Baseline, beatless | 42.4% | 43.1% | 13.5 s |
| Baseline, beatless, 1.5× speeds | 43.3% | 42.9% | 11.0 s |
| Baseline, 128 BPM | 47.0% | 58.4% | 13.3 s |
| Baseline, 128 BPM, 1.5× speeds | 45.3% | 40.0% | 10.0 s |
| Cohorts, beatless, original speeds | 76.9% | 47.8% | 12.5 s |
| Cohorts, 128 BPM, original speeds | 79.8% | 50.8% | 12.0 s |

The speed hypothesis is partly supported: faster movement reduces age and,
with a beat, offscreen share. It does not improve grouping. The hero travels
at 6 m/s versus initial enemies at 1.2–2.6 m/s, so walking away naturally leaves
some behind. Their ten-second net displacements average 1.30–1.38 m/s in the
baseline (curved routes make this different from actual path speed). There
is no evidence here of a general frozen-pathing failure. Independent weighted
ring picks mix silhouettes/colors everywhere; local cohorts address that
without a global speed increase or deleting older visible threats.

At the baseline 128 BPM final snapshot, skeletons average 10.48 s old, slimes
7.75 s, goblins 5.12 s and bats 6.41 s; fourteen of 198 bodies are over 20 s
old. Cohorts have none over 20 s at that particular final snapshot, but that
is not a promised lifetime cap. Body overlap remains possible during fresh
spawns/moving steps: 2 overlapping pairs baseline versus 9 with cohorts at
that snapshot, using 95% of summed body radii; beatless snapshots have zero.
Existing landing separation is retained. The experiments change encounter
history and random draws; they are comparative diagnostics, not identical
worlds or estimates of player difficulty.

## Local motion and audio evidence

Everything below is local, untracked, vendor-derived evidence under:
`/Users/dthurn/Library/Caches/playlist-survivors/evidence/ps-oli/crowd/`.
No pixels, audio, bakes or vendor meshes are committed.

- `before.mov` and `final.mov`: each 22 s of actual application audio/video
  from a normal 40 s run, 1280×720 High, soundtrack file, seed 31,
  `--walk reference --picks reference`. ScreenCaptureKit records just the
  game window/application sound, 651 video frames, about 1,100 audio buffers,
  both finalized successfully. Explicit per-run logs and recorded sessions
  accompany them. These are scripted real-time gameplay, not operator play.
  The reused launch recipe includes an unsupported `--start-minute 4` flag;
  it is ignored and these are opening runs, not accelerated dense fixtures.
- Inspected ordered before/final motion at gameplay speed, including swings,
  directional breaks, cleared pockets, walking around packs and colored
  approaches. The baseline leaves shard scribble where enemies died; final
  bursts disappear on the flight, revealing dark grid and retained green XP.
  `final-motion.jpg` is only a contact sheet of the motion, not its substitute.
- Compared ordered frames from the exact supplied
  `/Users/dthurn/Downloads/trailer-action-neon-edge.mp4`, 8–10 s. Its large
  colored formations and dark corridors motivate local cohorts. The trailer
  is denser, with staged choreography, camera and vendor silhouettes; this
  does not claim to reproduce that shot or redesign those meshes.
- `dense-session/` is a separate normal 650 s .NET reference run with the
  playlist fixture: 18,568 kills, level 33, minimum health 78. No health,
  damage, spawn or clock overrides. Ogre arrival clears 136 visible bodies;
  Conductor arrival clears 361. These retain the base revision's 5/10-minute
  schedule; the sibling pacing bead owns its 3/6/9 replacement.
- `dense-final-{low,medium,high}/` and matching silent MP4s: actual player
  replay frames at song 230–233, 303.3–305.3 and 633.3–635.3 s, moving dense
  horde and both arrival clears on every tier. Replay speed 4 captures one
  frame per 1/15 song second; MP4s play at 15 fps, preserving gameplay speed,
  with cuts between the three windows. Each launch exits 0, 105 frames.
  Inspected sequence: flying fragments, then open arena, surviving green XP,
  distinct boss body and visible danger. The XP carpet remains busy; the
  grid/beat wave and simultaneous yellow/white attacks remain readable once
  the long-lived debris is removed, so no blanket brightness cut was made.
- Complete Mono replay, separate from early-quit still capture:
  `dense-complete-replay.log`, exit 0, hash `23915F532D84D539`, 130,932 events,
  **matches**, presenter faults **none**.

Earlier `after*` movies/renders are intermediate experiments. The first
`after.mov` capture failed to find its window when the editor took focus;
`after-recovered.mov` contains only 56 frames before the player exited and
is not used as the final evidence. Use `final.mov` and `dense-final-*` above.

## Verification and limits

507 simulation tests and 142 Unity EditMode tests pass, with no skips. New
behavior checks cover local packs under the per-body rate, preserved elite
share, real GPU carried-piece launch, airborne disappearance, and 320-body
mass kills within every tier budget followed by complete removal. Existing
wave, beatless, steering, contact, replay and boss-clear checks remain green.
Fresh Mono build succeeds; reported shader variants 289 of 300.

Bounded 12 s Low-tier crowd bench: 1,000 walking monsters, 20 deaths/s, 1080p
manual offscreen drawing on Apple M5 Max. Crowd synchronized frame p50/p99
3.498/5.197 ms; empty 1.021/2.571 ms; CPU submission p50/p99 1.104/1.362 ms.
`bench-low.json` retains the results. Another worker's Unity process was
present; this is a loaded-machine observation. Synchronized whole-frame
readback timing is not GPU-only timing and does not establish the 1.5 ms GPU
proxy or M1 target. No new allocation or spatial-query pass was added to
horde motion. The tier bounds and shorter lifetime reduce debris residency;
windowed GPU profiling and target-hardware acceptance remain pending.

These captures do not certify hand-play fun, speaker sync, a 1,000-body
full-build boss fight, all songs/seeds or absence of macOS display faults.
No attempt was made to reproduce the reported display freeze. F audiovisual
impact, elite-special removal, judgement/logging and encounter pacing belong
to sibling beads; their integrated result still needs the final Warden.

## Replay fixture repair (ps-99n)

The cohort change deliberately changes survivor spawn draws, positions and
the reference player's later choices. The committed live `fixture-level`
still described the previous spawn behavior, so both .NET and Mono rejected
its hash in slow CI. Re-recorded it with the existing explicit
`LevelRunTests.RecordTheLevelSession` writer after integrating the rhythm and
three-minute pacing changes: 75.017 song seconds, 2,198 stream entries,
850 inputs and 10,991 events, hash `B4DFC5DEB472D752`.
The replay comparison remains strict; archived fixture rules are unchanged.
