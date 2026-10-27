# Restoration qualification — CI follow-up

The FR1 release sweep (`01a17df6-f1eb-76c1-9c19-1f91daba410d`, staging
`47a5e91ff7ff44b0ff63709d98b4352aa8bbadd1`) failed one comfort check:
`cached-rotate-positioning-3` survived 1800 seconds but reached 0.177 minimum
HP share, below the unchanged >0.25 threshold. Its complete retained step output
is `prior-release.txt`. The dependent replay step was skipped.

FR2's deliberately restored 190 px reach and full per-hit power fixed that exact
case without changing enemy HP, contact damage, food, recovery or progression:
`rotate-positioning-3.json` survives 1800 seconds at minimum HP share 1.0,
level 58 and 15648 kills. The original-order seed1 AFK control still falls, at
606.150 seconds, level 2 and 3681 kills (`original-afk-1.json`). These are actual
normal-damage cached-track reports on the production playlist, with no injected
loadout. They establish these cases, not aesthetic feel or all build paths.

The operator clarified during implementation that enforced minimum difficulty,
including mandatory losses while standing still, is secondary to feel. Therefore
AFK runs still execute and retain their full results, but have no release verdict.
The active positioning-player requirement is unchanged: completed 30 minutes,
no fall, minimum HP share >0.25. No production balance knob or performance gate
was changed. Tests cover AFK controls at 599.999, 600, 606.15 and 1800 seconds,
and preserve the active player's boundary checks.

Commands from `native/` (the CLI enters background QoS):

```
target/release/ps survive --bot positioning --seed 3 --order rotate --minutes 30 --jobs 1
target/release/ps survive --bot afk --seed 1 --order original --minutes 30 --jobs 1
```

The full required local CI run covers the changed CI policy and test tooling;
Tollgate gate promotion remains the delivery boundary. This does not qualify
FR3–FR10 or claim speaker/System checks.

Full local `scripts/ci-full` passed, including all workspace checks, actual-track
analysis parity, existing performance gates, all 41 survival reports with zero
active-player failures, and the 30-minute exact replay (21253958 records;
hash 921583d96b6e703b). `ci-full.txt` retains the complete output;
`matrix-summary.json` retains each case's survival, minimum HP, level, kills
and settled hash. AFK times remain visible, including runs beyond ten minutes.
