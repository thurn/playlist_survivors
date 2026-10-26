//! Observation-only zen players. They never read the stream or future simulation state.
use sim::core::rng::{Part, Rng};
use sim::movement::InputKey;
use sim::player::{KeyEvent, KeyEventKind, Observation, Player};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SurvivorBotKind {
    Positioning,
    Afk,
    RandomPick,
}
impl SurvivorBotKind {
    pub fn from_name(name: &str) -> Option<Self> {
        match name {
            "positioning" => Some(Self::Positioning),
            "afk" => Some(Self::Afk),
            "random-pick" => Some(Self::RandomPick),
            _ => None,
        }
    }
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Positioning => "positioning",
            Self::Afk => "afk",
            Self::RandomPick => "random-pick",
        }
    }
}

#[derive(Clone, Debug)]
pub struct SurvivorBot {
    kind: SurvivorBotKind,
    seed: String,
    held: Vec<InputKey>,
    release: Vec<InputKey>,
    picked: Option<(usize, u64)>,
    ready: bool,
    heading: (f64, f64),
}
impl SurvivorBot {
    pub fn new(kind: SurvivorBotKind, seed: u64) -> Self {
        let seed = seed.to_string();
        let mut rng = Rng::new(&[Part::Str("survivor-heading"), Part::Str(&seed)]);
        let angle = rng.next() * std::f64::consts::TAU;
        Self {
            kind,
            seed,
            held: Vec::new(),
            release: Vec::new(),
            picked: None,
            ready: true,
            heading: (angle.cos(), angle.sin()),
        }
    }
    fn choose_card(&self, level: usize) -> usize {
        if self.kind == SurvivorBotKind::RandomPick {
            // Stateless by level: retracting/reopening an offer cannot consume another draw.
            Rng::new(&[
                Part::Str("survivor-card"),
                Part::Str(&self.seed),
                Part::Num(level as f64),
            ])
            .int(0.0, 2.0) as usize
        } else {
            0
        }
    }
    fn direction(&self, obs: &Observation) -> (f64, f64) {
        const REACH: f64 = 240.0;
        const SAFETY: f64 = 90.0;
        let (mut x, mut y, mut weight, mut danger) = (0.0, 0.0, 0.0, false);
        for enemy in &obs.enemies {
            let (dx, dy) = (enemy.x - obs.hero.x, enemy.y - obs.hero.y);
            let d2 = dx * dx + dy * dy;
            if !d2.is_finite() || d2 > REACH * REACH {
                continue;
            }
            let w = 1.0 / (d2 + 24.0 * 24.0);
            x -= dx * w;
            y -= dy * w;
            weight += w;
            danger |= d2 < SAFETY * SAFETY;
        }
        if weight > 0.0 {
            x /= weight;
            y /= weight;
        }
        let escape = unit(x, y).unwrap_or(self.heading);
        if !danger
            && let Some(gem) = obs.gems.as_ref().and_then(|gems| {
                gems.iter().min_by(|a, b| {
                    (a.x - obs.hero.x)
                        .hypot(a.y - obs.hero.y)
                        .total_cmp(&(b.x - obs.hero.x).hypot(b.y - obs.hero.y))
                })
            })
            && let Some(pull) = unit(gem.x - obs.hero.x, gem.y - obs.hero.y)
        {
            // Prefer XP only while clear of contact; the wider crowd still bends that path.
            let bend = if weight > 0.0 { 0.35 } else { 0.0 };
            return unit(pull.0 + bend * escape.0, pull.1 + bend * escape.1).unwrap_or(escape);
        }
        escape
    }
    fn move_keys(&mut self, keys: Vec<InputKey>, s: f64, out: &mut Vec<KeyEvent>) {
        for &key in &self.held {
            if !keys.contains(&key) {
                out.push(event(s, key, false));
            }
        }
        for &key in &keys {
            if !self.held.contains(&key) {
                out.push(event(s, key, true));
            }
        }
        self.held = keys;
    }
}
fn unit(x: f64, y: f64) -> Option<(f64, f64)> {
    let d = x.hypot(y);
    (d.is_finite() && d > 1e-9).then(|| (x / d, y / d))
}
fn arrows((x, y): (f64, f64)) -> Vec<InputKey> {
    let mut keys = Vec::new();
    // Eight directions; never opposite keys, and never Space/stomp.
    if x.abs() > y.abs() * 0.414_213_562_373_095 {
        keys.push(if x < 0.0 {
            InputKey::Left
        } else {
            InputKey::Right
        });
    }
    if y.abs() > x.abs() * 0.414_213_562_373_095 {
        keys.push(if y < 0.0 {
            InputKey::Up
        } else {
            InputKey::Down
        });
    }
    keys
}
fn event(s: f64, key: InputKey, pressed: bool) -> KeyEvent {
    KeyEvent {
        s,
        key,
        kind: if pressed {
            KeyEventKind::Press
        } else {
            KeyEventKind::Release
        },
        id: None,
    }
}
impl Player for SurvivorBot {
    fn next_decision(&mut self, after: f64) -> f64 {
        (after.max(0.0) * 60.0).floor() / 60.0 + 1.0 / 60.0
    }
    fn decide(&mut self, obs: &Observation) -> Vec<KeyEvent> {
        let mut out: Vec<_> = self
            .release
            .drain(..)
            .map(|key| event(obs.s, key, false))
            .collect();
        if let Some(run) = obs.run {
            if run.frozen.is_some() {
                self.move_keys(Vec::new(), obs.s, &mut out);
                self.ready = true;
                if let Some(offer) = run.offer {
                    let token = (offer.level, offer.reveals[2].to_bits());
                    // The screen owns these fixed reveal times. Its last interval supplies one
                    // heard beat of decision time without consulting the stream's future.
                    let beat = (offer.reveals[2] - offer.reveals[1]).max(0.001);
                    if run.song >= offer.reveals[2] + beat && self.picked != Some(token) {
                        let key = [InputKey::Left, InputKey::Up, InputKey::Right]
                            [self.choose_card(offer.level)];
                        out.push(event(obs.s, key, true));
                        out.push(event(obs.s, InputKey::Confirm, true));
                        self.release.extend([key, InputKey::Confirm]);
                        self.picked = Some(token);
                    }
                }
                return out;
            }
            self.picked = None;
        }
        if self.kind == SurvivorBotKind::Afk {
            return out;
        }
        if obs.hero.stepping {
            self.ready = true;
        } else if self.ready {
            self.heading = self.direction(obs);
            self.move_keys(arrows(self.heading), obs.s, &mut out);
            self.ready = false;
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use sim::player::{EnemyView, GemView, HeroView};
    use sim::survivors::{Card, FreezeReason, Item, Offer, RunState, WeaponId};
    fn observation() -> Observation {
        Observation {
            run: Some(RunState::default()),
            s: 0.0,
            w: 0.0,
            hero: HeroView {
                x: 0.0,
                y: 0.0,
                stepping: false,
                stride: Some(35.0),
                tier: None,
                pickup: Some(46.0),
                health: Some(1.0),
            },
            enemies: Vec::new(),
            threats: Vec::new(),
            boss: None,
            formation: None,
            ring: None,
            runners: None,
            gems: None,
            vacuum: None,
            orbs: None,
            level_up_at: None,
            chickens: None,
            pace_point: sim::world::Frame {
                x: 0.0,
                y: 0.0,
                tx: 1.0,
                ty: 0.0,
            },
        }
    }
    fn screen(obs: &mut Observation, level: usize, song: f64) {
        let card = Card {
            item: Item::Weapon(WeaponId::Sword),
            before: 1,
            after: 2,
        };
        obs.s = song;
        obs.run = Some(RunState {
            song,
            frozen: Some(FreezeReason::LevelUp),
            offer: Some(Offer {
                level,
                cards: [card; 3],
                reveals: [1.0, 1.5, 2.0],
            }),
            ..RunState::default()
        });
    }
    #[test]
    fn positioning_flees_the_close_crowd_and_seeks_xp_only_when_safe() {
        let mut p = SurvivorBot::new(SurvivorBotKind::Positioning, 1);
        let mut o = observation();
        o.enemies.push(EnemyView {
            x: 20.0,
            y: 0.0,
            kind: "vs-zombie".into(),
            health: 1.0,
        });
        o.gems = Some(vec![GemView {
            x: 50.0,
            y: 0.0,
            value: 1.0,
        }]);
        let keys = p.decide(&o);
        assert_eq!(keys, vec![event(0.0, InputKey::Left, true)]);
        // Steering remains held until the next stride, rather than mashing/reversing mid-step.
        o.enemies[0].x = -20.0;
        assert!(p.decide(&o).is_empty());
        o.hero.stepping = true;
        assert!(p.decide(&o).is_empty());
        o.hero.stepping = false;
        o.enemies.clear();
        o.gems.as_mut().unwrap()[0].x = 0.0;
        o.gems.as_mut().unwrap()[0].y = -100.0;
        let keys = p.decide(&o);
        assert_eq!(
            keys,
            vec![
                event(0.0, InputKey::Left, false),
                event(0.0, InputKey::Up, true)
            ]
        );
        assert!(!keys.iter().any(|k| k.key == InputKey::Space));
    }
    #[test]
    fn afk_never_moves_and_picks_once_one_beat_after_the_cards_land() {
        let mut p = SurvivorBot::new(SurvivorBotKind::Afk, 1);
        let mut o = observation();
        for _ in 0..100 {
            assert!(p.decide(&o).is_empty());
        }
        screen(&mut o, 2, 2.499);
        assert!(p.decide(&o).is_empty());
        screen(&mut o, 2, 2.5);
        assert_eq!(
            p.decide(&o),
            vec![
                event(2.5, InputKey::Left, true),
                event(2.5, InputKey::Confirm, true)
            ]
        );
        let releases = p.decide(&o);
        assert!(releases.iter().all(|k| k.kind == KeyEventKind::Release));
        assert_eq!(releases.len(), 2);
        assert!(p.decide(&o).is_empty());
        screen(&mut o, 3, 2.5);
        assert!(p.decide(&o).iter().any(|k| k.key == InputKey::Confirm));
        o.run = Some(RunState::default());
        let after = p.decide(&o);
        assert!(after.iter().all(|k| k.kind == KeyEventKind::Release));
        assert!(p.decide(&o).is_empty());
    }
    #[test]
    fn freeze_releases_movement_and_resume_requires_a_fresh_press() {
        let mut p = SurvivorBot::new(SurvivorBotKind::Positioning, 1);
        p.heading = (1.0, 0.0);
        let mut o = observation();
        assert_eq!(p.decide(&o), vec![event(0.0, InputKey::Right, true)]);
        screen(&mut o, 2, 1.0);
        assert_eq!(p.decide(&o), vec![event(1.0, InputKey::Right, false)]);
        assert!(p.decide(&o).is_empty());
        o.run = Some(RunState::default());
        assert_eq!(p.decide(&o), vec![event(1.0, InputKey::Right, true)]);
    }
    #[test]
    fn seeded_random_picks_survive_reopening_and_preserve_all_seed_bits() {
        let a = SurvivorBot::new(SurvivorBotKind::RandomPick, 1);
        let b = SurvivorBot::new(SurvivorBotKind::RandomPick, 1);
        let c = SurvivorBot::new(SurvivorBotKind::RandomPick, (1_u64 << 63) + 1);
        let picks = |p: &SurvivorBot| {
            (2..70)
                .map(|level| p.choose_card(level))
                .collect::<Vec<_>>()
        };
        assert_eq!(picks(&a), picks(&b));
        assert_eq!(picks(&a), picks(&a));
        assert_ne!(picks(&a), picks(&c));
        assert!(picks(&a).iter().all(|&i| i < 3));
    }
}
