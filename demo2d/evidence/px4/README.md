# PX4 evidence: 2D choreography data

The song's 2D timeline from `demo2d/Assets/Demo2D/Data/` (the catalog, placeholder clip
annotations, the open-ground layout and the 2D level script), built by the shared
`PS.Choreography` core under .NET on the operator's Mac (Apple silicon).

Run: `dotnet test demo2d/dotnet/Demo2D.Choreography.Tests --nologo -v q --logger "console;verbosity=detailed"`

## Result

- **18 of 18 tests pass**, in 1.6 s.
- **Build time:** 0.323 s for the first build, 0.126 s for the second (the test takes the
  best of two, against the 3 s budget).
- **The timeline:** 4,804 entities, 117,173 segments, 14,494 actions, 11,742 effects, 3,381
  sounds, 11,940 framing samples. The dump is byte-identical across builds (19,019,296
  characters, FNV-1a `0cb7d2bd8a142cc2`).
- **The core's checks** (`TimelineChecks`, shared with demo3d's tests) all pass: ids and
  ordering, impacts on the grid, gait steps on beats, smooth free sections, deaths per second
  within 10% of each preset, no overlap past a quarter radius (worst 0.101 over 1,542
  samples), every hit and death with its clip and effect, spawns outside the view, at most 3
  voices per beat and category, every attack's fitted clip timing, and the zoom's easing.
- **Casters:** 1,610 bolts from the skeleton and blood mages, each leaving on its attack's
  beat and landing, parried, on an eighth note.
- **Every roster type appears:** skeleton 1,056, ghoul 679, flame imp 354, zombie warrior 316,
  ice skull 170, mask 125, skeleton mage 60, blood mage 37, ogre 20, werewolf 20, cyclops 6,
  lava beast 2, the boss devil 1.

## Sections

On screen counts monsters alive and dying in the 2D camera's rectangle (30 × 16.9 m at the
base view, 40.5 × 22.8 m at the climax).

| section | feel | s | load | target N | mean on screen | max | ratio | deaths/s target | deaths/s | goal reached |
|---|---|---|---|---|---|---|---|---|---|---|
| intro | pulsed | 0.0–12.4 | light | 3 | 10 | 60 | – | 0.08 | 0.08 | 0.0 s |
| groove | pulsed | 12.4–34.1 | light | 60 | 62 | 65 | 1.04 | 1.50 | 1.52 | 31.4 s |
| break-1 | free | 34.1–37.2 | light | 60 | 102 | 160 | – | 1.50 | 1.61 | – |
| drop-1 | pulsed | 37.2–74.3 | demo | 160 | 166 | 180 | 1.04 | 8.00 | 7.94 | 58.1 s |
| breakdown | pulsed | 74.3–86.7 | light | 120 | 151 | 202 | – | 3.00 | 2.91 | – |
| groove-2 | pulsed | 86.7–111.5 | demo | 200 | 186 | 216 | 0.93 | 10.00 | 10.01 | 108.8 s |
| build | pulsed | 111.5–120.8 | demo | 200 | 173 | 180 | 0.87 | 10.00 | 9.47 | no |
| break-2 | free | 120.8–123.9 | light | 160 | 213 | 298 | – | 4.00 | 3.87 | – |
| drop-2 | pulsed | 123.9–161.0 | demo | 320 | 308 | 378 | 0.96 | 16.00 | 15.98 | 152.5 s |
| final-peak | pulsed | 161.0–185.8 | demo | 384 | 399 | 414 | 1.04 | 19.20 | 19.21 | – |
| outro | pulsed | 185.8–195.1 | light | 200 | 346 | 394 | – | 5.00 | 4.95 | – |
| ending | free | 195.1–199.0 | light | 100 | 241 | 318 | – | 2.50 | 95.31 | – |

- **Held sections** (pulsed, after the opening, with a target at least the previous
  section's; ratio shown) stay within 13% of their targets. The shared check's tolerance is
  demo3d's: 25%, since demo3d's own song averages 77% of its target in `build`.
- **The others** are left out by design, in both demos: the opening's horde walks in, free
  breaks overfill as the reserve walks in smoothly, the breakdown and outro drain the bigger
  section before them at their load's death rate, and the ending clears the screen on its
  accents.
- **The climax** averages 399 on screen at 40.5 m (1.35 × the base 30 m view). The level
  script asks for 0.96 of the 400 envelope because the sim overfills it by about 4%.

## demo3d stays byte-identical

`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song --out song.json`
gives SHA-256 `09945e3b0a2932713c6d4fd00743bc81bc3cf648241b9cd961a60c804e1cdadd` before and
after PX4's core changes, PX0's hash. demo3d's dotnet tests: 122 passed (PX0's 121, plus the
section-count check).
