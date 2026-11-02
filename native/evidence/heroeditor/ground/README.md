# The Painted Dusk Ground (ps-4c8.2)

native under `--art heroeditor`: the painted procedural ground and the dusk
grade (docs/archive/design/heroeditor-native.md, "The Painted Ground"), with native's
LPC characters until the HeroEditor atlas lands (ps-4c8.3). Every image is
headless output; no window was opened. Nothing here is vendor art: the ground
is procedural and the characters are native's own LPC sprites.

## Stills

- `zone-<id>.png`: each zone theme on the empty opening field, from
  `--zone-check <dir> --art heroeditor` (a synthetic run with no enemies, one
  still per zone at its minute). The palettes shift per zone, and the grade is
  each zone's dusk.
- `dense-evolved.png`, `dense-moving.png`, `dense-deaths.png`: the densest
  minute, from `--density-check <dir> --art heroeditor` (the minute-25 roster at
  300 live enemies, 400 gems and six evolved weapons, standing, moving and
  killing). Bloom, lightning, the sword's arcs and the hero's light still pop
  over the warm ambient.
- `lpc-zone-galop-woods.png` and `lpc-dense-moving.png`: the same moments in
  native's own look (no flag), to compare.
- `opening-minute-sweep.mp4`: the zone check's 24 fps sweep through the
  ember hollow into the infernal grove with the camera following the hero:
  the transform wave's soft ring sweeps out and the ground repaints in the
  infernal palette inside it. The ground moves with the world and does not
  swim.

## Checks

- Default art is unchanged: `--still` (20 s of the house track) and all five
  zone-check stills are byte-identical between the base (394fa05) and this
  change without the flag.
- `render`'s ground tests: moving the camera by whole pixels far from the
  origin moves the painted ground with it (within 2 of 255 per channel), and
  the transform wave paints the next zone's palette inside its ring.

## Cost (Apple M5 Max)

- The ground pass on the GPU, offscreen, warmed up, median of nine runs of 120
  frames, with and without the ground (bloom and grade on, no sprites):

  | target | without | with | the ground |
  |---|---:|---:|---:|
  | 1920 x 1280 (960 x 640 window, Retina) | 0.42 ms | 1.14 ms | about 0.7 ms |
  | 2560 x 1600 | 0.41 ms | 1.44 ms | about 1.0 ms |
  | 3456 x 2234 (full screen, native) | 0.59 ms | 2.51 ms | about 1.9 ms |

- CPU frame work at the densest minute, `--bench-density` (1920 x 1080, 300
  enemies, 400 gems, six evolved weapons), at full clock:

  | | median | p95 | p99 | encode p99 |
  |---|---:|---:|---:|---:|
  | default | 0.84 ms | 2.56 ms | 3.13 ms | 1.40 ms |
  | `--art heroeditor` | 0.69 ms | 2.57 ms | 3.07 ms | 0.84 ms |

  The ground costs nothing on the CPU, and drawing no tiles or props makes the
  encode cheaper (the 20 s still draws 1074 sprites instead of 1985). With
  about 2 ms of GPU at most for the ground, a dense frame stays well inside
  60 fps.
