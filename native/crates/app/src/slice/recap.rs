//! A completed Survivors run: active time and actual per-weapon damage, alongside replay.
pub mod check;
use super::scene::End;
pub use eval::recap::Summary;
use render::{atlas::Atlases, font::Align, sprites::Sprite};
fn amount(value: f64) -> String {
    if value >= 1e9 {
        format!("{:.1}B", value / 1e9)
    } else if value >= 1e6 {
        format!("{:.1}M", value / 1e6)
    } else if value >= 1e3 {
        format!("{:.1}K", value / 1e3)
    } else if value > 0.0 && value < 10.0 {
        format!("{value:.1}")
    } else {
        format!("{value:.0}")
    }
}
fn held(seconds: f64) -> String {
    let tenths = (seconds.max(0.0) * 10.0).round() as u64;
    format!("{}:{:02}.{}", tenths / 600, tenths / 10 % 60, tenths % 10)
}
fn time(seconds: f64) -> String {
    let seconds = seconds.max(0.0) as u64;
    format!("{}:{:02}", seconds / 60, seconds % 60)
}
#[allow(clippy::too_many_arguments)]
pub fn draw(
    width: f32,
    height: f32,
    age: f64,
    end: End,
    summary: &Summary,
    art: &Atlases,
    out: &mut Vec<Sprite>,
) {
    let alpha = (age / 0.5).clamp(0.0, 1.0) as f32;
    let unit = (width / 960.0).min(height / 540.0);
    let left = (width - 820.0 * unit) * 0.5;
    let top = (height - 486.0 * unit) * 0.5;
    let face = &art.fonts["pixel"];
    let (title, color) = match end {
        End::Victory => ("VICTORY", [0.65, 1.0, 0.76]),
        End::Fell => ("YOU FELL", [1.0, 0.3, 0.36]),
        _ => ("RUN ENDED", [0.75, 0.85, 1.0]),
    };
    out.push(Sprite::solid(
        0.0,
        0.0,
        width,
        height,
        [0.0, 0.0, 0.01, 0.72 * alpha],
    ));
    out.push(Sprite::solid(
        left,
        top,
        820.0 * unit,
        486.0 * unit,
        [0.025, 0.035, 0.065, 0.96 * alpha],
    ));
    out.push(Sprite::solid(
        left,
        top,
        820.0 * unit,
        2.0 * unit,
        [color[0], color[1], color[2], alpha],
    ));
    face.text(
        title,
        width * 0.5,
        top + 19.0 * unit,
        3.5 * unit,
        Align::Centre,
        [color[0], color[1], color[2], alpha],
        out,
    );
    face.text(
        &format!(
            "TIME {}     KILLS {}     LEVEL {}",
            time(summary.elapsed),
            summary.kills,
            summary.level
        ),
        width * 0.5,
        top + 67.0 * unit,
        1.8 * unit,
        Align::Centre,
        [0.94, 0.95, 1.0, alpha],
        out,
    );
    let columns = [
        left + 38.0 * unit,
        left + 470.0 * unit,
        left + 620.0 * unit,
        left + 778.0 * unit,
    ];
    for (index, title) in ["WEAPON", "DAMAGE", "TIME HELD", "DPS"]
        .into_iter()
        .enumerate()
    {
        face.text(
            title,
            columns[index],
            top + 107.0 * unit,
            1.3 * unit,
            if index == 0 {
                Align::Left
            } else {
                Align::Right
            },
            [0.55, 0.7, 0.85, alpha],
            out,
        );
    }
    let rows = &summary.rows;
    let max = rows.iter().map(|r| r.damage).fold(1.0, f64::max);
    for (index, row) in rows.iter().enumerate() {
        let y = top + (132.0 + 28.0 * index as f32) * unit;
        let evolved = sim::survivors::weapon_catalog()
            .get(row.id)
            .evolution_of
            .is_some();
        let tint = if evolved {
            [1.0, 0.83, 0.4, alpha]
        } else {
            [0.8, 0.88, 1.0, alpha]
        };
        out.push(Sprite::solid(
            left + 24.0 * unit,
            y - 3.0 * unit,
            772.0 * unit,
            25.0 * unit,
            [
                0.1,
                0.16,
                0.23,
                if index % 2 == 0 {
                    0.35 * alpha
                } else {
                    0.12 * alpha
                },
            ],
        ));
        out.push(Sprite::solid(
            left + 60.0 * unit,
            y + 18.0 * unit,
            290.0 * unit * (row.damage / max) as f32,
            1.0 * unit,
            [tint[0], tint[1], tint[2], 0.5 * alpha],
        ));
        let item = sim::survivors::Item::Weapon(row.id);
        if let Some(icon) = art.icons.get(super::cards::icon(item)) {
            out.push(
                Sprite::centred(
                    *icon,
                    left + 40.0 * unit,
                    y + 7.0 * unit,
                    20.0 * unit,
                    20.0 * unit,
                )
                .tinted(tint),
            );
        }
        face.text(
            item.name(),
            left + 60.0 * unit,
            y,
            1.3 * unit,
            Align::Left,
            tint,
            out,
        );
        // Keep the HUD's human-facing damage scale; the ledger retains raw applied values.
        for (column, text) in [
            (1, amount(row.damage * 10.0)),
            (2, held(row.held_s)),
            (3, amount(row.dps * 10.0)),
        ] {
            face.text(
                &text,
                columns[column],
                y,
                1.3 * unit,
                Align::Right,
                [0.95, 0.96, 1.0, alpha],
                out,
            );
        }
    }
    face.text(
        "W WATCH MY RUN     ENTER NEW RUN",
        width * 0.5,
        top + 450.0 * unit,
        1.5 * unit,
        Align::Centre,
        [0.7, 0.84, 1.0, alpha],
        out,
    );
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn held_time_keeps_short_ownership_visible_and_rounds_near_whole_seconds() {
        assert_eq!(held(0.533_333), "0:00.5");
        assert_eq!(held(11.999_999), "0:12.0");
        assert_eq!(held(1800.0), "30:00.0");
    }
}
