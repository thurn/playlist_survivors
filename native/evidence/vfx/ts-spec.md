# The TS game's effects, as a spec for the port (hv-t8o8.84)

Extracted from `ts-freeze` (e97ef4a) by a read-only survey of `src/plan/evaluate.ts`,
`effects-life.ts`, `tier-look.ts`, `reactive-look.ts`, `src/runtime/renderer.ts`, `shaders.ts`,
`src/planner/emit.ts` and `content/effects.yaml`. Line numbers refer to e97ef4a. A reference, not an
oracle: the port matches look and feel.

## Recipe layers (evaluate.ts 1151-1291)

Age per layer: world clock `w - ev.w`, song clock `s - ev.s`; skipped while negative. `param(p, I)` =
number, or `lerp(a, b, I)`. `u = age/life`.

- shake: `t = param(trauma) - 1.6*age` summed, `trauma = min(1, sum)`, `shake = 9*trauma^2` px.
- flash: `param(opacity)*(1-u)`, the strongest flash wins.
- aberration: skipped if `I < 0.8` (except finale-blast): the only layer the 0.8 cut touches.
- light: radius `param(radius)*(1+0.3u)`, strength `param(strength)*(1-u)^1.5`.
- distortion: shockwave radius `param(radius)*u`, strength `param(strength)*(1-u)`.
- ring: `e = 1-(1-u)^3`; radius `param(radius)*scale*e`; width `param(width)*(1-0.6u)`; alpha `1-u^2`;
  drawn at (x, y-8), vertically 0.75, alpha `alpha*clamp(width/4, 0.4, 1)`, additive by default.
- text: rises `18*smoothstep(min(1, 3u))`, alpha `1-smoothstep((u-0.6)/0.4)`.
- sprite: scale `param(scale)*ev.scale`, alpha `(1-u^2)*tierA`; a swing hit's sprites take
  `SWORD_TINTS[min(3, swingTier)]` and `TIER_ALPHA = [0.5, 0.7, 0.88, 1]`. `align: hitShape`: an arc of
  half angle >= pi draws `proc:ring` (`r/62`), other arcs `arc120`/`arc150`/`arc200` by full angle
  (<135, <180, else; base 62/78/98), rotated `dir`, scale `(r/base)*scale*(0.85+0.25u)`, y-14;
  capsules a streak along the axis; circles a ring `(r/62)*(0.8+0.3u)`. `align: up`: y -= 10 + 40u.
- burst: `count*TIER_COUNT` (`[0.5, 0.75, 1, 1.3]` for swing hits); angle `spread >= 360 ? r1*2pi :
  dir + (r1-0.5)*spread`; speed `param(speed)*(0.45+1.1*r2)`; distance under drag `sp*(1-e^-dt)/d`;
  `y = ev.y - 12 + sin*dist*0.75 + 0.5*g*age^2` (debris settles at `ev.y + 4 + 8*r3`); scale
  `size*(0.7+0.6*r3)` (dust x(1+u)); alpha `(1-u)^1.5` sparks, `(1-u)` others.
- decal: life 20 s world, size `param(size)*scale`, alpha `min(1, 20*age)*(1-smoothstep((age-0.6L)/
  (0.4L)))*0.85`, debris tint `ev.debris[0]`, normal blend under the characters.

Draw order: ground, zone-wave tiles, decals (+ hero ground glow, slam markers), shadows, additive
aura, additive under-particles, y-sorted characters and props, overlay (blades, arrows, gems), normal
fx, normal particles, bolts, additive fx, additive particles, text.

## Who fires which recipe (emit.ts)

| Recipe | Cause | Arguments |
|---|---|---|
| hit-light / hit-heavy | every swing strike (329) | `sw.s`, hero, `sw.dir`; `I = min(1, h.intensity*(0.55+0.45*groove) + 0.25*groove)`; swing tint and tier alpha/count |
| hit-light/medium/heavy | non-lethal fate of a move hit (358) | enemy, `h.dir`, `0.6*h.intensity` |
| blades-hit | the orbit catching an enemy; any non-move non-lethal fate | `h.intensity` |
| lightning-hit | every chain jump | hit centre, `h.intensity` |
| fire-hit | the fire trail catching an enemy | `h.intensity` |
| blast-hit | a lethal hit from the finale's ring | `max(0.4, I)`, debris |
| death-<kind> | lethal fate (`enemies.yaml death.recipe`) | enemy, `h.dir`, `max(0.4, I)`, debris |
| hit-heavy + hit-burst | boss kill | `I = 1` |
| finale-blast | the finale's blast | hero y-16, `I = 1` |
| level-up | each upgrade | hero y-16, `I = 1` |
| accent-flash | song accents of strength >= 0.9 | `strength*secIntensity` |
| stomp | a stomp landing | `I = 1` |
| dust | a stride landing (`I = 0.5`); each landmark (`I = 1`) | |
| gem-pickup | a gem arriving (`I = 0.7`); a vacuum touched (`I = 1`) | under the characters |
| arrow-release / arrow-shatter | an arrow's release (`0.6`) / its touch (`0.8`) | |
| boss-slam / boss-land | a slam (`scale = radius/60`) / the boss landing | `I = 1` |
| near-miss | a threat's near miss | `I = 1` |
| shockwave | each cleave wave band hit | arc centre, `h.dir`, `I = 0.8`, swing tint |
| never fired | hit-spin, hit-dash, hit-sweep, dodge, death-elite | |

