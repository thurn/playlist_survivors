# SH6 — early enemy rhythm and contact attacks

Base: `da2561c49c590f47cae0e722c9560a39c35686d1` (SF7). Delivery SHA is in
`hv-n7fn.17`'s native notes. No per-bead cold review: SH11 reviews milestone H;
this change adds simulation state and LiveView records, not rollback-runner logic.

## Behavior and choices

- BAT1–3 flutter on eighths, ZOMBIE/slime hops on beats, GHOST/bee darts on
  sixteenths, SKELETON marches on beats. The schema accepts sixteenth gaits.
  Walk frames follow each role's beat cycle; frozen walk poses and positions
  hold, while bob continues in place. The shared downbeat squash remains.
- Nearby enemies wind up halfway before a known beat, leaning back four pixels,
  then lunging twelve pixels at its peak and returning over a quarter beat.
  These distances are animation/reach choices; VS speed, HP, power, armor,
  chicken, healing and offer values are unchanged. Both wind-up admission and
  peak contact use actual distance. The peak consumes one attempt per enemy,
  including misses and attempts blocked by hero invulnerability. Sword damage
  at that stop resolves before contact attacks. Free pulses use the same rule;
  a newer committed grid invalidates the old free-pulse generation.
- A late known beat can shorten notice, never create a past wind-up. A menu
  drops attack cadence and starts fresh notice on the next offbeat, avoiding
  catch-up or immediate resume damage. The active pose holds with world time.
- Logical damage, death, XP and reports remain immediate. Visible hit flashes,
  death frames and kind-specific bursts arrive on the next known sixteenth,
  or a quarter free pulse without a bounded grid. Recoil uses that same grid.
  Finite reactions survive freezes and resnap on resume, including beatless
  resumes. The queued reaction owns its Hit, avoiding eviction from the live
  recorder's short hit cache. Pending death actors survive view pruning until
  that reaction; logical alive and kill counts are already correct.
- `Moment::Hit` and `Moment::Death` keep their original report meaning. The
  additive `Moment::EnemyImpact { hit }` is presentation only. SF8 has this
  interface in its bead notes; counting both would double count hits.
- Fixed an opening motion edge found by the new regressions: before two beats
  are known, the old beat modulation could produce NaN positions. Survivors
  now glides freely until a bounded interval exists. Six early roles are
  checked for finite positions through startup and gait lock.

## Kept checks

- `contact-tests.log`: four regressions for notice, grid peaks, no repeat bite,
  leaving reach, freeze/resume, free-pulse invalidation, sixteenth reaction
  selection and poisoning the uncommitted stream tail.
- `gait-tests.log`: six role/cycle contracts and actual finite simulation with
  recorded positive-duration gait steps.
- `live-tests.log`: all 14 Live tests pass, including 69,420 matching settled
  records at 300-enemy density across an off-grid pause; nine actual XP offer
  picks across nine rollbacks (deepest 420 ms); pending death presentation does
  not count another kill. This exercises the existing rollback runner unchanged.
- `freeze-tests.log`: all 15 Survivors freeze tests pass.
- `legacy-causality.log`: six gate tests pass (one release-only ignored),
  including eight Galop cut runs with zero failing; `legacy-session-tests.log`:
  three original session fixtures still settle exactly.
- `check-fast.log`: passed in 83 s (native 80 s), over the nominal 60 s local
  budget; no failing check. No source changes followed it.

## Minute-two horde capture

`minute-two.png` is an offscreen render, inspected without opening a window.
`still.log`: song 160.000 s, run 157.414 s, schedule minute 2, level 3,
425 scheduled admissions, 282 kills, 143 alive, 2,150 sprites and 7 lights.
**Damage was exempt for this presentation capture; it is not survival evidence.**
The `--pick-first` and `--spawn-smoke` flags now also apply to offscreen stills.
The normal live app's source and input loop are unchanged.

The temporary still input came from Rust's kept SF6 File/Delayed analyzer
session of repeated public house-128 audio, not extracted VS data. It is not
committed. Regenerate it with:

```sh
python3 native/evidence/sh6/capture-fixture.py \
  /Users/dthurn/playlist_survivors/native/logs/sessions/1793001281/stream.jsonl \
  public/reactive/sh6-headless-check/stream.json
/usr/sbin/taskpolicy -b native/target/release/playlist-survivors \
  --headless --spawn-smoke --pick-first --still native/evidence/sh6/minute-two.png \
  --still-track sh6-headless-check --still-at 160
```

The converter applies recorded arrival stamps and drops startup late beats.
The still file format uses tick time for tick knowledge, so this conversion is
only for presentation; the actual session replay below preserves all stamps.
Remove the temporary `public/reactive/sh6-headless-check/` directory afterwards.

## Normal-damage playable run and replay

```sh
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions \
  /usr/sbin/taskpolicy -b native/target/release/playlist-survivors \
  --file /tmp/sf6-five-minutes.wav --headless --delayed --press-beats \
  --pick-first --seconds 65
/usr/sbin/taskpolicy -b native/target/release/ps replay \
  /Users/dthurn/playlist_survivors/native/logs/sessions/1793026073
native/target/release/ps report \
  /Users/dthurn/playlist_survivors/native/logs/sessions/1793026073
```

`/tmp/sf6-five-minutes.wav` repeats the public house-128 audio; the run uses
normal damage (`noHeroDamage: false`). Kept outputs: `headless.log`,
`session-summary.json`, `replay.log`, `timing-report.log`. The stopped run spans
58.952 song seconds, including the offer freeze; 55 kills, final HP 1.00.
All **79,862** settled records match replay (81,795 records including its tail),
hash `f1b46d8c777a2af3`. 282 sounds started, zero late or dropped.
Frame p99 1.8 ms, rollback p95 1.6 ms/s. Capture-to-arrival p95 272.1 ms is over
its report target under background QoS; this is kept as a report, not a pacing
or survival gate. Neither this short actor nor the damage-exempt still is a
three-minute or full-run survival verdict. Feel/listening remains operator-pending.
