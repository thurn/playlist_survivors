# SH11 — first-five-minute hook pass and H review

Base `12db4a0a34ad38ed77672862799165510c28767b`. H's ten integrated commits
and their parents are in `milestone-h-commits.json`, copied from each bead's
native delivery notes/comments and verified ancestors. Source/integrated SH11
SHAs and delivery certificate are recorded in the native bead notes.

## Survival and pacing

The existing SF9 driver, normal damage, observed-state keys, unchanged level
and offers, ran all **41 five-run-minute reports in 163.686 wall seconds**.
All **20 positioning runs survived**, minimum HP **72.92%**. All 20 AFK
runs were still alive at 5:00; this is not the before-10 full-run AFK gate.
The random-pick prefix also survived, level 12, minimum HP 98.75%.
`hook-summary.json` checks only the hook survival criterion; pacing stays
report-only. `cases.csv` and `matrix/summary.json` index the actual reports,
commands, stderr and hashes. Two additional AFK seed-0 smoke reports in that
directory are preparation checks, not part of the 41-case matrix.

| Input | Positioning survives | Minimum HP | Level at 5, seeds 1–5 (report) | Within 8–12 (report) |
|---|---:|---:|---|---:|
| cached-original | 5/5 | 72.92% | 2, 9, 1, 2, 2 | 1/5 |
| cached-reverse | 5/5 | 100.00% | 11, 11, 11, 8, 11 | 5/5 |
| cached-rotate | 5/5 | 82.92% | 11, 4, 12, 1, 2 | 2/5 |
| continuous-original | 5/5 | 83.33% | 10, 2, 10, 5, 2 | 2/5 |

Ten of twenty positioning reports reach the level 8–12 target; the other
orders/seeds show substantial variation, including two level-1 prefixes.
There are two positioning chest openings (reverse order) and one AFK opening.
The selected capture case opens none; SH10's separate real-kill/touch capture
covers roulette. All evolutions remain disabled until SW5. This short sweep
makes no 30-minute or final evolved-build claim. SW7 has a native handoff of
all values and owns the final balance sweep. Current AGENTS reserves offers
and easy knobs to SW7, overriding the older SH11 bead's request to rewrite
offers 2–12 here. No contactDamage, chickens, maxed-heal or offers change.

## Evidence from actual play

`recorded-reverse/` is positioning seed 1, reversed real playlist with its
normal gap schedule, level 11 at 5:00, minimum HP 100%, 995 kills. The existing
CLI writes actual arrivals/keys and the exact level source. The new
`--hook-check <recorded-directory>` entry runs before any event loop/audio
setup, replays every 120Hz Engine frame, uses production Scene/HUD, and
checks exact final record hash/count against that independent driver before
encoding. The final fixed-source capture keeps **1,207 frames at 24fps** (50.292s),
exactly matching 2,022,470 records. `capture-final-result.json` keeps final
source/media hashes; `capture-final.txt` is the successful repeated run.
The initial capture source/log are separately scoped before the card fix. It uses background QoS and checks child and ffmpeg exit statuses.

The `hook/` montage retains six chronological eight-run-second windows near
0/1/2/3/4/5 minutes, with real card/menu duration within a window. It skips
images between windows, not simulation steps. `frames.jsonl` identifies each
image's song/run clock and stats. Six minute stills, first fully revealed
cards and a nearby boss are required; roulette is optional if encountered.
The clip is silent and labelled as a normal-damage recorded bot, not a
sound-quality or human-feel proof. The real replay expected count/hash is
**2,022,470 / `b86dc62f2064b22a`**. Raw streams are retained as deterministic
gzip alongside meta/keys; decompress as `stream.jsonl` before replay/capture.

`drop-in.png` and `early-play.png` additionally use the existing `--still`
entry at house song seconds 6/45 with normal damage/pick-first. These are
shipped track-stream stills, distinct from the fresh Rust-Delayed playlist
recording. Stills are inspected offscreen; no app window opens.

`headless-play-result.json` and log keep a 30-app-second File/Delayed normal
playlist smoke: exit 0 in 34.787 wall seconds, session 1793061454 in the main
checkout's ignored logs, actual Garlic pick and resume, 106 sound starts,
zero late skips/drops. The printed `Stopped,24s` is song elapsed, not the
pause-subtracted run clock. The final fixed-source repetition is in
`headless-play-final*.{txt,json}`: exit 0, 34.764s wall, session 1793061828,
the real Garlic pick/resume and 106 sound starts with zero late skips/drops.
This repetition includes the card refresh fix listed below.

The offline production sound scheduler/mixer check reaches minute 5 with
1,680 voices started, zero late skips/drops, 233 speculative cancellations.
Category peaks [weapons,hits,kills,pickups,UI] are [4,5,2,3,0] against
[4,6,2,3,3] caps; admission denies [1,0,150,48,0] are expected budget cuts,
not mixer drops. Its synthetic music tests capacity, not real-music fit.
See `sound-check.txt`; actual normal headless UI voices are in the live smoke.

## Review and validation

`self-review.md` records the required pass before the one cold read-only
same-host Warden review of H. The one cold Warden found a stale card/inventory display after rollback
with unchanged reveal beats. It is fixed by refreshing the sim-owned offer
and inventory without restarting the animation or inventing a confirm. A
real-offer-table regression and all seven card-screen tests pass; focused
clippy passes. `review.md` records the finding and disposition. Warden found
no additional actionable issue and independently passed all 87 sim unit
tests. No confirmation review. Final
`scripts/check-fast` and Tollgate gate certify the final source; full local
CI is required only if a confirmed fix touches shared checks/configuration.
Only the operator can judge File/System feel and the mix under real music;
the first-five-minute row is in `operator-pending.md`, with no approval pause.

## Commands

From the repository root (builds from `native/`):

```sh
(cd native && cargo build --release -p app -p cli)
uv run --no-project python native/scripts/survivors_sweep.py --minutes 5 --jobs 2 --out native/evidence/sh11/matrix
native/target/release/ps survive --minutes 5 --bot positioning --seed 1 --order reverse --record native/evidence/sh11/recorded-reverse
native/target/release/playlist-survivors --hook-check native/evidence/sh11/recorded-reverse
native/target/release/playlist-survivors --headless --still native/evidence/sh11/drop-in.png --still-track house-128 --still-at 6 --pick-first
native/target/release/playlist-survivors --headless --still native/evidence/sh11/early-play.png --still-track house-128 --still-at 45 --pick-first
native/target/release/playlist-survivors --headless --sound-check native/evidence/sh11/recorded-reverse --seconds 300
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30
scripts/check-fast
```

Use a new record/output directory for a fresh CLI recording; the CLI refuses
to overwrite one. Existing recordings can be replayed with `ps replay DIR`.
Delivery: Conventional Commit, `tg candidate HEAD` in this fresh worktree,
then `tg approve --wait` from the main checkout. Completion is staging.
