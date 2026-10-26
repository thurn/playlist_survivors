//! The app (docs/design/native-app.md, "Phase G: Grow the Slice"): the game on live audio. The tap
//! (or a `--file` played through the output) feeds the analyzer on a thread; its entries go into
//! the sim's stream, stamped with their arrival, and the sim engine (`eval::live::Engine`) runs
//! ahead of the song time being heard, rolling back for late keys and arrivals (G5), and shows the
//! time heard each frame. Arrow keys or WASD stride
//! on the beat, Space stomps. The run ends when the hero falls, with a confirmed end (the song
//! stopped, the finale played), on Esc or losing focus (a pause ends it) or on Backspace; Enter or
//! Space starts the next.
//!
//!     playlist-survivors               # System mode: whatever the Mac is playing
//!     playlist-survivors --file <path> # plays the file and listens to it
//!     playlist-survivors --click       # also clicks on each committed beat
//!     playlist-survivors --file <path> --delayed  # Delayed listening: heard 3 s late, beats from hindsight
//!     playlist-survivors --delayed --app <name>   # Delayed on the system's audio: the app tapped, muted, played 3 s late
//!     playlist-survivors --delayed --tap-child <path> --headless --seconds 20 [--stall-at S]  # the tap
//!                                     # checked on a silent child of ours, touching no other app (and the watchdog)
//!     playlist-survivors --loopback 60  # Delayed listening's heard delay against M, by loopback, each minute
//!                                     # (speakers on, the capture and microphone grants)
//!     playlist-survivors --file <path> --volume 0.3  # the file quieter (0: silent, still heard)
//!     playlist-survivors --file <path> --mute  # everything runs, the device gets silence
//!     playlist-survivors --file <path> --headless --seconds 30 [--start-at S] [--switch-at S] [--press-beats] [--watch]  # no window, no Dock icon, muted:
//!                                     # the whole live path on a timer, for checks (then exits)
//!     playlist-survivors --input-offset-ms 30 --stomp-extra-ms 10  # calibration for this session
//!     playlist-survivors --legacy      # legacy mode (Z between runs): presses judged on the beat; zen mode, the
//!                                     # default, judges nothing and moves the hero on the beat
//!     (F1 in the app: the debug menu; F2 plays the next bundled track, F3 listens to the Mac;
//!     F8 saves the timing log)
//!     playlist-survivors --still <png> [--still-track <id> --still-at <s>] [--legacy]  # 20 s of the house
//!                                     # track (or a track to a time) through the engine, offscreen
//!     playlist-survivors --bench [--check]  # the frame's work (sim, evaluate, encode) over 60 s of it, offscreen

mod apps;
mod calibration;
// The tap and output are the phase 0 spike's (57e846a^:native/spike), kept as they were.
#[allow(clippy::pedantic, unused_qualifications, dead_code)]
mod coreaudio;
mod fx;
mod gems;
mod gfx;
mod hud;
mod keys;
mod lane;
mod live;
mod loopback;
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated,
    dead_code
)]
mod output;
mod scene;
mod session;
mod sounds;
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated,
    dead_code
)]
mod tap;
mod tier;
mod timing;
mod watchdog;

use std::collections::{HashMap, VecDeque};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc;

use analysis::analyzer::Entry;
use analysis::delayed::{DelayedParams, Listening};
use eval::live::{Engine, Moment};
use render::atlas::Atlases;
use render::post::Frame;
use sim::content::{ContentLibrary, SourceFile, load_content};
use sim::movement::InputKey;
use winit::application::ApplicationHandler;
use winit::event::{ElementState, WindowEvent};
use winit::event_loop::{ActiveEventLoop, ControlFlow, EventLoop};
use winit::keyboard::{KeyCode, PhysicalKey};
use winit::window::{Window, WindowId};

use audio::mixer::{Command, Report, Source as Voiced, Voice};
use calibration::{Calibration, Settings};
use coreaudio::now_s;
use eval::session::Arrival;
use gfx::Gfx;
use hud::{HudState, KeyLight, PressMark};
use live::{Msg, Source};
use output::{Event, Output};
use scene::{End, Look, Scene};
use session::Session;
use sounds::Sounds;
use timing::Timing;
use watchdog::Watchdog;

/// The tap's timeline runs this far behind the output's render timeline (phase 0, built-in
/// speakers); `PS_TAP_MINUS_RENDER_MS` overrides it.
const TAP_MINUS_RENDER_S: f64 = 0.023;
/// A captured buffer this loud is music, not silence.
const SOUND_PEAK: f32 = 0.01;
/// The analyzer's input rate (Hz).
const SR: f64 = 44_100.0;
/// A run keeps at most this much of its music for Watch my run (s; about 212 MB as i16).
const RUN_AUDIO_MAX_S: f64 = 2400.0;
/// Watch my run's music starts this long after it is asked for (s), so the mixer starts it on time.
const REPLAY_LEAD_S: f64 = 0.25;

/// A run's music for its replay: `audio` (at `SR`) from sample `skip` (before its start when
/// negative: silence first) for `seconds`, at the output's `rate` (linearly interpolated).
fn replay_music(audio: &[i16], skip: i64, seconds: f64, rate: f64) -> Arc<[f32]> {
    let n = (seconds.max(0.0) * rate) as usize;
    let at = |k: i64| {
        usize::try_from(k)
            .ok()
            .and_then(|k| audio.get(k))
            .copied()
            .map_or(0.0, |x| f32::from(x) / 32767.0)
    };
    (0..n)
        .map(|i| {
            let x = skip as f64 + i as f64 * SR / rate;
            let k = x.floor();
            let f = (x - k) as f32;
            let k = k as i64;
            at(k) + (at(k + 1) - at(k)) * f
        })
        .collect()
}

/// The analyzer's evidence gate (`manifest.json`'s `streamParams.gate`).
const GATE: f64 = 0.2;
/// How late a key can reach the engine: the window server's delivery to the main thread (p95
/// 30.5 ms, docs/design/native-app.md "Targets") and up to a frame more. The engine's settling
/// window covers it and the calibration's offset, so a press is applied at its own time.
const KEY_LATE_S: f64 = 0.05;
/// The frame's work, p99 in thread CPU time (ms): the target, half a frame at 120 Hz
/// (docs/design/native-app.md, "Targets"; a loaded machine stretches the measurement two- or
/// threefold, so its p95 plus half would flake).
const FRAME_WORK_GATE_MS: f64 = 4.0;
/// Under the ground (the TS game's world clear).
const CLEAR: [f64; 3] = [0.06, 0.07, 0.05];

#[allow(clippy::struct_excessive_bools)]
struct Options {
    file: Option<PathBuf>,
    level: Option<PathBuf>,
    still: Option<PathBuf>,
    /// The still's track (`public/reactive/<id>`) and song time.
    still_track: Option<String>,
    still_at: Option<f64>,
    volume: f32,
    click: bool,
    mute: bool,
    bench: bool,
    check: bool,
    headless: bool,
    /// Exit after this long (s).
    seconds: Option<f64>,
    /// No automatic start before this song time (s), for headless checks.
    start_at: Option<f64>,
    /// Press F2 (the next bundled track) this long after the start (s), for checks.
    switch_at: Option<f64>,
    /// Press on every committed beat, 20 ms late (a stand-in player, for checks).
    press_beats: bool,
    /// Zen mode, the default: nothing judged, every move on the beat. `--legacy` (or Z between
    /// runs) plays legacy mode, where presses are judged on the beat.
    zen: bool,
    /// Watch each run as soon as it ends (for checks).
    watch: bool,
    /// Delayed listening: the music heard `M` late, beats from hindsight; in System mode, the app
    /// named (`--app`) is tapped and muted, or (`--tap-child`, a check that touches no other app)
    /// a child of ours playing a file, silent until tapped.
    delayed: bool,
    app: Option<String>,
    tap_child: Option<PathBuf>,
    /// The child's own mode: play the file once told to (`--play-child`).
    play_child: Option<PathBuf>,
    /// Stall the main thread for 3 s this long after the start (s), to check the watchdog.
    stall_at: Option<f64>,
    /// Measure Delayed listening's heard delay by loopback for this many minutes, on a clicking
    /// child (`--click-child`, its own mode).
    loopback: Option<f64>,
    click_child: bool,
    /// Calibration offsets for this session (ms), over the saved ones.
    input_offset_ms: Option<f64>,
    stomp_extra_ms: Option<f64>,
}

fn options(args: &[String]) -> Result<Options, String> {
    let mut o = Options {
        file: None,
        level: None,
        still: None,
        still_track: None,
        still_at: None,
        volume: 1.0,
        click: false,
        mute: false,
        bench: false,
        check: false,
        headless: false,
        seconds: None,
        start_at: None,
        switch_at: None,
        press_beats: false,
        zen: true,
        watch: false,
        delayed: false,
        app: None,
        tap_child: None,
        play_child: None,
        stall_at: None,
        loopback: None,
        click_child: false,
        input_offset_ms: None,
        stomp_extra_ms: None,
    };
    let mut it = args.iter();
    while let Some(a) = it.next() {
        match a.as_str() {
            "--file" => o.file = Some(it.next().ok_or("--file needs a path")?.into()),
            "--level" => o.level = Some(it.next().ok_or("--level needs a path")?.into()),
            "--click" => o.click = true,
            "--mute" => o.mute = true,
            "--headless" => {
                o.headless = true;
                o.mute = true;
            }
            "--press-beats" => o.press_beats = true,
            "--legacy" => o.zen = false,
            "--watch" => o.watch = true,
            "--delayed" => o.delayed = true,
            "--app" => o.app = Some(it.next().ok_or("--app needs an app's name")?.clone()),
            "--tap-child" => {
                o.tap_child = Some(it.next().ok_or("--tap-child needs a file")?.into());
            }
            "--play-child" => {
                o.play_child = Some(it.next().ok_or("--play-child needs a file")?.into());
            }
            "--click-child" => o.click_child = true,
            "--loopback" => {
                o.loopback = Some(
                    it.next()
                        .and_then(|v| v.parse().ok())
                        .ok_or("--loopback needs minutes")?,
                );
            }
            "--stall-at" => {
                o.stall_at = Some(
                    it.next()
                        .and_then(|v| v.parse().ok())
                        .ok_or("--stall-at needs a time")?,
                );
            }
            "--switch-at" => {
                o.switch_at = Some(
                    it.next()
                        .and_then(|v| v.parse().ok())
                        .ok_or("--switch-at needs a time")?,
                );
            }
            "--start-at" => {
                o.start_at = Some(
                    it.next()
                        .and_then(|v| v.parse().ok())
                        .ok_or("--start-at needs a song time")?,
                );
            }
            "--seconds" => {
                o.seconds = Some(
                    it.next()
                        .and_then(|v| v.parse().ok())
                        .ok_or("--seconds needs a number")?,
                );
            }
            "--input-offset-ms" | "--stomp-extra-ms" => {
                let v = it
                    .next()
                    .and_then(|v| v.parse().ok())
                    .ok_or_else(|| format!("{a} needs a number"))?;
                if a == "--input-offset-ms" {
                    o.input_offset_ms = Some(v);
                } else {
                    o.stomp_extra_ms = Some(v);
                }
            }
            "--still" => o.still = Some(it.next().ok_or("--still needs a path")?.into()),
            "--bench" => o.bench = true,
            "--still-track" => {
                o.still_track = Some(it.next().ok_or("--still-track needs a track id")?.clone());
            }
            "--still-at" => {
                o.still_at = Some(
                    it.next()
                        .and_then(|v| v.parse().ok())
                        .ok_or("--still-at needs a song time")?,
                );
            }
            "--check" => o.check = true,
            "--volume" => {
                o.volume = it
                    .next()
                    .and_then(|v| v.parse().ok())
                    .ok_or("--volume needs a number")?;
            }
            // (Finder passes a process serial number to a bundled app.)
            s if s.starts_with("-psn_") => {}
            other => return Err(format!("unknown argument {other}")),
        }
    }
    Ok(o)
}

/// The game's content: the library, the reactive level, and each enemy kind's look.
struct World {
    lib: ContentLibrary,
    level: SourceFile,
    survivors: bool,
    looks: HashMap<String, Look>,
}

