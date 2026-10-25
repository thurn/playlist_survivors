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
//!     playlist-survivors --file <path> --volume 0.3  # the file quieter (0: silent, still heard)
//!     playlist-survivors --file <path> --mute  # everything runs, the device gets silence
//!     playlist-survivors --file <path> --headless --seconds 30  # no window, no Dock icon, muted:
//!                                     # the whole live path on a timer, for checks (then exits)
//!     playlist-survivors --input-offset-ms 30 --stomp-extra-ms 10  # calibration for this session
//!     playlist-survivors --still <png>  # 20 s of the house track's stream through the engine, drawn offscreen
//!     playlist-survivors --bench [--check]  # the frame's work (sim, evaluate, encode) over 60 s of it, offscreen

mod calibration;
// The tap and output are the phase 0 spike's (57e846a^:native/spike), kept as they were.
#[allow(clippy::pedantic, unused_qualifications, dead_code)]
mod coreaudio;
mod feed;
mod fx;
mod gfx;
mod hud;
mod keys;
mod live;
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated,
    dead_code
)]
mod output;
mod scene;
mod sounds;
#[allow(
    clippy::pedantic,
    clippy::explicit_counter_loop,
    unused_qualifications,
    deprecated,
    dead_code
)]
mod tap;

use std::collections::{HashMap, VecDeque};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc;

use analysis::analyzer::Entry;
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
use calibration::Calibration;
use coreaudio::now_s;
use gfx::Gfx;
use hud::{HudState, KeyLight, PressMark};
use live::{Msg, Source};
use output::{Event, Output};
use scene::{End, Look, Scene};
use sounds::Sounds;

/// The tap's timeline runs this far behind the output's render timeline (phase 0, built-in
/// speakers); `PS_TAP_MINUS_RENDER_MS` overrides it.
const TAP_MINUS_RENDER_S: f64 = 0.023;
/// A captured buffer this loud is music, not silence.
const SOUND_PEAK: f32 = 0.01;
/// This long without sound ends the run.
/// The analyzer's evidence gate (`manifest.json`'s `streamParams.gate`).
const GATE: f64 = 0.2;
/// The interim end: this long without sound, this far into a run (see `render`).
const SILENT_END_S: f64 = 5.0;
const MIN_RUN_S: f64 = 30.0;
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
    still: Option<PathBuf>,
    volume: f32,
    click: bool,
    mute: bool,
    bench: bool,
    check: bool,
    headless: bool,
    /// Exit after this long (s).
    seconds: Option<f64>,
    /// Calibration offsets for this session (ms), over the saved ones.
    input_offset_ms: Option<f64>,
    stomp_extra_ms: Option<f64>,
}

