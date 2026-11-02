# The hero and the bosses in the trailer's edge look (ps-6y2.29)

The knight, the Fat Ogre and the dragon drawn as the neon edge trailer drew them (`captures/trailer-action-edge.mp4`,
the look package at git 138684f2), in play, in the hero scene and in both duel scenes; no stand-ins where the vendor
files are baked. Judged by eye beside the trailer's frames; no number here passes or fails anything.

## What draws them

- **The knight**: the trailer drew it with the look package's HeroEdge (its hero look `edge`) on the knight's faceted
  meshes, holding demo3d's crystal blade in place of the broadsword and the shield on the left hand bone as its
  prefab comes. Ported here as `Resources/Crowd/HeroEdge.shader` on the crowd's skinning: smoked glass a little lit in
  hero.body, a fresnel rim, the feature lines (1.4 px at the capture's 2160 rows), a chest core, and the blade's every
  edge with its spine light and blow flare; its gains are `HeroGains` (pinned with the shader for `--look trailer` by
  MonsterLookTests). The trailer's demo3d set no scene exposure, so its hero level, exp2(0.8), is a constant.
- **The knight's body** is baked like a monster type (`monsters.json`'s `hero`, a `figure`; `champions monster bake hero`):
  the vendor knight, the crystal blade (`Editor/Monsters/CrystalBlade.cs`, demo3d's mesh and grip, nothing
  vendor-derived in it) and the shield combined, the shatter's pieces, and a rig of its twelve clips' local poses at
  60 Hz (`MonsterRigBaker`, the deleted edge cuts' rig baker ported; its own check composes the rig against the
  sampled matrices). `HeroPresenter` poses it on the CPU each frame (`RigPose`: the legs' run under the swings, the
  torso twisted to the aim, ps-6y2.20's `HeroMotion` unchanged) and `MonsterFigure` draws it. Hurt colours it
  hero.hurt once; its death breaks it into white shards (`MonsterShatter` seeded from the figure's palette).
- **The bosses** are the crowd's Fat Ogre and dragon (ps-6y2.28's types), which `BossPresenter` writes into the
  trailer's crowd each frame with `BossAnimator`'s clips (ps-6y2.17's timing unchanged) and breaks on its death's
  frame into the crowd's shatter. The duel camera now eases out from the boss's call, four seconds before it lands,
  so the boss lands whole in the frame (it eased out from the landing, which cut off the Ogre's head and the dragon's
  wings on the entrance frame).
- **The duel scenes** draw the real knight (it was a white ring) and the skeletons in the crowd (they were capsules),
  so the entrance shatters them outward. `duel-ogre-death` and `duel-dragon-death` play the duel with Sword Slash
  against a boss of 4 health, for its death: the Ogre falls at 12 s, the dragon at 9 s.
- `--look trailer` covers the hero and the bosses: its stills of `hero`, `duel-ogre` and `duel-dragon` are bit for
  bit the default's (97 of 97), as they must be until the default is tuned.

## Stills

From `champions stills --scene hero --scene duel-ogre --scene duel-dragon --scene duel-ogre-death --scene
duel-dragon-death --scene level` at 1080 rows, under the trailer's grade:

- [knight.jpg](knight.jpg): the trailer's knight (top: 1.0, 1.6, 55 and 58 s) beside the game's (a slash, 0.08 s
  after an impact, in a level 1 surge, in the dragon's duel).
- [dragon.jpg](dragon.jpg): the trailer's dragon at 55 and 58 s beside the game's breath and dive; the trailer's
  dragon shatter beside the game's.
- [ogre.jpg](ogre.jpg): the trailer has no Fat Ogre; its brutes (the counter family) above the game's Ogre's smash
  and stomp, its death's frame and its pieces 0.75 s on.
- [boss-deaths.jpg](boss-deaths.jpg): the trailer's dragon death at its break and 0.07, 0.2 and 0.47 s on, above the
  game's dragon and Ogre at their break, 0.25 and 0.75 s on and after.
- [hero-hurt-death.jpg](hero-hurt-death.jpg): the hurt flash, then the death's frame and the white shards.
- Full frames: [duel-ogre-entrance](duel-ogre-entrance.jpg), [duel-dragon-entrance](duel-dragon-entrance.jpg),
  [duel-ogre-smash](duel-ogre-smash.jpg), [duel-dragon-breath](duel-dragon-breath.jpg),
  [level-surge](level-surge.jpg); the trailer's [1 s](trailer-1s.jpg) and [55 s](trailer-55s.jpg).

## Blind critique

An agent given only these images and the aim (retro 3D neon wireframe: glowing edges of a 3D model connecting into
closed facets and a closed silhouette, dark inside; the knight in white the brightest thing on screen), no docs:

> **Knight: close, a little worse.** In knight.jpg the game's lines (bottom row) are thinner and more broken than the
> trailer's (top row). In "game: slash" the torso has a soft round white highlight, so it reads like a lit ball
> rather than a dark body. The "dragon duel" crop comes closest to "trailer 55 s".
>
> **Fat Ogre: clearly worse.** The trailer's brutes in ogre.jpg (top row) are boxy, chunky and low-poly, with thick
> bright edges. The game's Ogre ("smash", "stomp") looks like a coloring-book outline of a smooth, detailed model:
> muscle curves, belly folds and a cartoon face in thin, sketchy lines.
>
> **Dragon: about equal at rest, worse up close.** The game's dragon in "breath" (dragon.jpg, middle left) and in
> duel-dragon-entrance.jpg holds up. The torso is a denser knot of crossing lines than the trailer's clean long spars.

On the wireframe question:

> **Knight: mostly yes.** It slips in "game: slash" and "slash +0.08 s", where the glowing torso fill reads as glassy.
> [...] **Fat Ogre: no.** It reads as a contour drawing, not facet edges. The facets don't close into visible low-poly
> shapes, and the lines break up in "stomp", especially on the face and belly. On "its death's frame" it becomes a
> pinkish-white glassy ghost with a solid fill: the strongest slip in the set. **Dragon: mostly yes.** It slips in
> "game: dive": the wings nearest the camera (top of frame) turn into thick solid red bands, and the chest has a lit
> red fill.

On the brightest thing:

> **level-surge.jpg: yes.** **duel-ogre-entrance.jpg and duel-dragon-entrance.jpg: no.** About 25 skeletons in the
> same white, with heavier bloom, ring the knight. [...] **ogre.jpg, "its death's frame": no.** The Ogre's white
> flash is far bigger and brighter than the knight.

Its ranking of what hurts most: the Ogre drawn as smooth-model line art, then the white skeletons on the entrance
frames, then the dragon's shatter "clumped and scribbly next to the trailer's big flung shards", the dive's filled
wings, the dragon's red-orange floor, filled shapes (the grid's splat under the hero, the stomp ring's fill, the
knight's torso glow), the surge's clutter and the grid's ripple.

## What changed in response, and what didn't

- **Changed**: nothing more in the drawing. Before the critique, by eye: the shield rides the left hand bone as the
  trailer's did (the fist grip turned it into an oval racket above the knight), and the duel camera eases out from
  the call so the bosses land whole in the frame.
- **The shatters are the trailer's.** Beside the trailer's own dragon death ([boss-deaths.jpg](boss-deaths.jpg)), its
  pieces too hold the body's shape for the first half second, and its body flashes white-pink on the break frame:
  the critique's reference frame, the trailer's shatter well after its break and nearer the camera, made ours look
  clumped. The white death frame and the skeletons' white entrance frame are the same impact-frame flash
  (MonsterEdgeShard, pinned for `--look trailer`), one frame long; whether a mass break may outshine the knight for a
  frame is the look pass's call (ps-6y2.23, commented there).
- **The Fat Ogre is its vendor mesh**, as the trailer would have drawn it: a smooth, detailed sculpt, where the
  trailer's brutes were blocky. That is the monster-mesh bead's to redesign (ps-6y2.39, commented there); the
  dragon's dense torso and its wings filling at grazing angles likewise (ps-6y2.40).
- **The knight's torso glow** is HeroEdge's chest core at the trailer's gains, and its thin lines are the trailer's
  1.4 px at 2160 rows drawn at 1080; both stay as the trailer had them, the look pass's to tune in the default.
- The floor's colour, the grid's ripple, the surge's clutter and the HUD are other beads' (the zones, the beat wave,
  the horde, the UI).
