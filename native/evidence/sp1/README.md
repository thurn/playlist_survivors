# SP1 — evolution showpieces

Fresh Tollgate worktree `codex/sp1-evolution-showpieces`, base `d68e2be149e50505373809c761404c7b86b406e3`. The source and landed SHAs are recorded in hv-n7fn.30's native notes after delivery.

## Behavior and boundaries

A chest's simulation-owned `Roulette.id`, landing time and actual evolved rewards drive the transformation. The current view and owned inventory drive visuals; speculative run state supplies only the existing reconciled sound-cue API's lead. No simulation, recorder, rollback runner, movement, weapon statistics, damage or offer code changes.

The burst starts on the actual landing downbeat, lasts 1.1 seconds, and retains its identity across frames. Manual/music pauses hold its age; the musical chest pseudo-pause lets it advance. Corrected rewards refresh its identity/color without restarting; withdrawn inventory or a retracted grant clears it. Expiry cannot retrigger while a landed roulette remains. It can finish after the menu closes. Several evolved rewards share one sound, preserving the existing three-voice UI cap; music is never ducked.

Bloody Tear has a counter-cut crimson crescent and blood-like tip trails; critical labels remain the actual simulation's gold criticals. Soul Eater has a wide segmented teal/violet sigil with inward wisps and a transparent centre. Holy Wand's fast gold-white lances have split tips. Unholy Vespers has a continuous broken halo and blade echoes following actual bounded projectile samples. Thunder Loop has a forked crown and double ground rings on each actual strike, including the fixed-point return; presentation does not predict a target or manufacture another return. Ordinary weapon appearances are retained. Effects remain bounded, and SP5 owns the final five-evolution density/readability measurement.

Scene/module/sound-cue additions are registration calls; the chest showpiece and boss bodies remain SP2's. The transformation overlay uses footer lanes at 77%/82% of screen height. No `tier.rs`, camera, zone or build-aura work is included; those belong to SP3.

## Sound provenance

Six short item-unlock transients from rubberduck's [80 CC0 RPG SFX](https://opengameart.org/content/80-cc0-rpg-sfx) were selected from the already hash-pinned archive. The primary page's CC0 license and author were verified on 2026-10-27 UTC. The manifest, specific license entry and generated credits include the selections; no paid assets, AI art or raw VS extraction data are used.

`sound-source-audit.json` retains the archive hash, exact source paths, selected IDs and baked measurements. Normalized durations are 0.174–0.517 s and onsets 0–93.9 ms; peak limiting can leave a short transient below the nominal −18 LUFS target. One new key per evolution plus one multi-reward key uses the existing UI budget. Existing evolved normal/accent fire/contact cues remain. Offline decode, timing, no-duck, stable-identity and pre-play retraction tests cover the actual palette, including a 200 ms planning lead. They do not establish subjective musical fit; that is an operator-pending row.

`npm ci` ran before asset edits; a full normal bake produced 251 sounds. Existing atlas PNGs stayed byte-identical. No baker implementation changed.

## Kept visual proof

`captures/` contains a silent clip for each evolution, normal/accent stills, a transformation still, and Thunder Loop's return still. Source setup is explicit: injected initial evolved loadout with its actual partner, lead zero, synthetic 128 BPM, no hero damage exemption, hidden base-sword FX. Blood uses Luck 10 to force actual combat criticals; Blood/Wand/Thunder have eight durable HP1000/XP0 skeletons, Soul/Vespers no targets.

The transformation is a labelled **typed chest-presentation trigger at synthetic beat 4 (1.875 s)**, while the initial evolution is already injected. It is not a naturally earned chest or a simulation grant. SW5's actual five-item roulette tests cover eligibility/sequential grants; this bead's controller tests cover the view contract, pause/retraction and corrected rewards. Neither the clips nor the fixtures are survival, pacing, human listening or dense GPU evidence.

Expected clips: Blood 450 frames/15 s, Soul 120/4 s, Holy 180/6 s, Vespers 450/15 s, Thunder 240/8 s; total 1440 frames/48 s at 30 fps and 1280×720. `media-info.json` retains final file hashes and ffprobe metadata after writers finish. `initial-media-sha256.json` scopes the earlier visual iteration. All 16 final PNGs were directly inspected (the first ten before recapture were byte-identical afterwards; the final Vespers accent and remaining five were checked after writers ended). Hero/HP/damage remain readable in these isolated fixtures; `visual-qa.json` records scope. `initial-invalid-capture-flag.txt` is an author command typo, not a tool failure.

