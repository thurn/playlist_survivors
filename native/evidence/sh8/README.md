# SH8 — Gems and floor food

Survivors gems use their source value bands: blue 1–2, green 3–9, red 10+.
Sizes rise within a band, the red gem uses the larger glyph and its size is
bounded even after cap merging. Compact beat glows share their energy within
24 px cells; higher-value gems draw last and the field draws below actors.
Floor chicken uses three small hand-authored pixel masks in the runtime atlas.
No bake, external art or offer/balance change was needed.

## Geometry and collection

The kept source is `docs/survivors/appendix-feel.md`, Gem attraction and Chicken
pickup immunity. The common 154.26997245 px/U scale gives magnet radius
46.280992 px and gem body radius 15.426997 px. Magnet stat modifiers apply only
to the magnet. At overlap a gem leaves the resting field and its snapshot-owned
attraction state advances at 60 Hz: speed starts at 250 legacy px/s, rises one
per update, aims 0.08 U above the hero, and its velocity multiplier tweens from
−1 to +1 over 500 ms. Physical swept contact with the hero collects it; XP and
SH2's gem-chain chime still land on the next sixteenth. This grid finish is the
demo adaptation, not a source one-beat arrival timer. Motion and queued pickup
work hold through freezes.

`pickup-tests.txt` checks the actual sim: a gem at x=60 enters attraction while
x=62 waits, outward kick is visible at 0.2 s, first XP arrives at 0.75 s, and a
Magnet modifier widens only the magnet. Fractional-time pause freezes both the
gem and floor food. A source boss death drops one chicken; pickup heals HP
80→110 or 115→120, grants 300 ms immunity and permits the normal 4 HP test bite
after that window. Existing level drop rules remain one per boss and 5% of
brutes; ordinary foes do not gain food drops. The fixed 30 HP heal stays fixed.
`cap-test.txt` preserves the real 400-resting-gem/farthest-merge rule.

## Causal interface and checks

`GemPose { id, w, x, y }` and `FloorChicken::{Drop,Taken,Healed}` are explicit
Survivors records. Their recorder callbacks carry the canonical fixed stop's
song timestamp, because they can run before `clock()` publishes that stop.
LiveView restores them with its journal; arrival removes the position and gem.
Food sound and the brief green healing ring derive from actual grid heal
records, including a subdued ring when no HP was missing. No speculative heal
or gem chime is added to the hash journal. SH2's pickup voice budget still owns
all these cues.

`live-tests.txt` keeps 18 live and 3 session checks: seven actual XP choices
settle to the same 10,342 records with 0.4 s speculation and 30 ms late confirms
as with on-time input. `pickup-journal-test.txt` additionally checks explicit
stop timestamps, rewind, floor removal and the heal moment. `gem-view-tests.txt`
checks value/size bounds and recorded positions, alongside legacy arc cases.
The final pinned-toolchain aggregate checks are recorded by delivery.

## Kept presentation and playable run

`minute-1.png` is a normal-damage, stationary, first-card-pick run fed the real
Rust three-minute arrival fixture: at song 63.166667, run 60 s, full HP, level 3,
291 kills and 137 gems. Scene effects retain only the last two run seconds.
`cap-400.png` is a separately labelled synthetic layout of exactly 400 resting
gems (values 1, 2, 3, 9, 10, 100) and one floor chicken over that minute-one
view. It demonstrates drawing density and the hero/HUD hierarchy; it does not
claim naturally reaching that layout or surviving a dense late run.
`capture.txt` records the setup. Both captures are silent and offscreen.

`headless.txt` is a 60-second normal-damage File run, stopped after 57 song
seconds: 574 sounds, no late skips or drops, 254 kills and full HP. Kill voice
budgets deny excess requests at density as designed. `replay.txt` matches all
73,345 live settled records against the replay prefix; the replay has a longer
settled tail. `session-summary.json` keeps its live hash and mode. This is a
short playable check, not a balance or listening verdict.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 60 --pick-first
cargo run --release -p app -- --headless --pickups-check /tmp/pickups-check
```

SH9 can use the existing Magnet/stat interface without rescaling the gem body.
SW5's Soul Eater food can use explicit Drop/Taken/Healed presentation events;
SH11 owns the integrated feel and milestone review. Ear/feel checks remain in
`native/evidence/operator-pending.md`; there is no approval or playtest pause.
