# SW7 — the whole run

Worktree base 0f1d9ebf85d1aa2049a92da9fd431c29f0907533. Final SHA is recorded in native Beads after the immediate post-check commit.

Final complete `scripts/ci-full` passed in 727.428918 s on an unchanged source fingerprint (`ci-full-final-summary.json`). Source before/after SHA-256 f792a58f7130ecf1a68356d3e6db3edd7914c9d81815503a8bbcfda1077e3db7. The earlier in-flight matrix is superseded; none of it is reused for acceptance. The final fast check and staging delivery are recorded in native Beads.

## Baseline and choices

`baseline/provenance.json` identifies the exact clean-input asynchronous CI matrix reused here: 41 cases in 371.431185 s, all 20 positioning pass, 13 AFK fail. Reusing completed exact-input evidence avoids duplicate full batches. This is the integrated SW1–SW6 source including SW5 evolutions and SW6 events, original offers and all normal hero damage. `baseline/cases.csv` and raw JSON retain every result. Historical chest reports could duplicate a physical roulette across freeze-priority transitions and omit origin; keep them as originally measured, not as corrected chest counts. Baseline stream is deterministic-gzipped with its uncompressed hash; rebuild the baseline source to replay its old compile-time offer table.

Trials and rejected settings remain scoped in tune1–4. Unrebuilt tune1 files are baseline prefixes because offers use compile-time include_str; tune4 notes an unused level25 typo corrected before any final full checks. Contact1.5 rejected: one positioning prefix reached17.5%HP, while2AFK still survived10min. Original knobs all restored: contact0.5, brute chickens5%, bosses1, maxed heal30.

Final intended first cards by reached player level: 2 Wand (rank 1), 3 Tome, 4 Spinach, 5 Heart, 6 Garlic, Sword ranks 2–8 at player levels 7–13, Pummarola at 14/Garlic 8 at 21, Bible at 22/Ring at 23, Wand 8 at 30, Spellbinder at 31/Bible 8 at 38/Ring 8 at 45 with Duplicator at 46. Finish source passive ranks by67 without chest grants, heal68+. These are table/eligibility facts, not level-at-time gates. Early area defense now requires collecting XP by moving, as part of the intended movement-based progression. Other card columns still offer direct attack alternatives. Earlier tune4 10min prefix:20AFK all die225.383..329.030s;3moving cases survive with minimum86.25%. Full candidate results supersede these prefixes.

## Correctness and review

`review-index.md`, `self-review.md` and `review.md` contain the recorded W SHA scope, pre-review pass and one P2 disposition. Source Holy Wand RepeatInterval now divides by captured effective Speed:50ms normally,25ms with character Speed2. Six-shot normal burst250ms. Pause retains remaining25ms. No source stats/cooldown/XP/role data changed.

Chest reports now key by physical Roulette.id and retain originMinute/tier, sampling pre-reward ranks once even if music/level freeze takes precedence. A focused regression covers interruption, reentry, rewards and another chest at the same held run time.

The existing live fixture followed the previous first-card schedule. It is adapted using actual XP/cards and a bounded pre-construction synthetic actor schedule with a durable radius50/KB0 body to exercise all weapon contacts/trails and delayed-confirm rollback. No fixture actor, XP, damage exemption or synthetic music is used as survival/pacing evidence. The initial full-check failure and fixture attempts are kept; these are author test-setup changes, not tool failures.

## Reproduction

From the repository root: `scripts/ci-full` checks every release section, including `--assert-survival`. Only full normal-damage positioning and AFK outcomes gate; random-pick and all pacing numbers remain reports. `scripts/check-fast` runs once on the final source before the immediate commit. Review fixes have focused regressions; there is no confirmation review.

Normal playable headless command from root after build: `PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30`. Operator-only full File/System musical feel is pending in operator-pending.md; no app window is opened by these checks.

Final table refinement uses useful Spinach4 instead of Spellbinder before Bible ownership; Spellbinder moves31 ahead of Bible8, Duplicator46 completes Ring’s pair. Every AFK prefix died at level2, so this does not hide an AFK survival build. tune5 retains this choice and fresh prefixes; final full validation is repeated on a frozen source. See validation-scope.txt for the explicitly superseded in-flight matrix.

