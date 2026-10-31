//! The System source on a real tap, through the C interface: `afplay` plays a quiet tone after a
//! second and a half of silence, and the plugin taps it (muted, so the tone is never heard), plays
//! it `M` late, lets the watchdog release it when the main thread's heartbeat stops for 2 s, and
//! taps it again once the heartbeat is back. Ignored by default: it uses the Mac's audio devices.
//!
//!     cargo test -p plugin --test system_tap -- --ignored --nocapture
//!
//! Without the System Audio Recording grant the tap captures silence; the mechanics (muting, the
//! watchdog, tapping again) still run, and it prints whether the music came through. With the grant
//! (the terminal it runs from holds it), the delayed tone must play.

mod common;

use std::ffi::CString;
use std::process::{Child, Command};
use std::time::{Duration, Instant};

use common::{empty_entries, last_error, status, temp, write_wav};
use ps_plugin::{Config, PS_OK, PS_PLAYING, PS_SOURCE_SYSTEM, PS_STOPPED, Session};

const RATE: f64 = 48_000.0;
const FRAMES: usize = 512;
/// The tone's amplitude (-50 dB: faint, should anything ever let it through).
const LEVEL: f32 = 0.003;

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
}

#[test]
#[ignore = "taps a real process through the Mac's audio devices"]
fn a_real_app_is_tapped_muted_released_by_the_watchdog_and_tapped_again() {
    let dir = temp("tap");
    let wav = dir.join("tone.wav");
    let mut samples = vec![0.0f32; (1.5 * 44_100.0) as usize * 2];
    for i in 0..(20.0 * 44_100.0) as usize {
        let x = LEVEL * (std::f32::consts::TAU * 440.0 * i as f32 / 44_100.0).sin();
        samples.extend([x, x]);
    }
    write_wav(&wav, 2, &samples);
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

    // Tapped: the tone plays `M` late.
    player.play(6.0, true);
    let st = status(s);
    println!("tapped: {st:?}, peak {}", player.peak);
    assert_eq!((st.state, st.muted), (PS_PLAYING, 1));
    assert!(
        st.analysed > st.anchor_song + 2.5,
        "analysed {}",
        st.analysed
    );
    let heard = player.peak;

    // The main thread stalls for 2.5 s: the watchdog releases the app.
    player.play(2.5, false);
    let st = status(s);
    println!("released: {st:?}");
    assert_eq!((st.releases, st.muted, st.state), (1, 0, PS_STOPPED));

    // The heartbeat is back and the app still plays: it is tapped again, and the music resumes
    // `M` later.
    player.play(1.5, true);
    let st = status(s);
    println!("tapped again: {st:?}");
    assert_eq!((st.releases, st.muted), (1, 1));
    player.play(4.0, true);
    let st = status(s);
    println!("resumed: {st:?}");
    assert_eq!(st.state, PS_PLAYING);
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
