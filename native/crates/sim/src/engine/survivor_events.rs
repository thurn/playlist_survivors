//! Source event attempts, unlocked by the first causal loudness jump of their minute.
use super::{Member, Sim};
use crate::core::rng::{Part, Rng};
use crate::recorder::FormationRecord;
use crate::survivors::{SURVIVOR_CAP, SpawnEvent};

#[derive(Clone, Debug)]
struct Armed {
    minute: usize,
    song: f64,
}
#[derive(Clone, Debug)]
struct Attempt {
    minute: usize,
    row: usize,
    ordinal: usize,
    ready: f64,
    song: Option<f64>,
    event: SpawnEvent,
}
#[derive(Clone, Debug)]
struct Group {
    until: f64,
    start: f64,
    wall: bool,
    bar: Option<i64>,
    members: Vec<usize>,
}
#[derive(Clone, Debug, Default)]
pub(super) struct Events {
    next_minute: usize,
    jump: usize,
    next_group: usize,
    waiting: Vec<Armed>,
    pending: Vec<Attempt>,
    groups: Vec<Group>,
    held: bool,
}
impl Sim {
    fn events_active(&self) -> bool {
        self.st.survivor.is_some()
            && !self.st.freeze.frozen
            && !self.st.blasting
            && self.st.health > 0.0
            && self
                .env
                .input
                .level
                .survivors
                .is_some_and(|r| self.st.run_s < r.duration_s)
    }
    fn event_bar(&self) -> i64 {
        let grid_live = self.st.ingested.beats >= 2
            && self.log.beats[self.st.ingested.beats - 1].t
                >= self.st.s - 2.0 * self.log.beats[self.st.ingested.beats - 1].period;
        if grid_live {
            (self.known_beat_at(self.st.s) / 2.0).floor() as i64
        } else {
            (self.st.run_s / (2.0 * self.env.rule.free_period_s)).floor() as i64
        }
    }
    pub(super) fn event_stop(&self) -> f64 {
        if !self.events_active() {
            return f64::INFINITY;
        }
        let state = &self.st.events;
        if state.held {
            return self.st.s;
        }
        let offset = self.st.freeze.total_s;
        let mut next = self
            .env
            .input
            .level
            .survivor_schedule
            .get(state.next_minute)
            .map_or(f64::INFINITY, |r| r.minute as f64 * 60.0 + offset);
        for a in &state.waiting {
            next = next.min(a.minute as f64 * 60.0 + 50.0 + offset);
        }
        // Only the arrival stamp is inspected before an entry becomes known.
        if let Some(j) = self.log.jumps.as_deref().unwrap_or(&[]).get(state.jump) {
            next = next.min(if j.known_at > self.st.s {
                j.known_at
            } else {
                j.known_at.max(j.t)
            });
        }
        for a in &state.pending {
            next = next.min(a.song.unwrap_or(a.ready + offset));
        }
        for g in &state.groups {
            next = next.min(g.until + offset);
        }
        next.max(self.st.s)
    }
    pub(super) fn tick_survivor_events(&mut self) {
        if self.st.survivor.is_none() {
            return;
        }
        if !self.events_active() {
            self.st.events.held = self.st.freeze.frozen;
            return;
        }
        if self.st.events.held {
            for a in &mut self.st.events.pending {
                a.song = None;
            }
            self.st.events.held = false;
        }
        // Expiry owns run time: menus cannot spend a swarm's lifetime.
        let mut groups = std::mem::take(&mut self.st.events.groups);
        groups.retain(|g| {
            if g.until > self.st.run_s + 1e-9 {
                return true;
            }
            self.expire_event_members(&g.members);
            false
        });
        let bar = self.event_bar();
        for g in &mut groups {
            if g.wall && g.bar != Some(bar) {
                g.bar = Some(bar);
                for &id in &g.members {
                    if let Some(f) = self.st.foes.get(id).filter(|f| f.alive) {
                        let speed = self.env.input.lib.enemies[f.def].speed;
                        let f = &mut self.st.foes[id];
                        if let Some(path) = f.ring_path.as_mut() {
                            let distance = path.c.hypot(path.sn) * (path.from - path.to);
                            path.u = ((self.st.run_s - g.start) * speed / distance.max(1e-9))
                                .clamp(path.u, 1.0);
                        }
                    }
                    if self.st.foes.get(id).is_some_and(|f| f.alive) {
                        self.move_pinned(id, self.st.w);
                    }
                }
            }
        }
        self.st.events.groups = groups;
        let schedule = &self.env.input.level.survivor_schedule;
        while let Some(row) = schedule
            .get(self.st.events.next_minute)
            .filter(|row| row.minute as f64 * 60.0 <= self.st.run_s + 1e-9)
        {
            if !row.events.is_empty() {
                self.st.events.waiting.push(Armed {
                    minute: row.minute,
                    song: self.st.s,
                });
            }
            self.st.events.next_minute += 1;
        }
        let mut unlock = Vec::new();
        while let Some(jump) = self
            .log
            .jumps
            .as_deref()
            .unwrap_or(&[])
            .get(self.st.events.jump)
            .copied()
            .filter(|j| j.known_at.max(j.t) <= self.st.s + 1e-9)
        {
            self.st.events.jump += 1;
            if jump.db < 6.0 {
                continue;
            }
            let run = self.world_of(jump.t.min(self.st.s));
            for a in &self.st.events.waiting {
                if jump.known_at >= a.song - 1e-9
                    && run >= a.minute as f64 * 60.0 - 1e-9
                    && run < (a.minute + 1) as f64 * 60.0
                    && !unlock.contains(&a.minute)
                {
                    unlock.push(a.minute);
                }
            }
        }
        for a in &self.st.events.waiting {
            if self.st.run_s >= a.minute as f64 * 60.0 + 50.0 - 1e-9 && !unlock.contains(&a.minute)
            {
                unlock.push(a.minute);
            }
        }
        for minute in unlock {
            self.st.events.waiting.retain(|a| a.minute != minute);
            for (row, event) in self.env.input.level.survivor_schedule[minute]
                .events
                .iter()
                .enumerate()
            {
                let repeats = if event.delay_ms > 0.0 {
                    event.extra_repeats
                } else {
                    0
                };
                // The demo moves the first attempt to the drop. Source delay
                // spaces the extra attempts; zero delay remains one attempt.
                for ordinal in 0..=repeats {
                    self.st.events.pending.push(Attempt {
                        minute,
                        row,
                        ordinal,
                        ready: self.st.run_s + ordinal as f64 * event.delay_ms / 1000.0,
                        song: None,
                        event: event.clone(),
                    });
                }
            }
        }
        let mut pending = std::mem::take(&mut self.st.events.pending);
        for a in &mut pending {
            if a.ready <= self.st.run_s + 1e-9 && a.song.is_none() {
                a.song = Some(
                    self.known_grid_after(self.st.s, 0.5)
                        .map_or(self.st.s, |g| g.1),
                );
            }
        }
        pending.retain(|a| {
            if !a.song.is_some_and(|s| s <= self.st.s + 1e-9) {
                return true;
            }
            self.fire_survivor_event(a);
            false
        });
        self.st.events.pending = pending;
    }
    fn expire_event_members(&mut self, members: &[usize]) {
        for &id in members {
            if self.st.foes.get(id).is_some_and(|f| f.alive) {
                let f = &mut self.st.foes[id];
                f.alive = false;
                f.final_sample = true;
                f.death_w = self.st.w;
                self.rec.death(id, self.st.w, true);
            }
        }
        let foes = &self.st.foes;
        self.st
            .alive
            .retain(|&id| foes.get(id).is_some_and(|f| f.alive));
    }
    fn event_room(&mut self, count: usize) -> usize {
        let hero = self.hero_pose(self.st.w);
        let excess = (self.st.alive.len() + count).saturating_sub(SURVIVOR_CAP);
        let mut far: Vec<_> = self
            .st
            .alive
            .iter()
            .copied()
            .filter(|&id| {
                let f = &self.st.foes[id];
                f.alive && !f.survivor_boss && f.member.is_none()
            })
            .collect();
        far.sort_by(|&a, &b| {
            let a = self.pos(a, self.st.w);
            let b = self.pos(b, self.st.w);
            (b.0 - hero.x)
                .hypot(b.1 - hero.y)
                .total_cmp(&(a.0 - hero.x).hypot(a.1 - hero.y))
        });
        far.truncate(excess);
        self.expire_event_members(&far);
        count.min(SURVIVOR_CAP.saturating_sub(self.st.alive.len()))
    }
    fn fire_survivor_event(&mut self, a: &Attempt) {
        let mut rng = Rng::new(&[
            Part::Num(self.env.input.seed),
            Part::Str("survivor-event"),
            Part::Num(a.minute as f64),
            Part::Num(a.row as f64),
            Part::Num(a.ordinal as f64),
        ]);
        let luck = self
            .st
            .survivor
            .as_ref()
            .expect("survivors")
            .stats
            .finalized()
            .luck;
        if a.event
            .chance
            .is_some_and(|p| p > 0.0 && rng.next() * 100.0 > p / luck)
        {
            return;
        }
        let wall = a.event.kind == "FLOWER_WALL";
        let role = match a.event.kind.as_str() {
            "BAT_SWARM" => "vs-bat-swarm",
            "GHOST_SWARM" => "vs-ghost-swarm",
            "FLOWER_WALL" => "vs-flower",
            _ => return,
        };
        let count = self.event_room(a.event.count);
        if count == 0 {
            return;
        }
        let id = self.st.events.next_group;
        self.st.events.next_group += 1;
        let def = self
            .env
            .input
            .lib
            .enemies
            .get_index_of(role)
            .expect("event role validated");
        let view = self.st.camera.view();
        let (cx, cy) = if wall {
            let h = self.hero_pose(self.st.w);
            (h.x, h.y)
        } else {
            ((view[0] + view[2]) * 0.5, (view[1] + view[3]) * 0.5)
        };
        let (rx, ry) = ((view[2] - view[0]) * 0.72, (view[3] - view[1]) * 0.72);
        let theta = rng.range(0.0, std::f64::consts::TAU);
        let hero = self.hero_pose(self.st.w);
        let (dx, dy) = (
            hero.x - (cx + theta.cos() * rx),
            hero.y - (cy + theta.sin() * ry),
        );
        let distance = dx.hypot(dy).max(1e-9);
        let mut members = Vec::new();
        for i in 0..count {
            let angle = theta
                + if wall {
                    i as f64 * std::f64::consts::TAU / a.event.count as f64
                } else {
                    i as f64 * std::f64::consts::PI / 500.0
                };
            let (c, sn) = (angle.cos() * rx, angle.sin() * ry);
            let foe = self.spawn_member(def, cx + c, cy + sn, Member::SurvivorEvent(id));
            if wall {
                self.set_survivor_wall_path(foe, cx, cy, c, sn, rx.min(ry));
            } else {
                self.st.foes[foe].swarm_dir = Some((dx / distance, dy / distance));
            }
            members.push(foe);
        }
        self.rec.formation(FormationRecord {
            def: format!("survivors-{}", a.event.kind.to_lowercase()),
            s_start: self.st.s,
            s_release: self.st.s,
            members: members.iter().map(|&i| i as f64).collect(),
        });
        let bar = wall.then(|| self.event_bar());
        self.st.events.groups.push(Group {
            until: self.st.run_s + a.event.duration_ms / 1000.0,
            start: self.st.run_s,
            wall,
            bar,
            members,
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::content::level::load_level;
    use crate::content::{SourceFile, load_content};
    use crate::engine::SimInput;
    use crate::recorder::LogRecorder;
    use crate::schedule::reactive_song_map;
    use std::{cell::RefCell, path::Path, rc::Rc};
    use stream::{Beat, Jump, StreamLog, Tick};
    fn sim(minute: usize, jumps: Vec<Jump>) -> (Sim, Rc<RefCell<LogRecorder>>) {
        sim_beats(minute, jumps, None)
    }
    fn sim_beats(
        minute: usize,
        jumps: Vec<Jump>,
        beat_count: Option<usize>,
    ) -> (Sim, Rc<RefCell<LogRecorder>>) {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
        let source = |p: &Path| SourceFile {
            name: p.to_string_lossy().into(),
            text: std::fs::read_to_string(p).unwrap(),
        };
        let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))
            .unwrap()
            .map(|p| p.unwrap().path())
            .filter(|p| p.extension().is_some_and(|e| e == "yaml"))
            .collect();
        paths.sort();
        let lib = load_content(&paths.iter().map(|p| source(p)).collect::<Vec<_>>()).unwrap();
        let mut level = load_level(
            &source(&root.join("levels/survivors.yaml")),
            &lib,
            &reactive_song_map(f64::INFINITY),
            None,
        )
        .unwrap();
        for row in &mut level.survivor_schedule {
            row.minimum = Some(0);
            row.enemies = Some(Vec::new());
            row.bosses.clear();
            if row.minute != minute {
                row.events.clear();
            }
        }
        let mut input = SimInput::new(level, &lib, &indexmap::IndexMap::new(), 0.5, false);
        input.zen = true;
        let end = (minute as u32 + 2) * 60;
        let log = StreamLog {
            beats: (0..end * 2)
                .map(|i| {
                    let t = f64::from(i) * 0.5;
                    Beat {
                        t,
                        known_at: (t - 0.5).max(0.0),
                        period: 0.5,
                        evidence: 1.0,
                    }
                })
                .take(beat_count.unwrap_or(usize::MAX))
                .collect(),
            ticks: (0..end * 20)
                .map(|i| {
                    let t = f64::from(i) * 0.05;
                    Tick {
                        t,
                        known_at: t,
                        energy: 0.5,
                        evidence: 1.0,
                        level: None,
                        key: None,
                        bands: None,
                        brightness: None,
                    }
                })
                .collect(),
            jumps: Some(jumps),
            ..Default::default()
        };
        let rec = Rc::new(RefCell::new(LogRecorder::default()));
        let mut s = Sim::new(input, Rc::new(log), Box::new(Rc::clone(&rec)), None);
        s.disable_hero_damage_for_check();
        s.apply_survivor_stats(crate::survivors::StatDelta {
            might: -1.0,
            area: -1.0,
            ..Default::default()
        });
        (s, rec)
    }
    #[test]
    fn jump_waits_for_its_knowledge_and_scheduled_minute_then_repeats_and_expires() {
        let jumps = vec![
            Jump {
                t: 119.0,
                known_at: 130.0,
                db: 9.0,
            },
            Jump {
                t: 121.0,
                known_at: 131.0,
                db: 5.99,
            },
            Jump {
                t: 123.0,
                known_at: 135.0,
                db: 6.0,
            },
        ];
        let (mut s, rec) = sim(2, jumps);
        s.advance_to(134.99);
        assert!(rec.borrow().formations.is_empty());
        s.advance_to(135.01);
        assert_eq!(rec.borrow().formations[0].s_start, 135.0);
        assert_eq!(rec.borrow().formations[0].members.len(), 50);
        s.advance_to(145.01);
        let r = rec.borrow();
        assert_eq!(
            r.formations.iter().map(|f| f.s_start).collect::<Vec<_>>(),
            [135.0, 140.0, 145.0]
        );
        assert!(r.formations.iter().all(|f| f.members.len() == 50));
        drop(r);
        assert_eq!(
            s.st.events.groups.len(),
            2,
            "first attempt expires after ten run seconds"
        );
        s.advance_to(155.01);
        assert!(s.st.events.groups.is_empty());
        assert!(s.st.alive.is_empty());
    }
    #[test]
    fn no_jump_falls_back_in_the_last_ten_seconds_and_hidden_jumps_cannot_change_the_prefix() {
        let (mut a, ra) = sim(2, Vec::new());
        let (mut b, rb) = sim(
            2,
            vec![Jump {
                t: 121.0,
                known_at: 220.0,
                db: 100.0,
            }],
        );
        a.advance_to(169.99);
        b.advance_to(169.99);
        assert!(ra.borrow().formations.is_empty() && rb.borrow().formations.is_empty());
        assert_eq!(a.st.events.pending.len(), b.st.events.pending.len());
        assert_eq!(a.st.foes.len(), 0);
        assert_eq!(b.st.foes.len(), 0);
        a.advance_to(170.01);
        b.advance_to(170.01);
        assert_eq!(ra.borrow().formations, rb.borrow().formations);
        assert_eq!(ra.borrow().formations[0].s_start, 170.0);
    }
    #[test]
    fn event_snapshot_and_pause_keep_attempts_and_lifetime_in_run_time() {
        use crate::movement::InputKey;
        use crate::player::{KeyEvent, KeyEventKind};
        let jump = Jump {
            t: 130.0,
            known_at: 130.0,
            db: 8.0,
        };
        let (mut a, ra) = sim(2, vec![jump]);
        for (s, kind) in [
            (133.0, KeyEventKind::Press),
            (133.01, KeyEventKind::Release),
            (137.0, KeyEventKind::Press),
            (137.01, KeyEventKind::Release),
        ] {
            a.key(KeyEvent {
                s,
                key: InputKey::Pause,
                kind,
                id: None,
            });
        }
        a.advance_to(130.5);
        let snap = a.snapshot();
        let prefix = ra.borrow().formations.len();
        let rb = Rc::new(RefCell::new(ra.borrow().clone()));
        let (mut b, _) = sim(2, vec![jump]);
        b.restore(&snap);
        b.rec = Box::new(Rc::clone(&rb));
        a.advance_to(147.0);
        for i in 0..=66 {
            b.advance_to(130.5 + f64::from(i) * 0.25);
        }
        assert_eq!(ra.borrow().formations, rb.borrow().formations);
        assert_eq!(ra.borrow().formations.len() - prefix, 2);
        let offset = a.st.freeze.total_s;
        assert!(offset >= 4.0);
        assert!((ra.borrow().formations[prefix].s_start - (135.0 + offset)).abs() < 1e-8);
        assert_eq!(a.st.events.groups.len(), b.st.events.groups.len());
        assert_eq!(a.st.alive.len(), b.st.alive.len());
    }
    #[test]
    fn source_roles_keep_fixed_crossing_heading_and_cap_room_never_discards_bosses() {
        let (mut s, _) = sim(2, Vec::new());
        let role = s.env.input.lib.enemies.get_index_of("vs-bat4").unwrap();
        for _ in 0..300 {
            let id = s.spawn_foe(role, 10000.0, 0.0, None);
            s.mark_survivor_boss(id, true);
        }
        let event = s.env.input.level.survivor_schedule[2].events[0].clone();
        let a = Attempt {
            minute: 2,
            row: 0,
            ordinal: 0,
            ready: 0.0,
            song: Some(0.0),
            event,
        };
        s.fire_survivor_event(&a);
        assert_eq!(s.st.alive.len(), 300);
        assert!(s.st.events.groups.is_empty());
        // Retiring ordinary actors makes room without damaging source holders.
        for i in 0..50 {
            s.st.foes[i].survivor_boss = false;
        }
        s.fire_survivor_event(&a);
        assert_eq!(s.st.alive.len(), 300);
        let members = &s.st.events.groups[0].members;
        assert_eq!(members.len(), 50);
        let dir = s.st.foes[members[0]].swarm_dir.unwrap();
        for &id in members {
            assert_eq!(s.st.foes[id].swarm_dir, Some(dir));
            assert_eq!(s.st.foes[id].health, 0.1);
        }
        assert!(
            s.st.alive
                .iter()
                .all(|&id| s.st.foes[id].survivor_boss || s.st.foes[id].swarm_dir.is_some())
        );
    }
    #[test]
    fn zero_chance_wall_is_unconditional_closes_on_bars_at_source_speed_and_expires() {
        let (mut s, rec) = sim(
            5,
            vec![Jump {
                t: 301.0,
                known_at: 301.0,
                db: 8.0,
            }],
        );
        s.st.survivor.as_mut().unwrap().level = 7;
        s.advance_to(301.01);
        assert_eq!(rec.borrow().formations.len(), 1);
        assert_eq!(rec.borrow().formations[0].members.len(), 100);
        let id = s.st.events.groups[0].members[0];
        assert_eq!(s.st.foes[id].health, 21.0);
        let path = s.st.foes[id].ring_path.unwrap();
        assert_eq!(path.u, 0.0);
        s.advance_to(303.01);
        let next = s.st.foes[id].ring_path.unwrap();
        assert!(next.u > 0.0);
        let speed = s.env.input.lib.enemies[s.st.foes[id].def].speed;
        let travelled = next.c.hypot(next.sn) * (next.from - next.to) * next.u;
        assert!((travelled - 2.0 * speed).abs() < 1e-8);
        let actual = s.pos(id, s.st.w);
        let initial = (path.x + path.c * path.from, path.y + path.sn * path.from);
        assert!(
            ((actual.0 - initial.0).hypot(actual.1 - initial.1) - 2.0 * speed).abs() < 1e-8,
            "actual pinned world position closes at the source average speed"
        );
        s.advance_to(331.01);
        assert!(s.st.events.groups.is_empty());
        assert!(s.st.alive.is_empty());
        assert_eq!(
            rec.borrow().formations.len(),
            1,
            "explicit source chance zero does not reject"
        );
    }
    #[test]
    fn beatless_and_one_beat_swarm_attempts_stay_finite_and_zero_delay_does_not_repeat() {
        for beats in [0, 1] {
            let (mut s, rec) = sim_beats(
                2,
                vec![Jump {
                    t: 130.0,
                    known_at: 130.0,
                    db: 8.0,
                }],
                Some(beats),
            );
            let row = &mut Rc::get_mut(&mut s.env)
                .unwrap()
                .input
                .level
                .survivor_schedule[2]
                .events[0];
            row.delay_ms = 0.0;
            row.extra_repeats = 20;
            s.advance_to(131.0);
            assert_eq!(rec.borrow().formations.len(), 1);
            assert_eq!(s.st.alive.len(), 50);
            for &id in &s.st.alive {
                let f = &s.st.foes[id];
                assert!(f.x.is_finite() && f.y.is_finite() && f.vx.is_finite() && f.vy.is_finite());
            }
            s.advance_to(140.1);
            assert!(s.st.alive.is_empty());
            assert_eq!(
                rec.borrow().formations.len(),
                1,
                "source zero delay means one immediate attempt"
            );
        }
    }
}
