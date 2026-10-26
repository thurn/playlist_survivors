//! Selected numerical rules from docs/survivors; player HP is unscaled.

/// Unity world units, including normal Mad Forest's shared speed modifier.
pub const PX_PER_VS_UNIT: f64 = 140.0 / (0.825 * 1.1);
pub const HERO_SPEED: f64 = 0.825 * 1.1 * PX_PER_VS_UNIT;
pub const PICKUP_RADIUS: f64 = 0.3 * PX_PER_VS_UNIT;
pub const DAMAGE_LOCKOUT_S: f64 = 0.120;
pub const GEM_CAP: usize = 400;

pub fn enemy_speed(data_speed: f64) -> f64 {
    0.231 * 1.1 * (data_speed / 100.0) * PX_PER_VS_UNIT
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Stats {
    pub might: f64,
    pub cooldown: f64,
    pub amount: f64,
    pub area: f64,
    pub projectile_speed: f64,
    pub duration: f64,
    pub max_hp: f64,
    pub recovery: f64,
    pub armor: f64,
    pub move_speed: f64,
    pub magnet: f64,
    pub luck: f64,
    pub greed: f64,
    pub curse: f64,
    pub growth: f64,
}

impl Default for Stats {
    fn default() -> Self {
        Self {
            might: 1.0,
            cooldown: 1.0,
            amount: 0.0,
            area: 1.0,
            projectile_speed: 1.0,
            duration: 1.0,
            max_hp: 120.0,
            recovery: 0.0,
            armor: 1.0,
            move_speed: 1.0,
            magnet: 1.0,
            luck: 1.0,
            greed: 1.0,
            curse: 1.0,
            growth: 1.0,
        }
    }
}

/// Additive deltas, except max_hp's fractional multiplier (0.2 means +20%).
#[derive(Clone, Copy, Debug, Default)]
pub struct StatDelta {
    pub might: f64,
    pub cooldown: f64,
    pub amount: f64,
    pub area: f64,
    pub projectile_speed: f64,
    pub duration: f64,
    pub max_hp: f64,
    pub recovery: f64,
    pub armor: f64,
    pub move_speed: f64,
    pub magnet: f64,
    pub luck: f64,
    pub greed: f64,
    pub curse: f64,
    pub growth: f64,
}

impl Stats {
    /// Keep raw sums so a later negative modifier is applied before final-stat clamps.
    pub fn apply(&mut self, d: StatDelta) {
        self.might += d.might;
        self.cooldown += d.cooldown;
        self.amount += d.amount;
        self.area += d.area;
        self.projectile_speed += d.projectile_speed;
        self.duration += d.duration;
        self.max_hp *= 1.0 + d.max_hp;
        self.recovery += d.recovery;
        self.armor += d.armor;
        self.move_speed += d.move_speed;
        self.magnet += d.magnet;
        self.luck += d.luck;
        self.greed += d.greed;
        self.curse += d.curse;
        self.growth += d.growth;
    }

    #[must_use]
    pub fn finalized(self) -> Self {
        Self {
            might: self.might.min(10.0),
            cooldown: self.cooldown.max(0.1),
            amount: self.amount.min(10.0),
            area: self.area.min(10.0),
            projectile_speed: self.projectile_speed.min(5.0),
            duration: self.duration.min(5.0),
            armor: self.armor.min(50.0),
            ..self
        }
    }

    pub fn contact_damage(self, power: f64, multiplier: f64) -> f64 {
        (power * multiplier - self.finalized().armor).max(1.0)
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum GemColor {
    Blue,
    Green,
    Red,
}

pub fn gem_color(value: f64) -> GemColor {
    if value >= 10.0 {
        GemColor::Red
    } else if value >= 3.0 {
        GemColor::Green
    } else {
        GemColor::Blue
    }
}

pub fn gem_value(enemy_xp: f64, random: f64) -> f64 {
    ((random + 0.5) * enemy_xp).floor()
}

/// Data scalars, independent of sprite art. SF6 supplies these for every VS role.
#[derive(Clone, Copy, Debug, PartialEq, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EnemyStats {
    pub speed: f64,
    pub power: f64,
    pub xp: f64,
    pub knockback: f64,
    pub max_knockback: f64,
    pub res_debuffs: f64,
    pub level_scaled: bool,
    pub brute: bool,
}

impl EnemyStats {
    /// Fallback for authored levels using legacy art IDs; the shipped Survivors level has explicit stats.
    pub fn for_art(id: &str) -> Self {
        let (speed, power, xp, knockback) = match id {
            "bat" => (140.0, 5.0, 1.0, 1.0),
            "eyeball" => (200.0, 5.0, 1.5, 0.0),
            "bee" => (700.0, 5.0, 1.0, 1.0),
            "skeleton" | "skeleton-ember" | "skeleton-archer" => (100.0, 10.0, 2.0, 1.0),
            "orc" | "orc-elite" => (100.0, 10.0, 2.5, 0.3),
            "orc-ember" => (130.0, 14.0, 2.0, 0.8),
            "minotaur" => (160.0, 20.0, 50.0, 0.0),
            _ => (100.0, 10.0, 1.0, 0.8),
        };
        Self {
            speed,
            power,
            xp,
            knockback,
            max_knockback: if id == "minotaur" { 1.0 } else { 3.0 },
            res_debuffs: 0.0,
            level_scaled: false,
            brute: false,
        }
    }
}
