# SH4 — Garlic

Garlic is now a selectable weapon in the existing first XP offer. The offer
preferences and easy balance knobs are unchanged; its implemented flag enables
what the plan already listed.

## Combat tied to the source

`docs/survivors/appendix-feel.md`, Garlic runtime notes and table, supply the
rules. A persistent hero-centred aura damages each target once per incarnation,
including targets entering between pulses. Its next incarnation starts on the
beat-snapped raw weapon interval. Generic hitDelay 1,000, duration 1,300, Amount and
speed do not control its repeated collisions, as the source appendix explains.
The aura and target memory live in cloned SimState, along with mutable per-foe
knockback susceptibility, so a rollback restores them.

| Rank | Power (internal) | Area | Raw interval s | Radius in app px |
|---|---|---|---|---|
| 1 | 0.5 | 1 | 1.3 | 49.366391 |
| 8 | 1.5 | 2 | 1.0 | 98.732782 |

Radius is `32 legacy px * 0.01 U/legacy px * PX_PER_VS_UNIT * Area`, using the
same conversion as movement and pickups. Body-circle overlap includes the target
radius; this is not a claim that 32 legacy pixels are 32 screen pixels.
`garlic-tests.txt` keeps three source-path tests: both ranks hit two simultaneous
inside targets and a later entrant once per cycle while missing an outside
target; an aura follows a moving hero without a new shot; and surviving hits
increase eligible knockback susceptibility by 0.3 only while below max. The source
checks before adding, so 2.9 can become 3.2. ResDebuffs>=1 blocks it. Garlic's own
knockback is 0: the tests confirm no direct recoil and a later sword hit uses the
increased susceptibility. The current roster has no freeze attack, so no
freeze-resistance effect is exercised or claimed.

At 174 BPM with rank 8 and cooldown 0.6, hits at 0, 0.689655, 1.379310 demonstrate that
the generic 1,000 ms hit delay does not throttle new aura incarnations. These are
controlled target tests, not integrated survival or pacing measurements.
`table-and-offer-tests.txt` also checks all 45 weapon ranks/450 fields against the
kept source appendix and confirms Garlic is offered first while unbuilt weapons
still skip. No raw extracted game data is included.

## Presentation and sound

The aura draws behind the actors, follows the presented hero and pulses on kicks.
Accent pulses brighten it. `render::post::Grade::aura` is a bounded local haze
lens (centre/radius/strength), with DPI scaling and the HUD composed afterwards.
`haze-test.txt` and `render-tests.txt` (all 11 pass) use known stripe geometry to
show distortion inside the lens, no change outside it and an unchanged overlay.
`normal.png`, `accent.png` and `aura-and-accent.mp4` were rendered offscreen and
visually inspected. This four-second 128 BPM fixture injects an initial rank 1
Garlic loadout, uses lead 0 and synthetic committed beats/forecast, disables
enemies in memory and hides sword FX to isolate the aura. It is silent and is
not progression, rollback, survival or listening evidence. `--sword-check` keeps
its original behavior through the shared `weapons_check` helper.

Normal/accent fire and contact cues use existing baked licensed clips; no bake
or asset intake was needed. Typed `Cause::WeaponPulse {s,x,what,accent}` projects
from existing WeaponFire moments, replacing the old generic sound projection
for Garlic. Typed WeaponImpact now includes Garlic's real grid reactions.
The old journal records/hash encoding remain unchanged. SH2's scheduler owns
exact onsets, retractions, coalescing, category caps and the non-ducking mix.
`sound-test.txt` decodes all four clips, checks exact onsets and cancels pending
voices after retraction. Listening remains an operator row.

## Live and rollback checks

`live-and-session-tests.txt`:17 live and 3 session tests pass. A real XP choice
now learns Garlic, and the late-confirm run settles to the same records and
inventory as the on-time run. New cause assertions tie pulse/contact sounds to
actual journal moments and verify there are no new sound hash records.
`headless.txt` is a 35-second normal-damage File session with `--pick-first`:
first offer selects Garlic, resumes, 173 sounds start with zero late skips/drops,
frame-work p99 below 1 ms. `replay.txt` matches its 36,147 live settled records,
55 kills and full health. This short run is not a survival gate or listening
verdict. `bench.txt` passes the default CPU frame-work check at 1.27 ms p99
(4 ms limit); that fixture does not acquire Garlic, so this is not dense-aura or
GPU timing evidence. Cold runner review found no actionable issues and reran
five focused tests. The initial aggregate check found three old card tests that
assumed only Sword existed (`check-fast-initial.txt`). Their local fixtures now
max every implemented base weapon and verify the selected card/inventory instead
of a hardcoded Sword rank/card position. All22 focused Survivor tests pass in
`card-fixture-tests.txt`; gameplay is unchanged by this test-only repair.
Final fast-check/source SHA are recorded in bead notes.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 35 --pick-first
cargo run --release -p app -- --headless --garlic-check /tmp/garlic-check
```

## Handoffs

SH5 can project its typed pulses and impacts through the same scheduling path.
SW5 must replace/clear GarlicAura when Soul Eater replaces the weapon, provide
its own damage/heal behavior and palette, and adapt the presenter identity.
SH11 owns integrated crowd balance/feel; SP5 owns density/GPU budgets. Source SHA
is recorded after commit in the bead notes for milestone review.
