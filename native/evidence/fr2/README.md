# FR2 — Sword and accents

The survivors sword now uses the character's aimed wind-up/strike/follow-through
path. Its primary is radial, starting at 190 px, with Area and catalog-rank growth.
Each normal hit applies catalog power times Might. There is no tempo compensation.
Amount adds extra attacks. A primary hit ID links the recorded swing, shot, damage,
direction, visible radius and attack audio. Sword shot publication emits no second
swing sound; Bloody Tear keeps its critical damage and shared heal cooldown.

Heavy accents replace the ordinary primary with 2.5 times power and 1.3 times
radius (the shipped light/heavy recipe). The outward cleave adds one base-power
hit to each outer target; its group excludes targets of the primary and remembers
its own catches. Forecast/onset celebrations share a bounded deduplication ledger.
Legacy's timing requirements still guard judged cleaves. Zen adds no hitstop.
The camera keeps its bounded existing light/heavy punches. The directional arc
and afterimages take the primary attack's direction; wave effects take actual hit
geometry. Resume discards a menu-interrupted wind-up rather than repaying attacks.

Focused tests use real catalog power, four-direction radial targets, 190 px body
boundaries, actual emitted hit IDs, accent/onset records, freeze/resume and the
Bloody Tear critical/heal path. Tempo checks cover 70/128/174 BPM. These are
behavior checks, not claims of balanced survival or audible/visual feel.

Playable from `native/`:

```
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
```

FR3 handles the audible mix; FR4 restores the HUD and legacy tier bypasses;
FR10 handles integrated balance and real-music side-by-side qualification.

Cold review: both findings fixed. Bloody Tear's speculative pulse no longer adds
a second fire cue; the five-evolution recorder/recap test asserts this while
actual evolved attacks run. Pending cleaves retain the exact primary target IDs,
so a later qualifying legacy press cannot forget them after another weapon hit.
A regression launches that delayed reward after an intervening fire-trail hit.

`normal.png` and `accent.png` were inspected: the hero uses its real slash pose,
and the directed afterimages use the published radius. These are labelled
synthetic 128 BPM, no-enemy, silent renders, not a feel/balance/audio certificate.
The 120-frame clip is retained locally in `captures/feel-fr2/spin-and-accent.mp4`.
The visible generic enlarged ring was removed from survivor swings; weapon arcs
carry the actual primary and wave geometry. HUD restoration remains FR4.

Dependent offer/audio projection fixtures remain explicitly synthetic: fast XP
carriers finish their admitted step inside pickup reach, and a durable enlarged
body overlaps the maxed aura/orbit with recoil debuffs disabled. They prove real
card/rollback/contact projection, not production balance.
