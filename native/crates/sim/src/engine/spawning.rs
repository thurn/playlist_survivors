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
        let (x, y) = self.spawn_point(&env.input.lib.enemies[def], &mut rng, false);
        let health = boss.then(|| {
            env.input.lib.enemies[def].health
                * self.st.survivor.as_ref().expect("survivors").level as f64
        });
        let id = self.spawn_foe(def, x, y, health);
        self.mark_survivor_boss(id, boss);
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
}
