# Roster sets: heroic (ps-f8a.3)

`--roster heroic` with all nine slots filled from
HeroicFantasyCreaturesFullPackVol1, drawn by the stock crowd with the pack's
own URP Lit materials (albedo, normal, metallic/smoothness and AO). The hero
stays the Synty knight.

## The slots

| slot | creature | height, radius | clips (plain, never _RM) | notes |
|---|---|---|---|---|
| skeleton | Skeleton Knight | 1.8 m, 0.4 | 1HandedWalk, 1HandedAttack1, 1HandedAttack2, 1HandedGetHit, 1HandedDeath, Idle1Handed | sword, shield and helmet ride the bones; the two-handed sword is hidden |
| goblin | Goblin | 1.5 m, 0.4 | the SwordShield set (from ps-f8a.1) | daggers, slingshot and stone hidden |
| slime | Giant Rat | 0.7 m, 0.6 | walk, JumpBite, Taunt, GetHitFront, Death, Idle | the pack's only attack is JumpBite, so attack2 is the taunt |
| bat | Harpy (covered) | 1.25 m, 0.6 | Fly, Attack1, Attack2, FlyGetHit, Death, Fly | the flyer: every clip is airborne and Death falls to the ground, so no `hoverM` |
| mushroom | Kobold | 1.4 m, 0.4 | WalkNormal, Attack2, Attack3, GetHitFront, Death, IdleCombat | stone axe and shield; Attack1 barely moves the axe |
| demon | Ghoul | 1.7 m, 0.45 | Walk, Attack1, Attack2, GetHit1, Death, Idle | |
| bigork | Fat Ogre | 2.9 m, 0.9 | Walk, Attack1, Attack2, GetHit1, Death, Idle | morning star |
| troll | Troll | 3.1 m, 0.9 | Walk, Attack1, Attack2, GetHit1, Death, Idle | |
| dragon (boss) | Mountain Dragon | 4.5 m, 1.8 | Walk, ClawsAttackLeft, ClawsAttackRight, GetHitFront, Death, IdleBreathe | chosen over the Demon Lord, whose flames need a vendor script |

The slots favour distinct silhouettes at gameplay distance: an armoured
skeleton, a small green humanoid, a long low rat, a winged harpy, a furred
kobold, a pale gaunt ghoul, then the fat ogre, the tall green troll and the
dragon.

The radii now reach the choreography: under a set, each slot's radius
replaces Choreography/Data/roster.json's (crowd.md, Roster sets), so the
rats and harpies no longer overlap at the default's 0.35-0.4 m.

## Clip annotations

`demo3d unity AnnotateClips -roster heroic -evidence evidence/rosters/heroic/clips`
writes `rosters.heroic` in clip-annotations.json. The contact sheets are in
`clips/`. Three corrections, all recorded in roster-heroic.json:

- **The Harpy's stride** is the roster's 2.2 m (`strideM`). Its Fly loop is
  in place with no root-motion twin, and the feet's range gave 0.155 m,
  which would flap ten times too fast.
- **The Kobold's walk** measures its stride from WalkNormal_RM
  (`rootMotionClips`, measured only, never played): 1.01 m.
- **The skeleton's attack impact** is 0.22 s (`impact`), where the thrust
  lands on the 16-frame sheet. The peak hand speed came at 0.10 s, in the
  wind-up.

## Texture memory

The import cap (RosterTextureImport) holds the pack's 4K sources at 1K for
the fodder and 2K for the elites and the boss. It applies at import, so no
vendor file changes. `texture-2k-vs-1k.jpg` is the evidence: the 145 s close
still with every map at 2K (left) and at 1K (right). The ogre's and the
troll's hides soften at 1K when they fill a third of the frame. At the same
distance the fodder looked no different (the whole frame's mean grey difference stayed under 1.6 of 255).

| cap | heroic texture memory (85 textures) |
|---|---:|
| the pack's own import settings (2K, two at 4K) | 568 MB at a uniform 2K cap |
| 1K everywhere | 168 MB |
| **chosen: 1K fodder, 2K elites and boss** | **264 MB** |

Numbers are from RosterBaker's log (Profiler runtime size of every map the
set's own monsters' materials use).

## Frame rate (windowed IL2CPP player on the Mac, 1920x1080)

`--roster heroic --song-check song-check.json --stats stats.csv`: uncapped,
the whole song (0.9-199.0 s), runs of about 84,000-92,000 frames.

