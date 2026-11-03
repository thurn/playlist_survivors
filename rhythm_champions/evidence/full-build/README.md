# Full-build load and readability — ps-3wc.14

2026-11-03, jarvis / Apple M5 Max. Actual IL2CPP player, Metal, 1920×1080,
Low. This delivery records a failing proxy, fixes concrete incremental costs
and visibility problems, and preserves the remaining gap for review. It does
**not** claim M1 performance or complete visual acceptance.

## Reproduce

```sh
uv run --project rhythm_champions/tools champions setup
uv run --project rhythm_champions/tools champions build --backend il2cpp
mkdir -p /tmp/champions-full
uv run --project rhythm_champions/tools champions play --backend il2cpp --headless \
  --fixed-time --tier low --capture-stills /tmp/champions-full \
  --stills-scenes full-ogre-storm,full-conductor-storm,full-dragon-storm,full-ogre-barrage,full-conductor-barrage,full-dragon-barrage \
  --bench-out /tmp/champions-full/probe
```

Scenes are explicit opt-ins: `--stills-scenes all` excludes `full-*`, so neither
these sweeps nor timing gates run in slow CI. Omit `--bench-out` for captures
without per-frame synchronized rendering. Repeat `full-conductor-barrage`
with `--look trailer` and `--tier high` for the comparison. Use `--windowed`
instead of `--headless` to watch, retaining `--fixed-time` for reproducibility.

Each scene retains 1,000 ordinary monsters (5:1 skeleton/troll), structures,
and one boss. The preview gives monsters/boss/hero very high health so the
load survives every warning. It is not an attainable combat-duration or
balance fixture. Real attacks still run, collide and emit their normal events.
The hero walks a changing 5 m target and dashes every two seconds; empowerment
uses the real movement-dash path. The scene samples first occurrences of boss
patterns and nine phases across each traveling front's entire duration.
These are ordered samples from continuous simulation, not a real-time video.

Two maximum legal six-slot builds cover all eight identities:

| Profile | Level-8 weapons |
| --- | --- |
| Storm | Sword, Hammer, Bow, Orbit Blades, Lightning, Ember Field |
| Barrage | Sword, Spear, Bow, Orbit Blades, Sentinel, Ember Field |

Both use max-level Tempo, Multishot, Pierce, Split, Echo and Persistence.
These are slot/level-legal upper bounds, not builds obtained from the ordinary
25–35 choices. Friendly persistent fields and constructs use the live
schedulers, not hand-authored VFX. No summons ship in current encounters;
there is no fictitious summon load. A separate 384-diamond/cross overlay tests
the pickup renderer's cap; it does not inject collectible XP or establish
attraction motion. Real pickup merging/conservation, 256-flight bound and
100,000 stored-cell local-query behavior remain covered by `PickupTests`.

## Measured costs

[Raw six-case report](low-il2cpp.json), 2,526 measured frames per case after
six seconds of warm-up. The sim column times `SimDriver.Step`; presentation
is event drain plus the live presenters. Rendering is a hand-drawn,
single-sampled 1080p target, measured through a one-pixel synchronous GPU
readback. It includes submission and readback latency, and is an **upper
bound**, not a GPU timestamp. The synthetic pickup overlay's CPU submission
is outside the presentation column; its pixels are included in rendering.
The numbers are not added into an invented total-frame measurement.

| Boss/build | Sim p99 ms | Presentation p99 ms | Render submit p99 ms | Synchronized render p99 ms | Peak ranged flights |
| --- | ---: | ---: | ---: | ---: | ---: |
| Ogre / Storm | 2.673 | 1.505 | 1.981 | 7.344 | 176 |
| Conductor / Storm | 2.431 | 1.553 | 1.955 | 7.409 | 176 |
| Dragon / Storm | 2.993 | 1.343 | 1.834 | 6.752 | 176 |
| Ogre / Barrage | 2.190 | 1.085 | 0.826 | 5.235 | 300 |
| Conductor / Barrage | 2.007 | 1.122 | 0.827 | 5.083 | 290 |
| Dragon / Barrage | 2.545 | 1.065 | 0.847 | 5.556 | 298 |

Every case held at least 1,001 alive, and recorded zero presenter faults.
The cumulative event cursor is not retained event memory: `EventDispatch`
discards drained events. Flight counts are observed maxima, not global hard
caps or a proof that every build is bounded by 300. Flight lifetimes, finite
construct replacement and the per-root shared 32-node proc budget prevent
unbounded recursive accumulation. Pickup ownership can grow with explored
world cells by design; attraction does not scan that whole store and rendering
remains capped.

