# The plugin's System source on the Mac (ps-msz.5.2)

Delayed listening on an app through the plugin: tapped muted, played `M` late, released by the
watchdog, tapped again (docs/design/unity-game.md, "Platform capture and crash safety", Shipped
(ps-msz.5.2)).

## Without the grant (2026-10-31, jarvis, MacBook speakers at 48 kHz)

```sh
cd native && cargo test -p plugin --test system_tap -- --ignored --nocapture
```

`afplay` plays a -50 dB 440 Hz tone after 1.5 s of silence; the test opens a System source on its
pid at 48 kHz, renders in real time and polls (the heartbeat), then stops polling for 2.5 s, then
polls again. The test binary has no System Audio Recording grant, so the tap captured silence
(peak 0), as native's unbundled taps do; everything else ran:

| Moment | State | Muted | Releases | Analysed ahead of the render | Stalls |
|---|---|---|---|---|---|
| 6 s in, tapped | playing | 1 | 0 | 2.99 s | 0 |
| after a 2.5 s heartbeat gap | stopped | 0 | 1 | 3.00 s | 0 |
| 1.5 s after polling resumed | stopped (resuming `M` later) | 1 | 1 | 2.95 s | 0 |
| 4 s later | playing | 1 | 1 | 2.95 s | 0 |

- The tap's rate was the device's (48 kHz), so the line needed no resampling.
- The watchdog released the tap 2.03 s after the last heartbeat (song 5.03 s, with the stall from
  song 3.0 s), reason "the main thread stalled for 2 s"; `meta.json` lists it.
- The tap came back within the first look after polling resumed (1 s after the release, then every
  0.25 s while the heartbeat is alive), and the music resumed `M` after that tap.

Hearing it is the operator's row in [operator-pending.md](../operator-pending.md): run the same
test from a terminal holding the grant with `PS_EXPECT_GRANT=1`, which fails unless the delayed
tone plays, and listen that the tone is never heard directly while tapped.

## Without any device: a hand-driven tap

`cargo test -p plugin --test system --test no_alloc` (in the fast and slow checks): the app's
frames render exactly `M` late, bit for bit; a release silences the render at once while the line
still holds `M`, the analyzer keeps `M` ahead on silence, and the re-tap's frames start where that
silence reached and play there; the record holds the analyzer's input, every entry and the
release; and `ps_render_music` allocates nothing through all of it.
