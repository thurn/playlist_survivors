# SH7 — card screen presentation

Base `5b75fb48b1f77c25a1e27bd5137caa936b067f9a`. The sim’s offer table, reveal times, confirmation gate and next-beat resume are unchanged. This bead consumes the published RunState and inventory; no sim/eval implementation, game stats or asset bake changes.

## Presentation and interface

Cards use the Kenney pixel face, neon item icons, NEW!/rank, the source card’s one-line change and a catalogue-derived **EVO PAIR** mark. The mark means the weapon and its partner passive are owned after choosing the card, not that the max-rank/chest evolution conditions have all been met. Arrow highlighting pulses the selected card; confirmation punches it and drops the others. The world dims/desaturates, while the frozen hero/enemy gait keeps bobbing. The HP bar is hidden while cards cover the hero and restored afterward.

The 200 ms flips **finish** on each sim-owned reveal beat. The third card’s front is fully readable when a confirm can count. Chained offers keep the world dim. A retracted speculative offer closes without a pick animation; a retracted pick cancels its confirmation and reopens the offer. End/stop-replay clears the presenter.

SH2’s scheduler accepts additional presentation cues in the same desired-voice reconciliation and UI cap. Pending reveal cues can be prepared from `Sim::run_state()` at its causal speculation frontier; they use only the stream/keys already fed. Retractions cancel pending cues; same-level retiming replaces old cues, and started voices do not repeat. The actual selected sample’s onset is compensated to the reveal time. Confirm/highlight cannot be known before the key and get 100 ms response lead. The palette selects already baked card-placement, select, confirmation and digital-highUp samples. Musical fit remains an operator audition.

## Kept offscreen capture

Run from repository root after building app in `native/`:

```sh
native/target/release/playlist-survivors --headless --cards-check native/evidence/sh7
```

This diagnostic runs under `taskpolicy -b`, never opens a window or audio device, and writes `open.png`, `confirm.png`, `resumed.png` and a silent `open-pick-resume.mp4`. It uses an explicitly labelled **presentation fixture**: one quick bat (health 0.0001, speed 1000, XP 10), twelve durable background zombies (health 1,000,000), and one minute-zero roster with a 60 s tick, all modified only in memory. Hero damage remains normal. These values make a single pickup repeatable and say nothing about gameplay pacing, difficulty or survival. Art and the actual offer/input/freeze/resume paths are unchanged.

The capture contains 66 frames at 30 fps, 1280×720. `sequence.jsonl` and `capture.txt` record open at song 1.666667, reveals [1.9345, 2.417, 2.767], confirm key 3.025 and observed resume 3.325. The run clock stays 1.658875 through the open and confirm stills; it advances after resume. Actual capture trace and command output are retained. Visual inspection verifies complete icons/text/pair mark, the selected punch, no HP over the panels, and HP restored after the exit. This silent capture cannot establish audio timing by ear or input feel.

## Checks and handoff

`card-tests.txt` covers retraction, retracted picks, chains, catalogue pairs, forecast retiming and the third front face on the confirmation beat using actual baked icon regions. `cue-tests.txt` decodes the real palette and checks exact onset placement plus cancellation before start. `render-tests.txt` includes actual GPU readback against known red pixels with padded rows; still and sequence capture now share that readback path. Final focused/aggregate/headless/replay outputs are retained alongside these files.

No per-bead cold review is required by the survivors policy: the rollback runner is unchanged. SH11 owns the milestone review of this commit. SH10 can reuse the presenter/cue reconciliation pattern for its chest roulette, while preserving its own sim times. Operator feel/sound checks are in `native/evidence/operator-pending.md`.

Playable from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```
