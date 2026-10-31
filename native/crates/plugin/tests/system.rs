//! A System source through the session, on a hand-driven tap (no capture grant needed): the app's
//! music plays exactly `M` late and is analysed ahead of that; after the watchdog releases the tap
//! the music stops, the analyzer hears silence, and once the app plays again it is tapped again,
//! the music resuming `M` later on the same timeline; the record covers it all.

mod common;

use common::{Fake, empty_entries, hang_guard, native, open_source, status, temp};
use ps_plugin::decode::decode;
use ps_plugin::{PS_OK, PS_PLAYING, PS_STOPPED, PS_WAITING, Session};

const RATE: f64 = 48_000.0;
const FRAMES: usize = 512;
const WHY: &str = "the capture stopped for 0.5 s";

/// What the test plays the session and keeps of it.
struct Run {
    s: *mut Session,
    fake: Fake,
    /// The app's music (interleaved stereo at `RATE`), and the next frame of it to capture.
    music: Vec<f32>,
    next: usize,
    /// Every frame rendered, and every entry polled (as `ps entries` lines).
    played: Vec<f32>,
    polled: Vec<String>,
}

impl Run {
    /// Captures the app's next `frames` frames.
    fn capture(&mut self, frames: usize) {
        let chunk = &self.music[2 * self.next..2 * (self.next + frames)];
        assert!(self.fake.capture(chunk));
        self.next += frames;
    }

    /// Renders one buffer and polls.
    fn render(&mut self) {
        let mut out = [0.0f32; 2 * FRAMES];
        let dsp = (self.played.len() / 2) as f64 / RATE;
        let code =
            unsafe { ps_plugin::ps_render_music(self.s, out.as_mut_ptr(), FRAMES as i32, 2, dsp) };
        assert_eq!(code, PS_OK);
        if status(self.s).state != PS_WAITING {
            self.played.extend_from_slice(&out);
        }
        let mut entries = empty_entries(256);
        let n = unsafe { ps_plugin::ps_poll(self.s, entries.as_mut_ptr(), 256) };
        assert!(n >= 0);
        self.polled.extend(
            entries[..n as usize]
                .iter()
                .map(|e| serde_json::to_string(&e.line()).unwrap()),
        );
    }

    /// Waits for the analyzer to reach `M` (less a block and the resampler's lag) past the render
    /// position.
    fn wait_for_analysis(&self) {
        let started = std::time::Instant::now();
        loop {
            let st = status(self.s);
            if st.analysed >= st.anchor_song + 3.0 - 0.05 {
                return;
            }
            hang_guard(started, "the analysis never caught up");
            std::thread::yield_now();
        }
    }

    /// The rendered frame at song time frame `n`.
    fn played(&self, n: usize) -> (f32, f32) {
        (self.played[2 * n], self.played[2 * n + 1])
    }
}

