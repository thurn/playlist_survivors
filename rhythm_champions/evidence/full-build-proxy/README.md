# Full-build simulation proxy — ps-3wc.19

2026-11-03, jarvis / Apple M5 Max. This follows the
[ordered collision reductions](../collision-planning/README.md). These are
shared-host observations, not controlled speedup comparisons or an M1 claim.

## Profile and implementation

Refreshed inclusive scopes around complete `SimDriver.Step` bursts found
that queries, ordered gap pushes and slides remained material, together with
Storm's lightning searches. Structure movement and melee aiming were much
smaller. A fresh ten-second native sample on the combined implementation
finds 25 of 73 sampled `SimDriver.Step` stacks in `KeepGaps`, with recurring
per-tick ownership/elite scans as well. This is a small statistical sample,
not a precise cost percentage. The instrumented native Dragon/Storm report is retained with the
[exploratory trials](exploratory-trials.json); scope overhead makes it a
diagnostic, not a budget measurement.

The delivered algorithms preserve movement, query order and hit selection:

- Local ordinary-body grids use counting-sorted contiguous row/cell/id data.
  Circle queries scan clipped row spans. At most 16,384 cells use this path;
  populations spread across a larger area retain the original spatial hash
  and rectangle cache. Large bodies remain separate and merge in original
  query order. Death tombstones and slot reuse work in both layouts.
- A bounded exact-circle cache owns its result storage. Movement/spawning
  invalidate it; a death counter makes a hit filter living IDs when needed.
  Cache collisions or exhausted storage fall back to a complete query.
  Distinct movement-planning queries bypass exact-cache writes that publication
  would invalidate; they still use the same complete spatial query. Ownership
  and elite scans use bounded spans rather than repeated per-slot getters.
- Nearest eligible lightning targets use nearby cells first. A conservative
  centre-distance bound stops only when remaining cells cannot beat the
  current target, retaining ID ties. A pure, allocation-free value filter
  applies the same root reach, parent exclusion and shared hit ledger. Large
  bodies and wide/spread queries retain complete fallback searches.
- Ordered gap pushes reject an axis-disjoint body before computing squared
  distance. A bit-identical fixed point ends redundant remaining passes.
  Every actual push uses the original arithmetic and candidate order. The
  same conservative slide-distance bound excludes an unreachable hero; an
  eligible hero remains first in the uniform slide list. Slide behavior and
  nearest-first beat planning are unchanged.
- Proc and hit-window validation uses the same three defined integer values,
  avoiding boxing/reflection on the hot path. Native generated code confirmed
  enum boxing on every original proc attempt, even rejected attempts. The
  Dragon/Storm reproduction makes 60,378 of these attempts.
- Membership-only hit/encounter ledgers cache one local positive-identity
  page in 1,024 bits, with complete half-full open addressing for every
  other identity. Zero, negative structure identities and spawn
  generations retain exact membership; windows still clear at the same
  times. These sets expose no iteration and cannot reorder hits or procs.

The exact 48-second moving Dragon/Storm reproduction retains hash
`BA7B3B99AFFBBE37`, 1,001 alive and event cursor 89,283. Private diagnostic
counts, not speedup measurements: 310,859 redundant push checks are avoided;
8,350,027 remaining push candidates reject on an axis, leaving 1,374,996
squared-distance checks versus 10,035,882 previously. Collision geometry
is packed into 32-byte entries; IDs live separately and are read only for
the exactly coincident-push direction. Nearest-cell search also avoids
materializing full lightning candidate lists. Bend checks including the hero
fall from 1,714,508 to 1,429,145; only 455 of 98,820 planned moves retain the
hero as a possible slide contact. The 32-node proc budget, shared/repeat hit
windows, support inheritance and forms are unchanged.

Angular melee range scoring, per-cell push bounds, sorted lightning caches
and root-exhaustion shortcuts were investigated and not adopted. The first
combined native candidate failed Dragon/Storm at 1.736 ms. Intermediate six-case
reports remain in the exploratory file: the last before compact identity
sets reported 0.920–1.072 ms, with four cases still failing. None is relabeled
a pass or substituted for a final sweep. The later identity, packed,
bitmap-page, read-only and hero-pruning sweeps also remain recorded, including their failing cases. The final
movement-query/span sweep below is a separate complete run.

## Reproduction and measured budget

Use the [full-build reproduction](../full-build/README.md#reproduce), choosing
a new output directory and retaining `--backend il2cpp --headless --fixed-time
--tier low`, 1080p, all six named `full-*` scenes and `--bench-out`.
Non-development IL2CPP uses the existing Master/OptimizeSpeed/full-LTO build
policy. The scenes remain explicit opt-ins outside `all` and CI. Neither a
wall-clock assertion nor an automatic performance sweep was added.

[Final six-case raw report](low-il2cpp.json), Master IL2CPP / Low, Metal,
1920×1080, 2,526 measured samples per case after six seconds of warmup:

| Boss/build | Sim p50 ms | Sim p99 ms | Presentation p99 ms | Synchronized render p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Conductor / Barrage | 0.060 | 0.906 | 1.150 | 5.854 |
| Conductor / Storm | 0.029 | 0.931 | 1.440 | 6.695 |
| Dragon / Barrage | 0.052 | 0.932 | 1.120 | 6.741 |
| Dragon / Storm | 0.028 | 0.975 | 1.327 | 6.747 |
| Ogre / Barrage | 0.055 | 0.912 | 1.107 | 6.455 |
| Ogre / Storm | 0.032 | 0.976 | 1.396 | 7.198 |

**Measured budget status: all six sim p99 values meet the unchanged 1 ms M5 proxy.**
The observed range is 0.906–0.976 ms, with 24 microseconds of headroom in the
slowest case. That is limited headroom on a shared host, not a controlled
speedup comparison or an M1 claim. All six retain 1,001 alive, zero presenter
faults and exactly the previous event/peak-flight counts (Barrage 290/298/300;
Storm 176). No failing intermediate report was rounded down into a pass.

Each case uses the unchanged slot/level-legal maximum six-weapon/six-support
build, 1,000 retained ordinary mobs plus boss, structures and the 384-pickup
render overlay. The invulnerable fixture does not establish attainable fight
duration or balance. No test or other build was deliberately run alongside
the final sweep; the host remains shared.

Synchronized rendering includes CPU submission and synchronous GPU readback.
It is not GPU-only timing; the 1.5 ms GPU-only proxy remains unverified. Sim
and render percentiles are not added into an invented total frame time.
M1-class hardware, speakers, hand-play and subjective visual acceptance remain
pending. Drawing, closed wireframe facets, vendor provenance and independent
ps-6y2 scope are preserved.

## Validation

476 .NET behavior/replay tests pass. New checks cover circle-cache ownership,
deaths/reuse, dense/hash transitions, nearest eligible selection against a
brute-force reference, rounded cell-boundary reach, invalid enum values and
identity membership against `HashSet<long>` across collisions/growth/reset.
No test depends on machine speed. Unity EditMode passes 123/123 with no skips
on the merged tree.

The actual Master IL2CPP player replays all four recorded sessions to their
.NET hashes and event counts with no presenter faults; see
[replay validation](replay-validation.txt).

Enact delivery checks and staging commit are recorded on the bead. Private
probe sources, logs, raw reports and vendor-derived captures remain on jarvis
under `~/Library/Caches/playlist-survivors/evidence/ps-3wc.19/`. No vendor-derived
images, meshes or bakes are committed.
