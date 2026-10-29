# TD6 evidence: navigation

Text, and PNG maps that carry the `ps-render` tag (written by the choreography CLI's `LayoutPng`).
Measured on Cygnus under .NET (Release, x64) on 2026-10-28 while other agent sessions were running,
so timings are noisy upward.

- `tick-timing.md`: the song's build and tick timing, S1 to S6 at N = 500, 1000, 2000 and 2800, and
  back-to-back comparisons with TD3.
- `measurements.txt`: the layer's tests' own output: walls in every timeline, the courtyard test
  layout's reach, stuck, marching and jitter counts, S6's fill, stress fill, overlaps, the song's
  kill rates, and the placement list's counts at densities 1 and 2.
- `song.txt`: the song's summary and section table (`... -- song`).
- `s6.txt`: S6's summary at its tracked N = 1200 (`... -- stress S6`).
- `arena-map.png` (4 px/m): the arena's placement list (`placements --png arena-map.png --px 4`).
  Forest dark, fighting ground lighter, edge bands between; walls sand, trees deep green, bushes
  green, rocks grey; soft pieces as specks; goals as orange rings. North (+Y, where the camera
  looks) is up. Thin walls alias at this scale; they are continuous (a test checks).
- `courtyard-test-field.png` (10 px/m): the courtyard test layout with the field toward the hero in
  5 m bands: the courtyard's closed north side, the U open to the west, the long wall with one gap.
- `s6-funnel.png` (6 px/m, 80 m round the camera): S6 at 40 s, the horde in four funnels on the
  courtyard's gaps, the hero's kill zone inside the walls (`stress S6 --png ... --at 40`).
- `song-final-peak.png`: the song at 175 s, the hero out of the courtyard's north gate
  (`song --png ... --at 175`).
