# QA report: Playlist Survivors

Status of every item in the *Manual QA* section of
[`docs/design/gameplay-redesign.md`](../design/gameplay-redesign.md#manual-qa)
(the shipped rules), and of the flow items of
[`docs/design/interactive.md`](../design/interactive.md#flow) that still apply,
as of 2026-10-22. The reports for the rules the redesign replaced (the step
gameplay of [`beat-steps.md`](../design/beat-steps.md) and the first play-mode
rules of `interactive.md`) are kept under [History](#history).

Each item is either **verified** by a command, **partly verified** (a command
covers the measurable part and a person still has to judge the feel), or a
**human gate** that nothing automated can close. Items marked *agent review*
were judged by eye from captures by the implementing agent; a person should
still confirm them.

To reproduce:

```bash
npm run check:all   # lint, types, unit tests (golden demo hashes among them), content, assets,
                    # songmap, analysis, check:seeds, check:play and the e2e tests
npm run check:play  # play mode, headless: every virtual player through the open field, the
                    # signature moments, the groove and the difficulty curve
npm run qa          # real-time playback in Chromium, the skilled and casual players' sounds on
                    # the grid, a measured listening pass, then live play with synthetic key
                    # presses; writes captures/qa/report.md and review frames
npm run qa -- --play          # only the live-play items
npm run bench -- --play       # live play from theme A to the climax, a press on every beat
```

`captures/` is not committed. `npm run qa` regenerates `captures/qa/`.

## Summary

| Area | State |
|---|---|
| Strides: the same 140 px/s holding, tapping quarter or eighth notes and mashing | verified (`qa --play`, unit tests, `check:play` speed cap and holding checks) |
| Stomps, rock steps, diagonals, a press ending a hit-stop | verified |
| The groove: press classes, drain, tiers and their hysteresis; mashers and holders at tier 0 | verified (unit tests, `check:play` groove checks) |
| The self-firing sword and weapons, sized by the tier; every sound on its grid slot | verified (every row of the Sound on the Grid table, every player) |
| Accents and cleaves, set pieces, gems and vacuums, chickens, level-ups and upgrade orbs | verified by unit tests and `check:play`; playing them is a human gate |
| The difficulty curve: holders and mashers fall in the kick line, bridge or climax | verified within 1:30–2:10 (the design's 1:45–2:05 is missed on some seeds) |
| The difficulty curve: honest players' end health | **off target**, reported not gated: every honest bot ends at 90–100% on average (the design's note on the curve records why) |
| Watch my run after level-ups; Watch the demo on the new rules | verified |
| Audio clock, picture/audio offset, hidden tab, resize, cached plan, broken level | verified |
| Sound mix: clipping, effects level, music in front, no wall of noise, effects on time | verified by measurement; listening still needed |
| Feel with real hands and ears: every item below marked **Human** | **human gates, not yet run** |

## First assembled check

`?mode=play&start=1:10&debug=moves`, two bars of each.

| Item | State | Evidence |
|---|---|---|
| Hold Right: a stride on every beat, about 45 px each, about 140 px/s; the groove drains after a bar | verified | `qa --play`: a key held three beats records one press and covers 47.2, 43.4 and 44.8 px from beat to beat (one stride 44.8 px); 128 px/s over two seconds of holding. Unit tests: holding covers 140 px/s; the groove drains a bar after the last raising press; the `?debug=moves` overlay reads the walk speed while holding. **Human:** read the overlay. |
| Tap quarter notes: the same speed, a footfall on every tap; the groove rises and the tier steps up within about ten beats | verified | `qa --play`: 140 px/s. Unit test: Perfect presses on every beat reach tier 1 after 6 beats, 2 after 8 and 3 after 11. |
| Tap eighth notes: the same speed with twice as many shorter strides; off-beats outlined; the groove rises | verified | `qa --play`: 131 px/s. Unit tests: 140 px/s tapping eighth notes; an off-beat after a Great or better press is outlined on the key display. `check:play`: the eighths player's average groove is within 0.05 of the skilled player's. (The tuned meter gives off-beats nothing, so the groove rises from the counted presses alone.) |
| Mash: the same speed, a tiny stride on every press; stray presses red; the groove falls to tier 0 | verified | `qa --play`: 140 px/s mashing; three presses for one beat are one rated and two extra, each striding. Unit tests: stray presses take a chunk off the groove and show red. `check:play`: the mashers are at tier 0 for 96–100% of pulsed time. |
| Stomp with Space on the beat: the hero doesn't move; the groove rises | verified | Unit tests: a Space stomp stamps in place and is a rated press, and stomps on the beat raise the groove like any press. `qa --play`: Space on every beat while holding Right moves the hero 0.00 px. |
| Throughout: the sword swings on every beat at the tier's size, every impact on the beat | verified | `qa --play`: 15 live swings, none off the grid, each with its impact sound. `check:play`: "the sword swings on every grid time" and "impacts on the grid" for every player on four seeds. Unit tests: the sword, blades, lightning and trail sized at tiers 0 to 3. **Human:** listen. |

## Full playthroughs

| Item | State | Evidence |
|---|---|---|
| An honest run: about half health at the end, tier 2 or 3 through most of the galop | partly verified, **off target** | `check:play`: the moderate player is at tier 2 or higher for 100% of pulsed time after the galop vamp on all four seeds, grade A (average groove 0.90–0.93), lowest health 50–80%, but it ends at 100% (target 40–70%): heals cancel its damage. The casual player ends at 90–100% (target 10–40%). The design now judges this on the average over the checked seeds and reports it: nearly all honest damage is the minotaur's, healed back by its chickens and the bridge's level-up (hv-dtz9). **Human:** play it. |
| A mashing run: survive theme A, lose health in the kick line and bridge, fall in the climax | verified | `check:play`: the mashers at 2, 4 and 8 presses a second fall at 1:35–2:05 on every seed (the check allows 1:30–2:10; on seed 7 the masher at 4 falls at 1:35, in the kick line, and on seed 2024 at 2:05). **Human:** mash a run. |
| A holding run: the same fate | verified | `check:play`: the holder falls at 1:37 (seed 7) and 1:54 (seed 1858). **Human:** hold through a run. |
| Watch the bots against the difficulty curve | verified | `check:play` prints every bot's kills, groove, grade, time at tier, lowest health and end against its target (above). The e2e tests plan and play back the holder and a masher. **Human:** watch `?mode=play&player=moderate`, `casual`, `holder`, `mash4` and `eighths`. |

## Set pieces and pickups

| Item | State | Evidence |
|---|---|---|
| The ring at 1:02.3: out through a gap; or in the middle, a press on the chord clears it and a vacuum drops on the next downbeat | partly verified | Unit tests: the ring spawns 30 evenly spaced just outside the view with three gaps of three places and closes to 100 px on its accent; a Great press 50 ms late on the chord launches the wave on the "and"; the vacuum drops on the first downbeat after the first ring's accent, ahead of the hero. The virtual players wait in the middle from tier 2 and leave through a gap below it. **Human:** play both ways. |
| The stampede at 1:42: sidestep through a gap | partly verified | Unit tests: it runs straight across the view at 2.5 times its enemies' speed, ignoring the hero, and the runners that leave the far side are recycled; the virtual players dodge its runners. **Human:** sidestep it. |
| Gems: chimes climb the scale while striding into a field, and start again after a beat; at tier 3 the pull reaches further | verified | Unit tests: chimes climb through consecutive sixteenths, start again after a beat without pickups, and are pitched in the section's key; the pickup radius is 40 to 120 px by tier, drawn as a ring when it changes. `check:play`: the moderate player collects at least 60% of the experience dropped. **Human:** listen. |
| Chicken: kill an orc elite in theme A's repeat and walk over its chicken | partly verified | Unit tests: half the orc elites (seeded) and the boss (two) drop chickens where they die; touching one heals 30 on the first sixteenth at least 60 ms later, with its sound; the virtual players walk to one below 60% health. **Human:** eat one. |
| Level-ups: walk into an orb at 0:54.1; take none at 1:20.2 and the one that rose ahead is taken after two bars; too little experience before the bridge, and its level-up comes on the first downbeat after the bar fills | partly verified | Unit tests follow the design's trace: the level-up's downbeat pushes enemies out and raises three orbs; one walked into within 24 px applies on the next grid time with its sound and the others sink; left two bars, the orb ahead is taken. `check:play`: the moderate player walks into an orb at every level-up, on time, on all four seeds. `qa --play`: the bridge's level-up in live play, then an identical Watch my run. The late level-up has no automated check (every bot fills each bar before its moment). **Human:** all three. |

## Edge cases

| Item | State | Evidence |
|---|---|---|
| A late press on a chord: the impact on the chord, the wave on the "and"; in the coda, more than 70 ms late, a sixteenth later; no sound late | verified | Unit tests: the galop-opening trace (a Great press 50 ms late launches the wave on the "and"); in the coda, a press too late for the "and" starts the wave a sixteenth later; a Good press gets the impact without a wave. `check:play` "impacts on the grid": no sound off its slot. `qa`: 2246 sound events heard within 0.1 ms of their scheduled times. **Human:** press late on purpose. |
| An early press on a chord brings a brief hit-stop; a late one doesn't | verified | Unit test: a hit-stop when the press comes before the impact, none after. |
| A press during a hit-stop ends it and strides at once | verified | Unit test: the press ends the hit-stop and strides at once. `check:play` "hit-stops": a stop shorter than 40 ms only when a press ended it. |
| A diagonal: two perpendicular keys within 40 ms, one diagonal stride of the same length | verified | `qa --play`: Up and Right together moved 31.1, −31.1 (44.0 px, the press's stride 44.0 px). Unit test likewise. |
| Opposite keys together: a rock step out and back, one stomp | verified | `qa --play`: Left then Right 25 ms later: "left perfect, right (rock)", the hero back where it began (0.00 px). Unit tests likewise, timed by the first key. |
| Space while holding a direction skips the next held stride; holding and stomping on every beat never moves the hero | verified | `qa --play`: 4 stomps over four beats with Right held, the hero moved 0.00 px. Unit tests: one held stride skipped (two where the stride period halves), a little early or late too. |
| The tier threshold doesn't flicker | verified | Unit test: tiers rise at 0.45, 0.60 and 0.85 and drop only 0.05 below one. **Human:** hover around 0.6. |
| YOU FELL: restart the section; health and the world are restored | partly verified | Unit test: a restarted section rewinds to its snapshot (health and world included) and replays exactly as played. **Human:** fall and restart. |
| Watch my run after a run with level-ups: identical, offers included | verified | `qa --play`: a run from 1:37 through the bridge's level-up (three offers, one taken) to the end replays identically, part by part. Unit test: a live run across two level-ups and its replay agree on offers, picks and orbs. |
| Watch the demo: the skilled bot plays the new rules in the open field | verified | The e2e demo spec; the golden demo hashes (`check`). |
| Calibration with a known offset makes Perfect reachable | partly verified | Unit tests: the offset is the median tap error after the first two taps, ignoring taps that don't aim at a click; it is kept across the redesign. **Human:** calibrate with Bluetooth headphones. |
| The play log: F8, then `npm run log:play` shows press classes, time at each tier and bites per section | verified | Unit tests: the log follows each key from the keyboard through the runner to the simulation's outcome, adds up presses by class and time at each tier per pulsed section, and carries every hurt and heal. **Human:** press F8 after a run and read the summary. |

## Flow (interactive.md)

The flow items of [interactive.md](../design/interactive.md#flow), which the
redesign kept.

| Item | State | Evidence |
|---|---|---|
| Esc pauses and resumes with a count-in | verified | `qa --play` and the e2e play spec: song time stops while paused; the music replays the bar before the pause point and the game resumes exactly there. |
| Restart section returns to the section's first bar with the same world | verified | Unit test: a section restart 20 s back rewinds the recording and replays identically (Watch my run equals the live run afterwards). **Human:** try it. |
| Switching tabs pauses | verified | `qa --play`: a window blur pauses; a hidden page pauses too (the same handler). **Human:** a real tab switch. |
| A mid-song start in a believable world | verified | `qa --play` at `?start=1:50`: 289–352 monsters on screen just after the hand-over (capture `play-start-1-50.png`). The song is 2:24 long, so the design's `2:30` example is past its end. |
| Calibrate with a known offset, and Perfect ratings become reachable | partly verified | Unit tests: the offset is the median tap error after the first two taps, ignoring taps far from any click. **Human:** calibrate with Bluetooth headphones. |
| Watch my run replays the run identically | verified | `qa --play`: a run from 1:37 through the bridge's level-up to the end replays identically, part by part; unit tests likewise with a mid-song start, a restarted section, and level-ups with their offers and orbs. |
| Watch the demo plays the skilled player under the same rules | verified | The e2e demo spec: the plan is the skilled player's, in the open field, with tiers and upgrade orbs. Golden plan hashes for every seed and demo profile, in `check`, guard it. |

## Sound

`qa`'s listening pass renders the whole mix offline through the live mixing
graph (2026-10-22):

- No samples clip (peak −0.8 dBFS).
- The effects' 95th percentile sits 5.4 dB under the music's peak, and the
  music is in front in every section, by 0.9 dB (the kick line) to 22.6 dB
  (the prelude).
- The mix keeps the music's own rise and fall between hits in every section
  (prelude 21.0 → 20.4 dB, climax 3.8 → 4.1 dB), so the effects do not fill it
  into a wall.
- All 2246 effects are heard within 0.1 ms of their scheduled time.
- Check "sound budget": at most 4 voices start per sixteenth note.
- The finale's blast clears 207 living monsters within a beat of the final
  chord.

**Human:** listen through, especially the cleaves' shockwaves, the gem chimes
and the level-up fanfares, which are meant to sit under the music.

## Performance

`npm run bench` played the whole song at real-time speed in headed Chrome on
this machine, 17,343 frames on a 120 Hz display:

| Measure | Median | p95 | Max | Budget |
|---|---|---|---|---|
| JavaScript frame time | 1.80 ms | 3.00 ms | 5.70 ms | 8 ms at p95 |
| GPU frame time | 0.76 ms | 1.71 ms | 8.39 ms | 10 ms at p95 |
| Frame interval | 8.30 ms | 9.30 ms | 9.50 ms | 33 ms max |

No frame was dropped (measured 2026-10-21, before the redesign's last tuning). `qa --play` (2026-10-22): 60 fps in live
play, JavaScript frame time p95 3.1–3.8 ms. The simulation runs once at load: about 1.15 s in the browser worker
including its checks, then the plan cache makes reloads instant.

## Tuning

- **Experience:** 100, 470 and 800 for the three level-ups (0:54.1, 1:20.2 and
  1:40.7). The moderate player walks into an orb at each, on time, on all four
  seeds.
- **Grades:** S from an average groove of 0.94, A 0.85, B 0.5. On the checked
  seeds the perfect, skilled and eighths players earn an S, the moderate player
  an A (0.90–0.93), the casual player a B (0.67–0.83), and the holder and
  mashers a C (0.03–0.16). The best grade is stored under a new key.
- **Timing windows:** 45, 90 and 140 ms.
- **The curve:** the honest players' end health is off target (above); it is
  reported on its average over the seeds, and reaching the bands needs a change
  to the design's healing or damage.

## What a person still has to do

The README's *Human review gates* list these steps:

1. Listen to the analysis click track for the song map.
2. Watch `?metronome` from the first beat to the last.
3. Do the first assembled check at `?mode=play&start=1:10&debug=moves`.
4. Play the three full playthroughs (honest, mashing, holding) and watch the bots.
5. Play the set pieces and pickups, and the edge cases marked **Human** above,
   including Calibrate with Bluetooth headphones, a real tab switch and the play
   log.
6. Listen through the whole level.

## History

The reports below describe rules the gameplay redesign replaced: the tech
demo's step gameplay in the forest corridor ([beat-steps.md](../design/beat-steps.md))
and play mode's first rules ([interactive.md](../design/interactive.md)). They
are kept as they were written (2026-10-21); their numbers do not describe the
game today.

### Problems found and fixed while building the simulation

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

### First assembled check

| Item | State | Evidence |
|---|---|---|
| Open `?start=1:10` | verified | `qa`: playback starts on click with `?start`; captures `theme-a-75.20.png` and `theme-a-75.30.png`. |
| The hero stands still between steps | verified | `qa`: the hero moves 0.000 px between 75.20 s and 75.30 s. Unit test: still inside 20 beat gaps of theme A. Check "every hero movement has an input": step movement only within 110 ms of a press. |
| Each step is quick and firm | partly verified | The step is a 110 ms cubic ease-out with a 5 px hop and a dust puff on landing; check "every hero movement has an input" bounds every step to 44 px. **Human:** does it feel firm? |
| Swings go toward the crowd | verified, agent review | Check "auto-aim is the rule": every swing's direction is re-derived from its recorded aim snapshot, and the snapshot matches the recorded hero and enemies. Captures show swings into the thick of the crowd. **Human:** confirm. |
| Enemies chase and surround | partly verified, agent review | Enemies steer at the hero and stop at contact; check "population" keeps them on the level's curve and "spacing" keeps them apart. **Human:** does it read as a horde closing in? |
| Every sword impact on the beat | verified | Check "impacts on the grid": every strike within 1 ms of a grid time and its impact sound exactly on it, for every profile. `qa` listening pass: 2240 sound events heard within 0.1 ms of their scheduled times. **Human:** listen. |

### Full playthrough (`?player=skilled`)

| Item | State | Evidence |
|---|---|---|
| Prelude | partly verified | Unit test: the hero walks with held keys, swings by itself every two beats and cleaves on strong anchors; presses there are not rated. Check "no skating": the walk cycle advances with the distance walked. **Human:** does it feel calm? |
| Count-in | verified, agent review | The beat track fades in and four count marks pulse on the last four beats of the rubato passage, before the galop vamp's first downbeat at 43.21 s. |
| Galop vamp | partly verified | The quiet vamp (bars 15–30) is a pulsed section: one step and one swing per bar at full speed, at the galop's tempo. **Human:** does the first beat feel like it arrives at 0:43? |
| Galop opening | verified | Presses once per bar; cleaves with hit-stops on the tutti chords (check "hit-stops": 34 stops within 40–150 ms). Capture `first-cleave.png`. |
| Theme A | verified | A step and a swing on every beat; the groove sits at +20% (an orc's 1.15 health falls to one 1.2 swing). |
| Kick line | verified | Check "signature moments": every dancer dies within two beats of the release, most to the release cleave's shockwave. Capture `kick-line-release.png`. **Human:** does the ripple read? |
| Archers | verified | Check "signature moments": at least 90% of threats are near misses (all of them for the skilled player on the four CI seeds). Capture `archer-near-miss.png`. |
| Boss | verified | Slam markers drop under the hero, which steps off them (capture `boss-slam-dodged.png`). With 175 health and no floor, the skilled player kills it at 134–138 s on the four CI seeds; a casual player leaves it to the finale blast (capture `boss-death.png`). |
| Finale | verified | `qa`: 458 living monsters just before the final chord and 0 one beat later; check "clean ending". The end card shows kills, rating counts and the average groove. |
| Key display and groove | verified, agent review | Captures show the keys lit with their rating and the groove meter at +20% through the galop. |

### The reward spectrum

| Item | State | Evidence |
|---|---|---|
| `?player=casual` is calmer | verified | One press per bar (207 presses against the skilled 267). Average groove about +13% (12–14% over four seeds) against +20%; 7600–8000 kills against 8700–9000. Orcs often need two swings below a 0.75 groove. The design's +5–10% would need a far less generous accuracy table: see *Tuning Notes* in the design. |
| Casual impacts stay on the beat | verified | `qa`: 196 casual swings, 0 off the grid, although many presses land 70–250 ms off the beat. Unit test likewise. |
| The demo still progresses | verified | The casual run passes every check, reaches the finale and is cleared by the blast; its level-ups arrive one to four seconds after their scheduled moments. |
| Nothing punishes the player | verified | There is no miss rating, no hero health loss (arrows and slams that touch the hero do no damage) and no combo. |
| `?player=perfect` | verified | `qa`: every press Perfect, groove +20%. |

### Edge cases

| Item | State | Evidence |
|---|---|---|
| A press during a hit-stop | verified | The simulation ends a hit-stop at a press (check "hit-stops" accepts stops shorter than 40 ms only when a press ended them). **Human:** find one by eye. |
| A diagonal step | verified | Unit test: a perpendicular key within 40 ms bends the step smoothly onto the diagonal, landing 44 px away. The virtual player presses diagonals as two keys a few milliseconds apart. |
| The corridor edge | retired | The forest corridor is gone (hv-zoon): the open field has no edge. |
| Other seeds | verified | `check:seeds` (1859, 7, 2024) passes every check; seeds 1, 2, 3, 42, 99 and 500 also pass. |
| Determinism | verified | `check:seeds`: two runs of the level's seed give the same plan hash. e2e: a reload loads the cached plan with the same hash. |
| Hidden tab | verified | `qa` stalls the page's main thread for 3 s, which stops frames and timers as a hidden tab does, while the audio plays on. On return the picture jumps to the right moment (song time moved 3.0 s over 3.0 s, the picture within 30 ms of the audio), and the sounds that fell behind are skipped, not started late. A real tab switch in Google Chrome was dropped from `qa`: it was slow (it played to the climax in real time) and flaky (Chrome often never hid the page). A real tab switch is still worth trying by hand. |
| Window resize, cached plan, broken level script | verified | As before: `qa` resize letterboxing, e2e cached plan and broken level (13 broken fixtures in `check:content`). |

### Play mode

The Manual QA of [interactive.md](../design/interactive.md#manual-qa). The
commands judge everything measurable with the `moderate` virtual player
(headless, `check:play`) and with synthetic key presses in live play
(`qa --play`, the e2e play spec, `bench --play`). What it feels like under
real hands is still a human gate.

#### Feel

| Item | State | Evidence |
|---|---|---|
| A press steps at once | verified | `qa --play`: 8 of 8 presses on the beat moved the hero in the first frame drawn after the key event. A key event's song time comes from its timestamp; the runner applies it at that time (rolling back the speculative 200 ms ahead) before the frame is drawn. **Human:** no perceptible delay. |
| Impacts are on the beat, however sloppy the presses | verified | `qa --play`: every live swing within 1 ms of a grid time with its impact sound on the strike. `check:play`: "impacts on the grid" for the moderate and casual players on four seeds. Unit test: live play records exactly what a batch run of the same keys records. **Human:** listen. |
| Mashing moves the hero once per beat, the key display greys the extra presses | verified | `qa --play` and the e2e play spec: three presses for one beat step once and two are ignored; unit test: the ignored press is not rated, does not touch the groove and greys the key. |
| Diagonals: Up and Right together step 44 px diagonally | verified | `qa --play`: the second key bends the step; unit test: one straight and one diagonal step cover 44 px each. |
| Holding a key does nothing after the first step | verified | `qa --play`: a key held 0.4 s (with repeats) records one press. |
| Frame time in live play | verified | `bench -- --play` (theme A to the climax, a late press on every beat, 212 rollbacks): JavaScript p95 3.6 ms, max 11.2 ms; GPU p95 5.7 ms; no frame over 33 ms. `qa --play`: 60 fps, JavaScript p95 4.1 ms. |

#### The set pieces, played

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
