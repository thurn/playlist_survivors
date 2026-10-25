//! The app (docs/design/native-app.md, "Phase G: Grow the Slice"): the game on live audio. The tap
//! (or a `--file` played through the output) feeds the analyzer on a thread; its entries go into
//! the sim's stream, stamped with their arrival, and the sim engine (`eval::live::Engine`) steps to
//! the song time being heard every frame (G1's runner; G5 adds rollback). Arrow keys or WASD stride
//! on the beat, Space stomps. The run ends when the hero falls or after 3 s of silence (until G2
//! brings the finale); Enter or Space starts the next.
//!
//!     playlist-survivors               # System mode: whatever the Mac is playing
//!     playlist-survivors --file <path> # plays the file and listens to it
//!     playlist-survivors --click       # also clicks on each committed beat
//!     playlist-survivors --file <path> --volume 0.3  # the file quieter (0: silent, still heard)
//!     playlist-survivors --file <path> --mute  # everything runs, the device gets silence
//!     playlist-survivors --still <png>  # 20 s of the house track's stream through the engine, drawn offscreen

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
mod sfx;
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
use std::sync::atomic::{AtomicBool, Ordering};
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
use coreaudio::now_s;
use gfx::Gfx;
use hud::{HudState, KeyLight, PressMark};
use live::{Msg, Source};
use output::{Event, Output};
use scene::{Look, Scene};
use sfx::{Cue, Sfx};

/// The tap's timeline runs this far behind the output's render timeline (phase 0, built-in
/// speakers); `PS_TAP_MINUS_RENDER_MS` overrides it.
const TAP_MINUS_RENDER_S: f64 = 0.023;
/// A captured buffer this loud is music, not silence.
const SOUND_PEAK: f32 = 0.01;
/// This long without sound ends the run.
const SILENCE_S: f64 = 3.0;
/// The analyzer's evidence gate (`manifest.json`'s `streamParams.gate`).
const GATE: f64 = 0.2;
/// Under the ground (the TS game's world clear).
const CLEAR: [f64; 3] = [0.06, 0.07, 0.05];

struct Options {
    file: Option<PathBuf>,
    still: Option<PathBuf>,
    volume: f32,
    click: bool,
    mute: bool,
}

