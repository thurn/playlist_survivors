//! The players who ignore the beat (`MasherPlayer`, `HolderPlayer` in `src/sim/player.ts`): they
//! steer like the virtual player, whose sense of direction they borrow.

use std::collections::BTreeSet;

use sim::movement::{InputKey, Key};
use sim::player::{KeyEvent, KeyEventKind, Observation, Player};

use crate::virtual_player::{PlayerSection, VirtualPlayer, event, rng, section_at};

fn input(k: Key) -> InputKey {
    match k {
        Key::Up => InputKey::Up,
        Key::Down => InputKey::Down,
        Key::Left => InputKey::Left,
        Key::Right => InputKey::Right,
    }
}

/// The steerer's next decision outside step movement (its own presses are skipped).
fn steering_after(steerer: &mut VirtualPlayer, sections: &[PlayerSection], after: f64) -> f64 {
    let mut v = steerer.next_decision(after);
    while v.is_finite() && section_at(sections, v).steps {
        v = steerer.next_decision(v + 1e-6);
    }
    v
}

/// A player who presses a key every 1/rate seconds (jittered ±30%) in step movement, in the
/// direction the virtual player would choose.
#[derive(Clone, Debug)]
pub struct MasherPlayer {
    steerer: VirtualPlayer,
    sections: Vec<PlayerSection>,
    seed: f64,
    /// Presses per second.
    rate: f64,
    t: f64,
    n: f64,
}

impl MasherPlayer {
    pub fn new(
        steerer: VirtualPlayer,
        sections: Vec<PlayerSection>,
        seed: f64,
        rate: f64,
    ) -> MasherPlayer {
        MasherPlayer {
            steerer,
            sections,
            seed,
            rate,
            t: 0.0,
            n: 0.0,
        }
    }

    fn gap(&mut self) -> f64 {
        let g = (1.0 / self.rate) * rng(self.seed, "mash", self.n).range(0.7, 1.3);
        self.n += 1.0;
        g
    }

    /// The next mash time at or after `after`, in step movement.
    fn mash_at(&mut self, after: f64) -> f64 {
        while self.t < after - 1e-9 {
            self.t += self.gap();
        }
        if section_at(&self.sections, self.t).steps {
            return self.t;
        }
        match self.sections.iter().find(|x| x.steps && x.s0 > self.t) {
            Some(next) => {
                self.t = next.s0;
                self.t
            }
            None => f64::INFINITY,
        }
    }
}

impl Player for MasherPlayer {
    fn next_decision(&mut self, after: f64) -> f64 {
        let v = steering_after(&mut self.steerer, &self.sections, after);
        self.mash_at(after).min(v)
    }

    fn decide(&mut self, obs: &Observation) -> Vec<KeyEvent> {
        if !section_at(&self.sections, obs.s).steps {
            return self.steerer.decide(obs);
        }
        if (obs.s - self.t).abs() > 1e-6 {
            return Vec::new();
        }
        let key = input(self.steerer.steer_keys(obs)[0]);
        self.t = obs.s + self.gap();
        vec![
            event(obs.s, KeyEventKind::Press, key),
            event(obs.s + 0.03, KeyEventKind::Release, key),
        ]
    }
}

/// A player who never taps: in step movement it holds the direction the virtual player would
/// choose and changes it at most once a bar, at a seeded moment in the bar.
#[derive(Clone, Debug)]
pub struct HolderPlayer {
    steerer: VirtualPlayer,
    sections: Vec<PlayerSection>,
    moments: Vec<f64>,
    next: usize,
    held: BTreeSet<u8>,
}

fn code(k: Key) -> u8 {
    match k {
        Key::Up => 0,
        Key::Down => 1,
        Key::Left => 2,
        Key::Right => 3,
    }
}

const KEYS: [Key; 4] = [Key::Up, Key::Down, Key::Left, Key::Right];

impl HolderPlayer {
    /// `bars`: song times of the bars' starts, in order.
    pub fn new(
        steerer: VirtualPlayer,
        sections: Vec<PlayerSection>,
        seed: f64,
        bars: &[f64],
    ) -> HolderPlayer {
        let moments = bars
            .windows(2)
            .filter(|b| section_at(&sections, b[0]).steps)
            .map(|b| b[0] + (b[1] - b[0]) * rng(seed, "hold", (b[0] * 1000.0).round()).next())
            .collect();
        HolderPlayer {
            steerer,
            sections,
            moments,
            next: 0,
            held: BTreeSet::new(),
        }
    }

    fn moment_at(&mut self, after: f64) -> f64 {
        while self.next < self.moments.len() && self.moments[self.next] < after - 1e-9 {
            self.next += 1;
        }
        self.moments
            .get(self.next)
            .copied()
            .unwrap_or(f64::INFINITY)
    }
}

impl Player for HolderPlayer {
    fn next_decision(&mut self, after: f64) -> f64 {
        let v = steering_after(&mut self.steerer, &self.sections, after);
        self.moment_at(after).min(v)
    }

    fn decide(&mut self, obs: &Observation) -> Vec<KeyEvent> {
        if !section_at(&self.sections, obs.s).steps {
            // Into free movement: let go, and the virtual player steers from there.
            let mut out: Vec<KeyEvent> = self
                .held
                .iter()
                .map(|&k| event(obs.s, KeyEventKind::Release, input(KEYS[k as usize])))
                .collect();
            self.held.clear();
            out.extend(self.steerer.decide(obs));
            return out;
        }
        if (obs.s - self.moment_at(obs.s)).abs() > 1e-6 {
            return Vec::new();
        }
        self.next += 1;
        let want = self.steerer.steer_keys(obs);
        let mut out = Vec::new();
        for k in self.held.clone() {
            if !want.iter().any(|&w| code(w) == k) {
                self.held.remove(&k);
                out.push(event(obs.s, KeyEventKind::Release, input(KEYS[k as usize])));
            }
        }
        let mut s = obs.s;
        for &k in &want {
            if self.held.contains(&code(k)) {
                continue;
            }
            // A diagonal's second key follows within a few milliseconds.
            if out.iter().any(|e: &KeyEvent| e.kind == KeyEventKind::Press) {
                s += 0.008;
            }
            self.held.insert(code(k));
            out.push(event(s, KeyEventKind::Press, input(k)));
        }
        out
    }
}
