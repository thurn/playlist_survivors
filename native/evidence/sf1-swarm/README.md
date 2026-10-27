# SF1 swarm source follow-up

Discovered by SW6: the feel appendix included both event roles' speed and
resistance but pointed HP/contact/XP to a roster table that omitted them.
This bounded SF1 source-curation prerequisite fills only that gap.

Read `ENEMY_DATA` rank 1 through the existing private extraction loader at
`~/Documents/vs-extract/scripts/load.py`. Both rows have fixed direction and
omit the HP_x_Level skill. Checked `EnemyController.InitSkills` and its
health initialization: level scaling is enabled only by HP_x_Level.
`source-audit.json` retains only selected numeric/boolean facts and SHA-256
hashes, matching the original SF1 source files. No raw JSON, extracted source,
asset or game text is copied into the repository.

BATSWARM: max HP 0.1, contact power 1, speed 700, XP 1.
GHOSTSWARM: max HP 1, contact power 5, speed 700, XP 1.5.
Both use fixed direction, no level scaling; existing knockback fields agree
with the preceding curated table. SW6 consumes these facts from docs.

The final repository `scripts/check-fast` log is retained. This is source
documentation only: no runtime, CI, asset or analyzer change and no per-bead
cold review. SW7's W milestone review receives this dependency's commit SHA.
Playable command remains, from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

SW4's freshly recorded normal headless smoke is applicable because this bead
changes no game/app input; no new operator-only check is introduced. Delivery
SHA/candidate/promotion go in hv-n7fn.43 notes; completion is staging.
