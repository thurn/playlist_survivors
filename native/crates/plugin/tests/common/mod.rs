//! What the tests share: opening a session through the C interface and writing test audio.
#![allow(dead_code)]

use std::ffi::CString;
use std::path::{Path, PathBuf};

use ps_plugin::decode::SR;
use ps_plugin::{Config, Entry, PS_OK, PS_SOURCE_FILE, Session, Status};

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
    // (Its last frame interpolates to the source frame after it, hence a margin of two; a ulp's
    // slack, since at the source's end the two sides of the comparison are equal.)
    let want = (seconds + 2.0 / SR).min(total - next);
    while status(s).buffered + 1e-9 < want {
        std::thread::yield_now();
    }
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
