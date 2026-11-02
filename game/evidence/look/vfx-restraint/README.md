# VFX restraint: modest every day, bright at special moments (ps-wnj.25, 2026-11-01)

The operator's direction mid-run: the neon look was overwhelming; tone it down
and save the flashy effects for special moments. This is the VFX side
(ps-wnj.24 does the grid, the structure flashes and the post): ps-wnj.15's
slashes, spins, bursts, smears and sparks are modest every day, and their
bright versions (`-big`) draw only at a special moment through the `vfx`
table's `NAME@special` keys ([neon-look.md, VFX](../../../../docs/archive/design/neon-look.md#vfx)).

| | everyday (this change) | special moment (ps-wnj.15's look) |
| --- | --- | --- |
| slash, spin | one ribbon, 2/3 the width, core 2 (was 3.5), half the hdr, no echo, half the light | the wide white-cored ribbon, its echo, full light |
| prism slash, spin | three bands, 0.14 wide (was 0.2), core 0.8, 0.55 the hdr | as before |
| crystal burst (game) | chips' streak 0.04 and hdr 5 (were 0.06 and 8), one smear at half the hdr, light 1.4 | as before |
| hit flash (game), hit sparks, thrust, lunge (demo3d) | about half the hdr, fewer sparks | none: no special moment |
| slam, formation smash | always special: the ring as before | |

Special moments: the game's heavy strikes (slash or spin), its slam and its
mass kills (8 or more deaths at one instant); demo3d's slams, formation
smashes and heavy blows (blow feel weight 0.75 or more).

Same recipes as [../vfx](../vfx/README.md), from this change's Mono player:

- `slash-V/`: the peak run's light slash at 3.670 s, everyday: compare
  [../vfx/slash-V](../vfx/slash-light-streaks/slash-light-streaks.jpg).
- `steady-V/`: the steady stills and their metrics. They break the same
  rules as before (bright share and white at 10 s, the halo at 13 and 25 s,
  white and the key at 28 s), set by the area weapons and Bloody Tear rather
  than by these effects.
- [demo3d/evidence/look/vfx-restraint](../../../../demo3d/evidence/look/vfx-restraint/):
  `duel-V.jpg` (the opening slash and spin, everyday) and `shot2-V.jpg` (the
  crypt's slam and its cleaves through the charge, heavy blows: special).
- The game's special moments look as in [../vfx](../vfx/README.md): its
  spin strip is a heavy spin and its slam strip the slam. (This run's finale
  at 32.6 s now opens a treasure chest instead of a mass kill, so it has no
  strip of a mass kill's bursts.)

`game perf` (edge, IL2CPP): peak probe p50/p99 3.03/6.86 ms, break
2.67/4.21, finale 5.17/7.36, main p99 at most 2.1 ms, no frame over 33 ms.
`game beat peak` (edge): 14317 moments over 40 s, 0 off their frame.
