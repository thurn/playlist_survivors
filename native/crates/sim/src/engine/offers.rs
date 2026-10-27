//! Level-up choices live entirely in rollbackable simulation state.
use super::Sim;
use crate::survivors::{FreezeReason, Item, Offer, offer_table, weapon_catalog};
impl Sim {
    pub(super) fn open_survivor_offer(&mut self) {
        let Some(p) = self.st.survivor.as_ref() else {
            return;
        };
        if p.pending_levels == 0
            || p.offer.is_some()
            || self.st.roulette.is_some_and(|r| r.landed.is_none())
            || self.st.blasting
            || self.st.health <= 0.0
        {
            return;
        }
        let level = p.level - p.pending_levels + 1;
        let cards = offer_table().cards(level, p.arsenal.inventory(), weapon_catalog());
        let mut after = self.st.s;
        let reveals = std::array::from_fn(|_| {
            after = self.log.beats[..self.st.ingested.beats]
                .iter()
                .find(|b| b.known_at <= self.st.s + 1e-9 && b.t > after + 1e-9)
                .map_or(after + self.env.rule.free_period_s, |b| b.t);
            after
        });
        self.st.survivor.as_mut().expect("survivors").offer = Some(Offer {
            level,
            cards,
            reveals,
        });
        self.enter_freeze(FreezeReason::LevelUp);
    }
    pub(super) fn pick_survivor_offer(&mut self) {
        let Some(offer) = self.st.survivor.as_ref().and_then(|p| p.offer) else {
            return;
        };
        if self.st.s + 1e-9 < offer.reveals[2] {
            return;
        }
        let card = offer.cards[self.st.freeze.selected];
        match card.item {
            Item::Weapon(id) => {
                self.st
                    .survivor
                    .as_mut()
                    .expect("survivors")
                    .arsenal
                    .learn(id, self.st.run_s)
                    .expect("offerable weapon");
            }
            Item::Passive(id) => {
                self.st
                    .survivor
                    .as_mut()
                    .expect("survivors")
                    .arsenal
                    .learn_passive(id)
                    .expect("offerable passive");
                self.apply_survivor_stats(id.delta());
            }
            Item::Heal => self.heal(
                self.env
                    .input
                    .level
                    .survivors
                    .expect("survivors")
                    .maxed_heal,
                "maxed-offer",
            ),
        }
        let p = self.st.survivor.as_mut().expect("survivors");
        p.offer = None;
        p.pending_levels -= 1;
        let more = p.pending_levels > 0;
        self.st.freeze.upgrade.pending = true;
        self.st.freeze.confirmed = true;
        self.publish_inventory();
        if more {
            self.open_survivor_offer();
        } else {
            self.st
                .freeze
                .reasons
                .retain(|r| *r != FreezeReason::LevelUp);
            self.request_resume();
        }
    }
}
