//! The Unity game's music plugin (docs/design/unity-game.md, "Music: the Rust Plugin"): a source
//! played `M` late through Unity's output and analysed ahead of that, behind the plain C interface
//! of `include/ps_plugin.h`, which Unity calls through P/Invoke.
//!
//! - [`session`]: what every source shares: the analysis, the render path and the clock anchor.
//! - [`file`]: a File source's threads (decoding, playback feeding).
//! - [`system`]: a System source: an app tapped and muted, its watchdog, and tapping it again.
//! - [`decode`]: a File source's tracks, decoded by macOS's `afconvert` (`capture::decode`).
//! - [`record`]: the session's record (the analyzer's input and every entry).
//! - [`latency`]: the output device's latency as the OS reports it.
//!
//! Every entry point catches panics; those that can fail return a code that [`ps_last_error`]
//! describes.

pub mod decode;
pub mod file;
pub mod latency;
pub mod record;
pub mod session;
pub mod system;

use std::cell::RefCell;
use std::ffi::{CStr, CString, c_char};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::PathBuf;

pub use analysis::flat::Record as Entry;
pub use session::{Session, Source};

pub const PS_OK: i32 = 0;
pub const PS_ERR_ARGUMENT: i32 = -1;
pub const PS_ERR_SOURCE: i32 = -2;
pub const PS_ERR_MODELS: i32 = -3;
pub const PS_ERR_UNSUPPORTED: i32 = -4;
pub const PS_ERR_PANIC: i32 = -5;
pub const PS_ERR_FAILED: i32 = -6;

pub const PS_SOURCE_FILE: i32 = 0;
pub const PS_SOURCE_SYSTEM: i32 = 1;
pub const PS_SOURCE_REPLAY: i32 = 2;

pub const PS_WAITING: i32 = 0;
pub const PS_PLAYING: i32 = 1;
pub const PS_ENDED: i32 = 2;
pub const PS_STOPPED: i32 = 3;

/// `ps_config`.
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct Config {
    pub source: i32,
    pub pid: i32,
    pub m: f64,
    pub output_rate: f64,
    pub path: *const c_char,
    pub models: *const c_char,
    pub session_dir: *const c_char,
}

/// `ps_app`.
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct App {
    pub pid: i32,
    pub name: [c_char; 128],
}

/// `ps_status`.
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Status {
    pub anchor_dsp: f64,
    pub anchor_song: f64,
    pub anchor_frames: i64,
    pub output_latency: f64,
    pub buffered: f64,
    pub analysed: f64,
    pub stalls: i32,
    pub state: i32,
    pub muted: i32,
    pub analysis_done: i32,
    pub failed: i32,
    pub follower_gain: f32,
    pub releases: i32,
    pub device_changed: i32,
}

/// A failure: its code and what went wrong.
#[derive(Clone, Debug, PartialEq)]
pub struct Error {
    pub code: i32,
    pub message: String,
}

impl Error {
    pub fn new(code: i32, message: impl Into<String>) -> Error {
        Error {
            code,
            message: message.into(),
        }
    }
}

thread_local! {
    static LAST_ERROR: RefCell<CString> = RefCell::new(CString::default());
}

/// Records `e` as this thread's last error and returns its code.
fn fail(e: &Error) -> i32 {
    let message = CString::new(e.message.replace('\0', " ")).unwrap_or_default();
    LAST_ERROR.with(|l| *l.borrow_mut() = message);
    e.code
}

/// Runs an entry point, turning a panic into `PS_ERR_PANIC`.
fn guard(f: impl FnOnce() -> Result<i32, Error>) -> i32 {
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(n)) => n,
        Ok(Err(e)) => fail(&e),
        Err(panic) => fail(&Error::new(PS_ERR_PANIC, panic_message(&*panic))),
    }
}

