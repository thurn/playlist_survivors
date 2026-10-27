//! Bounded musical choreography inside the Survivors population, not a second spawner.
//! Authored shapes reuse FoeSlot/RingPath and the shared main-beat travel admission.
use super::{FoeSlot, Member, RingPath, Sim, SlotPlace};
use crate::core::rng::{Part, Rng};
use crate::recorder::{DanceRecord, FormationRecord};
use crate::survivors::SURVIVOR_CAP;
use stream::Level;

const GROUP_CAP: usize = 40;
const GROUP_GAP: f64 = 30.0;
const MAX_LIFE: f64 = 48.0;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(super) enum Kind {
    Stampede,
    Kick,
    Wedge,
    Ring,
}
impl Kind {
    fn eligible(self) -> f64 {
        match self {
            Self::Stampede => 120.0,
            Self::Kick => 180.0,
            Self::Wedge => 240.0,
            Self::Ring => 300.0,
        }
    }
    fn name(self) -> &'static str {
        match self {
            Self::Stampede => "stampede",
            Self::Kick => "skeleton-kick-line",
            Self::Wedge => "bee-wedge",
            Self::Ring => "ring",
        }
    }
}
#[derive(Clone, Copy, Debug)]
struct Pending {
    kind: Kind,
    born: f64,
    at: Option<f64>,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Phase {
    Entering,
    Dancing,
    Closing,
    Released,
}
#[derive(Clone, Debug)]
struct Group {
    kind: Kind,
    members: Vec<usize>,
    song: f64,
    until: f64,
    phase: Phase,
    beats: usize,
    dance: Option<f64>,
    recorded: bool,
}
#[derive(Clone, Debug, Default)]
pub(super) struct Musical {
    jump: usize,
    last_beat: Option<usize>,
    medium: usize,
    next: f64,
    issued: usize,
    pending: Option<Pending>,
    active: Option<Group>,
}
impl Musical {
    pub(super) fn cancel_pending(&mut self) {
        self.pending = None;
    }
}
impl Sim {
    fn musical_enabled(&self) -> bool {
        self.st.survivor.is_some()
            && self.env.director.set_pieces.is_some()
            && !self.st.freeze.frozen
            && !self.st.blasting
            && self.st.health > 0.0
    }
    pub(super) fn musical_stop(&self) -> f64 {
        if !self.musical_enabled() {
            return f64::INFINITY;
        }
        let m = &self.st.musical;
        let pending = m.pending.map_or(f64::INFINITY, |p| {
            p.at.unwrap_or(f64::INFINITY)
                .min(p.born + 2.0 + self.st.freeze.total_s)
        });
        pending
            .min(
                m.active
                    .as_ref()
                    .map_or(f64::INFINITY, |g| g.until + self.st.freeze.total_s),
            )
            .max(self.st.s)
    }
    fn musical_busy(&self) -> bool {
        self.st.alive.iter().any(|&id| {
            self.st.foes[id].alive
                && matches!(self.st.foes[id].member, Some(Member::SurvivorEvent(_)))
        })
    }
    fn can_stage(&self, kind: Kind) -> bool {
        self.st.run_s >= kind.eligible()
            && self.st.run_s >= self.st.musical.next
            && self.st.musical.active.is_none()
            && self.st.musical.pending.is_none()
            && !self.musical_busy()
            && self.st.spawner.waiting_bosses.is_empty()
    }
    fn stage(&mut self, kind: Kind) {
        if self.can_stage(kind) && self.enemy_reliable() {
            self.st.musical.pending = Some(Pending {
                kind,
                born: self.st.run_s,
                at: None,
            });
        }
    }
    /// A mandatory boss can reclaim ordinary/optional actors, but never another boss.
    pub(super) fn reserve_boss_room(&mut self) {
        let need = self.st.spawner.waiting_bosses.len().min(SURVIVOR_CAP);
        if need == 0 {
            return;
        }
        let excess = (self.st.alive.len() + need).saturating_sub(SURVIVOR_CAP);
        if excess == 0 {
            return;
        }
        let h = self.hero_pose(self.st.w);
        let mut retire: Vec<_> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&id| {
                let f = &self.st.foes[id];
                f.alive && !f.survivor_boss && self.st.boss_foe != Some(id)
            })
            .collect();
        retire.sort_by(|&a, &b| {
            let a = self.pos(a, self.st.w);
            let b = self.pos(b, self.st.w);
            (b.0 - h.x)
                .hypot(b.1 - h.y)
                .total_cmp(&(a.0 - h.x).hypot(a.1 - h.y))
        });
        retire.truncate(excess);
        self.expire_event_members(&retire);
    }
    pub(super) fn retire_musical(&mut self) {
        if let Some(g) = self.st.musical.active.take() {
            self.record_musical(&g);
            self.end_dance(&g);
            self.expire_event_members(&g.members);
        }
        self.st.musical.pending = None;
    }
    fn record_musical(&mut self, g: &Group) {
        if !g.recorded {
            self.rec.formation(FormationRecord {
                def: g.kind.name().into(),
                s_start: g.song,
                s_release: self.st.s,
                members: g.members.iter().map(|&id| id as f64).collect(),
            });
        }
    }
    fn end_dance(&mut self, g: &Group) {
        if let Some(start) = g.dance {
            for &id in &g.members {
                self.rec.dance(
                    id,
                    DanceRecord {
                        w0: start,
                        w1: self.st.w,
                        anim: "kick".into(),
                        alternate: true,
                        phase: 0.0,
                    },
                );
            }
        }
    }
    pub(super) fn tick_musical(&mut self) {
        if !self.musical_enabled() {
            return;
        }
        let main = self.enemy_contact_beat().then(|| {
            self.log.beats[..self.st.ingested.beats]
                .partition_point(|b| b.t <= self.st.s + 1e-9)
                .saturating_sub(1)
        });
        let fresh = main.filter(|b| self.st.musical.last_beat != Some(*b));
        if let Some(bi) = fresh {
            self.st.musical.last_beat = Some(bi);
        }
        self.advance_musical(fresh);
        // Only newly known jumps are candidates; menus/gaps discard old entrance debt.
        while let Some(j) = self
            .log
            .jumps
            .as_deref()
            .unwrap_or(&[])
            .get(self.st.musical.jump)
            .filter(|j| j.known_at <= self.st.s + 1e-9)
            .filter(|j| j.t <= self.st.s + 1e-9)
            .copied()
        {
            self.st.musical.jump += 1;
            if j.db < 6.0 || self.st.s - j.known_at > 0.1 {
                continue;
            }
            let quiet: Vec<_> = self.log.ticks[..self.st.ingested.ticks]
                .iter()
                .filter(|t| t.known_at <= self.st.s && t.t >= j.t - 8.0 && t.t <= j.t)
                .collect();
            if j.t >= 8.0
                && quiet.first().is_some_and(|t| t.t <= j.t - 7.9)
                && quiet
                    .iter()
                    .filter(|t| t.level == Some(Level::Loud))
                    .count()
                    * 20
                    <= quiet.len()
            {
                self.stage(Kind::Stampede);
            }
        }
        if let Some(bi) = fresh {
            let level = self.by_level(self.st.s, &|l| match l {
                Level::Quiet => 0.0,
                Level::Medium => 1.0,
                Level::Loud => 2.0,
            });
            if level > 1.5 {
                self.st.musical.medium = 0;
                if bi % 16 == 0 {
                    self.stage(Kind::Ring);
                }
            } else if level < 0.5 {
                self.st.musical.medium = 0;
                if bi % 16 == 0 {
                    self.stage(Kind::Wedge);
                }
            } else {
                self.st.musical.medium += 1;
                if self.st.musical.medium >= 16 {
                    self.st.musical.medium = 0;
                    self.stage(Kind::Kick);
                }
            }
        }
        let Some(mut p) = self.st.musical.pending.take() else {
            return;
        };
        if self.st.run_s > p.born + 2.0 - 1e-9 || self.musical_busy() {
            return;
        }
        if p.at.is_none() {
            p.at = self.log.beats[..self.st.ingested.beats]
                .iter()
                .find(|b| {
                    b.known_at <= self.st.s
                        && b.t >= self.st.s + 0.35 - 1e-9
                        && b.evidence >= self.env.input.gate
                })
                .map(|b| b.t);
        }
        if let Some(bi) = main.filter(|_| p.at.is_some_and(|s| (s - self.st.s).abs() < 1e-8)) {
            self.start_musical(p.kind, bi);
        } else if p.at.is_none_or(|s| s > self.st.s + 1e-9) {
            self.st.musical.pending = Some(p);
        }
    }
    fn advance_musical(&mut self, main: Option<usize>) {
        let Some(mut g) = self.st.musical.active.take() else {
            return;
        };
        let alive: Vec<_> = g
            .members
            .iter()
            .copied()
            .filter(|&id| self.st.foes.get(id).is_some_and(|f| f.alive))
            .collect();
        let view = self.st.camera.view();
        let crossed = g.kind == Kind::Stampede
            && alive.iter().all(|&id| {
                let f = &self.st.foes[id];
                let p = self.pos(id, self.st.w);
                f.run.is_some_and(|v| {
                    if v.0 > 0.0 {
                        p.0 > view[2] + 64.0
                    } else {
                        p.0 < view[0] - 64.0
                    }
                })
            });
        if alive.is_empty() || crossed || self.st.run_s >= g.until - 1e-9 {
            self.record_musical(&g);
            self.end_dance(&g);
            self.expire_event_members(&g.members);
            return;
        }
        if let Some(bi) = main {
            g.beats += 1;
            let period = self.enemy_beat_period(bi);
            let ready = match (g.kind, g.phase) {
                (Kind::Kick, Phase::Entering) => g.beats >= 8,
                (Kind::Wedge | Kind::Ring, Phase::Entering) => g.beats >= 4,
                (Kind::Kick, Phase::Dancing) => {
                    g.beats >= 32
                        || (g.beats >= 8
                            && self.log.onsets[..self.st.ingested.onsets].iter().any(|o| {
                                o.known_at <= self.st.s
                                    && o.strong
                                    && o.t <= self.st.s
                                    && o.t >= self.st.s - period - 1e-9
                            }))
                }
                (Kind::Wedge, Phase::Dancing) => g.beats >= 6,
                _ => false,
            };
            if ready {
                g.beats = 0;
                if g.phase == Phase::Entering && g.kind != Kind::Ring {
                    g.phase = Phase::Dancing;
                    if g.kind == Kind::Kick {
                        g.dance = Some(self.st.w);
                        for &id in &alive {
                            self.rec.dance_start(id, self.st.s, "kick", true);
                        }
                    }
                } else if g.kind == Kind::Ring {
                    g.phase = Phase::Closing;
                    for &id in &alive {
                        if let Some(path) = self.st.foes[id].ring_path.as_mut() {
                            path.du = 1.0 / (period * 8.0);
                        }
                    }
                    g.until = g.until.min(self.st.run_s + 20.0);
                } else {
                    self.end_dance(&g);
                    g.dance = None;
                    self.record_musical(&g);
                    g.recorded = true;
                    g.phase = Phase::Released;
                    g.until = g.until.min(self.st.run_s + 4.0 * period);
                    for &id in &alive {
                        let f = &mut self.st.foes[id];
                        f.slot = None;
                        f.slot_until = self.st.s;
                        if g.kind == Kind::Kick {
                            f.charge_until = self.st.s + 2.0 * period;
                        }
                    }
                }
            }
        }
        self.st.musical.active = Some(g);
    }
    pub(super) fn start_musical(&mut self, kind: Kind, bi: usize) {
        let Some(sp) = self.env.director.set_pieces.clone() else {
            return;
        };
        if self.st.musical.active.is_some()
            || self.musical_busy()
            || !self.st.spawner.waiting_bosses.is_empty()
        {
            return;
        }
        let lib = std::rc::Rc::clone(&self.env);
        let (role, count) = match kind {
            Kind::Stampede => (sp.stampede.enemy.as_str(), sp.stampede.count as usize),
            Kind::Ring => (sp.ring.enemy.as_str(), sp.ring.count as usize),
            Kind::Kick => {
                let d = &lib.input.lib.formations[sp.kick_line.formation.as_str()];
                (d.enemy.as_str(), d.count as usize)
            }
            Kind::Wedge => {
                let d = &lib.input.lib.formations[sp.wedge.as_str()];
                (d.enemy.as_str(), d.count as usize)
            }
        };
        let count = self.event_room(count.min(GROUP_CAP));
        if count == 0 {
            return;
        }
        let def = lib
            .input
            .lib
            .enemies
            .get_index_of(role)
            .expect("validated musical role");
        let id = self.st.musical.issued;
        self.st.musical.issued += 1;
        let mut rng = Rng::new(&[
            Part::Num(self.env.input.seed),
            Part::Str("musical-piece"),
            Part::Num(id as f64),
        ]);
        let hero = self.hero_pose(self.st.w);
        let view = self.st.camera.view();
        let heading = self.st.heading;
        let anchor = self.formation_anchor();
        let theta = rng.range(0.0, std::f64::consts::TAU);
        let dir = if rng.next() < 0.5 { 1.0 } else { -1.0 };
        let mut members = Vec::new();
        let places = count + 15;
        for k in 0..if kind == Kind::Ring { places } else { count } {
            if kind == Kind::Ring
                && (0..3).any(|gap| (gap * places / 3..gap * places / 3 + 5).contains(&k))
            {
                continue;
            }
            let (x, y, slot, path, run) = match kind {
                Kind::Stampede => {
                    let row = k % 2;
                    let n = count.div_ceil(2) + 3;
                    let mut place = k / 2;
                    let gap = n / 2;
                    if place >= gap {
                        place += 3;
                    }
                    let x = if dir > 0.0 {
                        view[0] - 64.0 - row as f64 * 48.0
                    } else {
                        view[2] + 64.0 + row as f64 * 48.0
                    };
                    (
                        x,
                        view[1] + (view[3] - view[1]) * (place as f64 + 0.5) / n as f64,
                        None,
                        None,
                        Some((dir * lib.input.lib.enemies[def].speed * 2.5, 0.0)),
                    )
                }
                Kind::Ring => {
                    let a = theta + k as f64 * std::f64::consts::TAU / places as f64;
                    let radius = (view[2] - view[0]).hypot(view[3] - view[1]) * 0.6 + 64.0;
                    let path = RingPath {
                        x: hero.x,
                        y: hero.y,
                        c: a.cos(),
                        sn: a.sin(),
                        from: radius,
                        to: 120.0,
                        u: 0.0,
                        du: 0.0,
                    };
                    (
                        hero.x + a.cos() * radius,
                        hero.y + a.sin() * radius,
                        None,
                        Some(path),
                        None,
                    )
                }
                Kind::Kick | Kind::Wedge => {
                    let formation = &lib.input.lib.formations[if kind == Kind::Kick {
                        sp.kick_line.formation.as_str()
                    } else {
                        sp.wedge.as_str()
                    }];
                    let place = if kind == Kind::Kick {
                        SlotPlace::KickLine {
                            d: 170.0 + k as f64 * formation.spacing,
                        }
                    } else {
                        SlotPlace::Wedge {
                            arm: k.div_ceil(2) as f64,
                            side: if k % 2 == 0 { 1.0 } else { -1.0 },
                            spacing: formation.spacing,
                        }
                    };
                    let start = if kind == Kind::Kick {
                        let distance = (view[2] - view[0]) * 0.6 + 64.0 + k as f64 * 6.0;
                        (heading.0 * distance, heading.1 * distance)
                    } else {
                        (k as f64 * 22.0 - 88.0, view[1] - anchor.1 - 80.0)
                    };
                    let slot = FoeSlot {
                        place,
                        dir: heading,
                        start,
                        b_start: bi as f64,
                        entrance_beats: formation.entrance_beats,
                        steps: formation.entrance_beats,
                    };
                    (
                        anchor.0 + start.0,
                        anchor.1 + start.1,
                        Some(slot),
                        None,
                        None,
                    )
                }
            };
            let fi = self.spawn_member(def, x, y, Member::Musical(id));
            let f = &mut self.st.foes[fi];
            f.slot = slot;
            f.ring_path = path;
            f.has_at = path.is_some();
            f.slot_until = f64::INFINITY;
            f.run = run;
            members.push(fi);
        }
        let g = Group {
            kind,
            members,
            song: self.st.s,
            until: self.st.run_s + MAX_LIFE,
            phase: if kind == Kind::Stampede {
                Phase::Released
            } else {
                Phase::Entering
            },
            beats: 0,
            dance: None,
            recorded: kind == Kind::Stampede,
        };
        if kind == Kind::Stampede {
            self.rec.formation(FormationRecord {
                def: kind.name().into(),
                s_start: self.st.s,
                s_release: self.st.s,
                members: g.members.iter().map(|&n| n as f64).collect(),
            });
        }
        self.st.musical.next = self.st.run_s + GROUP_GAP;
        self.st.musical.active = Some(g);
    }
}
