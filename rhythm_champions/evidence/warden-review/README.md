# Survivor-combat final review — ps-3wc.15

2026-11-03, jarvis / Apple M5 Max. Reviewed staging 83a883e7 and all fourteen
implementation-bead handoffs against current design, contracts, source mapping,
current code, tests and private runtime galleries. This is the light run's one
Warden, with direct repairs and essential remaining bugs under ps-3wc.

## Confirmed findings and repairs

- Sentinel shifted its lifetime across a hold but not its replacement cooldown.
  A long card/pause could replace the roster immediately, losing Persistence's
  intended uptime/cooldown relation. Its delayed Echo also used the pre-hold
  visual expiry. Both deadlines now retain elapsed combat time. The regression
  pauses ten seconds mid-roster, checks the delayed Echo, the original expiry,
  the inactive cooldown window and the subsequent replacement.
- Sentinel and melee presentation expired while cards/pause held the sim.
  Their poses now freeze and retain their remaining life on resume, through the
  existing effect hold command. Unity regressions exercise the public effect
  APIs through holds, resume and expiry.
- Ember Field's unconditional target predicate spent empowerment and fractional
  strike progress on empty ground. It now checks its actual next-pulse shapes,
  using its existing anchor while alive and the hero's position on replacement.
  The same shape builder drives collision and eligibility. All four forms now
  retain the flag/carry until a target enters, including empowered-only reach.
  The placed field is not dragged to the moving hero. The existing recorded
  attacks fixture loses 122 empty-ground events and was deliberately refreshed
  to hash 310EFE1CEBCA2381; inputs and music remain the recorded experiment.
- Melee crowd targeting repeatedly evaluated each body's angular width and
  inverse trig for every candidate direction. Widths are now computed once;
  lane scoring uses dot products, with the original geometry at floating-point
  boundaries. The policy still chooses the most bodies, then the nearest lane.
  Dense mixed-radius behavioral cases compare the resulting hits with direct
  geometry scoring. Pairwise scoring remains quadratic; this is a local cost
  repair, not a claim that the peak simulation budget is met.
- Simultaneous weapon hits whitened large parts of the retained crowd, competing
  with the white hero. Default body hit feedback now retains each enemy's hue.
  The trailer mode, pinned shaders, meshes, body gains and shards are unchanged.

## Review coverage and visual judgement

Reviewed six-slot/cap/form validation, additive support families, shared
per-strike ledgers, bounded Split/Echo trees, fractional scheduler credit,
nonstacking per-weapon empowerment, destination-only optional Landing Blast,
XP value persistence/local lookup, immediate offer queues and key release,
boss spawn/clock/retained-field rules and resumed telegraph deadlines. Existing
behavioral tests exercise these rules; no unrelated refactor or test churn.
The full-run victory/death/retry/replay cases still pass after the repairs.

The manifest and local bakers keep owned meshes private; magic uses the
explicit authored-facet fallback rather than calling unvalidated particle
prefabs ready. Setup resolved the local sources. No purchase, cross-epic edge,
ps-6y2 status change, vendor mesh or derived capture is introduced here.

Directly inspected the eight families' normal/empowered form sheets, plus
ordered Sentinel and melee phases and the existing minimal cards/HUD.
Sword cross/spin/dance, Hammer slam/eruption/falls, Spear ranks, Bow battery,
Orbit crown, Sentinel formation and the magic constructs have distinct
composition; single arrows and early spear heads intentionally remain thin.
These galleries use staged crowds, not proof of complete-build readability.

Reran all six full-build IL2CPP Low stress scenes with 1,001 alive, six max
weapons/supports, structures and the 384-pickup render overlay. Inspected the
new Conductor wall travel/motion and Dragon wave samples against the prior
ps-3wc.14 captures. Hue-preserving hits remove the pale crowd wash and keep
cyan/orange bodies distinct from white weapon edges. The traveling red wall
is still continuous and unmistakable over the crowd. Closed facets, dark
cores and the quiet grid survive. However the central hero still takes
searching in some phases, long white constructs cross it, and overlapping
yellow warning circles do not offer an immediately readable route. This is
an essential remaining visual bug, not art acceptance or merely operator taste.
The invulnerable stress crowd also cannot establish attainable normal-play
safe routes. No hand-play, speaker, real-time-video or M1 claim is made.

Private ordered frames/ID passes and view metadata:
`~/Library/Caches/playlist-survivors/evidence/ps-3wc.15/full-low/`.
Reproduce using the full-build README's six-scene IL2CPP command, changing
only the output directory. Source captures remain private.

## Performance remains unresolved

[Raw report](low-il2cpp.json). Each case has 2,526 measured frames after warmup,
zero presenter faults and at least 1,001 alive. Scene/setup and measurement
limits are unchanged from [full-build](../full-build/README.md). These were
run on a shared host, partly beside Unity checks; they are not a controlled
speedup comparison. In particular the synchronized render includes CPU
submission/readback and cannot establish GPU p99.

| Boss/build | Sim p99 ms | Presentation p99 ms | Synchronized render p99 ms |
| --- | ---: | ---: | ---: |
| Ogre / Storm | 2.638 | 1.430 | 7.063 |
| Conductor / Storm | 2.330 | 1.479 | 6.966 |
| Dragon / Storm | 2.929 | 1.335 | 6.820 |
| Ogre / Barrage | 2.317 | 1.717 | 11.042 |
| Conductor / Barrage | 1.970 | 1.228 | 5.926 |
| Dragon / Barrage | 2.547 | 1.097 | 5.441 |

The 1 ms simulation proxy is still exceeded. GPU-only evidence against the
1.5 ms proxy is still missing. Larger profiling/render work and the remaining
hero/threat hierarchy fix require essential Executor follow-ups under this
epic. They do not gate or repurpose independent ps-6y2 work. Performance
sweeps remain explicit opt-ins outside CI, and have no timing assertions.

## Verification

121/121 Unity EditMode tests pass without skips. IL2CPP builds and the six
stress runs succeed. Clean-cache Mono variant check: 279/300. Simulation
regressions and all four recorded replays run in the required checks; delivery
fast/slow results and follow-up bead IDs are recorded on ps-3wc.15.
First-choice route variance, long Dragon fights and broad-build balance remain
honestly recorded in assembly evidence/operator-pending; this review does not
convert those scripted tuning samples into manual balance approval.