The tier never enters `I`: it drives tint, `TIER_ALPHA`, `TIER_COUNT` and the hit shape's size.

## Reactive fast lane (evaluate.ts 569-661)

- Pulse: `amp = clamp((evidence - gate)/gate)` at the latest tick; over (s-0.5, s] `mix` = share of
  ticks at or above the gate and `loudness` = mean energy; `press = exp(-(s - lastPress)/0.25)`;
  `pulsed(v) = mix*amp*v + (1-mix)*press`.
- Accent flash and punch: per strong onset (knownAt, >= 0.5 s apart), age <= 0.6: flash
  `0.22*exp(-age/0.07)` white, zoom punch `0.018*exp(-age/0.16)`.
- Press ripple: per press, age < 0.45: ring at the hero (y-12), radius `14 + 150*age`, width
  `3*(1-age/0.45) + 1`, alpha `0.7*(1-age/0.45)`, additive; tint perfect #ffd040, great #ffffff, good
  #9fd0ff, loose #6a7080, extra #8a8a96, stray #e04848.
- Ground glow: share `glow` of Great+ presses over the last 2 s: light at the hero (y+6), radius
  `90 + 60*glow`, #fff0b0, strength `0.55*glow`.
- Onset bursts: per onset (knownAt), age < 0.4, `n = sqrt(strength/peak)` (peak halves each 8 s),
  `fade = 0.35 + 0.65*amp*mix`; centre at a seeded angle, radius 50-170 px (y x0.75): a glow (scale
  `0.5+0.9n`, alpha `fade*0.6*n*(1-u)^2`), `round(3+7n)` sparks flying 10-40 px x(0.5+n), a light
  (radius `30+50n`, strength `0.4*n*fade*(1-u)^2`), all in the song's key tint.
- Horde: the latest onset's `flinch = n*exp(-age/0.07)` squashes enemies; on the beat they squash
  `1 +- 0.16*groove*beatHit`, non-steppers bob `4*groove*sin(pi*u)`.
- Lights: `uLightPulse = 1 + (0.05 + 0.1*loudness)*(0.4+0.6*loudness)*beatPulse`; ambient
  `min(1.2, amb*1.1*(0.8+0.4*loudness) + 0.08)`; vignette `0.45 + 0.25*barPulse*intensity`.

## Cleaves and tiers

- Swing arcs: light radius/degrees by tier 100/150, 130/180, 160/240, 190/360; heavy 130/200,
  160/230, 190/290, 220/360 (tier 3 a full ring). `SWORD_TINTS = [#ffffff, #9fe4ff, #9fffd8, #ffe070]`
  by the tier at the wind-up.
- Cleave shockwave bands: `shockwave` per band hit, reach 260/300/340/380 by tier.
- Tier-3 spin: facing turns down, left, up, right over the wind-up.
- Afterimages: coded but never drawn (the trail channel is never written).
- Hero ground glow always (`proc:glow` at the feet, 1.6 x 0.8; `AURA_TINTS[tier]` alpha 0.7 from
  tier 1, else #fff0b0 alpha 0.55); the aura from tier 1, additive at (x, y-z-16), scale
  `(1.5 + 0.25*pulse, 1.9 + 0.3*pulse)`, `AURA_TINTS = [-, #60c0ff, #70f0c0, #ffd040]`, alpha
  `[0, 0.24, 0.34, 0.48][tier]*(0.8 + 0.2*pulse)`.
- Tier change ring: one beat, #80ffe0, alpha `0.6*flash`, radius the pickup radius
  `[40, 65, 95, 120][tier]`, flattened 0.7.
- Groove meter tier-up flash: 0.6 s, a rect behind the bar in the tier colour at `0.45*flash`.
- Heart glow before a cleave accent, cleave ticks on the beat track in #ffc040.

## Weapons

- Orbiting blades: count/radius by tier {3, 72}, {3, 90}, {4, 105}, {5, 120} (evolved +2, x1.2), a
  turn a bar; blade at (x + cos*R, y + sin*R*0.8), y-18, rotation 3*theta, tints
  `[#ffffff, #d8f4ff, #d8fff0, #fff4c0]`, glow `[#80c8ff, #50d8ff, #60f0c0, #ffd040]`.
- Fire trail: ten flames at the hero's past positions (every 0.07 w), `fx:fire` additive.
- Chain lightning: jittered segments, outer stroke `3 + tier` in `[#8fb8ff, #70d8ff, #80ffd0,
  #ffe070]`, a 1 px white core, fading 0.18 s after the last point.
- Arrows and slam markers (red glow and ring).

## Gems

Burst out, hang, fly to the hero in an arc (`-30*sin(pi*fly)`), `gem-pickup` on arrival.

## World look

- Zone sweep: the ring grows 175 px a sixteenth for 8 sixteenths; inside it the new zone's tiles and
  props; two additive rings at its edge (the new zone's ambient light, alpha 0.75; its tint, wider by
  60 px, alpha 0.35); the grade blends `t = smoothstep((radius - dist(camera, wave) + 150)/300)`.
- Key look: per detected section, the commonest key over the 4 s before; hue walks the circle of
  fifths, minor cooler and darker; `mul = lerp(1, c, 0.1)*dark`, `lift = 0.018*c`; key tint for the
  fast lane's effects lerped 0.45 to white.
- Prop sway on the bar (trees, bushes, mushrooms skew 0.06 x sway).
- Enemy hit flash (white frames 0.05 w), lethal knockback, death fade over 0.5 w.
