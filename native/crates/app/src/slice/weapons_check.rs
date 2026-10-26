//! Synthetic committed beat/forecast stream through real shot emission, drawn without a window.
use crate::slice::{HudState, KeyLight, Scene, World, clip::Clip, damage, hud, live, scene};
use render::{atlas::Atlases, capture, post::Frame};
use std::path::Path;
pub fn run(dir: &Path, fixture: Option<(sim::survivors::WeaponId, u8)>) -> Result<(), String> {
    if std::env::var_os("PS_NATIVE_BATCH").is_none() {
        let status = std::process::Command::new("taskpolicy")
            .arg("-b")
            .arg(std::env::current_exe().map_err(|e| e.to_string())?)
            .args(std::env::args_os().skip(1))
            .env("PS_NATIVE_BATCH", "1")
            .status()
            .map_err(|e| e.to_string())?;
        return if status.success() {
            Ok(())
        } else {
            Err(format!("sword-check child: {status}"))
        };
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let mut world = World::load(None)?;
    let lightning = fixture.is_some_and(|(id, _)| id == sim::survivors::WeaponId::LightningRing);
    let bible = fixture.is_some_and(|(id, _)| id == sim::survivors::WeaponId::KingBible);
    let wand = fixture.is_some_and(|(id, _)| id == sim::survivors::WeaponId::MagicWand);
    let old = "{\"minute\":0,\"minimum\":15,\"frequency\":1000,\"enemies\":[\"vs-bat3\"],\"bosses\":[],\"events\":[],\"treasure\":[]}";
    if !world.level.text.contains(old) {
        return Err("sword fixture minute0 changed".into());
    }
    let row = if wand || lightning {
        r#"{"minute":0,"minimum":8,"frequency":60000,"enemies":["vs-skeleton"],"bosses":[],"events":[],"treasure":[]}"#
    } else {
        r#"{"minute":0,"minimum":0,"frequency":60000,"enemies":[],"bosses":[],"events":[],"treasure":[]}"#
    };
    world.level.text = world.level.text.replacen(old, row, 1);
    if wand || lightning {
        let target = world
            .lib
            .enemies
            .get_mut("vs-skeleton")
            .ok_or("fixture skeleton missing")?;
        target.health = 1000.0;
        target
            .survivors
            .as_mut()
            .ok_or("fixture skeleton stats missing")?
            .xp = 0.0;
    }
    let accent_beat = if lightning {
        10.0
    } else if bible {
        30.0
    } else if wand {
        9.0
    } else if fixture.is_some() {
        6.0
    } else {
        4.0
    };
    let period = 60.0 / 128.0;
    let beats: Vec<_> = (0..=if bible { 48 } else { 20 })
        .map(|i| stream::Beat {
            t: f64::from(i) * period,
            known_at: (f64::from(i) * period - period).max(0.0),
            period,
            evidence: 1.0,
        })
        .collect();
    let times: Vec<_> = beats.iter().map(|b| b.t).collect();
    let log = stream::StreamLog {
        beats,
        ticks: (0..=if bible { 400 } else { 160 })
            .map(|i| stream::Tick {
                t: f64::from(i) * 0.05,
                known_at: f64::from(i) * 0.05,
                evidence: 1.0,
                energy: 0.5,
                level: None,
                key: None,
                bands: None,
                brightness: None,
            })
            .collect(),
        forecasts: Some(vec![stream::Forecast {
            t: accent_beat * period,
            known_at: (accent_beat - 1.0) * period,
            cycle: 4.0,
            confidence: 1.0,
        }]),
        ..Default::default()
    };
    let mut replay = stream::Replay::new(log);
    let mut engine = world.engine(true)?;
    if let Some((id, rank)) = fixture {
        engine.feed(|l| replay.feed_to(l, 0.0));
        engine.frame(0.0, 0.0);
        // Presentation-only initial loadout. Lead zero avoids speculative restore
        // of this injected starting inventory; rollback is tested separately.
        for _ in 0..rank {
            engine
                .sim
                .learn_survivor_weapon(id)
                .map_err(|e| format!("fixture weapon: {e:?}"))?;
        }
    }
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let (w, h) = (1280u32, 720u32);
    let mut gpu = capture::Offscreen::new(&art.pages, w, h)?;
    let mut clip = Clip::new(w, h, 30)?;
    let mut scene = Scene::default();
    let (mut normal, mut accent) = (false, false);
    let (mut shots, mut heavy) = (0, 0);
    let (mut strikes, mut accent_strikes) = (0, 0);
    for tick in 1..=if bible {
        1800
    } else if wand || lightning {
        720
    } else {
        480
    } {
        let s = f64::from(tick) / 120.0;
        engine.feed(|l| replay.feed_to(l, s));
        engine.frame(s, if fixture.is_some() { 0.0 } else { 0.3 });
        let mut moments = engine.view.borrow_mut().take_moments();
        if fixture.is_some() {
            // Isolate the aura in this presentation fixture; inventory/combat are unchanged.
            moments.retain(|m| !matches!(m,eval::live::Moment::WeaponFire(s) if s.id.is_sword()));
        }
        for m in &moments {
            if let eval::live::Moment::LightningStrike(strike) = m {
                strikes += 1;
                accent_strikes += usize::from(strike.accent);
            }
            if let eval::live::Moment::WeaponFire(shot) = m {
                if fixture.is_some_and(|(id, _)| shot.id != id) {
                    continue;
                }
                shots += 1;
                heavy += usize::from(shot.accent);
            }
        }
        scene.moments(
            &moments,
            s,
            &world.lib,
            &world.looks,
            &engine.view.borrow(),
            &damage::Clock {
                beats: &times,
                start: 0.0,
                free: 0.35,
            },
        );
        if tick % 4 != 0 {
            continue;
        }
        let run = engine.view.borrow().run.ok_or("no fixture run")?;
        let last = times
            .iter()
            .copied()
            .filter(|b| *b <= s)
            .fold(0.0, f64::max);
        let pulse = scene::Pulse {
            beat: ((s - last) / period).clamp(0.0, 1.0),
            bar: (s / period % 4.0) / 4.0,
            index: s / period,
        };
        if lightning && !(4.4..=5.8).contains(&s) {
            continue;
        }
        let mut layers = scene::Layers::default();
        let ground = world.field(&engine, run.presentation_s, period)?;
        scene.fast_lane(engine.stream(), 0.2, s, s, None);
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
        let st = HudState {
            keys: [KeyLight::default(); 5],
            presses: &[],
            beats: &[],
            beat_phase: pulse.beat,
            elapsed: run.elapsed,
            bitten_ago: f64::INFINITY,
            head: layers.head,
            feet: layers.feet,
            next_down: false,
            tier_flash: 0.0,
            delayed: 3.0,
            zen: true,
        };
        let mut overlay = std::mem::take(&mut layers.overlay);
        hud::draw(
            &engine.view.borrow(),
            &st,
            &art,
            w as f32,
            h as f32,
            &mut overlay,
        );
        gpu.draw(&Frame {
            clear: crate::slice::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        let rgba = gpu.rgba()?;
        if !bible || s >= 6.8 {
            clip.frame(&rgba)?;
        }
        if !normal
            && s >= if lightning {
                5.05
            } else if bible {
                8.1
            } else if wand {
                1.3
            } else if fixture.is_some() {
                0.1
            } else {
                2.0 * period + 0.1
            }
        {
            capture::write_png(&dir.join("normal.png"), w, h, &rgba)?;
            normal = true;
        }
        if !accent
            && s >= if lightning {
                accent_beat * period + 0.02
            } else if bible {
                accent_beat * period + 0.6
            } else if wand {
                accent_beat * period + 0.1
            } else if fixture.is_some() {
                3.0
            } else {
                4.0 * period + 0.1
            }
        {
            capture::write_png(&dir.join("accent.png"), w, h, &rgba)?;
            accent = true;
        }
    }
    if !normal || !accent || heavy == 0 {
        return Err("sword fixture missed its normal/accent shots".into());
    }
    clip.encode(&dir.join(if lightning {
        "sky-strikes.mp4"
    } else if bible {
        "orbit-and-accent.mp4"
    } else if wand {
        "homing-and-impact.mp4"
    } else if fixture.is_some() {
        "aura-and-accent.mp4"
    } else {
        "spin-and-accent.mp4"
    }))?;
    let setup = fixture.map_or_else(
        || "normal sword, speculative lead 0.3".to_owned(),
        |(id, rank)| {
            format!(
                "{} rank {rank}, injected initial loadout, lead 0, sword FX hidden",
                id.as_str()
            )
        },
    );
    let actors = if wand || lightning {
        "8 skeletons, health 1000, XP 0"
    } else {
        "no enemies"
    };
    if lightning {
        eprintln!(
            "lightning-check: {strikes} actual positioned strikes, {accent_strikes} accented; camera-window 4.4..5.8 s, zero-enemy casts draw/play no strikes"
        );
    }
    eprintln!(
        "weapon-check: {} frames at 30 fps, 1280x720, 128 BPM, {shots} matching real shots/{heavy} accent; {setup}; synthetic known beats/forecast, {actors}, no pacing/survival/rollback claim, silent capture",
        clip.count()
    );
    Ok(())
}
