//! Bounded memoization of the pure separation pass across rollback. A digest is only a
//! lookup hint: all input words and the bin origin must match exactly before reusing output.
use std::collections::VecDeque;
#[derive(Default)]
pub(super) struct Cache {
    entries: VecDeque<Entry>,
    pub hits: u64,
    pub misses: u64,
    #[cfg(test)]
    pub disabled: bool,
}
struct Entry {
    digest: u64,
    origin: (i64, i64),
    input: Vec<[u64; 4]>,
    output: Vec<[f64; 4]>,
}
fn digest(input: &[[u64; 4]]) -> u64 {
    input.iter().flatten().fold(0xcbf2_9ce4_8422_2325, |h, &v| {
        (h ^ v).rotate_left(9).wrapping_mul(0x100_0000_01b3)
    })
}
impl Cache {
    pub fn lookup(&mut self, origin: (i64, i64), input: &[[u64; 4]]) -> Option<&[[f64; 4]]> {
        #[cfg(test)]
        if self.disabled {
            return None;
        }
        let d = digest(input);
        if let Some(entry) = self
            .entries
            .iter()
            .rev()
            .find(|e| e.digest == d && e.origin == origin && e.input == input)
        {
            self.hits += 1;
            Some(&entry.output)
        } else {
            self.misses += 1;
            None
        }
    }
    pub fn insert(&mut self, origin: (i64, i64), input: Vec<[u64; 4]>, output: Vec<[f64; 4]>) {
        #[cfg(test)]
        if self.disabled {
            return;
        }
        if self.entries.len() == 64 {
            self.entries.pop_front();
        }
        self.entries.push_back(Entry {
            digest: digest(&input),
            origin,
            input,
            output,
        });
    }
    pub fn bytes(&self) -> usize {
        self.entries
            .iter()
            .map(|e| (e.input.len() + e.output.len()) * size_of::<[u64; 4]>())
            .sum()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn every_input_word_and_origin_are_verified_even_on_a_digest_collision() {
        let mut cache = Cache::default();
        let input = vec![[1, 2, 3, 4]];
        cache.insert((0, 0), input.clone(), vec![[5.0, 6.0, 7.0, 8.0]]);
        assert_eq!(
            cache.lookup((0, 0), &input),
            Some(&[[5.0, 6.0, 7.0, 8.0]][..])
        );
        assert!(cache.lookup((1, 0), &input).is_none());
        for word in 0..4 {
            let mut altered = input.clone();
            altered[0][word] += 1;
            cache.entries[0].digest = digest(&altered); // Deliberately force the lookup hint to collide.
            assert!(cache.lookup((0, 0), &altered).is_none());
        }
        for i in 0..100 {
            cache.insert((i, 0), input.clone(), vec![[0.0; 4]]);
        }
        assert_eq!(cache.entries.len(), 64);
        assert_eq!(cache.bytes(), 64 * 64);
    }
}
