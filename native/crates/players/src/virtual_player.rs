//! The virtual player (`VirtualPlayer` in `src/sim/player.ts`): a deterministic program that plays
//! like a skilled human. It knows the song (the press pattern on the grid, the roam pace), as a
//! practiced player would, and sees only the current game state when it acts.

use std::collections::BTreeSet;
use std::f64::consts::PI;

use sim::core::geometry::wrap_angle;
use sim::core::rng::{Part, Rng};
use sim::core::shaping::clamp;
use sim::movement::{InputKey, Key, ease_out, key_dir};
use sim::player::{KeyEvent, KeyEventKind, Observation, Player, Point, ThreatKind, ThreatView};
use sim::upgrades::preferred_offer;
use sim::world::Frame;

/// A skill profile: how a player's presses are timed and chosen.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct ProfileSpec {
    /// Standard deviation and clamp of the timing error, in seconds.
    pub sd: f64,
    pub clamp_s: f64,
    /// How late every press lands on average (seconds), on top of the error.
    pub bias: f64,
    /// Casual players press downbeats only.
    pub downbeats_only: bool,
    /// The eighths player presses every grid time and every off-beat.
    pub eighths: bool,
    /// Share of the section's pattern slots pressed (seeded draws).
    pub press_rate: f64,
    /// Seconds a threat must have been in view before the player reacts to it.
    pub reaction: f64,
    /// Chance per press of taking the second-best direction instead of the best.
    pub sloppy: f64,
}

/// The simulation shows a threat to the player this long after it appears.
const SIM_REACTION: f64 = 0.15;

const fn spec(
    sd: f64,
    clamp_s: f64,
    bias: f64,
    press_rate: f64,
    reaction: f64,
    sloppy: f64,
) -> ProfileSpec {
    ProfileSpec {
        sd,
        clamp_s,
        bias,
        downbeats_only: false,
        eighths: false,
        press_rate,
        reaction,
        sloppy,
    }
}

/// The skill profiles by name (`PROFILES`).
pub const PROFILES: [(&str, ProfileSpec); 7] = [
    ("perfect", spec(0.0, 0.0, 0.0, 1.0, SIM_REACTION, 0.0)),
    ("skilled", spec(0.012, 0.04, 0.0, 1.0, SIM_REACTION, 0.0)),
    (
        "casual",
        ProfileSpec {
            downbeats_only: true,
            ..spec(0.06, 0.15, 0.0, 1.0, SIM_REACTION, 0.0)
        },
    ),
    // The automated stand-in for a moderately competent human.
    ("moderate", spec(0.03, 0.1, 0.0, 0.85, 0.25, 0.15)),
    (
        "eighths",
        ProfileSpec {
            eighths: true,
            ..spec(0.012, 0.04, 0.0, 1.0, SIM_REACTION, 0.0)
        },
    ),
    // Timed like a person's play logs: a 50 ms spread, 35 ms late on average; and 100 ms late.
    ("human", spec(0.05, 0.15, 0.035, 0.85, 0.25, 0.15)),
    ("late", spec(0.05, 0.15, 0.1, 0.85, 0.25, 0.15)),
];

/// A skill profile by name.
pub fn profile(name: &str) -> Option<ProfileSpec> {
    PROFILES.iter().find(|p| p.0 == name).map(|p| p.1)
}

/// A section as the player knows it: the press pattern and whether it steps.
#[derive(Clone, Debug, PartialEq)]
pub struct PlayerSection {
    pub steps: bool,
    /// Press every slot ("x") or every other ("x."), cycled over the section's slots.
    pub presses: String,
    pub s0: f64,
    pub s1: f64,
    /// The roam speed (world px per world-second).
    pub pace: f64,
}

/// A slot the player may press: its song time, beat index, place in the bar and section.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Slot {
    pub s: f64,
    pub bi: f64,
    pub slot: usize,
    pub section: usize,
}