impl World {
    fn load(level: Option<&Path>) -> Result<World, String> {
        let dir = live::resource("content", "../../../content");
        let mut names: Vec<PathBuf> = std::fs::read_dir(&dir)
            .map_err(|e| format!("{}: {e}", dir.display()))?
            .filter_map(Result::ok)
            .map(|e| e.path())
            .filter(|p| p.extension().is_some_and(|x| x == "yaml"))
            .collect();
        names.sort();
        let read = |p: &Path, name: String| -> Result<SourceFile, String> {
            Ok(SourceFile {
                name,
                text: std::fs::read_to_string(p).map_err(|e| format!("{}: {e}", p.display()))?,
            })
        };
        let files = names
            .iter()
            .map(|p| {
                read(
                    p,
                    format!(
                        "content/{}",
                        p.file_name().unwrap_or_default().to_string_lossy()
                    ),
                )
            })
            .collect::<Result<Vec<_>, _>>()?;
        let lib = load_content(&files).map_err(|e| format!("content: {e:?}"))?;
        let level_path = level.map_or_else(
            || live::resource("levels", "../../../levels").join("survivors.yaml"),
            Path::to_path_buf,
        );
        let level = read(&level_path, level_path.to_string_lossy().into_owned())?;
        let looks = lib
            .enemies
            .values()
            .map(|d| {
                (
                    d.id.clone(),
                    Look {
                        atlas: d.atlas.clone(),
                        scale: d.scale as f32,
                        death: d.death.recipe.clone(),
                        debris: d.death.debris.clone(),
                    },
                )
            })
            .collect();
        let survivors = sim::content::level::load_level(
            &level,
            &lib,
            &sim::schedule::reactive_song_map(f64::INFINITY),
            None,
        )
        .map_err(|e| format!("level: {e:?}"))?
        .survivors
        .is_some();
        Ok(World {
            lib,
            level,
            survivors,
            looks,
        })
    }

    fn survivors(&self) -> bool {
        self.survivors
    }

    /// The zone the run's ground shows: the level's first (G2's director changes it on the music).
    fn theme(&self, engine: &Engine) -> Result<&sim::content::types::ZoneTheme, String> {
        engine
            .zones
            .first()
            .and_then(|z| self.lib.zones.get(z))
            .or_else(|| self.lib.zones.values().next())
            .ok_or_else(|| "no zone".to_string())
    }

    /// A new run's engine (live: the stream's length unknown).
    /// The ground for `engine`'s view at run time `s`, on beats `period` s long: its zone, and the
    /// transform wave sweeping in the next (its ring 175 px further each sixteenth for eight, as
    /// `mapgen/openfield.ts` grows it), until it has passed.
    fn field(&self, engine: &Engine, s: f64, period: f64) -> Result<scene::Field<'_>, String> {
        let first = self.theme(engine)?;
        let zone = |id: Option<&String>| id.and_then(|z| self.lib.zones.get(z)).unwrap_or(first);
        let view = engine.view.borrow();
        let sixteenths = view
            .wave
            .as_ref()
            .map(|w| ((s - w.s) / (period.max(0.2) / 4.0)).floor() + 1.0);
        Ok(match (&view.wave, sixteenths) {
            (Some(w), Some(n)) if (1.0..=12.0).contains(&n) => scene::Field {
                seed: engine.seed,
                theme: zone(w.prev.as_ref()),
                wave: Some((zone(Some(&w.zone)), w.x, w.y, 175.0 * n.min(8.0))),
            },
            _ => scene::Field {
                seed: engine.seed,
                theme: zone(view.zone.as_ref()),
                wave: None,
            },
        })
    }

    /// The engine for a run, in zen mode (nothing judged) or legacy mode.
    fn engine(&self, zen: bool) -> Result<Engine, String> {
        Engine::new(
            &self.level,
            &self.lib,
            &indexmap::IndexMap::new(),
            GATE,
            true,
            zen,
            f64::INFINITY,
        )
        .map_err(|e| format!("level: {e:?}"))
    }
}

#[allow(clippy::single_match_else)]
pub fn run(args: &[String]) -> Result<(), String> {
    let opts = options(args)?;
    if let Some(file) = &opts.play_child {
        return play_child(file);
    }
    if opts.click_child {
        return click_child();
    }
    if let Some(minutes) = opts.loopback {
        let (mut child, app) = spawn_child(&["--click-child".into()])?;
        let go = || {
            use std::io::Write;
            let stdin = child.stdin.as_mut().ok_or("the click child's input")?;
            writeln!(stdin, "go").map_err(|e| format!("the click child: {e}"))
        };
        let ok = loopback::run(
            minutes,
            DelayedParams::chosen().m,
            tap_offset(),
            &app.processes,
            go,
        );
        let _ = child.kill();
        let _ = child.wait();
        return if ok? {
            Ok(())
        } else {
            Err("the heard delay wasn't within 2 ms of M every minute".into())
        };
    }
    let legacy_level = live::resource("levels", "../../../levels").join("reactive.yaml");
    let level = opts
        .level
        .as_deref()
        .or_else(|| (!opts.zen).then_some(legacy_level.as_path()));
    if let Some(path) = &opts.still {
        return still(
            path,
            opts.still_track.as_deref(),
            opts.still_at,
            opts.zen,
            level,
        );
    }
    if opts.bench {
        return bench(opts.check);
    }
    let world = World::load(level)?;
    // Output first: our process object must exist before the tap can exclude it.
    let (sounds, bank) = match Sounds::load(
        &live::resource("sfx", "../../../public/assets/sfx"),
        Output::device_rate(),
        &world.lib,
    ) {
        Ok((sounds, bank)) => (Some(sounds), bank),
        Err(e) => {
            eprintln!("slice: no sound effects: {e}");
            (None, Vec::new())
        }
    };
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    // (Headless, the app never shows: no window, no Dock icon, no focus taken.)
    let event_loop = if opts.headless {
        use winit::platform::macos::{ActivationPolicy, EventLoopBuilderExtMacOS};
        EventLoop::builder()
            .with_activation_policy(ActivationPolicy::Prohibited)
            .build()
    } else {
        EventLoop::new()
    }
    .map_err(|e| e.to_string())?;
    let proxy = event_loop.create_proxy();
    let settings = Settings::load();
    let overrides = (opts.input_offset_ms, opts.stomp_extra_ms);
    let calibration = calibration_for(
        &settings,
        &coreaudio::device(coreaudio::default_output()),
        overrides,
    );
    let listening = if opts.delayed {
        Listening::Delayed(DelayedParams::chosen())
    } else {
        Listening::Direct
    };
    // (A tap or aggregate of ours a killed run left public is destroyed: private ones went with
    // it.)
    let leftovers = tap::destroy_leftovers();
    if leftovers > 0 {
        eprintln!("slice: destroyed {leftovers} tap objects a killed run left");
    }
    let (mut child, tap_target) = match (&opts.tap_child, &opts.app) {
        (Some(file), _) => {
            let (c, a) = spawn_child(&["--play-child".into(), file.clone().into_os_string()])?;
            (Some(c), Some(a))
        }
        (None, Some(name)) => (
            None,
            Some(apps::find(name).ok_or_else(|| format!("no app called {name} has audio"))?),
        ),
        (None, None) => (None, None),
    };
    let audio = Audio::start(
        opts.file.as_deref(),
        listening,
        tap_target.as_ref(),
        opts.volume,
        opts.mute,
        calibration.latency_correction_s(),
        &bank,
        proxy.clone(),
    )?;
    // (The child plays only once tapped, so it is never heard.)
    if let Some(c) = child.as_mut()
        && let Some(stdin) = c.stdin.as_mut()
    {
        use std::io::Write;
        writeln!(stdin, "go").map_err(|e| format!("the tap child: {e}"))?;
    }
    let engine = world.engine(opts.zen)?;
    let key_times = keys::KeyTimes::install();
    let mut app = App {
        key_times,
        calibration,
        device_uid: coreaudio::device(coreaudio::default_output()).uid,
        bluetooth: bluetooth(&coreaudio::device(coreaudio::default_output())),
        settings,
        screen: None,
        session: None,
        timing: None,
        last_stats: eval::live::RunnerStats::default(),
        run_index: 0,
        run_arrivals: Vec::new(),
        run_keys: Vec::new(),
        run_audio: Vec::new(),
        run_audio_s0: 0.0,
        analysed: 0,
        replay_music: None,
        watch: None,
        run_kills: 0,
        auto_watch: opts.watch,
        zen: opts.zen,
        run_zen: opts.zen,
        listening,
        tap_target,
        child,
        stall_at: opts.stall_at.map_or(f64::INFINITY, |s| now_s() + s),
        apps: Vec::new(),
        apps_at: f64::NEG_INFINITY,
        playing_since: HashMap::new(),
        note: None,
        overrides,
        gfx: None,
        sounds,
        art,
        world,
        audio,
        bank,
        volume: opts.volume,
        proxy,
        tracks: tracks(),
        track: 0,
        debug: false,
        click: opts.click,
        clock: None,
        engine,
        run_start: 0.0,
        scene: Scene::default(),
        beats: VecDeque::new(),
        peak: 0.0,
        last_sound: f64::NEG_INFINITY,
        over: None,
        running: false,
        recent: VecDeque::new(),
        listen: Listen::default(),
        start_at: opts.start_at.unwrap_or(f64::NEG_INFINITY),
        pending_start: None,
        title_at: 0.0,
        next_voice: 0,
        ahead: 0.02,
        voices: [0; 4],
        voices_at: 0.0,
        mute: opts.mute,
        device: coreaudio::default_output(),
        device_at: 0.0,
        frame_ms: Vec::new(),
        paced: opts.headless,
        occluded: false,
        headless: opts.headless,
        exit_at: opts.seconds.map_or(f64::INFINITY, |s| now_s() + s),
        switch_at: opts.switch_at.map_or(f64::INFINITY, |s| now_s() + s),
        press_beats: opts.press_beats,
        pressed_beat: f64::NEG_INFINITY,
        focused: false,
        next_frame: 0.0,
        keys: [KeyLight::default(); 5],
        presses: VecDeque::new(),
    };
    app.engine.settle_s = app.settle_s();
    app.begin_session();
    event_loop.run_app(&mut app).map_err(|e| e.to_string())
}

impl Drop for App {
    fn drop(&mut self) {
        // (The tap check's child goes with us.)
        if let Some(c) = self.child.as_mut() {
            let _ = c.kill();
            let _ = c.wait();
        }
    }
}

/// The tap check's child (`--play-child`): connects to the output silently (so it has a process
/// object to tap), says `ready`, and plays `file` once told `go`, until its input closes. The
/// parent taps it, muted, before telling it: it is never heard.
fn play_child(file: &Path) -> Result<(), String> {
    use std::io::{BufRead, Write};
    let music = Arc::<[f32]>::from(live::decode(file, Output::device_rate())?);
    let mut out = Output::start(Some(music), 0.0, 0.0, None, Vec::new(), false)?;
    println!("ready");
    std::io::stdout().flush().map_err(|e| e.to_string())?;
    let mut lines = std::io::stdin().lock().lines();
    while let Some(Ok(line)) = lines.next() {
        if line.trim() == "go" {
            let _ = out.commands.push(Command::MusicGain(0.5));
        }
    }
    Ok(())
}

/// The loopback's child (`--click-child`): connects to the output silently, says `ready`, and
/// clicks once a second once told `go`, until its input closes.
fn click_child() -> Result<(), String> {
    use std::io::{BufRead, Write};
    let mut out = Output::start(None, 1.0, 0.0, None, Vec::new(), false)?;
    println!("ready");
    std::io::stdout().flush().map_err(|e| e.to_string())?;
    // (Told `go`, then until our input closes: the parent gone.)
    let (go_tx, go_rx) = mpsc::channel();
    let open = Arc::new(AtomicBool::new(true));
    let still = Arc::clone(&open);
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines().map_while(Result::ok) {
            if line.trim() == "go" {
                let _ = go_tx.send(());
            }
        }
        still.store(false, Ordering::Relaxed);
    });
    if go_rx.recv().is_err() {
        return Ok(());
    }
    let mut id = 0u64;
    while open.load(Ordering::Relaxed) {
        id += 1;
        let _ = out.commands.push(Command::Play(Voice {
            id,
            source: Voiced::Click(1000.0),
            at: now_s() + 0.05,
            from: 0.0,
            lead: 0.0,
            gain: 1.0,
            pan: 0.0,
            rate: 1.0,
            duck: false,
        }));
        std::thread::sleep(std::time::Duration::from_secs(1));
    }
    Ok(())
}

