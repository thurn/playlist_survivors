//! Append-heavy history shared by rollback snapshots. Sealed pages are immutable; a write
//! copies only the page index and the touched page. Slice views are lazy and never cloned.
use std::{
    cell::OnceCell,
    fmt,
    ops::{Deref, Index, IndexMut},
    rc::Rc,
};

const PAGE: usize = 256;

pub struct SharedVec<T> {
    pages: Rc<Vec<Rc<Vec<T>>>>,
    len: usize,
    flat: OnceCell<Vec<T>>,
}
impl<T> Default for SharedVec<T> {
    fn default() -> Self {
        Self {
            pages: Rc::new(Vec::new()),
            len: 0,
            flat: OnceCell::new(),
        }
    }
}
impl<T> Clone for SharedVec<T> {
    fn clone(&self) -> Self {
        Self {
            pages: Rc::clone(&self.pages),
            len: self.len,
            flat: OnceCell::new(),
        }
    }
}
impl<T> SharedVec<T> {
    pub fn new() -> Self {
        Self::default()
    }
    pub fn len(&self) -> usize {
        self.len
    }
    pub fn is_empty(&self) -> bool {
        self.len == 0
    }
    pub fn get(&self, i: usize) -> Option<&T> {
        (i < self.len).then(|| &self.pages[i / PAGE][i % PAGE])
    }
    pub fn last(&self) -> Option<&T> {
        self.len.checked_sub(1).and_then(|i| self.get(i))
    }
    pub fn iter(&self) -> impl DoubleEndedIterator<Item = &T> {
        self.pages.iter().flat_map(|p| p.iter())
    }
    /// Storage in the page index (history payload is shared). Useful for accounting, not a claim
    /// that the complete retained history is constant-size.
    pub fn index_bytes(&self) -> usize {
        self.pages.len() * size_of::<Rc<Vec<T>>>()
    }
}
impl<T: Clone> SharedVec<T> {
    pub fn push(&mut self, item: T) {
        self.flat.take();
        let pages = Rc::make_mut(&mut self.pages);
        if self.len.is_multiple_of(PAGE) {
            pages.push(Rc::new(Vec::with_capacity(PAGE)));
        }
        Rc::make_mut(pages.last_mut().expect("page")).push(item);
        self.len += 1;
    }
    pub fn truncate(&mut self, len: usize) {
        if len >= self.len {
            return;
        }
        self.flat.take();
        let pages = Rc::make_mut(&mut self.pages);
        pages.truncate(len.div_ceil(PAGE));
        if !len.is_multiple_of(PAGE) {
            Rc::make_mut(pages.last_mut().expect("page")).truncate(len % PAGE);
        }
        self.len = len;
    }
    pub fn get_mut(&mut self, i: usize) -> Option<&mut T> {
        if i >= self.len {
            return None;
        }
        self.flat.take();
        Some(&mut Rc::make_mut(&mut Rc::make_mut(&mut self.pages)[i / PAGE])[i % PAGE])
    }
    pub fn last_mut(&mut self) -> Option<&mut T> {
        self.len.checked_sub(1).and_then(|i| self.get_mut(i))
    }
    pub fn clear(&mut self) {
        *self = Self::new();
    }
}
impl<T: Clone> Extend<T> for SharedVec<T> {
    fn extend<I: IntoIterator<Item = T>>(&mut self, iter: I) {
        for item in iter {
            self.push(item);
        }
    }
}
impl<T: Clone> FromIterator<T> for SharedVec<T> {
    fn from_iter<I: IntoIterator<Item = T>>(iter: I) -> Self {
        let mut out = Self::new();
        out.extend(iter);
        out
    }
}
impl<T: Clone> From<Vec<T>> for SharedVec<T> {
    fn from(v: Vec<T>) -> Self {
        v.into_iter().collect()
    }
}
impl<T> Index<usize> for SharedVec<T> {
    type Output = T;
    fn index(&self, i: usize) -> &T {
        self.get(i).expect("history index out of bounds")
    }
}
impl<T: Clone> IndexMut<usize> for SharedVec<T> {
    fn index_mut(&mut self, i: usize) -> &mut T {
        self.get_mut(i).expect("history index out of bounds")
    }
}
impl<T: Clone> Deref for SharedVec<T> {
    type Target = [T];
    fn deref(&self) -> &[T] {
        self.flat.get_or_init(|| self.iter().cloned().collect())
    }
}
impl<T: fmt::Debug> fmt::Debug for SharedVec<T> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_list().entries(self.iter()).finish()
    }
}
impl<T: PartialEq> PartialEq for SharedVec<T> {
    fn eq(&self, rhs: &Self) -> bool {
        self.len == rhs.len && self.iter().eq(rhs.iter())
    }
}
impl<T: Clone> PartialEq<Vec<T>> for SharedVec<T>
where
    T: PartialEq,
{
    fn eq(&self, rhs: &Vec<T>) -> bool {
        self.len == rhs.len() && self.iter().eq(rhs.iter())
    }
}
impl<'a, T> IntoIterator for &'a SharedVec<T> {
    type Item = &'a T;
    type IntoIter = std::iter::FlatMap<
        std::slice::Iter<'a, Rc<Vec<T>>>,
        std::slice::Iter<'a, T>,
        fn(&'a Rc<Vec<T>>) -> std::slice::Iter<'a, T>,
    >;
    fn into_iter(self) -> Self::IntoIter {
        self.pages.iter().flat_map(|p| p.iter())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn page_boundaries_restore_and_branch_without_changing_old_snapshots() {
        for n in [0, 1, PAGE - 1, PAGE, PAGE + 1, 5 * PAGE] {
            let mut live: SharedVec<_> = (0..n).collect();
            let old = live.clone();
            assert!(Rc::ptr_eq(&live.pages, &old.pages));
            live.push(9000);
            if n > PAGE {
                assert!(Rc::ptr_eq(&live.pages[0], &old.pages[0]));
            }
            live.truncate(n / 2);
            live.extend([8000, 8001]);
            if n > 2 {
                live[0] = 7000;
            }
            assert_eq!(
                old.iter().copied().collect::<Vec<_>>(),
                (0..n).collect::<Vec<_>>()
            );
            assert_eq!(live.len(), n / 2 + 2);
            assert_eq!(&live[live.len() - 2..], &[8000, 8001]);
            assert!(old.flat.get().is_none());
            let snap = live.clone();
            assert!(
                snap.flat.get().is_none(),
                "slice caches never enter snapshots"
            );
        }
    }
}

macro_rules! range_index {
    ($($r:ty),*) => { $(impl<T: Clone> Index<$r> for SharedVec<T> {
        type Output = [T];
        fn index(&self, r: $r) -> &[T] { &self.deref()[r] }
    })* };
}
range_index!(
    std::ops::Range<usize>,
    std::ops::RangeFrom<usize>,
    std::ops::RangeTo<usize>,
    std::ops::RangeFull,
    std::ops::RangeInclusive<usize>,
    std::ops::RangeToInclusive<usize>
);