#[test]
fn an_app_plays_m_late_stops_when_released_and_resumes_when_tapped_again() {
    let dir = temp("system");
    let record = dir.join("session");
    let song = native().join("../public/reactive/house-128/house-128.ogg");
    let music = decode(&song, RATE).unwrap().stereo;
    let fake = Fake::new(RATE);
    let s = open_source(fake.source(), RATE, Some(&record));
    let mut run = Run {
        s,
        fake,
        music,
        next: 0,
        played: Vec::new(),
        polled: Vec::new(),
    };
    let st = status(s);
    assert_eq!((st.state, st.muted, st.releases), (PS_WAITING, 1, 0));

    // The music waits for `M` of capture, then plays it from its first frame.
    run.capture((2.9 * RATE) as usize);
    run.render();
    assert_eq!(status(s).state, PS_WAITING);
    run.capture((0.1 * RATE) as usize + FRAMES);
    // (Then the app keeps playing in real time: a buffer captured for each one rendered.)
    for _ in 0..(5.0 * RATE) as usize / FRAMES {
        run.render();
        run.capture(FRAMES);
    }
    run.wait_for_analysis();
    let st = status(s);
    assert_eq!((st.state, st.stalls, st.muted), (PS_PLAYING, 0, 1));
    assert!(
        (st.buffered - 3.0).abs() < 0.02,
        "buffered {} {st:?}",
        st.buffered
    );
    for n in 0..run.played.len() / 2 {
        assert_eq!(
            run.played(n),
            (run.music[2 * n], run.music[2 * n + 1]),
            "frame {n}"
        );
    }
    run.wait_for_analysis();
    assert!(!run.polled.is_empty());

    // The watchdog releases the tap: the music stops at once, though the line holds `M` more,
    // and the analyzer hears silence, keeping `M` ahead.
    run.fake.set_playing(false);
    run.fake.release(WHY);
    let started = std::time::Instant::now();
    while status(s).releases == 0 {
        hang_guard(started, "the release was never seen");
        std::thread::yield_now();
    }
    let st = status(s);
    assert_eq!((st.state, st.muted), (PS_STOPPED, 0));
    let stopped_at = run.played.len() / 2;
    for _ in 0..(1.0 * RATE) as usize / FRAMES {
        run.render();
    }
    assert!(run.played[2 * stopped_at..].iter().all(|&x| x == 0.0));
    run.wait_for_analysis();
    assert_eq!(status(s).stalls, 0);

    // Once the app plays again it is tapped again where the analyzer's silence reached (`M` past
    // the render position), and the music resumes there.
    run.fake.set_playing(true);
    let started = std::time::Instant::now();
    let waited_from = run.played.len() / 2;
    while run.fake.taps().len() < 2 {
        hang_guard(started, "the app was never tapped again");
        // (Rendered in real time, as Unity's audio thread would, while the source waits.)
        if ((run.played.len() / 2 - waited_from) as f64 / RATE) < started.elapsed().as_secs_f64() {
            run.render();
        } else {
            std::thread::yield_now();
        }
    }
    let first = run.fake.taps()[1] as usize;
    let song_at = run.played.len() / 2;
    assert!(
        first > song_at + (2.9 * RATE) as usize && first <= song_at + (3.1 * RATE) as usize,
        "tapped again at frame {first}, the render at {song_at}"
    );
    assert_eq!(status(s).muted, 1);
    let resumed = run.next;
    for _ in 0..(5.0 * RATE) as usize / FRAMES {
        run.capture(FRAMES);
        let st = status(s);
        let silent = (run.played.len() / 2) < first;
        assert_eq!(st.state, if silent { PS_STOPPED } else { PS_PLAYING });
        run.render();
    }
    // (Silence until the tap's first frame, then the app's music from where it was tapped.)
    for n in stopped_at..run.played.len() / 2 {
        let want = if n < first {
            (0.0, 0.0)
        } else {
            let m = resumed + n - first;
            (run.music[2 * m], run.music[2 * m + 1])
        };
        assert_eq!(run.played(n), want, "frame {n}");
    }
    let st = status(s);
    assert_eq!((st.state, st.stalls, st.releases), (PS_PLAYING, 0, 1));
    run.wait_for_analysis();
    let analysed = status(s).analysed;
    unsafe { ps_plugin::ps_close(s) };

    // The record: the analyzer's input, its entries (the polled ones first) and the release.
    let meta: serde_json::Value =
        serde_json::from_str(&std::fs::read_to_string(record.join("meta.json")).unwrap()).unwrap();
    assert_eq!(meta["source"], "system: Fake (pid 42)");
    assert_eq!(meta["complete"], false);
    assert_eq!(meta["releases"].as_array().unwrap().len(), 1);
    assert_eq!(meta["releases"][0]["why"], WHY);
    let duration = meta["duration"].as_f64().unwrap();
    assert!(
        duration >= analysed,
        "{duration} s recorded, {analysed} s analysed"
    );
    let wav = std::fs::metadata(record.join("audio.wav")).unwrap().len();
    assert_eq!(wav, 44 + 4 * (duration * 44_100.0).round() as u64);
    let entries = std::fs::read_to_string(record.join("entries.jsonl")).unwrap();
    let recorded: Vec<&str> = entries.lines().collect();
    assert!(recorded.len() >= run.polled.len());
    assert_eq!(recorded[..run.polled.len()], run.polled);
    let _ = std::fs::remove_dir_all(dir);
}

#[test]
fn the_main_threads_calls_are_its_heartbeat() {
    let fake = Fake::new(RATE);
    let s = open_source(fake.source(), RATE, None);
    let before = capture::clock::now_s();
    let mut entries = empty_entries(1);
    unsafe { ps_plugin::ps_poll(s, entries.as_mut_ptr(), 1) };
    let started = std::time::Instant::now();
    while fake.heartbeat() < before {
        hang_guard(started, "the heartbeat never reached the watchdog");
        std::thread::yield_now();
    }
    unsafe { ps_plugin::ps_close(s) };
}
