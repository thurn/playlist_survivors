# ps-6y2.1: the project skeleton

Measured on jarvis (M5 Max), 2026-11-02, with another bead's slow CI running.

- `just champions --headless --seconds 5`: set up already, nothing to rebuild, the player exits 0
  after 7.8 s on Metal (`Boot: Unity 6000.6.7f1, Metal, batch mode True, headless`).
- [boot-001.000.jpg](boot-001.000.jpg): `just champions-play --headless --fixed-time
  --capture-stills DIR --stills-at 1`, kept at t=1.000 and shrunk to 960 px. Every pixel of the
  1080p original is 0: the black void.
- `champions check --variants`: a clean-cache Mono build in 26 s, 227 compiled variants of the
  300 budget. The largest: CoreBlit 56, UI Toolkit's UIRDefault 40, CoreBlitColorAndDepth 24,
  CopyDepth 16, Bloom 16. Before the stripping beyond URP's own, the same build kept 639.
- An incremental build reuses the last build's shaders and processes none, so the budget keeps
  the last counts (227 again) rather than counting 0.
- `champions setup` in a fresh cache: 792 vendor files synced in 220 s (the Synty packages
  unpacked once), the record of 441,496 vendor files, the licence verified, 250 s in all.
- EditMode tests: 7 of 7 in 18 s, cold, with the sim core's code (ps-6y2.3) compiled under the
  Champions.Sim assembly.
- The guard over the index: 12 s (the frozen demo3d guard: 18 s).
- A cached Library keeps `Library/Bee`: a project copy at another path, whose source path was
  then deleted, built its own edited Boot.cs (its player logged the copy's message).
