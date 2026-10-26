//! Admission uses actual sample durations, including slowed playback and onset lead.
use super::{Voice, VoiceKey};
use std::collections::HashMap;
#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub enum Category {
    Weapons,
    Hits,
    Kills,
    Pickups,
    Ui,
}
impl Category {
    pub fn of(key: &str) -> Self {
        match key {
            "swing" => Self::Weapons,
            "kill" | "kill-multi" => Self::Kills,
            "chime" | "gem" | "chicken" | "vacuum" => Self::Pickups,
            "hurt" => Self::Hits,
            _ if key.starts_with("hit-") || key.ends_with("-hit") => Self::Hits,
            _ if key.starts_with("weapon-") || key.ends_with("-fire") => Self::Weapons,
            _ => Self::Ui,
        }
    }
    pub fn cap(self) -> usize {
        [4, 6, 2, 3, 3][self as usize]
    }
}
#[derive(Clone, Copy)]
pub struct Scheduled {
    pub id: u64,
    pub start: f64,
    pub end: f64,
    pub category: Category,
    pub reconcile: bool,
    pub note: Option<f64>,
}
#[derive(Clone, Default)]
pub struct Budget {
    pub peaks: [usize; 5],
    pub dropped: [u64; 5],
    denied: HashMap<VoiceKey, f64>,
}
impl Budget {
    pub fn admit(
        &mut self,
        key: &VoiceKey,
        voice: &Voice,
        duration: f64,
        scheduled: &HashMap<VoiceKey, Scheduled>,
        now: f64,
    ) -> Option<Scheduled> {
        let category = Category::of(&key.key);
        let end = voice.at + duration / f64::from(voice.rate);
        let overlaps: Vec<_> = scheduled
            .values()
            .filter(|v| v.category == category && v.end > voice.at && v.start < end)
            .collect();
        let max = std::iter::once(voice.at)
            .chain(overlaps.iter().map(|v| v.start.max(voice.at)))
            .map(|t| {
                overlaps
                    .iter()
                    .filter(|v| v.start <= t && v.end > t)
                    .count()
                    + 1
            })
            .max()
            .unwrap_or(1);
        self.denied.retain(|_, t| *t > now - 2.0);
        if max > category.cap() {
            if self.denied.insert(key.clone(), voice.at).is_none() {
                self.dropped[category as usize] += 1;
            }
            return None;
        }
        self.peaks[category as usize] = self.peaks[category as usize].max(max);
        Some(Scheduled {
            id: voice.id,
            start: voice.at,
            end,
            category,
            reconcile: true,
            note: None,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use audio::mixer::Voice;
    fn key(n: u32) -> VoiceKey {
        VoiceKey {
            key: "swing".into(),
            s: 0,
            x: 0,
            n,
            gem: None,
        }
    }
    #[test]
    fn caps_cover_future_overlap_and_slow_sample_tails() {
        let mut budget = Budget::default();
        let mut scheduled = HashMap::new();
        for n in 0..4 {
            let mut v = Voice::sample(n as u64, 0, 1.0, 1.0);
            v.rate = 0.5;
            let slot = budget.admit(&key(n), &v, 1.0, &scheduled, 0.0).unwrap();
            assert_eq!(slot.end, 3.0);
            scheduled.insert(key(n), slot);
        }
        let v = Voice::sample(9, 0, 0.5, 1.0);
        assert!(
            budget.admit(&key(9), &v, 1.0, &scheduled, 0.0).is_none(),
            "must include overlap starting later"
        );
        assert!(budget.admit(&key(9), &v, 1.0, &scheduled, 0.0).is_none());
        assert_eq!(budget.dropped[0], 1, "count a rejected cause once");
        let v = Voice::sample(10, 0, 3.0, 1.0);
        assert!(
            budget.admit(&key(10), &v, 0.2, &scheduled, 0.0).is_some(),
            "ends are exclusive"
        );
        scheduled.remove(&key(0));
        assert!(
            budget
                .admit(
                    &key(9),
                    &Voice::sample(9, 0, 1.0, 1.0),
                    0.5,
                    &scheduled,
                    0.0
                )
                .is_some(),
            "rollback cancellation releases a slot"
        );
        assert_eq!(budget.peaks[0], 4);
    }
}
