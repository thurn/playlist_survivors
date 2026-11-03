# Retained-horde collision planning — ps-3wc.18

2026-11-03, jarvis / Apple M5 Max. This follows
[ps-3wc.17](../simulation-bursts/README.md). All measurements are shared-host
observations, not a controlled speedup comparison or an M1 performance claim.

## Profiling and changes

A finer temporary profile separates neighbor queries, landing snapshots,
crowded-spawn pushes and slide bends. Instrumented native IL2CPP confirms
that queries and repeated collision passes consume the movement burst.
A five-second native sample of the intermediate player, symbolicated with
its generated GameAssembly dSYM, finds 21 of 22 sampled `Horde.Step` stacks
inside `Land`. These are diagnostic scopes/small statistical samples, not
precise percentages or player budget measurements.

The delivered implementation preserves synchronized nearest-first movement
and the original query/collision order:

- A movement batch snapshots compact landing positions and radii once,
  then updates each mover's entry as its plan is committed. Each move uses
  a reusable neighbor array and precomputes squared separation gaps.
- Gap pushes retain every original candidate. Only after pushing, bodies
  beyond the remaining move length plus their gap are removed from slide
  checks. A conservative geometric margin retains boundary cases. Each
  slide only spends its remaining length, so an excluded body cannot meet
  a later bend. The first zero-time contact ends that contact search:
  all later contact times are clamped to zero or above and cannot beat it.
- The enemy grid marks dead cached entries directly. Queries fill a reserved
  output array and publish its count once. Large-body insertion uses binary
  search and one overlapping copy, retaining row/cell/id ties.
- A bounded, direct-mapped cache reuses the ordered contents of identical
  grid rectangles. Every caller still performs its own exact circle test;
  deaths remain visible and movement/spawning invalidate the cache. A cache
  collision or full buffer falls back to the ordinary scan.
- Enemy cone tests prepare shared trigonometric constants once per shape.
  Dot-product tests replace per-body inverse trigonometry; a conservative
  boundary band falls back to the original geometric calculation. Wide
  and unusual cones also retain the original calculation.

The exact 48-second moving Dragon/Storm reproduction retains simulation hash
`BA7B3B99AFFBBE37`. Before/after structural counts from private instrumented
.NET copies (not wall-clock speedups):

| Work | Staged baseline | Delivered algorithms |
| --- | ---: | ---: |
| Planned moves | 98,820 | 98,820 |
| Horde query results | 3,069,689 | 3,069,689 |
| Gap push checks | 10,035,882 | 10,035,882 |
| Bend checks, including hero | 9,031,301 | 1,714,508 |
| All-query grid cells searched | 3,406,702 | 2,209,048 |

The rectangle cache records 44,514 hits and 86,901 misses. Ordinary grid
visits split into 6,152,991 direct visits and 3,747,726 cached visits, the same
9,900,717 total candidates as before; it removes cell lookup work rather
than pretending those bodies no longer need geometry tests. Large-body
checks are additional. Grid rebuilds remain 1,294.

Recursive and stackless global landing-bound trees preserved the replay but
were slower in the actual player; they were discarded. Group bounds and
circle-row clipping were also explored and are not shipped.
[Exploratory raw reports](exploratory-trials.json) retain these observations,
including the normal windowed trial, which still exceeded the simulation
proxy. Neither windowing nor a different sample count is used to hide a gap.

## Reproduction and budget status

Use the [full-build reproduction](../full-build/README.md#reproduce), with a
new output directory. The six `full-*` scenes remain explicit opt-ins outside
`all` and CI. Retain `--backend il2cpp`, `--headless --fixed-time --tier low`,
1080p and `--bench-out`.

The intermediate Release compiler six-case sweep is retained in
[release-intermediate.json](release-intermediate.json). It predates prepared
cone tests and reports simulation p99 1.117–1.560 ms: all six still fail the
1 ms M5 proxy, with 1,001 alive, zero presenter faults and unchanged event and
flight counts. The complete slot/level-legal six-weapon/max-support builds,
384-pickup render overlay and invulnerable stress balance limitations are
unchanged from the full-build fixture.

Non-development IL2CPP builds now use Unity's Master compiler configuration
and OptimizeSpeed code generation; Mono remains the default for development
and CI. The previous project settings are restored after the build. The
Master build graph confirms `-O3` and full `-flto=full`, without fast-math.
The first such build took 298.2 seconds on this host; it is an explicit perf
build, outside slow CI. [Unity recommends Master for shipping builds when
its longer build time is acceptable](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/il2cppcompilerconfiguration).
This is not a controlled claim about the compiler's speedup.

[Final six-case raw report](low-il2cpp.json), Master IL2CPP / Low, Metal,
1920×1080, 2,526 samples per case after six seconds of warmup:

| Boss/build | Sim p50 ms | Sim p99 ms | Presentation p99 ms | Synchronized render p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Conductor / Barrage | 0.079 | 1.085 | 1.369 | 6.617 |
| Conductor / Storm | 0.033 | 1.343 | 1.439 | 6.721 |
| Dragon / Barrage | 0.070 | 1.201 | 1.096 | 5.701 |
| Dragon / Storm | 0.032 | 1.452 | 1.348 | 6.564 |
| Ogre / Barrage | 0.073 | 1.077 | 1.097 | 6.241 |
| Ogre / Storm | 0.036 | 1.446 | 1.372 | 7.094 |

**Budget status: all six still exceed the 1 ms M5 simulation proxy.** The
remaining excess is 0.077–0.452 ms. The requirement remains implementation
work under ps-3wc, not operator-only taste/hardware acceptance. All cases
retain 1,001 alive, zero presenter faults and the prior six-case event/flight
counts. These shared-host results do not establish a controlled speedup or
satisfy the proxy. No other test/build was deliberately run alongside this
final sweep. No timing threshold assertion, automatic performance sweep or
opt-in regression suite was added to CI.

Synchronized offscreen rendering still includes CPU submission and one-pixel
GPU readback. It is not GPU-only timing; no defensible GPU timestamp was
obtained, so the 1.5 ms GPU-only proxy remains unverified. Simulation and
render percentiles are not added into an invented total-frame budget.

Private diagnostic sources, native samples, raw logs and vendor-derived
captures remain on jarvis at
`~/Library/Caches/playlist-survivors/evidence/ps-3wc.18/`.
No vendor-derived images, meshes or bakes are committed. Drawing, closed
wireframe facets, hero feedback, telegraphs and the independent ps-6y2 scope
are unchanged.

## Validation

459 .NET behavior/replay tests pass, including unchanged recorded session
hashes, exact body-query ordering through deaths/moves/reused slots, repeated
rectangles with distinct exact circles, cone angular/radial tangencies, and
crowded-query stepping/gliding against a real nearby obstacle. No test uses
a machine-speed threshold or a performance sweep. Unity EditMode passes 123/123,
with no skips. The actual Master IL2CPP player replays all four recorded
sessions to their .NET hashes with matching event counts and no presenter
faults; [replay validation](replay-validation.txt) records the results.
Enact delivery checks and the staging commit are recorded on the bead.
