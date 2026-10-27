//! Clock-driven population; music supplies only the arrival grid and existing placement.
use super::Sim;
use crate::core::rng::{Part, Rng};
use crate::survivors::SURVIVOR_CAP;
use std::rc::Rc;
impl Sim {
    fn spawning_active(&self) -> bool {
        self.st.survivor.is_some()
            && !self.st.freeze.frozen
            && !self.st.blasting
            && self
                .env
                .input
                .level
                .survivors
                .is_some_and(|s| self.st.run_s < s.duration_s)
    }
    pub(super) fn spawn_stop(&self) -> f64 {
        if !self.spawning_active() {
            return f64::INFINITY;
        }
        self.st.spawner.next_stop(
            &self.env.input.level.survivor_schedule,
            self.st.s,
            self.st.freeze.total_s,
        )
    }
    fn alive_count(&self) -> usize {
        self.st
            .alive
            .iter()
            .filter(|&&i| self.st.foes[i].alive)
            .count()
    }
    fn spawn_role(&mut self, role: &str, boss: bool) {
        let env = Rc::clone(&self.env);
        let def = env
            .input
            .lib
            .enemies
            .get_index_of(role)
            .expect("validated role");
        let mut rng = Rng::new(&[
            Part::Num(env.input.seed),
            Part::Str("survivor-spawn"),
            Part::Num(self.st.spawner.spawned as f64),
        ]);
        let (mut x, mut y) = self.spawn_point(&env.input.lib.enemies[def], &mut rng, false);
        if boss {
            // Land visibly at the edge after the notice. Keep the seeded approach direction
            // and a full screen edge of notice/distance, without a hidden hittable descent.
            let v = self.st.camera.view();
            let (hx, up, down) = super::foes::sprite_extents(&env.input.lib.enemies[def]);
            let (cx, cy) = ((v[0] + v[2]) * 0.5, (v[1] + v[3]) * 0.5);
            let (dx, dy) = (x - cx, y - cy);
            let half_width = ((v[2] - v[0]) * 0.5 - hx - 12.0).max(1.0);
            let half_height =
                ((v[3] - v[1]) * 0.5 - if dy < 0.0 { up } else { down } - 12.0).max(1.0);
            let factor = (half_width / dx.abs().max(1e-9))
                .min(half_height / dy.abs().max(1e-9))
                .min(1.0);
            x = cx + dx * factor;
            y = cy + dy * factor;
        }
        let health = boss.then(|| {
            env.input.lib.enemies[def].health
                * self.st.survivor.as_ref().expect("survivors").level as f64
        });
        let id = self.spawn_foe(def, x, y, health);
        self.mark_survivor_boss(id, boss);
        if boss {
            self.boss_cue(crate::survivors::BossCueKind::Landed, id, x, y);
        }
        self.st.spawner.spawned += 1;
    }
    pub(super) fn tick_spawner(&mut self) {
        if !self.spawning_active() {
            return;
        }
        let beats = &self.log.beats[..self.st.ingested.beats];
        self.st.spawner.resume(self.st.run_s, self.st.s, beats);
        self.st.spawner.prepare(
            &self.env.input.level.survivor_schedule,
            self.st.run_s,
            self.st.s,
            beats,
        );
        let batches = self.st.spawner.take_due(self.st.s);
        for batch in &batches {
            self.st
                .spawner
                .waiting_bosses
                .extend(batch.bosses.iter().cloned());
        }
        let room = self.alive_count() < SURVIVOR_CAP;
        if self
            .st
            .spawner
            .bosses_due(self.st.s, &self.log.beats[..self.st.ingested.beats], room)
        {
            let mut bosses = std::mem::take(&mut self.st.spawner.waiting_bosses).into_iter();
            while self.alive_count() < SURVIVOR_CAP {
                let Some(role) = bosses.next() else {
                    break;
                };
                self.spawn_role(&role.role, true);
                self.attach_treasure(role.minute);
            }
            self.st.spawner.waiting_bosses.extend(bosses);
        }
        // Reserve open slots while a held boss waits for its grid step. A regular refill
        // must not continually take the slot back before the boss can arrive.
        let regular_cap = SURVIVOR_CAP.saturating_sub(self.st.spawner.waiting_bosses.len());
        for batch in batches {
            if batch.roster.enemies.is_empty() {
                continue;
            }
            loop {
                for role in &batch.roster.enemies {
                    if self.alive_count() >= regular_cap {
                        break;
                    }
                    self.spawn_role(role, false);
                }
                if self.alive_count() >= batch.roster.minimum.min(regular_cap) {
                    break;
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::content::level::load_level;
    use crate::content::{SourceFile, load_content};
    use crate::core::geometry::Shape;
    use crate::engine::{SimInput, combat::HitSpec};
    use crate::recorder::{HitSource, NullRecorder, Weight};
    use crate::schedule::reactive_song_map;
    use crate::survivors::ChestLoot;
    use std::path::Path;
    use stream::StreamLog;

    fn sim() -> Sim {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../..");
        let source = |path: &Path| SourceFile {
            name: path.to_string_lossy().into(),
            text: std::fs::read_to_string(path).unwrap(),
        };
        let mut paths: Vec<_> = std::fs::read_dir(root.join("content"))
            .unwrap()
            .map(|e| e.unwrap().path())
            .filter(|p| p.extension().is_some_and(|x| x == "yaml"))
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
        // Isolate scheduled holders, leaving exact boss and treasure rows intact.
        for row in &mut level.survivor_schedule {
            row.minimum = Some(0);
            row.enemies = Some(Vec::new());
            row.events.clear();
        }
        let mut input = SimInput::new(level, &lib, &indexmap::IndexMap::new(), 0.5, false);
        input.zen = true;
        Sim::new(
            input,
            Rc::new(StreamLog::default()),
            Box::new(NullRecorder),
            None,
        )
    }

    #[test]
    fn big_four_spawn_on_their_minutes_scale_actual_hp_and_drop_scripted_tiers() {
        for level in [1, 7, 35] {
            for (minute, role, raw_hp, tier) in [
                (10, "vs-boss-xlmantis", 15.0, 1),
                (15, "vs-boss-werewolf", 20.0, 1),
                (20, "vs-boss-xlmummy", 25.0, 1),
                (25, "vs-boss-xlflower", 15.0, 3),
            ] {
                let mut s = sim();
                s.st.survivor.as_mut().unwrap().level = level;
                s.st.spawner.minute = Some(minute - 1);
                let at = minute as f64 * 60.0;
                s.st.s = at - 0.01;
                s.st.w = s.st.s;
                s.st.run_s = s.st.s;
                s.tick_spawner();
                assert_eq!(s.st.foes.len(), 0, "{role} spawned before its minute");
                s.st.s = at;
                s.st.w = at;
                s.st.run_s = at;
                s.tick_spawner();
                assert_eq!(s.st.foes.len(), 1);
                let f = &s.st.foes[0];
                assert_eq!(s.env.input.lib.enemies[f.def].id, role);
                assert!(f.survivor_boss);
                assert_eq!(f.health, raw_hp * level as f64);
                assert_eq!(f.chest_loot, Some(ChestLoot { tier, minute }));
                let h = s.add_hit(
                    HitSpec {
                        source: HitSource::Weapon,
                        what: "sword-spin".into(),
                        weight: Weight::Heavy,
                        primary: true,
                        shape: Shape::Circle {
                            x: 0.0,
                            y: 0.0,
                            radius: 1.0,
                        },
                        dir: 0.0,
                        accent: 0.0,
                        damage: raw_hp * level as f64 + 1.0,
                        group: None,
                    },
                    Some(0.5),
                );
                s.damage(0, &h);
                assert!(!s.st.foes[0].alive);
                assert_eq!(s.st.chests.len(), 1);
                assert_eq!((s.st.chests[0].tier, s.st.chests[0].minute), (tier, minute));
                // A second lethal delivery cannot duplicate a holder's chest.
                s.damage(0, &h);
                s.tick_spawner();
                assert_eq!(s.st.chests.len(), 1);
                assert_eq!(s.st.foes.len(), 1);
                eprintln!(
                    "SW4 minute{minute} {role} level{level}: HP{} chest tier{tier}, one spawn/drop",
                    raw_hp * level as f64
                );
            }
        }
    }
    #[test]
    fn boss_showpiece_warns_two_bars_before_visible_downbeat_landing_and_holds_on_freeze() {
        let mut s = sim();
        s.log = Rc::new(StreamLog {
            beats: (0..130)
                .map(|i| stream::Beat {
                    t: f64::from(i) * 0.5,
                    known_at: (f64::from(i) * 0.5 - 0.5).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                })
                .collect(),
            ..Default::default()
        });
        s.st.ingested.beats = s.log.beats.len(); // Deliberately include unknown future observations.
        s.st.s = 57.0;
        s.st.run_s = 57.0;
        assert_eq!(s.boss_notice_stop(), 58.0);
        s.tick_boss_notice();
        assert!(s.st.boss_presentation.warned.is_empty());
        s.st.s = 58.0;
        s.st.run_s = 58.0;
        s.tick_boss_notice();
        assert_eq!(s.st.boss_presentation.warned, vec![1]);
        assert_eq!(s.st.boss_presentation.warning_run, Some(58.0));
        s.st.freeze.frozen = true;
        assert!(s.boss_notice_stop().is_infinite());
        s.st.freeze.frozen = false;
        s.st.spawner.minute = Some(0);
        s.st.s = 60.0;
        s.st.run_s = 60.0;
        s.tick_spawner();
        assert!(s.st.foes.iter().any(|f| f.survivor_boss));
        let v = s.st.camera.view();
        for f in s.st.foes.iter().filter(|f| f.survivor_boss) {
            assert!(f.x >= v[0] && f.x <= v[2] && f.y >= v[1] && f.y <= v[3]);
        }
        assert_eq!(s.st.boss_presentation.landed_run, Some(60.0));
    }
    #[test]
    fn partial_boss_admission_keeps_notice_and_camera_until_last_held_boss_lands() {
        let mut s = sim();
        Rc::get_mut(&mut s.env)
            .unwrap()
            .input
            .level
            .survivor_schedule[1]
            .bosses = vec!["vs-bat4".into(), "vs-bat4".into()];
        s.log = Rc::new(StreamLog {
            beats: (0..130)
                .map(|i| stream::Beat {
                    t: f64::from(i) * 0.5,
                    known_at: (f64::from(i) * 0.5 - 0.5).max(0.0),
                    period: 0.5,
                    evidence: 1.0,
                })
                .collect(),
            ..Default::default()
        });
        s.st.ingested.beats = s.log.beats.len();
        s.st.s = 58.0;
        s.st.run_s = 58.0;
        s.tick_boss_notice();
        let def = s.env.input.lib.enemies.get_index_of("vs-bat1").unwrap();
        for _ in 0..SURVIVOR_CAP - 1 {
            s.spawn_foe(def, 5000.0, 5000.0, Some(1.0));
        }
        s.st.spawner.minute = Some(0);
        s.st.s = 60.0;
        s.st.run_s = 60.0;
        s.tick_spawner();
        assert_eq!(s.st.boss_presentation.pending, 1);
        assert_eq!(s.st.spawner.waiting_bosses.len(), 1);
        assert!(s.st.boss_presentation.landed_run.is_none());
        s.st.foes[0].alive = false;
        s.st.s = 60.5;
        s.st.run_s = 60.5;
        s.tick_spawner();
        assert_eq!(s.st.boss_presentation.pending, 1);
        s.st.s = 61.0;
        s.st.run_s = 61.0;
        s.tick_spawner();
        assert_eq!(s.st.boss_presentation.pending, 0);
        assert_eq!(s.st.boss_presentation.landed_run, Some(61.0));
    }
    #[test]
    fn deferred_boss_death_keeps_identity_after_freeze_and_actual_pool_retirement() {
        use crate::{
            engine::Ev,
            recorder::LogRecorder,
            survivors::{BossCueKind, FreezeReason},
        };
        use std::cell::RefCell;
        let mut s = sim();
        let log = Rc::new(RefCell::new(LogRecorder::default()));
        s.rec = Box::new(log.clone());
        s.st.s = 0.24;
        s.st.run_s = 0.24;
        s.st.w = 0.24;
        let def = s.env.input.lib.enemies.get_index_of("vs-bat4").unwrap();
        let id = s.spawn_foe(def, 0.0, 0.0, Some(1.0));
        s.mark_survivor_boss(id, true);
        let hit = s.add_hit(
            HitSpec {
                source: HitSource::Weapon,
                what: "sword-spin".into(),
                weight: Weight::Heavy,
                primary: true,
                shape: Shape::Circle {
                    x: 0.0,
                    y: 0.0,
                    radius: 1.0,
                },
                dir: 0.0,
                accent: 0.0,
                damage: 1000.0,
                group: None,
            },
            Some(0.5),
        );
        s.damage(id, &hit);
        assert!(!s.st.foes[id].alive);
        s.enter_freeze(FreezeReason::Paused);
        assert!(s.st.freeze.frozen);
        assert!(
            !s.st
                .q
                .heap
                .iter()
                .any(|e| matches!(e.ev, Ev::EnemyImpact { .. })),
            "impact held outside the active queue"
        );
        s.st.foe_samples.clear(); // Its final published sample is no longer retained.
        s.trim_state();
        assert!(s.st.foes.get(id).is_none());
        s.st.s = 2.0;
        s.st.freeze.reasons.clear();
        s.st.freeze.resume = Some(2.0);
        s.maybe_resume();
        let entry =
            s.st.q
                .heap
                .iter()
                .find(|e| matches!(e.ev, Ev::EnemyImpact { .. }))
                .unwrap()
                .clone();
        s.st.s = entry.key.s;
        s.dispatch_entry(entry);
        let log = log.borrow();
        assert_eq!(log.boss_cues.len(), 1);
        assert_eq!(log.boss_cues[0].kind, BossCueKind::Defeated);
        assert_eq!(log.boss_cues[0].id, id);
        assert!(log.boss_cues[0].song >= 2.0);
    }
}
