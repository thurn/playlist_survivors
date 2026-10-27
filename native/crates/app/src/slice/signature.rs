//! Held-world signature staging; cosmetic props never alter collision or rewards.
use super::gfx::Sprite;
use eval::{field::FieldProp, live::LiveView};
use render::{atlas::Atlases, font::Align};
pub fn stones(view: &LiveView) -> Vec<FieldProp> {
    static LAYOUT: std::sync::OnceLock<Vec<(String, usize, f64, f64)>> = std::sync::OnceLock::new();
    let layout = LAYOUT.get_or_init(|| {
        let v: serde_json::Value = serde_json::from_str(include_str!(concat!(
            env!("CARGO_MANIFEST_DIR"),
            "/../../../content/landmarks/standing-stones.tmj"
        )))
        .expect("authored stones");
        v["layers"][0]["objects"]
            .as_array()
            .unwrap()
            .iter()
            .map(|p| {
                let variant = p["properties"]
                    .as_array()
                    .and_then(|a| a.iter().find(|p| p["name"] == "variant"))
                    .and_then(|p| p["value"].as_u64())
                    .unwrap_or(0) as usize;
                (
                    p["name"].as_str().unwrap().into(),
                    variant,
                    p["x"].as_f64().unwrap() - 384.0,
                    p["y"].as_f64().unwrap() - 384.0,
                )
            })
            .collect()
    });
    view.landmarks
        .iter()
        .filter(|l| l.id == "standing-stones")
        .flat_map(|l| {
            layout.iter().map(move |(sprite, variant, x, y)| FieldProp {
                sprite: sprite.clone(),
                variant: *variant,
                x: l.x + x,
                y: l.y + y,
                flip: false,
            })
        })
        .collect()
}
fn ring(
    art: &Atlases,
    at: &impl Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
    p: (f64, f64),
    radius: f64,
    color: [f32; 4],
) {
    if let Some(glow) = art.proc.get("glow") {
        for k in 0..48 {
            let a = f64::from(k) * std::f64::consts::TAU / 48.0;
            let (x, y) = at(p.0 + a.cos() * radius, p.1 + a.sin() * radius);
            out.push(
                Sprite::centred(*glow, x, y, 6.0 * scale, 6.0 * scale)
                    .tinted(color)
                    .additive(),
            );
        }
    }
}
pub fn draw(
    view: &LiveView,
    art: &Atlases,
    at: &impl Fn(f64, f64) -> (f32, f32),
    scale: f32,
    out: &mut Vec<Sprite>,
) {
    let song = view.run.map_or(view.s, |r| r.presentation_s);
    let world = view.run.map_or(view.s, |r| r.world);
    for mark in view
        .slam_marks
        .values()
        .filter(|m| song >= m.s_mark && song < m.s_land)
    {
        let u = ((song - mark.s_mark) / (mark.s_land - mark.s_mark).max(1e-6)).clamp(0.0, 1.0);
        ring(
            art,
            at,
            scale,
            out,
            (mark.x, mark.y),
            sim::engine::SLAM_RADIUS,
            [1.0, 0.25, 0.06, 0.9],
        );
        ring(
            art,
            at,
            scale,
            out,
            (mark.x, mark.y),
            sim::engine::SLAM_RADIUS * (1.0 - u),
            [1.0, 0.7, 0.2, 0.8],
        );
    }
    for slam in view
        .slams
        .iter()
        .filter(|m| world >= m.w_land && world - m.w_land < 0.45)
    {
        let u = (world - slam.w_land) / 0.45;
        ring(
            art,
            at,
            scale,
            out,
            (slam.x, slam.y),
            slam.radius + u * 70.0,
            [1.0, 0.5, 0.15, (1.0 - u) as f32],
        );
    }
    if let Some(t) = view.punctuation
        && world >= t
        && world - t < 0.4
        && let Some(h) = view.hero
    {
        let u = (world - t) / 0.4;
        ring(
            art,
            at,
            scale,
            out,
            (h.x, h.y),
            35.0 + u * 130.0,
            [0.4, 0.7, 1.0, ((1.0 - u) * 0.3) as f32],
        );
    }
    if let Some((id, _, _, health)) = view.signature
        && let Some(f) = view.foes.get(&id).filter(|f| f.death_w.is_none())
    {
        let (x, y) = at(f.x, f.y - 88.0);
        out.push(Sprite::solid(
            x - 42.0 * scale,
            y,
            84.0 * scale,
            5.0 * scale,
            [0.1, 0.025, 0.025, 0.9],
        ));
        out.push(Sprite::solid(
            x - 42.0 * scale,
            y,
            84.0 * scale * health as f32,
            5.0 * scale,
            [1.0, 0.3, 0.15, 1.0],
        ));
        art.fonts["pixel"].text(
            "MINOTAUR",
            x,
            y - 10.0 * scale,
            scale,
            Align::Centre,
            [1.0, 0.75, 0.4, 1.0],
            out,
        );
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn actual_stones_layout_is_bounded_and_centered_on_recorded_landmark() {
        let mut v = LiveView::default();
        v.landmarks.push(sim::recorder::LandmarkRecord {
            id: "standing-stones".into(),
            s: 90.0,
            w: 90.0,
            x: 100.0,
            y: 200.0,
        });
        let p = stones(&v);
        assert_eq!(p.len(), 14);
        assert_eq!(
            p.iter().filter(|p| p.sprite == "standing-stone").count(),
            10
        );
        assert!(p.iter().all(|p| (p.x - 100.0).hypot(p.y - 200.0) <= 330.0));
    }
}
