//! Survivors build strength and bounded presentation of actual drop-triggered swarms.
pub mod check;
use eval::live::LiveView;
use render::{atlas::Atlases, sprites::Sprite};
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Aura {
    pub color: [f32; 3],
    pub alpha: f32,
    pub size: f32,
    pub count: usize,
}
pub fn aura(view: &LiveView) -> Aura {
    let count = view
        .inventory
        .map_or(0, |i| {
            i.weapons
                .iter()
                .filter(|w| {
                    sim::survivors::weapon_catalog()
                        .get(w.id)
                        .evolution_of
                        .is_some()
                })
                .count()
        })
        .min(5);
    let mix = count as f32 / 5.0;
    Aura {
        color: [0.3 + 0.7 * mix, 0.8 + 0.12 * mix, 1.0 - 0.75 * mix],
        alpha: if count == 0 {
            0.0
        } else {
            0.12 + 0.045 * count as f32
        },
        size: 1.15 + 0.1 * count as f32,
        count,
    }
}
pub fn drop_trails(
    view: &LiveView,
    art: &Atlases,
    at: &dyn Fn(f64, f64) -> (f32, f32),
    scale: f32,
    drop: f64,
    out: &mut Vec<Sprite>,
) {
    if view.run.is_none() || drop < 0.02 {
        return;
    }
    let Some(glow) = art.proc.get("glow") else {
        return;
    };
    for (_, foe) in view
        .alive()
        .filter(|(_, f)| matches!(f.kind.as_str(), "vs-bat-swarm" | "vs-ghost-swarm"))
        .take(64)
    {
        let (dx, dy) = ((foe.x - foe.prev.0) as f32, (foe.y - foe.prev.1) as f32);
        if dx.hypot(dy) < 0.1 {
            continue;
        }
        let (x, y) = at(
            foe.x - f64::from(dx) * 0.5,
            foe.y - f64::from(dy) * 0.5 - 12.0,
        );
        out.push(
            Sprite::centred(
                *glow,
                x,
                y,
                (dx.hypot(dy) * 2.0 + 12.0).min(48.0) * scale,
                5.0 * scale,
            )
            .rotated(dy.atan2(dx))
            .tinted([0.8, 0.55, 1.0, 0.4 * drop as f32])
            .additive(),
        );
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    use sim::survivors::{Inventory, WeaponId, weapon_catalog};
    #[test]
    fn aura_reads_actual_owned_evolutions_and_ignores_pinned_tier_and_base_ranks() {
        let mut view = LiveView {
            run: Some(sim::survivors::RunState::default()),
            tier: 3.0,
            ..Default::default()
        };
        let mut inventory = Inventory::default();
        for id in [
            WeaponId::Sword,
            WeaponId::Garlic,
            WeaponId::MagicWand,
            WeaponId::KingBible,
            WeaponId::LightningRing,
        ] {
            inventory.weapons.upgrade(id, 8).unwrap();
        }
        view.inventory = Some(inventory);
        assert_eq!(aura(&view).count, 0);
        assert_eq!(aura(&view).alpha, 0.0);
        let mut last = 0.0;
        for id in [
            WeaponId::BloodyTear,
            WeaponId::SoulEater,
            WeaponId::HolyWand,
            WeaponId::UnholyVespers,
            WeaponId::ThunderLoop,
        ] {
            inventory
                .weapons
                .replace(weapon_catalog().get(id).evolution_of.unwrap(), id)
                .unwrap();
            view.inventory = Some(inventory);
            let current = aura(&view);
            assert!(current.alpha > last);
            last = current.alpha;
        }
        assert_eq!(aura(&view).count, 5);
        assert!(aura(&view).alpha < 0.35);
    }
}