## Final full-run measurements

`final-matrix/provenance.json` ties the complete exact-source matrix to its command, source fingerprint and kept raw reports. `cases.csv`, `chest-ranks.csv` and `measurements.json` derive directly from those reports. Normal hero damage throughout; no fixture actors or injected inventory. All41 reports completed in202.508127s, comfortably within the20min budget. Only survival asserts.

| Measurement | Original integrated baseline | Final |
|---|---:|---:|
| Positioning survival |20/20 at30:00 |20/20 at30:00 |
| Lowest positioning HP |82.5% |82.986111% |
| AFK death before10min |7/20 |20/20 |
| Final AFK death range |not all die |225.383333–329.029730s |
| Level5-minute target8–12 |14/20; range2–11 |18/20; range4–12 |
| Level at first crossing30:00 |range11–20 |range15–20;0/20 reach50 |
| Level after victory clear |see raw cases |range24–40 |

The positioning cohort includes15 cached-track cases (5seeds×3orders) and5 continuous-analysis original-order cases; AFK has the same20 cases. Random-pick is report-only: survives1800s, minimum85.416667%HP, level3 at5min/9 at30:00/15 after the finale, no evolution. Level-at-time targets remain reports, never assertions. The finale awards XP while suppressing new offers, so post-clear levels do not imply those cards were acquired during play.

Actual collected eligible chests, keyed by retained origin (not collection time):

| Origin | Positioning chests observed | Intended-path rank before rewards |
|---|---:|---|
|10 |7 |Sword8 in all7 |
|15 |3 |Garlic7,7,8 |
|20 |0 |Wand rank unobserved |
|25 |0 |Bible/Ring ranks unobserved |

There are7 Bloody Tear evolution runs and1 Soul Eater run. Holy Wand/Vespers/Thunder Loop are not naturally acquired by this conservative bot cohort. These results do not establish human XP pace, completion of the original50+level target, all five natural evolution paths or late-chest musical feel. The offer rewrite prioritizes Sword8 at player13 (within measured natural levels), then Garlic8 at21, retaining complete source ranks/partners for more active human play; it does not change VS XP, damage, speed, HP, the schedule, movement or bot behavior to manufacture pacing. Original intended later chest targets remain visible as unmet/unobserved reports. SP1–SP4 polish and operator velocity feedback continue; SP5 measures the final evolved-effects density fixture.

Normal recorded original/positioning seed1:1800run seconds, minimum98.75%HP,3129kills, peak300foes, level17 at30:00/32 after clear, one origin10 chest picked up at1389.076333run seconds (Sword8/Wand1/Garlic4), Bloody Tear. Full normal recording and replay both settle exactly28,645,747records/hash`edd2c4598be943e1` (`full-normal-replay.txt`). The stream is gzipped with its uncompressed SHA-256; decompress before `native/target/release/ps replay native/evidence/sw7/final-matrix/normal-session`. CI’s dirty-source replay fallback is separately marked diagnostic/no-hero-damage (28,645,743records/hash1b88d1a8816cd14f); it is not substituted for this normal run.

The full native performance checks pass: normal1920×1080 frame-work p99=1.18ms; density p99=2.59ms, max5.82ms at300foes/400gems/five rank8 base weapons/six applied max passives. The gate is p99≤4ms, not a maximum-frame guarantee. This fixture does not measure all evolved effects; SP5 owns that. Runner engine p99=0.853ms. Raw output is ci-full-final.txt.

Final normal app smoke on the frozen build:30app seconds, exit0 in35.081893wall seconds, session1793068914, first actual pickMagicWand,163sounds/0late/0dropped.28,473 live settled records match replay (29,952 including replay tail). The printed Stopped24s is song span, not pause-subtracted run time. The final20song-second still is byte-identical to the inspected earlier frame (normal damage, level2/Wand1,95%HP); hash/command in still-final-summary.json. No app window opened, no listening/human-feel claim.
