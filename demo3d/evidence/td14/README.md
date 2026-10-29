# TD14: environment rendering — evidence

All measurements on Cygnus (RTX 4070 Ti, i7-12700F, DDR4-2133), IL2CPP player, 1920×1080 windowed,
6 job workers, MSAA 4×, through TD7's harness (`demo3d sweep`, one spec per process). Other
sessions (TD11's A/B captures, TD15's sweeps and builds, idle editors) ran beside most of it, so
nearly every row is marked noisy (other processes over 10 % CPU, and S0's zero-GPU-time frames at
about 1,000 fps); each table gives the median of a run's three attempts. Within one round, repeats
agree to 0.01 ms on S0 (round 3) and 0.1–0.5 ms on S3 at 2,000; between rounds S3 drifts by up to
0.7 ms, so only same-round S3 numbers are compared.

- `r1/`: round 1 (TD9's arena, TD8's effects merged): E0, E1 (GPU Resident Drawer, with and
  without its GPU occlusion culling, with and without merged small props), E2, E2 + E1. S0 at 1×
  and 2× (40 and 55 m), S3 at 2,000 (40 m).
- `r2/`: round 2 (same arena): E2 against merged, the texture caps (E5), the prepass (E6), and
  E2 at 0.5× and 4× with and without the drawer.
- `r3/`: round 3, the final measurements on TD15's arena (dressing, mist cards, rune circles,
  canopy dither; TD11's default crowd cuts): E0, E2 at every density, the E3 and E4 bounds, E5,
  E6; S3 at 2,000 and the S3 40 m frontier at E2 1×, E2 2× and E0 1×. The build has no GPU
  Resident Drawer (E1 dropped, so ProjectSetup strips its variants), so r3 has no E1 rows.
- Each round: `rows.jsonl`, the harness's `report.md`, `table.md` (`summarize.py`, every
  configuration with its attempts and counters); r3 adds `frontier.jsonl`.
- `stills/`: E0 and E2, E2 without the static set in the prepass (E6), and E2 with the kit's
  textures capped at 1,024 (E5), at the forest edge and the graveyard (`ArenaStills`, 960 px);
  `rock-triplanar-e2-1x-forest-edge-crop.jpg`: the forest edge's top-left quarter at full
  resolution, with the triplanar rock (centre top: rock-wall sides, grass on its top).

## The ladder

