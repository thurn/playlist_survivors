//! The live analysis: the system tap's samples, or a `--file`'s as the output plays it, through
//! `capture`'s analysis thread with the app's models; committed beats go to the main thread with
//! the host time they are heard. And where the app finds its resources.

use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64};
use std::sync::mpsc;

use analysis::delayed::Listening;
use capture::analysis::{Msg, Source};
use winit::event_loop::EventLoopProxy;

/// A resource: `PS_<NAME>` (upper case), the bundle's `Resources/<name>`, or `fallback` relative
/// to this crate in the checkout.
pub fn resource(name: &str, fallback: &str) -> PathBuf {
    if let Ok(p) = std::env::var(format!("PS_{}", name.replace('.', "_").to_uppercase())) {
        return PathBuf::from(p);
    }
    if let Some(res) = std::env::current_exe()
        .ok()
        .and_then(|e| Some(e.parent()?.parent()?.join("Resources").join(name)))
        && res.exists()
    {
        return res;
    }
    Path::new(env!("CARGO_MANIFEST_DIR")).join(fallback)
}

/// The analyzer's models: `PS_MODELS`, the bundle's Resources, or the checkout's.
pub fn models_path() -> PathBuf {
    if let Ok(p) = std::env::var("PS_MODELS") {
        return PathBuf::from(p);
    }
    resource("madmom.bin", "../../models/madmom.bin")
}

/// Starts the analysis thread on `source` (`capture::analysis::spawn`), waking the event loop
/// after each step's messages. `heard_after` (an f64's bits, so the app can change it when the
/// output device changes) is the time from a buffer's host time to when it is heard (the tap's
/// offset from the output, or the output latency for a played file).
pub fn spawn(
    source: Box<dyn Source>,
    listening: Listening,
    heard_after: Arc<AtomicU64>,
    tx: mpsc::Sender<Msg>,
    proxy: EventLoopProxy<()>,
    stop: Arc<AtomicBool>,
) -> Result<std::thread::JoinHandle<()>, String> {
    capture::analysis::spawn(
        source,
        &models_path(),
        listening,
        heard_after,
        tx,
        move || {
            let _ = proxy.send_event(());
        },
        stop,
    )
}