/// Starts a child of ours with `args`, once it is ready, and finds its process object.
fn spawn_child(
    args: &[std::ffi::OsString],
) -> Result<(std::process::Child, apps::AudioApp), String> {
    use std::io::BufRead;
    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let mut child = std::process::Command::new(exe)
        .args(args)
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .spawn()
        .map_err(|e| format!("the tap child: {e}"))?;
    let mut ready = String::new();
    if let Some(out) = child.stdout.take() {
        let _ = std::io::BufReader::new(out).read_line(&mut ready);
    }
    if ready.trim() != "ready" {
        let _ = child.kill();
        return Err("the tap child didn't start".into());
    }
    let pid = child.id() as i32;
    for _ in 0..40 {
        if let Some(app) = apps::of_pid(pid) {
            return Ok((child, app));
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    let _ = child.kill();
    Err("the tap child has no process object".into())
}

/// An analyzer entry's group and the content time it is about, for the timing log.
fn content_time(e: &Entry) -> Option<(&'static str, f64)> {
    Some(match e {
        Entry::Beat(c) => ("beat", c.t),
        Entry::Tick(t) => ("tick", t.t),
        Entry::Onset(o) => ("onset", o.t),
        Entry::Change(c) => ("change", c.t),
        Entry::Jump(j) => ("jump", j.t),
        Entry::End(x) => ("end", x.t),
        Entry::Forecast(f) => ("forecast", f.t),
        Entry::Hindsight(_) | Entry::Timbre(_) => return None,
    })
}

/// The calibration for output device `d`: its own from the settings (else the default), with the
/// command line's offsets over it; and a notice when the device is Bluetooth.
fn calibration_for(
    settings: &Settings,
    d: &coreaudio::Device,
    overrides: (Option<f64>, Option<f64>),
) -> Calibration {
    let mut c = settings.for_device(&d.uid);
    if let Some(ms) = overrides.0 {
        c.input_offset_ms = Some(ms);
    }
    if let Some(ms) = overrides.1 {
        c.stomp_extra_ms = Some(ms);
    }
    eprintln!(
        "slice: output {} ({}): input offsets {:.0} ms, stomp {:.0} ms, display {:.0} ms, latency correction {:.0} ms{}",
        d.name,
        if settings.devices.contains_key(&d.uid) {
            "calibrated"
        } else {
            "not calibrated yet"
        },
        c.offset_s(InputKey::Up) * 1e3,
        c.offset_s(InputKey::Space) * 1e3,
        c.visual_s() * 1e3,
        c.latency_correction_s() * 1e3,
        if bluetooth(d) {
            "; Bluetooth: sync is best-effort"
        } else {
            ""
        }
    );
    c
}

/// How late Delayed listening hears the music (s; 0 in Direct).
fn delay_of(listening: Listening) -> f64 {
    match listening {
        Listening::Direct => 0.0,
        Listening::Delayed(d) => d.m,
    }
}

/// Whether a device is Bluetooth (its latency is long and varies: sync is best-effort).
fn bluetooth(d: &coreaudio::Device) -> bool {
    d.transport.starts_with("blue") || d.transport.starts_with("blea")
}

/// Where the music comes from and goes: the output (playing the file in File mode), the
/// analysis thread on the tap or the file, and the timing between them.
struct Audio {
    out: Output,
    rx: mpsc::Receiver<Msg>,
    stop: Arc<AtomicBool>,
    analyzer: Option<std::thread::JoinHandle<()>>,
    l_out: f64,
    /// When a captured buffer is heard after its host time (shared with the analysis thread):
    /// the output's latency plus `heard_offset`.
    heard_after: Arc<AtomicU64>,
    heard_offset: f64,
    /// The file played, in File mode.
    file: Option<PathBuf>,
    /// Direct or Delayed listening, as it runs.
    listening: Listening,
    /// Delayed listening on the system's audio: the app tapped, and the watchdog over its tap.
    app: Option<String>,
    watchdog: Option<Watchdog>,
}

/// The tap's lead over the render timeline (s): `TAP_MINUS_RENDER_S`, or
/// `PS_TAP_MINUS_RENDER_MS`.
fn tap_offset() -> f64 {
    std::env::var("PS_TAP_MINUS_RENDER_MS")
        .ok()
        .and_then(|s| s.parse::<f64>().ok())
        .map_or(TAP_MINUS_RENDER_S, |ms| ms / 1e3)
}

impl Audio {
    /// The output and the analysis thread: on `file` played through the output (File mode), or
    /// on the tap of everything else the Mac plays (System mode); in `listening`. Delayed, the
    /// file is heard `M` after the analyzer reads it, or (System mode) `app` is tapped, muted, and
    /// its capture played `M` late; with no app, System mode listens Direct.
    #[allow(clippy::too_many_arguments)]
    fn start(
        file: Option<&Path>,
        listening: Listening,
        app: Option<&apps::AudioApp>,
        volume: f32,
        mute: bool,
        correction: f64,
        bank: &sounds::Bank,
        proxy: winit::event_loop::EventLoopProxy<()>,
    ) -> Result<Audio, String> {
        let music = match file {
            Some(p) => Some(Arc::<[f32]>::from(live::decode(p, Output::device_rate())?)),
            None => None,
        };
        let listening = if music.is_none() && listening != Listening::Direct && app.is_none() {
            eprintln!(
                "slice: Delayed listening on the system's audio needs an app; listening Direct"
            );
            Listening::Direct
        } else {
            listening
        };
        let m = delay_of(listening);
        let offset = tap_offset();
        // (System mode, Delayed: the app's tap first, muting it, and its delay line, which plays
        // each frame M after its capture less the tap's lead, so it is heard exactly M late.)
        let (delayed_tap, line) = match (&music, app) {
            (None, Some(a)) if m > 0.0 => {
                let (t, line) = tap::Tap::start_processes(&a.processes, m - offset, m + 2.0)?;
                (Some(t), Some(line))
            }
            _ => (None, None),
        };
        if let Some(a) = app.filter(|a| delayed_tap.is_some() && a.browser()) {
            eprintln!(
                "slice: {} is a browser: its video will run {m} s ahead of its sound",
                a.name
            );
        }
        let mut out = Output::start(music.clone(), volume, m, line, bank.clone(), mute)?;
        // (The device's reported latency, corrected by the player's calibration for it.)
        let l_out = Output::reported_latency_s() + correction;
        let mut watchdog = None;
        #[allow(clippy::single_match_else)]
        let (source, heard_after, mode) = match music {
            Some(samples) => (
                Source::File {
                    samples,
                    metas: out.music_meta.take().ok_or("no music timing")?,
                    rate: out.rate,
                },
                l_out + m,
                "file",
            ),
            None => {
                let tap = match delayed_tap {
                    Some(t) => {
                        watchdog = Some(Watchdog::start(
                            t.tap,
                            t.aggregate,
                            Arc::clone(&t.destroyed),
                            Arc::clone(&t.alive),
                        ));
                        t
                    }
                    None => tap::Tap::start(true)?,
                };
                (Source::Tap(tap), l_out - offset + m, "system")
            }
        };
        eprintln!(
            "slice: {mode} mode{}{}, output {} Hz, L_out {:.1} ms, heard {:.1} ms after capture",
            file.map_or(String::new(), |p| format!(" ({})", p.display())),
            match (m > 0.0, app.filter(|_| file.is_none())) {
                (true, Some(a)) => format!(", Delayed listening (M = {m} s) on {}", a.name),
                (true, None) => format!(", Delayed listening (M = {m} s)"),
                _ => String::new(),
            },
            out.rate,
            l_out * 1e3,
            heard_after * 1e3
        );
        let stop = Arc::new(AtomicBool::new(false));
        let (tx, rx) = mpsc::channel();
        let heard_offset = heard_after - l_out;
        let heard_after = Arc::new(AtomicU64::new(heard_after.to_bits()));
        let analyzer = live::spawn(
            source,
            listening,
            Arc::clone(&heard_after),
            tx,
            proxy,
            stop.clone(),
        )?;
        Ok(Audio {
            out,
            rx,
            stop,
            analyzer: Some(analyzer),
            l_out,
            heard_after,
            heard_offset,
            file: file.map(Path::to_path_buf),
            listening,
            app: app.filter(|_| watchdog.is_some()).map(|a| a.name.clone()),
            watchdog,
        })
    }

    /// Stops the analysis thread (the output stops when dropped).
    fn shut_down(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
        if let Some(a) = self.analyzer.take() {
            let _ = a.join();
        }
    }
}

impl Drop for Audio {
    fn drop(&mut self) {
        self.shut_down();
    }
}

/// The bundled tracks' audio, for File mode from the debug menu (`public/reactive/index.json`).
fn tracks() -> Vec<PathBuf> {
    let dir = live::resource("reactive", "../../../public/reactive");
    std::fs::read_to_string(dir.join("index.json"))
        .ok()
        .and_then(|t| serde_json::from_str::<serde_json::Value>(&t).ok())
        .and_then(|v| {
            v["tracks"].as_array().map(|ts| {
                ts.iter()
                    .filter_map(|t| t["audio"].as_str().map(|a| dir.join(a)))
                    .collect()
            })
        })
        .unwrap_or_default()
}

/// This thread's CPU time (s): the frame's cost, whatever else the machine is doing.
fn thread_cpu_s() -> f64 {
    let mut ts = libc::timespec {
        tv_sec: 0,
        tv_nsec: 0,
    };
    // SAFETY: clock_gettime writes the timespec it is given.
    unsafe { libc::clock_gettime(libc::CLOCK_THREAD_CPUTIME_ID, &raw mut ts) };
    ts.tv_sec as f64 + ts.tv_nsec as f64 * 1e-9
}

/// The frame's own work, in this thread's CPU time (docs/design/native-app.md, "Targets"): 60 s of the house track's stream
/// through the engine at 120 Hz, 0.2 s ahead, a press on each beat 5 to 35 ms late; each frame's
/// scene and HUD built and encoded offscreen at 1920 x 1080. `check` fails above the gate.
fn bench(check: bool) -> Result<(), String> {
    let world = World::load(Some(
        &live::resource("levels", "../../../levels").join("reactive.yaml"),
    ))?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let stream_path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../public/reactive/house-128/stream.json");
    let text = std::fs::read_to_string(&stream_path)
        .map_err(|e| format!("{}: {e}", stream_path.display()))?;
    let file = stream::StreamFile::parse(&text).map_err(|e| e.0)?;
    let mut engine = world.engine(true)?;
    let mut replay = stream::Replay::new(file.log.clone());
    let beats: Vec<f64> = file.log.beats.iter().map(|b| b.t).collect();
    let (w, h) = (1920u32, 1080u32);
    let mut out = render::capture::Offscreen::new(&art.pages, w, h)?;
    let mut scene = Scene::default();
    let mut next = 0;
    let mut times = Vec::new();
    let mut jitter = 0.0f64;
    for frame in 1..=(60 * 120) {
        let s = f64::from(frame) / 120.0;
        let t0 = thread_cpu_s();
        engine.feed(|log| replay.feed_to(log, s));
        jitter = (jitter * 7.3 + 0.37).fract();
        while next < beats.len() && beats[next] + 0.005 + 0.03 * jitter <= s {
            let key = if next % 4 < 2 {
                InputKey::Right
            } else {
                InputKey::Down
            };
            engine.key(beats[next], key, true);
            engine.key(beats[next] + 0.08, key, false);
            next += 1;
        }
        engine.frame(s, 0.2);
        let moments = engine.view.borrow_mut().take_moments();
        scene.moments(&moments, s, &world.lib, &world.looks, &engine.view.borrow());
        scene.level(&engine.view.borrow(), s, &world.lib);
        let last_period = file
            .log
            .beats
            .iter()
            .rev()
            .find(|b| b.t <= s)
            .map_or(0.5, |b| b.period);
        let ground = world.field(&engine, s, last_period)?;
        let last = beats
            .iter()
            .copied()
            .filter(|&b| b <= s)
            .fold(0.0, f64::max);
        let pulse = scene::Pulse {
            beat: (s - last) / 0.47,
            bar: 0.5,
            index: beats.iter().filter(|&&b| b <= s).count() as f64,
        };
        let wave = engine.view.borrow().wave.as_ref().map(|v| v.s);
        scene.fast_lane(engine.stream(), GATE, s, s, wave);
        let mut layers = scene::Layers::default();
        scene.draw(
            &engine.view.borrow(),
            &ground,
            &art,
            &world.looks,
            &world.lib,
            s,
            pulse,
            w as f32,
            h as f32,
            &mut layers,
        );
        let state = HudState {
            keys: [KeyLight::default(); 5],
            presses: &[],
            beats: &[],
            beat_phase: pulse.beat,
            elapsed: s,
            bitten_ago: f64::INFINITY,
            head: layers.head,
            next_down: false,
            tier_flash: scene.tier_flash(s),
            delayed: 0.0,
            zen: true,
        };
        let mut overlay = Vec::new();
        hud::draw(
            &engine.view.borrow(),
            &state,
            w as f32,
            h as f32,
            &mut overlay,
        );
        out.draw(&Frame {
            clear: CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        times.push((thread_cpu_s() - t0) * 1e3);
    }
    let r = engine.stats;
    times.sort_by(f64::total_cmp);
    let at = |p: f64| times[((times.len() - 1) as f64 * p) as usize];
    println!(
        "bench: frame work (sim, evaluate, encode) at 1920 x 1080: median {:.2} ms, p95 {:.2}, p99 {:.2}, max {:.2} ({} frames); {} rollbacks re-simulating {:.0} ms",
        at(0.5),
        at(0.95),
        at(0.99),
        at(1.0),
        times.len(),
        r.rollbacks,
        r.resim_s * 1e3
    );
    if check && at(0.99) > FRAME_WORK_GATE_MS {
        return Err(format!(
            "frame work p99 {:.2} ms over the gate {FRAME_WORK_GATE_MS} ms",
            at(0.99)
        ));
    }
    Ok(())
}

/// 20 s of the house track's committed stream through the engine (a stride on each beat), drawn
/// offscreen to a PNG, in zen mode or not.
fn still(
    path: &Path,
    track: Option<&str>,
    at: Option<f64>,
    zen: bool,
    level: Option<&Path>,
) -> Result<(), String> {
    let world = World::load(level)?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let stream_path = Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
        "../../../public/reactive/{}/stream.json",
        track.unwrap_or("house-128")
    ));
    let text = std::fs::read_to_string(&stream_path)
        .map_err(|e| format!("{}: {e}", stream_path.display()))?;
    let file = stream::StreamFile::parse(&text).map_err(|e| e.0)?;
    let mut engine = world.engine(zen)?;
    let mut replay = stream::Replay::new(file.log.clone());
    let beats: Vec<f64> = file.log.beats.iter().map(|b| b.t).collect();
    let mut scene = Scene::default();
    let mut next = 0;
    let end = at.unwrap_or(20.0);
    for frame in 1..=((end * 60.0) as usize) {
        let s = frame as f64 / 60.0;
        engine.feed(|log| replay.feed_to(log, s));
        while next < beats.len() && beats[next] <= s {
            let key = if next % 4 < 2 {
                InputKey::Right
            } else {
                InputKey::Down
            };
            engine.key(beats[next], key, true);
            engine.key(beats[next] + 0.08, key, false);
            next += 1;
        }
        engine.advance_to(s);
        let moments = engine.view.borrow_mut().take_moments();
        scene.moments(&moments, s, &world.lib, &world.looks, &engine.view.borrow());
        scene.level(&engine.view.borrow(), s, &world.lib);
    }
    let upcoming: Vec<f64> = beats
        .iter()
        .map(|b| b - end)
        .filter(|d| d.abs() < 2.5)
        .collect();
    // (A chosen track and time renders at the trailer's size, to set beside its frames.)
    let (w, h) = if track.is_some() {
        (1920, 1080)
    } else {
        (960, 640)
    };
    let mut layers = scene::Layers::default();
    let period = file
        .log
        .beats
        .iter()
        .rev()
        .find(|b| b.t <= end)
        .map_or(0.5, |b| b.period);
    let ground = world.field(&engine, end, period)?;
    let last = beats
        .iter()
        .copied()
        .filter(|&b| b <= end)
        .fold(0.0, f64::max);
    let beat_phase = (end - last) / 0.47;
    let bar = beats
        .iter()
        .filter(|&&b| b <= end)
        .count()
        .saturating_sub(1)
        % 4;
    let heard = beats.iter().filter(|&&b| b <= end).count();
    let pulse = scene::Pulse {
        beat: beat_phase,
        bar: (bar as f64 + beat_phase.min(1.0)) / 4.0,
        index: heard.saturating_sub(1) as f64 + beat_phase.min(1.0),
    };
    let wave = engine.view.borrow().wave.as_ref().map(|v| v.s);
    scene.fast_lane(engine.stream(), GATE, end, end, wave);
    scene.draw(
        &engine.view.borrow(),
        &ground,
        &art,
        &world.looks,
        &world.lib,
        end,
        pulse,
        w as f32,
        h as f32,
        &mut layers,
    );
    let mut keys = [KeyLight::default(); 5];
    keys[3] = KeyLight {
        held: true,
        rating: Some("perfect"),
    };
    let presses = [PressMark {
        age: 0.1,
        delta: -0.012,
        rating: Some("great"),
    }];
    let state = HudState {
        keys,
        presses: &presses,
        beats: &upcoming,
        beat_phase,
        elapsed: end,
        bitten_ago: f64::INFINITY,
        head: layers.head,
        next_down: bar == 3,
        tier_flash: scene.tier_flash(end),
        delayed: 0.0,
        zen,
    };
    let mut overlay = Vec::new();
    hud::draw(
        &engine.view.borrow(),
        &state,
        w as f32,
        h as f32,
        &mut overlay,
    );
    let frame = Frame {
        clear: CLEAR,
        world: &layers.sprites,
        lights: &layers.lights,
        grade: layers.grade,
        overlay: &overlay,
    };
    let rgba = render::capture::render(&art.pages, w, h, &frame)?;
    render::capture::write_png(path, w, h, &rgba)?;
    let v = engine.view.borrow();
    eprintln!(
        "slice: wrote {} ({} sprites, {} lights; {} enemies, health {:.2}, groove {:.2}; zone {})",
        path.display(),
        layers.sprites.len() + overlay.len(),
        layers.lights.len(),
        v.alive().count(),
        v.health,
        v.groove,
        v.zone.as_deref().unwrap_or("first")
    );
    Ok(())
}

/// Watch my run: the run replayed from its arrivals and keys on a fresh engine.
struct Watch {
    began: f64,
    s0: f64,
    span: f64,
    arrivals: usize,
    keys: usize,
}

/// A calibration screen, between runs (docs/design/native-app.md, "Calibration (D2)").
#[derive(Debug)]
enum Screen {
    /// The tap-along: the clicks' heard times (host s), the taps (host s, a stomp?), and what it
    /// found once over (the line to show and until when).
    Taps {
        clicks: Vec<f64>,
        taps: Vec<(f64, bool)>,
    },
    /// The visual test: a click and a flash on each beat (heard at `first + k * 0.5`), the flash
    /// nudged by the offset being tried (ms).
    Visual { first: f64, scheduled: f64, ms: f64 },
    /// A result shown until host time `until`.
    Said { text: String, until: f64 },
}

/// The automatic start (docs/design/native-app.md, "Runs on live audio (D1)"), headless only: a
/// run starts once the evidence has stayed at or above the gate for 2 s after at least 1.5 s of
/// quiet, or after the previous run's end (or the app's start). With a window, Enter or Space
/// starts a run.
#[derive(Debug)]
struct Listen {
    /// The latest tick's evidence, where the clear stretch began (song s), where the quiet one
    /// began, and whether a quiet stretch long enough came before the clear one.
    evidence: f64,
    clear_since: Option<f64>,
    quiet_since: Option<f64>,
    quiet_held: bool,
}

impl Default for Listen {
    fn default() -> Listen {
        // (The app's start counts as an end: music already playing starts the first run.)
        Listen {
            evidence: 0.0,
            clear_since: None,
            quiet_since: None,
            quiet_held: true,
        }
    }
}

/// The start rule's times (s).
const START_CLEAR_S: f64 = 2.0;
const START_QUIET_S: f64 = 1.5;
/// How long a new run waits behind the end card, at least (host s).
const END_CARD_S: f64 = 3.0;
/// How far back the entries kept for a run's start reach (song s).
const RECENT_S: f64 = 20.0;

impl Listen {
    /// A tick of evidence `evidence` for song time `t`.
    fn tick(&mut self, t: f64, evidence: f64) {
        self.evidence = evidence;
        if evidence >= GATE {
            if let Some(q) = self.quiet_since.take()
                && t - q >= START_QUIET_S - 1e-9
            {
                self.quiet_held = true;
            }
            self.clear_since.get_or_insert(t);
        } else {
            self.clear_since = None;
            self.quiet_since.get_or_insert(t);
        }
    }

    /// Whether a run should start at song time `song`.
    fn ready(&self, song: f64) -> bool {
        self.quiet_held
            && self
                .clear_since
                .is_some_and(|c| song - c >= START_CLEAR_S - 1e-9)
    }

    /// A run ended (or started): the next needs its own clear stretch after it.
    fn reset(&mut self, ended: bool) {
        self.clear_since = None;
        self.quiet_held = ended;
    }
}

#[allow(clippy::struct_excessive_bools)]
struct App {
    /// Key press times from the window server, and the player's calibration.
    key_times: keys::KeyTimes,
    calibration: Calibration,
    /// The saved settings, the output device's UID and whether it is Bluetooth, and the command
    /// line's offsets (kept over each device's).
    settings: Settings,
    /// The session's record and its timing log, the index of its run going, and the engine's
    /// counters at the last frame (to log each frame's rollbacks).
    session: Option<Session>,
    timing: Option<Timing>,
    last_stats: eval::live::RunnerStats,
    run_index: usize,
    /// The run's own arrivals and applied keys, for Watch my run, and the replay showing (when it
    /// began, host s, and how far its feeds have got).
    run_arrivals: Vec<Arrival>,
    run_keys: Vec<sim::player::KeyEvent>,
    /// The run's music: the analyzer's input since the run began (44.1 kHz mono), the song time
    /// of its first sample, and the samples analysed so far this session. Watch my run plays it.
    run_audio: Vec<i16>,
    run_audio_s0: f64,
    analysed: u64,
    /// The replay's music the mixer holds (kept here, so the render thread never frees it).
    replay_music: Option<Arc<[f32]>>,
    watch: Option<Watch>,
    /// The run's kills when it ended, and whether to watch each run as it ends (`--watch`).
    run_kills: usize,
    auto_watch: bool,
    /// Zen mode for the next run (the default; `--legacy` or Z between runs for legacy mode), and
    /// whether the run (or the last) is in it.
    zen: bool,
    run_zen: bool,
    /// The listening mode asked for, and the app Delayed listening taps in System mode (without
    /// one, System mode listens Direct); the tap check's child; when to stall, for the check.
    listening: Listening,
    tap_target: Option<apps::AudioApp>,
    child: Option<std::process::Child>,
    stall_at: f64,
    /// The apps with audio, as last looked (host s), when each started playing (by its
    /// responsible process), and a note for the listening screen until a host time.
    apps: Vec<apps::AudioApp>,
    apps_at: f64,
    playing_since: HashMap<i32, f64>,
    note: Option<(String, f64)>,
    /// A calibration screen showing, if any.
    screen: Option<Screen>,
    device_uid: String,
    bluetooth: bool,
    overrides: (Option<f64>, Option<f64>),
    gfx: Option<Gfx>,
    sounds: Option<Sounds>,
    art: Atlases,
    world: World,
    audio: Audio,
    /// The sound effects (for a new output), the file's volume, and the event loop's waker.
    bank: sounds::Bank,
    volume: f32,
    proxy: winit::event_loop::EventLoopProxy<()>,
    /// The bundled tracks File mode cycles through, the next one, and whether the debug menu shows.
    tracks: Vec<PathBuf>,
    track: usize,
    debug: bool,
    click: bool,
    /// Song time heard at a host time (the analysis thread's latest anchor).
    clock: Option<(f64, f64)>,
    engine: Engine,
    /// The song time the run began at (the engine's song time 0).
    run_start: f64,
    scene: Scene,
    /// Committed beats' song times, recent and upcoming.
    beats: VecDeque<f64>,
    /// The loudest sample heard in the last moments.
    peak: f32,
    /// When sound was last heard (host s).
    last_sound: f64,
    /// Whether a run is going; between runs the app listens (the analyzer always runs).
    running: bool,
    /// The entries of the last moments with their arrival (song s), to feed a run that starts
    /// now; and the start rule's state.
    recent: VecDeque<(f64, eval::session::Entry)>,
    listen: Listen,
    start_at: f64,
    pending_start: Option<f64>,
    /// When the run ended (host s), and whether the hero fell.
    over: Option<(f64, End, f64)>,
    title_at: f64,
    /// The next voice's identity, and how far ahead of now the output is rendering (s): a sound
    /// scheduled that far ahead lands on its sample.
    next_voice: u64,
    ahead: f64,
    /// Voices started, skipped (onset passed), dropped (no room) and cancelled (a rollback undid
    /// them) this run; muted, they're logged
    /// every 10 s (at host `voices_at`), for a check without sound.
    voices: [u32; 4],
    voices_at: f64,
    mute: bool,
    /// The output device in use, checked once a second (host `device_at`): a change ends the run.
    device: coreaudio::Id,
    device_at: f64,
    /// Each frame's own work since the last log (ms).
    frame_ms: Vec<f64>,
    /// Frames are paced by a timer, not the display, while the window is hidden or occluded (it
    /// gives no drawable to wait on); the next one is due at host `next_frame`.
    paced: bool,
    occluded: bool,
    /// No window: frames on the timer, drawn nowhere; and when to exit (host s), if asked.
    headless: bool,
    exit_at: f64,
    switch_at: f64,
    /// The stand-in player (`--press-beats`): the last beat it pressed on (song s).
    press_beats: bool,
    pressed_beat: f64,
    /// Whether the window has the keyboard.
    focused: bool,
    next_frame: f64,
    /// The key display: up, left, down, right, stomp.
    keys: [KeyLight; 5],
    /// Recent presses for the beat track: when (host s), how far off, their rating.
    presses: VecDeque<(f64, f64, Option<&'static str>)>,
}

impl App {
    /// The song time heard at host time `now`, once the analysis has begun.
    fn song_at(&self, now: f64) -> Option<f64> {
        self.clock.map(|(song, heard)| song + (now - heard))
    }

    fn drain(&mut self) {
        let now = now_s();
        while let Ok(m) = self.audio.rx.try_recv() {
            match m {
                Msg::Clock { song, heard } => self.clock = Some((song, heard)),
                Msg::Entry(e) => {
                    let arrived = self.song_at(now).unwrap_or(0.0);
                    if let Entry::Beat(c) = &e {
                        if self.click {
                            let heard = self.clock.map_or(now, |(song, h)| h + (c.t - song));
                            self.voice(Voiced::Click(1800.0), heard - self.audio.l_out, 0.0, 1.0);
                        }
                        self.beats.push_back(c.t);
                    }
                    if let Entry::Tick(t) = &e {
                        self.listen.tick(t.t, t.evidence);
                    }
                    if let Some((g, t)) = content_time(&e) {
                        self.log(serde_json::json!({"k": "arr", "s": arrived, "g": g, "lag": arrived - t}));
                    }
                    let entry = eval::session::from_analyzer(&e).stamped(arrived);
                    if self.running {
                        let start = self.run_start;
                        self.engine
                            .feed(|log| eval::session::rebase_append(log, &entry, start));
                    }
                    let arrival = Arrival { arrived, entry };
                    if let Some(s) = self.session.as_mut() {
                        s.arrival(&arrival);
                    }
                    if self.running {
                        self.run_arrivals.push(arrival.clone());
                    }
                    self.recent.push_back((arrival.arrived, arrival.entry));
                    while self
                        .recent
                        .front()
                        .is_some_and(|(a, _)| *a < arrived - RECENT_S)
                    {
                        self.recent.pop_front();
                    }
                }
                Msg::Follow(g) => {
                    let _ = self.audio.out.commands.push(Command::Follow(g));
                }
                Msg::Audio(samples) => {
                    if let Some(s) = self.session.as_mut() {
                        s.audio(&samples);
                    }
                    // (A run keeps its music for Watch my run, up to RUN_AUDIO_MAX_S.)
                    if self.running && self.run_audio.len() < (RUN_AUDIO_MAX_S * SR) as usize {
                        if self.run_audio.is_empty() {
                            self.run_audio_s0 = self.analysed as f64 / SR;
                        }
                        let remaining = (RUN_AUDIO_MAX_S * SR) as usize - self.run_audio.len();
                        self.run_audio.extend(
                            samples
                                .iter()
                                .take(remaining)
                                .map(|x| (x.clamp(-1.0, 1.0) * 32767.0).round() as i16),
                        );
                    }
                    self.analysed += samples.len() as u64;
                }
                Msg::Level { peak, heard } => {
                    self.log(serde_json::json!({"k": "cap", "h": heard}));
                    if heard <= now + 0.5 {
                        self.peak = self.peak.max(peak);
                    }
                    if peak >= SOUND_PEAK {
                        self.last_sound = self.last_sound.max(heard);
                    }
                }
            }
        }
        if let Some(song) = self.song_at(now) {
            while self.beats.front().is_some_and(|&b| b < song - 2.0) {
                self.beats.pop_front();
            }
        }
        while let Ok(e) = self.audio.out.events.pop() {
            match e {
                Event::Buffer { host, frames, now } => {
                    self.ahead = host + f64::from(frames) / self.audio.out.rate - now;
                }
                Event::Voice(Report::Started { .. }) => self.voices[0] += 1,
                Event::Voice(Report::Skipped { .. }) => self.voices[1] += 1,
                Event::Voice(Report::Dropped { .. }) => self.voices[2] += 1,
                Event::Voice(Report::Cancelled { .. }) => self.voices[3] += 1,
                Event::Voice(Report::Delay { fill, underrun }) => {
                    self.log(
                        serde_json::json!({"k": "delay", "fill": fill * 1e3, "underrun": underrun}),
                    );
                }
                Event::Voice(Report::ReplayStarted { at, actual }) => {
                    self.log(
                        serde_json::json!({"k": "replay", "at": at, "late": (actual - at) * 1e3}),
                    );
                    eprintln!(
                        "slice: the replay's music started ({:.1} ms late)",
                        (actual - at) * 1e3
                    );
                }
            }
        }
    }

    fn render(&mut self) {
        let t0 = std::time::Instant::now();
        let now = now_s();
        self.peak *= 0.97;
        // (Drawn for when the display shows it: the player's display offset later.)
        let song = self.song_at(now + self.calibration.visual_s());
        if self.running
            && let Some(s) = song
        {
            // Ahead of the time heard by the output's latency at least, so a sound is known
            // before the output must render it (docs/design/native-app.md, "Arrivals roll back
            // like late keys").
            // (The stand-in player presses on each committed beat once it is 20 ms past (a real
            // key's delivery), and
            // releases 80 ms after it: the keys reach the engine late, as real ones do.)
            if self.press_beats {
                let due: Vec<f64> = self
                    .beats
                    .iter()
                    .copied()
                    .filter(|&b| b > self.pressed_beat && b + 0.02 <= s && b >= self.run_start)
                    .collect();
                for b in due {
                    let key = if (b * 2.0) as i64 % 2 == 0 {
                        InputKey::Right
                    } else {
                        InputKey::Down
                    };
                    self.engine.key(b - self.run_start, key, true);
                    self.engine.key(b + 0.08 - self.run_start, key, false);
                    self.pressed_beat = b;
                }
            }
            let lead = 0.2f64.max(self.audio.l_out + self.ahead.max(0.0) + 0.02);
            self.engine.frame(s - self.run_start, lead);
            self.record_keys();
            let (r, was) = (self.engine.stats, self.last_stats);
            if r.rollbacks > was.rollbacks {
                self.log(serde_json::json!({
                    "k": "rb",
                    "s": s,
                    "n": r.rollbacks - was.rollbacks,
                    "keys": r.key_rollbacks - was.key_rollbacks,
                    "replayed": r.replayed_s - was.replayed_s,
                    "resim": (r.resim_s - was.resim_s) * 1e3,
                    "deepest": r.deepest_s,
                }));
            }
            self.last_stats = r;
            let moments = self.engine.view.borrow_mut().take_moments();
            self.cues(&moments, now);
            self.schedule_sounds();
            self.scene.moments(
                &moments,
                now,
                &self.world.lib,
                &self.world.looks,
                &self.engine.view.borrow(),
            );
            self.scene
                .level(&self.engine.view.borrow(), now, &self.world.lib);
            let end = if self.engine.fallen() {
                Some(End::Fell)
            } else if self.engine.done() {
                Some(if self.world.survivors() {
                    End::Victory
                } else {
                    End::Song
                })
            } else {
                None
            };
            if let Some(end) = end {
                if end == End::Fell {
                    self.sound_now("boss-slam", 1.0);
                }
                self.end_run(end, now);
            }
        }
        // A new output device ends the run: the stream's heard times carry the old latency.
        if now > self.device_at {
            self.device_at = now + 1.0;
            let device = coreaudio::default_output();
            if device != self.device {
                self.device = device;
                let d = coreaudio::device(device);
                self.calibration = calibration_for(&self.settings, &d, self.overrides);
                self.device_uid.clone_from(&d.uid);
                self.bluetooth = bluetooth(&d);
                let l_out = Output::reported_latency_s() + self.calibration.latency_correction_s();
                eprintln!(
                    "slice: output now {} ({} Hz), L_out {:.1} ms{}",
                    d.name,
                    d.rate,
                    l_out * 1e3,
                    if (d.rate - self.audio.out.rate).abs() > 0.5 {
                        "; its rate differs from the mixer's: restart the app"
                    } else {
                        ""
                    }
                );
                self.audio.l_out = l_out;
                self.audio.heard_after.store(
                    (l_out + self.audio.heard_offset).to_bits(),
                    Ordering::Relaxed,
                );
                self.log(serde_json::json!({"k": "dev", "name": d.name, "lOut": l_out * 1e3, "heardAfter": (l_out + self.audio.heard_offset) * 1e3}));
                self.end_run(End::Device, now);
                self.engine.settle_s = self.settle_s();
            }
        }
        self.watch_tick(now);
        self.screen_tick(now);
        // Listening, headless (no keys to press): the next run starts on its own once the music is
        // clear (after the end card), unless a calibration screen shows. With a window, the
        // player starts each run.
        if self.headless
            && !self.running
            && self.screen.is_none()
            && self.watch.is_none()
            && let Some(s) = song
            && self.listen.ready(s)
            && s >= self.start_at
            && self.over.is_none_or(|(t, _, _)| now > t + END_CARD_S)
        {
            self.request_start(s);
        }
        if !self.running
            && let (Some(requested), Some(s)) = (self.pending_start, song)
            && let Some(beat) = self
                .beats
                .iter()
                .copied()
                .find(|b| *b > requested && *b <= s)
        {
            self.start(beat);
        }
        if self.mute && now > self.voices_at {
            self.voices_at = now + 10.0;
            let r = self.engine.stats;
            let mut f = std::mem::take(&mut self.frame_ms);
            f.sort_by(f64::total_cmp);
            let at = |p: f64| {
                f.get(((f.len().max(1) - 1) as f64 * p) as usize)
                    .copied()
                    .unwrap_or(0.0)
            };
            eprintln!(
                "slice: frame work median {:.2} ms, p95 {:.2}, p99 {:.2} ({:.0} frames a second{}); rollbacks re-simulated {:.0} ms",
                at(0.5),
                at(0.95),
                at(0.99),
                f.len() as f64 / 10.0,
                if self.paced {
                    ", paced while hidden"
                } else {
                    ""
                },
                r.resim_s * 1e3
            );
            eprintln!(
                "slice: sounds {} started, {} skipped late, {} dropped, {} cancelled; {} rollbacks replaying {:.1} s, {} late keys",
                self.voices[0],
                self.voices[1],
                self.voices[2],
                self.voices[3],
                r.rollbacks,
                r.replayed_s,
                r.late_keys
            );
        }
        if now > self.title_at {
            self.title_at = now + 0.5;
            if let Some(g) = &self.gfx {
                g.window.set_title(&format!(
                    "Playlist Survivors - {} enemies, health {:.0}%",
                    self.engine.view.borrow().alive().count(),
                    self.engine.view.borrow().health * 100.0
                ));
            }
        }
        let debug_lines = self.debug.then(|| self.debug_lines());
        let mut screen_sprites = Vec::new();
        if let Some((w, h)) = self.gfx.as_ref().map(Gfx::logical_size) {
            self.screen_card(w, h, now, &mut screen_sprites);
        }
        // (The listening screen shows which apps can be tapped.)
        if !self.running && self.over.is_none() {
            self.poll_apps(now, false);
            if self.note.as_ref().is_some_and(|n| now > n.1) {
                self.note = None;
            }
        }
        let Some(g) = self.gfx.as_mut() else {
            // (Headless: the frame's work is the engine's and the sounds'.)
            let built = t0.elapsed().as_secs_f64() * 1e3;
            if self.mute {
                self.frame_ms.push(built);
            }
            let song_now = self.song_at(now);
            self.log(serde_json::json!({"k": "frame", "s": song_now, "build": built}));
            return;
        };
        let (w, h) = g.logical_size();
        let upcoming: Vec<f64> =
            song.map_or_else(Vec::new, |s| self.beats.iter().map(|b| b - s).collect());
        let run_s = song.map_or(0.0, |s| s - self.run_start);
        let period = self.engine.stream().beats.last().map_or(0.5, |b| b.period);
        let Ok(ground) = self.world.field(&self.engine, run_s, period) else {
            return;
        };
        // The beat phase from the last beat heard and the gap to the next; bars of four from the
        // first beat.
        let (phase, bar, index) = song.map_or((1.0, 1.0, 0.0), |s| {
            let heard = self.beats.iter().filter(|&&b| b <= s).count();
            let last = heard.checked_sub(1).map(|k| self.beats[k]);
            match (last, self.beats.get(heard)) {
                (Some(last), Some(&next)) => {
                    let phase = (s - last) / (next - last).max(1e-3);
                    (
                        phase,
                        (((heard - 1) % 4) as f64 + phase.min(1.0)) / 4.0,
                        (heard - 1) as f64 + phase.min(1.0),
                    )
                }
                _ => (1.0, 1.0, heard as f64),
            }
        });
        let wave = self.engine.view.borrow().wave.as_ref().map(|v| v.s);
        self.scene
            .fast_lane(self.engine.stream(), GATE, run_s, now, wave);
        let mut layers = scene::Layers::default();
        self.scene.draw(
            &self.engine.view.borrow(),
            &ground,
            &self.art,
            &self.world.looks,
            &self.world.lib,
            now,
            scene::Pulse {
                beat: phase,
                bar,
                index,
            },
            w,
            h,
            &mut layers,
        );
        let presses: Vec<PressMark> = self
            .presses
            .iter()
            .map(|&(t, delta, rating)| PressMark {
                age: now - t,
                delta,
                rating,
            })
            .collect();
        let state = HudState {
            keys: self.keys,
            presses: &presses,
            beats: &upcoming,
            beat_phase: phase,
            elapsed: song.map_or(0.0, |s| s - self.run_start),
            bitten_ago: now - self.scene.bitten(),
            head: layers.head.filter(|_| self.over.is_none()),
            next_down: bar >= 0.75,
            tier_flash: self.scene.tier_flash(now),
            delayed: delay_of(self.audio.listening),
            zen: self.run_zen,
        };
        let mut overlay = Vec::new();
        if self.running || self.over.is_some() {
            hud::draw(&self.engine.view.borrow(), &state, w, h, &mut overlay);
        }
        if !self.running && self.over.is_none() {
            let tapped = self.audio.app.as_deref();
            let others = self
                .apps
                .iter()
                .filter(|a| a.playing && Some(a.name.as_str()) != tapped)
                .count();
            let mode = scene::ListeningMode {
                delayed: self.audio.listening != Listening::Direct,
                m: DelayedParams::chosen().m,
                file: self.audio.file.is_some(),
                app: tapped,
                browser: self
                    .tap_target
                    .as_ref()
                    .is_some_and(apps::AudioApp::browser),
                others,
                note: self.note.as_ref().map(|n| n.0.as_str()),
                zen: self.zen,
            };
            scene::listening_card(
                w,
                h,
                self.listen.evidence,
                GATE,
                now,
                self.bluetooth,
                &mode,
                &mut overlay,
            );
        }
        overlay.extend(screen_sprites);
        if let Some(lines) = debug_lines {
            let u = (w / 960.0).min(h / 540.0);
            overlay.push(gfx::rect(
                8.0 * u,
                40.0 * u,
                520.0 * u,
                (14.0 * lines.len() as f32 + 8.0) * u,
                [0.0, 0.0, 0.0, 0.75],
            ));
            for (k, line) in lines.iter().enumerate() {
                render::font::text(
                    line,
                    14.0 * u,
                    (44.0 + 14.0 * k as f32) * u,
                    u.round().max(1.0),
                    render::font::Align::Left,
                    [0.85, 0.95, 0.85, 1.0],
                    &mut overlay,
                );
            }
        }
        if self.watch.is_some() {
            let u = (w / 960.0).min(h / 540.0);
            render::font::text(
                "REPLAY   ESC STOPS",
                w / 2.0,
                h - 40.0 * u,
                (2.0 * u).round().max(1.0),
                render::font::Align::Centre,
                [1.0, 0.85, 0.4, 1.0],
                &mut overlay,
            );
        } else if let Some((t, end, seconds)) = self.over {
            scene::end_card(
                w,
                h,
                now - t,
                seconds.max(0.0) as u64,
                end,
                &self.engine.view.borrow(),
                &mut overlay,
            );
        }
        overlay.push(gfx::rect(
            0.0,
            h - 4.0,
            w * self.peak.min(1.0),
            4.0,
            [0.3, 0.8, 0.4, 0.8],
        ));
        let built = t0.elapsed().as_secs_f64();
        let drawn = g.draw(&Frame {
            clear: CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        // (Sim, evaluate and encode: the frame's own work, the drawable's wait apart; logged muted.)
        self.paced = drawn.is_none() || self.occluded;
        let song_now = self.song_at(now);
        self.log(serde_json::json!({
            "k": "frame",
            "s": song_now,
            "build": built * 1e3,
            "encode": drawn.map(|e| e * 1e3),
        }));
        if self.mute {
            self.frame_ms.push((built + drawn.unwrap_or(0.0)) * 1e3);
        }
    }

    /// The key display and the beat track's presses, from the view's new moments.
    fn cues(&mut self, moments: &[Moment], now: f64) {
        for m in moments {
            if let Moment::Press { rating, delta, .. } = m {
                self.presses.push_back((now, *delta, *rating));
                // (The keys held show the press's rating.)
                for k in self.keys.iter_mut().filter(|k| k.held) {
                    k.rating = *rating;
                }
            }
        }
        while self.presses.front().is_some_and(|p| now - p.0 > 1.0) {
            self.presses.pop_front();
        }
    }

    /// Brings the scheduled sounds in line with the simulation's speculation, for when they're
    /// heard.
    fn schedule_sounds(&mut self) {
        let (Some(sounds), Some(clock)) = (self.sounds.as_mut(), self.clock) else {
            return;
        };
        let caused: Vec<_> = self.engine.causes().cloned().collect();
        let camera = self.engine.view.borrow().camera;
        let seed = self.engine.seed;
        let at = sounds::At {
            seed,
            run_start: self.run_start,
            beats: &self.engine.stream().beats,
            gate: GATE,
            camera,
            clock,
            l_out: self.audio.l_out,
            // (Known in time for the output to render it: past its latency and lookahead.)
            never_late: sounds::NEVER_LATE.max(self.audio.l_out + self.ahead.max(0.0) + 0.005),
            now: now_s(),
            ahead: self.ahead.max(0.0),
        };
        let mut commands = Vec::new();
        sounds.reconcile(&caused, &at, &mut commands);
        for c in commands {
            self.send(c);
        }
    }

    /// A sound as soon as the output can play it on its sample.
    fn sound_now(&mut self, key: &str, gain: f64) {
        let at = now_s() + self.ahead.max(0.0) + 0.001;
        let mut commands = Vec::new();
        if let Some(s) = self.sounds.as_mut() {
            s.now(key, at, gain, &mut commands);
        }
        for c in commands {
            self.send(c);
        }
    }

    /// Schedules `source` to start at host time `at` (on the output's timeline), `from` samples
    /// in.
    fn voice(&mut self, source: Voiced, at: f64, from: f64, gain: f32) {
        self.next_voice += 1;
        self.send(Command::Play(Voice {
            id: self.next_voice,
            source,
            at,
            from,
            lead: 0.0,
            gain,
            pan: 0.0,
            rate: 1.0,
            duck: false,
        }));
    }

    /// The engine's settling window: no key arrives later than this after its (calibrated) time.
    fn settle_s(&self) -> f64 {
        let offset = self
            .calibration
            .offset_s(InputKey::Up)
            .max(self.calibration.offset_s(InputKey::Space))
            .max(0.0);
        eval::live::SETTLE_S + KEY_LATE_S + offset + self.calibration.visual_s().max(0.0)
    }

    /// Starts the session's record and timing log (ending any before).
    fn begin_session(&mut self) {
        self.timing = None;
        self.session = None;
        self.session = self.new_session();
        self.timing = self.session.as_ref().and_then(|s| Timing::open(s.dir()));
        let name = coreaudio::device(self.device).name;
        let heard_after = f64::from_bits(self.audio.heard_after.load(Ordering::Relaxed));
        let listening = session::listening_name(self.audio.listening);
        let m = delay_of(self.audio.listening);
        self.log(serde_json::json!({"k": "dev", "name": name, "lOut": self.audio.l_out * 1e3, "heardAfter": heard_after * 1e3, "listening": listening, "m": m}));
    }

    /// A timing event.
    #[allow(clippy::needless_pass_by_value)]
    fn log(&mut self, v: serde_json::Value) {
        if let Some(t) = self.timing.as_mut() {
            t.event(&v);
        }
    }

    /// Sends a command to the output, logging a voice's schedule margin or a cancellation.
    fn send(&mut self, c: Command) {
        let now = now_s();
        let s = self.song_at(now);
        match c {
            // (How long before its onset must be rendered: a voice may start partway while its
            // onset is still ahead.)
            Command::Play(v) => {
                self.log(
                    serde_json::json!({"k": "snd", "s": s, "margin": (v.at + v.lead - now) * 1e3}),
                );
            }
            Command::Cancel(_) => self.log(serde_json::json!({"k": "cancel", "s": s})),
            _ => {}
        }
        let _ = self.audio.out.commands.push(c);
    }

    /// A new session's record, for the source and device now.
    fn new_session(&self) -> Option<Session> {
        Session::start(session::Meta {
            version: env!("CARGO_PKG_VERSION").to_string(),
            started_unix: session::unix_now(),
            file: self.audio.file.as_ref().map(|p| p.display().to_string()),
            listening: session::listening_name(self.audio.listening).to_string(),
            m: (self.audio.listening != Listening::Direct).then(|| delay_of(self.audio.listening)),
            app: self.audio.app.clone(),
            device: coreaudio::device(self.device).name,
            l_out_ms: self.audio.l_out * 1e3,
            calibration: self.calibration,
            runs: Vec::new(),
        })
    }

    /// Starts Watch my run: the run just ended, replayed from its own arrivals and keys.
    fn watch_run(&mut self, now: f64) {
        let Some((_, _, seconds)) = self.over else {
            return;
        };
        let Ok(mut engine) = self.world.engine(self.run_zen) else {
            return;
        };
        engine.settle_s = self.settle_s();
        self.engine = engine;
        self.scene = Scene::default();
        // The run's music under it, heard from the replay's start: a little ahead, so the mixer
        // can start it on time.
        let began = now + REPLAY_LEAD_S;
        let skip = ((self.run_start - self.run_audio_s0) * SR).round() as i64;
        let music = replay_music(&self.run_audio, skip, seconds + 0.5, self.audio.out.rate);
        self.replay_music = Some(Arc::clone(&music));
        self.send(Command::Replay {
            samples: music,
            at: began - self.audio.l_out,
        });
        self.watch = Some(Watch {
            began,
            s0: self.run_start,
            span: seconds,
            arrivals: 0,
            keys: 0,
        });
    }

    /// Looks at the apps with audio again (every second, or now if `force`), noting when each
    /// started playing.
    fn poll_apps(&mut self, now: f64, force: bool) {
        if !force && now - self.apps_at < 1.0 {
            return;
        }
        self.apps_at = now;
        self.apps = apps::list();
        let playing: Vec<i32> = self
            .apps
            .iter()
            .filter(|a| a.playing)
            .map(|a| a.pid)
            .collect();
        self.playing_since.retain(|pid, _| playing.contains(pid));
        for pid in playing {
            self.playing_since.entry(pid).or_insert(now);
        }
    }

    /// Delayed listening's app by default: the one that most recently started playing.
    fn default_app(&self) -> Option<apps::AudioApp> {
        self.apps
            .iter()
            .filter(|a| a.playing)
            .max_by(|a, b| {
                let since = |x: &apps::AudioApp| self.playing_since.get(&x.pid).copied();
                since(a)
                    .partial_cmp(&since(b))
                    .unwrap_or(std::cmp::Ordering::Equal)
            })
            .cloned()
    }

    /// L on the listening screen: Direct to Delayed (System mode: tapping the app that most
    /// recently started playing) and back; the run ends, as a source change does.
    fn toggle_listening(&mut self, now: f64) {
        let file = self.audio.file.clone();
        if self.audio.listening == Listening::Direct {
            if file.is_none() {
                self.poll_apps(now, true);
                let Some(app) = self.default_app() else {
                    self.note = Some((
                        "No app is playing: play something, then press L".into(),
                        now + 4.0,
                    ));
                    return;
                };
                self.tap_target = Some(app);
            }
            self.listening = Listening::Delayed(DelayedParams::chosen());
        } else {
            self.listening = Listening::Direct;
        }
        self.switch(file.as_deref());
    }

    /// P on the listening screen, tapping an app: the next playing app is tapped instead.
    fn next_app(&mut self, now: f64) {
        self.poll_apps(now, true);
        let playing: Vec<&apps::AudioApp> = self.apps.iter().filter(|a| a.playing).collect();
        let current = self.tap_target.as_ref().map(|a| a.pid);
        let at = playing.iter().position(|a| Some(a.pid) == current);
        let next = match at {
            Some(k) => playing.get((k + 1) % playing.len().max(1)),
            None => playing.first(),
        };
        if let Some(&app) = next.filter(|a| Some(a.pid) != current) {
            self.tap_target = Some(app.clone());
            self.switch(None);
        }
    }

    /// Ends Watch my run, and its music.
    fn stop_watch(&mut self) {
        if self.watch.take().is_some() {
            self.send(Command::StopReplay);
        }
    }

    /// Steps the replay showing to the time since it began, as the run went.
    fn watch_tick(&mut self, now: f64) {
        let Some(w) = self.watch.as_mut() else {
            return;
        };
        let t = (now - w.began).max(0.0);
        let s0 = w.s0;
        let fed = &self.run_arrivals[w.arrivals..];
        let n = fed.iter().take_while(|a| a.arrived - s0 <= t).count();
        if n > 0 {
            self.engine.feed(|log| {
                for a in &fed[..n] {
                    eval::session::rebase_append(log, &a.entry, s0);
                }
            });
            w.arrivals += n;
        }
        while let Some(k) = self.run_keys.get(w.keys).filter(|k| k.s <= t) {
            self.engine
                .key(k.s, k.key, k.kind == sim::player::KeyEventKind::Press);
            w.keys += 1;
        }
        self.engine.frame(t, 0.2);
        let moments = self.engine.view.borrow_mut().take_moments();
        self.scene.moments(
            &moments,
            now,
            &self.world.lib,
            &self.world.looks,
            &self.engine.view.borrow(),
        );
        if t > w.span + 0.5 {
            self.stop_watch();
            eprintln!(
                "slice: replay over: {} kills (the run had {})",
                self.engine.view.borrow().kills,
                self.run_kills
            );
        }
    }

    /// The keys the engine applied since the last call, into the session's record.
    fn record_keys(&mut self) {
        let run = self.run_index;
        if let Some(log) = self.engine.key_log.as_mut() {
            for k in log.drain(..) {
                if let Some(session) = self.session.as_mut() {
                    session.key(&session::key_of(run, &k));
                }
                self.run_keys.push(k);
            }
        }
    }

    /// Ends the run (host time `now`) for `end`: the end card shows, listening goes on.
    fn end_run(&mut self, end: End, now: f64) {
        self.pending_start = None;
        if self.over.is_some() {
            return;
        }
        let seconds = self.song_at(now).map_or(0.0, |s| s - self.run_start);
        self.record_keys();
        if let Some(s) = self.session.as_mut() {
            s.run_ended(
                self.run_start + seconds,
                &format!("{end:?}"),
                self.engine.settled_hash,
                self.engine.settled_count,
            );
        }
        self.log(serde_json::json!({"k": "end", "s": self.run_start + seconds, "why": format!("{end:?}")}));
        self.over = Some((now, end, seconds));
        self.run_kills = self.engine.view.borrow().kills;
        self.running = false;
        if self.auto_watch {
            self.watch_run(now);
        }
        self.listen.reset(true);
        let _ = self.audio.out.commands.push(Command::CancelAll);
        eprintln!(
            "slice: run over ({end:?}), {seconds:.0} s; sounds {} started, {} skipped late, {} dropped",
            self.voices[0], self.voices[1], self.voices[2]
        );
    }

    /// A key for a calibration screen (or C and V opening one while listening); whether it took
    /// it.
    fn screen_key(&mut self, code: KeyCode, pressed: bool, repeat: bool, now: f64) -> bool {
        if repeat {
            return self.screen.is_some();
        }
        let heard = |k: usize, first: f64| first + k as f64 * 60.0 / calibration::BPM;
        match &mut self.screen {
            None if pressed && !self.running && code == KeyCode::KeyC => {
                // Eight clicks, the first a second from now; the taps are compared with when
                // each is heard.
                let first = now + 1.0;
                let clicks: Vec<f64> = (0..calibration::CLICKS).map(|k| heard(k, first)).collect();
                for (k, &c) in clicks.iter().enumerate() {
                    let freq = if k % 4 == 0 { 2400.0 } else { 1800.0 };
                    self.voice(Voiced::Click(freq), c - self.audio.l_out, 0.0, 1.0);
                }
                self.screen = Some(Screen::Taps {
                    clicks,
                    taps: Vec::new(),
                });
                true
            }
            None if pressed && !self.running && code == KeyCode::KeyZ => {
                self.zen = !self.zen;
                true
            }
            None if pressed && !self.running && code == KeyCode::KeyL => {
                self.toggle_listening(now);
                true
            }
            None if pressed
                && !self.running
                && code == KeyCode::KeyP
                && self.audio.watchdog.is_some() =>
            {
                self.next_app(now);
                true
            }
            None if pressed && !self.running && code == KeyCode::KeyV => {
                self.screen = Some(Screen::Visual {
                    first: now + 0.5,
                    scheduled: f64::NEG_INFINITY,
                    ms: self.calibration.visual_offset_ms.unwrap_or(0.0),
                });
                true
            }
            None => false,
            Some(Screen::Taps { taps, .. }) => {
                if pressed {
                    match code {
                        KeyCode::Escape => self.screen = None,
                        KeyCode::Space => taps.push((now, true)),
                        KeyCode::ArrowLeft
                        | KeyCode::ArrowRight
                        | KeyCode::ArrowUp
                        | KeyCode::ArrowDown
                        | KeyCode::KeyA
                        | KeyCode::KeyD
                        | KeyCode::KeyW
                        | KeyCode::KeyS => taps.push((now, false)),
                        _ => {}
                    }
                }
                true
            }
            Some(Screen::Visual { ms, .. }) => {
                if pressed {
                    match code {
                        KeyCode::ArrowLeft => *ms -= 5.0,
                        KeyCode::ArrowRight => *ms += 5.0,
                        KeyCode::Enter => {
                            let ms = *ms;
                            self.save_calibration(|c| c.visual_offset_ms = Some(ms));
                            self.screen = Some(Screen::Said {
                                text: format!("DISPLAY OFFSET {ms:.0} MS SAVED"),
                                until: now + 3.0,
                            });
                        }
                        KeyCode::Escape => self.screen = None,
                        _ => {}
                    }
                }
                true
            }
            Some(Screen::Said { .. }) => {
                self.screen = None;
                false
            }
        }
    }

    /// Saves a change to the current device's calibration.
    fn save_calibration(&mut self, change: impl Fn(&mut Calibration)) {
        let mut c = self.settings.for_device(&self.device_uid);
        change(&mut c);
        self.settings.devices.insert(self.device_uid.clone(), c);
        if let Err(e) = self.settings.save() {
            eprintln!("slice: can't save the settings: {e}");
        }
        change(&mut self.calibration);
        self.engine.settle_s = self.settle_s();
    }

    /// The calibration screens' time passing: the tap-along's result once its clicks are over,
    /// the visual test's next clicks.
    fn screen_tick(&mut self, now: f64) {
        match &mut self.screen {
            Some(Screen::Taps { clicks, taps }) if now > clicks[clicks.len() - 1] + 0.6 => {
                let stomps = taps.iter().filter(|t| t.1).count();
                let group = calibration::tap_group(stomps, taps.len());
                let stomp = group == calibration::Group::Stomp;
                let times: Vec<f64> = taps.iter().filter(|t| t.1 == stomp).map(|t| t.0).collect();
                let which = if stomp { "STOMP KEY" } else { "ARROW" };
                let text = match calibration::tap_offset(&times, clicks) {
                    Some(ms) => {
                        self.save_calibration(|c| c.set_offset(group, ms));
                        format!(
                            "YOUR {which} TAPS LAND {:.0} MS {}: PLAY ALLOWS FOR IT",
                            ms.abs(),
                            if ms >= 0.0 { "LATE" } else { "EARLY" }
                        )
                    }
                    None => format!("NOT ENOUGH {which} TAPS ON THE CLICKS: NOTHING CHANGED"),
                };
                self.screen = Some(Screen::Said {
                    text,
                    until: now + 4.0,
                });
            }
            Some(Screen::Visual {
                first, scheduled, ..
            }) => {
                // Clicks a third of a second ahead, each on a beat.
                let period = 60.0 / calibration::BPM;
                let mut next = if scheduled.is_finite() {
                    *scheduled + period
                } else {
                    *first
                };
                let l_out = self.audio.l_out;
                let mut voices = Vec::new();
                while next < now + 0.35 {
                    voices.push(next - l_out);
                    *scheduled = next;
                    next += period;
                }
                for at in voices {
                    self.voice(Voiced::Click(1800.0), at, 0.0, 1.0);
                }
            }
            Some(Screen::Said { until, .. }) if now > *until => self.screen = None,
            _ => {}
        }
    }

    /// Draws the calibration screen showing.
    fn screen_card(&self, w: f32, h: f32, now: f64, out: &mut Vec<gfx::Sprite>) {
        let Some(screen) = &self.screen else { return };
        let u = (w / 960.0).min(h / 540.0);
        let px = |k: f32| (k * u).round().max(1.0);
        out.push(gfx::rect(0.0, 0.0, w, h, [0.02, 0.02, 0.04, 0.92]));
        let (cx, cy) = (w / 2.0, h / 2.0);
        let line = |text: &str, y: f32, k: f32, out: &mut Vec<gfx::Sprite>| {
            render::font::text(
                text,
                cx,
                cy + y * u,
                px(k),
                render::font::Align::Centre,
                [0.9, 0.9, 0.95, 1.0],
                out,
            );
        };
        match screen {
            Screen::Taps { taps, .. } => {
                line("TAP ALONG WITH THE 8 CLICKS", -60.0, 3.0, out);
                line(
                    "WITH THE KEY YOU PLAY WITH: AN ARROW, OR SPACE",
                    -20.0,
                    2.0,
                    out,
                );
                line(&format!("{} TAPS", taps.len()), 20.0, 2.0, out);
            }
            Screen::Visual { first, ms, .. } => {
                line("MOVE THE FLASH ONTO THE CLICK", -90.0, 3.0, out);
                line(
                    &format!("LEFT AND RIGHT: {ms:.0} MS   ENTER SAVES   ESC LEAVES"),
                    70.0,
                    2.0,
                    out,
                );
                // The flash for the beat heard at `first + k * period`, drawn `ms` later.
                let period = 60.0 / calibration::BPM;
                let since = (now - ms / 1000.0 - first).rem_euclid(period);
                if now - ms / 1000.0 >= *first && since < 0.08 {
                    let s = 80.0 * u;
                    out.push(gfx::rect(
                        cx - s / 2.0,
                        cy - s / 2.0,
                        s,
                        s,
                        [1.0, 1.0, 1.0, 1.0],
                    ));
                }
            }
            Screen::Said { text, .. } => line(text, 0.0, 2.0, out),
        }
    }

    /// Changes the music's source: `file` played through the output (File mode), or the Mac's own
    /// (System mode). The run ends; listening starts afresh on the new source.
    fn switch(&mut self, file: Option<&Path>) {
        let now = now_s();
        if self.running {
            self.end_run(End::Source, now);
        }
        // (Leaving a tap, the delay line's music fades out over 50 ms before the app is heard
        // live again.)
        if self.audio.watchdog.is_some() {
            self.send(Command::FadeDelayLine);
            std::thread::sleep(std::time::Duration::from_millis(60));
        }
        self.audio.shut_down();
        let start = |file: Option<&Path>, listening: Listening, app: Option<&apps::AudioApp>| {
            Audio::start(
                file,
                listening,
                app,
                self.volume,
                self.mute,
                self.calibration.latency_correction_s(),
                &self.bank,
                self.proxy.clone(),
            )
        };
        match start(file, self.listening, self.tap_target.as_ref()).or_else(|e| {
            eprintln!("slice: can't switch the music's source: {e}; back to System mode, Direct");
            start(None, Listening::Direct, None)
        }) {
            Ok(audio) => self.audio = audio,
            Err(e) => eprintln!("slice: no music source: {e}"),
        }
        self.clock = None;
        self.recent.clear();
        self.beats.clear();
        self.listen = Listen::default();
        self.device = coreaudio::default_output();
        // (A new source is a new session: its song time starts again.)
        self.analysed = 0;
        self.begin_session();
        // (Watch my run's music goes on through the new output, partway in if it is late.)
        if let (Some(w), Some(m)) = (&self.watch, &self.replay_music) {
            let (samples, at) = (Arc::clone(m), w.began - self.audio.l_out);
            self.send(Command::Replay { samples, at });
        }
    }

    /// The debug menu's lines.
    fn debug_lines(&self) -> Vec<String> {
        let r = self.engine.stats;
        let mode = self.audio.file.as_ref().map_or("SYSTEM".to_string(), |p| {
            format!(
                "FILE {}",
                p.file_stem()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_uppercase()
            )
        });
        vec![
            "DEBUG  F1 CLOSE  F2 NEXT FILE  F3 SYSTEM".to_string(),
            format!("MODE {mode}"),
            format!(
                "OUTPUT {:.0} HZ  L OUT {:.1} MS  HEARD {:.1} MS AFTER CAPTURE",
                self.audio.out.rate,
                self.audio.l_out * 1e3,
                f64::from_bits(self.audio.heard_after.load(Ordering::Relaxed)) * 1e3
            ),
            format!(
                "INPUT OFFSET {:.0} MS  STOMP {:.0} MS  SETTLE {:.0} MS",
                self.calibration.offset_s(InputKey::Up) * 1e3,
                self.calibration.offset_s(InputKey::Space) * 1e3,
                self.engine.settle_s * 1e3
            ),
            format!(
                "ROLLBACKS {}  REPLAYED {:.1} S  LATE KEYS {}",
                r.rollbacks, r.replayed_s, r.late_keys
            ),
            format!(
                "SOUNDS {} STARTED  {} LATE  {} DROPPED  {} CANCELLED",
                self.voices[0], self.voices[1], self.voices[2], self.voices[3]
            ),
            format!("EVIDENCE {:.2}  GATE {GATE}", self.listen.evidence),
        ]
    }

    /// A new run from the song time heard now, with a fresh engine and stream: the entries of
    /// the last moments go in first, rebased to its start.
    fn request_start(&mut self, s: f64) {
        if self.world.survivors() {
            self.pending_start.get_or_insert(s);
        } else {
            self.start(s);
        }
    }

    fn start(&mut self, s0: f64) {
        self.run_zen = self.zen;
        match self.world.engine(self.zen) {
            Ok(mut engine) => {
                engine.settle_s = self.settle_s();
                self.engine = engine;
            }
            Err(e) => {
                eprintln!("slice: can't restart: {e}");
                return;
            }
        }
        self.run_start = s0;
        self.pending_start = None;
        self.scene = Scene::default();
        let _ = self.audio.out.commands.push(Command::CancelAll);
        if let Some(s) = self.sounds.as_mut() {
            s.reset();
        }
        self.over = None;
        self.voices = [0; 4];
        let s0 = self.run_start;
        for (_, e) in &self.recent {
            self.engine
                .feed(|log| eval::session::rebase_append(log, e, s0));
        }
        self.engine.key_log = Some(Vec::new());
        self.run_arrivals = self
            .recent
            .iter()
            .map(|(arrived, entry)| Arrival {
                arrived: *arrived,
                entry: entry.clone(),
            })
            .collect();
        self.run_keys.clear();
        self.run_audio.clear();
        self.stop_watch();
        let zen = self.zen;
        self.run_index = self
            .session
            .as_mut()
            .map_or(0, |s| s.run_started(s0, zen, &self.world.level.text));
        self.running = true;
        self.listen.reset(false);
        eprintln!("slice: run started at song time {s0:.1}");
        self.last_stats = eval::live::RunnerStats::default();
        self.log(serde_json::json!({"k": "run", "s": s0}));
    }

    fn key(&mut self, code: KeyCode, pressed: bool, repeat: bool) {
        // (Each press and release at the window server's time, not the event loop's.)
        let now = if repeat {
            now_s()
        } else {
            self.key_times.take(pressed, now_s())
        };
        if !repeat {
            let s = self.song_at(now);
            self.log(serde_json::json!({"k": "key", "s": s, "lag": now_s() - now}));
        }
        if pressed && code == KeyCode::F8 {
            if let Some(t) = self.timing.as_mut() {
                eprintln!("slice: timing log saved: {}", t.flush().display());
            }
            return;
        }
        if self.screen_key(code, pressed, repeat, now) {
            return;
        }
        // The debug menu: F1 shows it, F2 plays the next bundled track (File mode), F3 listens to
        // the Mac again (System mode).
        if pressed && !repeat {
            match code {
                KeyCode::F1 => {
                    self.debug = !self.debug;
                    return;
                }
                KeyCode::F2 if !self.tracks.is_empty() => {
                    let file = self.tracks[self.track % self.tracks.len()].clone();
                    self.track += 1;
                    self.switch(Some(&file));
                    return;
                }
                KeyCode::F3 => {
                    self.switch(None);
                    return;
                }
                _ => {}
            }
        }
        // Watch my run: W on the end card replays it; Esc stops the replay.
        if pressed && !self.running {
            if self.watch.is_some() && code == KeyCode::Escape {
                self.stop_watch();
                return;
            }
            if self.watch.is_none() && self.over.is_some() && code == KeyCode::KeyW {
                self.watch_run(now);
                return;
            }
        }
        // Listening: Enter or Space starts a run (a second after an end, not to skip its card).
        if !self.running {
            if pressed
                && matches!(code, KeyCode::Enter | KeyCode::Space)
                && self.over.is_none_or(|(t, _, _)| now > t + 1.0)
                && self.clock.is_some()
            {
                self.request_start(self.song_at(now).unwrap_or(0.0));
            }
            return;
        }
        // Esc pauses, which ends the run (the music moves on); Backspace ends it.
        if pressed && matches!(code, KeyCode::Escape | KeyCode::Backspace) {
            let end = if code == KeyCode::Escape {
                End::Paused
            } else {
                End::Stopped
            };
            self.end_run(end, now);
            return;
        }
        let key = match code {
            KeyCode::ArrowLeft | KeyCode::KeyA => InputKey::Left,
            KeyCode::ArrowRight | KeyCode::KeyD => InputKey::Right,
            KeyCode::ArrowUp | KeyCode::KeyW => InputKey::Up,
            KeyCode::ArrowDown | KeyCode::KeyS => InputKey::Down,
            KeyCode::Space => InputKey::Space,
            _ => return,
        };
        if repeat {
            return;
        }
        let slot = match key {
            InputKey::Up => 0,
            InputKey::Left => 1,
            InputKey::Down => 2,
            InputKey::Right => 3,
            InputKey::Space => 4,
        };
        self.keys[slot].held = pressed;
        // (Less the player's calibration offset for that kind of key.)
        if let Some(s) = self.song_at(now) {
            let s = s - self.calibration.offset_s(key);
            self.engine.key(s - self.run_start, key, pressed);
        }
    }
}

impl ApplicationHandler for App {
    fn resumed(&mut self, el: &ActiveEventLoop) {
        if self.gfx.is_some() || self.headless {
            return;
        }
        let window = el.create_window(
            Window::default_attributes()
                .with_title("Playlist Survivors (slice)")
                .with_inner_size(winit::dpi::LogicalSize::new(960, 640)),
        );
        match window
            .map_err(|e| e.to_string())
            .and_then(|w| Gfx::new(Arc::new(w), &self.art.pages))
        {
            Ok(g) => self.gfx = Some(g),
            Err(e) => {
                eprintln!("slice: no window: {e}");
                el.exit();
            }
        }
    }

    fn user_event(&mut self, _el: &ActiveEventLoop, (): ()) {
        self.drain();
    }

    fn window_event(&mut self, el: &ActiveEventLoop, _id: WindowId, event: WindowEvent) {
        match event {
            WindowEvent::CloseRequested => {
                if self.running {
                    self.end_run(End::Stopped, now_s());
                }
                el.exit();
            }
            WindowEvent::Resized(s) => {
                if let Some(g) = self.gfx.as_mut() {
                    g.resize(s.width, s.height);
                }
            }
            WindowEvent::KeyboardInput { event, .. } => {
                if let PhysicalKey::Code(code) = event.physical_key {
                    self.key(code, event.state == ElementState::Pressed, event.repeat);
                }
            }
            WindowEvent::RedrawRequested => {
                self.drain();
                self.render();
            }
            WindowEvent::Occluded(occluded) => self.occluded = occluded,
            // (Losing focus pauses, once the window has had it: the keys go elsewhere.)
            WindowEvent::Focused(true) => self.focused = true,
            WindowEvent::Focused(false) if self.focused => {
                self.focused = false;
                self.end_run(End::Paused, now_s());
            }
            _ => {}
        }
    }

    fn about_to_wait(&mut self, el: &ActiveEventLoop) {
        // Delayed listening's watchdog: the main thread checks in; once it has destroyed the tap
        // (the app heard again), listening falls back to Direct and the run ends.
        if let Some(w) = &self.audio.watchdog {
            w.beat(now_s());
        }
        if let Some(w) = self.audio.watchdog.as_ref()
            && let Some(why) = w.tripped()
        {
            let after = w.after();
            eprintln!(
                "slice: the watchdog destroyed the tap {after:.2} s in ({why}); listening Direct"
            );
            self.log(serde_json::json!({"k": "watchdog", "why": why, "after": after}));
            self.listening = Listening::Direct;
            self.switch(None);
        }
        if now_s() >= self.stall_at {
            self.stall_at = f64::INFINITY;
            eprintln!("slice: stalling the main thread for 3 s (--stall-at)");
            std::thread::sleep(std::time::Duration::from_secs(3));
        }
        if now_s() >= self.exit_at {
            // (A run going ends with the session, so its record is whole.)
            if self.running {
                self.end_run(End::Stopped, now_s());
            }
            el.exit();
            return;
        }
        if now_s() >= self.switch_at && !self.tracks.is_empty() {
            self.switch_at = f64::INFINITY;
            let file = self.tracks[self.track % self.tracks.len()].clone();
            self.track += 1;
            self.switch(Some(&file));
        }
        if !self.paced {
            // (The display paces the frames: the drawable waits for the next refresh.)
            if let Some(g) = &self.gfx {
                g.window.request_redraw();
            }
            el.set_control_flow(ControlFlow::Poll);
            return;
        }
        // Hidden: a frame every 1/120 s by the clock, so the engine and the sounds keep time
        // without spinning a core.
        let now = now_s();
        if now >= self.next_frame {
            self.next_frame = now + 1.0 / 120.0;
            if self.headless {
                self.drain();
                self.render();
            } else if let Some(g) = &self.gfx {
                g.window.request_redraw();
            }
        }
        let wait = std::time::Duration::from_secs_f64((self.next_frame - now).max(0.0));
        el.set_control_flow(ControlFlow::WaitUntil(std::time::Instant::now() + wait));
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn a_runs_music_is_resampled_from_its_start_with_silence_before_it() {
        let audio: Vec<i16> = (0..441).collect();
        // (From 10 samples before the music, for 0.01 s at twice its rate.)
        let m = replay_music(&audio, -10, 0.01, 2.0 * SR);
        assert_eq!(m.len(), 882);
        assert_eq!(m[0], 0.0);
        assert_eq!(m[20], 0.0);
        assert!((m[21] * 32767.0 - 0.5).abs() < 1e-6);
        assert!((m[30] * 32767.0 - 5.0).abs() < 1e-6);
    }
    use super::*;

    #[test]
    fn a_run_starts_after_two_clear_seconds_once_quiet_or_after_an_end() {
        let mut l = Listen::default();
        // At the app's start, music already playing: 2 s of clear evidence start the first run.
        for k in 0..=20 {
            l.tick(f64::from(k) * 0.1, 0.6);
        }
        assert!(!l.ready(1.9) && l.ready(2.0));
        // A run starts, the evidence stays clear: no second start without quiet or an end.
        l.reset(false);
        for k in 21..=60 {
            l.tick(f64::from(k) * 0.1, 0.6);
        }
        assert!(!l.ready(6.0));
        // A short dip is not quiet enough; 1.5 s of quiet is, then 2 s clear.
        for k in 61..=65 {
            l.tick(f64::from(k) * 0.1, 0.05);
        }
        for k in 66..=90 {
            l.tick(f64::from(k) * 0.1, 0.6);
        }
        assert!(!l.ready(9.0), "a 0.5 s dip");
        for k in 91..=110 {
            l.tick(f64::from(k) * 0.1, 0.05);
        }
        for k in 111..=131 {
            l.tick(f64::from(k) * 0.1, 0.6);
        }
        assert!(l.ready(13.1));
        // After an end, the next clear stretch starts a run, quiet or not.
        l.reset(true);
        assert!(!l.ready(13.2));
        for k in 132..=152 {
            l.tick(f64::from(k) * 0.1, 0.6);
        }
        assert!(l.ready(15.2));
    }
}
