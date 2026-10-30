# Look pass: monster types in clumps (ps-e8r.9, 2026-10-29)

The level script picked every monster's type at random, so the horde read as
confetti. Refills now arrive in same-type clumps that form masses of colour,
as in DRG:S, and each section keeps its mix.

## What changed

- **`clumpSize`** in the level script (opt-in, in the shared choreography
  package; 0 by default, which keeps the old path and its random draws, so
  demo2d's song dump is byte-identical: FNV-1a `451c7fd5a48a1490` before and
  after). demo3d's `action-level.json` sets **80**.
- In clumped mode, refills:
  - **deal** types in the section's mix proportions (smooth weighted
    round-robin) instead of drawing them, so counts hold the mix exactly;
  - **spawn each type from its own arcs** of the view's edge. The section's
    types lay out end to end in a pattern. Each arc is the type's share of
    the ground the refills cover (weight x diameter²), so every arc fills
    alike. The pattern is scaled so the heaviest type's arc holds 80 packed
    across the 4 m spawn band. It repeats round the edge and drifts 0.5 m/s
    along it, so masses arrive from every side;
  - **retry** a type that found no room as that type on the next beat (once
    per beat).
- Clump sizes follow weight: in the climax, skeleton ~80, goblin ~60, bat and
  demon ~40, slime and mushroom ~20, elites ~4-8.
- The opening horde is unchanged (the intro is all skeletons).

Tuning: 60 gave a neighbour share of 0.69. 90 and 100 broke the on-screen,
framing or hero-stride tests. Drift 0.4-0.6 m/s made little difference.

## Measures (dotnet, `ClumpTests`)

- **Same-type share of each monster's 4 nearest neighbours at spawn**, mean
  over every monster but the boss: **0.747** clumped, against **0.291** for
  the random mix (the baseline).
- **Count per type per section:** within max(5%, 1) of the mix's share. The
  test logs the random baseline's counts too. Those stray from the mix by
  sampling noise: groove's bats were 8 of 31 against a 12.5% weight, so the
  literal "within 5% of before" can't hold for small counts. Section totals
  are within about 7% of the baseline's (build 157 vs 169, final peak 523 vs
  545).
- **demo3d's song dump:** FNV-1a `d5b3eecd6e8345d1` before, `da49148f51f99e23`
  after.
- **On screen against target:** the climax holds 0.96 (the baseline's 0.95),
  and drop-2 holds 0.92 (the baseline's 0.87).

## Stills

Windowless Mono player on the Mac (mac.md), `--fixed-time --start-at 55
--stills-at 60,100,145,170,180`. There are two builds of the same commit: one
with `clumpSize` 0 (before) and one with 80 (after). The look harness
(ps-e8r.1) hadn't landed, so these are mac.md's stills, downscaled to 960 px
with their `ps-render` tags kept. See [contact-sheet.jpg](contact-sheet.jpg)
(left: before, right: after).

| still | mean chroma before | after | RMS L* contrast before | after | near-black % before | after |
|---|---|---|---|---|---|---|
| 060 (drop-1) | 7.0 | 7.1 | 12.8 | 12.5 | 50.4 | 47.8 |
| 100 (groove-2) | 9.9 | 10.0 | 14.3 | 14.2 | 42.0 | 44.0 |
| 145 (drop-2) | 9.8 | 10.7 | 14.4 | 14.2 | 43.8 | 44.0 |
| 170 (final peak) | 9.3 | 10.5 | 14.1 | 15.9 | 38.1 | 39.8 |
| 180 (final peak) | 11.7 | 10.1 | 17.8 | 14.4 | 27.1 | 35.2 |

The whole-frame stats barely move: clumping regroups the same colours. They
also depend on where the hero's route puts the horde in each frame. What
changed is visible in the frames. Demons form red blocks, slimes and
mushrooms form green and yellow ones, skeletons form grey ranks, and elites
march together. Before, every frame mixed all types evenly. The type colours
(another bead) will make the masses stronger.

**Cost:** none in the player. The timeline is built at load (0.5 s under
.NET), and a refill spends a few float operations per spawn.