/// A near miss: within this many pixels of the hurtbox's edge.
const NEAR: f64 = 24.0;
/// Free movement: the weave across the roam heading (px) and its period (beats).
const WEAVE: f64 = 70.0;
const WEAVE_BEATS: f64 = 16.0;
/// Free movement: aim this far ahead of the roam point, and stand still when this close.
const STEER_LEAD: f64 = 16.0;
const STEER_STOP: f64 = 14.0;
/// The roam point turns at most this much per bar, heads for a crowd farther than this, and stays this close to the hero.
const ROAM_TURN: f64 = PI / 4.0;
const ROAM_CROWD: f64 = 200.0;
const ROAM_LEASH: f64 = 120.0;
/// The roam walks up this close to a dancing formation's middle, so the cleave on its release reaches the line.
const ROAM_FORMATION: f64 = 100.0;
/// A released line is walked at for this long (song seconds) after it last danced.
const LINE_MEMORY: f64 = 0.7;
/// From tier 2 the player waits this close to a closing ring's middle; below it, leaves this far beyond a gap.
const RING_MIDDLE: f64 = 40.0;
const RING_OUT: f64 = 80.0;
/// A stampede's runner is avoided this far ahead in time (s), at these distances (px).
const RUNNER_LOOK: f64 = 0.15;
const RUNNER_NEAR: f64 = 40.0;
const RUNNER_CLOSE: f64 = 70.0;
/// Gems: what each point of experience in reach is worth to a step (up to a cap).
const GEM_PULL: f64 = 0.12;
const GEM_PULL_MAX: f64 = 15.0;
/// Gems beyond the pickup radius draw each step this strongly per point (up to a cap) per scale px closer.
const GEM_SEEK: f64 = 0.15;
const GEM_SEEK_MAX: f64 = 200.0;
const GEM_SEEK_SCALE: f64 = 100.0;
/// Free movement walks to gems within this much of the pickup radius.
const GEM_WALK: f64 = 150.0;
/// A vacuum in sight draws the player to touch it, this strongly.
const VACUUM_PULL: f64 = 4.0;
/// The chosen upgrade orb draws each step this strongly per 100 px closer, and by ORB_AIM for its direction.
const ORB_PULL: f64 = 6.0;
const ORB_AIM: f64 = 4.0;
/// A step leaving the hero within BOSS_KEEP px of the boss costs up to BOSS_AVOID.
const BOSS_KEEP: f64 = 110.0;
const BOSS_AVOID: f64 = 4.0;
/// Below this share of its health the player walks to the nearest chicken in sight.
const CHICKEN_HEALTH: f64 = 0.6;
/// A decision this close before a glowing bar's level-up (s) waits for its orbs: it stomps instead.
const LEVEL_WAIT: f64 = 0.1;

/// The eight step directions: one key, or two perpendicular keys for a diagonal.
const DIRECTIONS: [&[Key]; 8] = [
    &[Key::Right],
    &[Key::Right, Key::Down],
    &[Key::Down],
    &[Key::Down, Key::Left],
    &[Key::Left],
    &[Key::Left, Key::Up],
    &[Key::Up],
    &[Key::Up, Key::Right],
];

fn input(k: Key) -> InputKey {
    match k {
        Key::Up => InputKey::Up,
        Key::Down => InputKey::Down,
        Key::Left => InputKey::Left,
        Key::Right => InputKey::Right,
    }
}

pub(crate) fn event(s: f64, kind: KeyEventKind, key: InputKey) -> KeyEvent {
    KeyEvent {
        s,
        kind,
        key,
        id: None,
    }
}

fn dir_vector(keys: &[Key]) -> (f64, f64) {
    let (mut x, mut y) = (0.0, 0.0);
    for &k in keys {
        let d = key_dir(k);
        x += d.x;
        y += d.y;
    }
    let l = x.hypot(y);
    let l = if l == 0.0 { 1.0 } else { l };
    (x / l, y / l)
}

pub(crate) fn rng(seed: f64, what: &str, n: f64) -> Rng {
    Rng::new(&[Part::Num(seed), Part::Str(what), Part::Num(n)])
}

/// Normal deviate from two uniforms (Box–Muller).
fn normal(r: &mut Rng) -> f64 {
    let u = r.next().max(1e-12);
    (-2.0 * u.ln()).sqrt() * (2.0 * PI * r.next()).cos()
}

fn dist(ax: f64, ay: f64, bx: f64, by: f64) -> f64 {
    (ax - bx).hypot(ay - by)
}

