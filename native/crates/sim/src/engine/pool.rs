//! Compact live storage with monotonic recording IDs. Retired IDs are never reused: queued
//! references can be rejected, and public identity, RNG inputs and tie order stay unchanged.
use std::{
    collections::HashMap,
    fmt,
    hash::{BuildHasherDefault, Hasher},
    ops::{Index, IndexMut},
};

#[derive(Default)]
struct IdHasher(u64);
impl Hasher for IdHasher {
    fn finish(&self) -> u64 {
        self.0
    }
    fn write_usize(&mut self, id: usize) {
        self.0 = (id as u64).wrapping_mul(0x9e37_79b9_7f4a_7c15);
    }
    fn write(&mut self, bytes: &[u8]) {
        for byte in bytes {
            self.0 = (self.0 ^ u64::from(*byte)).wrapping_mul(0x100_0000_01b3);
        }
    }
}

#[derive(Clone)]
pub(super) struct Pool<T> {
    next: usize,
    ids: Vec<usize>,
    values: Vec<T>,
    slots: HashMap<usize, usize, BuildHasherDefault<IdHasher>>,
}
impl<T> Default for Pool<T> {
    fn default() -> Self {
        Self {
            next: 0,
            ids: Vec::new(),
            values: Vec::new(),
            slots: HashMap::default(),
        }
    }
}
impl<T: fmt::Debug> fmt::Debug for Pool<T> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_map()
            .entries(self.ids.iter().zip(&self.values))
            .finish()
    }
}
impl<T> Pool<T> {
    /// The next public ID, including retired entries (the old append-only arena's length).
    pub fn len(&self) -> usize {
        self.next
    }
    pub fn stored(&self) -> usize {
        self.values.len()
    }
    pub fn push(&mut self, value: T) {
        self.slots.insert(self.next, self.values.len());
        self.ids.push(self.next);
        self.values.push(value);
        self.next = self.next.checked_add(1).expect("simulation ID overflow");
    }
    pub fn get(&self, id: usize) -> Option<&T> {
        self.slots.get(&id).map(|&slot| &self.values[slot])
    }
    pub fn at_slot_mut(&mut self, slot: usize) -> &mut T {
        &mut self.values[slot]
    }
    pub fn slot(&self, id: usize) -> usize {
        self.slots[&id]
    }
    /// Slots remain stable while the physics pass is borrowing the pool (no retirement/insertion).
    pub fn pair_at_slots_mut(&mut self, a: usize, b: usize) -> (&mut T, &mut T) {
        assert_ne!(a, b);
        if a < b {
            let (left, right) = self.values.split_at_mut(b);
            (&mut left[a], &mut right[0])
        } else {
            let (left, right) = self.values.split_at_mut(a);
            (&mut right[0], &mut left[b])
        }
    }
    pub fn retain(&mut self, mut keep: impl FnMut(usize, &T) -> bool) {
        let mut next_ids = Vec::with_capacity(self.ids.len());
        let mut i = 0;
        self.values.retain(|v| {
            let id = self.ids[i];
            i += 1;
            if keep(id, v) {
                next_ids.push(id);
                true
            } else {
                false
            }
        });
        self.ids = next_ids;
        self.slots.clear();
        self.slots
            .extend(self.ids.iter().enumerate().map(|(slot, &id)| (id, slot)));
        // Bound spare capacity after a large temporary wave as well as logical length.
        let limit = self.values.len().max(32) * 2;
        if self.values.capacity() > limit {
            self.values.shrink_to(limit);
        }
        if self.slots.capacity() > limit {
            self.slots.shrink_to(limit);
        }
    }
    #[cfg(test)]
    pub fn clear(&mut self) {
        *self = Self::default();
    }
    #[cfg(test)]
    pub fn iter(&self) -> std::slice::Iter<'_, T> {
        self.values.iter()
    }
}
impl<T> Index<usize> for Pool<T> {
    type Output = T;
    fn index(&self, id: usize) -> &T {
        &self.values[self.slots[&id]]
    }
}
impl<T> IndexMut<usize> for Pool<T> {
    fn index_mut(&mut self, id: usize) -> &mut T {
        let slot = self.slots[&id];
        &mut self.values[slot]
    }
}
impl<'a, T> IntoIterator for &'a Pool<T> {
    type Item = &'a T;
    type IntoIter = std::slice::Iter<'a, T>;
    fn into_iter(self) -> Self::IntoIter {
        self.values.iter()
    }
}
impl<'a, T> IntoIterator for &'a mut Pool<T> {
    type Item = &'a mut T;
    type IntoIter = std::slice::IterMut<'a, T>;
    fn into_iter(self) -> Self::IntoIter {
        self.values.iter_mut()
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn retirement_never_reuses_identity_and_rollback_restores_it() {
        let mut pool = Pool::default();
        pool.push(10);
        pool.push(20);
        let old = pool.clone();
        pool.retain(|id, _| id == 1);
        pool.push(30);
        assert_eq!(pool.len(), 3);
        assert_eq!(pool.stored(), 2);
        assert_eq!(pool.get(0), None);
        assert_eq!(pool[2], 30);
        assert_eq!(old[0], 10);
        assert_eq!(old.len(), 2);
    }
}
