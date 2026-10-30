# Look set: final, the palette pass (ps-e8r.10, 2026-10-30)

The look pass's last step: the warm stage, the type colours, the hot effects
and the music-reactive post stack, tuned together under one palette (epic
ps-e8r, Decision 1). Rendered by the look harness (IL2CPP player, Apple M5
Max, Metal) at staging `af223e69` plus this bead. The numbers are in
[look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | the epic's [baseline](../baseline/README.md) left, final right |
| [contact-sheet-start.jpg](contact-sheet-start.jpg) | staging `af223e69` (every earlier look bead in, this one not) left, final right |

## The palette

Role by role, with where each colour lives. Linear rgb unless noted.

| role | colour | where |
|---|---|---|
| **Warm stage** | | |
| key light (the moon) | warm neutral (1.0, 0.88, 0.72), 42° up, intensity 1 | arena-layout.json `lighting.moon` |
| key on the horde | 3x the moon, ambient fill 0.5 | `lighting.crowd.key`, `fill` |
| hero's pool | peach (1.0, 0.86, 0.76), intensity 40, 6 m up, 15 m range | `lighting.hero` |
| fighting ground | warm brown, L* 31-45 in the clearings | ArenaBaker's ground albedo (ps-e8r.6) |
| flames | orange (1.0, 0.48-0.62, 0.18-0.32) | `lighting.flames` |
| grade | shadows near black, midtones warm, saturated colours keep their hue | NightLook.Grade (the LUT) |
| **Darkness** | | |
| frame edges | vignette 0.29, cool navy (0.02, 0.025, 0.07); +0.08 in drops, +0.1 at the final peak | `lighting.vignette`, `postFeel.sections` |
| forest and edge bands | L* 2-21 | the kit at `kitValue`, the fog (0.035, 0.05, 0.09 sRGB) |
| **Monster types** (HSV hue, saturation 0.8, dye 0.85) | | roster.json |
| skeleton | 20, rust orange | fodder |
| mushroom | 60, ochre-yellow | fodder |
| goblin | 100, toxic green | fodder |
| slime | 140, emerald | fodder |
| bat | 260, violet | fodder |
| demon | 340, crimson | fodder |
| bigork | 180, cyan-teal | elite |
| troll | 220, icy azure | elite |
| dragon | 300, magenta | boss |
| crowd highlights | lit colour rolls off above 0.35 (linear), toward 0.7 | `lighting.crowd.shoulder` (new) |
| hit flash | ember (1.3, 0.65, 0.2) | `lighting.crowd.flash` |
| **Cool accents** (at most 15% of a frame; measured 0.1-1.0%) | | |
| glowing trees | cyan (0.35, 0.85, 1.0), intensity 10 | `lighting.glow` |
| rune circles | violet (0.42, 0.24, 1.0) / (0.5, 0.26, 1.0) / blue-violet (0.3, 0.4, 1.0), intensity 0.12-0.15 | `atmosphere.runes` |
| lightning and its circle | white → lilac (0.78, 0.5, 1) → violet (0.4, 0.08, 1) | effects.json `heat` |
| fireflies, wisps | cool | effects.json `client.ambient` |
| **Effects** (the brightest values) | | effects.json |
| fire and impacts | white → orange (1, 0.5, 0.08) → red (1, 0.06, 0.02) | `heat` |
| the hero's ring | white → gold (1, 0.72, 0.2) → orange (1, 0.25, 0.02) | `heat` |
| sword slashes | their own blue | |

## What changed

- **Crowd highlights roll off** (`lighting.crowd.shoulder` 0.35, new; the
  player's `--crowd-shoulder k` overrides it). CrowdSkinned scales a lit
  colour whose brightest channel passes the knee down toward twice the knee,
  hue kept. The near-white non-effect pixels were monsters: key-lit yellow
  mushroom caps (sRGB 254, 254, 0), the goblins' yellow-green, the pale
  trolls and hit-flashed bodies. The horde's p90 L* drops from 85-91 to 75-78;
  its median only 3 L*.
- **Effects brighter** (effects.json `hdr`, effect-wide): hit-spark and
  hit-spark-b 1.6, hit-heavy and bolt-impact 1.4, death-burst 0.8 → 1.3,
  death-burst-large 0.8 → 1.2, slam-shock 0.8 → 1.2, the ring 1.4 → 2.0.
  The per-child `tune` that shrank ps-e8r.7's large layers stays, so the
  cores get hotter without the layers growing. Largest clipped region: 0.0%
  in every song still.
- **The climaxes close in** (`postFeel.sections`): a section `vignette`
  offset (new, eased like the rest): drops +0.08, the final peak +0.1; the
  final peak's contrast 10 → 20.
- **Rune circles** read as violet accents instead of white: more saturated
  colours at about 60% of the intensity.
- **The look harness** adds two measures (look.md):
  - `cool`: the share of the frame in the cool accents' hues, HSV hue
    180-280° with C*ab > 20. I read the bead's "180-280 degrees" as HSV
    hue, the roster's hue space, where it runs cyan through violet. In CIE
    hab it would stop short of violet.
  - The effects table now covers the song stills too (each song still gets a
    `--no-effects` twin) and adds `rest p98`, the other pixels' L* p98, with
    `brightest` when the effects' p98 is above it.

Tried and dropped:
- Per-type albedo scale (darkening the mushroom, goblin and troll): it pulled
  the horde's median down with its highlights. forest-edge |dL*| fell to 18.3.
- Lower crowd key (2.2): the same problem, |dL*| 16.8 at forest-edge.
- Heat gain 1.6 (whiter ramps): no change in luminance, by design of the ramp.
- Halved light cues: no change in contrast.

## Against the acceptance criteria

| criterion | result |
|---|---|
| song stills: mean chroma above the baseline's | +30.0 to +39.7 C*ab in every still |
| song stills: RMS contrast above the baseline's | +4.5 to +9.5 in four stills; song-175 replaced by ruling (below) |
| song-175, as ruled: clipped region < 1.5%, effects brightest, chroma and near-black beat the baseline, RMS contrast >= 24.0 | 0.0%, brightest, both met, 24.3 |
| song stills: near-black share below the baseline's | -11.6 to -31.2 points in every still |
| effects' p98 L* above the other pixels' | every song still and beauty shot, by 2.6-23.2 L* (start of this bead: 7 of 11 failed) |
| cool hues at most 15% of a song still | 0.1-1.0% |
| the palette recorded | the table above |
| \|dL*\| >= 17 in every beauty shot | 20.0-24.3 |

### song-175's contrast

The baseline's 31.5 came from the final peak's flat clipped blob (7.5% of the
frame clipped). ps-e8r.7 removed it under its own bar (largest clipped region
under 1.5%); its README and ps-e8r.6's record the drop. The final peak now
has 0.0% clipped. To find how far the frame can go, from this set's still:

| change | song-175 RMS contrast | near-black |
|---|---:|---:|
| this set | 24.3 | 12.2% |
| post contrast 25 → 65 (whole grade) | 27.9 | 23.0% |
| that, and vignette 0.29 → 0.4 | 28.9 | 44.7% (baseline 23.8%) |
| every effect pixel painted L* 90 (offline) | 29.3 | |
| every effect pixel painted pure white (offline) | 31.3 | |
| the ground (not monsters or effects) at 0.6x L* (offline) | 26.7 | |

Even with every effect pixel white, a clipped region of about 11% of the
frame, the still stays under 31.5. Meeting this criterion means bringing the
blob back, which ps-e8r.7's bar forbids, or crushing the frame past the
baseline's near-black share. So this bead left it open: filed as its own
bead (ps-e8r.19) for a ruling, with this evidence.

**Ruling** (Justiciar, incident ps-8r9, 2026-10-30, recorded on ps-e8r.19
and ps-e8r): for song-175 the RMS contrast criterion is replaced, not kept.
The final peak now needs a largest clipped region under 1.5%, effects' p98
L* above the other pixels', mean chroma above and near-black below the
baseline, and RMS contrast of at least 24.0 so it cannot regress. All are met
here. The other four song stills keep RMS contrast above the baseline,
|dL*| >= 17 still holds in every beauty shot, and ps-e8r.7's bar stays.

## Cost

- The shoulder: about 6 ALU operations per crowd fragment (a max3, a
  compare, a divide). No texture sample.
- Effect HDR: colour scales only. Brighter cores may bloom a little more;
  bloom's threshold (1.5) and cost don't change.
- The section vignette: one more volume parameter per frame, already set by
  the beat's breath.
- Runes: intensity and colour only.

Unmeasured on Cygnus: the S0/S3 re-check row in plan.md's Pending on the
operator covers the whole epic.
