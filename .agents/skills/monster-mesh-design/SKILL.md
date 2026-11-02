---
name: monster-mesh-design
description: Design a new low-poly mesh for one Rhythm Champions monster from its vendor source asset, so that it reads as a retro 3D neon wireframe in the synthwave style (simple faceted shapes, glowing connected edges over a dark body) at game scale, in a crowd and up close, still moving with the vendor rig and clips. Use for the monster-mesh beads, or any time a monster's look in the edge look needs reworking at the mesh level.
---

# Design a monster's mesh for the edge look

You're designing one monster: its vendor model goes in, and a low-poly
model comes out that reads as a **retro 3D neon wireframe**, the synthwave
look of glowing low-poly mountains, palm trees and cities on black. This is
an art task. Your eye decides it, not a number.

Read first:

- [design.md, The aim: retro 3D neon wireframe](../../../docs/champions/design.md#the-aim-retro-3d-neon-wireframe),
  and the operator's references it lists in `captures/style-references/` in
  the main checkout (`/Users/dthurn/playlist_survivors/captures/style-references/`).
  Look at them; they're the target.
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

**What it should look like: a retro 3D neon wireframe.**

- A 3D model with a deliberately simple, low-poly topology, usually a few
  simple faceted volumes joined together.
- Its edges are glowing lines that connect at vertices into closed facets,
  so the silhouette is closed.
- The dark body hides the lines behind it, and every facet stays dark
  inside.
- Not lit facet patches, glassy fills, broken or blotchy scratches, or a flat
  outline drawing.

If the trailer's drawing of the vendor mesh shows patches or broken lines in
our build, find out whether the renderer is off before reworking the mesh:
compare with the trailer's own frames, and report it on ps-6y2.28.

So **the mesh's topology is the drawing.** With the 24-degree rule, a crease
you want is a sharp edge, and a surface you want quiet is smooth (neighbours
under 24 degrees apart) or flat. Every sharp edge on the model is a stroke
on screen, whether you meant it or not. A designed low-poly mesh can instead
draw every one of its edges, a true wireframe as the references are, if the
recipe flags its edges (see Build it). Then each edge of the topology
is a line you chose, so lay it out with care:

- clean quads and triangles of even size;
- edges that follow the form, like bands around a limb or ridges along a
  back.

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
  - whether it reads as a 3D neon wireframe (connected lines, closed facets,
    a closed silhouette, dark inside) or as lit patches and broken
    scratches, and where;
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

- **Simple faceted shapes, joined together, are the usual way**, as the
  synthwave references are built: a clean low-poly volume per mass (a
  faceted dome of a head, a prism of a forearm), meeting at clear joints.
  Some monsters need more detail kept.
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

Write a recipe at `rhythm_champions/tools/recipes/monster/<type>.py`: a
module whose docstring says what it changes and why (the review's README
quotes it), with

```python
from champions_tools.monster import Mesh, Source, blender

def build(source: Source) -> Mesh:
    mesh = source.mesh()  # the vendor mesh, unchanged: start here
    ...
    return mesh
```

- `Source` is the vendor model as the game draws it: every renderer
  combined in root space, metres at the type's height, its rest pose.
  Numpy arrays `positions`, `normals`, `triangles`, `bones` and `weights`
  (four influences per vertex, into `bone_names`), `carried` (a weapon's or
  shield's vertices), and `parts` (each renderer's vertex range:
  `source.part("Sword_LP")`); `source.bone("Hand_R")` finds a bone.
- Return a `Mesh` skinned to the same bones (`positions`, `triangles`,
  `bones`, `weights`; `normals` optional; `carried` optional, so the shatter
  flings your weapon's chunks with the body). It shares the vendor bone
  texture, so it moves with the vendor rig and clips.
- Which edges draw: by default every crease sharper than 24 degrees, as the
  trailer drew the vendor meshes. `mesh.edge_angle = 1` draws every edge but
  a flat quad's diagonal (a true wireframe of your topology); `mesh.edges`,
  shape (triangles, 3), flags each triangle's edges yourself (1 for the edge
  opposite that corner). A mesh that chooses its edges isn't decimated for
  distance: its topology is the drawing, so keep it low-poly.
- Blender 5.2 LTS headless: `blender(script, *args)` runs a script with
  `--background --python-exit-code 1`; `source.save(path)` and
  `Mesh.load(path)` trade `.npz` files with it.

`champions monster bake <type>` exports the vendor mesh (Unity), runs the
recipe and bakes both looks into ignored `Assets/Baked/Monsters/<type>/`
(`source/` the export, `recipe/` your mesh), feature edges, LODs and shard
pieces included; each step reruns only when its inputs change (`--force`
reruns all). `--look trailer` always bakes from the vendor mesh. A player
build bakes your mesh too (`champions build` runs the recipes first).

Techniques that tend to work:

- **Planar dissolve and limited decimation, part by part**, to quiet busy
  surfaces into fewer, larger facets.
- **Remodel a part from simple primitives** fitted to the source (a helmet
  as a faceted dome, a forearm as a tapered prism, a hand as a mitten). Skin
  it by transferring weights from the source part it replaces.
- **Sharpen or soften creases on purpose**: move vertices or bevel so the
  angle crosses 24 degrees where you want a line, and stays under it where
  you don't. Keep each crease well clear of 24: one near it draws on some
  facets and not others, a dashed line (the skeleton's vendor helmet).
- **Replace fiddly accessories** with one clean shape, or drop them if they
  don't name the type.
- **Drop what is hidden**: parts inside the armour in every view (toes in
  boots, ribs under a cuirass) cost triangles and draw nothing. Count each
  part's visible pixels over a ring of views before keeping it.
- **Keep big facets from turning far from the camera**: a large facet seen
  at a grazing angle catches the fresnel rim as a lit patch (the skeleton's
  domed shield, and a ridged breastplate tried and dropped). Thin plates, and
  facets facing up toward the game camera, stay dark. Where a big facet must
  turn away, give its vertices normals that lean up (`Mesh.normals`): the
  rim mixes them with the facet's own (the slime's sides).
- **Mind the camera's 60 degrees on a round body**: seen from above, rings
  around a vertical axis nest into a target and an apex's spokes into an
  umbrella or a spider web. The slime closes under a crown and a face
  instead, and hides its lowest tier under a bulge.
- **Turn what names the type toward the camera**: a feature on the top
  front, tilted about 55 degrees up, shows from every side in a crowd, not
  only when the monster faces the camera (the slime's eye).
- **Print a small feature on its panel**: give each of its vertices the
  panel corners' weights, blended by where it lies, so it follows the panel
  through every clip instead of stretching or sinking (the slime's eye).

`tools/recipes/monster/skeleton.py` is a worked example: vendor parts kept,
dropped and rebuilt from primitives skinned rigidly to one bone each, and
the trailer's 24-degree creases flagged by the recipe itself (`creases()`),
so the mesh is drawn as designed at every distance.
`tools/recipes/monster/slime.py` is the other kind: a body built from
scratch as polygons, every edge it designs drawn (`mesh.edges`), and some
hushed so a panel stays quiet.

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

- `champions monster review <type>` renders the default look into
  `rhythm_champions/evidence/monsters/<type>/`: `default-far.jpg`,
  `default-game.jpg` and `default-near.jpg` (idle, walk and attack, then the
  shatter), `default-crowd.jpg` (a crowd over the grid) and `compare.jpg`
  (`--look trailer` on the left, the default on the right); add
  `--look trailer` for the `trailer-*.jpg` sheets. Everything in the type's
  colour at the game camera. Look at game scale first, then zoom. Watch
  every pose: a shape that reads at idle can collapse mid-stride.
- **Get a blind critique** from a fresh agent that knows nothing of your
  intent. Give it only the images, no docs, no recipe, no numbers, so its
  eye isn't anchored on yours:

  > You're an art director for a top-down neon game drawn as glowing line
  > art on black. Here are two drawings of the same monster, A and B, each
  > at three screen sizes, in three poses, its death shatter and a crowd:
  > <paths>. The style aimed for is retro 3D neon wireframe, as in synthwave
  > art. Does each read as a low-poly 3D model of simple shapes whose
  > glowing edges connect into closed facets and a closed silhouette, dark
  > inside? Or does it break into patches and scratches, and where? Which reads better as a creature at the in-game
  > size and in the crowd? What's wrong with each, most important first,
  > concretely enough to act on? Under 300 words.

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
    still looks weak. The review rewrites the README's top each time and
    keeps everything from its `## Notes` heading on: write yours there.
- The default look uses the new mesh only if it reads better than the
  trailer's drawing; otherwise say why the vendor mesh stays.
  `--look trailer` never changes.
- A row in `rhythm_champions/evidence/operator-pending.md` asking the
  operator to compare the type with and without `--look trailer`.