fn options(args: &[String]) -> Result<Options, String> {
    let mut o = Options {
        file: None,
        still: None,
        volume: 1.0,
        click: false,
        mute: false,
    };
    let mut it = args.iter();
    while let Some(a) = it.next() {
        match a.as_str() {
            "--file" => o.file = Some(it.next().ok_or("--file needs a path")?.into()),
            "--click" => o.click = true,
            "--mute" => o.mute = true,
            "--still" => o.still = Some(it.next().ok_or("--still needs a path")?.into()),
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
    let world = World::load()?;
    let music = match &opts.file {
        Some(p) => Some(Arc::<[f32]>::from(live::decode(p, Output::device_rate())?)),
        None => None,
    };
    // Output first: our process object must exist before the tap can exclude it.
    let (sfx, bank) = match Sfx::load(
        &live::resource("sfx", "../../../public/assets/sfx"),
        Output::device_rate(),
    ) {
        Ok((sfx, bank)) => (Some(sfx), bank),
        Err(e) => {
            eprintln!("slice: no sound effects, blips instead: {e}");
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
    let event_loop = EventLoop::new().map_err(|e| e.to_string())?;
    let stop = Arc::new(AtomicBool::new(false));
    let (tx, rx) = mpsc::channel();
    let analyzer = live::spawn(
        source,
        heard_after,
        tx,
        event_loop.create_proxy(),
        stop.clone(),
    )?;
    let engine = world.engine()?;
    let key_times = keys::KeyTimes::install();
    let mut app = App {
        key_times,
        gfx: None,
        sfx,
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
        cue_at: f64::NEG_INFINITY,
        next_voice: 0,
        ahead: 0.02,
        voices: [0; 3],
        voices_at: 0.0,
        mute: opts.mute,
        keys: [KeyLight::default(); 5],
        presses: VecDeque::new(),
    };
    let result = event_loop.run_app(&mut app).map_err(|e| e.to_string());
    stop.store(true, Ordering::Relaxed);
    let _ = analyzer.join();
    result
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
        replay.feed_to(engine.log(), s);
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

struct App {
    /// Key press times from the window server.
    key_times: keys::KeyTimes,
    gfx: Option<Gfx>,
    sfx: Option<Sfx>,
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
    over: Option<(f64, bool, f64)>,
    title_at: f64,
    /// When the last swing or kill sound started (host s), so a burst plays once.
    cue_at: f64,
    /// The next voice's identity, and how far ahead of now the output is rendering (s): a sound
    /// scheduled that far ahead lands on its sample.
    next_voice: u64,
    ahead: f64,
    /// Voices started, skipped (onset passed), dropped (no room) this run; muted, they're logged
    /// every 10 s (at host `voices_at`), for a check without sound.
    voices: [u32; 3],
    voices_at: f64,
    mute: bool,
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
                    feed::append(self.engine.log(), &e, arrived, self.run_start);
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
                Event::Voice(_) => {}
            }
        }
    }

    fn render(&mut self) {
        let now = now_s();
        self.peak *= 0.97;
        let song = self.song_at(now);
        if self.over.is_none()
            && let Some(s) = song
        {
            self.engine.advance_to(s - self.run_start);
            let moments = self.engine.view.borrow_mut().take_moments();
            self.cues(&moments, now);
            self.scene
                .moments(&moments, now, &self.world.lib, &self.world.looks);
            self.scene
                .level(&self.engine.view.borrow(), now, &self.world.lib);
            let fell = self.engine.fallen();
            if fell || (self.last_sound.is_finite() && now - self.last_sound > SILENCE_S) {
                if fell {
                    self.cue(Cue::Death, 150.0);
                }
                self.over = Some((now, fell, s - self.run_start));
                eprintln!(
                    "slice: run over ({}), {:.0} s; sounds {} started, {} skipped late, {} dropped",
                    if fell { "fell" } else { "silence" },
                    s - self.run_start,
                    self.voices[0],
                    self.voices[1],
                    self.voices[2]
                );
            }
        }
        if let Some((t, false, _)) = self.over
            && self.last_sound > t
            && now > t + 1.0
        {
            self.restart();
        }
        if self.mute && now > self.voices_at {
            self.voices_at = now + 10.0;
            eprintln!(
                "slice: sounds {} started, {} skipped late, {} dropped",
                self.voices[0], self.voices[1], self.voices[2]
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
        let Some(g) = self.gfx.as_mut() else { return };
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
        if let Some((t, fell, seconds)) = self.over {
            scene::end_card(w, h, now - t, seconds.max(0.0) as u64, fell, &mut overlay);
        }
        overlay.push(gfx::rect(
            0.0,
            h - 4.0,
            w * self.peak.min(1.0),
            4.0,
            [0.3, 0.8, 0.4, 0.8],
        ));
        g.draw(&Frame {
            clear: CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
    }

    /// The sounds the view's new moments make: kills, swings, bites.
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
        let kills = moments
            .iter()
            .filter(|m| matches!(m, Moment::Death { .. }))
            .count();
        let swung = moments.iter().any(|m| matches!(m, Moment::Swing { .. }));
        if moments.iter().any(|m| matches!(m, Moment::Bite { .. })) {
            self.cue(Cue::Hurt, 220.0);
        }
        // (A burst of kills or swings in one frame plays once, and at most every 80 ms.)
        if now - self.cue_at > 0.08 {
            if kills >= 3 {
                self.cue(Cue::KillMulti, 1500.0);
                self.cue_at = now;
            } else if kills > 0 {
                self.cue(Cue::Kill, 1500.0);
                self.cue_at = now;
            } else if swung {
                self.cue(Cue::Swing, 1000.0);
                self.cue_at = now;
            }
        }
    }

    /// A sound effect now (or, without the samples, a blip at `freq`).
    fn cue(&mut self, cue: Cue, freq: f32) {
        // (As soon as the output can play it on its sample.)
        let at = now_s() + self.ahead.max(0.0) + 0.001;
        match self.sfx.as_mut().and_then(|s| s.next(cue)) {
            Some(play) => self.voice(Voiced::Sample(play.sound), at, play.from as f64, play.gain),
            None => self.voice(Voiced::Click(freq), at, 0.0, 1.0),
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

    /// A new run from the song time heard now, with a fresh engine and stream.
    fn restart(&mut self) {
        let now = now_s();
        match self.world.engine() {
            Ok(engine) => self.engine = engine,
            Err(e) => {
                eprintln!("slice: can't restart: {e}");
                return;
            }
        }
        self.run_start = self.song_at(now).unwrap_or(0.0);
        self.scene = Scene::default();
        self.over = None;
        self.voices = [0; 3];
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
        if let Some(s) = self.song_at(now) {
            self.engine.key(s - self.run_start, key, pressed);
        }
    }
}

impl ApplicationHandler for App {
    fn resumed(&mut self, el: &ActiveEventLoop) {
        if self.gfx.is_some() {
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
            _ => {}
        }
    }

    fn about_to_wait(&mut self, el: &ActiveEventLoop) {
        if let Some(g) = &self.gfx {
            g.window.request_redraw();
        }
        el.set_control_flow(ControlFlow::Poll);
    }
}
