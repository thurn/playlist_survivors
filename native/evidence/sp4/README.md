# SP4 — completed-run cards and actual damage recap

Survivors' VICTORY and YOU FELL cards show active run time, kills, level,
and one row per weapon identity: applied damage, active time held and DPS.
Base and evolved identities stay separate. Rank upgrades preserve the timer;
evolution closes the base's held interval and opens the evolved one. An
already emitted base projectile still credits its captured base identity.
Menus and music freezes do not accrue held time. Zero-held rows have zero
DPS, matching the source's zero-lifetime guard.

`LiveView.recap` is bounded by the ten typed weapon identities and restored
with the view through the existing rollback/cache lifecycle. It consumes
existing Inventory/Run and logical Hit records only. Critical/overkill values
come from actual applied Fate.damage. The delayed EnemyImpact is presentation
and never counted again; the finale blast, healing and recycling do not enter
weapon damage. The UI uses the same ×10 scale as combat labels for damage and
DPS, while the ledger retains raw values. Held time is rounded to0.1s; run
time follows the displayed run clock. Rows stay in deterministic base/evolved
pair order, including owned weapons with zero damage.

The first fatal Health record captures an immutable Summary in LiveView,
restored or retracted with rollback. Confirmation delay cannot add later
combat or held time. At completion, the app keeps only these metrics/rows. Watch-my-run still uses the original song/audio span (including
freezes); stopping a partial replay cannot replace the completed card with
partial statistics. Starting a new run clears the summary. Legacy card
content remains the same.

## Source check

Read-only local primary-source audit (no extracted code or data committed):
`Weapon.cs` SHA2569559f5f9e106bfd367ca6fb42d926fb33c6450418b77bd5974b726ab58296e41
and `RecapPage.cs` SHA256f8c90565594be5c73fcf3a95ee23753b6390b57b4544241e2dc304a1010d6b07.
The recap reads accumulated inflicted damage, weapon lifetime and damage /
lifetime, guarded at zero. The demo maps lifetime to its explicit held run
clock and keeps its mandated human-facing damage scale.

## Kept checks and captures

`recap-tests.txt` compares the projection against an independent actual Sim
LogRecorder producer:1695 applied enemy fates across all five evolutions,
including a pause; held run17.491666667 s. Durable fast XP-free actors have
zero knockback, the hero is damage-exempt, and all five evolutions are set up
through actual APIs at the start. This is an attribution contract fixture,
not a source-balance or natural-acquisition claim. A separate record test
checks rank upgrades, replacement, lingering-base damage, critical values,
long-menu time, zero-time DPS, and one logical hit despite its visual echo.

`live-tests.txt` exercises the real late-offer-pick rollback and compares
both the entire recorded sequence and final recap; the existing full-view
reference assertions include recap state at each frame. `clippy.txt` keeps
focused lint. `check-fast.txt` keeps the final diff-aware check. A required
single cold warden review covers the live-view/rollback projection changes;
its disposition is kept in `cold-review.md` and native bead notes.

From `native/`:

```
cargo run --release -p app -- --recap-check evidence/sp4
```

`victory.png`, `fell.png` and `cards.jsonl` come from actual endings and
actual combat/ownership records on a synthetic120BPM stream. Rank8 bases are
injected initially and actual API evolutions occur after8 active seconds;
XP is suppressed and skeleton HP1000/knockback0. Victory is shortened to20s
and exempts hero damage; its actual finale supplies the ending. The defeat
case uses speed1200/power10000 to ensure an actual fatal contact. Both hold a
manual pause from song4 until its beat resume after6. These are labelled
presentation fixtures, not natural progression, source combat balance or
full-run survival. Held times/damage/DPS in the raw trace are unscaled; card
formatting applies the combat ×10 convention. The ten-row layout is checked
in both outcomes.

The fatal-boundary regression uses rank8 Sword and fast durable XP-free
skeletons through a pause. Settlement windows0.04/0.4 at lead0.237 preserve
exactly run0.7 and damage56.2962962963, although the later display clocks
are0.7416667/1.1083333 and raw later damage reaches115.5555556.

Human card readability and W/replay feel go in operator-pending. SP1 keeps
its evolution art/bake ownership. SP5 owns the milestone review, dense
readability/budget and final scale measurements; no bake or analyzer changes
here.