pub(crate) fn panic_message(panic: &(dyn std::any::Any + Send)) -> String {
    let what = panic
        .downcast_ref::<&str>()
        .map(|s| (*s).to_string())
        .or_else(|| panic.downcast_ref::<String>().cloned())
        .unwrap_or_default();
    format!("the plugin panicked: {what}")
}

/// A C string argument as a path; None when null.
///
/// # Safety
/// `p` is null or a NUL-terminated string.
unsafe fn path(p: *const c_char, what: &str) -> Result<Option<PathBuf>, Error> {
    if p.is_null() {
        return Ok(None);
    }
    let s = unsafe { CStr::from_ptr(p) }
        .to_str()
        .map_err(|_| Error::new(PS_ERR_ARGUMENT, format!("{what} isn't UTF-8")))?;
    Ok(Some(PathBuf::from(s)))
}

/// The analyzer's models: `PS_MODELS`, else the checkout's.
fn default_models() -> PathBuf {
    std::env::var_os("PS_MODELS").map_or_else(
        || {
            PathBuf::from(concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../../models/madmom.bin"
            ))
        },
        PathBuf::from,
    )
}

/// Opens a session on `config`'s source and writes it to `out`.
///
/// # Safety
/// `config` and `out` are null or valid; `config`'s strings are null or NUL-terminated.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ps_open(config: *const Config, out: *mut *mut Session) -> i32 {
    guard(|| {
        if config.is_null() || out.is_null() {
            return Err(Error::new(
                PS_ERR_ARGUMENT,
                "ps_open needs a config and an out",
            ));
        }
        let c = unsafe { *config };
        let source = match c.source {
            PS_SOURCE_FILE => Source::File(
                unsafe { path(c.path, "the path") }?
                    .ok_or_else(|| Error::new(PS_ERR_ARGUMENT, "the File source needs a path"))?,
            ),
            PS_SOURCE_SYSTEM => Source::System {
                pid: c.pid,
                taps: system_taps()?,
            },
            PS_SOURCE_REPLAY => {
                return Err(Error::new(
                    PS_ERR_UNSUPPORTED,
                    "the Replay source isn't available yet",
                ));
            }
            s => return Err(Error::new(PS_ERR_ARGUMENT, format!("unknown source {s}"))),
        };
        let config = session::Config {
            source,
            m: c.m,
            output_rate: c.output_rate,
            models: unsafe { path(c.models, "the models' path") }?.unwrap_or_else(default_models),
            session_dir: unsafe { path(c.session_dir, "the session folder") }?,
        };
        let session = Session::open(config)?;
        unsafe { *out = Box::into_raw(Box::new(session)) };
        Ok(PS_OK)
    })
}

/// The platform's app taps (a Result for the platforms without them yet).
#[cfg(target_os = "macos")]
#[allow(clippy::unnecessary_wraps)]
fn system_taps() -> Result<Box<dyn system::Taps>, Error> {
    Ok(system::platform())
}

#[cfg(not(target_os = "macos"))]
fn system_taps() -> Result<Box<dyn system::Taps>, Error> {
    Err(Error::new(
        PS_ERR_UNSUPPORTED,
        "System listening isn't available on this platform yet",
    ))
}

/// `name` as a `ps_app` name: NUL-terminated, cut at a character to fit.
fn app_name(name: &str) -> [c_char; 128] {
    let mut out = [0 as c_char; 128];
    let mut end = name.len().min(out.len() - 1);
    while !name.is_char_boundary(end) {
        end -= 1;
    }
    for (o, &b) in out.iter_mut().zip(&name.as_bytes()[..end]) {
        *o = if b == 0 { b' ' } else { b } as c_char;
    }
    out
}

