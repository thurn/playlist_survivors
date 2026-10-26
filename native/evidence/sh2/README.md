# SH2 — survivors sound foundation

Base `fd8ee17c06c53a3e53048c1dfbd1ee9bb5397d54` (SH1). No asset bake: the survivors palette selects already baked samples. SF11 descriptions remain provisional until the operator auditions the mix. Weapon polish and evolution sets belong to their consuming beads.

## Interfaces

- `Sounds::load(..., survivors)` chooses the `survivors` palette only in that mode. Sword spin uses swing + light hit, accent uses heavy hit without extra game damage. Other weapon causes map `<weapon-id>-fire` / `<weapon-id>-hit`; the base palette supplies candidates for the four later weapons. SH3→SH4→SH5→SW1→SW2 own their final palettes and accent/evolution metadata. Existing legacy palette behavior remains available.
- Category caps: **weapons 4, hits 6, kills 2, pickups 3, UI 3**. Admission uses actual decoded sample duration and pitch rate, includes scheduled future overlap, and keeps already audible tails across a restart. Stable ordering makes the chosen subset independent of cause input order. Excess sounds are omitted; same-slot kills are one centred multi-kill transient.
- Pickups derive `Cause::Gem` from existing `Record::GemArrive`, with the same first-known and rollback removal machinery as other sounds. No extra journal record or hash byte is introduced. The scheduler replays speculative pitches from a settled prefix; a rollback can repitch an unstarted gem, while a started note stays untouched.
- Each pickup within **0.3 s** advances C6 D6 E6 G6 A6 C7 D7 E7 G7 A7 C8, then holds C8. A pause resets to C6. The measured G6 glass sample is transposed without random detuning; the palette validates this pitch basis. Dense simultaneous pickups advance the chain even when a voice is omitted by its cap.
- In survivors, the effects bus has a **−12 dB peak ceiling against the heard music’s RMS**. File, Delayed and replay measure their actual music samples with a 400 ms one-pole follower. Direct System uses the current causal 400 ms linear-energy capture window. Compression and the common output limiter remain; the music bus has no duck envelope in survivors, even if a voice is marked ducking. Synthesized calibration/reference clicks bypass this gameplay ceiling so they remain audible without music. Test output, rather than listening, establishes the ceiling and no-duck behavior.
- UI cues sent through `Sounds::now` share the cap. SH7 can use `level-up` and add card/confirm keys; SH8’s actual pickups automatically feed the chain. Later chest/evolution/victory palette entries remain their own beads.

## Kept checks and limits

`audio-tests.txt` includes actual mixer PCM tests for heard music and Direct System, silence, no duck and the ceiling, plus existing audio tests. `sound-tests.txt` covers actual palette decoding, caps at dense input, input-order stability, future overlap, slow sample tails, cancellation, restart tails, chain reset/top-note/rollback and future-note repitch. `live-tests.txt` ties pickup causes to actual GemArrive records and verifies they add no sound hash record. `session-tests.txt` and `preexisting-replay.txt` preserve prior recordings.

`--sound-check <session-directory>` runs the actual Engine, scheduler and mixer without a window or output device. It re-execs under `taskpolicy -b`. `--seconds` means run-clock seconds for this diagnostic (default 1510). It uses decoded samples at 8 kHz and synthetic 0.1 RMS music; it tests capacity, not musical fit. A normal-damage short-session smoke (`sound-smoke.txt`) starts 54 voices, skips/drops none, and respects every cap. The late-run check consumes the separately labelled SF8 damage-exempt runner soak, not a survival result. Its final output is `minute-25-budgets.txt`: minute 25 with 300 alive/peak enemies, 8,541 mixer starts, zero late skips, zero mixer drops and 18 cancellations. Actual category peaks were [4,3,2,3,0], with 51 excess kill transients denied. It exercises weapons/kills/pickups at their caps; the dense source-backed regression reaches all five caps [4,6,1,3,3] (kills coalesce to one slot there). This diagnostic is from the pre-review sample/fixed-RMS binary; the final short smoke and 18 audio/10 sound tests cover the confirmed mix fixes and final source. Final `scripts/check-fast` output is in `validation.txt`.

Reproduce from repository root after building app in `native/`:

```sh
mkdir -p /tmp/sh2-soak
cp native/evidence/sf8/recorded/soak/{meta.json,keys.jsonl} /tmp/sh2-soak/
gzip -dc native/evidence/sf8/recorded/stream.jsonl.gz > /tmp/sh2-soak/stream.jsonl
native/target/release/playlist-survivors --headless --mute --sound-check /tmp/sh2-soak
```

Normal playable command from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```

The operator-only gem-chain/mix audition is recorded in operator-pending. The pre-existing concurrent decoder temporary-file collision is filed as tooling `hv-n7fn.38`; this bead shares one decoded palette per test binary as required by AGENTS, and does not claim that decoder defect is repaired.
