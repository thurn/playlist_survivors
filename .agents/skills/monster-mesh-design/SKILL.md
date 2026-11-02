---
name: monster-mesh-design
description: Design a new mesh for one Rhythm Champions monster from its vendor source asset, so that the trailer's edge look (MonsterEdge.hlsl, crease lines over a dark glass core) draws it as a coherent, handsome figure at game scale, in a crowd and up close, still moving with the vendor rig and clips. Use for the monster-mesh beads, or any time a monster's look in the edge look needs reworking at the mesh level.
---

# Design a monster's mesh for the edge look

You're designing one monster: its vendor model goes in, and a mesh comes out
that the edge look draws beautifully. This is an art task. Your eye decides
it, not a number.

Read first:

- [design.md, The Look](../../../docs/champions/design.md#the-look): the
  trailer is the reference, it looks good, and changes to it are judged by
  eye.
- [style.md, The Edge Look](../../../docs/champions/style.md#the-edge-look).
- The project's AGENTS.md for worktrees, checks and delivery.
- [The vendor asset rules](../../../docs/archive/tech-demo-3d/vendor-assets.md):
  the repository is public. Your recipe is committed; every mesh, export,
  `.blend` and bake it produces stays in ignored folders.

## How the edge look draws a mesh

`MonsterEdge.hlsl`, ported from the trailer (ps-6y2.28), draws:

- **a line on every crease sharper than 24 degrees**, and on every open edge,
  in the type's colour, a little brighter toward the silhouette;
- a dark glass core, each facet a shade apart;
- a fresnel rim around the figure;
- a small inner glow that flares on the beat.

So **the mesh's facet angles are the drawing.** A crease you want is a sharp
edge. A surface you want quiet is smooth (neighbours under 24 degrees apart)
or flat. Every sharp edge on the model is a stroke on screen, whether you
meant it or not.

## What went wrong before, so you don't repeat it

The deleted "edge cuts" picked edges one at a time to hit a black-share
number and a segment cap. They lost the outline, the head and every closed
shape, and passed every threshold while looking like scattered scratches.
In particular:

- Don't optimise a metric.
- Don't choose strokes in isolation from the shapes they belong to.
- Don't delete what names the monster to make it emptier.

## 1. Look before you touch anything

- Render the trailer's drawing of the vendor mesh:
  `champions monster review <type> --look trailer`. Look at the sheets at
  native size first (that's game scale), then zoomed.
- Watch the monster in the trailer itself, `captures/trailer-action-edge.mp4`
  in the main checkout's `captures/` (outside the tree). Pull frames with
  `ffmpeg -ss <s> -i <mp4> -frames:v 1 out.jpg` into your scratch folder. The
  stills in `docs/champions/reference/` help too.
- Read the evidence of monsters already done in
  `rhythm_champions/evidence/monsters/`, so this one joins them as a roster.
- Write down in plain words:
  - what reads and what doesn't, at game scale, in a crowd and up close;
  - what names the type: its silhouette, head, weapon, shield, wings, tusks,
    posture;
  - what turns to noise, or merges into a fill (the trailer's goblins).
- If you're unsure, get a first blind read now (see step 4) of the trailer
  sheets alone.

The trailer's drawing may already be right. Keeping the vendor mesh is an
accepted outcome; say why in the README.

## 2. Design the figure

Decide the figure before the geometry:

- **The big masses**: head, torso, limbs, weapon, shield, wings. How many,
  and how they join.
- **The few features that name the type**, which must survive at game scale.
- **What goes**: detail that only adds scribble at game scale.

Guidelines, not rules. Each one has exceptions, and a frame that looks
better beats any of them:

- **Simple joined shapes often read well**: a clean faceted volume per
  mass, meeting at clear joints. Some monsters read better with more detail
  kept.
- **Fewer, larger facets read at game scale**; many small facets with sharp
  angles between them merge into a fill.
- **Keep the silhouette's landmarks and negative spaces**: the gap between
  arm and body, the shield's circle, a horn's point.
- **The head almost always matters.** It's the first thing a viewer looks
  for.
- **Weapons and shields belong to the figure.** Draw them as clean shapes,
  but not so bright or clean that they outdraw the creature. They shatter
  with it.
- **Tell siblings apart by shape**: the skeleton and the demon share a
  colour family, and so do the goblin and the mushroom, and the big ork and
  the troll.

## 3. Build it

Write a recipe at `rhythm_champions/tools/recipes/monster/<type>.py`.
`champions monster bake <type>` runs it (Blender 5.2 LTS headless allowed:
`/Applications/Blender.app`, `--background --python`, with
`--python-exit-code 1`) and bakes the result, feature edges and shard pieces
included, into ignored `Assets/Baked/Monsters/<type>/`. If the interface
that ps-6y2.28 landed differs from this, follow what landed and fix this
skill.

Techniques that tend to work:

- **Planar dissolve and limited decimation, part by part**, to quiet busy
  surfaces into fewer, larger facets.
- **Remodel a part from simple primitives** fitted to the source (a helmet
  as a faceted dome, a forearm as a tapered prism, a hand as a mitten). Skin
  it by transferring weights from the source part it replaces.
- **Sharpen or soften creases on purpose**: move vertices or bevel so the
  angle crosses 24 degrees where you want a line, and stays under it where
  you don't.
- **Replace fiddly accessories** with one clean shape, or drop them if they
  don't name the type.

Keep the vendor rig, bind poses and clips. For skin transfer, partitioning,
stream compaction and bounds, follow
[the create-lod skill](../create-lod/SKILL.md)'s "Preserve appearance,
binding and streams":

- transfer weights by region and side, never across limbs;
- keep the influence count;
- recompute animated bounds over every clip.

Triangle count is a diagnostic. Stay near or under the vendor's, and note
the cost if you go over (the crowd draws 1,000).

## 4. Judge it

- `champions monster review <type>` renders the default look beside
  `--look trailer`: three sizes, idle, walk and attack, the shatter and a
  crowd cell over the grid, in the type's colour. Look at game scale first,
  then zoom. Watch every pose: a shape that reads at idle can collapse
  mid-stride.
- **Get a blind critique** from a fresh agent that knows nothing of your
  intent. Give it only the images, no docs, no recipe, no numbers, so its
  eye isn't anchored on yours:

  > You're an art director for a top-down neon game drawn as glowing line
  > art on black. Here are two drawings of the same monster, A and B, each
  > at three screen sizes, in three poses, its death shatter and a crowd:
  > <paths>. Which reads better as a creature at the in-game size and in the
  > crowd? What's wrong with each, most important first, concretely enough
  > to act on? Under 300 words.

  Label the trailer's sheets and yours A and B in random order.
- Act on what it finds that you agree with, and render again. Repeat until
  the new mesh is clearly better than the trailer's drawing, or until you're
  sure it isn't.
- Never trade away a feature that names the type for a number.

## 5. Deliver

- The committed recipe, with the source's hash, the Blender version and the
  exact bake command.
- `rhythm_champions/evidence/monsters/<type>/`:
  - the review sheets;
  - a README saying what you saw in the trailer's drawing, what you changed
    and why, the blind critique quoted with what you did about it, and what
    still looks weak.
- The default look uses the new mesh only if it reads better than the
  trailer's drawing; otherwise say why the vendor mesh stays.
  `--look trailer` never changes.
- A row in `rhythm_champions/evidence/operator-pending.md` asking the
  operator to compare the type with and without `--look trailer`.
