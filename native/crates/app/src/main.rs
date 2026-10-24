//! The winit app: runner, input, menus, HUD flow, calibration and logs.
//!
//! Until C3 lands the real runner, the app is the playable slice (docs/design/native-app.md,
//! "Phase V: The Playable Slice"): a rough game on live system audio.

mod slice;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if let Err(e) = slice::run(&args) {
        eprintln!("playlist-survivors: {e}");
        std::process::exit(1);
    }
}
