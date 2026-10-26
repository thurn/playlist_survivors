# SF4 — VS stats, XP and gems

Sources are the numerical rules in `docs/survivors/progression.md`,
`appendix-xp-and-leveling.md`, `appendix-feel.md` and the enemy table in
`appendix-damage-vs-health.md`. No private extracted files were used or added.

## Fixed choices and scope

- The single conversion is **154.26997245179064 display px / Unity U**:
  `140 / (0.825 * 1.1)`. Equivalently 1.5426997245 display px per VS legacy
  physics pixel. Hero 140 px/s; BAT1 54.88; scalar-100 enemy 39.2;
  magnet 46.28099173553719 px. Forest's shared 1.1 modifier preserves ratios.
  Musical gaits remain; music's calm no longer reduces survivor base speed.
- Antonio begins with 120 HP and 1 Armor. Contact power is multiplied by the
  level's 0.5 before subtracting Armor, with a 1 HP floor. Damage lockout is
  120 ms. No Antonio level-based Might or meta power-ups.
- Stat layers add; each Hollow Heart rank multiplies current maximum by 1.2
  and heals the added maximum. Read-time clamps preserve the underlying sums.
  Cooldown/Amount/Area/Duration/projectile Speed are ready for SF5's framework;
  the remaining non-demo stats exist at base. Recovery advances on fixed world
  steps, never on presentation/frame cuts or during a freeze.
- Living recoil is 120 ms of reverse base movement at enemy susceptibility ×
  weapon knockback. It starts next sixteenth; without committed grid ahead,
  the next quarter-free-pulse is used. There is no invented `(1-resistance)`
  equation: SF1 documents susceptibility and the `res_Knockback`/ResDebuffs
  source quirk. Zero susceptibility is immune. No fixed-distance shove.
- XP totals keep formula fractions where the appendix's summary table rounds
  them: level21=2866.5, 39=9886.5. Growth adds +1 at 20/40 to current Growth.
  A gem's Growth is evaluated before its thresholds resolve. XP is cumulative,
  and `pending_levels` preserves every earned level for SF7's card chain.
  SF7 owns opening/confirming offers; SF4 introduces no temporary offers.
- Gems use seeded per-enemy random values, skip zero drops, and carry value
  into the view for blue1–2/green3–9/red10+ selection. At 400 ground gems,
  new XP merges into the one farthest from the hero, even if the death was
  near another gem. SF10 must retain logical enemy identity in RNG seeds if
  it reuses arena slots.
- Enemy metadata accepts source speed/power/XP/knockback independently of art.
  Existing art is temporarily cast into source roles; HP and population stay
  the prototype until SF6 transcribes the full schedule. Chicken and offer
  knobs are untouched. Hitstop, dips and rate scaling are off in survivors.
  Reactive/legacy paths keep their existing behavior.

## Checks

From `native/` (raw output beside this file):

- `cargo test -p sim --test survivor_stats -- --nocapture`: five tests, exact
  source thresholds/clamps/compounding/ratios/colors. 20,000 seeded xp=1 deaths
  yield 9,949 zeros, 10,051 XP, mean0.50255 (source expectation0.5).
- `cargo test -p sim --lib survivor_ -- --nocapture`: four integrated cases:
  damage and lockout; Hollow Heart heals only added maximum; recovery held by
  freeze with equal recordings across frame cuts; 400 cap/farthest merge and
  pending XP levels; zero drops and grid-timed/immune recoil.
- `cargo test -p sim --test survivors -- --nocapture`: all 15 freeze/clock cases.
- `cargo test -p eval --test live a_growing -- --nocapture`: growing gem values
  reach the view/color selection; existing growing-stream play check passes.
- `cargo clippy -p sim -p eval -p app --all-targets -- -D warnings`: clean.
- `cargo test -p eval --test session -- --nocapture`: both stored legacy sessions
  retain their exact hashes; unknown key parsing still fails. Gem value is an
  additive Survivors-only record, preserving the legacy record format.
- Final `scripts/check-fast` from the worktree root, retained in `check-fast.txt`.

No analyzer code, CI scripts, Tollgate policy or shared infrastructure changed,
so analysis corpus parity and local ci-full are not required. SF4 gets the
milestone review at SF10; the eval change carries gem value/level in view
records and does not change the rollback runner.

## Playable headless session

```
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions \
  target/release/playlist-survivors --file ../public/reactive/house-128/house-128.ogg \
  --delayed --headless --seconds 20
target/release/ps replay /Users/dthurn/playlist_survivors/native/logs/sessions/1792993501
```

Session 1792993501 ran 14.2 game seconds (song2.8–17.0) inside the 20s process.
All 21,307 settled records matched replay (24,465 total replay records including
later tail); 36 kills, full HP. Live timing reports four stream-arrival rollbacks,
no late/dropped sounds and no late keys. This is a short AFK foundation smoke,
not a survival gate, a pace target or a 30-minute soak. No window was opened.
Operator speed/pickup/damage feel is recorded in operator-pending.
