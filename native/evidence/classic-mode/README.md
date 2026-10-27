# Classic mode fairness (2026-10-27)

Legacy (judged) mode became classic mode, judged more fairly: the groove starts full; off-beat
eighth notes cost nothing (though a press in the Good window of a beat not yet pressed still counts
for it, so early or late players on fast beats are counted and the drift corrects them); a Perfect
or Great press after a loose one takes the beat instead of costing a stray; penalties
count only from beat evidence 0.35 (full at 0.65) and not where the offline and online trackers
disagree near the press; Good, loose and stray presses cost less (`content/hero.yaml`); the drain
restarts after a level-up menu instead of counting it; and the groove meter squiggles while
judging pauses.

## The operator's last classic session, replayed

Session `1793137783` (Chrome in Delayed listening, AirPods, 262 presses over 113 s; the run never
recorded its end, so a scratch copy set it to the last key plus 1 s). `ps replay` on the scratch
copy, groove sampled every replay frame (120 a second):

| Rules | Groove mean | Groove lowest | Frames below full | Kills | Health |
|---|---|---|---|---|---|
| Before (legacy rules and costs, the release tip `4f91249`) | 0.69 | 0.00 | 64% | 441 | 0.94 |
| After (this change) | 0.94 | 0.44 | 39% | 325 | 1.00 |

The replay feeds the keys as the operator pressed them under the old rules, so it does not show
how they would have played under the new ones; kills and health move with the tier (damage) and
the spawns it shapes. The hash differs from the live run's by design.

Tracker agreement at the operator's presses (offline vs online beats within 2 s, each matching at
least 75% of the other's): 251 of 262 presses agree; 11 sit where the trackers disagree (shares
0.4 to 0.6), and those can no longer cost groove.

## The virtual players, before and after

`players/tests/smoke.rs`'s run on the reactive level, settling as the app does: mean groove over
the song and the share of it at tier 2 or above. Before is the release tip `4f91249` (a scratch
test in a throwaway checkout); after is this change. No player fell on house-128 either way.

| Track | Player | Before groove, tier 2+ | After groove, tier 2+ | Fell before / after |
|---|---|---|---|---|
| house-128 | reactive-moderate | 0.89, 88% | 1.00, 100% | no / no |
| house-128 | reactive-human | 0.75, 72% | 0.99, 100% | no / no |
| house-128 | reactive-skilled | 0.89, 88% | 1.00, 100% | no / no |
| house-128 | reactive-offbeat | 0.77, 75% | 0.98, 100% | no / no |
| house-128 | reactive-half | 0.75, 74% | 0.99, 100% | no / no |
| house-128 | mash2 | 0.06, 0% | 0.86, 94% | no / no |
| house-128 | mash4 | 0.07, 0% | 0.57, 57% | no / no |
| house-128 | mash8 | 0.00, 0% | 0.12, 10% | no / no |
| house-128 | holder | 0.00, 0% | 0.22, 18% | no / no |
| orpheus-galop | reactive-moderate | 0.94, 94% | 1.00, 100% | no / no |
| orpheus-galop | reactive-human | 0.91, 91% | 1.00, 100% | no / no |
| orpheus-galop | reactive-skilled | 0.94, 94% | 1.00, 100% | no / no |
| orpheus-galop | reactive-offbeat | 0.82, 83% | 0.99, 100% | no / no |
| orpheus-galop | reactive-half | 0.94, 94% | 1.00, 100% | no / no |
| orpheus-galop | mash2 | 0.59, 56% | 0.98, 100% | 123 s / no |
| orpheus-galop | mash4 | 0.49, 46% | 1.00, 100% | 115 s / no |
| orpheus-galop | mash8 | 0.43, 40% | 0.94, 94% | 130 s / no |
| orpheus-galop | holder | 0.53, 50% | 0.96, 100% | 123 s / no |

On dnb-174 (after only; not measured before): every honest style 0.99 to 1.00 at 100% tier 2+;
mash2 0.91, mash4 0.99 (four presses a second at 174 bpm is near eighth notes), mash8 0.17 at
13%, the holder 0.25 at 22%; nobody fell.

Every honest style now keeps a full groove, the off-beat and half-time players included. On a
clear beat (house-128) the mashers and the holder still rank below them, by how much they mash.
On the galop the tracker is rarely sure of the beat (the penalty scale averaged 0.07 over the
bots' settled presses, and the trackers disagreed at about 64% of them), so classic mode judges
almost nothing there and the masher keeps its groove: the rule "penalize only when sure" working
as asked. The old smoke test (the masher falls on the galop) became two: the honest player
outplays the masher on house-128 by 0.2 at least, and the off-beat and half-time players keep
their groove on both tracks; the determinism check stays. Letting a Perfect take a beat from a
Good press (tried, then dropped) lifted mash4 on house-128 to 0.81.
