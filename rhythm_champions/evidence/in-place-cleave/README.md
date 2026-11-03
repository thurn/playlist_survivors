# In-place rhythm cleave — ps-uyc.1

F is appended as key code 9; existing codes and session format 6 remain
unchanged. The shared dash controller remembers whether the buffered action
is F or Space and retains its existing preference for on-beat inputs.
F uses the same shield, aura, sword stats and single landing sweep, without
calling the movement dash. Its sweep follows ordinary walking over the
0.15-second windup. Presentation skips the dodge pose, travel effects and
travel whoosh, while retaining the natural-speed spin, impact effects/sound
and on-beat accent. F does not choose cards or repeat while held.

## Validation

- .NET suite: 499 passed before the final two boundary tests; all 11 focused
  cleave tests subsequently passed, including cards and buffer arbitration.
- Unity EditMode: 138/138 passed (keyboard mapping/suspension, spin cancellation
  and pause with/without travel, impact sound without a travel whoosh).
- Mono player built twice successfully. Real-time 1280×720 normal gameplay
  using the trailer soundtrack, seed 31, scripted walking and key inputs.
  Captured 19 seconds of actual application audio/video: 562 video frames,
  951 audio buffers, finalized successfully. This is automated evidence,
  not subjective hand-play approval.
- Recording replayed exactly in .NET: `C04463B9BD804A91`, 843/843 events.
  Built-player headless replay also exited 0; inspect its replay log below.

Local artifacts (vendor-derived pixels/audio; never commit) live under
`~/Library/Caches/playlist-survivors/evidence/ps-uyc/`:
`cleave.mov`, `player.log`, and `session/` including `event-timing.csv` and
`state-timing.csv`. The capture is inspected as sequential motion frames:
F winds up and sweeps while walking, without a dash trail or abrupt travel;
the yellow ring remains after the off-beat F. Space still visibly travels.

Timing from the actual music-plugin session:

| Input heard | Action | Judgement | Impact | Position behavior |
| --- | --- | --- | --- | --- |
| 8.28 | F | half beat | 8.43 | 0.9 m ordinary walking |
| 9.16 | F | off beat | 9.31 | 0.9 m ordinary walking, existing aura retained |
| 10.22 | Space | half beat | 10.37 | 4 m dash |
| 11.28 | F | off beat | 11.43 | 0.9 m ordinary walking |

Reproduce with the built player's `--file` pointing to the local soundtrack,
`--seed 31 --seconds 22 --picks reference --record <local-session>` and
`--walk 'right@0-2,down@2-5,left@5-10,up@10-18,f@8.28-8.33,f@9.16-9.21,space@10.22-10.27,f@11.28-11.33'`.
Then `just champions-tool play --headless --replay <local-session> --replay-speed 8`.
Operator hand-play remains useful for judging the new positioning option.

## Independent Warden review — ps-uyc.2

Reviewed the implementation at `87e7786` against current staging and the
live design, including shared buffer arbitration, appended key serialization,
keyboard suspension, card/death input blocking, movement preservation and
the common landing-sweep presentation. No gameplay defect found.

Added deterministic regression coverage for actual damage dodging inside F's
shield and damage after its expiry, both uninterrupted and across a pause.
The paused case also verifies exactly one stationary sweep at the shifted
impact time. Both buffered F and buffered Space are discarded by pause and
require a new press after resume. All 505 simulation tests pass.
Unity EditMode also passes 138/138 with no skips after standard local setup
restored this worktree's initially absent plugin and vendor assets.

Independently replayed the local captured session: exact hash
`C04463B9BD804A91`, 843/843 events. Inspected sequential capture frames around
both F actions and Space: ordinary walking with stationary relative cleave,
retained yellow aura after off-beat F, and distinct Space travel. This remains
automated capture evidence, not operator hand-play approval.
