# HeroEditor in Native: Evidence (ps-4c8)

native's survivors run under `--art heroeditor`: the HeroEditor knight and
undead horde (`ps-4c8.1`'s capture, baked by `ps-4c8.3`) on the painted dusk
ground (`ps-4c8.2`), beside native's own LPC look at the same moments. How it
works: [docs/archive/design/heroeditor-native.md](../../../docs/archive/design/heroeditor-native.md).

Every image is headless output; no window was opened. The HeroEditor frames in
them are vendor-derived, so the images are small tagged renders (ps-render),
as the vendor rules allow. The capture's contact sheets and face lineup are in
[capture/](capture/README.md), the ground's stills in [ground/](ground/README.md).

## Commands

```sh
just herocap                       # capture every recipe and bake the atlas into the cache
just native --art heroeditor       # play it

# The evidence below (from the repository root, after `cargo build --release -p app` in native/):
uv run --project demo3d/tools python native/scripts/heroeditor_evidence.py pair OUT.jpg --track T --at S
uv run --project demo3d/tools python native/scripts/heroeditor_evidence.py strip OUT.jpg --track T --at S \
  --bars 2 --per-beat 6 [--crop W H --shift X --lift Y --scale K]
```

`pair` and `strip` drive the app's `--still` on a bundled track's committed
stream (a stride on each beat, the first card at each level-up), so every
frame is the same run, re-simulated to its time.

## Paired Stills (LPC left, HeroEditor right)

- `pair-quiet.jpg`: house-128 at 46 s, a quiet minute: the knight and a few
  wraiths and ghouls.
- `pair-dense.jpg`: cc-zep-hurme-hang-low at 4:45, about 110 alive: zombie
  militiamen (the skeletons) closing in.
- `pair-boss.jpg`: cc-zep-hurme-hang-low at 2:15: the death lord (the
  minotaur boss) on the knight, wraiths (the bats) massed behind.
- `pair-densest-moving.jpg`, `pair-densest-deaths.jpg`: `--density-check`'s
  minute 25, 300 enemies, 400 gems and six evolved weapons, moving and killing.
- `faces-closeup.jpg`: in-game close-ups (2x) of the knight (two dots, a short
  line, flat brows) beside the death lord, and zombie militiamen (soft green
  glows, no mouth): decision 7 holds in the run, as in the capture's lineup.

## Filmstrips (two bars, six frames a beat; per beat, LPC above, HeroEditor below)

- `strip-hero.jpg`: the knight's swings and steps at 2:31 of
  cc-zep-hurme-hang-low. Each swing winds up (the sword raised behind), lands
  its strike frame on native's strike (the sword level, the white smear at its
  brightest), and follows through over the frames native's LPC swing takes;
  the jab (native's backslash) alternates with the slashes. The 30 fps
  frames are timed by the sim's continuous phase, so every frame of a clip
  plays.
- `strip-horde.jpg`: the wraiths massed at 2:14 of the same song, stepping on
  the beat with their hover and squash, beside native's bats.

## Whole-Run Acceptance (2026-10-31, Apple M5 Max)

- **One command sequence.** `just herocap` (setup, capture, bake), then
  `just native --art heroeditor`. The bake takes about 3 s: 13 sprites,
  1104 frames, one 4096-wide page in
  `~/Library/Caches/playlist-survivors/demo3d/heroeditor/assets/`.
- **Every role is HeroEditor.** Every atlas the survivors run uses has a
  recipe and a baked sprite (hero, skeleton, slime, bat, bee, orc, orc-elite,
  orc-ember, brute, minotaur, both thorn plants, and goblin, the fallback); the
  roster check's stills and every image here show no LPC sprite or placeholder
  square under the flag. (The eyeball and skeleton archers, which the
  survivors run never spawns, keep their LPC art.)
- **Faces** match decision 7 (`faces-closeup.jpg`, capture/faces.png).
- **Smooth at speed:** the filmstrips; walk cycles are 20 frames a cycle where
  LPC's are 8, swings 16 to 21 where LPC's are 6 to 8, and a death plays 16
  frames over native's 0.5 s fall from the killing blow.
- **The default is unchanged:** without the flag, `--still` (house-128 at 20 s)
  and `--still --still-track cc-zep-hurme-hang-low --still-at 150` are
  byte-identical between the base (f70d9b62) and this change. Nothing reads
  the cache without the flag, so a machine without HeroEditor runs as before;
  with the flag and no bake, the app stops with
  "`just herocap` captures and bakes them".
- **Smoke:** `--file house-128.ogg --headless --seconds 30` passes with and
  without `--art heroeditor` (exit 0, the run over after 27 s, no late or
  dropped sounds either way).
- **60 fps:** `--bench-density` (minute 25, 300 enemies, six evolved weapons,
  1920 x 1080), at full clock:

  | | median | p95 | p99 | max |
  |---|---:|---:|---:|---:|
  | default | 0.61 ms | 2.20 ms | 2.64 ms | 3.26 ms |
  | `--art heroeditor` | 0.66 ms | 2.12 ms | 2.66 ms | 3.87 ms |

  The characters cost the CPU nothing measurable; on the GPU they are the same
  instanced sprites from one page, sampled linearly, beside the ground's 0.7 to
  2 ms (ground/README.md): well inside 16.7 ms.
- **Operator:** a row in [../operator-pending.md](../operator-pending.md) asks
  for the feel judgement.

## Feel Pass

Judged from the filmstrips, the roster check's per-role stills and clips
(`--roster-check <dir> --art heroeditor`) and the paired stills:

- **The beat step** was HeroEditor's Jump clip, which holds one pose: the
  knight slid. It is the run cycle now, one stride a step.
- **Swings** read but lacked LPC's white crescent, drawn in those frames: a
  smooth additive smear now sweeps the slashes around the strike.
- **Timing:** the hero's swings were first stepped by native's integer LPC
  frame (6 steps a swing): the sim now reports each pose's phase, and the
  wind-up and follow-through map onto HeroEditor's frames either side of the
  impact frame.
- **Size:** each role stands as tall as its LPC sprite, so the crowd's density
  and the sword's reach read as before; the wraiths, ghosts and ghouls keep
  their recipe's scale against the hero, since their LPC sprites were blobs.
- **Left for the operator:** in the infernal zones the wraiths' dark cloth is
  dark on the dark red dusk ground; their glows and hoods carry them, where
  LPC's bats were bright pink. The thorn-plant shamans stand as small as the
  LPC prop (about 0.6 of the knight). Both are a recipe change and a
  re-capture if the operator wants them brighter or bigger.
