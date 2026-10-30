# 3D Tech Demo: Style Lab

A separate Unity project, `stylelab/`, for choosing how the demo's monsters
should look. It puts demo3d's horde (the same Magic Pig and Synty monsters and
the knight) into a small night arena with the song playing, and lets you cycle
through stylized shader treatments live. It ignores performance on purpose:
the question here is only what looks best.

Why it exists: the demo's monsters read as washed out and muted, while Deep
Rock Galactic: Survivor's swarms pop. The lab mocks up concrete answers side
by side, on our own monsters, moving to our music.

## Running It

On the operator's Mac, from a worktree's root. It needs what demo3d needs on
the Mac ([Running on the Mac](mac.md)): Unity 6000.6.7f1 with Mac IL2CPP
support, `uv`, ffmpeg, the vendor sources and the song.

```sh
# Setup: vendor files (a 64-entry subset of demo3d's manifest), the song, a warm Library.
uv run --project demo3d/tools stylelab setup

# Build: stylelab/Builds/mac/il2cpp/StyleLab.app. It bakes first (StyleLabData).
# `--backend mono` builds faster.
uv run --project demo3d/tools stylelab build

# Watch, with sound.
open stylelab/Builds/mac/il2cpp/StyleLab.app
```

You can also open `stylelab/` in the Unity editor and press Play in the
`StyleLab` scene, after `stylelab unity Bake`. Material values are easiest to
tweak live there.

### Keys

```
1-9, ← →, Space    pick a treatment (Space and → go to the next)
C                  next camera shot: Gameplay, Close, Low, Orbit
O                  the painted look's ink lines on or off
A                  auto-cycle: the next treatment every 8 bars
H                  hide the label
M                  mute or unmute
Esc                quit
```

### Flags

The app takes these after `--args` (`open ... --args --treatment 3`), or on
the binary inside the bundle:

- `--treatment N` and `--shot N` (both 1-based): what to start on.
- `--start-at S`: the song time to start from (default 37.2 s, the first
  drop). The song loops back there at its end.
- `--count N`: how many fodder monsters (default 260), plus 3 orks, 3 trolls
  and the dragon.
- `--sound`: a built player starts muted (`just stylelab` passes it; M toggles
  it). `--no-sound` skips the song and its effects entirely.
