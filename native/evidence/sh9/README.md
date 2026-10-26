# SH9 — The six passives

The SF4/SF7 foundations already implement and offer all six passives with their
source deltas, rank caps, icons and evolution partners. SH9 verifies every rank
through the real card-input path and makes two card lines more precise:
Hollow Heart heals the added maximum HP, and Duplicator adds a projectile/spin.
The authored offer table and all stat/gameplay rules are unchanged.

## Source-rank proof

The authority is `docs/survivors/appendix-feel.md`, The six passives, and
`docs/survivors/progression.md`, How stats combine. `passive-ranks.txt` records
27 real card selections and compares all stat fields with literal source totals:

| Passive | Totals after each rank |
|---|---|
| Spinach | Might 1.1, 1.2, 1.3, 1.4, 1.5 |
| Hollow Heart | Max HP 144, 172.8, 207.36, 248.832, 298.5984 |
| Pummarola | Recovery 0.2, 0.4, 0.6, 0.8, 1.0 HP/s |
| Empty Tome | Cooldown 0.92, 0.84, 0.76, 0.68, 0.60 |
| Spellbinder | Duration 1.1, 1.2, 1.3, 1.4, 1.5 |
| Duplicator | Amount bonus 1, 2 |

The fixture starts at 80 HP and maxes the other items through their real stat
paths, leaving only the passive under test eligible in the unchanged table.
It supplies enough XP to chain its ranks, presses Confirm after each third
reveal, and checks inventory rank and all 15 stats. World time stays frozen
through the chain: only Hollow Heart heals, ending at 258.5984 HP from 80.
An attempted rank beyond the cap is rejected without changing stats. This is
an isolated progression fixture, not pacing, combat balance or survival evidence.

`offers.txt` keeps six tests: card descriptions/caps/typed IDs, rejection of
legacy `max-health`, `magnet`, `might`, `armour`, existing table milestones,
maxed fallback and evolution ownership. Only this demo's six typed passive IDs
can be offered. Existing engine tests separately cover active-world recovery,
freeze holding it and added-maximum healing. Weapon consumers already read
finalized Might/Cooldown/Duration/Amount; no parallel stat layer was added.

## Presentation and playable checks

`passives-1.png` and `passives-2.png` show all six source card lines and the six
HUD icons with rank pips, using the real card/HUD renderer and baked licensed
icon atlas. This is a labelled synthetic rank-one inventory; it also includes
all five base weapons so the five true evolution partners show their marks,
while Spinach has none. It makes no claim of owning unbuilt weapons in a normal
run. `capture.txt` prints all card lines and checks that every icon exists.
No asset bake or new image source was needed. Captures are silent and offscreen.
`card-view-tests.txt` keeps six existing real-renderer/card-state cases, including
readability at the confirmation beat and correct evolution partner marks.

`headless.txt` keeps a 15-second normal-damage File-mode check: 69 sounds,
zero late skips/drops and a real first-card Garlic selection/resume. It is a
short playable smoke check, not a passive acquisition or balance measurement.
The native bead comment records final `scripts/check-fast` and Tollgate delivery.
No per-bead cold review is required by the Survivors policy: no rollback runner
is changed; SH11 reviews the H milestone using this bead's recorded source SHA.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 15 --pick-first
cargo run --release -p app -- --headless --passives-check /tmp/passives-check
```

SW1/SW2 consume the existing finalized Duration/Amount and raw Cooldown sum.
SW5 consumes ownership of the evolution partner, not maximum passive rank.
Hollow Heart's heal is the gained maximum; Pummarola remains active-world
HP/s, and SH8's fixed 30 HP chicken remains fixed. Eye/feel checks are in
`native/evidence/operator-pending.md`, without an approval or playtest pause.
