//! `ps_render_music` allocates nothing: a counting allocator watches the rendering thread through
//! a whole song (its start, a minute of the follower's history, and past its end) while the
//! analyzer runs, and through a System source's start, release and resumption.

mod common;

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;
use std::sync::atomic::{AtomicUsize, Ordering};

use common::{Fake, hang_guard, native, open, open_source, status, wait_for_music};
use ps_plugin::decode::SR;
use ps_plugin::{PS_ENDED, PS_OK, PS_PLAYING, PS_STOPPED, PS_WAITING, Session};

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
    let total = ps_plugin::decode::decode(&song, RATE).unwrap().mono.len() as f64 / SR;
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

/// Renders the next buffer, watched, counting it in `rendered`.
fn render(s: *mut Session, out: &mut [f32], rate: f64, rendered: &mut u64) {
    let dsp = *rendered as f64 * (out.len() / 2) as f64 / rate;
    *rendered += 1;
    WATCHED.with(|w| w.set(true));
    let code =
        unsafe { ps_plugin::ps_render_music(s, out.as_mut_ptr(), (out.len() / 2) as i32, 2, dsp) };
    WATCHED.with(|w| w.set(false));
    assert_eq!(code, PS_OK);
}

#[test]
fn rendering_a_system_source_allocates_nothing() {
    const FRAMES: usize = 256;
    const RATE: f64 = 48_000.0;
    let song = native().join("../public/reactive/house-128/house-128.ogg");
    let music = ps_plugin::decode::decode(&song, RATE).unwrap().stereo;
    let fake = Fake::new(RATE);
    let s = open_source(fake.source(), RATE, None);
    let mut out = vec![0.0f32; 2 * FRAMES];
    let mut next = 0;
    let mut capture = |frames: usize| {
        if fake.capture(&music[2 * next..2 * (next + frames)]) {
            next += frames;
        }
    };
    let mut rendered = 0u64;
    // (Waiting for `M`, playing, stopped, and playing again: a buffer captured for each one
    // rendered while the app is tapped.)
    capture((2.9 * RATE) as usize);
    render(s, &mut out, RATE, &mut rendered);
    assert_eq!(status(s).state, PS_WAITING);
    capture((0.1 * RATE) as usize + FRAMES);
    for _ in 0..(10.0 * RATE) as usize / FRAMES {
        render(s, &mut out, RATE, &mut rendered);
        capture(FRAMES);
    }
    assert_eq!(status(s).state, PS_PLAYING);
    fake.release("the main thread stalled for 2 s");
    let started = std::time::Instant::now();
    while fake.taps().len() < 2 {
        hang_guard(started, "the app was never tapped again");
        // (In real time while the source waits to tap the app again.)
        if rendered as f64 * FRAMES as f64 / RATE < 10.0 + started.elapsed().as_secs_f64() {
            render(s, &mut out, RATE, &mut rendered);
        } else {
            std::thread::yield_now();
        }
    }
    assert_eq!(status(s).state, PS_STOPPED);
    for _ in 0..(5.0 * RATE) as usize / FRAMES {
        capture(FRAMES);
        render(s, &mut out, RATE, &mut rendered);
    }
    let st = status(s);
    unsafe { ps_plugin::ps_close(s) };
    assert_eq!((st.state, st.stalls, st.releases), (PS_PLAYING, 0, 1));
    assert_eq!(ALLOCATIONS.load(Ordering::Relaxed), 0);
}