**Budget status:** simulation exceeds the retained 1 ms M5 proxy. The render
upper bound does not establish the 1.5 ms GPU p99 proxy. No 60 fps / M1 claim
is justified by these measurements. The final Warden receives this gap;
operator hardware checks are also recorded in operator-pending. A repeat
queue improvement alone is not represented as solving total simulation cost.
The moving stress runs differ from the initial stationary trial, so their
timings are not a controlled before/after speedup claim.

## Concrete fixes and visual judgment

- Echo batch removal previously shifted every remaining entry for each due
  echo (quadratic work). It now partitions the queue once, preserving order,
  detaches due callbacks and reuses the delivery list. A 512-root mixed-deadline
  regression checks order, exactly-once delivery and deferral of callbacks'
  newly scheduled echoes, without a wall-clock assertion.
- Ground marks previously depth-tested beneath bodies, hiding warning lanes
  in the retained crowd. Thin warning outline/front pixels now composite
  after friendly effects and remain visible through bodies; their dim fill
  remains additive. Shape, timing and collision are unchanged. No new shader
  variant, mesh or vendor dependency was added.
- The 384 near-white pickup outlines competed with the hero. Their linear
  gain was reduced from 1.4 to 0.4, retaining the hollow diamonds/crosses and
  separating them from white attacks and yellow threats.

The first blind critique, made without repository context, found a strong
retro wireframe identity and clear large closed facets, but cyan figures
merged into fine line texture, the white hero took searching to locate, and
pickup diamonds/projectiles competed with it. It saw the red ring as a clear
major danger boundary, while overlapping yellow circles made the immediate
safe area ambiguous. It could not identify an assured escape route through
the invulnerable peak crowd. These findings are retained, not converted to
art approval. Dimmer pickups address one part of that critique; the crowd's
extreme density and player hierarchy remain limits of this fixture.

Private source frames, ordered motion samples and comparison sheets stay at
`~/Library/Caches/playlist-survivors/evidence/ps-3wc.14/` on jarvis. No
vendor-derived captures, bakes or meshes are committed. `verified-low/`
contains the timed six-case set; `motion-low/`, `motion-trailer/` and
`motion-high/` extend captures across full wave durations. Early wall samples
can be outside the view because the wall begins behind the hero's full
walking reach; inspect the later arrival phases too.

The final blind follow-up inspected wall phases 2/3 in all three drawings and
the Dragon wave. It found the red band unmistakable and continuous across
crowd/white projectiles, and its changing position communicated travel.
Pickups competed less strongly, though the repeated pattern still added
texture. It found Low and High essentially alike at game scale; Trailer's
yellow enemies showed more anatomy but busier internal lines, while the
default had cleaner broad facets. The Dragon had more breathing room and a
clear silhouette/wave. It still found the player buried in central white
flashes/projectiles, overlapping yellow circles ambiguous, and safe routes
hard to judge. The traveling front's visibility is improved; this is not
acceptance of player hierarchy or safe-lane readability at peak density.

Author side-by-side inspection agrees: the Low wall remains a solid red
boundary over both drawings, dim pickups no longer look like attacks, and
the default trolls read as large closed angular volumes beside Trailer's
more anatomical detail. The low tier retains dark bodies, connected edge
drawing, quiet grid, beat wave and bloom. The separate [crowd/shatter probe](crowd-low-il2cpp.json) exercises deaths,
which the very tough full-build scenes deliberately do not: 1,000 alive,
20 kills/s, 15 s, synchronized crowd p50/p99 2.412/3.806 ms versus empty
0.779/2.101 ms; these percentiles must not be subtracted as a GPU p99.
No windowed GPU timing samples were available in batch mode. Operator taste, speakers,
normal-play safe lanes and target-Mac performance remain pending.

## Validation

448 .NET tests pass, including the new mixed echo batch case and current
recorded replay hashes. Unity EditMode: 119/119, no skips. IL2CPP player builds
and all six stress runs complete without presenter faults. A clean-cache Mono variant build passes at 279/300 compiled variants.
Enact delivery checks and staging SHA are recorded on the bead. Independent ps-6y2 work and the trailer's monster renderer were
not changed.
