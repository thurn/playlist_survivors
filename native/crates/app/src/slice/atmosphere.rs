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
    let (ranks, count) = view.inventory.map_or((1usize, 0usize), |i| {
        (
            i.weapons
                .iter()
                .map(|w| {
                    usize::from(
                        sim::survivors::weapon_catalog()
                            .get(w.id)
                            .evolution_of
                            .map_or(w.rank, |base| {
                                sim::survivors::weapon_catalog().get(base).max_rank()
                            }),
                    )
                })
                .sum::<usize>()
                + i.passives
                    .iter()
                    .map(|p| usize::from(p.rank))
                    .sum::<usize>(),
            i.weapons
                .iter()
                .filter(|w| {
                    sim::survivors::weapon_catalog()
                        .get(w.id)
                        .evolution_of
                        .is_some()
                })
                .count(),
        )
    });
    let count = count.min(5);
    let growth = (ranks.saturating_sub(1) as f32 / 50.0).clamp(0.0, 1.0);
    let mix = count as f32 / 5.0;
    Aura {
        color: [0.3 + 0.7 * mix, 0.75 + 0.15 * growth, 1.0 - 0.65 * mix],
        alpha: 0.14 + 0.11 * growth + 0.015 * count as f32,
        size: 1.05 + 0.2 * growth + 0.05 * count as f32,
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
    fn aura_starts_visible_grows_with_real_ranks_and_keeps_evolution_layers() {
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
            for _ in 0..8 {
                inventory.weapons.upgrade(id, 8).unwrap();
            }
        }
        view.inventory = Some(inventory);
        assert_eq!(aura(&view).count, 0);
        let strong = aura(&view);
        let mut opening_inventory = Inventory::default();
        opening_inventory
            .weapons
            .upgrade(WeaponId::Sword, 8)
            .unwrap();
        view.inventory = Some(opening_inventory);
        let opening = aura(&view);
        assert!(
            opening.alpha >= 0.14 && strong.alpha > opening.alpha && strong.size > opening.size
        );
        view.tier = 0.0;
        view.groove = 0.0;
        assert_eq!(
            aura(&view),
            opening,
            "zen input grades cannot erase starting presence"
        );
        for _ in 0..5 {
            inventory
                .passives
                .upgrade(sim::survivors::PassiveId::Armour, 5)
                .unwrap();
        }
        view.inventory = Some(inventory);
        assert!(aura(&view).alpha > strong.alpha);
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
