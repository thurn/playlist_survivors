# 3D Tech Demo: Effects

## Effects

Effects are half of the main question. A stock Hovl hit effect is a prefab of
3 to 8 Shuriken particle systems. At the stress peak, 1,000 or more live
systems would be needed, and Unity's per-system cost lands on the main thread
long before particle counts matter. So effects run on GPU particles from the
start. An editor converter carries the chosen vendor effects into the GPU
runtime, which a spike picks first (see
[Choosing the GPU runtime](#choosing-the-gpu-runtime)).

### How many effects

The player causes nearly every effect, but most effect instances land on
enemies. Survivors-genre enemies rarely emit effects of their own. Here
monsters fight only in melee, so their share is a small dust puff per attack,
plus the hero's parry sparks. Hits and deaths, though, scale with the horde
and its kill rate.

- **Player weapons.** A late-game Vampire Survivors build spawns about 50–60
  weapon effects a second and keeps 60–80 alive, plus about 10 that persist.
  That's six evolved weapons with +3 Amount, from the repo's VS data in
  `docs/survivors/`. The hero's spells here are fewer and larger, and each
  combo blow adds a slash, so tens are alive at once.
- **Enemy feedback** at an envelope of 2,600 on screen and demo load (130
  deaths and 260 hits a second):

```
              rate      lifetime   alive at once
hit sparks    260/s     ~0.35 s    ~90
death bursts  130/s     ~0.8 s     ~105
all effects                        ~250-300   (heavy load: ~800-1,000)
```

**Beats make effects bursty.** Every impact lands on the beat, so one beat's
hits and deaths spawn in a single frame. At 140 bpm a beat is 0.43 s:

- at demo load, about 110 sparks and 55 death bursts spawn in one frame;
- at heavy load, about 450 effects spawn in one frame;
- each effect is at its largest and brightest at birth, so the beat frame is
  also the overdraw peak.

**Particles are cheap; overdraw is not.**

- **Particle counts.** About 20 particles per spark, 60 per death and a few
  hundred per spell give 15,000–30,000 live particles at demo load and about
  100,000 at heavy. Simulating 100,000 on a 4060-class GPU takes well under
  0.5 ms.
- **The cost of a layer.** One full-screen layer of HDR additive blending at
  1080p moves about 33 MB: 2 million pixels, each read and written at 8
  bytes. That is about 0.13 ms of an RTX 4060 Laptop's 256 GB/s, or 0.2–0.3
  ms with texture reads.
- **The overdraw budget.** If effects get about 3 ms of the client's 12.5 ms,
  that allows a mean of about 5 full-screen layers, and 10–15 on beat frames.
- **Glow quads eat it.** A Hovl hit effect often carries a soft glow quad a
  few metres across. A hundred at once make one or two full-screen layers on
  their own.

These are estimates, which TD8 measures. The harness flags any point whose
mean transparent fragments per pixel exceed 5, or whose beat-frame peak
exceeds 12 (see [Measurements](benchmarks.md#measurements)). Cygnus has twice the
target's bandwidth, so its times understate this cost, and the counters
show it directly.

### Effect design rules for a horde

Three rules keep effects affordable without breaking the fidelity floor.

- **Bloom makes the glow.** Post-process bloom costs the same for one effect
  or a thousand, while a glow quad costs per effect. Converted effects keep
  small, HDR-bright cores and drop their large soft glow quads, so bloom draws
  the halo. Each dropped glow layer is a cut, gated by an A/B check.
- **Feedback is tiered.** A **tick**, a repeat hit from a lingering effect
  such as the laser's sweep or a ring's afterburn, gets the monster's shader
  flash and nothing else: no particles, flinch or knockback, so a swept crowd
  doesn't jitter in place. Weapon impacts get sparks, deaths get bursts, and
  accents and spells get the large effects. Vampire Survivors tiers its
  feedback the same way.
- **The beat frame is the budget.** The p99 pass rule measures it. Spawning
  must cost almost nothing per effect on the CPU: all of a frame's spawns go
  up in one GPU buffer, never as one object per effect.

### The effect list

About twenty effects cover the demo, chosen by TD8 from the vendor packs:

- **Hovl Studio:** sword slashes (Sword slash VFX), hit sparks (Magic hits),
  rings (AOE Magic spells), bolts and impacts (AAA Projectiles), the laser
  (3D Lasers Pack), ground circles (Magic circles) and parry flashes.
- **KriptoFX:** death bursts and explosions, where they read better than
  Hovl's.
- **Ambient:** embers and drifting wisps over the arena.
- **Monsters:** a small dust puff on each attack. The song's clear ring
  (choreography.md) keeps monsters from attacking, so it shows only in the
  stress scenes.
- **The hero stays in sight.** Effects centred on the hero leave out their
  smoke and fire children: the slash, spin and slam lose their smoke (the
  slash its fire too), and the ring spell keeps only its expanding ground
  wave, since its sunburst core sat over the hero on every downbeat. The
  bolt's muzzle flash fires from the hero's edge at 0.6 size.

### The look

The look pass (epic ps-e8r, [evidence](../../demo3d/evidence/look/effects/README.md))
retunes the converted effects toward Deep Rock Galactic: Survivor's: brief,
shaped, white-hot at the core and saturated at the edge, never a flat clipped
blob. All of it is selection data in `effects.json`, applied by the converter
and our own draw shader; no vendor file changes. Per effect:

- **`heat`** `{ edge, mid, gain, mix }`: a colour ramp in the draw shader.
  Each fragment's own luminance times `gain` picks its hue, from `edge`
  (default deep red) through `mid` (default orange) to white, keeping the
  luminance. So a sprite's bright centre reads white and its falloff yellow
  to red, whatever the vendor texture's tint. Hero fire and impacts use a red
  edge, the ring a gold one, lightning violet (off the runes' cyan).
- **`hdr`**: a scale on the emitters' HDR colour, for large-area layers that
  stacked into a clipped blob.
- **`life`**: a lifetime scale. It plays the same motion and curves faster,
  as the simulation speed would, so circles flash and go.
- **`tune`** `{ child: { size, hdr, life, heat } }`: the same per child;
  `"heat": false` keeps smoke out of the ramp.
- **`sparks`** `{ count, speed, life, size, gravity, radius, upward, stretch,
  hdr, heightM, heat }`: our own stretched spark burst, built by the
  converter and added to the effect, for effects whose prefab throws none
  (the ring and lightning).
- **Light cues** take the effect's heat colour: a white cue on every hit
  washed the ground and the horde yellow-white at a climax.

**The crystal shatter's burst** (epic ps-2iq,
[evidence](../../demo3d/evidence/shatter/impact/README.md)): wherever Crystal draws a type, its
deaths skip their death burst. At the killing blow, where its shards break, `crystal-burst`
spawns where the blow lands on the body (`CrystalShatter.HitPoint`) instead: the selection's
`client.crystalShatter`, scaled by the monster's height and turned along the blow (the spawn's
yaw turns the effect's +z). It is built in, with no prefab: an entry without one is built from
its `sparks`, which may be a list of layers. Its two layers are thrown forward in cones along the
blow (a spark layer's `pitch` and `spread`), through and past the breaking body, with the shards:
34 small bright chips (4 to 10 m/s, slightly stretched) and 80 grains of fine crystal dust (1.5
to 6 m/s, in a wider cone), both slowed by drag (`limit`, `dampen`), shrinking (`shrink`) and
fading out smoothly (`fade`) within 0.4 to 0.7 s. Nothing blinks or flickers, and it has no glow
quad, so the shards stay in sight. Each spawn carries a **tint**, the type's gem colour, so one
definition serves every type. A tinted spawn's heat ramp runs from the gem squared, through the
gem, to a white core: the chips start white-hot and cool into the gem, the dust stays in the
gem. Its light cue takes the gem's colour: a 0.25 s flash of intensity 2.2 and range 4 m, set
0.4 m above the burst (a cue's `height`). The breaks at one moment (one blow's kills) share their
flash: each spawn's light is 1/√n of the n breaks (the spawn's `LightGain`), so a slam that breaks
seven goblins flashes about 2.6 times as hard as one, not seven, which washed the view lime.

The horde's hit flash (CrowdSkinned, `lighting.crowd.flash` in the layout)
is an ember rather than white: at the final peak, flashing bodies alone made
a clipped blob 2.6% of the frame.

### The converter

The converter is an editor script that reads each chosen prefab through
Unity's public `ParticleSystem` and `ParticleSystemRenderer` APIs. It writes
an effect definition for our own runtime, or a parameter set for the VFX
Graph templates, whichever the spike picks. Its outputs are vendor-derived,
so they live only in the gitignored baked folder.

**The converter covers only what the chosen effects use.** It is not a
general Shuriken converter.

- **Inventory first.** Before writing the converter, TD8 runs an editor
  script over the chosen prefabs and all their child systems. It lists every
  module enabled, and every shape, render mode, curve mode and vertex stream
  in use, with the prefabs that use each. The table goes to TD8's evidence.
- **Build to the inventory.** The converter implements exactly the entries in
  that table. Anything else fails loudly, as below.
- **Swap before extending.** When one effect is the only user of a costly
  module, such as ribbons or sub-emitters, choose another effect that reads the
  same before implementing the module, unless no candidate reads as well.
  Record each swap in TD8's notes.

The modules the chosen effects might need, as a guide to what the inventory
will contain:

- **`main`:** duration, looping, start delay, lifetime, speed, 3D size and
  rotation, color, gravity, simulation space and max particles.
- **`emission`:** rate over time, rate over distance and bursts.
- **`shape`:** sphere, hemisphere, cone, circle, box, edge and donut, with
  arc, transform and direction randomization.
- **Over-lifetime modules:** velocity, limit velocity, force, color, size and
  rotation; also color and size by speed.
- **`noise`:** approximated with curl noise. It cannot match exactly. Every
  converted effect therefore gets an A/B still against the stock prefab, for
  review. A conversion is not a cut and has no threshold.
- **`textureSheetAnimation`, `trails` (ribbons), `subEmitters`** (birth and
  death), and `customData` with the renderer's active vertex streams (Hovl's
  shaders read dissolve and UV offsets from them).
- **`lights`:** converted into the effect's light cue.
- **`renderer`:** billboard, stretched, horizontal, vertical and mesh modes,
  the material, sorting fudge, and minimum and maximum particle size.

Curves and gradients in every mode (constant, two constants, curve, two
curves, gradient, two gradients, random color) are baked into 64-sample rows
of one lookup texture per definition.

Unsupported modules make the converter fail loudly. Collision, triggers,
external forces and lifetime by emitter speed are examples. It lists each one
in use per prefab:

```
error: hovl/Magic hits/Hit_Electric: unsupported modules in use
  collision (world, bounce 0.3) on child 'Sparks'
  fix: drop the module (cosmetic), or choose another effect
```

The runtime's draw shaders cover what the chosen effects' materials do:
additive, alpha blend, soft particles, dissolve and distortion. Distortion
samples URP's opaque texture.

- **Written from scratch.** They are our own code, written from each
  material's observed behavior (blend mode, inputs, vertex streams), so they
  are tracked.
- **Never copied.** No Hovl shader source is copied into them. A shader that
  must reuse vendor code counts as derived and lives in the baked folder.

### Choosing the GPU runtime

Two GPU runtimes could run the converted effects, and TD8 picks one with a
spike before building either out. The vendor packs contain no VFX Graph
assets. There are no `.vfx` files; Hovl and KriptoFX use Shuriken with 78
hand-written shaders and 26 Shader Graphs. So effects are rebuilt whichever
runtime wins, and the converter stays the same; only its output changes.

**Our own compute runtime** (see [The runtime](#the-runtime)):

- **One pool for every effect type,** so one simulation dispatch and one
  global back-to-front sort serve them all.
- **Exact control** over the beat-frame spawn path.
- **All code,** so agents own it end to end.
- **But it builds everything itself:** sorting, ribbons, sub-emitters and
  soft particles.

**VFX Graph driven by template graphs:**

- **Mature features.** Unity's own GPU particle system brings simulation,
  sorting within a system, ribbon strips, and sub-emitters through GPU events.
  It also has collision with the depth buffer (sparks bouncing off the
  ground), lit outputs and Shader Graph outputs.
- **One system per effect type, not one per hit.** Each frame the client
  fills a `GraphicsBuffer` with that type's spawn requests and sets a spawn
  count. The graph's initialize step reads each new particle's position, seed
  and scale from the buffer.
- **Templates.** About 5 to 8 generic graphs cover the effects: a billboard
  burst, stretched sparks, a ribbon trail, a mesh particle and a ground ring.
  Each exposes its curves as baked textures, and its flipbook and timing as
  properties. Every converted effect is one or more template instances with
  its own parameter set.
- **Built once, by hand.** Graphs can only be authored in the node editor.
  There's no supported API for building them in code, and `.vfx` files are
  fragile to edit as text. So someone builds the templates once: the operator
  in the editor (about an afternoon), or an agent carefully editing copies of
  the templates that ship with the VFX Graph package. After that, agents work
  only in data.
- **Sorting stops at the system.** Alpha-blended effects sort only within
  their own system, so overlapping smoke from two effect types can sort
  wrongly. Additive sparks, most of Hovl's look, are unaffected.

**The spike** builds one billboard-burst effect both ways. It runs both on
stress scene S4 (effects only) at heavy load, about 450 spawns on each beat
frame, and fills in a table like this:

```
spike: billboard burst, S4 heavy, 40 m, N 2,600
                    gpu p99   main p99   beat-frame spawn cost   authoring
own runtime         …         …          …                       code only
VFX Graph template  …         …          …                       one graph
```

- **Decision rule.** VFX Graph wins if its GPU and main-thread p99 times are
  within 10% of ours and its template was built or edited without
  hand-repairing the asset. Otherwise our runtime wins.
- **Recorded in TD8's notes:** both rows, the template's authoring route, and
  any VFX Graph feature that turned out to disable its batching.

### The runtime

Whichever runtime wins must meet the requirements below. The mechanisms
describe our own runtime; VFX Graph meets them through the spawn buffers and
templates described above. The runtime simulates and draws every instance of
an effect together, so its cost follows particles, not prefabs.

- **One pool.** A single GPU particle pool, shared by every emitter
  definition, holds state buffers, a dead list and an alive list. Each
  particle carries its definition's index. The pool is sized at twice its
  peak live count under the heavy load at the largest N swept. Capacity costs
  only memory, so an overflow signals a sizing bug.
- **Spawning.** Each frame the CPU uploads one buffer of spawn requests: the
  effects due this frame from the timeline, plus continuous emitters on
  projectiles and attached effects. Each request carries its definition,
  transform, seed and sub-frame time offset.
- **Compute kernels** emit, simulate (curves through the lookup texture),
  spawn sub-emitters and compact the alive list. Alpha-blended particles are
  then sorted back to front together, across effect types.
- **Drawing.** Indirect procedural draws (quads, ribbons and meshes) are
  issued from a URP renderer feature after opaques.
- **Randomness** comes from hashing the spawn seed and particle index, so
  fixed-time captures reproduce exactly.
- **Budgets are hard.** A spawn that would overflow the pool is rejected and
  counted. Any rejection fails the pass rule, because it would hide a hit or
  death effect.
- **Merging is a cut.** Identical spawns of one definition in the same frame,
  with centers within 8 px on screen, merge into one with combined density.
  It is allowed only after an A/B check.

### Effect lights

Effect lights are what make the dusk arena come alive, and the fourth item of
the fidelity floor.

- **Cues.** Each effect definition may carry a light cue: color, range and an
  intensity curve over lifetime. It comes from the prefab's lights module, or
  is authored where the prefab has none.
- **Clustering is always on.** Each frame, active cues are clustered on a
  1.5 m world grid into one light per cell: intensities summed, the position
  a weighted centroid, the range the largest. Effect lights reach several
  metres, so lights within 1.5 m merge without visible change. Larger cells
  are a cut, gated by an A/B check.
- **The pool.** Pooled URP point lights without shadows take the strongest
  clusters by **screen contribution**: intensity times the screen area of the
  light's range sphere.
- **Culling.** Clusters whose range sphere misses the view are culled, not
  dropped.
- **Pool size.** A quality setting that costs GPU time, so it is fixed before
  the first sweep with lights, and stays fixed. It starts at 128. TD8
  measures 64, 128 and 256 against the Forward+ visible-light limit of URP
  17.6, which TD8 confirms, and records its choice. S1 and S2 have no effect
  lights, so earlier sweeps do not depend on it.
- **Drops.** Visible clusters beyond the pool are dropped, weakest first, and
  their share of the frame's visible cue intensity is recorded. A run passes
  only if the p99 of that share is at most 2%. So a dense beat can drop its
  faintest lights, but a horde never goes dark.