## Validation and reproduction

All 61 app tests, app all-target clippy and five content/legacy-reference tests pass. Four controller tests cover early/duplicate/expired/retracted/corrected grants and pause clocks. The real palette test decodes all six cues, places their onsets at the landing with a 200 ms lead, keeps one UI voice, and cancels retracted unstarted voices.

The shared weapon-capture helper now exports evolved clips, so the complete local `scripts/ci-full` is run with a tracked-and-untracked source/asset fingerprint before and after. It passed exit 0 in 1075.223 s; the 539-file tracked-and-untracked source/asset fingerprint stayed `a5b023a698fd47a8093c79bdda00e4b14a0d138b3b8cb61ab515b37146b655f2`. `ci-full-summary.json` records it. There is no per-bead cold review under the Survivors policy; SP5 reviews P. No rollback-runner change is made.

From the repository root after building the app:

```
native/target/release/playlist-survivors --evolutions-check native/evidence/sp1/captures
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
```

No window is opened by these checks. Only the operator can judge the clips under music or exercise System capture permission; see `native/evidence/operator-pending.md`.

The asserting matrix completed 41 cases in 290.263 s: all 20 positioning cases survived 1800 run seconds with minimum HP 82.986111%; all 20 AFK cases died at 225.383333–329.029730 s. The remaining random-pick case is report-only. All 41 settled gameplay hashes match SW7 exactly (`gameplay-hash-comparison.json`); this is the pre-SP2/SP3 base, not a claim about their independent changes. Full case reports/commands are kept in `final-matrix/`. Pacing stays report-only with SW7's limitations unchanged (natural later three evolutions remain unproven).

The CI diagnostic no-damage full replay matched 28,645,743 records/hash `1b88d1a8816cd14f` in 71.676 s. It is explicitly diagnostic, not substituted for SW7's retained normal full replay (28,645,747/hash `edd2c4598be943e1`). The normal headless 30-app-second run here exited 0 in 35.275 wall seconds, session `1793073476`, 163 sounds/0 late/0 dropped; 28,424 live settled records match its replay (29,909 including tail), one rollback, 63 kills, HP 0.97. `headless-summary.json`, `headless-replay.txt` and `timing.txt` retain outputs. The CLI's printed song span is not pause-subtracted run time.

The final full-CI 1920×1080 density fixture passed: whole-frame p99 2.95 ms, max 4.14 ms, 300 foes/400 gems, five rank-8 **base** weapons and six applied max passives. This does not establish all-five evolved dense performance or moving-pickup readability; SP5 owns those checks. No source/build writer remained when acceptance was finalized.

## Integration with SP2 and SP3

While these checks ran, independent SP2 and SP3 landed as `ccae0912293968c1614903bbdd6b90fb28bb7830` and `a8c6a4eb5aada1cb77ead825b2b61cc031409d0f`. The first candidate was a terminal merge conflict only in the append-only operator table; supported `tg update` aborted cleanly, and a manual single-commit rebase preserved all rows and all additive scene/module/sound registrations. No simulation or showpiece body from either peer was rewritten.

On the integrated source, all 65 app tests, all-target clippy and release app build pass. A new normal headless run exited 0 in 38.967 s (30 app seconds), session `1793074305`, 163 sounds/0 late/0 dropped. A freshly rebuilt CLI matches all 28,473 live settled records (29,952 including tail), one rollback, 63 kills, HP 0.97. The earlier full suite and 41-case hash comparison explicitly cover the SP1/SW7 source before these peer changes; the integrated fast check and Tollgate gate certify integration, with final P validation owned by SP5.

The repository's general `captures/` ignore initially omitted the diagnostic media. All 21 explicitly requested SP1 non-vendor media files are included deliberately; their final SHA-256 manifest is kept. This is an author staging correction, not a tooling defect. Final fast-check output is `integrated-check-fast.txt`; the source/landed SHA and promotion certificate follow in the bead's native notes.
