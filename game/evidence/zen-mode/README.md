# The zen mode (ps-myz)

`--mode zen` ([the design](../../../docs/archive/design/game-modes.md#the-zen-mode)): the hero walks freely and the sword
strikes on the beat by itself, kept to the beat grid; the loudness scales its strikes and the horde reels to the
music. The question that matters most: does every slash land on the beat?

## How it was run

The harness, [Zen](Zen/Program.cs), drives a zen run over one entry dump as the sweeps drive a run (60 fps frames,
each entry arriving 60 ms after its stamp, the run's level in the game's arena, seed 1, the headless start), with the
starting sword, no cards (nobody answers them), a hero that never falls, walking a square (each arrow held 2 s in
turn). It writes every sword impact to `LABEL.csv` (kind, heavy, reach, the tick level, the beat's period and the
offset from the grid) and a summary to stdout. The grid is each committed beat, and each half beat where the beat's
period is over 0.75 s; a loud beat's follow-ups are checked against the quarter beats.

```sh
S=/tmp/zen   # any scratch folder
ffmpeg -i public/reactive/house-128/house-128.ogg -filter:a atempo=0.55 $S/house-70.wav   # 70 BPM
(cd native && cargo run --release -q -p cli -- entries ../public/reactive/house-128/house-128.ogg --out $S/house-128.jsonl)
(cd native && cargo run --release -q -p cli -- entries ../public/reactive/dnb-174/dnb-174.ogg --out $S/dnb-174.jsonl)
(cd native && cargo run --release -q -p cli -- entries $S/house-70.wav --out $S/house-70.jsonl </dev/null)
for s in house-70 house-128 dnb-174; do
  dotnet run --project game/evidence/zen-mode/Zen -c Release -- game/evidence/zen-mode $s $S/$s.jsonl
done
```

(`ps entries` decodes with afconvert, which hangs on an Ogg that ffmpeg wrote: give it a WAV.)

## What it shows

On the beat, on all three songs (2026-11-01, this change):

| Song | Tracked tempo | Slashes | On the grid | Slashes apart (median, 10-90%) | Loud follow-ups on the quarter beats |
|---|---|---|---|---|---|
| house-128 slowed to 70 BPM | 141 BPM (double time) | 250 | 250, offset 0 | 0.430 s (0.410-0.450) | 196 of 225, the rest within 30 ms |
| house-128 | 128 BPM | 125 | 125, offset 0 | 0.470 s (0.460-0.470) | 116 of 121, the rest within 5 ms |
| dnb-174 | 174 BPM, every other beat | 81 | 81, offset 0 | 0.690 s (0.680-0.700) | 68 of 79, the rest within 10 ms |

- **Every slash lands exactly on its beat**, and every beat is struck: the gaps are the beat's own (house-128's
  0.448-0.502 s, its tracked beats' jitter) or two of them on dnb-174's half-time pulse.
- The follow-ups that miss their quarter beat (only on loud beats, only the extra strike) are planned on an
  estimated interval when the next beat isn't yet revealed, and its wind-up comes before the beat arrives
  (committed beats are revealed in batches about once a second). Before the strikes moved onto the beats at their
  wind-up, 26 of 79 dnb-174 follow-ups did, all 10 ms off: the pulse's midpoint, not its skipped beat.
- Slowed to 70 BPM, house-128 is tracked at 141 BPM, so zen strikes every 0.43 s on the beat the tracker hears:
  the half-beat rule (a period over 0.75 s) never fired on real audio here. cc-kevin-macleod-mirage (the one real
  song tracked under 80 BPM, at 61) never gets a clear beat (its evidence is about 0.05), so its run never starts.
  The sim tests cover the rule ([ZenModeTests](../../Assets/Game/Tests/Sim/ZenModeTests.cs): 70 BPM strikes every
  half beat, 0.43 s apart, each a slash; 120 BPM every beat; 170 BPM every other beat).

Loudness and the horde:

- These songs are loud almost throughout, so nearly every strike is the loud one: reach 8.80 m (7.04 x 1.25) and an
  extra strike on each beat's eighth. The few quiet strikes reach 5.28 m (x 0.75). No two accented beats in a row
  came up, so no back-to-back heavies; the sim test makes them.
- Each zone change shoved and staggered the enemies in view: 59, 53 and 52 on house-128's three, 49, 76 and 58 on
  dnb-174's, 46 to 79 on the 70 BPM house's seven.

In the player (the Mono build, fixed time, windowless), house-128 with a forced section change, walking up from 8 s:

```sh
uv run --project demo3d/tools game look zen-mode --backend mono --build --at 9.34 --frames 18 --strip 6 --evidence zen-mode \
  --player-args "--mode zen --file $PWD/public/reactive/house-128/house-128.ogg --walk right@6-8,up@8-10,left@10-12 --debug change@9 --view-width 14"
```

- Its events (`--events-out`): the zone change lands on the 9.40 s downbeat, the sword's slam on the same beat, and
  195 enemies are shoved at 9.40; the strikes' impacts are 8.93, 9.40 and 9.87 (the beats) with the loud eighths'
  follow-ups at 9.165, 9.635 and 10.105, every reach 8.80 m.
- **[zen-mode.jpg](zen-mode.jpg)**, 9.35 to 9.63 s a frame apart: the knight walks up into its wind-up with no key
  pressed, the slam lands on the beat frame (+3, 9.400), and its white-hot ring and the crystal burst roll out from
  there.

How it looks and feels (the kick bob, the stagger, a slash on every beat while walking) is on
[the operator's list](../operator-pending.md).
