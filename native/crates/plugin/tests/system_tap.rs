//! The System source on a real tap, through the C interface: `afplay` plays a quiet tone after a
//! second and a half of silence, and the plugin taps it (muted, so the tone is never heard), plays
//! it `M` late, lets the watchdog release it when the main thread's heartbeat stops for 2 s, and
//! taps it again once the heartbeat is back. Ignored by default (it uses the Mac's audio devices);
//! slow CI runs it.
//!
//!     cargo test -p plugin --test system_tap -- --ignored --nocapture
//!
//! Without the System Audio Recording grant the tap captures silence; the mechanics (muting, the
//! watchdog, tapping again) still run, and it prints whether the music came through. With the grant
//! (the terminal it runs from holds it), the delayed tone must play.
//!
//! It waits on what the session reports, never on a clock: other audio on the machine (slow CI's
//! Unity lanes) can stall the capture for 0.5 s, and the watchdog then releases the app and the
//! source taps it again, as it should; that only adds releases.

mod common;

use std::ffi::CString;
use std::process::{Child, Command};
use std::time::{Duration, Instant};

use capture::watchdog::MAIN_STALL_S;
use common::{empty_entries, hang_guard, last_error, status, temp, write_wav};
use ps_plugin::{Config, PS_OK, PS_PLAYING, PS_SOURCE_SYSTEM, PS_STOPPED, Session, Status};

const RATE: f64 = 48_000.0;
const FRAMES: usize = 512;
/// The tone's amplitude (-50 dB: faint, should anything ever let it through).
const LEVEL: f32 = 0.003;
/// The tone's length (s): it plays on through the waits' hang guards' worth of a slow run.
const TONE_S: f64 = 60.0;

/// Kills the child when the test ends, however it ends.
struct Killed(Child);

impl Drop for Killed {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

/// The session, driven as Unity would: the audio thread renders in real time, and the main thread
/// polls (its heartbeat) unless told to stall.
struct Player {
    s: *mut Session,
    started: Instant,
    rendered: u64,
    /// The loudest sample rendered.
    peak: f32,
}

impl Player {
    /// Plays for `seconds`, polling only if `beating`.
    fn play(&mut self, seconds: f64, beating: bool) {
        let until = self.started.elapsed().as_secs_f64() + seconds;
        let mut out = [0.0f32; 2 * FRAMES];
        let mut entries = empty_entries(256);
        while self.started.elapsed().as_secs_f64() < until {
            if (self.rendered as f64 * FRAMES as f64 / RATE) < self.started.elapsed().as_secs_f64()
            {
                let dsp = self.rendered as f64 * FRAMES as f64 / RATE;
                let code = unsafe {
                    ps_plugin::ps_render_music(self.s, out.as_mut_ptr(), FRAMES as i32, 2, dsp)
                };
                assert_eq!(code, PS_OK);
                self.rendered += 1;
                self.peak = out.iter().fold(self.peak, |p, x| p.max(x.abs()));
            }
            if beating {
                let n = unsafe { ps_plugin::ps_poll(self.s, entries.as_mut_ptr(), 256) };
                assert!(n >= 0, "{}", last_error());
            }
            std::thread::sleep(Duration::from_millis(2));
        }
    }

    /// Plays, polling, until the session's status satisfies `done`.
    fn play_until(&mut self, what: &str, done: impl Fn(&Status) -> bool) -> Status {
        let started = Instant::now();
        loop {
            self.play(0.01, true);
            let st = status(self.s);
            if done(&st) {
                return st;
            }
            hang_guard(started, &format!("{what}: {st:?}"));
        }
    }
}

#[test]
#[ignore = "taps a real process through the Mac's audio devices"]
fn a_real_app_is_tapped_muted_released_by_the_watchdog_and_tapped_again() {
    let dir = temp("tap");
    let wav = dir.join("tone.wav");
    let mut samples = vec![0.0f32; (1.5 * 44_100.0) as usize];
    for i in 0..(TONE_S * 44_100.0) as usize {
        samples.push(LEVEL * (std::f32::consts::TAU * 440.0 * i as f32 / 44_100.0).sin());
    }
    write_wav(&wav, 1, &samples);
    let child = Killed(Command::new("afplay").arg(&wav).spawn().unwrap());
    let pid = child.0.id() as i32;
    let started = Instant::now();
    while capture::apps::of_pid(pid).is_none() {
        assert!(
            started.elapsed() < Duration::from_secs(10),
            "afplay has no process object"
        );
        std::thread::sleep(Duration::from_millis(20));
    }

    let record = dir.join("session");
    let record_c = CString::new(record.to_str().unwrap()).unwrap();
    let config = Config {
        source: PS_SOURCE_SYSTEM,
        pid,
        m: 3.0,
        output_rate: RATE,
        path: std::ptr::null(),
        models: std::ptr::null(),
        session_dir: record_c.as_ptr(),
    };
    let mut s = std::ptr::null_mut();
    let code = unsafe { ps_plugin::ps_open(&raw const config, &raw mut s) };
    assert_eq!(code, PS_OK, "{}", last_error());
    assert_eq!(status(s).muted, 1);
    let mut player = Player {
        s,
        started: Instant::now(),
        rendered: 0,
        peak: 0.0,
    };

    // Tapped: the tone plays `M` late, the analysis `M` ahead of what is heard.
    let st = player.play_until("the music never started", |st| {
        st.state == PS_PLAYING && st.muted == 1 && st.analysed > st.anchor_song + 2.5
    });
    println!("tapped: {st:?}");

    // The main thread stalls past the watchdog's 2 s: it releases the app, and the app stays
    // released and the music stopped while the stall lasts.
    let before = st.releases;
    player.play(MAIN_STALL_S + 2.0, false);
    let st = status(s);
    println!("released: {st:?}");
    assert!(
        st.releases > before,
        "the watchdog never released the app: {st:?}"
    );
    assert_eq!((st.muted, st.state), (0, PS_STOPPED));

    // The heartbeat is back and the app still plays: it is tapped again, and the music resumes
    // `M` later.
    let st = player.play_until("the app was never tapped again", |st| st.muted == 1);
    println!("tapped again: {st:?}");
    let st = player.play_until("the music never resumed", |st| st.state == PS_PLAYING);
    println!("resumed: {st:?}");
    let heard = player.peak;
    unsafe { ps_plugin::ps_close(s) };
    let meta = std::fs::read_to_string(record.join("meta.json")).unwrap();
    println!("{meta}");
    println!(
        "the music came through: {} (peak {heard}; without the capture grant the tap is silent)",
        heard > LEVEL / 2.0
    );
    if std::env::var_os("PS_EXPECT_GRANT").is_some() {
        assert!(heard > LEVEL / 2.0, "the delayed tone never played");
    }
    drop(child);
    let _ = std::fs::remove_dir_all(dir);
}
