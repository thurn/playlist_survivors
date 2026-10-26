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
        self.st.foes[id].survivor_boss = boss;
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
                self.spawn_role(&role, true);
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
