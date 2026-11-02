# The finishers (ps-qbd)

`--mode zen --finishers` ([the design](../../../docs/archive/design/game-modes.md#finishers)): every phrase a ring closes on the
hero toward the phrase's downbeat, and Space heard in its window fires a screen-clearing slam; a miss costs nothing.
The questions: do the rings land on the phrase downbeats at the cadence asked for (8-15 s apart), causally; does a press
in the window fire at once and clear the view; does a press outside it fire nothing?

## Over real songs

The harness, [Finishers](Finishers/Program.cs), drives a zen run with the finishers over one entry dump as the sweeps
drive a run (60 fps frames, each entry arriving 60 ms after its stamp, the run's level in the game's arena, seed 1, the
headless start), with the starting sword, no cards, a hero that never falls, walking a square (each arrow held 2 s in
turn). It answers the rings in turn: the even ones with Space heard near the downbeat (-60, -20, +20, +60 ms in turn),
the odd ones with Space heard 180 ms after it (outside the window), each key taken a live lead (90 ms) after it was
heard and planned on the ring's latest downbeat (as a player follows a retimed ring). Every ring goes to `LABEL.csv`; a
summary to stdout.

```sh
S=/tmp/finishers   # any scratch folder
for s in house-128 dnb-174 hiphop-92-swing; do
  (cd native && cargo run --release -q -p cli -- entries ../public/reactive/$s/$s.ogg --out $S/$s.jsonl </dev/null)
  dotnet run --project game/evidence/zen-finishers/Finishers -c Release -- game/evidence/zen-finishers $s $S/$s.jsonl
done
```

2026-11-01, this change:

| Song | Tracked tempo | Phrase, telegraph | Rings | Apart | Hit / missed | On their beat | Retimes |
|---|---|---|---|---|---|---|---|
| [house-128](house-128.csv) | 128 BPM | 16 beats, 1.88 s | 7 | 7.49-7.51 s | 4 / 3 | 7 of 7 | 9, at most 12.5 ms |
| [dnb-174](dnb-174.csv) | 174 BPM | 32 beats, 1.38 s | 4 | 6.54-29.31 s | 2 / 2 | 4 of 4 | 3, 165-177 ms |
| [hiphop-92-swing](hiphop-92-swing.csv) | 92 BPM | 16 beats, 1.30 s | 0 | | | | |

- **house-128: every ring on its phrase downbeat**, every 16 beats from the lock (9.40, 16.90, 24.40 ... 54.40 s),
  started 1.87-1.88 s (its 4-beat telegraph) before it. The first prediction was within 10 ms of the beat, eased onto
  it as the beat was revealed.
- **Hits fire at once**: every slam's impact is 33.3 ms after its press was heard (`Sword.PressImpactS`), presses heard
  60 ms early to 60 ms late alike, though the sim took each key 90 ms after it was heard. house-128's slams killed 4 to
  24 (median 15) and broke 21 structures over the run; dnb-174's killed 15 and 16.
- **Misses fire nothing**: Space heard 180 ms late left each odd ring to end as a miss, with no slam.
- **dnb-174's tracker flips** between committed beats 0.34 and 0.69 s apart (full and half time), so a ring predicted
  four beats ahead finds its beat count off by one or two: it moved to the beat nearest its promise, 165-177 ms away, and
  the player (here, the harness) follows. The jumps also pass over some phrase downbeats' start windows (one found too
  late, skipped as the design says), so rings came 6.5 to 29 s apart. Before the ring took the nearest beat it kept
  its beat index and moved 0.52-0.55 s (1.5 beats); see the open questions in the design.
- **hiphop-92-swing never rings**: its beat is reliable only 18 s of 62 (its evidence hovers at the 0.2 gate), never
  when a phrase downbeat's ring would start. The gate rule, as designed.
- The sim tests ([FinisherTests](../../Assets/Game/Tests/Sim/FinisherTests.cs)) cover the cadence on steady fixtures at
  70, 128 and 174 BPM (13.7, 7.5 and 11 s apart), the causality (each ring's downbeat is the prediction from the beats
  known when it fired; truncating the stream after a cut changes nothing before it, with presses), a hit, its damage,
  kills and structure breaks, a press outside the window and none, a live key resolved in heard time, holds, the gate,
  and a session replaying to its hash ([SessionTests](../../Assets/Game/Tests/Sim/SessionTests.cs)).

## In the player

In the player (the Mono build, fixed time, windowless), house-128 with a scripted walk and Space at 9.38 s for the
first ring, none for the second:

```sh
uv run --project demo3d/tools game look zen-finishers --backend mono --build --at 7.6,8.3,8.9,9.25,9.37,9.42,9.47,9.6 --strip 4 \
  --evidence zen-finishers --player-args "--mode zen --finishers --file $PWD/public/reactive/house-128/house-128.ogg \
  --walk right@6-8,up@8-10,space@9.38-9.43,left@10-12 --view-width 14 --events-out /tmp/hit-events.csv"
uv run --project demo3d/tools game look zen-finishers-miss --backend mono --at 15.6,16.3,16.75,16.9,17.0,17.15 --strip 3 \
  --evidence zen-finishers --player-args "--mode zen --finishers --file $PWD/public/reactive/house-128/house-128.ogg \
  --walk right@6-8,up@8-10,space@9.38-9.43,left@10-12,down@12-14,right@14-17 --view-width 14 --events-out /tmp/miss-events.csv"
```

(The strips were renamed `hit.jpg` and `miss.jpg`.) Its events (`--events-out`), against the beats:

| Ring | Starts closing | Downbeat (first predicted, then) | Press heard | Outcome |
|---|---|---|---|---|
| 0 | 7.53 s | 9.41, retimed to 9.40 at 8.23 (the beat revealed) | 9.383 (17 ms early, taken as heard: a scripted key) | hit: the slam's impact 9.417, 7 deaths on it, 4 structures broken by its wave at 9.49 |
| 1 | 15.03 s | 16.91, retimed to 16.8975 at 15.23, to 16.90 at 16.23 | none | missed: it fades after 17.0 (its miss event at 17.5, past the last still) |

- **[hit.jpg](hit.jpg)**, ring 0: the ring is the broad white band at the knight's feet, 4.5 m out as it starts (7.6
  s, under the beat's slash), 3.5 m at 8.3, 2.5 m at 8.9, 1.3 m at 9.25, reaching the knight's own ring on the beat;
  Space at 9.383 and the slam lands two frames on (9.42: the white-hot shock round the knight, the crowd's shatter),
  then its ring and crystal burst roll out across the view (9.47, 9.6).
- **[miss.jpg](miss.jpg)**, ring 1: the same close from 15.6 to the downbeat at 16.9, where it flares in its window,
  then fades (17.0) and is gone (17.15) with nothing fired; the beat's slashes go on as ever.

How it looks and feels (does the ring read as an invitation and its arrival as the beat, does the slam read as the
screen's biggest move) is on [the operator's list](../operator-pending.md).
