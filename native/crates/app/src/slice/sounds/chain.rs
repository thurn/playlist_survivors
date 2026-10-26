//! Replay speculative gem notes from a settled prefix; rollbacks cannot advance it twice.
use eval::live::{Cause, Caused};
#[derive(Clone, Copy, Default)]
struct State {
    last: Option<f64>,
    step: usize,
}
impl State {
    fn next(&mut self, s: f64) -> f64 {
        self.step = if self.last.is_some_and(|last| s - last <= 0.3 + 1e-9) {
            (self.step + 1).min(10)
        } else {
            0
        };
        self.last = Some(s);
        // C6 through C8: two octaves, holding the top note.
        f64::from([84, 86, 88, 91, 93, 96, 98, 100, 103, 105, 108][self.step])
    }
}
#[derive(Clone, Default)]
pub struct Chain {
    prefix: State,
    pending: Vec<(f64, usize)>,
}
impl Chain {
    pub fn notes(&mut self, caused: &[Caused], final_before: f64) -> Vec<(usize, f64, f64)> {
        for &(s, _) in &self.pending {
            if s <= final_before {
                self.prefix.next(s);
            }
        }
        self.pending = caused
            .iter()
            .filter_map(|c| match c.cause {
                Cause::Gem { s, gem } => Some((s, gem)),
                _ => None,
            })
            .collect();
        self.pending
            .sort_by(|a, b| a.0.total_cmp(&b.0).then(a.1.cmp(&b.1)));
        self.pending.dedup();
        let mut state = self.prefix;
        self.pending
            .iter()
            .map(|&(s, gem)| (gem, s, state.next(s)))
            .collect()
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    fn gem(s: f64, gem: usize) -> Caused {
        Caused {
            cause: Cause::Gem { s, gem },
            known: s - 0.1,
        }
    }
    #[test]
    fn chain_is_pentatonic_bounded_resets_and_rewinds() {
        let mut c = Chain::default();
        let all: Vec<_> = (0..15).map(|i| gem(i as f64 * 0.1, i)).collect();
        let notes = c.notes(&all, -1.0);
        assert_eq!(
            notes
                .iter()
                .take(11)
                .map(|n| n.2 as i32)
                .collect::<Vec<_>>(),
            vec![84, 86, 88, 91, 93, 96, 98, 100, 103, 105, 108]
        );
        assert_eq!(notes[14].2, 108.0);
        assert_eq!(
            c.notes(&all[..2], -1.0)[1].2,
            86.0,
            "rollback restores pitch"
        );
        assert_eq!(
            c.notes(&all[..2], -1.0)[1].2,
            86.0,
            "repeat does not advance"
        );
        // First two gems settle out of the window; the next stays on the chain.
        assert_eq!(c.notes(&[gem(0.2, 2)], 0.15)[0].2, 88.0);
        assert_eq!(c.notes(&[gem(0.6, 3)], 0.25)[0].2, 84.0, "a pause resets");
    }
}
