# FR3 — Audible effects

Effects no longer depend on capture RMS or beat confidence. Strides play in
Survivors. `[` / `]` adjust the independent effects bus by 10% (0–200%);
F1 shows the current value, and listening shows the shortcut. The global
setting survives source/mode changes and application restarts. Existing
settings default to 100%. Changes glide over 50 ms. Music volume is separate.

The effects compressor, smooth relative-level follower, actual-duration caps,
rollback cancellation, spatial sample variation and coalesced kills remain.
Player attacks (3), hero damage (2) and footsteps (2) have independent reserved
slots; important UI keeps its own 3. All category caps total 25, below the
mixer's 32 active voices. Gameplay never enables optional ducking. The music
limiter measures music alone; effects add within remaining per-channel
headroom, so effects cannot pump the music limiter or clip the output.

## Checks and limits

- `focused.txt`: audio and app tests; includes quiet/absent music, default no
  ducking, independent effects mute, music limiter isolation, saved settings,
  beatless footsteps/attack/hurt/heal, long crowd sample tails, and speculative
  scheduling/cancellation. No cold review required: 195 changed production
  lines, no simulation edits.
- `real-mix.txt`: actual decoded palette samples mixed at 8 kHz against 24 s
  excerpts of Aria, No Life After Crypts, DnB 174, and Beethoven V. Follower at
  its deepest normal cut (0.316). Effects RMS 0.007689, peak 0.045897 across
  all excerpts, output peaks 0.192401–0.868442. These measure independent,
  nonzero PCM and output headroom, not subjective listening quality.
- Short stereo float WAV exports are kept outside git at
  `/Users/dthurn/playlist_survivors/captures/feel-fr3/`. Source identities and
  credits remain in `public/reactive/index.json`. These are an intentionally
  repeated palette diagnostic, not a natural playthrough.
- `headless.txt`: actual House 128 file, Delayed, muted, eight seconds; four
  scheduled sounds started, none late/dropped. Debug build to exercise routing.
- `check-fast.txt`: final repository aggregate.

Reproduce PCM: from `native/`, `PS_MIX_EXPORT_DIR=/tmp/fr3-mix cargo test -p app
real_music_effects_mix_qualification -- --ignored --nocapture`.

Playable: from `native/`, `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed` (add `--legacy` for judging).
Speaker/System listening is recorded in operator-pending.md and is not claimed.
