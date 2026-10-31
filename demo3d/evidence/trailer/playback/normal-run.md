# A normal run with this bead

Without `--trailer`, this bead changes nothing a normal run draws. Two
measurements, both windowless IL2CPP look sets (`demo3d look SLUG --trial`):

## Against staging's own build

The look set's song stills from this bead's build (`ps-hgs6-normal`) against
the same stills from a build of its base, staging `286eafb3`
(`ps-hgs6-base`, `--song-only`), pixel by pixel:

| still | base L* p50 | this bead | dp50 | mean \|dL*\| | max \|dL*\| |
|---|---:|---:|---:|---:|---:|
| song-025 | 42.51 | 42.51 | +0.00 | 0.001 | 0.96 |
| song-060 | 33.52 | 33.52 | -0.00 | 0.000 | 2.09 |
| song-100 | 43.27 | 43.27 | +0.00 | 0.002 | 3.42 |
| song-145 | 35.30 | 35.30 | +0.00 | 0.001 | 1.83 |
| song-175 | 53.03 | 53.03 | +0.00 | 0.005 | 4.40 |

The few pixels that differ at all are a handful of particles; the frames are
the same.

## Against look/final

`residency.py final ps-hgs6-normal` against
[look/final](../../look/final/README.md)'s numbers:

| still | p50 | final | dp50 |
|---|---:|---:|---:|
| song-025 | 42.51 | 42.16 | +0.35 |
| song-060 | 33.52 | 32.78 | +0.74 |
| song-100 | 43.27 | 41.86 | +1.41 |
| song-145 | 35.30 | 30.71 | +4.59 |
| song-175 | 53.03 | 31.51 | +21.52 |
| opening | 44.66 | 44.65 | +0.01 |
| path | 37.96 | 37.94 | +0.02 |
| glowing-tree | 39.62 | 39.62 | -0.00 |
| graveyard | 37.72 | 37.71 | +0.01 |
| courtyard | 35.22 | 35.22 | -0.00 |
| forest-edge | 30.17 | 30.17 | +0.00 |

The six beauty shots match within 0.02 L*. The song stills don't, but they
differ from look/final on staging itself, exactly as here. The song's
normal timeline has changed on purpose since look/final was taken, so the
hero and the horde stand elsewhere in those frames. Among the changes:
74989741 (one-hit kills for every monster but the boss), 2895cd24 (the
hero fights on until the ending's slam windup) and 09b85282 (the drops
cleave). ps-hgs.1 measured 0.11 L* at `8161a26c`, before
them. look/final's song stills need retaking against today's song before the
epic's "within 1 L*" can hold for them.
