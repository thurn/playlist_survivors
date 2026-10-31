//! What the tests share: opening a session through the C interface and writing test audio.
#![allow(dead_code)]

use std::ffi::CString;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use audio::delay::{DelayLine, Writer, delay_line};
use capture::Meta;
use capture::apps::AudioApp;
use ps_plugin::decode::SR;
use ps_plugin::system::{Tap, Taps};
use ps_plugin::{Config, Entry, PS_OK, PS_SOURCE_FILE, Session, Source, Status};

pub fn native() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("../..")
}

/// A fresh folder for one test's files.
pub fn temp(name: &str) -> PathBuf {
    let dir = std::env::temp_dir().join(format!("ps-plugin-test-{name}-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    dir
}

/// Opens a File session on `path` at `rate` through `ps_open`, recording to `record` if given.
pub fn open(path: &Path, rate: f64, record: Option<&Path>) -> Result<*mut Session, i32> {
    let path = CString::new(path.to_str().unwrap()).unwrap();
    let record = record.map(|r| CString::new(r.to_str().unwrap()).unwrap());
    let config = Config {
        source: PS_SOURCE_FILE,
        pid: 0,
        m: 3.0,
        output_rate: rate,
        path: path.as_ptr(),
        models: std::ptr::null(),
        session_dir: record.as_ref().map_or(std::ptr::null(), |r| r.as_ptr()),
    };
    let mut s = std::ptr::null_mut();
    match unsafe { ps_plugin::ps_open(&raw const config, &raw mut s) } {
        PS_OK => Ok(s),
        code => Err(code),
    }
}

pub fn status(s: *mut Session) -> Status {
    let mut st = Status::default();
    unsafe { ps_plugin::ps_status(s, &raw mut st) };
    st
}

/// Waits until the player has written the music of the next buffer (`seconds` long, from song
/// time `next`) of a source `total` s long: the tests render faster than real time.
pub fn wait_for_music(s: *mut Session, next: f64, seconds: f64, total: f64) {
    // (Its last frame interpolates to the frame after it, hence a margin of two less one of
    // slack: at the source's end the music stops at an output frame, within one of `total`.)
    let want = (seconds + 2.0 / SR).min(total - next);
    let started = Instant::now();
    while status(s).buffered + 1.0 / SR < want {
        hang_guard(started, "the music never arrived");
        std::thread::yield_now();
    }
}

/// Fails a wait that has run far longer than any loaded machine needs (a hang, not a slow run).
pub fn hang_guard(started: Instant, what: &str) {
    assert!(started.elapsed() < Duration::from_secs(120), "{what}");
}

pub fn last_error() -> String {
    unsafe { std::ffi::CStr::from_ptr(ps_plugin::ps_last_error()) }
        .to_string_lossy()
        .into_owned()
}

pub fn empty_entries(n: usize) -> Vec<Entry> {
    vec![
        Entry {
            kind: 0,
            t: 0.0,
            known_at: 0.0,
            v: [0.0; 12],
        };
        n
    ]
}

/// Writes 44.1 kHz float samples (`channels` interleaved) as a WAV file.
pub fn write_wav(path: &Path, channels: u16, samples: &[f32]) {
    let data = (samples.len() * 4) as u32;
    let mut b = b"RIFF".to_vec();
    b.extend((36 + data).to_le_bytes());
    b.extend(b"WAVEfmt ");
    b.extend(16u32.to_le_bytes());
    b.extend(3u16.to_le_bytes());
    b.extend(channels.to_le_bytes());
    b.extend(44_100u32.to_le_bytes());
    b.extend((44_100 * 4 * u32::from(channels)).to_le_bytes());
    b.extend((4 * channels).to_le_bytes());
    b.extend(32u16.to_le_bytes());
    b.extend(b"data");
    b.extend(data.to_le_bytes());
    for x in samples {
        b.extend(x.to_le_bytes());
    }
    std::fs::write(path, b).unwrap();
}

/// A System source's taps for the tests, driven by hand: [`Fake::capture`] stands for the tap's
/// IOProc and [`Fake::release`] for its watchdog.
#[derive(Clone)]
pub struct Fake {
    state: Arc<Mutex<FakeState>>,
    rate: f64,
}

#[derive(Default)]
struct FakeState {
    playing: bool,
    /// The first frame of each tap so far.
    taps: Vec<u64>,
    live: Option<Live>,
    heartbeat: f64,
}

/// The live tap: the line's writer, the next frame, what it has captured since the last drain,
/// and its watchdog's verdict.
struct Live {
    writer: Writer,
    next: u64,
    samples: Vec<f32>,
    metas: Vec<Meta>,
    released: Option<&'static str>,
}

impl Fake {
    /// App 42, playing, tapped at `rate`.
    pub fn new(rate: f64) -> Fake {
        Fake {
            state: Arc::new(Mutex::new(FakeState {
                playing: true,
                ..FakeState::default()
            })),
            rate,
        }
    }

    pub fn source(&self) -> Source {
        Source::System {
            pid: 42,
            taps: Box::new(self.clone()),
        }
    }

    fn state(&self) -> std::sync::MutexGuard<'_, FakeState> {
        self.state.lock().unwrap()
    }

    /// Captures `frames` frames of `stereo` (interleaved, from the live tap's next frame); false
    /// once no tap is live.
    pub fn capture(&self, stereo: &[f32]) -> bool {
        let mut st = self.state();
        let Some(live) = st.live.as_mut() else {
            return false;
        };
        let frames = stereo.len() / 2;
        assert!(live.writer.write(live.next, frames, 0.0, |a, b| {
            a.copy_from_slice(&stereo[..a.len()]);
            b.copy_from_slice(&stereo[a.len()..]);
        }));
        live.samples
            .extend(stereo.chunks(2).map(|f| f32::midpoint(f[0], f[1])));
        live.metas.push(Meta {
            first: live.next,
            frames: frames as u32,
            ..Meta::default()
        });
        live.next += frames as u64;
        true
    }

    /// The live tap's watchdog releases it.
    pub fn release(&self, why: &'static str) {
        self.state().live.as_mut().unwrap().released = Some(why);
    }

    pub fn set_playing(&self, playing: bool) {
        self.state().playing = playing;
    }

    /// The first frame of each tap so far.
    pub fn taps(&self) -> Vec<u64> {
        self.state().taps.clone()
    }

    /// The main thread's last heartbeat the source relayed (host s).
    pub fn heartbeat(&self) -> f64 {
        self.state().heartbeat
    }

    fn start(&self, writer: Writer, first: u64) -> Box<dyn Tap> {
        let mut st = self.state();
        st.taps.push(first);
        st.live = Some(Live {
            writer,
            next: first,
            samples: Vec::new(),
            metas: Vec::new(),
            released: None,
        });
        Box::new(self.clone())
    }
}

impl Taps for Fake {
    fn app(&mut self, pid: i32) -> Option<AudioApp> {
        (pid == 42).then(|| AudioApp {
            name: "Fake".into(),
            pid,
            bundle: None,
            processes: vec![1],
            playing: self.state().playing,
        })
    }

    fn first(&mut self, _: &AudioApp, keep: f64) -> Result<(Box<dyn Tap>, DelayLine), String> {
        let (writer, line) = delay_line(self.rate, 0.0, keep);
        Ok((self.start(writer, 0), line))
    }

    fn again(
        &mut self,
        _: &AudioApp,
        writer: Writer,
        rate: f64,
        first: u64,
    ) -> Result<Box<dyn Tap>, (String, Writer)> {
        assert_eq!(rate, self.rate);
        Ok(self.start(writer, first))
    }
}

impl Tap for Fake {
    fn drain(&mut self, samples: &mut Vec<f32>, metas: &mut Vec<Meta>) {
        if let Some(live) = self.state().live.as_mut() {
            samples.append(&mut live.samples);
            metas.append(&mut live.metas);
        }
    }

    fn beat(&self, heartbeat: f64) {
        self.state().heartbeat = heartbeat;
    }

    fn released(&self) -> Option<&'static str> {
        self.state().live.as_ref().and_then(|l| l.released)
    }

    fn muted(&self) -> bool {
        self.state()
            .live
            .as_ref()
            .is_some_and(|l| l.released.is_none())
    }

    fn close(self: Box<Self>) -> Writer {
        self.state().live.take().unwrap().writer
    }
}

/// Opens a session on `source` at `rate`, recording to `record` if given (the C interface can't
/// carry a test's taps).
pub fn open_source(source: Source, rate: f64, record: Option<&Path>) -> *mut Session {
    let config = ps_plugin::session::Config {
        source,
        m: 3.0,
        output_rate: rate,
        models: native().join("models/madmom.bin"),
        session_dir: record.map(Path::to_path_buf),
    };
    Box::into_raw(Box::new(Session::open(config).unwrap()))
}
