# SF11 — survivors asset intake

A full bake adds sources without wiring any into the game. `sources.json`
pins the six downloaded archives by SHA-256; local LPC sources retain their
existing read-only paths. `licences.yaml` selects CC0 for Kenney, CC-BY 3.0
for every icon (Lorc or Delapouite), CC-BY-SA 3.0 for Johannes Sjölund's
skeleton boss, and OGA-BY 3.0 for the plants. The baked credits include all
sixteen icon source files and their individual authors.

## Interfaces for consumers

- `characters.json`: `brute`, with four-facing walk, attack and hurt, plus
  white hit-flash frames. The skeleton boss's explicit `full_base.xml`
  include supplies its real walk cycle; its single death direction is reused
  for all facings. SW3 replaces the sim's existing stand-ins later.
- `world.json`: `thorn-plant-{summer,autumn,winter,spring}` variants. Spiky
  leaf clusters from LPC Revised, anchored at their base; SW3 animates them
  in code. They are available art, not newly placed scenery.
- `effects.json`: `particle-{circle-01,flare-01,magic-01,magic-03,slash-01,
  smoke-01,smoke-04,spark-01,spark-04}`. White HD textures at 128 px, for
  additive tinting and shader layering. Existing effect recipes are unchanged.
- `fonts.json` / `fonts-0.png`: `pixel` and `pixel-square`, printable ASCII
  32–126 (95 glyphs each), baked at 32 em pixels. Each glyph stores its atlas
  `rect`, `advance`, `bearingX`, `bearingY`. Origin is the text baseline;
  draw at `(pen_x + bearingX, baseline_y + bearingY)` and advance by
  `advance`. `ascent` and `lineHeight` describe each face. Space has a blank
  1×1 rect and a real advance. Unsupported text should use `?`. Outlines
  are parsed by pinned opentype.js and rasterized by sharp, independent of
  installed fonts/Fontconfig. SH1 owns drawing these glyphs.
- `icons.json` / `icons-0.png`: sixteen transparent white 64×64 glyphs keyed
  by the five weapon, five evolution and six passive names in
  [intake-audit.json](intake-audit.json). The starting sword key is
  `sword-spin`; SH1 maps content's `sword` to it. Tint and glow are runtime
  work. CC-BY outputs are separate from CC0 fonts.
- `sfx/sounds.json`: fifteen new `kenney/{sci,digital,casino}-*` samples,
  listed with duration, loudness and onset in the audit. These remain
  normalized by the existing baker and are not selected by a game palette.
  Short laser/zap/pulse candidates support SH2/SH3; card and chip candidates
  support SH7/SH10. The catalogue marks descriptions provisional: listening
  requires the operator's ear and is recorded in operator-pending. No claim
  of musical fit is made before that check.

## Evidence and limits

[The intake sheet](intake.png) was inspected: transparent white icons, both
pixel faces, the eight brute walk frames and plant crops are legible.
[The audit](intake-audit.json) compares every existing character animation,
effect frame and prop variant with its pre-intake raster and metadata:
13 sprites, 57 effects and 22 props are unchanged despite atlas repacking.
Existing sound metadata is also unchanged. The new assets total 4.7 MiB
with all previous assets. This verifies intake, not later in-game presentation.

`bake.txt` records the full bake. A repeat full bake reproduced all 257
output files byte for byte; SHA-256 values are in `bake-hashes.json`. Validation and a muted, offscreen File-mode
smoke are recorded in `validation.txt` and `headless.txt`. No sim, renderer,
audio scheduler, level or runtime palette was changed. Milestone F's cold
review belongs to SF10, per the project's survivors review policy.

Playable command (from `native/`):

```sh
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --delayed
```

Routine check adds `--headless --seconds 12`; no window or audio output.