- For agents, windowless: `"$APP" -batchmode --stills DIR [--shots 1,2,3]
  [--treatments 1,...,9]` writes one tagged JPEG per shot and treatment,
  each drawn mid-beat (after the blows' hit flashes fade), then quits. All
  36 take about 20 seconds.

## The Treatments

Each is one shader under `stylelab/Assets/StyleLab/Shaders/`, with a palette
per species in `Looks.cs`. The beat drives all of them through a few globals
(`_SL_Beat`, `_SL_Pulse`, ...). The main one is the **beat wave**: a ring that
leaves the hero on every beat and sweeps out through the horde at 11 m per
beat. Glow, rims and sparkle flare as it passes, so the whole horde pulses
with the song.

1. **Demo3D today** (`Baseline`): the crowd shader's current look, for
   comparison. Vendor albedo through URP PBR at smoothness 0.2, the night's
   cool rim multiplied by the albedo, neutral tonemapping.
2. **Polished painted** (`Painted`): the Deep Rock Galactic: Survivor look.
   - The texture's colours are thrown away and only its light and dark kept,
     looked up in a colour strip per species (a gradient map).
   - Shadows turn a deeper, saturated hue, not grey.
   - A hard-edged highlight and a lacquer sheen from an analytic matcap.
   - A rim in a complementary colour.
   - Glowing spots that flare with the beat wave.
   - An optional ink line (`Outline`, key O).
3. **Living flame** (`FireBody`, `FireShell`): charcoal bodies split by
   molten cracks, inside a second, additive copy of the mesh pushed out into
   licking flames. Each species gets its own fire colour (blue skeletons,
   green goblins, red demons), and embers rise. Death burns them away from
   the feet up.
4. **Living crystal** (`Crystal`): faceted gems that refract the ground behind
   them with a little dispersion. Facet edges catch the light, some facets
   glint on the beat, and death shatters them into flying triangles.
5. **Spectrum** (`Synth`): a black lacquer body with a thin-film rainbow that
   sweeps out from the hero on every beat. Adds a neon wireframe on every
   triangle edge, equalizer bars that climb the body, and a bass swell.
   Death bursts into rainbow shards.
6. **Hologram** (`Hologram`): additive projections with fresnel edges,
   scanlines, a rolling band, red and cyan fringes and flicker. Glitch slices
   jump sideways on the off-beats, and death scans them out from the top.
7. **Liquid chrome** (`Chrome`): a mirror finish reflecting a synthwave world
   that the shader computes rather than samples: a sunset with a striped sun,
   studio strips overhead and a neon grid floor. A ripple runs over the
   surface on the beat, and death melts them into a puddle.
8. **Void** (`Void`): holes cut into the night. A starfield and nebula fixed
   to the screen fill each body, ringed by a flaring edge, and death implodes
   them to a point.
9. **Showcase mix**: one way to combine them, using the strange looks only
   for what should feel special:
   - painted skeletons, goblins and mushrooms;
   - crystal slimes, spectrum bats and fire demons;
   - chrome orks, void trolls and a fire dragon.

The hero always wears the painted look (or the baseline in treatment 1), so
the treatments compare monsters only. Each treatment also sets its own post
stack (`PostLook`): ACES tonemapping, with more contrast, saturation and bloom
than demo3d's. Hologram and Spectrum add chromatic aberration.

## What the Stills Show

Contact sheets of every treatment from each camera shot are in
[stylelab/evidence/treatments/](../../stylelab/evidence/treatments/)
(`gameplay.jpg`, `close.jpg`, `low.jpg`, `orbit.jpg`), with full-size low
shots of the baseline, the painted look and the showcase mix. These are first
impressions from stills; the app, with the music and motion, is the real
test.

- **Polished painted is the biggest single win.** It is the closest to Deep
  Rock Galactic: Survivor. Each species reads as its own colour at gameplay
  distance, and the sheen and rims carry the gloss.
- **Spectrum and Hologram feel the most like a rhythm game**, since the beat
  wave is so visible in them. Both get busy in dense packs: spectrum's
  wireframe and death shards fill the air, and overlapping holograms add up
  toward white.
- **Living flame works once each species has its own fire colour.** Its
  flames are additive, so dense packs needed toning down to keep from
  blowing out. The dragon's wings needed their own, lower intensity.
- **Crystal, Chrome and Void pay off on big monsters.** Up close the gem
  facets, mirror reflections and starfields read well. At gameplay distance
  the fodder looks like pastel flat shading, dark bronze or outlines.
  That's why the mix gives these looks to the elites.

## In demo3d

The treatments also run in the real demo, with its arena, effects, camera
and song (epic ps-a54): `--monster-style <name>` on demo3d's player, with
the names baseline (the default: demo3d as it is), painted, fire, crystal,
spectrum, hologram, chrome, void and mix (the showcase mix). For example, on
the Mac:

```sh
open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args \
  -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --sound --monster-style mix
```

The look harness takes it through `--player-args "--monster-style fire"`.
demo3d has its own copies of the shaders and palettes, drawn through the
crowd's batched indirect draws; neither project depends on the other.
demo3d tunes each treatment's post look and brightness for its own darker
arena and hot effects, and `--style-tune` tries other values for a run.
[crowd.md, Monster styles](crowd.md#monster-styles) has how it is built, and
[demo3d/evidence/styles/](../../demo3d/evidence/styles/) has a still set per
treatment and a contact sheet across all of them (`all/`).

## How It Is Built

- **Separate project, shared tools.** `stylelab` is a third entry point over
  `demo3d/tools`, beside `demo3d` and `demo2d`: the same setup, sync,
  guard, Unity runner and build, and the same per-machine cache.
- **The roster is demo3d's.** `Data/roster.json` is a copy of demo3d's
  `Assets/Demo3D/Data/roster.json` minus the rejected Evil God, so its GUIDs
  match the synced vendor files.
- **The bake** (`stylelab unity Bake`, also run by every build) writes
  `Assets/Baked/Resources/StyleLabData.asset`, gitignored. For each type:
  - it spawns the prefab with its weapons;
  - it merges every renderer into one skinned mesh in root space, with a
    submesh per albedo texture (the Magic Pig skeleton alone has 148
    renderers);
  - it unwelds that mesh so every triangle has its own vertices (see Shaders
    below);
  - it evaluates InfinityPBR's colour-ID shader into a plain albedo texture,
    as demo3d's CrowdBaker does;
  - it measures the albedo's value range over the texels the mesh actually
    uses, to set where the painted look's colour strip starts and ends;
  - it builds an animator controller from the roster's clips.

  The bake also stores the song, its beats and energy, and every StyleLab
  shader, so the build includes them.
- **The player** builds everything at runtime from that asset, in an empty
  scene: the arena (a procedural flagstone floor, crystal clusters and
  rocks), the lights, the post volume, the horde and the hero.
  - Monsters step in on the beat and hold a 2.5 m clear ring round the hero.
  - The hero lands a blow on every beat: alternating slashes, a spin every
    other downbeat, and a slam every fourth bar.
  - Dead monsters come back from the edge.
- **Shaders.** The style mesh carries what the treatments need:
  - barycentric coordinates and a random value per triangle, for wireframes,
    facet glints and shattering;
  - the bind-pose position in metres, so noise stays stuck to the body while
    it animates;
  - the face normal, stored in the tangent so skinning turns it with the
    bones.

  Each monster's hit flash, death progress and seed come in through a
  `MaterialPropertyBlock`. A second skinned renderer per monster, the shell,
  draws the fire's flames or the ink line.

## Vendor Assets

The 3D demo's rules apply in full ([Vendor Assets](vendor-assets.md)). The
guard covers `stylelab/` like the other demos, and its `Assets/Vendor/`,
`Assets/Baked/`, builds and Library are gitignored. Everything else in the
lab is our own: the arena, props, effects and shaders are generated or
written here. Stills under `stylelab/evidence/` carry the `ps-render` tag.
