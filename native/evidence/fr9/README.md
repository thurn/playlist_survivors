# FR9 — Presence, reactions and aftermath

The starting hero has a blue aura independent of zen input grades. Actual weapon
and passive ranks strengthen it; replacing a rank8 base with an evolution keeps
that growth, and each of the five owned evolutions adds its own colored layer.

Dense reactions reserve nearby hit/death cells independently of distant activity.
Nearby particles retain their size/count, with eight closest body flashes and
protected bosses; rapid hits leave a visible hold instead of continuous white.
The primary sword, wave fronts and signature effects retain their own presentation.
Ordinary per-victim lights/shakes are bounded and duplicate broad sword arcs
recede at density. Damage labels keep their actual sums while sharing screen
space. No damage, reach, HP, immunity, XP or population changes.

Ordinary catalog death decals now come from the rollbackable live view: at most
96 marks, coalesced in32px cells within1 world second, fading over20 world seconds.
Menus hold that clock, and discarded deaths lose their marks. Boss death recipes
remain protected. Full death particles are short lived; only floor decals persist.

## Kept checks and limits

- focused-all.txt: complete app/eval suites; focused clippy/fx/flash/aura outputs.
- aftermath-tests.txt: actual Rust3m timestamped excerpt through an explicitly
  injected evolved300-foe/40HP/zeroXP fixture,30ms late movement and237ms lead.
  193 actual kills,96 marks,29 key rollbacks; equal recording and current view.
  Separate journal tests cover cap, held menu clock, expiry and retracted deaths.
- aura/: clearly labelled displayed legal inventory states (opening, growing
  unevolved, max bases,1–5 evolutions). These do not demonstrate acquisition.
- dense/ and restored/: real House timestamped stream through explicitly injected
  max builds,300 actors/up to400 gems, synthetic HP, hero damage exemption.
  Death case uses40HP/zeroXP. The isolation still hides actors and other effects
  to show actual view-owned marks. These are silent diagnostics, not balance runs.
- bench-combat.txt:1080p,7200 frames with real kills and the bounded aftermath pool.
  bench-restored.txt: actual six-slot Chain/Trail build and moving gem pickups.
  Existing4ms p99 threshold stays unchanged; local runs are not general guarantees.
- ci-full.txt and check-fast.txt retain final repository checks. FR10 summarizes
  integrated30-minute survival/replay, both modes and real mixed PCM.

Playable from native/: `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed`; add `--legacy` for judged play.
Reproduce visuals with `--aura-check DIR`, `--density-check DIR`,
`--restored-check DIR`; performance with `--bench-combat --check` and
`--bench-restored --check`. Human feel/speaker/System checks remain explicitly
pending in operator-pending.md. Dense peak frames remain intentionally intense.

Cold review found two confirmed presentation issues, fixed with regressions:
weapon catch recipes now share the hit budget rather than suppressing a same-cell
death; only catalog kinds with an actual Decal layer enter the aftermath pool,
so flyer deaths cannot evict or suppress ground marks. No confirmation review.

Final corrected app/eval suites, including release-only HUD/cards/real-mix tests,
pass in final-app-eval.txt (87 app,16 eval unit,24 live,4 session,4 survive).
Final combined-source check-fast passed21s. Corrected-source1080p actual-kill benchmark:
2093 kills,96 peak marks,300 peak actors,400 gems, p99 3.05ms/max3.80ms.
Restored Chain/Trail/moving-pickup benchmark p99 2.99ms/max3.86ms.
These retain the existing4ms p99 bar. The full suite began before the two review
fixes; final-app-eval and check-fast explicitly recheck the corrected source.
The later full matrix/replay build uses that corrected source.

The first local full suite was deliberately stopped during parity after FR8's
long-run abandoned-slam stall was diagnosed; ci-full-interrupted.txt is partial,
not a pass. Repair a2d6e37 is integrated before final check-fast and the fresh
complete full suite. That repair has its own exact30-minute cases and cold
review in fr-ci15. The final full suite output is kept as ci-full.txt.

Final clean-source scripts/ci-full passed all sections. The41-case full-run
matrix completed806.473s with zero survival failures: all20 active cases
survived1800s, minimum HP share0.416667. AFK remains diagnostic. The random
legal-choice run survived with max Chain Lightning/Fire Trail and three
evolutions (level80, minimum HP0.792). Exact30-minute replay passed22,737,077
records/hashd381e4ea7f83b0e8 in76.536s. Raw reports/commands and summaries are
retained here. Normal1080p p991.42ms; constant300-actor density p993.10ms,
unchanged4ms threshold. Final-source native suites pass after the separately
reviewed signature cancellation repair; no shipping source changed after this
qualification. FR10 packages mode, listening and comparison evidence.
