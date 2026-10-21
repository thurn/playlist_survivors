# QA report: Orpheus galop tech demo

Status of every item in the design's *Manual QA* section
(`docs/design/tech-demo.md`), as of 2026-10-21.

Each item is either **verified** by a command, **partly verified** (a command
covers the measurable part and a person still has to judge the feel), or a
**human gate** that nothing automated can close. Items marked *agent review*
were judged by eye from captures and contact sheets by the implementing agent;
a person should still confirm them.

To reproduce:

```bash
npm run check:all   # lint, types, unit tests, content, assets, songmap, plan, determinism, seeds, analysis
npm run test:e2e    # capture hook, metronome flash, broken level, cached plan
npm run qa          # real-time playback (Chromium, plus a real tab switch in Google Chrome) and a measured
                    # listening pass over the offline-rendered mix; writes captures/qa/report.md and review frames
npm run bench       # whole song at real-time speed, frame-time budgets
```

`captures/` is not committed. `npm run qa` regenerates `captures/qa/`.

## Summary

| Area | State |
|---|---|
| Plan correctness (all 10 output checks, 8 seeds, determinism) | verified |
| Planning time (budget 2 s) | verified: about 1.4–1.6 s in the browser worker, including the output checks |
| Audio clock, picture/audio offset, a real hidden tab in Google Chrome, resize, cached plan, broken level | verified |
| Frame time over the whole song; climax frame rate in Google Chrome | verified (numbers below) |
| Sound mix: clipping, effects level, music in front, no wall of noise, effects on time | verified by measurement; listening still needed |
| Kick line on the beat; kick frames readable | fixed and verified (unit test, captures); art confirmed by agent review |
| Feel: metronome lock by eye and ear, dance, juice, zones, art, listening | **human gates, not yet run** |

## Problems this QA pass found and fixed

- **Kick line danced off the beat.** The kick reached full extension 42% of
  the way through each beat and the dancers stood still on the beat. The
  wind-up now happens before the beat and the kick lands on it. A unit test in
  `tests/planner.test.ts` would have caught this.
- **Kick frames read as a stick.** The extended leg was a straight plank with
  no knee or foot. It now has knee and ankle knobs and a toe-up can-can foot,
  and is long enough to clear the body in side view.
- **Slow-attack samples landed late.** Some heavy, medium, boss, finisher,
  multi-kill and weapon samples took 25–210 ms to reach half their peak. The
  baker now records each sample's onset in `sounds.json`, and the planner
  starts the sample that much early.
- **The mix clipped and the effects buried the music.** The output peaked at
  +0.1 dBFS. The effects sat only 3.3 dB under the music's peak, and in the
  quiet kick-line passage they ran 7 dB over the music. The effects now follow
  the music's loudness as scheduled automation and sit 5.7 dB under its peak,
  and an output trim keeps the peak at −1.1 dBFS.

## First check: the synced metronome

| Item | State | Evidence |
|---|---|---|
| Start | verified | `qa`: the title screen shows "Click to begin" once loading finishes. Clicking starts a running AudioContext. |
| Beat flash | partly verified | e2e: at three sampled galop beats the `?metronome` flash is bright on the beat and has faded just past mid-beat. `qa`: song time advances 0.999–1.003× real time. The picture's song time is 21–32 ms behind the audio thread's, which matches output latency plus one frame, so there is no drift. `qa` also confirms the click track is scheduled. **Human:** watch and listen with `?metronome` from the first beat to the last. |
| Slow-motion camera test | **human gate** | Needs a phone camera, speakers and a screen. |

## Full playthrough