fn options(args: &[String]) -> Result<Options, String> {
    let mut o = Options {
        file: None,
        still: None,
        volume: 1.0,
        click: false,
        mute: false,
        bench: false,
        check: false,
        headless: false,
        seconds: None,
        input_offset_ms: None,
        stomp_extra_ms: None,
    };
    let mut it = args.iter();
    while let Some(a) = it.next() {
        match a.as_str() {
            "--file" => o.file = Some(it.next().ok_or("--file needs a path")?.into()),
            "--click" => o.click = true,
            "--mute" => o.mute = true,
            "--headless" => {
                o.headless = true;
                o.mute = true;
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
    looks: HashMap<String, Look>,
}

impl World {
    fn load() -> Result<World, String> {
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
        let level_path = live::resource("levels", "../../../levels").join("reactive.yaml");
        let level = read(&level_path, "levels/reactive.yaml".into())?;
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
        Ok(World { lib, level, looks })
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
    fn engine(&self) -> Result<Engine, String> {
        Engine::new(
            &self.level,
            &self.lib,
            &indexmap::IndexMap::new(),
            GATE,
            true,
            f64::INFINITY,
        )
        .map_err(|e| format!("level: {e:?}"))
    }
}

#[allow(clippy::single_match_else)]
pub fn run(args: &[String]) -> Result<(), String> {
    let opts = options(args)?;
    if let Some(path) = &opts.still {
        return still(path);
    }
    if opts.bench {
        return bench(opts.check);
    }
    let world = World::load()?;
    let music = match &opts.file {
        Some(p) => Some(Arc::<[f32]>::from(live::decode(p, Output::device_rate())?)),
        None => None,
    };
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
    let mut out = Output::start(music.clone(), opts.volume, bank, opts.mute)?;
    let l_out = Output::reported_latency_s();
    let (source, heard_after, mode) = match music {
        Some(samples) => (
            Source::File {
                samples,
                metas: out.music_meta.take().ok_or("no music timing")?,
                rate: out.rate,
            },
            l_out,
            "file",
        ),
        None => {
            let tap = tap::Tap::start(true)?;
            let offset = std::env::var("PS_TAP_MINUS_RENDER_MS")
                .ok()
                .and_then(|s| s.parse::<f64>().ok())
                .map_or(TAP_MINUS_RENDER_S, |ms| ms / 1e3);
            (Source::Tap(tap), l_out - offset, "system")
        }
    };
    eprintln!(
        "slice: {mode} mode, output {} Hz, L_out {:.1} ms, heard {:.1} ms after capture",
        out.rate,
        l_out * 1e3,
        heard_after * 1e3
    );
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
    let stop = Arc::new(AtomicBool::new(false));
    let (tx, rx) = mpsc::channel();
    // (The output's latency plus this: what the analysis thread adds to a buffer's host time.)
    let heard_offset = heard_after - l_out;
    let heard_after = Arc::new(AtomicU64::new(heard_after.to_bits()));
    let analyzer = live::spawn(
        source,
        Arc::clone(&heard_after),
        tx,
        event_loop.create_proxy(),
        stop.clone(),
    )?;
    let engine = world.engine()?;
    let key_times = keys::KeyTimes::install();
    let mut calibration = Calibration::load();
    if let Some(ms) = opts.input_offset_ms {
        calibration.input_offset_ms = Some(ms);
    }
    if let Some(ms) = opts.stomp_extra_ms {
        calibration.stomp_extra_ms = Some(ms);
    }
    eprintln!(
        "slice: input offsets {:.0} ms, stomp {:.0} ms",
        calibration.offset_s(InputKey::Up) * 1e3,
        calibration.offset_s(InputKey::Space) * 1e3
    );
    let mut app = App {
        key_times,
        calibration,
        gfx: None,
        sounds,
        art,
        world,
        rx,
        out,
        l_out,
        click: opts.click,
        clock: None,
        engine,
        run_start: 0.0,
        scene: Scene::default(),
        beats: VecDeque::new(),
        peak: 0.0,
        last_sound: f64::NEG_INFINITY,
        over: None,
        title_at: 0.0,
        next_voice: 0,
        ahead: 0.02,
        voices: [0; 4],
        voices_at: 0.0,
        mute: opts.mute,
        device: coreaudio::default_output(),
        heard_after,
        heard_offset,
        device_at: 0.0,
        frame_ms: Vec::new(),
        paced: opts.headless,
        occluded: false,
        headless: opts.headless,
        exit_at: opts.seconds.map_or(f64::INFINITY, |s| now_s() + s),
        focused: false,
        next_frame: 0.0,
        keys: [KeyLight::default(); 5],
        presses: VecDeque::new(),
    };
    app.engine.settle_s = app.settle_s();
    let result = event_loop.run_app(&mut app).map_err(|e| e.to_string());
    stop.store(true, Ordering::Relaxed);
    let _ = analyzer.join();
    result
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
    let world = World::load()?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let stream_path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../public/reactive/house-128/stream.json");
    let text = std::fs::read_to_string(&stream_path)
        .map_err(|e| format!("{}: {e}", stream_path.display()))?;
    let file = stream::StreamFile::parse(&text).map_err(|e| e.0)?;
    let mut engine = world.engine()?;
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
        scene.moments(&moments, s, &world.lib, &world.looks);
        scene.level(&engine.view.borrow(), s, &world.lib);
        let theme = world.theme(&engine)?;
        let ground = scene::Field {
            seed: engine.seed,
            theme,
        };
        let last = beats
            .iter()
            .copied()
            .filter(|&b| b <= s)
            .fold(0.0, f64::max);
        let pulse = scene::Pulse {
            beat: (s - last) / 0.47,
            bar: 0.5,
        };
        let mut layers = scene::Layers::default();
        scene.draw(
            &engine.view.borrow(),
            &ground,
            &art,
            &world.looks,
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
/// offscreen to a PNG.
fn still(path: &Path) -> Result<(), String> {
    let world = World::load()?;
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let stream_path = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../public/reactive/house-128/stream.json");
    let text = std::fs::read_to_string(&stream_path)
        .map_err(|e| format!("{}: {e}", stream_path.display()))?;
    let file = stream::StreamFile::parse(&text).map_err(|e| e.0)?;
    let mut engine = world.engine()?;
    let mut replay = stream::Replay::new(file.log.clone());
    let beats: Vec<f64> = file.log.beats.iter().map(|b| b.t).collect();
    let mut scene = Scene::default();
    let mut next = 0;
    let end = 20.0;
    for frame in 1..=(end as usize * 60) {
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
        scene.moments(&moments, s, &world.lib, &world.looks);
        scene.level(&engine.view.borrow(), s, &world.lib);
    }
    let upcoming: Vec<f64> = beats
        .iter()
        .map(|b| b - end)
        .filter(|d| d.abs() < 2.5)
        .collect();
    let (w, h) = (960, 640);
    let mut layers = scene::Layers::default();
    let theme = world.theme(&engine)?;
    let ground = scene::Field {
        seed: engine.seed,
        theme,
    };
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
    let pulse = scene::Pulse {
        beat: beat_phase,
        bar: (bar as f64 + beat_phase.min(1.0)) / 4.0,
    };
    scene.draw(
        &engine.view.borrow(),
        &ground,
        &art,
        &world.looks,
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
        "slice: wrote {} ({} sprites, {} lights; {} enemies, health {:.2}, groove {:.2})",
        path.display(),
        layers.sprites.len() + overlay.len(),
        layers.lights.len(),
        v.alive().count(),
        v.health,
        v.groove
    );
    Ok(())
}

#[allow(clippy::struct_excessive_bools)]
struct App {
    /// Key press times from the window server, and the player's calibration.
    key_times: keys::KeyTimes,
    calibration: Calibration,
    gfx: Option<Gfx>,
    sounds: Option<Sounds>,
    art: Atlases,
    world: World,
    rx: mpsc::Receiver<Msg>,
    out: Output,
    l_out: f64,
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
    /// When a captured buffer is heard after its host time (shared with the analysis thread):
    /// the output's latency plus `heard_offset`.
    heard_after: Arc<AtomicU64>,
    heard_offset: f64,
    /// Each frame's own work since the last log (ms).
    frame_ms: Vec<f64>,
    /// Frames are paced by a timer, not the display, while the window is hidden or occluded (it
    /// gives no drawable to wait on); the next one is due at host `next_frame`.
    paced: bool,
    occluded: bool,
    /// No window: frames on the timer, drawn nowhere; and when to exit (host s), if asked.
    headless: bool,
    exit_at: f64,
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
        while let Ok(m) = self.rx.try_recv() {
            match m {
                Msg::Clock { song, heard } => self.clock = Some((song, heard)),
                Msg::Entry(e) => {
                    let arrived = self.song_at(now).unwrap_or(0.0);
                    if let Entry::Beat(c) = &e {
                        if self.click {
                            let heard = self.clock.map_or(now, |(song, h)| h + (c.t - song));
                            self.voice(Voiced::Click(1800.0), heard - self.l_out, 0.0, 1.0);
                        }
                        self.beats.push_back(c.t);
                    }
                    let start = self.run_start;
                    self.engine
                        .feed(|log| feed::append(log, &e, arrived, start));
                }
                Msg::Follow(g) => {
                    let _ = self.out.commands.push(Command::Follow(g));
                }
                Msg::Level { peak, heard } => {
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
        while let Ok(e) = self.out.events.pop() {
            match e {
                Event::Buffer { host, frames, now } => {
                    self.ahead = host + f64::from(frames) / self.out.rate - now;
                }
                Event::Voice(Report::Started { .. }) => self.voices[0] += 1,
                Event::Voice(Report::Skipped { .. }) => self.voices[1] += 1,
                Event::Voice(Report::Dropped { .. }) => self.voices[2] += 1,
                Event::Voice(Report::Cancelled { .. }) => self.voices[3] += 1,
            }
        }
    }

    fn render(&mut self) {
        let t0 = std::time::Instant::now();
        let now = now_s();
        self.peak *= 0.97;
        let song = self.song_at(now);
        if self.over.is_none()
            && let Some(s) = song
        {
            // Ahead of the time heard by the output's latency at least, so a sound is known
            // before the output must render it (docs/design/native-app.md, "Arrivals roll back
            // like late keys").
            let lead = 0.2f64.max(self.l_out + self.ahead.max(0.0) + 0.02);
            self.engine.frame(s - self.run_start, lead);
            let moments = self.engine.view.borrow_mut().take_moments();
            self.cues(&moments, now);
            self.schedule_sounds();
            self.scene
                .moments(&moments, now, &self.world.lib, &self.world.looks);
            self.scene
                .level(&self.engine.view.borrow(), now, &self.world.lib);
            let end = if self.engine.fallen() {
                Some(End::Fell)
            } else if self.engine.done()
                // (Interim, until the sim's confirmed end allows for the evidence's tail: the song
                // has ended when nothing has sounded for SILENT_END_S, MIN_RUN_S into the run.)
                || (s - self.run_start > MIN_RUN_S
                    && self.last_sound.is_finite()
                    && now - self.last_sound > SILENT_END_S)
            {
                Some(End::Song)
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
                let l_out = Output::reported_latency_s();
                eprintln!(
                    "slice: output now {} ({} Hz), L_out {:.1} ms{}",
                    d.name,
                    d.rate,
                    l_out * 1e3,
                    if (d.rate - self.out.rate).abs() > 0.5 {
                        "; its rate differs from the mixer's: restart the app"
                    } else {
                        ""
                    }
                );
                self.l_out = l_out;
                self.heard_after
                    .store((l_out + self.heard_offset).to_bits(), Ordering::Relaxed);
                self.end_run(End::Device, now);
            }
        }
        // (A run that ended with the song starts again when sound comes back; part 2 of G6 brings
        // the listening screen's start rule.)
        if let Some((t, End::Song, _)) = self.over
            && self.last_sound > t
            && now > t + 1.0
        {
            self.restart();
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
        let Some(g) = self.gfx.as_mut() else {
            // (Headless: the frame's work is the engine's and the sounds'.)
            if self.mute {
                self.frame_ms.push(t0.elapsed().as_secs_f64() * 1e3);
            }
            return;
        };
        let (w, h) = g.logical_size();
        let upcoming: Vec<f64> =
            song.map_or_else(Vec::new, |s| self.beats.iter().map(|b| b - s).collect());
        let Ok(theme) = self.world.theme(&self.engine) else {
            return;
        };
        let ground = scene::Field {
            seed: self.engine.seed,
            theme,
        };
        // The beat phase from the last beat heard and the gap to the next; bars of four from the
        // first beat.
        let (phase, bar) = song.map_or((1.0, 1.0), |s| {
            let heard = self.beats.iter().filter(|&&b| b <= s).count();
            let last = heard.checked_sub(1).map(|k| self.beats[k]);
            match (last, self.beats.get(heard)) {
                (Some(last), Some(&next)) => {
                    let phase = (s - last) / (next - last).max(1e-3);
                    (phase, (((heard - 1) % 4) as f64 + phase.min(1.0)) / 4.0)
                }
                _ => (1.0, 1.0),
            }
        });
        let mut layers = scene::Layers::default();
        self.scene.draw(
            &self.engine.view.borrow(),
            &ground,
            &self.art,
            &self.world.looks,
            now,
            scene::Pulse { beat: phase, bar },
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
        };
        let mut overlay = Vec::new();
        hud::draw(&self.engine.view.borrow(), &state, w, h, &mut overlay);
        if let Some((t, end, seconds)) = self.over {
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
            l_out: self.l_out,
            // (Known in time for the output to render it: past its latency and lookahead.)
            never_late: sounds::NEVER_LATE.max(self.l_out + self.ahead.max(0.0) + 0.005),
            now: now_s(),
            ahead: self.ahead.max(0.0),
        };
        let mut commands = Vec::new();
        sounds.reconcile(&caused, &at, &mut commands);
        for c in commands {
            let _ = self.out.commands.push(c);
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
            let _ = self.out.commands.push(c);
        }
    }

    /// Schedules `source` to start at host time `at` (on the output's timeline), `from` samples
    /// in.
    fn voice(&mut self, source: Voiced, at: f64, from: f64, gain: f32) {
        self.next_voice += 1;
        let _ = self.out.commands.push(Command::Play(Voice {
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
        eval::live::SETTLE_S + KEY_LATE_S + offset
    }

    /// Ends the run (host time `now`) for `end`: the end card shows, listening goes on.
    fn end_run(&mut self, end: End, now: f64) {
        if self.over.is_some() {
            return;
        }
        let seconds = self.song_at(now).map_or(0.0, |s| s - self.run_start);
        self.over = Some((now, end, seconds));
        let _ = self.out.commands.push(Command::CancelAll);
        eprintln!(
            "slice: run over ({end:?}), {seconds:.0} s; sounds {} started, {} skipped late, {} dropped",
            self.voices[0], self.voices[1], self.voices[2]
        );
    }

    /// A new run from the song time heard now, with a fresh engine and stream.
    fn restart(&mut self) {
        let now = now_s();
        match self.world.engine() {
            Ok(mut engine) => {
                engine.settle_s = self.settle_s();
                self.engine = engine;
            }
            Err(e) => {
                eprintln!("slice: can't restart: {e}");
                return;
            }
        }
        self.run_start = self.song_at(now).unwrap_or(0.0);
        self.scene = Scene::default();
        let _ = self.out.commands.push(Command::CancelAll);
        if let Some(s) = self.sounds.as_mut() {
            s.reset();
        }
        self.over = None;
        self.voices = [0; 4];
    }

    fn key(&mut self, code: KeyCode, pressed: bool, repeat: bool) {
        // (Each press and release at the window server's time, not the event loop's.)
        let now = if repeat {
            now_s()
        } else {
            self.key_times.take(pressed, now_s())
        };
        if self.over.is_some() {
            if pressed
                && matches!(code, KeyCode::Enter | KeyCode::Space)
                && self.over.is_some_and(|(t, _, _)| now > t + 1.0)
            {
                self.restart();
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
            WindowEvent::CloseRequested => el.exit(),
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
        if now_s() >= self.exit_at {
            el.exit();
            return;
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
