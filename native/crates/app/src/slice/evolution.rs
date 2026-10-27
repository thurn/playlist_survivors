//! A chest's actual reward identity drives a bounded, retractable transformation.
use super::sounds::Cue;
use eval::live::LiveView;
use render::{
    atlas::Atlases,
    font::Align,
    post::Light,
    sprites::{Arc, Sprite},
};
use sim::survivors::{FreezeReason, Inventory, Item, Roulette, RunState, WeaponId};

const LIFE: f64 = 1.1;
pub fn evolved(id: WeaponId) -> bool {
    matches!(
        id,
        WeaponId::BloodyTear
            | WeaponId::SoulEater
            | WeaponId::HolyWand
            | WeaponId::UnholyVespers
            | WeaponId::ThunderLoop
    )
}
pub fn color(id: WeaponId) -> [f32; 3] {
    match id {
        WeaponId::BloodyTear => [1.0, 0.12, 0.25],
        WeaponId::SoulEater => [0.3, 0.95, 0.9],
        WeaponId::HolyWand => [1.0, 0.82, 0.3],
        WeaponId::UnholyVespers => [0.78, 0.35, 1.0],
        WeaponId::ThunderLoop => [1.0, 0.4, 0.85],
        _ => [1.0; 3],
    }
}
fn rewards(chest: Roulette) -> Vec<WeaponId> {
    let mut ids = Vec::new();
    for item in chest.rewards.into_iter().flatten() {
        if let Item::Weapon(id) = item
            && evolved(id)
            && !ids.contains(&id)
        {
            ids.push(id);
        }
    }
    ids
}
fn key(id: WeaponId) -> &'static str {
    match id {
        WeaponId::BloodyTear => "evolve-bloody-tear",
        WeaponId::SoulEater => "evolve-soul-eater",
        WeaponId::HolyWand => "evolve-holy-wand",
        WeaponId::UnholyVespers => "evolve-unholy-vespers",
        WeaponId::ThunderLoop => "evolve-thunder-loop",
        _ => unreachable!("only evolved rewards"),
    }
}
/// Forecast state supplies lead; the existing cue reconciler cancels a retracted grant.
/// Five sequential rewards share one transient, preserving the three-voice UI cap.
pub fn cues(run: Option<RunState>) -> Vec<Cue> {
    let Some(chest) = run.and_then(|r| r.roulette) else {
        return Vec::new();
    };
    let Some(landed) = chest.landed.filter(|s| s.is_finite()) else {
        return Vec::new();
    };
    let ids = rewards(chest);
    if ids.is_empty() {
        return Vec::new();
    }
    vec![Cue {
        key: if ids.len() > 1 {
            "evolve-multiple"
        } else {
            key(ids[0])
        },
        s: landed,
        id: u32::try_from(chest.id).unwrap_or(u32::MAX),
        gain: 0.75,
    }]
}
struct Burst {
    chest: usize,
    landed: u64,
    weapons: Vec<WeaponId>,
    age: f64,
}
#[derive(Default)]
pub struct Evolution {
    burst: Option<Burst>,
    seen: Option<(usize, u64)>,
    song: Option<f64>,
}
impl Evolution {
    pub fn update(&mut self, run: Option<RunState>, inv: Option<Inventory>) {
        let (Some(run), Some(inv)) = (run, inv) else {
            *self = Self::default();
            return;
        };
        let delta = self.song.map_or(0.0, |s| (run.song - s).max(0.0));
        if self.song.is_some_and(|s| run.song < s - 1e-8) {
            self.burst = None;
            self.seen = None;
        }
        self.song = Some(run.song);
        if let Some(b) = self.burst.as_mut() {
            b.weapons.retain(|w| inv.weapons.rank(*w).is_some());
            if !matches!(
                run.frozen,
                Some(FreezeReason::Paused | FreezeReason::MusicStopped)
            ) {
                b.age += delta;
            }
        }
        if let Some(chest) = run.roulette {
            if let Some(at) = chest
                .landed
                .filter(|s| s.is_finite() && *s <= run.song + 1e-9)
            {
                let identity = (chest.id, at.to_bits());
                let ids: Vec<_> = rewards(chest)
                    .into_iter()
                    .filter(|w| inv.weapons.rank(*w).is_some())
                    .collect();
                if ids.is_empty() && self.seen == Some(identity) {
                    self.seen = None;
                }
                if !ids.is_empty() && self.seen != Some(identity) {
                    self.burst = Some(Burst {
                        chest: chest.id,
                        landed: at.to_bits(),
                        weapons: ids,
                        age: (run.song - at).max(0.0),
                    });
                    self.seen = Some(identity);
                } else if let Some(b) = self
                    .burst
                    .as_mut()
                    .filter(|b| (b.chest, b.landed) == identity)
                {
                    b.weapons = ids;
                }
            } else if self.seen.is_some_and(|(id, _)| id == chest.id) {
                // A correction retracts the grant, rather than replaying a cached celebration.
                self.burst = None;
                self.seen = None;
            }
        }
        if self
            .burst
            .as_ref()
            .is_some_and(|b| b.age >= LIFE || b.weapons.is_empty())
        {
            self.burst = None;
        }
    }
    #[allow(clippy::too_many_arguments)]
    pub fn draw(
        &self,
        view: &LiveView,
        art: &Atlases,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        width: f32,
        height: f32,
        world: &mut Vec<Sprite>,
        lights: &mut Vec<Light>,
        overlay: &mut Vec<Sprite>,
    ) {
        let (Some(burst), Some(hero)) = (&self.burst, view.hero) else {
            return;
        };
        let (x, y) = at(hero.x, hero.y);
        let progress = (burst.age / LIFE).clamp(0.0, 1.0) as f32;
        let fade = (1.0 - progress).powi(2);
        let radius = (45.0 + 200.0 * (1.0 - (1.0 - progress).powi(3))) * scale;
        for (i, id) in burst.weapons.iter().enumerate() {
            let tint = color(*id);
            let offset = i as f32 * 0.5;
            world.push(Sprite::arc(
                x,
                y,
                radius * (1.0 - offset * 0.06),
                Arc {
                    head: offset + progress * 0.8,
                    span: std::f32::consts::TAU,
                    width: 3.0 * scale,
                },
                [tint[0], tint[1], tint[2], 0.65 * fade],
            ));
            for ray in 0..6 {
                let angle = ray as f32 * std::f32::consts::TAU / 6.0 + offset;
                let distance = (30.0 + 110.0 * progress) * scale;
                world.push(
                    Sprite::solid(
                        x + angle.cos() * distance - 22.0 * scale,
                        y + angle.sin() * distance - 1.0 * scale,
                        44.0 * scale,
                        2.0 * scale,
                        [tint[0], tint[1], tint[2], 0.6 * fade],
                    )
                    .rotated(angle)
                    .additive(),
                );
            }
            lights.push(Light {
                x,
                y,
                radius: 170.0 * scale,
                color: tint,
                strength: 0.22 * fade,
            });
        }
        let tint = color(burst.weapons[0]);
        if let Some(glow) = art.proc.get("glow") {
            world.push(
                Sprite::centred(*glow, x, y, 110.0 * scale, 110.0 * scale)
                    .tinted([tint[0], tint[1], tint[2], 0.22 * fade])
                    .additive(),
            );
        }
        let unit = (width / 960.0).min(height / 540.0);
        let alpha = (burst.age / 0.08).min(1.0) as f32 * (1.0 - progress).min(0.3) / 0.3;
        art.fonts["pixel"].text(
            "EVOLVED",
            width / 2.0,
            height * 0.77,
            1.1 * unit,
            Align::Centre,
            [tint[0], tint[1], tint[2], alpha],
            overlay,
        );
        let label = if burst.weapons.len() == 1 {
            Item::Weapon(burst.weapons[0]).name()
        } else {
            "WEAPONS AWAKENED"
        };
        art.fonts["pixel"].text(
            label,
            width / 2.0,
            height * 0.82,
            1.8 * unit,
            Align::Centre,
            [1.0, 0.94, 0.82, alpha],
            overlay,
        );
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    fn state(at: f64) -> (RunState, Inventory) {
        let mut inv = Inventory::default();
        inv.weapons.upgrade(WeaponId::HolyWand, 1).unwrap();
        (
            RunState {
                song: at,
                roulette: Some(Roulette {
                    id: 3,
                    tier: 1,
                    minute: 20,
                    opened: 0.0,
                    tick: 8,
                    next_song: 2.5,
                    next_beat: None,
                    started: true,
                    landed: Some(2.0),
                    rewards: [
                        Some(Item::Weapon(WeaponId::HolyWand)),
                        None,
                        None,
                        None,
                        None,
                    ],
                }),
                ..RunState::default()
            },
            inv,
        )
    }
    #[test]
    fn chest_identity_never_fires_early_restarts_after_expiry_or_survives_retraction() {
        let mut e = Evolution::default();
        let (mut r, i) = state(1.9);
        e.update(Some(r), Some(i));
        assert!(e.burst.is_none());
        r.song = 2.0;
        e.update(Some(r), Some(i));
        assert_eq!(e.burst.as_ref().unwrap().age, 0.0);
        r.song = 2.2;
        e.update(Some(r), Some(i));
        assert!((e.burst.as_ref().unwrap().age - 0.2).abs() < 1e-8);
        r.song = 4.0;
        e.update(Some(r), Some(i));
        assert!(e.burst.is_none());
        e.update(Some(r), Some(i));
        assert!(e.burst.is_none());
        r.song = 2.0;
        e.update(Some(r), Some(i));
        assert!(e.burst.is_some());
        e.update(Some(r), Some(Inventory::default()));
        assert!(e.burst.is_none());
        r.roulette.as_mut().unwrap().landed = None;
        e.update(Some(r), Some(i));
        assert!(e.burst.is_none());
    }
    #[test]
    fn manual_and_music_pauses_hold_burst_but_chest_animation_advances() {
        let mut e = Evolution::default();
        let (mut r, i) = state(2.0);
        e.update(Some(r), Some(i));
        for reason in [FreezeReason::Paused, FreezeReason::MusicStopped] {
            r.song += 2.0;
            r.frozen = Some(reason);
            e.update(Some(r), Some(i));
            assert_eq!(e.burst.as_ref().unwrap().age, 0.0);
        }
        r.song += 0.25;
        r.frozen = Some(FreezeReason::Chest);
        e.update(Some(r), Some(i));
        assert_eq!(e.burst.as_ref().unwrap().age, 0.25);
        r.roulette = None;
        r.song += 0.25;
        r.frozen = None;
        e.update(Some(r), Some(i));
        assert_eq!(e.burst.as_ref().unwrap().age, 0.5);
    }
    #[test]
    fn cues_reconcile_at_landing_and_multi_reward_uses_one_ui_voice() {
        let (mut r, _) = state(1.8);
        let c = cues(Some(r));
        assert_eq!(c.len(), 1);
        assert_eq!(c[0].s, 2.0);
        assert_eq!(c[0].key, "evolve-holy-wand");
        r.roulette.as_mut().unwrap().rewards[1] = Some(Item::Weapon(WeaponId::SoulEater));
        let c = cues(Some(r));
        assert_eq!(c.len(), 1);
        assert_eq!(c[0].key, "evolve-multiple");
        r.roulette.as_mut().unwrap().landed = None;
        assert!(cues(Some(r)).is_empty());
    }
    #[test]
    fn corrected_rewards_refresh_identity_without_restarting_the_animation() {
        let mut presentation = Evolution::default();
        let (mut run, mut inv) = state(2.0);
        presentation.update(Some(run), Some(inv));
        inv.weapons
            .replace(WeaponId::HolyWand, WeaponId::SoulEater)
            .unwrap();
        run.song = 2.1;
        run.roulette.as_mut().unwrap().rewards[0] = Some(Item::Weapon(WeaponId::SoulEater));
        presentation.update(Some(run), Some(inv));
        let burst = presentation.burst.as_ref().unwrap();
        assert_eq!(burst.weapons, [WeaponId::SoulEater]);
        assert!((burst.age - 0.1).abs() < 1e-8);
        assert_eq!(cues(Some(run))[0].key, "evolve-soul-eater");
        run.song = 2.2;
        presentation.update(Some(run), Some(Inventory::default()));
        assert!(presentation.burst.is_none());
        run.song = 2.3;
        presentation.update(Some(run), Some(inv));
        assert!((presentation.burst.as_ref().unwrap().age - 0.3).abs() < 1e-8);
    }
}
