//! Boss attachments, physical chests and a causal, rollbackable roulette.
use super::Sim;
use crate::core::rng::{Part, Rng};
use crate::survivors::{
    ChestEvent, FieldChest, FreezeReason, Item, Roulette, chest_item, chest_items, treasure_tier,
    weapon_catalog,
};
impl Sim {
    pub(super) fn attach_treasure(&mut self, minute: usize) {
        if self.st.treasure_minutes & (1 << minute) != 0 {
            return;
        }
        let Some(row) = self
            .env
            .input
            .level
            .survivor_schedule
            .iter()
            .find(|r| r.minute == minute)
        else {
            return;
        };
        let Some(t) = row.treasure.first() else {
            return;
        };
        let fi = self.st.foes.len() - 1;
        let mut rng = Rng::new(&[
            Part::Num(self.env.input.seed),
            Part::Str("survivor-treasure"),
            Part::Num(minute as f64),
            Part::Num(fi as f64),
        ]);
        if let Some(tier) = treasure_tier(&t.chances, t.tier, &mut rng) {
            self.st.foes[fi].chest_loot = Some(crate::survivors::ChestLoot { tier, minute });
            self.st.treasure_minutes |= 1 << minute;
        }
    }
    pub(super) fn drop_chest(&mut self, x: f64, y: f64, tier: u8, minute: usize) {
        let c = FieldChest {
            id: self.st.n_chests,
            x,
            y,
            tier,
            minute,
        };
        self.st.n_chests += 1;
        self.st.chests.push(c);
        self.rec.chest(self.st.s, ChestEvent::Drop(c));
    }
    pub(super) fn touch_chests(&mut self) {
        if self.st.survivor.is_none()
            || self.st.freeze.frozen
            || self.st.blasting
            || self.st.health <= 0.0
            || self.st.survivor.as_ref().is_some_and(|p| p.offer.is_some())
        {
            return;
        }
        let h = self.hero_pose(self.st.w);
        let Some(i) = self
            .st
            .chests
            .iter()
            .position(|c| (c.x - h.x).hypot(c.y - h.y) <= 22.0)
        else {
            return;
        };
        let c = self.st.chests.remove(i);
        self.rec.chest(self.st.s, ChestEvent::Taken { id: c.id });
        let grid = self
            .known_grid_after(self.st.s, 2.0)
            .filter(|(beat, _)| self.chest_grid_point(*beat).is_some());
        self.st.roulette = Some(Roulette {
            id: c.id,
            tier: c.tier,
            minute: c.minute,
            opened: self.st.s,
            tick: 0,
            started: false,
            next_song: grid.map_or_else(|| self.free_grid_at_or_after(self.st.s, 2.0), |g| g.1),
            next_beat: grid.map(|g| g.0),
            landed: None,
            rewards: [None; 5],
        });
        self.enter_freeze(FreezeReason::Chest);
    }
    fn chest_grid_point(&self, beat: f64) -> Option<f64> {
        let beats = &self.log.beats[..self.st.ingested.beats];
        let index = beat.floor() as usize;
        let left = beats.get(index)?;
        let (at, known) = if beat.fract() == 0.0 {
            (left.t, left.known_at)
        } else {
            let right = beats.get(index + 1)?;
            (
                left.t + (right.t - left.t) * beat.fract(),
                left.known_at.max(right.known_at),
            )
        };
        // next_stop may ingest to a proposed stop before deciding an earlier one.
        // A late commitment must not backdate a tick before either bound was known.
        (at + 1e-9 >= known).then_some(at)
    }
    fn chest_due(&self) -> Option<(Option<f64>, f64)> {
        let r = self.st.roulette.filter(|r| r.landed.is_none())?;
        if self
            .st
            .freeze
            .reasons
            .iter()
            .any(|r| matches!(r, FreezeReason::Paused | FreezeReason::MusicStopped))
        {
            return None;
        }
        if let Some(beat) = r.next_beat
            && let Some(at) = self.chest_grid_point(beat)
        {
            return Some((Some(beat), at.max(self.st.s)));
        }
        // A free pulse is provisional until its deadline. Re-lock to a committed
        // grid when it arrives, including the first/last downbeat of the spin.
        let step = if !r.started || r.tick == 7 { 2.0 } else { 0.5 };
        let due = r.next_song.max(self.st.s);
        self.known_grid_after(due, step)
            .filter(|(beat, _)| self.chest_grid_point(*beat).is_some())
            .map(|(bi, at)| (Some(bi), at))
            .or(Some((None, due)))
    }
    pub(super) fn chest_stop(&self) -> f64 {
        self.chest_due().map_or(f64::INFINITY, |(_, s)| s)
    }
    pub(super) fn tick_chest(&mut self) {
        let Some(mut r) = self.st.roulette else {
            return;
        };
        if let Some(landed) = r.landed {
            if self.st.s >= landed + 1.0 {
                self.st.roulette = None;
            }
            return;
        }
        let Some((bi, due)) = self.chest_due() else {
            r.next_beat = None;
            r.next_song = self.st.s + self.env.rule.free_period_s * 0.5;
            self.st.roulette = Some(r);
            return;
        };
        if due > self.st.s + 1e-9 {
            return;
        }
        let period = bi
            .and_then(|b| self.log.beats.get(b.floor() as usize))
            .map_or(self.env.rule.free_period_s, |b| b.period);
        if r.started {
            r.tick += 1;
        } else {
            r.started = true;
        }
        if r.tick == 8 {
            for slot in r.rewards.iter_mut().take(chest_items(r.tier)) {
                let inv = self
                    .st
                    .survivor
                    .as_ref()
                    .expect("survivors")
                    .arsenal
                    .inventory();
                let item = chest_item(inv, weapon_catalog(), matches!(r.minute, 10 | 15 | 20 | 25));
                self.apply_chest_item(item);
                *slot = Some(item);
            }
            r.landed = Some(self.st.s);
            self.st.roulette = Some(r);
            self.rec.chest(
                self.st.s,
                ChestEvent::Landed {
                    id: r.id,
                    rewards: r.rewards,
                },
            );
            self.st.freeze.reasons.retain(|r| *r != FreezeReason::Chest);
            self.st.freeze.confirmed = true;
            self.publish_inventory();
            self.open_survivor_offer();
            if self.st.freeze.reasons_empty() {
                // The roulette's landing already is the resume grid. A further
                // beat would add an unrequested extra hold after the two-bar spin.
                self.st.freeze.resume = Some(self.st.s);
                self.st.freeze.resume_grid = if bi.is_some() {
                    super::freeze::ResumeGrid::Committed
                } else {
                    super::freeze::ResumeGrid::Free
                };
            }
        } else {
            self.rec.chest(
                self.st.s,
                ChestEvent::Tick {
                    id: r.id,
                    index: r.tick,
                },
            );
            r.next_beat = bi.map(|beat| {
                let next = beat + 0.5;
                if r.tick == 7 {
                    (next / 2.0 - 1e-9).ceil() * 2.0
                } else {
                    next
                }
            });
            let interval = bi.zip(r.next_beat).map_or(0.5, |(from, to)| to - from);
            r.next_song = self.st.s + period * interval;
            self.st.roulette = Some(r);
        }
        self.publish_run();
    }
    fn apply_chest_item(&mut self, item: Item) {
        match item {
            Item::Weapon(id) => {
                if weapon_catalog().get(id).evolution_of.is_some() {
                    self.evolve_survivor_weapon(id).expect("eligible evolution");
                } else {
                    self.st
                        .survivor
                        .as_mut()
                        .expect("survivors")
                        .arsenal
                        .learn(id, self.st.run_s)
                        .expect("owned upgrade");
                }
            }
            Item::Passive(id) => {
                self.st
                    .survivor
                    .as_mut()
                    .expect("survivors")
                    .arsenal
                    .learn_passive(id)
                    .expect("owned upgrade");
                self.apply_survivor_stats(id.delta());
            }
            Item::Heal => self.heal(
                self.env
                    .input
                    .level
                    .survivors
                    .expect("survivors")
                    .maxed_heal,
                "chest",
            ),
        }
    }
}
