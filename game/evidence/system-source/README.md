# The plugin's System source on the Mac (ps-msz.5.2)

Delayed listening on an app through the plugin: tapped muted, played `M` late, released by the
watchdog, tapped again (docs/design/unity-game.md, "Platform capture and crash safety", Shipped
(ps-msz.5.2)).

## Without the grant (2026-10-31, jarvis, MacBook speakers at 48 kHz)

```sh
cd native && cargo test -p plugin --test system_tap -- --ignored --nocapture
```

`afplay` plays a -50 dB 440 Hz tone after 1.5 s of silence; the test opens a System source on its
pid at 48 kHz, renders in real time and polls (the heartbeat) until the music plays, then stops
polling for 4 s (the watchdog's 2 s and room), then polls until the app is tapped again and the
music resumes. It waits on the session's status, never on a clock (ps-msz.5.5: under slow CI,
other audio on the machine stalled the capture, and the extra releases it caused failed the
earlier fixed-time version). The test binary has no System Audio Recording grant, so the tap
captured silence (peak 0), as native's unbundled taps do; everything else ran (ps-msz.5.5's run):

| Moment | Song time | State | Muted | Releases | Analysed ahead of the render | Stalls |
|---|---|---|---|---|---|---|
| the music plays, tapped | 0.00 s | playing | 1 | 0 | 3.00 s | 0 |
| after a 4 s heartbeat gap | 4.00 s | stopped | 0 | 1 | 3.00 s | 0 |
| polling again: tapped again | 4.04 s | stopped (resuming `M` later) | 1 | 1 | 2.96 s | 0 |
| the music resumes | 7.02 s | playing | 1 | 1 | 2.94 s | 0 |

It passed 3 of 3 runs with all 18 cores busy, and 9 of 9 with three runs at once on the same
device.

- The tap's rate was the device's (48 kHz), so the line needed no resampling.
- The watchdog released the tap about 2 s after the last heartbeat (2.03 s in ps-msz.5.2's run),
  reason "the main thread stalled for 2 s"; `meta.json` lists it.
- The tap came back at the first look after polling resumed (1 s after the release, then every
  0.25 s while the heartbeat is alive), and the music resumed `M` after that tap.
- A suspended app is no capture stall (ps-msz.5.5, a scratch run on a `kill -STOP`ped `afplay` for
  3 s): the tap went on capturing silence on the device's clock, so there was no release, the app
  stayed muted and the music playing (silence) through the suspension and after `kill -CONT`.

Hearing it is the operator's row in [operator-pending.md](../operator-pending.md): run the same
test from a terminal holding the grant with `PS_EXPECT_GRANT=1`, which fails unless the delayed
tone plays, and listen that the tone is never heard directly while tapped.

## Without any device: a hand-driven tap

`cargo test -p plugin --test system --test no_alloc` (in the fast and slow checks): the app's
frames render exactly `M` late, bit for bit; a release silences the render at once while the line
still holds `M`, the analyzer keeps `M` ahead on silence, and the re-tap's frames start where that
silence reached and play there; the record holds the analyzer's input, every entry and the
release; and `ps_render_music` allocates nothing through all of it.