GPU / main / render p99 in ms, medians of three attempts; S0 at 40 m and 55 m; S3 at 2,000, 40 m
(GPU p99), same round as the rung's reference. E1's rows are from rounds 1 and 2 (TD9's arena);
the rest from round 3 (TD15's arena).

| rung | S0 1×, 40 m | S0 1×, 55 m | S0 2×, 40 m | S0 2×, 55 m | S3 at 2,000 | verdict |
|---|---|---|---|---|---|---|
| E0 stock (r3) | 0.44 / 1.16 / 0.68 | 0.47 / 1.25 / 0.74 | 0.46 / 1.12 / 0.65 | 0.51 / 1.22 / 0.74 | 7.62 | the baseline |
| E0 (r1, TD9's arena) | 0.42 / 1.08 / 0.63 | 0.46 / 2.13 / 1.04 | 0.44 / 1.05 / 0.65 | 0.48 / 1.86 / 0.96 | 9.18 | r1's reference |
| E1 drawer, on E0 (r1) | 0.44 / 0.99 / 0.62 | 0.45 / 0.94 / 0.60 | 0.48 / 1.83 / 0.91 | 0.85 / 3.14 / 1.36 (noisy) | 8.91 | helps E0's CPU at 55 m |
| E1 + occlusion, on E0 (r1) | 0.49 / 1.24 / 1.01 | 0.51 / 1.25 / 0.97 | 0.50 / 1.28 / 1.07 | 0.53 / 1.28 / 0.98 | 9.41 | dropped: +0.05 GPU, +0.4 render |
| E1 + merged, on E0 (r1) | 0.44 / 0.96 / 0.59 | 0.46 / 0.92 / 0.59 | 0.46 / 0.96 / 0.59 | 0.48 / 0.95 / 0.60 | 9.52 | no gain over the drawer alone |
| E2 (r1) | 0.41 / 0.85 / 0.56 | 0.44 / 0.91 / 0.60 | 0.41 / 0.86 / 0.58 | 0.46 / 0.93 / 0.61 | 8.89 | kept (r3 below) |
| E2 + E1 drawer (r1) | 0.41 / 0.97 / 0.60 | 0.43 / 0.93 / 0.59 | 0.41 / 0.95 / 0.57 | 0.44 / 1.06 / 0.64 | 9.16 | E1 dropped: nothing on top of E2 |
| E2 + E1 drawer, 4× (r2) | 0.42 / 0.88 / 0.54 | 0.46 / 0.84 / 0.52 | | | | against E2 4×: 0.41 / 0.72 / 0.53, 0.49 / 0.82 / 0.59 |
| E2 merged, no drawer (r2) | 0.41 / 1.25 / 0.64 | 0.60 / 1.39 / 0.80 (noisy) | 0.41 / 0.76 / 0.49 | 0.46 / 0.78 / 0.50 | 9.06 (E2: 9.55) | dropped: within noise, flatter probe light |
| **E2 (r3, final)** | **0.46 / 1.04 / 0.69** | **0.51 / 1.11 / 0.73** | **0.47 / 0.91 / 0.61** | **0.55 / 1.01 / 0.67** | **8.04** | **kept** |
| E3 bound: no small props (r3) | 0.43 / 0.95 / 0.61 | 0.70 / 1.72 / 0.96 (noisy) | 0.45 / 1.02 / 0.65 | 0.49 / 0.87 / 0.59 | 7.61 | dropped: LODs can save at most this |
| E4 bound: no forest interior (r3) | 0.44 / 1.03 / 0.66 | 0.46 / 1.03 / 0.64 | 0.45 / 0.90 / 0.59 | 0.47 / 0.98 / 0.63 | 7.82 | dropped: impostors can save at most this |
| E5 kit textures at 1,024 (r3) | 0.46 / 0.93 / 0.62 | 0.51 / 0.99 / 0.64 | 0.47 / 0.92 / 0.61 | 0.55 / 1.13 / 0.72 | 7.83 | dropped: no change |
| E6 static set out of the prepass (r3) | 0.45 / 1.00 / 0.63 | 0.48 / 0.96 / 0.63 | 0.45 / 1.03 / 0.64 | 0.51 / 1.20 / 0.75 | 7.89 | dropped: ≤ 0.04 ms, loses SSAO on the kit |

**S3's 40 m frontier** (r3, GPU-limited; [frontier.jsonl](r3/frontier.jsonl), the harness's search
at 5 % resolution, every run noisy): E0 1× 723 (GPU p99 4.08 ms), E2 1× 716 (4.10 ms), E2 2× 721
(4.08 ms). The final rung against E0: the same count within the search's resolution.

**Counters** (one frame per run, the tour's last shot): at 1×, 40 m, E0 issues 747 draws (55 m:
1,028) with every kit piece a shadow caster; E2 326 (533), its casters 0 (the static shadow map
takes all 1,903 kit renderers once at load). The drawer at E0 (r1) took the draws from 524 to
112 (55 m: 1,570 to 117), and at E2 4× from 703 to 85, and saved no time at E2. E6 halves E2's
draws again (326 to 145) for at most 0.04 ms. Set-pass calls stay 40–54 (TD15's materials).
Some S3 counter frames read about 0.22 M draws in any configuration (an effect burst in the
counted frame), so S3's draws aren't compared.

## The density curve (the final rung, E2, r3)

| density | kit renderers | S0 40 m | S0 55 m | S3 at 2,000 | S3 40 m frontier |
|---|---|---|---|---|---|
| 0.5× | 1,419 | 0.45 / 0.91 / 0.59 | 0.47 / 0.99 / 0.64 | | |
| 1× (authored) | 1,903 | 0.46 / 1.04 / 0.69 | 0.51 / 1.11 / 0.73 | 8.04 | 716 |
| 2× | 2,598 | 0.47 / 0.91 / 0.61 | 0.55 / 1.01 / 0.67 | 7.92 | 721 |
| 4× | 3,271 | 0.48 / 0.93 / 0.60 | 0.59 / 1.10 / 0.70 | | |

- **S0 at the authored density fits the ceiling**: 0.46 and 0.51 ms of GPU p99 against 1.5 ms;
  main 1.04 / 1.11 and render 0.69 / 0.73 ms against 1.7 ms. Even 4× uses 0.59 ms at 55 m.
- **Richness to monsters:** doubling the decoration (1× to 2×) costs 0.01–0.04 ms of S0's GPU
  p99 (about 0.03–0.1 ms of the target's) and no monsters: S3's frontier is 716 at 1× and 721 at
  2×, equal within 5 %. 0.5× to 4× costs 0.03–0.12 ms, which at the frontier's slope (4.5 µs per
  monster, S3's runs at 615 and 973) is 7 to 27 monsters. The CPU lines don't move with density
  (main 0.9–1.1 ms at every density: S0 is bound by fixed per-frame work at about 1,000 fps).

## What it doesn't show

- S0 runs CPU-bound near 1,000 fps with the GPU mostly idle, and 25–30 % of its frames read no
  GPU time; its GPU p99 says the environment is cheap on this GPU, not where its limit is. S3 at
  2,000 is GPU-bound at 8–9.5 ms, where the environment is a small share: no rung's S3 difference
  (at most 0.4 ms) clears the 0.5–0.7 ms run-to-run spread.
- E1 was measured only on TD9's arena (rounds 1–2); TD15's arena has 34 % more kit renderers.
  `demo3d/sweeps/td14/s0-e1-*.json` and `*-grd-*` need ProjectSetup.GpuResidentDrawer turned on
  (and a build with its DOTS variants, about 40 minutes of shader compiles) to run again.
- The E3 and E4 bounds hide the pieces entirely (after the static shadow map took them), so they
  bound what an LOD or an impostor could save; they say nothing about either cut's look, so no
  A/B check ran (TD11's thresholds were ready, but no candidate could clear the keep rule).
- The texture cap's memory saving isn't measured: the release player has no "Gfx Used Memory"
  counter (the rows' `gfxUsedMB` stays empty). By size, the cap takes the four 2,048² atlases and
  their maps (13 textures) from about 70 MB to about 18 MB of VRAM with mips.
- One counter frame per run; draw counts vary with the tour's shot.

## After the scene milestone's review (recorded by TD15's headroom round)

- E2 is kept for the static shade it gives monsters, not for time: its S0 main-thread difference
  against E0 is inside its own attempt spread, and in S3 it is GPU-slower at every matched N
  (3.82 against 3.69 ms at 646, the search probes; 8.04 against 7.62 ms at 2,000).
- Round 3's S0 E2 4× 40 m row reads 0 draws and 0 set-passes: a bad counter frame, not a measurement.
- E1 was measured on TD9's arena, before TD15's dressing.
- `summarize.py` now keeps rounds (folders) and run kinds (fixed runs, search probes, the `above`
  checks) apart; every round's `table.md` was regenerated with it.
