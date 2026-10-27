//! Bounded slots and run-clock deadlines. Snapping never feeds back into the cooldown.
use super::{PassiveId, Stats, WeaponId, weapon_catalog};
use stream::Beat;

pub const ITEM_SLOTS: usize = 6;
const EPS: f64 = 1e-9;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Owned<T> {
    pub id: T,
    pub rank: u8,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Slots<T: Copy> {
    items: [Option<Owned<T>>; ITEM_SLOTS],
}
impl<T: Copy> Default for Slots<T> {
    fn default() -> Self {
        Self {
            items: [None; ITEM_SLOTS],
        }
    }
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum SlotError {
    Full,
    Maxed,
    Unavailable,
}
impl<T: Copy + Eq> Slots<T> {
    pub fn iter(&self) -> impl Iterator<Item = Owned<T>> + '_ {
        self.items.iter().copied().flatten()
    }
    pub fn rank(&self, id: T) -> Option<u8> {
        self.iter().find(|s| s.id == id).map(|s| s.rank)
    }
    pub fn is_full(&self) -> bool {
        self.items.iter().all(Option::is_some)
    }
    pub fn upgrade(&mut self, id: T, max: u8) -> Result<u8, SlotError> {
        if let Some(item) = self.items.iter_mut().flatten().find(|s| s.id == id) {
            if item.rank >= max {
                return Err(SlotError::Maxed);
            }
            item.rank += 1;
            return Ok(item.rank);
        }
        let slot = self
            .items
            .iter_mut()
            .find(|s| s.is_none())
            .ok_or(SlotError::Full)?;
        *slot = Some(Owned { id, rank: 1 });
        Ok(1)
    }
    pub fn replace(&mut self, from: T, to: T) -> Result<(), SlotError> {
        if self.rank(to).is_some() {
            return Err(SlotError::Unavailable);
        }
        let slot = self
            .items
            .iter_mut()
            .flatten()
            .find(|s| s.id == from)
            .ok_or(SlotError::Unavailable)?;
        *slot = Owned { id: to, rank: 1 };
        Ok(())
    }
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct Inventory {
    pub weapons: Slots<WeaponId>,
    pub passives: Slots<PassiveId>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct GridPoint {
    pub song: f64,
    pub beat: Option<f64>,
}

/// Both bounds of a fractional step must already be committed at `now`.
fn grid_at_or_after(beats: &[Beat], now: f64, due: f64, step: f64) -> Option<GridPoint> {
    let n = beats.partition_point(|b| b.known_at <= now + EPS);
    let beats = &beats[..n];
    let start = beats
        .partition_point(|b| b.t < due.max(now) - EPS)
        .saturating_sub(1);
    for (i, b) in beats.iter().enumerate().skip(start) {
        let mut bi = (i as f64 / step - EPS).ceil() * step;
        while bi < i as f64 + 1.0 - EPS {
            let song = if bi.fract().abs() < EPS {
                b.t
            } else {
                let next = beats.get(i + 1)?;
                b.t + (next.t - b.t) * bi.fract()
            };
            if song >= due.max(now) - EPS {
                return Some(GridPoint {
                    song,
                    beat: Some(bi),
                });
            }
            bi += step;
        }
    }
    None
}

fn grid_point(beats: &[Beat], now: f64, bi: f64) -> Option<GridPoint> {
    let i = bi.floor() as usize;
    let b = beats.get(i).filter(|b| b.known_at <= now + EPS)?;
    let song = if bi.fract().abs() < EPS {
        b.t
    } else {
        let next = beats.get(i + 1).filter(|b| b.known_at <= now + EPS)?;
        b.t + (next.t - b.t) * bi.fract()
    };
    Some(GridPoint {
        song,
        beat: Some(bi),
    })
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Cooldown {
    pub next_ready: f64,
}
impl Cooldown {
    pub fn new(run: f64) -> Self {
        Self { next_ready: run }
    }
    pub fn take_ready(&mut self, now: f64, interval: f64) -> Option<f64> {
        if self.next_ready > now + EPS {
            return None;
        }
        assert!(
            interval.is_finite() && interval > 0.0,
            "a positive cooldown"
        );
        let ready = self.next_ready;
        self.next_ready += interval;
        Some(ready)
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Shot {
    pub id: WeaponId,
    pub rank: u8,
    pub ready_run: f64,
    pub song: f64,
    pub x: f64,
    pub y: f64,
    pub damage: f64,
    pub amount: usize,
    pub radius: f64,
    pub area: f64,
    pub speed: f64,
    pub duration_s: f64,
    pub pierce: f64,
    pub hit_delay_s: f64,
    pub knockback: f64,
    pub crit_chance: f64,
    pub crit_multiplier: f64,
    pub period: f64,
    pub accent: bool,
}

#[derive(Clone, Copy, Debug, PartialEq)]
struct Pending {
    id: WeaponId,
    ready_run: f64,
    period: f64,
    fire: Option<GridPoint>,
    planned: bool,
    serial: u64,
}
#[derive(Clone, Debug, PartialEq)]
pub struct Arsenal {
    inventory: Inventory,
    clocks: [Option<Cooldown>; ITEM_SLOTS],
    reserved: [Option<GridPoint>; ITEM_SLOTS],
    pending: Vec<Pending>,
    serial: u64,
    song_offset: f64,
}
impl Default for Arsenal {
    fn default() -> Self {
        let mut out = Self {
            inventory: Inventory::default(),
            clocks: [None; ITEM_SLOTS],
            reserved: [None; ITEM_SLOTS],
            pending: Vec::new(),
            serial: 0,
            song_offset: 0.0,
        };
        out.inventory
            .weapons
            .upgrade(WeaponId::Sword, 8)
            .expect("empty sword slot");
        out
    }
}
impl Arsenal {
    /// The clock's accumulated frozen song time, an anchor independent of frame cuts.
    pub fn resume(&mut self, song_offset: f64) {
        self.song_offset = song_offset;
    }
    #[cfg(test)]
    pub(crate) fn measurement_weapon(id: WeaponId, rank: u8) -> Self {
        let mut out = Self::default();
        out.inventory.weapons = Slots::default();
        for _ in 0..rank {
            out.inventory.weapons.upgrade(id, 8).unwrap();
        }
        out.clocks[0] = Some(Cooldown::new(0.0));
        out
    }
    pub fn inventory(&self) -> Inventory {
        self.inventory
    }
    pub fn learn(&mut self, id: WeaponId, run: f64) -> Result<u8, SlotError> {
        let def = weapon_catalog().get(id);
        if !def.implemented
            || def.evolution_of.is_some()
            || self
                .inventory
                .weapons
                .iter()
                .any(|s| weapon_catalog().get(s.id).evolution_of == Some(id))
        {
            return Err(SlotError::Unavailable);
        }
        let was = self.inventory.weapons.rank(id);
        let rank = self.inventory.weapons.upgrade(id, def.max_rank())?;
        if was.is_none() && !id.is_sword() {
            let slot = self.slot(id);
            self.clocks[slot] = Some(Cooldown::new(run));
        }
        Ok(rank)
    }
    pub fn evolve(&mut self, id: WeaponId, run: f64) -> Result<(), SlotError> {
        let d = weapon_catalog().get(id);
        let parent = d.evolution_of.ok_or(SlotError::Unavailable)?;
        if !d.implemented
            || self.inventory.weapons.rank(parent) != Some(weapon_catalog().get(parent).max_rank())
            || d.requires
                .is_none_or(|p| self.inventory.passives.rank(p).is_none())
        {
            return Err(SlotError::Unavailable);
        }
        let slot = self.slot(parent);
        self.inventory.weapons.replace(parent, id)?;
        self.pending.retain(|p| p.id != parent);
        self.reserved[slot] = None;
        self.clocks[slot] = (!id.is_sword()).then(|| Cooldown::new(run));
        Ok(())
    }
    pub fn learn_passive(&mut self, id: PassiveId) -> Result<u8, SlotError> {
        self.inventory.passives.upgrade(id, id.max_rank())
    }
    fn slot(&self, id: WeaponId) -> usize {
        self.inventory
            .weapons
            .items
            .iter()
            .position(|s| s.is_some_and(|s| s.id == id))
            .expect("owned weapon")
    }
    fn push(&mut self, id: WeaponId, ready_run: f64, period: f64, fire: Option<GridPoint>) {
        self.pending.push(Pending {
            id,
            ready_run,
            period,
            fire,
            planned: false,
            serial: self.serial,
        });
        self.serial += 1;
    }
    /// A committed beat owns its subdivisions; a free pulse uses its fixed known period.
    pub fn sword_pulse(
        &mut self,
        run: f64,
        song: f64,
        beat: Option<f64>,
        period: f64,
        stats: Stats,
    ) {
        let Some(owned) = self.inventory.weapons.iter().find(|s| s.id.is_sword()) else {
            return;
        };
        let n = weapon_catalog()
            .get(owned.id)
            .level(owned.rank)
            .amount(stats);
        for i in 0..n {
            let fraction = i as f64 / n as f64;
            self.push(
                owned.id,
                run + period * fraction,
                period,
                Some(GridPoint {
                    song: song + period * fraction,
                    beat: beat.map(|b| b + fraction),
                }),
            );
        }
    }
    pub fn hold(&mut self) {
        // The sword is beat cadence (like Auto), regenerated at the resume beat.
        // Interval weapons retain every ready deadline, independent of song time.
        self.pending.retain(|p| !p.id.is_sword());
        for pending in &mut self.pending {
            pending.fire = None;
            pending.planned = false;
        }
        self.reserved = [None; ITEM_SLOTS];
    }
    pub fn prepare(&mut self, run: f64, song: f64, beats: &[Beat], stats: Stats) {
        let owned: Vec<_> = self.inventory.weapons.iter().collect();
        for weapon in owned {
            if weapon.id.is_sword() {
                continue;
            }
            let slot = self.slot(weapon.id);
            let interval = weapon_catalog()
                .get(weapon.id)
                .level(weapon.rank)
                .interval(stats);
            let clock = self.clocks[slot].get_or_insert_with(|| Cooldown::new(run));
            let mut ready = Vec::new();
            while let Some(at) = clock.take_ready(run, interval) {
                ready.push(at);
            }
            for at in ready {
                self.push(weapon.id, at, 0.0, None);
            }
        }
        self.pending.sort_by(|a, b| {
            a.ready_run
                .total_cmp(&b.ready_run)
                .then(a.serial.cmp(&b.serial))
        });
        for i in 0..self.pending.len() {
            let p = self.pending[i];
            if p.planned {
                continue;
            }
            let slot = self.slot(p.id);
            let due = (p.ready_run + self.song_offset).max(song);
            let step = if p.id.is_sword() {
                0.25
            } else {
                weapon_catalog().get(p.id).grid.beats()
            };
            let mut point = p.fire.or_else(|| grid_at_or_after(beats, song, due, step));
            if !p.id.is_sword()
                && let (Some(at), Some(last)) = (point, self.reserved[slot])
                && at.song <= last.song + EPS
                && let (Some(_), Some(last_bi)) = (at.beat, last.beat)
            {
                point = grid_point(beats, song, (last_bi * 4.0 + EPS).floor() / 4.0 + 0.25);
                if point.is_none()
                    && beats
                        .iter()
                        .any(|b| b.known_at <= song + EPS && b.t > song + EPS)
                {
                    continue;
                }
            }
            let at = point.unwrap_or(GridPoint {
                song: due,
                beat: None,
            });
            self.pending[i].fire = Some(at);
            self.pending[i].planned = true;
            self.reserved[slot] = Some(at);
        }
    }
    pub fn next_stop(&self, _run: f64, song: f64) -> f64 {
        let ready = self
            .clocks
            .iter()
            .flatten()
            .map(|c| (c.next_ready + self.song_offset).max(song))
            .fold(f64::INFINITY, f64::min);
        self.pending
            .iter()
            .filter_map(|p| p.fire.filter(|_| p.planned).map(|p| p.song.max(song)))
            .fold(ready, f64::min)
    }
    pub fn take_due(&mut self, song: f64) -> Vec<(WeaponId, f64, f64)> {
        let mut due = Vec::new();
        self.pending.retain(|p| {
            if p.planned && p.fire.is_some_and(|f| f.song <= song + EPS) {
                due.push((p.id, p.ready_run, p.period));
                false
            } else {
                true
            }
        });
        due
    }
    pub fn pending_count(&self) -> usize {
        self.pending.len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn beats(bpm: f64, seconds: f64) -> Vec<Beat> {
        let period = 60.0 / bpm;
        (0..((seconds / period).ceil() as usize + 4))
            .map(|i| {
                let t = i as f64 * period;
                Beat {
                    t,
                    known_at: (t - period).max(0.0),
                    period,
                    evidence: 1.0,
                }
            })
            .collect()
    }
    fn measured(id: WeaponId, rank: u8) -> Arsenal {
        let mut out = Arsenal::default();
        out.inventory.weapons = Slots::default();
        for _ in 0..rank {
            out.inventory.weapons.upgrade(id, 8).unwrap();
        }
        out.clocks[0] = Some(Cooldown::new(0.0));
        out
    }
    fn run(arsenal: &mut Arsenal, beats: &[Beat], stats: Stats, end: f64) -> Vec<(f64, f64)> {
        let mut now = 0.0;
        let mut shots = Vec::new();
        while now < end - 1e-9 {
            arsenal.prepare(now, now, beats, stats);
            for (_, ready, _) in arsenal.take_due(now) {
                shots.push((ready, now));
            }
            let next = arsenal.next_stop(now, now).min(now + 1.0 / 120.0);
            assert!(next > now + 1e-12, "stuck at {now}");
            now = next;
        }
        shots
    }
    #[test]
    fn six_slots_are_bounded_and_an_owned_upgrade_uses_no_new_slot() {
        let mut slots = Slots::default();
        for id in 0..6 {
            assert_eq!(slots.upgrade(id, 2), Ok(1));
        }
        assert!(slots.is_full());
        assert_eq!(slots.upgrade(6, 2), Err(SlotError::Full));
        assert_eq!(slots.upgrade(0, 2), Ok(2));
        assert_eq!(slots.upgrade(0, 2), Err(SlotError::Maxed));
        assert_eq!(slots.iter().count(), 6);
        slots.replace(5, 7).unwrap();
        assert_eq!(slots.rank(7), Some(1));
        assert_eq!(PassiveId::Duplicator.max_rank(), 2);
        assert_eq!(PassiveId::HollowHeart.max_rank(), 5);
    }
    #[test]
    fn cooldowns_keep_vs_throughput_without_grid_drift_at_three_tempos() {
        for bpm in [70.0, 128.0, 174.0] {
            let grid = beats(bpm, 600.0);
            for (id, rank, cooldown) in [
                (WeaponId::Garlic, 1, 1.0),
                (WeaponId::Garlic, 8, 1.0),
                (WeaponId::Garlic, 8, 0.6),
                (WeaponId::HolyWand, 1, 0.6),
            ] {
                let stats = Stats {
                    cooldown,
                    ..Stats::default()
                };
                let def = weapon_catalog().get(id);
                let interval = def.level(rank).interval(stats);
                let shots = run(&mut measured(id, rank), &grid, stats, 600.0);
                let expected = 600.0 / interval;
                let error = (shots.len() as f64 / expected - 1.0).abs();
                assert!(
                    error < 0.01,
                    "{bpm} {id:?}: {} shots vs {expected}",
                    shots.len()
                );
                if id == WeaponId::HolyWand && bpm == 70.0 {
                    let eighth = 60.0 / bpm / 2.0;
                    assert!(shots.iter().any(|(_,fire)| (fire/eighth-(fire/eighth).round()).abs()>1e-6),
                        "colliding eighth-note shots must actually stagger on sixteenths");
                }
                let step = 60.0 / bpm * def.grid.beats();
                for &(ready, fire) in &shots {
                    assert!(fire >= ready - 1e-8, "shot before ready");
                    assert!(
                        fire - ready < step + 1e-8,
                        "{bpm} {id:?}: delay {} >= {step}",
                        fire - ready
                    );
                    let phase = fire / (60.0 / bpm / 4.0);
                    assert!((phase - phase.round()).abs() < 1e-7, "off grid: {fire}");
                }
                eprintln!(
                    "{bpm} BPM {id:?} rank{rank}, interval{interval:.3}s: {} shots/600s, expected{expected:.6}, error{:.4}%",
                    shots.len(),
                    100.0 * error
                );
            }
        }
    }
    #[test]
    fn no_grid_uses_the_exact_raw_interval() {
        let stats = Stats {
            cooldown: 0.6,
            ..Stats::default()
        };
        let shots = run(&mut measured(WeaponId::HolyWand, 1), &[], stats, 3.0);
        assert_eq!(shots.len(), 10);
        for (i, &(ready, fire)) in shots.iter().enumerate() {
            assert!((ready - i as f64 * 0.3).abs() < 1e-9);
            assert!((fire - ready).abs() < 1e-9);
        }
    }
    #[test]
    fn a_pending_cooldown_survives_a_long_freeze_without_song_time_debt() {
        let grid = beats(120.0, 30.0);
        let stats = Stats::default();
        let mut arsenal = measured(WeaponId::Garlic, 1);
        arsenal.prepare(0.0, 0.0, &grid, stats);
        assert_eq!(arsenal.take_due(0.0).len(), 1);
        arsenal.prepare(1.3, 1.3, &grid, stats);
        assert!(arsenal.take_due(1.3).is_empty());
        arsenal.hold();
        arsenal.resume(19.0);
        arsenal.prepare(1.4, 20.4, &grid, stats);
        assert!(arsenal.take_due(20.4).is_empty());
        let due = arsenal.next_stop(1.4, 20.4);
        assert!((due - 20.5).abs() < 1e-9);
        let fired = arsenal.take_due(due);
        assert_eq!(fired.len(), 1);
        assert!((fired[0].1 - 1.3).abs() < 1e-9);
        assert!((arsenal.clocks[0].unwrap().next_ready - 2.6).abs() < 1e-9);
    }
    #[test]
    fn an_unknown_future_grid_cannot_change_a_raw_shot() {
        let mut a = measured(WeaponId::Garlic, 1);
        let mut b = a.clone();
        let mut hidden = beats(120.0, 10.0);
        for beat in &mut hidden {
            beat.known_at = 8.0;
        }
        a.prepare(0.0, 0.0, &[], Stats::default());
        b.prepare(0.0, 0.0, &hidden, Stats::default());
        assert_eq!(a, b);
        assert_eq!(a.take_due(0.0), b.take_due(0.0));
    }
}