| Item | State | Evidence |
|---|---|---|
| Prelude | partly verified | Capture `section-prelude.png`: the title card is legible over the slow-motion prelude. **Human:** does it feel calm? |
| First chord | partly verified | Capture `section-galop-opening.png` shows the LEVEL UP callout. Plan check "hit-stop clearance" covers 35 stops within 40–150 ms, with clearance before the next primary event. **Human:** is the snap obvious? |
| On the beat | verified | Plan check "impacts on time": 313 hero impacts land on the beat grid, and the hero's impact frame is on the sample nearest the impact. **Human:** confirm the feel. |
| Ensemble rhythm | partly verified | Enemy types are grouped on different subdivisions (`group: beat` / `eighth` in `content/enemies.yaml`, with walk cycles timed in beats), The kick line now kicks on the beat (unit test). **Human:** does the horde *look* like it is dancing? |
| No ghost hits | verified | Plan check "no pass-through" (2646 checked): no hit shape passes through a body it does not damage. "deaths covered" (2949): every death has a hit that covers it. |
| Spawning | partly verified | Plan check "off-screen spawns" (2918): spawns start outside the camera view, at the forest edge. **Human:** do the entrance effects read well? |
| Kick line | verified, agent review | Unit test: every dancing member is in the kick's impact frame on each beat and back to standing late in the beat. Captures at 96.82–97.76 s show all twelve kicking in unison, alternating legs each beat. The sweep starts at 100.01 s, on the phrase's final accent. **Human:** does it read as a can-can? |
| Archers | verified | Plan check "near misses stay near" (16). Capture `archer-near-miss.png` shows the PERFECT callout. |
| Boss | verified | Slams are near misses with telegraphs (capture `boss-slam-dodged.png`). The finisher lands at 139.041 s, the second-to-last chord (capture `boss-death.png`). **Human:** are the telegraphs readable? |
| Finale | verified | `qa`: 456 living monsters just before the final chord (139.58 s) and 0 one beat later. Plan check "clean ending": nobody outlives the ring. Capture `finale-blast.png`. |
| Zones | agent review | Section captures `section-*.png` run from the dark teal twilight forest of the prelude, through amber grass, to red infernal ground with lava and stone faces in the climax. **Human:** judge the mood. |
| Sound | partly verified | Plan check "sound budget": at most 4 voices per sixteenth. The `qa` listening pass renders the whole mix offline through the live mixing graph. No samples clip (peak −1.1 dBFS). Effects sit 5.7 dB under the music's peak, and under the music in every section, by 3.2–12.9 dB. The mix keeps the music's own rise and fall between hits in every section (climax 3.8 → 3.9 dB), so the effects do not fill it into a wall. All 2161 effects are heard within 0.1 ms of their scheduled time. **Human:** listen through. |
| HUD | verified | Unit test: the XP bar is exactly full at each level-up and on the final chord. Captures show level, combo and kill counters climbing. The end card totals (2949 kills) match the plan. |
| Credits | partly verified | Capture `end-card.png` shows the totals and the start of the credits. `check:assets` checks that every licensed asset is in the credits. **Human:** watch the roll to the share-alike notices. |

## Edge cases

| Item | State | Evidence |
|---|---|---|
| Hidden tab | verified | `qa` drives the installed Google Chrome, without Playwright, and switches to a new tab for 10 s during the climax. The page reports `hidden` and draws 0 frames. The audio plays on and schedules 310 sounds meanwhile, because Chrome does not throttle tabs that are playing audio. On return, song time has moved 10.884 s over 10.883 s, the picture is 16 ms from the audio, and 0 sounds start late. One of four runs failed to hide the tab and the cause was never found; the check now logs window ids. A 10 s main-thread stall additionally tests the skip path: 19–20 stale sounds skipped, 0 started late. |
| Window resize | verified | `qa`: resizing to 1280×1024 mid-playback gives a 1280×1024 canvas with the 16:9 view letterboxed, and playback continues. Capture `resize-1280x1024.png` shows the pixel art is still sharp. |
| Cached plan | verified | e2e: the second load reports `planSource = cache` and the same plan hash. |
| Broken level script | verified | e2e: a level with a bar range outside the song shows an error naming the file and line, and no canvas or start button ever appears. `check:content` covers 12 broken fixtures. |
| Performance | verified | `qa`: 119.9 fps in Google Chrome from 105.8 s to 109.8 s, the start of the climax, on a 120 Hz display. `bench` numbers below. |

## Performance

`npm run bench` played the whole song, climax included, at real-time speed in headed Chrome on this machine. It covered 17,346 frames:

| Measure | Median | p95 | Max | Budget |
|---|---|---|---|---|
| JavaScript frame time | 1.70 ms | 3.00 ms | 4.90 ms | 8 ms at p95 |
| GPU frame time | 0.76 ms | 1.54 ms | 4.91 ms | 10 ms at p95 |
| Frame interval | 8.30 ms | 10.20 ms | 10.40 ms | 33 ms max |

The display refreshes at 120 Hz. The longest gap between frames was 10.4 ms, so no frame was dropped, even at that rate.

Planning time (fresh profile, no cache):

- Browser worker, including the output checks: 1407 ms (default seed), 1577 ms (seed 1859).
- Node `check:plan`: about 1.6–1.9 s.
- On battery power the same machine is about twice as slow. Node took 2.4–5.7 s, and the
  code from before this QA pass is just as slow, so this is the power state, not a
  regression. The budget is for the reference machine; measure it on mains power.

## What a person still has to do

The README's *Human review gates* list these steps:

1. Listen to the analysis click track for the song map.
2. Watch `?metronome` from the first beat to the last, and optionally film it in slow motion.
3. Confirm the art: the contact sheets, including the new can-can kick frames.
4. Confirm zones and juice from `captures/qa/`.
5. Listen through the whole level.
6. Do one full manual playthrough with this table at hand.
