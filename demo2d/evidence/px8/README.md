# PX8: hero and presentation (2026-10-30)

Windowless stills from the IL2CPP Mac player (operator's Mac, Metal, 1920×1080), drawn at fixed
song times:

```
APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
"$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px8" \
  --stills-at 10,30,37.125,50,166,170,176,180,185,195.928,197.476
```

The IL2CPP timeline differs a little from the dotnet tests' (as PX5 found), so times below are the
player's where they differ.

## The first minute

- [10 s](still-010.000.jpg), [30 s](still-030.000.jpg), [50 s](still-050.000.jpg): the intro's
  skeletons, the groove's first wave, the first drop.
- [37.125 s](still-037.125.jpg): 0.03 s after the first spin (37.095 s, an accent), at the flash's
  peak. The struck ring of skeletons is lit warm by the blow, every one still readable, where PX7's
  white flash made one white mass (its notes; its stills sat 0.15 s after each event to avoid it).

## The climax and the boss

- [166 s](still-166.000.jpg): the boss devil has entered from the cliff ledge's side (south-east) and
  walks in; [170 s](still-170.000.jpg), [176 s](still-176.000.jpg), [180 s](still-180.000.jpg),
  [185 s](still-185.000.jpg): it stands off the hero's right in the crowd and casts hellfire on beats
  (11 attacks in the dotnet timeline, from 174.6 s), the hero parrying each where it lands.
- The beam's sweep at 173.4 s ticks monsters with a half-strength flash and no knockback or hop
  (`PresentationTests.BeamTicksFlashMonstersWithoutKnockback`, 144 ticks).

## The ending

- [195.928 s](still-195.928.jpg) and [197.476 s](still-197.476.jpg): 0.03 s after the ending's two
  accents, each a hero slam (the only two accents in the section). The first fells half of what is on
  screen, the last the rest and the boss, each lit by its flash as it falls.

## Sound

`--dump-sounds` on the player: 2,933 events over the song, every one with a baked clip (24 sound
names from demo3d's packs): 876 deaths, 780 parries, 526 hits, 347 bolts, 182 swings, 121 rings,
99 lightning strikes, 2 laser sweeps. They play scheduled on the mixer's clock in live play; the
listening check is the operator's (plan.md, Pending on the operator).
