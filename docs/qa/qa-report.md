# QA report: Orpheus galop tech demo

Status of every item in the design's *Manual QA* section
(`docs/design/tech-demo.md`), as of 2026-10-21.

Each item is either **verified** by a command, **partly verified** (a command
covers the measurable part and a person still has to judge the feel), or a
**human gate** that nothing automated can close.

To reproduce:

```bash
npm run check:all   # lint, types, unit tests, content, assets, songmap, plan, determinism, seeds, analysis
npm run test:e2e    # capture hook, metronome flash, broken level, cached plan
npm run qa          # real-time playback in Chromium; writes captures/qa/report.md and review frames
npm run bench       # whole song at real-time speed, frame-time budgets
```

`captures/` is not committed. `npm run qa` regenerates `captures/qa/`.

## Summary

| Area | State |
|---|---|
| Plan correctness (all 10 output checks, 8 seeds, determinism) | verified |
| Planning time (budget 2 s) | verified: about 1.4–1.6 s in the browser worker, including the output checks |
| Audio clock, picture/audio offset, hidden-tab recovery, resize, cached plan, broken level | verified |
| Frame time over the whole song | verified by `npm run bench` (numbers below) |
| Feel: metronome lock by eye and ear, dance, juice, zones, sound mix, art | **human gates, not yet run** |

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
| Ensemble rhythm | **human gate** | Enemy types are grouped on different subdivisions (`group: beat` / `eighth` in `content/enemies.yaml`, with walk cycles timed in beats), but whether the horde *looks* like it is dancing is a judgement call. |
| No ghost hits | verified | Plan check "no pass-through" (2646 checked): no hit shape passes through a body it does not damage. "deaths covered" (2949): every death has a hit that covers it. |
| Spawning | partly verified | Plan check "off-screen spawns" (2918): spawns start outside the camera view, at the forest edge. **Human:** do the entrance effects read well? |
| Kick line | partly verified | Capture `kick-line-sweep.png`. The sweep starts at 100.01 s, on the phrase's final accent. **Human:** does it read as a can-can? (The kick frames are part of the art gate.) |
| Archers | verified | Plan check "near misses stay near" (16). Capture `archer-near-miss.png` shows the PERFECT callout. |
| Boss | verified | Slams are near misses with telegraphs (capture `boss-slam-dodged.png`). The finisher lands at 139.041 s, the second-to-last chord (capture `boss-death.png`). **Human:** are the telegraphs readable? |
| Finale | verified | `qa`: 456 living monsters just before the final chord (139.58 s) and 0 one beat later. Plan check "clean ending": nobody outlives the ring. Capture `finale-blast.png`. |
| Zones | **human gate** | Section captures `section-*.png` run from twilight forest to infernal grove. Their mood needs a person to judge. |
| Sound | partly verified | Plan check "sound budget" (841 sixteenths): at most 4 voices per sixteenth. Sounds are scheduled on the audio clock. **Human:** listen through; does the climax turn into a wall of noise? |
| HUD | partly verified | Captures show level, XP, combo and kill counters climbing. The end card totals (2949 kills) match the plan. **Human:** does the XP bar fill exactly at each level-up? |
| Credits | partly verified | Capture `end-card.png` shows the totals and the start of the credits. `check:assets` checks that every licensed asset is in the credits. **Human:** watch the roll to the share-alike notices. |

## Edge cases

| Item | State | Evidence |
|---|---|---|
| Hidden tab | partly verified | Automated browsers never really hide a tab, so `qa` stalls the main thread for 10 s instead, which stops frames and timers the same way. After the stall, song time had moved 10.40 s in 10.40 s. The picture was 24 ms from the audio. 20 stale sounds were skipped and 0 started late. **Human:** switch tabs in desktop Chrome during the climax. |
| Window resize | verified | `qa`: resizing to 1280×1024 mid-playback gives a 1280×1024 canvas with the 16:9 view letterboxed, and playback continues. Capture `resize-1280x1024.png` shows the pixel art is still sharp. |
| Cached plan | verified | e2e: the second load reports `planSource = cache` and the same plan hash. |
| Broken level script | verified | e2e: a level with a bar range outside the song shows an error naming the file and line, and no canvas or start button ever appears. `check:content` covers 12 broken fixtures. |
| Performance | partly verified | See below. **Human:** watch Chrome's FPS meter on the reference machine. |

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

## What a person still has to do

The README's *Human review gates* list these steps:

1. Listen to the analysis click track for the song map.
2. Watch `?metronome` from the first beat to the last, and optionally film it in slow motion.
3. Review the art contact sheets, including the can-can kick frames.
4. Review zones and juice from `captures/qa/`.
5. Listen through the whole level.
6. Switch tabs for real during the climax.
7. Do one full manual playthrough with this table at hand.
