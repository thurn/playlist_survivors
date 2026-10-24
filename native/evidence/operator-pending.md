# Checks waiting on the operator

Checks an agent can't finish alone: they need a click, a device, the speakers on, or an ear.
Clear them in one sitting; the consuming bead re-runs them and records the result, then removes
the row. Add a row whenever a bead records something as pending on the operator
(AGENTS.md, Native app).

| Check | What the operator does | Command | Recorded by | Consumed by |
|---|---|---|---|---|
| Play the slice (V1) in System mode | Click Allow on the System Audio Recording prompt at first launch, play music in any app, play with speakers on: arrows or WASD move, Space (or J, K) attacks on the beat, Space or Enter restarts; check the sprites, effects and sound effects (V2) look and sound right (File mode is the fallback: `--file <path>`) | `native/scripts/bundle && open native/target/PlaylistSurvivors.app` (File mode: `native/target/PlaylistSurvivors.app/Contents/MacOS/playlist-survivors --file public/audio/orpheus-galop.flac`) | V1 (`hv-t8o8.62`) | V1 follow-ups |
| System Audio Recording for the app | One click on the first capture prompt (the bundle ID changed from the spike's, so the old grant doesn't apply) | Launch `PlaylistSurvivors.app` from `native/scripts/bundle` in System mode (once C3/C4 capture) | F4 (`hv-t8o8.5`) | C4 (`hv-t8o8.24`) |
| A tapped app is silent, and audible after its tap is destroyed or its IOProc stopped | Speakers on and unmuted, room quiet for about a minute | `spike mute` (the spike at `f37435a`: `git show f37435a:native/spike/README.md`), or E2's replacement | ES (`hv-t8o8.36`), [evidence](es/) | E2 (`hv-t8o8.32`) |
| A source is audible after `SIGKILL` of the process holding its private tap | As above | `spike sigkill` with `PS_CHILD_PRIVATE=1` (at `f37435a`), or E2's | ES | E2 |
| The source is audible again after the watchdog destroys its tap | As above | `spike watchdog` (at `f37435a`), or E2's | ES | E2 |
| Delayed listening is heard `M` late (after subtracting the tap-versus-render offset) | As above | E2's delay line, measured by loopback | ES | E2 |
| Output latency on wired headphones and AirPods | Connect each device | `ps` latency report (D5); the spike's `spike devices` at `f37435a` | S0 (`hv-t8o8.1`) | D2 (`hv-t8o8.26`), D5 (`hv-t8o8.29`) |
| Delay-line drift across two devices with the resampler | Connect a second output device (AirPods or USB) | E2's delay line over an hour | ES | E2 |
| Key latency on real presses (HID and window server) | Press keys when asked | D5's latency report | S0 | D5 |
| Flash and click land on the beat, by ear and eye | Watch and listen to a steady track | The app in File mode on house-128 | S0 | D5 |
