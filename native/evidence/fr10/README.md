# FR10 — Integrated restoration qualification

FR1–FR9 are implemented and landed. Shipping source is fe260cfd63dba01ae7435e3629d290c0d78fa36f
(the evidence-only amendment of qualified56d7a0d). This package changes evidence
and documentation only. Game feel takes priority over minimum difficulty:
AFK losses are reported, never required by the release survival gate.

## Final-source results

| Check | Kept result and evidence |
|---|---|
| Full local suite | [FR9 ci-full.txt](../fr9/ci-full.txt): baker, analysis, native workspace/ignored tests, purity, dependencies, frame gates, full survival matrix and exact replay pass. Unchanged analyzer inputs reuse their exact successful8-track parity stamp. |
| Full30-minute runs | [Summary](../fr9/matrix-summary.json), [raw commands/reports](../fr9/matrix-raw.jsonl.gz):41 cases in806.473s, zero survival failures. All20 active cases complete1800s, minimum HP share0.416667 (existing target>0.25). No damage exemption. |
| Growth/pacing | [Diagnostics](balance-diagnostics.json): active kills18,711–22,123; level at minute5 ranges14–17, at minute30 ranges44–74. Source XP curve/drop economy remain unchanged; levels measure XP progression without claiming exact earned-XP totals. |
| HP changes | Largest adjacent one-second net HP-share decrease0.173611. These samples include recovery/max-HP changes and can hide several contacts; they are not exact burst damage or enemy-source attribution. Catalog contact/slam, swept contact and120ms lockout regressions remain in the full suite. No automatic HP/immunity/recovery retune was made. |
| Restored choice path | [Actual random-choice build](random-choice-build.json) survives1800s, level80, minHP0.792, with rank8 Chain Lightning and Fire Trail plus Bloody Tear/Soul Eater/Holy Wand. This path intentionally excludes Bible. Legal-choice search separately reaches all7 bases/all5 evolutions and six-slot alternatives. |
| Exact replay | [FR9 replay](../fr9/replay.txt):22,737,077 records, hashd381e4ea7f83b0e8,76.536s. FR9's presentation leaves corrected seed4's22,203,956-record hashf9e6625092c353c4 unchanged from [the boss repair](../fr-ci15/summary.json). |
| Modes and Watch | [Zen](zen-watch.txt)34/34 kills; [legacy](legacy-watch.txt)30/30 kills. Actual File/Delayed, normal damage, scripted beat presses/card picks; the [fixture](watch-fixture.yaml) shortens duration to6s. Both complete victory and Watch playback. These are short routing/replay checks, not30-minute legacy balance claims. Full suites verify judged rewards/drain/tier damage/hitstop/beatless fairness and zen's unjudged damage. |
| Density hierarchy | Final-source1080p ordinary p991.42ms and constant300-enemy/400-gem p993.10ms, unchanged4ms p99 bar. [Actual-kill pool](../fr9/bench-combat.txt) reaches2093 kills/96 marks/300 peak actors, p993.05ms; [restored moving build](../fr9/bench-restored.txt)p992.99ms. Those two60s fixtures preceded the boss cancellation repair, which only activates after90s. Local runs do not establish general hardware timing. |
| Actual mixed PCM | [Four real tracks](real-mix.txt): Aria, No Life After Crypts, DnB174, BeethovenV; decoded actual palette at8kHz,24s, effects RMS0.008249/peak0.054599, output peak<=0.868442. Music-only reference and isolated music-limiter tests pass; game effects do not alter music gain/PCM. Cues are a deliberately repeated palette diagnostic, not a natural soundtrack capture or subjective audition. |

Short float-stereo listening WAVs live outside git in
`/Users/dthurn/playlist_survivors/captures/feel-fr10/`; source credits remain in
public/reactive/index.json. Speakers/System and human feel remain in
[operator-pending.md](../operator-pending.md). We do not infer audible output
from Play-command counts or certify feel from bots/silent stills.

## Visual comparison and limits

[Open the side-by-side gallery](comparison.html). Reference source is exactly
6a00a47f9d657be0dbbd6ec6244ce82b4a78dea9, read-only archived/built independently.
Old and new opening use20s of Hang Low and ordinary damage. Old98s boss vicinity
uses ordinary damage; the restored98s arena still explicitly disables damage
to inspect staging. Near-five-minute frames use song290s (about284s run time).
Both normal-damage scripted captures have fallen; the additional restored
invulnerable capture shows presentation. None is a matched balance comparison.
Source inventories differ deliberately (rich old arsenal versus sword-only start).

[FR9 dense/aftermath diagnostics](../fr9/dense/) and
[aura states](../fr9/aura/) label injected actors, HP and displayed loadouts.
The durable evolved crowd's peak can still be very bright; actual-kill scenes
leave visible floor marks and keep hero/HP/beat indicators clear. Human dense
readability remains a playtest, not a claim settled by one peak frame.
Silent motion clips remain outside git: captures/feel-fr2/spin-and-accent.mp4
and captures/feel-fr8/opening-minute-sweep.mp4 in the main checkout. FR1's
sampled root-displacement tests establish admission/holds at70/128/174BPM;
stills cannot establish rhythm or audible alignment.

The integrated run found one real regression: abandoned minotaur slam deadlines
stalled long runs. [The independently reviewed fix](../fr-ci15/README.md) clears
cancelled deadlines on death/retreat; before/after regressions, exact affected
cases and the final sweep/replay pass. No performance/survival bar was weakened.

## Play and reproduce

From native/: `cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`;
add `--legacy` for judged play. `[ / ]` changes saved effects level, F1 shows it,
F4 toggles saved key lights. Music ducking stays off.

Full checks: `scripts/ci-full` from the repository. PCM: from native/,
`PS_MIX_EXPORT_DIR=/tmp/feel-mix cargo test -p app real_music_effects_mix_qualification -- --ignored --nocapture`.
Short mode Watch: app with `--headless --file public/reactive/house-128/house-128.ogg
--level native/evidence/fr10/watch-fixture.yaml --delayed --seconds 34 --press-beats
--pick-first --watch` from repository root (also `--legacy`).

All game changes received the repository's required cold reviews; confirmed
findings were fixed with regressions and no confirmation reviews. This final
package has no game-code changes and requires no additional cold review.