/// Fills `out` with up to `max` of the apps producing audio now; returns how many.
///
/// # Safety
/// `out` is null or holds `max` apps.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ps_list_apps(out: *mut App, max: i32) -> i32 {
    guard(|| {
        if max < 0 || (out.is_null() && max > 0) {
            return Err(Error::new(
                PS_ERR_ARGUMENT,
                "ps_list_apps needs room for max apps",
            ));
        }
        if max == 0 {
            return Ok(0);
        }
        #[cfg(target_os = "macos")]
        let apps = system::playing();
        #[cfg(not(target_os = "macos"))]
        let apps: Vec<capture::apps::AudioApp> = Vec::new();
        let out = unsafe { std::slice::from_raw_parts_mut(out, max as usize) };
        let n = apps.len().min(out.len());
        for (slot, app) in out.iter_mut().zip(&apps) {
            *slot = App {
                pid: app.pid,
                name: app_name(&app.name),
            };
        }
        Ok(n as i32)
    })
}

/// Copies up to `max` new entries into `out`; the main thread's heartbeat.
///
/// # Safety
/// `s` is null or an open session, polled from one thread at a time; `out` holds `max` entries.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ps_poll(s: *mut Session, out: *mut Entry, max: i32) -> i32 {
    guard(|| {
        if s.is_null() || (out.is_null() && max > 0) || max < 0 {
            return Err(Error::new(
                PS_ERR_ARGUMENT,
                "ps_poll needs a session and room",
            ));
        }
        if max == 0 {
            return Ok(0);
        }
        let out = unsafe { std::slice::from_raw_parts_mut(out, max as usize) };
        unsafe { (*s).poll(out) }.map(|n| n as i32)
    })
}

/// Writes one output buffer of the delayed music and records the anchor.
///
/// # Safety
/// `s` is null or an open session, rendered from one thread at a time; `out` holds
/// `frames * channels` samples.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ps_render_music(
    s: *mut Session,
    out: *mut f32,
    frames: i32,
    channels: i32,
    dsp_time: f64,
) -> i32 {
    if out.is_null() || frames <= 0 || channels <= 0 {
        return PS_ERR_ARGUMENT;
    }
    let out = unsafe { std::slice::from_raw_parts_mut(out, frames as usize * channels as usize) };
    if s.is_null() {
        out.fill(0.0);
        return PS_ERR_ARGUMENT;
    }
    // (No guard: a failure's message would allocate on the audio thread.)
    let rendered = catch_unwind(AssertUnwindSafe(|| unsafe {
        (*s).render(out, channels as usize, dsp_time);
    }));
    if rendered.is_ok() {
        PS_OK
    } else {
        out.fill(0.0);
        PS_ERR_PANIC
    }
}

/// The session's anchor, latency, stalls, mute, gain, progress and device; the main thread's
/// heartbeat, as `ps_poll` is.
///
/// # Safety
/// `s` is null or an open session; `out` is null or valid.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ps_status(s: *mut Session, out: *mut Status) {
    if out.is_null() {
        return;
    }
    let status = if s.is_null() {
        Status::default()
    } else {
        catch_unwind(AssertUnwindSafe(|| unsafe { (*s).status() })).unwrap_or_default()
    };
    unsafe { *out = status };
}

/// Stops and frees a session.
///
/// # Safety
/// `s` is null or an open session, not used after this.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn ps_close(s: *mut Session) {
    if s.is_null() {
        return;
    }
    let session = unsafe { Box::from_raw(s) };
    let _ = catch_unwind(AssertUnwindSafe(move || drop(session)));
}

/// This thread's last failure, until its next failing call.
#[unsafe(no_mangle)]
pub extern "C" fn ps_last_error() -> *const c_char {
    LAST_ERROR.with(|l| l.borrow().as_ptr())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_app_name_is_cut_at_a_character_and_terminated() {
        let name = app_name(&"é".repeat(100));
        let bytes: Vec<u8> = name.iter().map(|&c| c as u8).collect();
        let end = bytes.iter().position(|&b| b == 0).unwrap();
        assert_eq!(end, 126);
        assert_eq!(std::str::from_utf8(&bytes[..end]).unwrap(), "é".repeat(63));
        assert_eq!(
            app_name("Music")[..6],
            [77, 117, 115, 105, 99, 0].map(|b| b as c_char)
        );
    }
}
