//! Bounded presentation labels, summed once per enemy and musical beat.
use super::gfx::Sprite;
use render::{atlas::Atlases, font::Align};
const CAP: usize = 128;
const HOLD: f64 = 0.75;
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Beat {
    Known(u64),
    Free(i64),
}
pub struct Clock<'a> {
    pub beats: &'a [f64],
    pub start: f64,
    pub free: f64,
}
impl Clock<'_> {
    fn beat(&self, song: f64) -> Beat {
        let at = song + self.start;
        let n = self.beats.partition_point(|b| *b <= at + 1e-9);
        let anchor = n
            .checked_sub(1)
            .and_then(|i| self.beats.get(i))
            .copied()
            .unwrap_or(self.start);
        if let Some(&last) = n.checked_sub(1).and_then(|i| self.beats.get(i)) {
            let period = if n >= 2 {
                last - self.beats[n - 2]
            } else {
                self.free
            };
            if at - last <= 1.5 * period.max(0.1) {
                return Beat::Known(last.to_bits());
            }
        }
        Beat::Free(((at - anchor) / self.free.max(0.1)).floor() as i64)
    }
}
#[derive(Clone, Debug)]
struct Number {
    foe: usize,
    beat: Beat,
    value: f64,
    critical: bool,
    x: f64,
    y: f64,
    born: f64,
    last: f64,
}
#[derive(Default)]
pub struct Numbers {
    items: std::collections::VecDeque<Number>,
    hero: Option<(f64, f64)>,
}
impl Numbers {
    pub fn focus(&mut self, hero: Option<(f64, f64)>) {
        self.hero = hero;
    }
    /// Grid-aligned reactions present each applied Fate once, including across freezes.
    pub fn moments(&mut self, moments: &[eval::live::Moment], now: f64, clock: &Clock) {
        for moment in moments {
            if let eval::live::Moment::EnemyImpact { hit } = moment
                && let eval::live::Moment::Hit {
                    foe,
                    song,
                    damage,
                    critical,
                    x,
                    y,
                    ..
                } = hit.as_ref()
            {
                self.add(*foe, *song, *damage, *critical, *x, *y, now, clock);
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    pub fn add(
        &mut self,
        foe: usize,
        song: f64,
        value: f64,
        critical: bool,
        x: f64,
        y: f64,
        now: f64,
        clock: &Clock,
    ) {
        if !value.is_finite() || value <= 0.0 || !x.is_finite() || !y.is_finite() {
            return;
        }
        self.items.retain(|n| now - n.last < HOLD);
        let beat = clock.beat(song);
        if let Some(n) = self
            .items
            .iter_mut()
            .find(|n| n.foe == foe && n.beat == beat)
        {
            n.value += value;
            n.critical |= critical;
            n.last = now;
            n.x = x;
            n.y = y;
        } else {
            if self.items.len() >= CAP {
                self.items.pop_front();
            }
            self.items.push_back(Number {
                foe,
                beat,
                value,
                critical,
                x,
                y,
                born: now,
                last: now,
            });
        }
    }
    pub fn draw(
        &mut self,
        art: &Atlases,
        now: f64,
        at: &dyn Fn(f64, f64) -> (f32, f32),
        scale: f32,
        out: &mut Vec<Sprite>,
    ) {
        self.items.retain(|n| now - n.last < HOLD);
        let face = &art.fonts["pixel"];
        // Values remain summed per enemy/beat; crowded labels only share screen space.
        let mut cells = std::collections::BTreeSet::new();
        let (mut near_labels, mut far_labels) = (0, 0);
        let dense = self.items.len() > 32;
        for offset in 0..self.items.len() {
            let n = if self.items.len() > 32 {
                &self.items[self.items.len() - 1 - offset]
            } else {
                &self.items[offset]
            };
            let near = self
                .hero
                .is_some_and(|h| (n.x - h.0).hypot(n.y - h.1) <= 190.0);
            if dense && ((near && near_labels >= 12) || (!near && far_labels >= 20)) {
                continue;
            }
            let age = (now - n.last).max(0.0);
            let fade = (1.0 - age / HOLD).min(1.0) as f32;
            let pop = (1.0 + 0.25 * (-age * 18.0).exp()) as f32;
            let (x, y) = at(n.x, n.y - 35.0 - (now - n.born).min(1.2) * 18.0);
            if dense
                && !cells.insert((
                    (x / (64.0 * scale)).floor() as i32,
                    (y / (32.0 * scale)).floor() as i32,
                ))
            {
                continue;
            }
            if near {
                near_labels += 1;
            } else {
                far_labels += 1;
            }
            let fade = fade
                * if dense && !near && !n.critical {
                    0.55
                } else {
                    1.0
                };
            let color = if n.critical {
                [1.0, 0.81, 0.22, fade]
            } else {
                [1.0, 1.0, 1.0, fade]
            };
            face.text(
                &label(n.value),
                x,
                y,
                scale * pop * if n.critical { 2.0 } else { 1.5 },
                Align::Centre,
                color,
                out,
            );
        }
    }
}
fn label(value: f64) -> String {
    let value = (value * 10.0).round();
    if value >= 1_000_000.0 {
        format!("{:.1}M", value / 1_000_000.0)
    } else if value >= 10_000.0 {
        format!("{:.1}K", value / 1000.0)
    } else {
        format!("{value:.0}")
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn damage_is_scaled_and_summed_per_enemy_per_beat() {
        let clock = Clock {
            beats: &[0.0, 0.5, 1.0],
            start: 0.0,
            free: 0.5,
        };
        let mut n = Numbers::default();
        n.add(7, 0.1, 1.0, false, 0.0, 0.0, 0.1, &clock);
        n.add(7, 0.3, 2.0, true, 0.0, 0.0, 0.3, &clock);
        assert_eq!(n.items.len(), 1);
        assert_eq!(label(n.items[0].value), "30");
        assert!(n.items[0].critical);
        n.add(8, 0.3, 1.0, false, 0.0, 0.0, 0.3, &clock);
        n.add(7, 0.6, 1.0, false, 0.0, 0.0, 0.6, &clock);
        assert_eq!(n.items.len(), 3);
        assert_ne!(clock.beat(0.6), clock.beat(0.1));
        assert_ne!(clock.beat(3.0), clock.beat(3.6));
    }
    #[test]
    fn a_free_pulse_uses_run_or_last_beat_origin() {
        let clock = Clock {
            beats: &[],
            start: 2.8,
            free: 0.5,
        };
        assert_eq!(clock.beat(0.1), clock.beat(0.3));
        assert_ne!(clock.beat(0.1), clock.beat(0.6));
    }
    #[test]
    fn labels_wait_for_the_grid_reaction_across_a_freeze() {
        use eval::live::Moment;
        let clock = Clock {
            beats: &[0.0, 0.5],
            start: 0.0,
            free: 0.5,
        };
        let hit = Moment::Hit {
            x: 0.0,
            y: 0.0,
            lethal: false,
            damage: 3.0,
            song: 0.1,
            critical: false,
            foe: 7,
            source: "weapon",
            weight: "light",
            what: "sword".into(),
            dir: 0.0,
            intensity: 1.0,
        };
        let mut n = Numbers::default();
        n.moments(std::slice::from_ref(&hit), 0.1, &clock);
        assert!(n.items.is_empty(), "logical damage must not present early");
        n.moments(&[Moment::EnemyImpact { hit: Box::new(hit) }], 5.0, &clock);
        assert_eq!(n.items.len(), 1);
        assert_eq!(label(n.items[0].value), "30");
        assert_eq!(
            n.items[0].born, 5.0,
            "the label survives a long offer freeze"
        );
    }
    #[test]
    fn labels_are_bounded_at_density() {
        let clock = Clock {
            beats: &[],
            start: 0.0,
            free: 0.5,
        };
        let mut n = Numbers::default();
        for foe in 0..400 {
            n.add(foe, 0.1, 1.0, false, 0.0, 0.0, 0.1, &clock);
        }
        assert_eq!(n.items.len(), CAP);
        n.add(401, 2.0, 1.0, false, 0.0, 0.0, 2.0, &clock);
        assert_eq!(n.items.len(), 1);
    }
}

#[cfg(test)]
mod draw_tests {
    use super::*;
    #[test]
    fn dense_coincident_labels_share_space_without_changing_their_values() {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets");
        let art = Atlases::load(&path).unwrap();
        let clock = Clock {
            beats: &[],
            start: 0.0,
            free: 0.5,
        };
        let mut dense = Numbers::default();
        let mut single = Numbers::default();
        dense.focus(Some((0.0, 0.0)));
        single.focus(Some((0.0, 0.0)));
        for foe in 0..64 {
            dense.add(foe, 0.0, 1.0, false, 0.0, 0.0, 0.0, &clock);
        }
        single.add(0, 0.0, 1.0, false, 0.0, 0.0, 0.0, &clock);
        let (mut a, mut b) = (Vec::new(), Vec::new());
        dense.draw(&art, 0.0, &|x, y| (x as f32, y as f32), 1.0, &mut a);
        single.draw(&art, 0.0, &|x, y| (x as f32, y as f32), 1.0, &mut b);
        assert_eq!(a, b, "dense labels must not overdraw the same cell64 times");
        assert_eq!(dense.items.len(), 64);
        assert!(dense.items.iter().all(|n| n.value == 1.0));
    }
    #[test]
    fn critical_labels_are_larger_and_gold() {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets");
        let art = Atlases::load(&path).unwrap();
        let clock = Clock {
            beats: &[],
            start: 0.0,
            free: 0.5,
        };
        let mut n = Numbers::default();
        n.add(1, 0.0, 1.0, false, 0.0, 0.0, 0.0, &clock);
        n.add(2, 0.0, 1.0, true, 0.0, 0.0, 0.0, &clock);
        let mut sprites = Vec::new();
        n.draw(&art, 0.0, &|x, y| (x as f32, y as f32), 1.0, &mut sprites);
        let white = sprites.iter().find(|s| s.tint == [1.0; 4]).unwrap();
        let gold = sprites
            .iter()
            .find(|s| s.tint == [1.0, 0.81, 0.22, 1.0])
            .unwrap();
        assert!(gold.h > white.h);
        assert!(gold.w > white.w);
        assert!(
            sprites
                .iter()
                .position(|s| s.tint == [1.0, 0.81, 0.22, 1.0])
                .unwrap()
                > sprites.iter().position(|s| s.tint == [1.0; 4]).unwrap(),
            "sparse labels retain arrival order, newest on top"
        );
    }
}
