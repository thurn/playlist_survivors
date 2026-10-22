# QA report: Orpheus galop tech demo

Status of every item in the *Manual QA* sections of
[`docs/design/beat-steps.md`](../design/beat-steps.md) (the simulated
playthrough), of [`docs/design/tech-demo.md`](../design/tech-demo.md) where
it still applies, and of [`docs/design/interactive.md`](../design/interactive.md)
(play mode; see [Play mode](#play-mode)), as of 2026-10-21.

Each item is either **verified** by a command, **partly verified** (a command
covers the measurable part and a person still has to judge the feel), or a
**human gate** that nothing automated can close. Items marked *agent review*
were judged by eye from captures by the implementing agent; a person should
still confirm them.

To reproduce:

```bash
npm run check:all   # lint, types, unit tests, content, assets, songmap, analysis, and check:seeds
                    # (the level's seed with causality and determinism, then three more seeds)
npm run test:e2e    # capture hook, metronome flash, broken level, cached plan, ?player=
npm run qa          # about 45 s: real-time playback in Chromium, the skilled and casual players
                    # playthrough, and a measured listening pass over the offline-rendered mix;
                    # writes captures/qa/report.md and review frames
npm run bench       # whole song at real-time speed, frame-time budgets
npm run check:play  # play mode, headless: moderate and casual players on four seeds
npm run qa -- --play          # only play mode's items, in live play with synthetic key presses
npm run bench -- --play       # live play from theme A to the climax, a press on every beat
```

`captures/` is not committed. `npm run qa` regenerates `captures/qa/`.

## Summary

| Area | State |
|---|---|
| Recording checks (14 checks, skilled and casual players, 4 seeds in CI, 6 more by hand) | verified |
| Causality: re-simulating with the presses after a cut removed changes nothing before it | verified (`check:seeds` on the level's seed; unit test on the synthetic song) |
| Simulation time (budget 3 s) | verified: about 1.0–1.3 s in Node, 1.1–1.2 s in the browser worker including the checks |
| Every strike and its impact sound on the grid, for every profile | verified |
| Stillness between steps, no skating, every movement from an input | verified |
| Reward spectrum: casual below skilled, perfect at the top | verified (numbers below) |
| Signature moments: boss on its chord, near misses, the kick line's fall | verified for the skilled and perfect players |
| Audio clock, picture/audio offset, hidden tab, resize, cached plan, broken level | verified |
| Sound mix: clipping, effects level, music in front, no wall of noise, effects on time | verified by measurement; listening still needed |
| Feel: steps, swings toward the crowd, juice, the dance of the horde | **human gates, not yet run** |
| The frozen demo: plan hashes for 4 seeds × 3 demo profiles unchanged since before play mode | verified (`tests/golden/`, in `check`) |
| Play mode: live play equals a replay of its key events; rollbacks leave no trace | verified (unit tests; `qa --play`: Watch my run identical) |
| Play mode: signature moments for the moderate player, waves, one step per slot | verified (`check:play`, four seeds, in CI) |
| Play mode: feel with real hands and ears | **human gates, not yet run** |

## Problems found and fixed while building the simulation

- **Cutting presses changed earlier frames.** Enemies were integrated over
  steps that a press could split, so a press after the cut time changed
  rounding before it. Enemies now integrate only on the fixed 1/120 s steps,
  are extrapolated between them, and world time comes from the warp's
  breakpoints. Automatic swings are scheduled by their own (wind-up) time,
  not their strike time.
- **A cleave's shockwave could not catch a charging line**, and its bands
  were pushed past two beats by the cleave's own hit-stop. Bands now keep
  their sixteenth-note slots and each reaches everyone the front has passed.
- **The sword ignored the boss and the kick line in a crowd of hundreds.**
  The aim weights are now 1000 for the boss and 20 for formation members, and a
  cleave aims with its shockwave's reach.
- **Two archers' arrows could not both be near-missed by one step.** Archers
  of a volley now share a post, 20 px apart, and the virtual player judges a
  dodge by where the step has taken the hero when the arrow lands.
- **Enemies appeared on screen for a frame.** An enemy's first sample was
  taken up to 33 ms after it spawned, a few pixels inside the view. The spawn
  spot is now its first sample.
- **The prelude's mix filled in.** Cleave shockwaves trigger kills on eight
  sixteenth notes, each with a ringing multi-kill sound. Free movement now
  groups kill sounds per beat, uses the short single-kill sound, and the
  prelude palette is quieter.

## First assembled check

| Item | State | Evidence |
|---|---|---|
| Open `?start=1:10` | verified | `qa`: playback starts on click with `?start`; captures `theme-a-75.20.png` and `theme-a-75.30.png`. |
| The hero stands still between steps | verified | `qa`: the hero moves 0.000 px between 75.20 s and 75.30 s. Unit test: still inside 20 beat gaps of theme A. Check "every hero movement has an input": step movement only within 110 ms of a press. |
| Each step is quick and firm | partly verified | The step is a 110 ms cubic ease-out with a 5 px hop and a dust puff on landing; check "every hero movement has an input" bounds every step to 44 px. **Human:** does it feel firm? |
| Swings go toward the crowd | verified, agent review | Check "auto-aim is the rule": every swing's direction is re-derived from its recorded aim snapshot, and the snapshot matches the recorded hero and enemies. Captures show swings into the thick of the crowd. **Human:** confirm. |
| Enemies chase and surround | partly verified, agent review | Enemies steer at the hero and stop at contact; check "population" keeps them on the level's curve and "spacing" keeps them apart. **Human:** does it read as a horde closing in? |
| Every sword impact on the beat | verified | Check "impacts on the grid": every strike within 1 ms of a grid time and its impact sound exactly on it, for every profile. `qa` listening pass: 2240 sound events heard within 0.1 ms of their scheduled times. **Human:** listen. |

## Full playthrough (`?player=skilled`)

| Item | State | Evidence |
|---|---|---|
| Prelude | partly verified | Unit test: the hero walks with held keys, swings by itself every two beats and cleaves on strong anchors; presses there are not rated. Check "no skating": the walk cycle advances with the distance walked. **Human:** does it feel calm? |
| Count-in | verified, agent review | The beat track fades in and four count marks pulse on the last four beats of the rubato passage, before the galop vamp's first downbeat at 43.21 s. |
| Galop vamp | partly verified | The quiet vamp (bars 15–30) is a pulsed section: one step and one swing per bar at full speed, at the galop's tempo. **Human:** does the first beat feel like it arrives at 0:43? |
| Galop opening | verified | Presses once per bar; cleaves with hit-stops on the tutti chords (check "hit-stops": 34 stops within 40–150 ms). Capture `first-cleave.png`. |
| Theme A | verified | A step and a swing on every beat; the groove sits at +20% (an orc's 1.15 health falls to one 1.2 swing). |
| Kick line | verified | Check "signature moments": every dancer dies within two beats of the release, most to the release cleave's shockwave. Capture `kick-line-release.png`. **Human:** does the ripple read? |
| Archers | verified | Check "signature moments": at least 90% of threats are near misses (all of them for the skilled player on the four CI seeds). Capture `archer-near-miss.png`. |
| Boss | verified | Slam markers drop under the hero, which steps off them (capture `boss-slam-dodged.png`). The boss dies on its death chord at 139.041 s (check "signature moments"; capture `boss-death.png`). |
| Finale | verified | `qa`: 458 living monsters just before the final chord and 0 one beat later; check "clean ending". The end card shows kills, rating counts and the average groove. |
| Key display and groove | verified, agent review | Captures show the keys lit with their rating and the groove meter at +20% through the galop. |

## The reward spectrum

| Item | State | Evidence |
|---|---|---|
| `?player=casual` is calmer | verified | One press per bar (207 presses against the skilled 267). Average groove about +13% (12–14% over four seeds) against +20%; 7600–8000 kills against 8700–9000. Orcs often need two swings below a 0.75 groove. The design's +5–10% would need a far less generous accuracy table: see *Tuning Notes* in the design. |
| Casual impacts stay on the beat | verified | `qa`: 196 casual swings, 0 off the grid, although many presses land 70–250 ms off the beat. Unit test likewise. |
| The demo still progresses | verified | The casual run passes every check, reaches the finale and is cleared by the blast; its level-ups arrive one to four seconds after their scheduled moments. |
| Nothing punishes the player | verified | There is no miss rating, no hero health loss (arrows and slams that touch the hero do no damage) and no combo. |
| `?player=perfect` | verified | `qa`: every press Perfect, groove +20%. |

## Edge cases

| Item | State | Evidence |
|---|---|---|
| A press during a hit-stop | verified | The simulation ends a hit-stop at a press (check "hit-stops" accepts stops shorter than 40 ms only when a press ended them). **Human:** find one by eye. |
| A diagonal step | verified | Unit test: a perpendicular key within 40 ms bends the step smoothly onto the diagonal, landing 44 px away. The virtual player presses diagonals as two keys a few milliseconds apart. |
| The corridor edge | verified | Unit test: a step toward the edge stops at it. |
| Other seeds | verified | `check:seeds` (1859, 7, 2024) passes every check; seeds 1, 2, 3, 42, 99 and 500 also pass. |
| Determinism | verified | `check:seeds`: two runs of the level's seed give the same plan hash. e2e: a reload loads the cached plan with the same hash. |
| Hidden tab | verified | `qa` stalls the page's main thread for 3 s, which stops frames and timers as a hidden tab does, while the audio plays on. On return the picture jumps to the right moment (song time moved 3.0 s over 3.0 s, the picture within 30 ms of the audio), and the sounds that fell behind are skipped, not started late. A real tab switch in Google Chrome was dropped from `qa`: it was slow (it played to the climax in real time) and flaky (Chrome often never hid the page). A real tab switch is still worth trying by hand. |
| Window resize, cached plan, broken level script | verified | As before: `qa` resize letterboxing, e2e cached plan and broken level (13 broken fixtures in `check:content`). |

## Sound

`qa`'s listening pass renders the whole mix offline through the live mixing
graph:

- No samples clip (peak −0.9 dBFS).
- The effects' 95th percentile sits 5.0 dB under the music's peak, and the
  music is in front in every section, by 1.1–14.6 dB.
- The mix keeps the music's own rise and fall between hits in every section
  (prelude 19.5 → 18.6 dB, climax 3.8 → 4.3 dB), so the effects do not fill it
  into a wall.
- All 2240 effects are heard within 0.1 ms of their scheduled time.
- Check "sound budget": at most 4 voices start per sixteenth note.

**Human:** listen through, especially the cleaves' shockwaves and the step
rustles, which are meant to sit well under the music.

## Performance

`npm run bench` played the whole song at real-time speed in headed Chrome on
this machine, 17,343 frames on a 120 Hz display:

| Measure | Median | p95 | Max | Budget |
|---|---|---|---|---|
| JavaScript frame time | 1.80 ms | 3.00 ms | 5.70 ms | 8 ms at p95 |
| GPU frame time | 0.76 ms | 1.71 ms | 8.39 ms | 10 ms at p95 |
| Frame interval | 8.30 ms | 9.30 ms | 9.50 ms | 33 ms max |

No frame was dropped. `qa` measured 120 fps through the climax in Google
Chrome. The simulation runs once at load: about 1.15 s in the browser worker
including its checks, then the plan cache makes reloads instant.

## Play mode

The Manual QA of [interactive.md](../design/interactive.md#manual-qa). The
commands judge everything measurable with the `moderate` virtual player
(headless, `check:play`) and with synthetic key presses in live play
(`qa --play`, the e2e play spec, `bench --play`). What it feels like under
real hands is still a human gate.

### Feel

| Item | State | Evidence |
|---|---|---|
| A press steps at once | verified | `qa --play`: 8 of 8 presses on the beat moved the hero in the first frame drawn after the key event. A key event's song time comes from its timestamp; the runner applies it at that time (rolling back the speculative 200 ms ahead) before the frame is drawn. **Human:** no perceptible delay. |
| Impacts are on the beat, however sloppy the presses | verified | `qa --play`: every live swing within 1 ms of a grid time with its impact sound on the strike. `check:play`: "impacts on the grid" for the moderate and casual players on four seeds. Unit test: live play records exactly what a batch run of the same keys records. **Human:** listen. |
| Mashing moves the hero once per beat, the key display greys the extra presses | verified | `qa --play` and the e2e play spec: three presses for one beat step once and two are ignored; unit test: the ignored press is not rated, does not touch the groove and greys the key. |
| Diagonals: Up and Right together step 44 px diagonally | verified | `qa --play`: the second key bends the step; unit test: one straight and one diagonal step cover 44 px each. |
| Holding a key does nothing after the first step | verified | `qa --play`: a key held 0.4 s (with repeats) records one press. |
| Frame time in live play | verified | `bench -- --play` (theme A to the climax, a late press on every beat, 212 rollbacks): JavaScript p95 3.6 ms, max 11.2 ms; GPU p95 5.7 ms; no frame over 33 ms. `qa --play`: 60 fps, JavaScript p95 4.1 ms. |

### The set pieces, played

| Item | State | Evidence |
|---|---|---|
| Prelude: held keys walk across the open field; the sword swings by itself | verified | Same rules as the demo (free movement); the open field has no walls (unit test: no route; the hero roams). **Human:** play it. |
| Count-in, then the first transform wave on the galop's downbeat | verified, agent review | The play file starts the galop in the woods, so the first wave sweeps out at 43.21 s (`check:play` "zone waves": every wave on a downbeat and off screen within a bar). Capture reviewed by the agent: the green woods fill the ring as it passes. |
| Galop opening: gold "press here" rings, a Great press cleaves | verified, agent review | Unit tests: the cleaves' accents show as large gold ticks with a gold ring on the beat before, and the heart glows over that beat (play mode only; the demo's HUD is unchanged). `check:play`: at least 80% of cleave accents cleaved by the moderate player. |
| Theme A: a step and a swing on every beat; the crowd comes mostly from ahead | verified | 60% of spawns from the half ahead of the hero's heading (same spawn director as the demo); "off-screen spawns" and "population" pass in `check:play`. |
| Kick line: stays on screen, dances, charges; the release cleave ripples through | verified | `check:play`: every skeleton down within two beats of the release on four seeds; the camera leads toward the line while it is in play. **Human:** does it read? |
| Archers: telegraphed; a sidestep earns a near miss | verified | `check:play`: 75% or more near misses (14–16 of 14–16 for the moderate player). |
| Boss: standing stones rise around the hero; slams can be stepped off; the boss dies on its chord | verified | `check:play`: the boss dies on its chord on all four seeds. Unit test: the standing stones are stamped on the hero at the climax's first downbeat; the renderer raises them over a bar. **Human:** watch them rise. |
| Finale: the shrine rises, the blast clears the screen, the end card shows a grade | verified | `check:play` "the finale" (moderate and casual) and "clean ending"; `qa --play` capture `play-end-card.png`. Grades: moderate A on all four seeds (average groove 0.96–0.97), casual B (0.71–0.80); thresholds S 0.972, A 0.9, B 0.7. |

### Flow

| Item | State | Evidence |
|---|---|---|
| Esc pauses and resumes with a count-in | verified | `qa --play` and the e2e play spec: song time stops while paused; the music replays the bar before the pause point and the game resumes exactly there. |
| Restart section returns to the section's first bar with the same world | verified | Unit test: a section restart 20 s back rewinds the recording and replays identically (Watch my run equals the live run afterwards). **Human:** try it. |
| Switching tabs pauses | verified | `qa --play`: a window blur pauses; a hidden page pauses too (the same handler). **Human:** a real tab switch. |
| A mid-song start in a believable world | verified | `qa --play` at `?start=1:50`: 430 monsters on screen just after the hand-over (capture `play-start-1-50.png`). The song is 2:24 long, so the design's `2:30` example is past its end. |
| Calibrate with a known offset, and Perfect ratings become reachable | partly verified | Unit tests: the offset is the median tap error after the first two taps, ignoring taps far from any click. **Human:** calibrate with Bluetooth headphones. |
| Watch my run replays the run identically | verified | `qa --play`: the replay's recording equals the live run's, part by part; unit test likewise with a mid-song start and a restarted section. |
| Watch the demo plays the unchanged scripted demo | verified | Golden plan hashes for every seed and demo profile, in `check`. |

### Tuning

- **Experience:** 360, 850 and 1700 for the three level-ups. The moderate
  player collects about 400–450, 950–990 and 1900–2200 by each scheduled
  moment, so every bar fills a little before its level-up on all four seeds.
- **Boss health:** unchanged: the moderate player wears it down to its floor
  before the death chord on every seed.
- **Timing windows:** 45, 90 and 140 ms as the design started them.
- **Grades:** the design's starting thresholds gave every bot an S (play mode's
  windows make most of the moderate player's presses Perfect), so they are
  S 0.972, A 0.9, B 0.7. The grade is the first thing to retune after real
  play: the moderate player sits within 0.01 of an S.

## What a person still has to do

The README's *Human review gates* list these steps:

1. Listen to the analysis click track for the song map.
2. Watch `?metronome` from the first beat to the last.
3. Do the first assembled check at `?start=1:10`.
4. Compare `?player=skilled`, `?player=casual` and `?player=perfect`.
5. Listen through the whole level.
6. Do one full playthrough with this table at hand.
7. Play mode: watch `?mode=play&player=moderate` from the start, at
   `?start=1:10` and at the climax; play theme A by hand at
   `?mode=play&start=1:10` with headphones; then the play-mode playthrough
   above, including Calibrate with Bluetooth headphones and a real tab switch.
