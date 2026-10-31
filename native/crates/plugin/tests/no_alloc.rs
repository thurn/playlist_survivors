//! `ps_render_music` allocates nothing: a counting allocator watches the rendering thread through
//! a whole song (its start, a minute of the follower's history, and past its end) while the
//! analyzer runs.

mod common;

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;
use std::sync::atomic::{AtomicUsize, Ordering};

use common::{native, open, status, wait_for_music};
use ps_plugin::decode::SR;
use ps_plugin::{PS_ENDED, PS_OK, PS_WAITING};

struct Counting;

thread_local! {
    static WATCHED: Cell<bool> = const { Cell::new(false) };
}
static ALLOCATIONS: AtomicUsize = AtomicUsize::new(0);

fn count() {
    if WATCHED.try_with(Cell::get).unwrap_or(false) {
        ALLOCATIONS.fetch_add(1, Ordering::Relaxed);
    }
}

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        count();
        unsafe { System.alloc(layout) }
    }
    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        count();
        unsafe { System.alloc_zeroed(layout) }
    }
    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        count();
        unsafe { System.realloc(ptr, layout, new_size) }
    }
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        count();
        unsafe { System.dealloc(ptr, layout) }
    }
}

#[global_allocator]
static GLOBAL: Counting = Counting;

#[test]
fn rendering_allocates_nothing() {
    const FRAMES: usize = 256;
    const RATE: f64 = 48_000.0;
    let song = native().join("../public/reactive/house-128/house-128.ogg");
    let total = ps_plugin::decode::decode(&song).unwrap().mono.len() as f64 / SR;
    let s = open(&song, RATE, None).unwrap();
    let mut out = vec![0.0f32; 2 * FRAMES];
    let mut buffers = 0u64;
    // (62.5 s of music, then silence past its end.)
    while status(s).state != PS_ENDED || buffers < 70 * RATE as u64 / FRAMES as u64 {
        // (The next buffer's song time is the last one's and its length.)
        let st = status(s);
        let next = if st.state == PS_WAITING {
            0.0
        } else {
            st.anchor_song + FRAMES as f64 / RATE
        };
        wait_for_music(s, next, FRAMES as f64 / RATE, total);
        WATCHED.with(|w| w.set(true));
        let code = unsafe {
            ps_plugin::ps_render_music(s, out.as_mut_ptr(), FRAMES as i32, 2, buffers as f64)
        };
        WATCHED.with(|w| w.set(false));
        assert_eq!(code, PS_OK);
        buffers += 1;
    }
    let stalls = status(s).stalls;
    unsafe { ps_plugin::ps_close(s) };
    assert_eq!(stalls, 0);
    assert_eq!(ALLOCATIONS.load(Ordering::Relaxed), 0);
}
