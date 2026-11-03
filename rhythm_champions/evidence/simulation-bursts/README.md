# Full-build simulation bursts — ps-3wc.17

2026-11-03, jarvis / Apple M5 Max. This follows the failing
[Warden measurement](../warden-review/README.md), preserving the full-build
setup, complete weapon/support output, collision order and drawing. Results
below are actual IL2CPP Low measurements, not an M1 or GPU-budget claim.
The final sweep includes the parallel hero/readability delivery `9374ce01`;
this bead changes none of its rendering rules.

## Profiling and changes

The assumption that melee targeting dominated was not supported by the moving
stress scene. A temporary instrumented .NET copy used the exact
`DuelSceneDriver.Begin` configuration for Dragon/Storm, `ScriptedSong`,
`SimDriver`, `FrameClock`, 48 seconds at 60 frames/s, the real target/dash
schedule and event draining. The high-cost frames coincide with synchronized
horde steps. `Horde.Step → Land → KeepGaps → Enemies.Near` dominates; weapon
activation/collision is smaller. Scopes are inclusive and add overhead; these
.NET times are diagnostic, not player timings or a controlled speedup claim.

A five-second native sample of the first optimized IL2CPP player corroborated
the path: 22 of 27 sampled `Simulation.RunTick` stacks were inside `Horde.Tick`,
21 in `Horde.Step`. This is a small statistical sample, not an exact cost
percentage. Symbols came from the player's generated GameAssembly dSYM.

Concrete changes:

- Bodies over 1 m radius are checked separately from the ordinary-body grid.
  Previously the Dragon's 3.5 m radius expanded every small-mob neighborhood
  query. Large matches are inserted in the original row/cell/id order, keeping
  nearest ties, collision pushes and bounded proc spending unchanged.
- The grid stores compact query geometry in bucket order. Rejected candidates
  no longer require indirect reads from the larger enemy slots. Living state
  is still read on every accepted candidate, so deaths need no grid rebuild;
  spawns and moves invalidate the grid as before.
- A mob's collision passes snapshot neighbor landings once per move. The
  four push attempts and three possible slide bends reuse those positions and
  radii in the same order, with the same geometry and arithmetic.
- Hot `StructList` accessors/addition allow IL2CPP inlining while retaining all
  bounds checks and growth behavior.

The exact 48-second reproduction issues 131,415 neighborhood queries and
1,294 grid rebuilds. Separating large bodies reduces searched cells from
7,094,602 to 3,406,702 and ordinary grid entries visited from 18,822,296 to
9,900,717; the separate large-body checks are additional. These are structural
work counts, not wall-clock speedups. The final simulation hash is unchanged:
`BA7B3B99AFFBBE37`. Temporary profiling code lives outside the repository and
is absent from the delivered simulation. No timing thresholds or performance
sweep tests were added to CI.

## Reproduce the player measurement

Use the [full-build command](../full-build/README.md#reproduce), changing the
output directory. The six `full-*` scenes remain explicit opt-ins, excluded
from `--stills-scenes all` and CI. Builds use `--backend il2cpp`; play uses
`--headless --fixed-time --tier low`, all six named scenes and `--bench-out`.
No other test/build was deliberately run alongside the final sweep. This is
still a shared host, not a controlled comparison with earlier evidence.

Private source frames, ID passes, native sample and temporary diagnostic
sources are retained on jarvis under
`~/Library/Caches/playlist-survivors/evidence/ps-3wc.17/`.
No vendor-derived image, mesh or bake is committed. The fixes touch simulation
storage and query work only; the wireframe meshes, shaders, palette, presenter
and trailer drawing are unchanged.

## Measurements and limits

[Raw six-case report](low-il2cpp.json).

| Boss/build | Sim p50 ms | Sim p99 ms | Presentation p99 ms | Synchronized render p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Conductor / Barrage | 0.078 | 1.404 | 1.116 | 6.533 |
| Conductor / Storm | 0.043 | 1.765 | 1.492 | 7.211 |
| Dragon / Barrage | 0.078 | 1.632 | 1.109 | 5.243 |
| Dragon / Storm | 0.037 | 1.908 | 1.340 | 7.007 |
| Ogre / Barrage | 0.080 | 1.496 | 1.077 | 6.490 |
| Ogre / Storm | 0.042 | 1.891 | 1.451 | 7.681 |

**Budget status: all six simulation p99 values still exceed the 1 ms M5 proxy.**
The remaining excess is 0.404–0.908 ms. This delivery reduces concrete query
work and preserves output; it does not establish that the simulation budget
is met. Synchronized retained-horde collision planning remains the cost to
address. Earlier reports and these shared-host sweeps are not controlled
speedup comparisons.

The sim column measures `SimDriver.Step`; each case has 2,526 samples after
six seconds of warmup, six maximum weapons/supports, 1,000 retained ordinary
mobs plus boss, structures and the 384-pickup render overlay. See the original
full-build evidence for the invulnerable stress fixture's balance limitations.
Flight/event counts remain identical to the Warden's six-case reports.

Synchronized render time includes CPU submission and synchronous readback;
it is not a GPU timestamp. No GPU-only measurement was obtained, so the
1.5 ms GPU proxy remains unverified. The simulation percentiles and rendering
percentiles are not added into an invented frame budget. No M1/60 fps claim,
hand-play, speaker check or art acceptance is made here. The separate hero
visibility/threat hierarchy work and ps-6y2 scope are unaffected.

## Validation

Exact body-query results and row/cell/id order are compared with brute-force
geometry through mixed radii, tangencies, zero reach, negative cells, deaths,
movement and reused slots, including an all-large-body remainder. Existing
recorded replays and support/form/proc tests retain their expected outputs.
The final player build and all six full-build scenes complete successfully;
Enact fast/slow results are recorded on the bead.