pub(crate) fn section_at(sections: &[PlayerSection], s: f64) -> &PlayerSection {
    sections
        .iter()
        .rev()
        .find(|x| s >= x.s0 - 1e-9)
        .unwrap_or(&sections[0])
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Kind {
    Press,
    Watch,
    Steer { release: bool },
}

#[derive(Clone, Copy, Debug)]
struct Decision {
    s: f64,
    kind: Kind,
    beat: f64,
}

#[derive(Clone, Copy, Debug)]
struct Roam {
    x: f64,
    y: f64,
    dir: f64,
    w: f64,
    bi: f64,
}

/// The virtual player.
#[derive(Clone, Debug)]
pub struct VirtualPlayer {
    /// Planned decisions: presses on the grid, a look for threats on rest slots, steering on each
    /// beat of a free section.
    plan: Vec<Decision>,
    next: usize,
    last_dir: Option<(f64, f64)>,
    count: f64,
    held: BTreeSet<KeyOrd>,
    spec: ProfileSpec,
    sections: Vec<PlayerSection>,
    beats: Vec<f64>,
    seed: f64,
    step_length: f64,
    step_s: f64,
    /// The cost of the latest choice of keys.
    best_cost: f64,
    /// The latest dancing formation's middle, and when it was seen.
    last_line: Option<(f64, f64, f64)>,
    roam: Option<Roam>,
}

/// A key in a set, in a fixed order.
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
struct KeyOrd(u8);

fn ord(k: Key) -> KeyOrd {
    KeyOrd(match k {
        Key::Up => 0,
        Key::Down => 1,
        Key::Left => 2,
        Key::Right => 3,
    })
}

fn unord(k: KeyOrd) -> Key {
    [Key::Up, Key::Down, Key::Left, Key::Right][k.0 as usize]
}

impl VirtualPlayer {
    /// A player of `spec` pressing `slots` (their sections' patterns) and steering on `beats` in free
    /// sections; `step_length` (px) and `step_s` (s) are the hero's step.
    pub fn new(
        step_length: f64,
        step_s: f64,
        seed: f64,
        slots: &[Slot],
        sections: Vec<PlayerSection>,
        beats: Vec<f64>,
        spec: ProfileSpec,
    ) -> VirtualPlayer {
        let mut plan = Vec::new();
        let grid: Vec<&Slot> = slots
            .iter()
            .filter(|sl| sections[sl.section].steps)
            .collect();
        let err = |r: &mut Rng| {
            (if spec.sd > 0.0 {
                clamp(normal(r) * spec.sd, -spec.clamp_s, spec.clamp_s)
            } else {
                0.0
            }) + spec.bias
        };
        for (i, sl) in grid.iter().enumerate() {
            let sec = &sections[sl.section];
            let p = sec.presses.as_bytes();
            let mut pressed = spec.eighths
                || if spec.downbeats_only {
                    sl.slot == 0
                } else {
                    p.get(sl.slot % p.len().max(1)) == Some(&b'x')
                };
            let ms = (sl.s * 1000.0).round();
            if pressed
                && spec.press_rate < 1.0
                && rng(seed, "press-skip", ms).next() >= spec.press_rate
            {
                pressed = false;
            }
            let e = err(&mut rng(seed, "press-time", ms));
            plan.push(Decision {
                s: sl.s + if pressed { e } else { 0.0 },
                kind: if pressed { Kind::Press } else { Kind::Watch },
                beat: sl.bi,
            });
            // The eighths player's off-beat, half-way to the next grid time.
            if let Some(next) = grid.get(i + 1) {
                let joined = next.section == sl.section
                    || (next.section == sl.section + 1
                        && sections[next.section].s0 <= sec.s1 + 1e-6);
                if spec.eighths && joined {
                    let off = f64::midpoint(sl.s, next.s);
                    let e = err(&mut rng(seed, "offbeat-time", (off * 1000.0).round()));
                    plan.push(Decision {
                        s: off + e,
                        kind: Kind::Press,
                        beat: sl.bi,
                    });
                }
            }
        }
        // Free movement: change the held keys at most once per beat; let go before step movement.
        for (i, &b) in beats.iter().enumerate() {
            if section_at(&sections, b).steps {
                continue;
            }
            let release = beats
                .get(i + 1)
                .is_none_or(|&n| section_at(&sections, n).steps);
            plan.push(Decision {
                s: b + 0.01,
                kind: Kind::Steer { release },
                beat: i as f64,
            });
        }
        plan.sort_by(|a, b| a.s.total_cmp(&b.s));
        VirtualPlayer {
            plan,
            next: 0,
            last_dir: None,
            count: 0.0,
            held: BTreeSet::new(),
            spec,
            sections,
            beats,
            seed,
            step_length,
            step_s,
            best_cost: f64::INFINITY,
            last_line: None,
            roam: None,
        }
    }

    fn held_keys(&self) -> Vec<Key> {
        self.held.iter().map(|&k| unord(k)).collect()
    }

    fn release_all(&mut self, s: f64) -> Vec<KeyEvent> {
        let out = self
            .held_keys()
            .into_iter()
            .map(|k| event(s, KeyEventKind::Release, input(k)))
            .collect();
        self.held.clear();
        out
    }

    /// Strides: let go of the keys held, press the new direction and hold it until the next
    /// decision; with a threat in view, tap instead. Stomp when standing beats every direction.
    fn stride_keys(&mut self, obs: &Observation, keys: &[Key], r: &mut Rng) -> Vec<KeyEvent> {
        let mut out = self.release_all(obs.s);
        // (Not while a dancing formation's middle is out of reach: walk up to the line to cleave it.)
        let at_line = self
            .line(obs)
            .is_none_or(|f| dist(f.x, f.y, obs.hero.x, obs.hero.y) <= ROAM_FORMATION);
        if at_line && self.stand_cost(obs) < self.best_cost {
            out.push(event(obs.s, KeyEventKind::Press, InputKey::Space));
            out.push(event(
                obs.s + r.range(0.06, 0.1),
                KeyEventKind::Release,
                InputKey::Space,
            ));
            return out;
        }
        let mut s = obs.s;
        for (i, &k) in keys.iter().enumerate() {
            // A diagonal's second key follows within a few milliseconds.
            if i > 0 {
                s += r.range(0.002, 0.015);
            }
            out.push(event(s, KeyEventKind::Press, input(k)));
        }
        if obs.threats.is_empty() {
            self.held.extend(keys.iter().map(|&k| ord(k)));
        } else {
            for &k in keys {
                out.push(event(
                    s + r.range(0.06, 0.1),
                    KeyEventKind::Release,
                    input(k),
                ));
            }
        }
        out
    }

    fn stomp(&mut self, obs: &Observation, r: &mut Rng) -> Vec<KeyEvent> {
        let mut out = self.release_all(obs.s);
        out.push(event(obs.s, KeyEventKind::Press, InputKey::Space));
        out.push(event(
            obs.s + r.range(0.06, 0.1),
            KeyEventKind::Release,
            InputKey::Space,
        ));
        out
    }

    /// On a rest slot: keep holding while going on beats standing still, else let go.
    fn keep_holding(&mut self, obs: &Observation) -> Vec<KeyEvent> {
        if self.held.is_empty() {
            return Vec::new();
        }
        let keys = self.held_keys();
        let p = self.pace_frame(obs);
        let d = dir_vector(&keys);
        let l = self.step_length;
        if self.place_cost(obs, p, d, obs.hero.x + d.0 * l, obs.hero.y + d.1 * l, l)
            <= self.stand_cost(obs)
        {
            return Vec::new();
        }
        self.release_all(obs.s)
    }

    /// The formation to walk up to: one dancing now, or for a moment after its release.
    fn line(&mut self, obs: &Observation) -> Option<Point> {
        if let Some(f) = obs.formation {
            self.last_line = Some((f.x, f.y, obs.s));
        }
        self.last_line
            .filter(|l| obs.s - l.2 <= LINE_MEMORY)
            .map(|l| Point { x: l.0, y: l.1 })
    }

    /// The way through a closing ring the hero is inside: from tier 2 its middle; below it, out
    /// through the gap nearest the hero. How close counts as there, too.
    fn ring_way(obs: &Observation) -> Option<(f64, f64, f64)> {
        let ring = obs.ring.as_ref()?;
        if dist(obs.hero.x, obs.hero.y, ring.x, ring.y) >= ring.r {
            return None;
        }
        if obs.hero.tier.unwrap_or(0.0) >= 2.0 {
            return Some((ring.x, ring.y, RING_MIDDLE));
        }
        let th = (obs.hero.y - ring.y).atan2(obs.hero.x - ring.x);
        let g = ring.gaps.iter().copied().reduce(|a, b| {
            if wrap_angle(b - th).abs() < wrap_angle(a - th).abs() {
                b
            } else {
                a
            }
        })?;
        Some((
            ring.x + g.cos() * (ring.r + RING_OUT),
            ring.y + g.sin() * (ring.r + RING_OUT),
            30.0,
        ))
    }

    /// The gem in sight beyond the pickup radius worth most for its distance.
    fn gem_seek(obs: &Observation) -> Option<(f64, f64, f64)> {
        let mut best = None;
        let mut best_score = 0.0;
        for g in obs.gems.iter().flatten() {
            let d = dist(g.x, g.y, obs.hero.x, obs.hero.y);
            if d <= obs.hero.pickup.unwrap_or(0.0) {
                continue;
            }
            let score = g.value / (d + 30.0);
            if score > best_score {
                best_score = score;
                best = Some((g.x, g.y, g.value));
            }
        }
        best
    }

    fn chicken_target(obs: &Observation) -> Option<Point> {
        let cs = obs.chickens.as_ref()?;
        if cs.is_empty() || obs.hero.health.unwrap_or(1.0) >= CHICKEN_HEALTH {
            return None;
        }
        let d = |c: &Point| dist(c.x, c.y, obs.hero.x, obs.hero.y);
        cs.iter()
            .copied()
            .reduce(|a, c| if d(&c) < d(&a) { c } else { a })
    }

    fn orb_target(obs: &Observation) -> Option<Point> {
        let orbs = obs.orbs.as_ref().filter(|o| !o.is_empty())?;
        let items: Vec<_> = orbs.iter().map(|o| o.item.clone()).collect();
        let o = &orbs[preferred_offer(&items, obs.hero.health.unwrap_or(1.0))];
        Some(Point { x: o.x, y: o.y })
    }

    /// Free movement: the chosen orb, a vacuum, or the gem in reach worth most for its distance.
    fn gem_target(obs: &Observation) -> Option<Point> {
        if let Some(p) = Self::orb_target(obs).or_else(|| Self::chicken_target(obs)) {
            return Some(p);
        }
        if obs.vacuum.is_some() {
            return obs.vacuum;
        }
        let pickup = obs.hero.pickup.unwrap_or(0.0);
        let mut best = None;
        let mut best_score = 0.0;
        for g in obs.gems.iter().flatten() {
            let d = dist(g.x, g.y, obs.hero.x, obs.hero.y);
            if d <= pickup * 0.5 || d > pickup + GEM_WALK {
                continue;
            }
            let score = g.value / (d + 30.0);
            if score > best_score {
                best_score = score;
                best = Some(Point { x: g.x, y: g.y });
            }
        }
        best
    }

    fn stand_cost(&mut self, obs: &Observation) -> f64 {
        let p = self.pace_frame(obs);
        self.place_cost(obs, p, (0.0, 0.0), obs.hero.x, obs.hero.y, 0.0)
    }

    /// Free movement: hold the key toward the roam point plus a slow weave across its heading.
    fn steer(&mut self, obs: &Observation, beat: f64, release: bool) -> Vec<KeyEvent> {
        let mut want: Vec<Key> = Vec::new();
        if !release {
            let p = self.pace_frame(obs);
            let weave = WEAVE * (2.0 * PI * beat / WEAVE_BEATS).sin();
            let (mut tx, mut ty) = (
                p.x + p.tx * STEER_LEAD - p.ty * weave,
                p.y + p.ty * STEER_LEAD + p.tx * weave,
            );
            if let Some(g) = Self::gem_target(obs) {
                tx = g.x;
                ty = g.y;
            }
            let (dx, dy) = (tx - obs.hero.x, ty - obs.hero.y);
            if dx.hypot(dy) > STEER_STOP {
                let k = ((dy.atan2(dx) / (2.0 * PI) * 8.0).round() as i64).rem_euclid(8) as usize;
                want = DIRECTIONS[k].to_vec();
            }
        }
        let mut out = Vec::new();
        for k in self.held_keys() {
            if !want.contains(&k) {
                self.held.remove(&ord(k));
                out.push(event(obs.s, KeyEventKind::Release, input(k)));
            }
        }
        for &k in &want {
            if self.held.insert(ord(k)) {
                out.push(event(obs.s, KeyEventKind::Press, input(k)));
            }
        }
        out
    }

    /// What the player keeps up with: its roam point (before it first moves, the hero's frame).
    fn pace_frame(&self, obs: &Observation) -> Frame {
        match self.roam {
            None => obs.pace_point,
            Some(r) => Frame {
                x: r.x,
                y: r.y,
                tx: r.dir.cos(),
                ty: r.dir.sin(),
            },
        }
    }

    fn beat_index(&self, s: f64) -> f64 {
        let b = &self.beats;
        if b.is_empty() || s <= b[0] {
            return 0.0;
        }
        let mut hi = b.len() - 1;
        if s >= b[hi] {
            return hi as f64;
        }
        let mut lo = 0;
        while hi - lo > 1 {
            let mid = usize::midpoint(lo, hi);
            if b[mid] <= s {
                lo = mid;
            } else {
                hi = mid;
            }
        }
        lo as f64 + (s - b[lo]) / (b[hi] - b[lo])
    }

    /// The open field's roam point: it starts at the hero and moves at the section's pace along a
    /// slowly turning heading, pulled toward a ring's way out, a dancing formation or the crowd.
    fn move_roam(&mut self, obs: &Observation) {
        let bi = self.beat_index(obs.s);
        let Some(mut r) = self.roam else {
            self.roam = Some(Roam {
                x: obs.hero.x,
                y: obs.hero.y,
                dir: 0.0,
                w: obs.w,
                bi,
            });
            return;
        };
        let dw = (obs.w - r.w).max(0.0);
        // A turn rate per bar of two beats.
        let mut b = r.bi;
        while b < bi - 1e-9 {
            let bar = (b / 2.0 + 1e-9).floor();
            let next = bi.min(bar * 2.0 + 2.0);
            r.dir += rng(self.seed, "roam", bar).range(-1.0, 1.0) * ROAM_TURN * ((next - b) / 2.0);
            b = next;
        }
        let turn = |r: &mut Roam, x: f64, y: f64, k: f64| {
            r.dir += wrap_angle((y - r.y).atan2(x - r.x) - r.dir) * (dw * k).min(1.0);
        };
        if let Some(way) = Self::ring_way(obs) {
            turn(&mut r, way.0, way.1, 3.0);
        } else if let Some(f) = obs.formation {
            if dist(f.x, f.y, r.x, r.y) > ROAM_FORMATION {
                turn(&mut r, f.x, f.y, 1.5);
            }
        } else if !obs.enemies.is_empty() {
            let n = obs.enemies.len() as f64;
            let cx = obs.enemies.iter().map(|e| e.x).sum::<f64>() / n;
            let cy = obs.enemies.iter().map(|e| e.y).sum::<f64>() / n;
            if dist(cx, cy, r.x, r.y) > ROAM_CROWD {
                turn(&mut r, cx, cy, 1.5);
            }
        }
        let pace = section_at(&self.sections, obs.s).pace;
        r.x += r.dir.cos() * pace * dw;
        r.y += r.dir.sin() * pace * dw;
        // On a leash: it never runs far ahead of the hero.
        let (lx, ly) = (r.x - obs.hero.x, r.y - obs.hero.y);
        let ld = lx.hypot(ly);
        if ld > ROAM_LEASH {
            r.x = obs.hero.x + lx / ld * ROAM_LEASH;
            r.y = obs.hero.y + ly / ld * ROAM_LEASH;
        }
        r.w = obs.w;
        r.bi = bi;
        self.roam = Some(r);
    }

    /// Would standing here be hit?
    fn in_danger(obs: &Observation) -> bool {
        obs.threats
            .iter()
            .any(|t| Self::threat_cost(t, obs.hero.x, obs.hero.y) >= 50.0)
    }

    /// 50 inside a threat, a little when clear of it but not close enough for a near miss.
    fn threat_cost(t: &ThreatView, x: f64, y: f64) -> f64 {
        let edge = match t.kind {
            ThreatKind::Slam => dist(x, y, t.x, t.y) - t.radius.unwrap_or(30.0),
            ThreatKind::Arrow => {
                // The arrow's line, from the archer through its aim point, against the hurtbox.
                let (dx, dy) = (t.ax.unwrap_or(x) - t.x, t.ay.unwrap_or(y) - t.y);
                let dl = dx.hypot(dy);
                let dl = if dl == 0.0 { 1.0 } else { dl };
                let (hx, hy) = (x - t.x, y - 20.0 - t.y);
                if hx * dx + hy * dy < 0.0 {
                    return 0.0;
                }
                (hx * (dy / dl) - hy * (dx / dl)).abs() - 10.0
            }
        };
        if edge <= 3.0 {
            50.0
        } else if edge > NEAR - 4.0 {
            12.0
        } else {
            0.0
        }
    }

    /// The keys the player would press now, ignoring timing (the holder and masher borrow its sense
    /// of direction).
    pub fn steer_keys(&mut self, obs: &Observation) -> Vec<Key> {
        self.move_roam(obs);
        self.count += 1.0;
        self.choose_keys(obs)
    }

    /// What a step toward d (a unit vector, or zero to stand) costs where it leaves the hero, (ax, ay),
    /// l px away: keeping up with the roam frame p, threats, the crowd, the boss, set pieces, gems,
    /// orbs and chickens.
    #[allow(clippy::too_many_lines)]
    fn place_cost(
        &mut self,
        obs: &Observation,
        p: Frame,
        d: (f64, f64),
        ax: f64,
        ay: f64,
        l: f64,
    ) -> f64 {
        let (nx, ny) = (-p.ty, p.tx);
        let along = (ax - p.x) * p.tx + (ay - p.y) * p.ty;
        let lat = (ax - p.x) * nx + (ay - p.y) * ny;
        // (While an upgrade orb is chosen, the roam point waits.)
        let orb = Self::orb_target(obs);
        let mut cost = if orb.is_some() {
            0.0
        } else if along > 0.0 {
            (along / 30.0).powi(2)
        } else {
            (along / 70.0).powi(2)
        };
        if orb.is_none() {
            cost += (lat / 160.0).powi(2);
        }
        // Leave slam markers and arrow lines, judged where the step has taken the hero when they land.
        for t in &obs.threats {
            let e = ease_out(clamp((t.lands_at - obs.s) / self.step_s, 0.0, 1.0));
            cost += Self::threat_cost(t, obs.hero.x + d.0 * l * e, obs.hero.y + d.1 * l * e);
        }
        // Don't get buried, but stay in the fight.
        let (mut near, mut close) = (0.0_f64, 0.0_f64);
        for e in &obs.enemies {
            let dd = dist(e.x, e.y, ax, ay);
            if dd < 30.0 {
                close += 1.0;
            }
            if dd < 100.0 {
                near += 1.0;
            }
        }
        cost += 0.5 * close.min(6.0) * 0.5;
        cost -= 0.8 * near.min(15.0) / 5.0;
        if let Some(b) = obs.boss {
            cost += BOSS_AVOID * clamp((BOSS_KEEP - dist(ax, ay, b.x, b.y)) / BOSS_KEEP, 0.0, 1.0);
        }
        if let Some(f) = self.line(obs) {
            cost += 3.0 * clamp((dist(ax, ay, f.x, f.y) - ROAM_FORMATION) / 100.0, 0.0, 3.0);
        }
        if let Some(way) = Self::ring_way(obs) {
            cost += 3.0 * clamp((dist(ax, ay, way.0, way.1) - way.2) / 120.0, 0.0, 3.0);
        }
        if let (Some(gems), Some(pickup)) = (obs.gems.as_ref(), obs.hero.pickup)
            && orb.is_none()
        {
            let value: f64 = gems
                .iter()
                .filter(|g| dist(ax, ay, g.x, g.y) <= pickup)
                .map(|g| g.value)
                .sum();
            cost -= GEM_PULL * value.min(GEM_PULL_MAX);
            // (A vacuum in sight sends every gem in view: it comes first.)
            if obs.vacuum.is_none()
                && let Some(seek) = Self::gem_seek(obs)
            {
                cost += GEM_SEEK
                    * seek.2.min(GEM_SEEK_MAX)
                    * clamp(dist(ax, ay, seek.0, seek.1) / GEM_SEEK_SCALE, 0.0, 3.0);
            }
        }
        if let Some(v) = obs.vacuum {
            cost += VACUUM_PULL * clamp((dist(ax, ay, v.x, v.y) - 10.0) / 100.0, 0.0, 3.0);
        }
        let goal = orb.or_else(|| Self::chicken_target(obs));
        if let Some(g) = goal {
            cost += ORB_PULL * clamp(dist(ax, ay, g.x, g.y) / 100.0, 0.0, 3.0);
        }
        if let Some(aim) = goal.or(obs.vacuum) {
            let (ox, oy) = (aim.x - obs.hero.x, aim.y - obs.hero.y);
            let ol = ox.hypot(oy);
            let ol = if ol == 0.0 { 1.0 } else { ol };
            let dl = d.0.hypot(d.1);
            cost += ORB_AIM
                * if dl > 0.0 {
                    1.0 - (d.0 * ox + d.1 * oy) / (dl * ol)
                } else {
                    1.0
                };
        }
        for rn in obs.runners.iter().flatten() {
            let dd = dist(
                ax,
                ay,
                rn.x + rn.vx * RUNNER_LOOK,
                rn.y + rn.vy * RUNNER_LOOK,
            );
            if dd < RUNNER_NEAR {
                cost += 6.0;
            } else if dd < RUNNER_CLOSE {
                cost += 2.0;
            }
        }
        cost
    }

    /// Scores the eight directions by where the step would leave the hero, and picks the lowest.
    fn choose_keys(&mut self, obs: &Observation) -> Vec<Key> {
        let l = obs.hero.stride.unwrap_or(self.step_length);
        let p = self.pace_frame(obs);
        let (mut best, mut best_cost) = (DIRECTIONS[0], f64::INFINITY);
        let (mut second, mut second_cost) = (DIRECTIONS[0], f64::INFINITY);
        let mut r = rng(self.seed, "press-noise", self.count);
        for keys in DIRECTIONS {
            let d = dir_vector(keys);
            let mut cost =
                self.place_cost(obs, p, d, obs.hero.x + d.0 * l, obs.hero.y + d.1 * l, l);
            // Avoid back-and-forth jitter.
            if self
                .last_dir
                .is_some_and(|ld| d.0 * ld.0 + d.1 * ld.1 < -0.7)
            {
                cost += 0.6;
            }
            // A little variety.
            cost += r.next() * 0.2;
            if cost < best_cost {
                second = best;
                second_cost = best_cost;
                best_cost = cost;
                best = keys;
            } else if cost < second_cost {
                second_cost = cost;
                second = keys;
            }
        }
        self.best_cost = best_cost;
        if self.spec.sloppy > 0.0
            && obs.orbs.as_ref().is_none_or(Vec::is_empty)
            && second_cost.is_finite()
            && rng(self.seed, "sloppy", self.count).next() < self.spec.sloppy
        {
            best = second;
            self.best_cost = second_cost;
        }
        self.last_dir = Some(dir_vector(best));
        // Which of a diagonal's two keys comes first is a matter of habit.
        if best.len() == 2 && r.next() < 0.5 {
            return vec![best[1], best[0]];
        }
        best.to_vec()
    }
}

impl Player for VirtualPlayer {
    fn next_decision(&mut self, after: f64) -> f64 {
        while self.next < self.plan.len() && self.plan[self.next].s < after - 1e-9 {
            self.next += 1;
        }
        self.plan.get(self.next).map_or(f64::INFINITY, |d| d.s)
    }

    fn decide(&mut self, seen: &Observation) -> Vec<KeyEvent> {
        // A slower player only reacts to a threat once it has been in view a while.
        let filtered;
        let obs = if self.spec.reaction > SIM_REACTION + 1e-9 {
            filtered = Observation {
                threats: seen
                    .threats
                    .iter()
                    .copied()
                    .filter(|t| seen.s >= t.seen_at + self.spec.reaction - 1e-9)
                    .collect(),
                ..seen.clone()
            };
            &filtered
        } else {
            seen
        };
        self.move_roam(obs);
        while self.next < self.plan.len() && self.plan[self.next].s < obs.s - 1e-9 {
            self.next += 1;
        }
        let Some(&d) = self.plan.get(self.next) else {
            return Vec::new();
        };
        self.next += 1;
        if let Kind::Steer { release } = d.kind {
            return self.steer(obs, d.beat, release);
        }
        // On a rest slot, press only to get out of a threat's way, or toward an orb or a chicken.
        if d.kind == Kind::Watch
            && !Self::in_danger(obs)
            && obs.orbs.as_ref().is_none_or(Vec::is_empty)
            && Self::chicken_target(obs).is_none()
        {
            return self.keep_holding(obs);
        }
        let keys = self.choose_keys(obs);
        self.count += 1.0;
        let mut r = rng(self.seed, "press-keys", self.count);
        if obs.level_up_at.is_some_and(|l| l - obs.s < LEVEL_WAIT) && !Self::in_danger(obs) {
            return self.stomp(obs, &mut r);
        }
        self.stride_keys(obs, &keys, &mut r)
    }
}
