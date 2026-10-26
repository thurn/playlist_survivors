# SH1 — HUD and damage presentation

Base: `089c08f5f3cc71b70a6f5724c8e7227fffb6d68c` (SF8, including SH6). The app loads SF11’s two font faces and sixteen icons without rebaking. Six atlas pages are bound; procedural effects use linear sampling, pixel art and UI use nearest sampling. Survivors uses the Kenney pixel face for HUD/cards; the old debug/legacy font stays available.

## Kept captures and commands

Run from the repository root, after `cargo build --release -p app -p cli` in `native/`:

```sh
native/target/release/playlist-survivors --headless --still native/evidence/sh1/minute-0.png --still-track house-128 --still-at 0.5
native/target/release/playlist-survivors --headless --still native/evidence/sh1/full-slots.png --still-track house-128 --still-at 12 --hud-full-slots
```

Both are actual 1920×1080 offscreen GPU captures. Minute 0 has the starting sword, empty slots, XP/level, timer, kills and HP under the hero. The full-slot image is an explicit renderer capacity fixture: it replaces only the displayed inventory with twelve max-rank icons, including a base/evolution pair that normal play would not own together. It injects two same-enemy damage hits totalling 3 → white **30**, and a separate critical-style 10 → gold **100**. It proves layout, merging and styling, not an achieved inventory, real critical combat or a density-performance result. `--hud-full-slots` is accepted only with `--still`.

The normal-damage 20-second File/Delayed headless check starts a run at song 2.8, stops at about 14 run seconds, and replays the same hash (24 kills, full HP). See `headless-20s.txt` and `headless-replay.txt`. No window was opened. Playable command from `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```

## Interfaces and limits

- `Moment::Hit` carries actual `Fate.damage`, relative song time and a critical flag. SH6’s grid-aligned `EnemyImpact` presents labels once with flashes/recoil; logical hits do not present early. Watch my run uses beats from the recorded stream, rebased through the same causal session filter as the engine.
- Labels sum per enemy and committed beat (free pulse when no recent beat), display damage ×10, drift/pop/fade, and cap at 128 live labels. Critical labels are larger/gold. **Current Fates do not expose critical status; SW5 must wire the flag when implementing real critical combat.** The diagnostic and regression exercise its styling today.
- `BitmapFont::text` takes the existing HUD scale units (uppercase height = 7 × scale), handles alignment and a shadow, and supports ASCII plus punctuation fallbacks. SH7 can reuse it and the plain card helper for its animation/polish.
- UI overlay is ungraded, separate from world glows. The health bar tracks the rendered hero’s feet. Beat track/groove/key decorations are omitted only in survivors mode.
- Hash bytes preserve the old Hit/EnemyImpact representation, excluding only new presentation fields; record count and game rules do not change. `preexisting-replay.txt` is the same-hash replay of the pre-SH1 recording `native/logs/sessions/1793026969` (133 kills, 1 rollback). The original local session is gitignored; its replay output is retained here.

## Verification

App tests (36, including five damage tests and recorded-beat replay coverage), render tests including GPU capture (8), eval unit tests (5), live rollback tests (14), historical session tests (3), and clippy for app/render/eval all targets passed. Tests tie labels to actual Fate damage, literal pre-change hash bytes, and actual baked font/icon pages. Final aggregate validation and cold-review disposition are recorded in bead notes and `validation.txt`/`review.md`.

Operator-only readability/feel check is in `native/evidence/operator-pending.md`; headless captures cannot establish readability while playing or sound feel.