| run | build | worst 20 s window p99 (GPU / main / render) | slowest frame after warm-up | frames over 16.7 ms in the 0.5 s stats samples |
|---|---|---|---|---|
| 1 | first build | 5.8 / 4.6 / 2.5 ms | 41.8 ms main | 2 (42.5 s, 85.7 s: a type's slots made in one frame) |
| 2 | slots made at load | 6.2 / 5.0 / 2.5 ms | 15.3 ms main | none |
| 3 | final texture caps, new build | 4.9 / 7.6 / 2.6 ms | 23.9 ms main | 1 (106.2 s, Ghoul and Ogre first drawn) |
| 4 | same build as 3, run again | 6.2 / 4.7 / 2.5 ms | 18.0 ms main | none |

- **60 fps holds through the whole song:** the worst window's p99 is under
  8 ms against 16.7 ms. The slowest 20 s window still averaged about 240 fps.
  The song check's own `pass: false` is against Cygnus's 4.2 ms GPU line,
  not 60 fps.
- **The hitches were slot creation.** The stock crowd made a section's worth
  of heavy prefabs (Kobold: 11 renderers) in one frame. With a set it now
  makes them at load (crowd.md), which removed the 25-40 ms frames.
- **What's left is one isolated frame per run (18-24 ms).** On a fresh
  build it lands where a type is first drawn, which fits Metal compiling
  that material's pipeline. Each would be one missed vsync. A smaller
  envelope doesn't help, since these frames don't scale with the count.
- **The envelope stays at 150 on screen at 18 m** (the epic's start). The
  p99 has three times the headroom, but the slowest frames are already
  near 16 ms, and the final peak is dense at 18 m (look/song-175.jpg).
  The peak was 115-130 alive on screen against the final peak's target of
  158.

## The final look (ps-f8a.4)

`look/` and `close-*.jpg` were re-rendered with the set's own look:
arena-layout.json's `rosters.heroic` (a cooler moon, 0.86, 0.9, 1.0 at 1.15;
exposure 0.65, saturation 14, contrast 32, temperature -10) and corpses
that sink into the ground (crowd.md, Roster sets). The warm grade had
turned the browns of leather, bone and fur toward the ground's orange; the
cooler, less saturated grade keeps them apart, and the normal and AO
detail reads in the close stills. The song stills' mean chroma fell by
about 6-13 (song-100: 42.1 to 30.2) while every beauty shot still passes
readability (|dL*| 39-45) and the effects stay the brightest pixels.
Windowless probe at 60, 100, 145 and 175 s, twice
([gpu-probe-final.json](gpu-probe-final.json), [2](gpu-probe-final-2.json)):
medians 3.9-12.6 ms, the slowest 20.5 ms at 175 s on the second run. The
probe times CPU submission and GPU in series and the Mac was under other
sessions' load (load average about 28), so it is an upper bound; the grade
changes no pass. The windowed song check above stands, and the operator's
row asks for a windowed check.

## Stills

- `look/`: `demo3d look rosters-heroic --baseline final --player-args
  "--roster heroic"`: the song stills and TD15's beauty shots, the default
  (final) left and heroic right in `look/contact-sheet.jpg`, numbers in
  `look/look.md`. Every beauty shot passes readability: monster |dL*| is
  43-48 against the default's 21-27. The vendor materials are darker and
  more contrasted than the dyed crowd, with monster chroma within 4%.
- `close-*.jpg`: the same song at `--view-width 8 --no-effects` (the camera
  about 2.25 times closer), where the normal, metallic/smoothness and AO
  maps read: the ogre's skin and strap, the troll's hide, the skeleton's
  rusted helmet and shield, the goblins' armour.
- `clips/`: the annotator's contact sheets, with the impact frame framed.

Commands, after `demo3d setup --roster heroic`, `demo3d unity BakeAll`,
`demo3d unity BakeRoster -roster heroic` and `demo3d build`:

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile "$PWD/captures/rosters/close/player.log" --roster heroic \
  --view-width 8 --no-effects --fixed-time --capture-stills "$PWD/captures/rosters/close" --stills-at 60,100,145,175
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --roster heroic \
  --song-check "$PWD/captures/rosters/perf/song-check.json" --stats "$PWD/captures/rosters/perf/stats.csv"
```

The song check needs a windowed player: a batch-mode player draws no frames.
