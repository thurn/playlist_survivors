# Operator-pending checks

> **Combat revision 2026-11-02:** rows below record checks for the implementation
> that produced them. Old dash/build/Break/duel balance criteria are superseded
> by [the current design](../../docs/champions/design.md). They are historical
> checks, not acceptance requirements for the redesign. Art/audio checks remain
> useful. This documentation revision changes no bead or check status.


Checks only the operator can make: hand-play balance, the speakers, a window
on screen, a low-end Mac. Beads add a row here instead of waiting; none of
them blocks work ([plan](../../docs/champions/plan.md)). The operator marks a
row done (or files a bead) after checking.

| Check | Why only the operator | Added by | Status |
| --- | --- | --- | --- |
| `just champions` opens a fullscreen 1080p window titled Rhythm Champions on the black void, and `--windowed` a window | A window on screen | ps-6y2.1 | pending |
| Install the Unity Accelerator on jarvis ([SETUP.md](../../SETUP.md#the-unity-accelerator)) and confirm `champions build` logs its connection | Downloading and installing an app | ps-6y2.1 | pending |
| Delayed listening on the speakers: with Music (or Spotify) playing, `just champions --app Music` mutes the app at once and its song comes back 3 s later through the game, in time and without dropouts; quitting the game unmutes the app | Real speakers and a real app playing | ps-6y2.2 | pending |
| The edge crowd on screen: `just champions-play --windowed --edge-crowd 1000 --seconds 20 --bench-out captures/edge-crowd.json`: the skeletons read as lit shapes with dark between them and the shards fly on the kill frame; the JSON's `windowedGpuFrameMs` is the GPU frame time a headless run can't read (Metal has no GPU recorder) | A window on screen, on a quiet machine | ps-6y2.8 | dropped: the edge crowd was deleted with the edge-cut pipeline |
| The world in a window: `just champions --file <song>` shows the grid with a ring leaving the hero's spot on every beat you hear (stronger on the bar's first), the floor's colour sweeping to a new key at a section change, and nothing but the quiet grid in a beatless stretch; `--tier low` and `--tier high` look alike but for edges and bloom | A window on screen and the speakers | ps-6y2.14 | pending |
| Structures in a window, once ps-6y2.21 assembles level 1 (`just champions --file <song>`; until then the stills in [ps-6y2.15](ps-6y2.15/README.md)): the cottages, huts, tower, walls, fences, trees, pines and rocks read as solid shapes in the zone's dim fill, clearly apart from the monsters' dark bodies, their edges flaring as the beat wave passes; a dash through a fence or a tree shatters it on the frame it lands, thrown along the dash, the pieces clattering down and sinking about three seconds later | A window on screen, and judging the look | ps-6y2.15 | dropped: the structures draw as stand-ins until the trailer's edge look is ported |
| The weapon effects in motion: `just champions-play --gallery --windowed` loops every effect at the hero's spot; each slash comes in fast and fades slowly, reads as white on the void with a faint white-blue edge, and the on-beat and special versions read as bigger and brighter ([gallery](effects/README.md)) | A window on screen; motion a still can't show | ps-6y2.16 | pending |
| The hero's sounds under the music on the speakers, once the assembly plays them (ps-6y2.21): swings land on their impacts, a dash's whoosh starts with the key, an on-beat dash's accent is heard under it, the kills' shatters sit under the music without piling up, and nothing ducks the music | Real speakers and a played run | ps-6y2.16 | pending |
| Both duels by hand, once the assembly plays them (ps-6y2.21; until then the stills in [ps-6y2.17](ps-6y2.17/README.md)): the entrance lands on a strong downbeat as the level's biggest moment; every red telegraph reads at a glance and its attack lands on the beat you hear; a dash through it on that beat feels like a perfect dodge and a quarter Break per dodge is the right pace; the Ogre falls in about 60-90 s and the dragon in 2-3 minutes with a good build on a real song (the scripted lengths and their tempo spread are in the README); the dragon's dive reads at fast tempos, where its takeoff and landing clips play sped up | Hand-play feel, the speakers, judging the look in motion | ps-6y2.17 | pending |
| The menus in a window: `just champions` opens on the title; the arrows and Enter, or the mouse, take you to the picker, which lists the apps playing audio (start Music or Spotify: it appears within a second) and File..., which opens the Mac's open panel; a pick starts the run on that music; Esc pauses (as does switching to another app) and Resume, Quality and End run work; the text reads at a glance ([stills](ui/README.md)) | A window on screen, the Mac's panel, a real app playing | ps-6y2.19 | pending |
| Calibration on the speakers and on Bluetooth: Calibrate > Video's flash can be lined up with its click with Left and Right and Save keeps it (relaunch: the offset stays); Calibrate > Input's eight clicks, tapped with Space, give an offset near your real lag and save it; in a run (pause > Calibrate) the music ducks under the clicks and comes back after | Real speakers and headphones, and the operator's own timing | ps-6y2.19 | pending |
| The indicator in play, once ps-6y2.21 assembles level 1: the ticks arrive on the beats you hear, the pip is easy to read at a glance (cooling, ready, the on-beat flash, the dropped press's blink) and the health bar's hurt flash is noticed without stealing the eye | Hand-play on the speakers | ps-6y2.19 | pending |
| Click-to-move in a window, once ps-6y2.21 assembles level 1 (`just champions --file <song>`): a left click walks the hero straight to the clicked spot, holding the button has it follow the cursor smoothly (also while the camera follows it), a movement key takes over at once, and a click on a card or a menu row never walks the hero | A window on screen and hand-play feel | ps-6y2.27 | pending |
| The hero in motion: `just champions-play --windowed --fixed-time --capture-stills captures/hero --stills-scenes hero` plays the hero's scripted scene in a window (about 42 s: Sword Slash at 80, 128 and 174 BPM, standing then running, the dash poses, the hurt flash and the death); each slash's blade sweeps through its impact on the beat you see the ring leave, the legs run without sliding under a torso turned to the dummies, the spin reads as a spin, each dash pose reads as its own move, and the knight is the brightest thing on screen ([stills](hero/README.md)) | A window on screen; motion a still can't show | ps-6y2.20 | dropped: the hero draws as a stand-in until the trailer's edge look is ported |
| The monsters in the trailer's edge look on screen: `just champions-play --windowed --crowd-bench 1000 --seconds 20 --bench-out captures/crowd.json`, then with `--look trailer`, beside `captures/trailer-action-edge.mp4`: each type reads as glowing line art over a dark core as in the trailer, its weapon and shield shatter with it, and the JSON's `windowedGpuFrameMs` is the crowd's GPU frame time a headless run can't read ([evidence](monsters/README.md)) | A window on screen and judging the look in motion | ps-6y2.28 | pending |
| Level 1 by hand, for balance: `just champions --file <a playlist of 15-20 minutes>` a few times. A first run usually reaches the Ogre (about minute 7) and the dragon falls after two to four runs; an idle hero dies within 90 s; levels come about every 2 minutes from about 1:30; the second half (denser, mushrooms' spores) is hard but fair; loud songs surge the horde without burying the hero. The scripted reference player reaches and beats the Ogre in about half its seeds and the dragon in about one in eight (spores were slowed to every 4 bars for 6 on its evidence; [evidence](ps-6y2.21/README.md)); tune `Content/level1.json`, `enemies.json` and `progression.json` from what hand-play shows | Hand-play balance | ps-6y2.21 | pending |
| The run's flow in a window: the first offer opens over the empty, held world while the music's first bars play; play starts on the next downbeat after the pick; Esc and switching apps pause (Resume, End run); a death shows the end card 1.5 s after the shatter, a victory the lit card after the dragon's; Again (Enter or a click) starts a new run at once on the same music, which never stops; Esc on the card goes to the title | A window on screen and hand-play | ps-6y2.21 | pending |
| The dash and its judgement on the speakers: a dash leaves on the key-down frame; a press on the beat you hear (80 ms either side; 60 ms for half beats and strong onsets) pays the bigger effect, accent and ring, and syncopated music's onsets feel fair; the 0.6 s cooldown and its 0.15 s buffer never feel like a dropped press; with no beat every press pays | Hand-play on real speakers | ps-6y2.21 | pending |
| The same dash and judgement on Bluetooth headphones after calibrating Input (pause > Calibrate > Input): on-beat presses still land on the beat heard, and nothing feels late | Bluetooth latency and the operator's own timing | ps-6y2.21 | pending |
| The hero and the bosses in the trailer's edge look in a window: `just champions-play --windowed --fixed-time --capture-stills captures/hero-bosses --stills-scenes hero,duel-ogre,duel-dragon,duel-ogre-death,duel-dragon-death` (about 2 minutes) beside `captures/trailer-action-edge.mp4`: the knight reads as the trailer's (white lines, chest glow, the crystal blade flaring on each blow), the brightest thing on screen; its legs run without sliding under a torso turned to its swings, each dash pose reads, it flashes red once when hurt and breaks into white shards; the Fat Ogre and the dragon land whole in the frame and shatter the skeletons outward, every move plays into its telegraph's landing, and each boss's death throws its pieces away from the blow as the trailer's dragon did ([evidence](ps-6y2.29/README.md)) | A window on screen; motion a still can't show | ps-6y2.29 | pending |
| Structures in a window: `just champions --file <song>`, then with `--look trailer`: the cottages, huts, towers, walls, fences, pines, trees and rocks read as neon wireframes in the zone's key over a dark fill, apart from the monsters, their lines flaring as the beat wave passes and taking each new zone; a dash through a fence, a tree or a rock shatters it on the frame it lands into hot, outlined chunks thrown along the dash, which clatter down and sink about three seconds later ([evidence](structures/README.md)) | A window on screen and judging the look in motion | ps-6y2.30 | pending |
| The skeleton's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the cyan knights with round shields (or the sheets and `compare.jpg` in [monsters/skeleton](monsters/skeleton/README.md)). The new mesh should read as the same knight with a faceted helm, a broad sword with a crossguard, a flat dark shield and fewer short strokes at the joints; keep it, or delete `tools/recipes/monster/skeleton.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.31 | pending |
| The slime's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the cyan blobs (or the sheets and `compare.jpg` in [monsters/slime](monsters/slime/README.md)). The new mesh should read as a low-poly blob of a few big dark facets looking up with one almond eye and a slit pupil, from every side, where the trailer's is a soft bubble with a lashed eye on its front; check that it still reads as soft goo hopping on the beat, not a gem or a rock; keep it, or delete `tools/recipes/monster/slime.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.32 | pending |
| The bat's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the blue swarmers once they come (or the sheets and `compare.jpg` in [monsters/bat](monsters/bat/README.md)). The new mesh draws each bat as a few closed facets: scalloped wings spoked by three fingers and a fold, a spindle body, a wedge head with two tall ears, no feet or tail; the blind critiques all called it the look and split on whether it reads as a bat as quickly as the vendor's busier drawing. Keep it, or delete `tools/recipes/monster/bat.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.33 | pending |
| The demon's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the deep-blue hunched demons that join at 4:30 (or the sheets and `compare.jpg` in [monsters/demon](monsters/demon/README.md)). The new mesh should read as a horned brute with a broad spiked back, heavy clawed arms and beast's legs, dark inside, apart from the cyan skeletons by its horns and claws, where the vendor's reads as blue lace; keep it, or delete `tools/recipes/monster/demon.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.34 | pending |
| The goblin's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the magenta goblins with swords and shields (or the sheets and `compare.jpg` in [monsters/goblin](monsters/goblin/README.md)). The new mesh should read as an open wireframe of a small hunched goblin soldier, a few simple shapes with dark insides: big drooping leaf ears that show from every side, a long nose and slanted eyes, a broad sword and a plain plank shield, where the trailer's is a filled magenta knot whose ears hide behind its pauldrons; check that it reads as a creature rather than a little robot and that the shield doesn't outshine it in a crowd; keep it, or delete `tools/recipes/monster/goblin.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.35 | pending |
| The mushroom's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the magenta spore shooters in the second half (or the sheets and `compare.jpg` in [monsters/mushroom](monsters/mushroom/README.md)). The new mesh draws a toadstool of a few closed facets: a gored cap with a grinning face high on its front and two large spots behind, on a squat bulb and four thick roots; the trailer's is a soft outline with zigzag spots over a tangle of roots. All eight blind critiques called the new one the look and the better creature, but read it first as a grinning rock or soccer-ball creature, a mushroom only as a second guess; check that it reads as a mushroom monster in play and apart from the goblins. Keep it, or delete `tools/recipes/monster/mushroom.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.36 | pending |
| The big ork's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the orange elites that come at 4:00 (or the sheets and `compare.jpg` in [monsters/bigOrk](monsters/bigOrk/README.md)). The new mesh should read as a hunched brute of a few faceted volumes, dark inside: a big low head with a scowl, slanted eyes, tusks and pointed ears, heavy arms with big fists, one layered pauldron, a strap across the chest, and a notched cleaver whose flat faces the camera as it hangs, where the trailer's is a striped orange clump with a jagged streak of a sword. The blind critiques all preferred it but read it as a horned brute (ork, ogre or minotaur) more than an ork: check that it reads as an ork in play and apart from the demons and the trolls. Keep it, or delete `tools/recipes/monster/bigOrk.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.37 | pending |
| The troll's designed mesh against the trailer's: `just champions --file <song>`, then again with `--look trailer`, watching the amber elites with clubs that come at 5:30 (or the sheets and `compare.jpg` in [monsters/troll](monsters/troll/README.md)). The new mesh should read as a hunched troll of a few faceted volumes, dark inside: a hump of a back with a spine ridge, a big head slung low in front of it with a scowling brow, a blunt nose and short tusks, very long heavy arms with big fists, and a spiked club, where the trailer's is a gold hatching with a round helmet for a head. The blind critiques all preferred it but read it as a gorilla or rock troll whose faceted hump leans toward a stone golem: check that it reads as a troll in play and apart from the big orks beside it. Keep it, or delete `tools/recipes/monster/troll.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.38 | pending |
| The Fat Ogre's designed mesh against the trailer's: `just champions --file <song>` to its duel at about 7 minutes (or `just champions-tool stills --scene duel-ogre`), then again with `--look trailer` (or the sheets and `compare.jpg` in [monsters/fatOgre](monsters/fatOgre/README.md)). The new mesh draws the mid-boss as a few closed faceted shapes with dark insides: a big gut, a strap across it, a dark skull sunk between the shoulders with a scowling brow, big ears, an underbite jaw and tusks, heavy prism arms ending in fists, and a spiked mace; the trailer's is a colouring-book contour of a smooth sculpt. All seven blind critiques called the new one the look aimed for; five of them found the trailer's the clearer ogre and the last two the new one as good or better (the new reads as a troll, a golem or an armoured brute as often as an ogre, its head as a helmet to some); check that it reads as the fat ogre the duel needs, and keep it, or delete `tools/recipes/monster/fatOgre.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.39 | pending |
| The dragon's designed mesh against the trailer's: watch its duel in a window, `just champions-play --windowed --fixed-time --capture-stills captures/dragon --stills-scenes duel-dragon,duel-dragon-death`, then again with `--look trailer` (or play to it, about minute 13; or the sheets, `compare.jpg` and `duel.jpg` in [monsters/dragon](monsters/dragon/README.md)). The new mesh should read as a low-poly dragon of a few faceted shapes, dark inside: bat wings of three fingers with scalloped trailing edges that stay outlined when folded, a chevroned neck and tail ending in a spade, a horned wedge of a head, thick legs; where the trailer's folded and diving wings glow as solid red bands. Judge it in motion: the bottom of each wingbeat still wraps the wings into a lit shape in both looks, and the new dragon is a lighter drawing than the trailer's dense one over the warm zone. Keep it, or delete `tools/recipes/monster/dragon.py` to draw the vendor mesh again | Judging the look in motion, and the operator's eye | ps-6y2.40 | pending |
| Revised weapon taste pass after ps-3wc family tasks: compare hammer swing/slam, bow volley, lightning/field and posed Sentinel at game scale and in motion with six weapons, retained horde and red/yellow boss threats; confirm form changes read as actions and copies remain dimmer than the hero. [Asset probe](combat-assets/README.md) is a sparse Blender storyboard, not this Unity check | Operator aesthetic judgement on a real moving build; automatic Unity captures remain required in implementation tasks | ps-3wc.1 | pending |
| Movement dash and Landing Blast in the assembled horde: timed/off-beat movement feels identical; the immediate whoosh/timing accent and each empowered weapon's local ring remain audible/readable under six weapons; the destination burst leaves red/yellow boss threats visible. [Sparse event-driven stills](movement-dash/README.md) verify local rendering, not crowd motion or speakers | Full-build aesthetic judgement, hand-play and real speakers | ps-3wc.4 | pending |
| Persistent pickups in a moving full build: XP diamonds and healing crosses stay distinct from yellow/red threats; 3 m baseline attraction and 10% recovery every 100 kills feel useful; confirm windowed GPU cost on the low-end Mac at 384 visible representations ([preview](pickups/README.md)) | Hand-play balance, motion and target hardware; headless crowd stills and 100,000-cell CPU probe already verified | ps-3wc.5 | pending |
| Sword/Hammer in a real moving run: judge all four forms and empowered coverage with six weapons and red/yellow boss threats; listen to reused swing/slam cues under music. [Runtime gallery](melee-weapons/README.md) contains inspected seven-phase Unity crowd samples, not a continuous played run or hardware timing measurement | Operator taste, speakers and real hardware; complete-run assembly | ps-3wc.7 | pending |
| Spear/Bow in an assembled six-weapon run: confirm narrow thrust lanes, rain/battery depth and empowerment remain legible over moving crowds and red/yellow threats, and reused whoosh/hit cues balance on real speakers. [Local Unity gallery and sampled motion](spear-bow/README.md) cover all forms/variants against standing crowds | Hand-play, full-build aesthetic judgement and real speakers | ps-3wc.8 | pending |

| Orbit/Sentinel in an assembled moving run: judge the crown's four arms and the sentry/parry/leap/three-knight formation with six weapons and red/yellow boss threats, and listen to reused spectral swing cues under music. [Runtime evidence](construct-weapons/README.md) includes inspected normal/empowered Unity crowd captures and nine ordered motion samples per variant; these do not establish speaker balance or low-end GPU timing | Operator taste, speakers and target hardware; complete-run assembly | ps-3wc.9 | pending |

- ps-3wc.6: hand-play the integrated weapon/encounter build to measure actual
  collected XP, first-choice time and total normal choices against the
  scripted progression samples (evidence/progression/README.md). Check card
  brevity and held/resumed boss-threat readability with six weapons; the
  samples establish the curve's response, not actual run balance.
| Lightning/Ember in a complete six-weapon run with moving bosses: judge constellation size, late field clutter, quiet reused Hit/Slam cues under music and hardware frame time. [Magic runtime evidence](magic-weapons/README.md) includes inspected normal/empowered crowd phases and actual sim-event captures, without claiming a continuous played run | Operator taste, speakers and target hardware; full-run assembly | ps-3wc.10 | pending |

- ps-3wc.12: hand-play the immediate weapon/support cards in a window using
  clicks, 1–3 and arrows/Enter; switch apps during a queued offer, resume,
  and confirm no held key/button causes movement or a dash. Judge form icons,
  short text and the minimal XP/boss-health HUD against a full moving build.
  [Scripted UI captures](survivor-ui/README.md) and synthetic input tests
  verify layout/routing, not actual focus behavior or speaker/taste judgement.

- ps-3wc.11: play the three retained-horde bosses on speakers and the target
  display, checking arrival seals, fan gaps and traveling ring/wall arrival
  timing under six weapons. Automated physical dodge fixtures and actual
  player still previews pass; no speaker, controller or subjective taste
  approval is claimed. See `survivor-bosses/README.md` for clock/pattern contract
  and reproducible private captures.

- ps-3wc.13: hand-play focused, six-weapon broad and defensive builds on music
  and silence, with no Landing Blast and without deliberate horde beat inputs.
  [Assembly matrix](survivor-assembly/README.md) has actual wins/deaths, 27–30
  earned normal levels in successful cases and first choices from 18.8–57.1 s;
  check the 20–30 s first-choice target on less efficient collection routes,
  broad-build fragility, recovery and late Dragon durations. Verify death/Again
  on the continuing music source in a window. Scripted wins/replays establish
  rules and feasibility, not fun, speaker balance or taste. Full-build
  performance/readability is ps-3wc.14, independent of ps-6y2.

- ps-3wc.14: run the full-build stress scenes on an M1 Air class Mac in a
  1080p window at Low, then hand-play ordinary full builds. The M5 Max
  [initial measured load](full-build/README.md) exceeded the simulation proxy; its
  synchronized render measurements do not establish the 1.5 ms GPU budget.
  The latest [CPU proxy sweep](full-build-proxy/README.md) passes all six cases;
  this does not establish target-hardware or GPU acceptance. Compare
  Low/High and `--look trailer`; judge hero visibility, dimmer pickup diamonds,
  red/yellow warning outlines over bodies, and traveling fronts through the
  full encounter. The invulnerable 1,000-body test is deliberately denser than
  a real damaging build and does not establish safe escape routes or fun.
  Screens, speakers, target hardware and operator taste remain unperformed.

## Final survivor review (ps-3wc.15, 2026-11-03)

Sentinel/weapon hold lifetimes and empty-ground Ember empowerment were repaired
with regressions. Default enemy hit feedback retains its hue instead of
whitening the whole struck pack; trailer drawing remains unchanged. See
[Warden evidence](warden-review/README.md) for new IL2CPP captures and raw timing.
Peak hero visibility/warning-route ambiguity was assigned to ps-3wc.16;
the then-exceeded M5 simulation proxy became separate performance work. No
full-build art/performance acceptance is claimed.

- ps-3wc.16: judge the foreground hero and stable, hatched elite danger areas
  in a hand-played six-weapon retained-horde fight, including hurt, dash and
  overlapping slam landings. [Readability evidence](hero-readability/README.md)
  contains actual IL2CPP game-scale and ordered-motion captures plus blind
  visual critique; it does not establish an attainable safe route through the
  invulnerable 1,000-body stress fixture. Confirm the small contrast pocket
  suits the aesthetic and the hatching remains quiet in real-time motion.
  Operator taste, real speakers and target-Mac GPU cost remain pending.

## Simulation burst follow-up (ps-3wc.17, 2026-11-03)

[Profiling and final IL2CPP results](simulation-bursts/README.md) identify
synchronized retained-horde collision planning as the peak. Large-body
queries, grid data layout and neighbor collision passes now do less repeated
work while retaining exact output. All six final scenes keep 1,001 alive and
zero presenter faults, but sim p99 is still 1.404–1.908 ms against the 1 ms
M5 proxy. That implementation gap was carried through the later repairs;
see the current status below. The 1.5 ms GPU-only
proxy also remains unverified; synchronized render/readback is not a GPU
timestamp. These measurements do not accept M1 performance or peak-load art.

## Full-build CPU proxy (ps-3wc.19, 2026-11-03)

The [final actual Master IL2CPP Low sweep](full-build-proxy/README.md) reports
sim p99 0.906–0.976 ms across all six complete-build cases, with 1,001 alive,
zero faults and unchanged event/flight counts. It meets the unchanged 1 ms
M5 simulation proxy in those observations, with limited shared-host headroom.
Earlier failing reports remain historical evidence. The 1.5 ms GPU-only
proxy, M1-class hardware, hand-play, speakers and full-build art judgment
remain unverified; synchronized render/readback is not a GPU timestamp.

### ps-ueo.2: sword and timed rhythm feel

- Agent evidence: 30 fps scripted live-presentation motion at
  `~/Library/Caches/playlist-survivors/evidence/ps-ueo/sword-feel-visible/motion.mp4`;
  ordinary swing/body lead, successive landing clears and aura expiry inspected.
  Exact implementation/scenario limits are in `docs/champions/game-feel-plan.md`.
- Pending operator judgement: natural attack feel at the faster cadence, dash-exit
  readability under moving-horde pressure, and music/speaker impact sync. These
  are subjective checks, not a gate on the unattended implementation delivery.


### ps-ueo.3: health, XP and phase readability

- Implemented red animated health orb with current/max numbers, top-center
  HORDE/BOSS active clock, filled green animated XP, orange/red enemy hazards,
  and blue/magenta opening enemy groups with unchanged body colors.
- Agent evidence and exact fixture limits: `docs/champions/game-feel-plan.md`,
  ps-ueo.3 delivery. Local player frames and motion live under
  `~/Library/Caches/playlist-survivors/evidence/ps-ueo/hud-readability-final/`
  and `hud-opening/`; no media or vendor-derived material is committed.
- Pending non-blocking operator judgement: low-health urgency at native screen
  scale, pickup/shot distinction during overlapping full-build effects, and
  boss-bar/beat-cue readability during moving pressure. Shader/readback tests
  and a scripted capture do not certify subjective readability or fun.

### ps-ueo.4: boss arrival and reinforcement feel

- Arrival clears and visibly shatters the local ordinary horde with no XP
  reward, preserves existing drops/structures, retires the outside old field,
  and admits at most three non-stacking groups of three fodder per boss.
- Agent evidence: `docs/champions/game-feel-plan.md`, ps-ueo.4 delivery;
  ordered actual player frames and `boss-arrival-motion.mp4` under
  `~/Library/Caches/playlist-survivors/evidence/ps-ueo/`. This is an early-call
  encounter fixture with six active weapons and exaggerated initial toughness.
- Non-blocking operator judgement remains: entrance impact/speaker sync,
  reinforcement cadence and pressure during a normal build's boss duration,
  readable escape lanes with live movement, and target-hardware shard cost.

### ps-ueo.5: assembled combat and pacing

- [Assembled evidence](game-feel/README.md) includes normal full-run sim/replay
  results, rendered moving hordes and all three naturally scheduled bosses,
  actual application audio/video, and a real idle death through 1 health.
  A duplicated/uncancellable Sword whoosh was fixed; actual impacts now own
  survivor Sword sounds. No vendor-derived media is committed.
- Non-blocking hand-play: judge natural faster swings and landing spin entry,
  whether the broad yellow arcs leave hazards readable with six weapons, XP
  carpet clutter, urgency of the near-empty orb, and music impact on speakers
  and Bluetooth. Agent timing checks and captured audio do not certify fun or
  perceived speaker/display sync.
- Explicit pacing deviation for review: the four additional winning runs first
  level at 12–14 s, with 33–34 normal choices over 16–17 minutes; their bosses
  take 8–54 s. These are faster than the older 20–30 s first-choice and 1–2 min
  boss targets. Per-hit power was preserved as requested; no automatic damage
  nerf or hidden scaling was added to force those older durations.
- Hardware limits remain: this is shared-host Mono evidence, not an M1 GPU
  budget acceptance. Existing IL2CPP performance evidence remains separately
  authoritative for its measured build/scenarios.

### ps-oli.5: walking-sword assembly

- Current [contract](../../docs/champions/walking-sword-plan.md) and
  [assembly evidence](walking-assembly/README.md) distinguish new live
  soundtrack/analyzer F-primary scripting from synthetic full-run records.
- Hand-play the grip-bound alternating F cut while walking: judge actual
  contact/whiff audio on speakers and Bluetooth, reach and shared Space buffer,
  half-beat reward and genuine off-beat gaps. Compare dark cleared pockets,
  retained green XP and single-type cohorts with the supplied trailer.
- Ordinary big ork/troll elites now have contact threats only; mushrooms and
  all boss warnings remain. Judge progression with 3.5 m pickup reach and
  3/6/9 horde-minute bosses. Old timed-elite and 5/10/15-minute rows above are
  historical. M1/GPU-only timing and human feel remain non-blocking checks.

## Performance at 1,000 alive (ps-6y2.22, 2026-11-03)

[`champions perf` evidence](perf/README.md): on the M5 Max the low tier's GPU
p99 is 1.16 ms (frames it had the GPU alone), its sim p99 0.97 ms and every
tier's frame p99 2.5-2.6 ms, meeting the proxy until an M1-class run replaces it.

- On an M1 Air class Mac with Xcode installed, run
  `just champions-tool perf --tier low --out rhythm_champions/evidence/perf/m1`
  (it builds the IL2CPP player, about 5 minutes, then plays the peak headless:
  level 1 held at 1,000 alive against the maximum build). The low tier holds
  60 fps there if its frame p99 is at most 16.7 ms; its GPU and sim numbers
  replace the M5 Max proxy. Commit the folder beside this one's.
