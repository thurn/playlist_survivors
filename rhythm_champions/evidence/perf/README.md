# Performance at 1,000 alive — ps-6y2.22

2026-11-03, jarvis (Apple M5 Max), Master IL2CPP player, Metal, 1920×1080,
headless, on a shared host (other sessions' Unity work ran on it). This is the
proxy for the target ([plan.md, Performance](../../../docs/champions/plan.md#performance)):
the low tier on an M1 Air class Mac holding 1,000 monsters at 60 fps, measured
on the M5 Max until an M1-class run replaces it (operator-pending).

## Reproduce

```sh
uv run --project rhythm_champions/tools champions setup
uv run --project rhythm_champions/tools champions perf
```

`champions perf` builds the IL2CPP player (about 5 minutes), plays the peak
once per tier (`--tier low` for one) and writes `TIER.json` and `perf.json`
here (`--out DIR` elsewhere). The GPU's numbers need Xcode (`xcrun xctrace`);
without it the CPU's and the frame's still come. It never runs in CI and exits
1 when a budget breaks.

## The peak

`--perf-peak` (`Client/Bench/PerfPeak.cs`): level 1's sim and every presenter
of a run (`RunPresentation`: the world and its beat wave, structures, crowd and
shatter, telegraphs, spores, pickups, hero, weapon effects and flights, the run
UI) on a loud scripted 128 BPM song, held at the horde's cap:

- the spawner's target at the cap, 1,000 alive, with the second half's mix
  (every mob type) from the start; no bosses (ps-3wc's `full-*` scenes cover
  them), no level-ups, a hero who can't die;
- ps-3wc.14's heaviest build, Storm: Sword, Hammer, Bow, Orbit Blades,
  Lightning and Ember Field at level 8, Tempo, Multishot, Pierce, Split, Echo
  and Persistence at their most; its fixture's hero walking a 5 m circle and
  dashing every 2 s;
- toughness 100, so the horde presses in round the hero: about a third of it
  on screen (312 p50; the game's spacing fills the screen at that), 16.5 kills
  a second shattering and dropping pickups, ~285 flights in the air. The
  level's own toughness at the cap (3.5) has the build clear the hero's
  surroundings to a sixth on screen; this is the heavier case for the GPU and
  the sim alike.

## Results

From this folder's JSONs. GPU "alone": frames the GPU was the game's alone
(see below); "all": every frame, the window server's compositing included.

| Tier | Frame p50 / p99 ms | CPU p50 / p99 ms | Sim p50 / p99 ms | GPU alone p50 / p99 ms (frames) | GPU all p99 ms | Shared |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Low | 1.206 / 2.596 | 1.066 / 2.321 | 0.035 / 0.973 | 0.933 / 1.157 (1,654) | 2.147 | 30% |
| Medium | 1.191 / 2.484 | 1.009 / 2.213 | 0.033 / 0.969 | 0.958 / 1.212 (1,994) | 1.944 | 15% |
| High | 1.202 / 2.592 | 1.055 / 2.336 | 0.034 / 0.965 | 0.970 / 1.377 (1,831) | 1.947 | 17% |

Two more low-tier runs: GPU alone 0.876 / 1.201 and 0.879 / 1.323 ms, sim
p99 0.977 and 0.968 ms. Shader variants: 289 of 300, unchanged.

**Budgets** (on this machine):

- Low tier GPU p99 at most 1.5 ms: **kept**, 1.157 ms (1.20 and 1.32 in the
  repeats).
- Low tier sim p99 at most 1 ms: **kept, with almost no headroom**: 0.973 ms
  (0.977, 0.968); a busier moment of the shared host can push one run past it.
- Every tier at 60 fps (frame p99 at most 16.7 ms): **kept**, 2.5-2.6 ms
  (about 385 fps; frames run pipelined and uncapped, as fast as they come).

The high tier's 4x MSAA and wider bloom cost little on Apple's tile-based GPU
(about 0.04 ms at p50).

## How the GPU is measured

The other benches here (`--crowd-bench`, ps-3wc's `FullBuildProbe`) time a
frame through a synchronous 1-pixel readback. On this machine that misleads, as
traces of those loops showed:

- **Clocks.** Waiting on every frame leaves the GPU idle half the loop, and
  Apple's GPU lowers its clocks for it. On the old peak a synchronized frame's
  GPU wait was 1.6 ms p50; rendering the same frame eight times more each
  frame, to keep the GPU busy, brought it to 1.0 ms; pipelined, its traced GPU
  time was 0.76 ms.
- **It isn't the GPU's time.** The wait after `Camera.Render()` returns holds
  the render thread's encoding and the GPU's wake-up too.
- **The compositor.** Its p99 spikes came on every third frame or so: the
  window server's compositing time-slices the GPU (0.54 ms of its work in a
  slow frame against 0.02 ms in a fast one), and every pass in a shared frame
  runs 3-5 times slower. The game will share the GPU with it in play too; the
  budget is about the game's own cost.

So the peak runs its frames pipelined, as the game does, and asks for a
1-pixel readback it never waits on after each camera. `champions perf` records
a 4-second Metal System Trace once the peak measures and reads each frame's GPU
work: the player's command buffers up to the end of the one that ends with
that readback (Unity sometimes commits a camera's passes in two). A frame in
which another process's work ran on the GPU is counted apart: "alone" against
"all" above.

## Hotspots found and fixed

**Weapon pieces drawn one by one.** The melee, magic and construct visuals
(every sword, hammer, lightning link and node, ember piece and orbiting
blade), the flights and the XP pickups each queued one `Graphics.RenderMesh`
per piece with its own property block: hundreds of draws a frame at the peak.
Splitting the main pass by presenter (each switched off in turn, a development
build's trace) put the weapon effects at 0.36 ms of the low tier's 1.08 ms main
pass and 1.15 ms of the CPU's 1.88 ms submission; flights and pickups 0.06 ms
GPU each. `Render/Effects/PieceBatch.cs` now packs each frame's pieces into one
buffer and draws them as one instanced draw per mesh and submesh; MeleeEdge,
RangedFacet and Pickup read their piece by instance (`Shaders/Pieces.hlsl`,
no new variants). Low tier, same peak:

| | Before | After |
| --- | ---: | ---: |
| GPU alone p50 / p99 ms | 1.129 / 1.763 | 0.933 / 1.157 |
| Frame p50 / p99 ms | 2.885 / 6.001 | 1.206 / 2.596 |
| Presenters p50 / p99 ms | 0.863 / 1.798 | 0.466 / 0.987 |
| Submission p50 / p99 ms | 1.720 / 3.931 | 0.540 / 1.032 |

Nothing looks different: 643 of 648 stills of the sword-feel, magic-combat,
gallery and full-conductor-storm scenes (melee, magic, constructs, flights and
the pickup overlay) are identical pixel for pixel; five differ in at most 51
pixels by at most 3 levels (one 3-pixel spot by 16), less than two runs of the
same build differ (up to 463 pixels by 7). Opaque, depth-tested pieces drawn
in another order change only exact depth ties.

**The peak's own target** first carried no MSAA, and URP draws an offscreen
camera with its target's samples, so every tier drew unsampled. It now takes
the tier's MSAA and resolves, as the stills do.

## Where the frame goes now

Low tier, development build, frames the GPU had alone (p50 0.93, p99 1.30 ms),
per frame: the main pass 0.75 ms (monsters and shards about 0.5 of it), the
grade's halo and URP's post 0.11 ms, bloom 0.11 ms, the crowd's cull and
palette compute 0.06 ms, the rest under 0.03 each.

The sim's p99 frames are the horde's step on the beat (all ~975 mobs that step
then, 0.6-0.7 ms: neighbour queries, gap pushes and slides) with Echo's
repeated strikes (0.16 ms) and the weapons' own (0.06 ms) on the same tick.
ps-3wc.17 and .19 already made the step's exact optimizations this bead had
made too (neighbours gathered once, slides limited to the bodies a move can
reach) and more; no further exact win was found, so the sim keeps its thin
margin here.

## Investigated, not kept

- The grade's halo at half its rows on the low tier, as its bloom does: it
  saved about 0.01 ms (0.110 against the high tier's 0.120 ms for those
  passes), too little for a visible change on the low tier.
- Master IL2CPP: staging's build policy already compiles it so.

## Not established

- An M1-class Mac: the operator-pending row asks for this run there; its
  numbers replace the proxy (and its 8x assumption).
- The shared host: other sessions' work and the GPU's clock state were not
  controlled; repeats are quoted above.
- Bosses: the peak is the horde's; ps-3wc's `full-*` scenes measure the
  bosses' CPU cost, their GPU is not traced.
