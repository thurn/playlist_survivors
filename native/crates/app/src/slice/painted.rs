//! The painted dusk look under `--art heroeditor` (docs/design/heroeditor-native.md, "The Painted
//! Ground"): each zone's ground colours for the painted ground (`render::ground`), and a warm,
//! low-sun evening grade over native's own, its beat pulses kept.

use render::ground::{Ground, Palette};
use render::post::Grade;
use sim::content::types::ZoneTheme;

use super::fx::hex;

/// The painted ground in view: centred on the camera (world px), `scale` logical pixels per world
/// pixel, the zone's palette and, inside the transform wave's ring, the next zone's.
pub fn ground(
    seed: f64,
    theme: &ZoneTheme,
    wave: Option<(&ZoneTheme, f64, f64, f64)>,
    (cx, cy): (f64, f64),
    scale: f32,
) -> Ground {
    let bits = seed.to_bits();
    Ground {
        center: [cx as f32, cy as f32],
        scale,
        seed: (bits ^ (bits >> 32)) as u32,
        palette: palette(theme),
        wave: wave.map(|(z, x, y, r)| (palette(z), [x as f32, y as f32, r as f32])),
    }
}

/// A zone's ground colours: each zone shifts the season, from the glade's cool green to the
/// infernal grove's scorched rock and ember paths. Unknown zones get the woods'.
fn palette(theme: &ZoneTheme) -> Palette {
    let p = |c: [&str; 8]| {
        let [grass, lush, moss, earth, path, tuft, tip, pebble] = c.map(|s| {
            let v = hex(s);
            [v[0], v[1], v[2]]
        });
        Palette {
            grass,
            lush,
            moss,
            earth,
            path,
            tuft,
            tip,
            pebble,
        }
    };
    match theme.id.as_str() {
        "twilight-glade" => p([
            "#357050", "#5a9a5e", "#3e6c46", "#80766a", "#a0927c", "#285c40", "#b0d68c", "#a09cb0",
        ]),
        "mushroom-grove" => p([
            "#556238", "#7a7e42", "#6e5634", "#9a7a58", "#a88c66", "#4a5028", "#d8a44c", "#9e8c7a",
        ]),
        "ember-hollow" => p([
            "#574a3a", "#6e5e44", "#4c3828", "#8a6a50", "#93705a", "#3e3224", "#cf8a44", "#857670",
        ]),
        "infernal-grove" => p([
            "#3c2c2a", "#4e3830", "#2c2222", "#5a3a30", "#6e3a2a", "#281c1c", "#e07838", "#5a4e4c",
        ]),
        _ => p([
            "#3d7a3a", "#6aa648", "#4f7330", "#8f7a56", "#a89470", "#2f6230", "#b4d468", "#a49c8c",
        ]),
    }
}

/// Dusk over the zone's grade `g` (whose own grade was `zone_mul`): a warm ambient bright enough
/// for flat colours to read, tinted a little by the zone's light; warm highlights, violet
/// shadows and a low sun's glaze from the top left. The pulses, bloom and vignette stay.
pub fn dusk(g: &mut Grade, theme: &ZoneTheme, zone_mul: [f32; 3], loud: f32) {
    let amb = hex(&theme.ambient_light);
    let top = amb[0].max(amb[1]).max(amb[2]).max(1e-3);
    let level = 0.9 + 0.18 * loud;
    for k in 0..3 {
        let warm = [1.0, 0.9, 0.8][k];
        g.ambient[k] = level * (0.86 * warm + 0.14 * amb[k] / top);
        // (The zone's grade a little, the key's look, as `scene::grade` set it, kept.)
        g.mul[k] *= [1.05, 0.98, 0.9][k] * (1.0 + 0.4 * (zone_mul[k] - 1.0)) / zone_mul[k];
    }
    g.lift[2] += 0.012;
    g.contrast = 1.08;
    g.saturation *= 1.04;
    g.shadows = [0.05, 0.0, 0.1];
    g.highlights = [0.035, 0.015, -0.015];
    g.sun = [1.0, 0.6, 0.3, 0.08];
}
