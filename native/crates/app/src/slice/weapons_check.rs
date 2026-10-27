//! Synthetic committed beat/forecast stream through real shot emission, drawn without a window.
use crate::slice::{HudState, KeyLight, Scene, World, clip::Clip, damage, hud, live, scene};
use render::font::Align;
use render::{atlas::Atlases, capture, post::Frame};
use sim::survivors::{WeaponId, weapon_catalog};
use std::path::Path;
fn reexec_batch() -> Result<bool, String> {
    if std::env::var_os("PS_NATIVE_BATCH").is_none() {
        let status = std::process::Command::new("taskpolicy")
            .arg("-b")
            .arg(std::env::current_exe().map_err(|e| e.to_string())?)
            .args(std::env::args_os().skip(1))
            .env("PS_NATIVE_BATCH", "1")
            .status()
            .map_err(|e| e.to_string())?;
        return if status.success() {
            Ok(true)
        } else {
            Err(format!("sword-check child: {status}"))
        };
    }
    Ok(false)
}
pub fn evolutions(dir: &Path) -> Result<(), String> {
    if reexec_batch()? {
        return Ok(());
    }
    for id in [
        WeaponId::BloodyTear,
        WeaponId::SoulEater,
        WeaponId::HolyWand,
        WeaponId::UnholyVespers,
        WeaponId::ThunderLoop,
    ] {
        run(&dir.join(id.as_str()), Some((id, 1)))?;
    }
    Ok(())
}
pub fn run(dir: &Path, fixture: Option<(WeaponId, u8)>) -> Result<(), String> {
    if reexec_batch()? {
        return Ok(());
    }
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let mut world = World::load(None)?;
    let thunder = fixture.is_some_and(|(id, _)| id == WeaponId::ThunderLoop);
    let blood = fixture.is_some_and(|(id, _)| id == WeaponId::BloodyTear);
    let evolved = fixture.is_some_and(|(id, _)| weapon_catalog().get(id).evolution_of.is_some());
    let lightning = fixture
        .is_some_and(|(id, _)| matches!(id, WeaponId::LightningRing | WeaponId::ThunderLoop));
    let bible =
        fixture.is_some_and(|(id, _)| matches!(id, WeaponId::KingBible | WeaponId::UnholyVespers));
    let wand =
        fixture.is_some_and(|(id, _)| matches!(id, WeaponId::MagicWand | WeaponId::HolyWand));
    let old = "{\"minute\":0,\"minimum\":15,\"frequency\":1000,\"enemies\":[\"vs-bat3\"],\"bosses\":[],\"events\":[],\"treasure\":[]}";
    if !world.level.text.contains(old) {
        return Err("sword fixture minute0 changed".into());
    }
    let row = if wand || lightning || blood {
        r#"{"minute":0,"minimum":8,"frequency":60000,"enemies":["vs-skeleton"],"bosses":[],"events":[],"treasure":[]}"#
    } else {
        r#"{"minute":0,"minimum":0,"frequency":60000,"enemies":[],"bosses":[],"events":[],"treasure":[]}"#
    };
    world.level.text = world.level.text.replacen(old, row, 1);
    if wand || lightning || blood {
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
    let accent_beat = if blood {
        20.0
    } else if lightning {
        10.0
    } else if bible {
        if evolved { 20.0 } else { 30.0 }
    } else if wand {
        9.0
    } else if fixture.is_some_and(|(id, _)| id == WeaponId::SoulEater) {
        7.0
    } else if fixture.is_some() {
        6.0
    } else {
        4.0
    };
    let period = 60.0 / 128.0;
    let beats: Vec<_> = (0..=if bible || blood { 48 } else { 20 })
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
        ticks: (0..=if bible || blood { 400 } else { 160 })
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
        if let Some(parent) = weapon_catalog().get(id).evolution_of {
            while engine
                .sim
                .survivor_inventory()
                .unwrap()
                .weapons
                .rank(parent)
                .unwrap_or(0)
                < 8
            {
                engine
                    .sim
                    .learn_survivor_weapon(parent)
                    .map_err(|e| format!("fixture base: {e:?}"))?;
            }
            engine
                .sim
                .learn_survivor_passive(weapon_catalog().get(id).requires.unwrap())
                .map_err(|e| format!("fixture passive: {e:?}"))?;
            engine
                .sim
                .evolve_survivor_weapon(id)
                .map_err(|e| format!("fixture evolution: {e:?}"))?;
        } else {
            for _ in 0..rank {
                engine
                    .sim
                    .learn_survivor_weapon(id)
                    .map_err(|e| format!("fixture weapon: {e:?}"))?;
            }
        }
    }
    if blood {
        engine.sim.apply_survivor_stats(sim::survivors::StatDelta {
            luck: 9.0,
            ..Default::default()
        });
    }
    let art = Atlases::load(&live::resource("assets", "../../../public/assets"))?;
    let (w, h) = (1280u32, 720u32);
    let mut gpu = capture::Offscreen::new(&art.pages, w, h)?;
    let mut clip = Clip::new(w, h, 30)?;
    let mut scene = Scene::default();
    let (mut normal, mut accent, mut returned) = (false, false, false);
    let (mut shots, mut heavy) = (0, 0);
    let (mut strikes, mut accent_strikes) = (0, 0);
    for tick in 1..=if bible || blood {
        1800
    } else if thunder {
        960
    } else if wand || lightning || blood {
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
            moments.retain(|m| !matches!(m,eval::live::Moment::WeaponFire(s) if s.id.is_sword() && fixture.is_some_and(|(id,_)|id != s.id)));
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
        if evolved && (4.0 * period..4.0 * period + 0.5).contains(&s) {
            // A labelled presentation trigger only. Real grant/eligibility is tested in sim;
            // these injected initial-loadout clips make no natural-acquisition claim.
            let mut visual_run = engine.view.borrow().run.ok_or("missing presentation run")?;
            visual_run.roulette = Some(sim::survivors::Roulette {
                id: 0,
                tier: 1,
                minute: 25,
                opened: 0.0,
                tick: 8,
                next_song: 5.0 * period,
                next_beat: None,
                started: true,
                landed: Some(4.0 * period),
                rewards: [
                    Some(sim::survivors::Item::Weapon(fixture.unwrap().0)),
                    None,
                    None,
                    None,
                    None,
                ],
            });
            scene
                .evolution
                .update(Some(&visual_run), engine.view.borrow().inventory);
        }
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
        if lightning && !evolved && !(4.4..=if thunder { 7.2 } else { 5.8 }).contains(&s) {
            continue;
        }
        let normal_at = if lightning {
            5.05
        } else if bible {
            8.1
        } else if wand {
            1.3
        } else if blood {
            8.1
        } else if fixture.is_some() {
            0.1
        } else {
            2.0 * period + 0.1
        };
        let accent_at = accent_beat * period
            + if bible {
                0.6
            } else if blood {
                0.15
            } else {
                0.1
            };
        let mut layers = scene::Layers::default();
        let ground = world.field(&engine, run.presentation_s, period)?;
        scene.fast_lane(engine.stream(), 0.2, s, s, None, 0.0);
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
            next_down: false,
            tier_flash: 0.0,
            delayed: 3.0,
            zen: true,
            rhythm: scene.hud_rhythm(),
            keys_visible: true,
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
        if evolved {
            let id = fixture.unwrap().0;
            art.fonts["pixel"].text(
                sim::survivors::Item::Weapon(id).name(),
                w as f32 / 2.0,
                150.0,
                2.0,
                Align::Centre,
                [1.0; 4],
                &mut overlay,
            );
            art.fonts["pixel"].text(
                if blood {
                    "DIAGNOSTIC: INJECTED BLOODY TEAR / LUCK 10 / DURABLE TARGETS"
                } else {
                    "DIAGNOSTIC: INJECTED EVOLUTION / SYNTHETIC STREAM"
                },
                w as f32 / 2.0,
                h as f32 - 15.0,
                1.0,
                Align::Centre,
                [0.8, 0.8, 0.9, 1.0],
                &mut overlay,
            );
        }
        overlay.append(&mut layers.modal);
        gpu.draw(&Frame {
            clear: crate::slice::CLEAR,
            world: &layers.sprites,
            lights: &layers.lights,
            grade: layers.grade,
            overlay: &overlay,
        });
        let rgba = gpu.rgba()?;
        if evolved || !bible || s >= 6.8 {
            clip.frame(&rgba)?;
        }
        if evolved && (4.0 * period + 0.16..4.0 * period + 0.16 + 1.0 / 30.0).contains(&s) {
            capture::write_png(&dir.join("transformation.png"), w, h, &rgba)?;
        }
        if thunder && !returned && s >= 6.25 {
            capture::write_png(&dir.join("return.png"), w, h, &rgba)?;
            returned = true;
        }
        if !normal && s >= normal_at {
            capture::write_png(&dir.join("normal.png"), w, h, &rgba)?;
            normal = true;
        }
        if !accent
            && s >= if evolved {
                accent_at
            } else if lightning {
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
    if !normal || !accent || heavy == 0 || (thunder && (!returned || strikes < 14)) {
        return Err("sword fixture missed its normal/accent shots".into());
    }
    {
        clip.encode(&dir.join(if evolved {
            "showpiece.mp4"
        } else if lightning {
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
    }
    let setup = fixture.map_or_else(
        || "normal sword, speculative lead 0.3".to_owned(),
        |(id, rank)| {
            format!(
                "{} rank {rank}, injected initial loadout, lead 0, base sword FX hidden",
                id.as_str()
            )
        },
    );
    let actors = if wand || lightning || blood {
        if blood {
            "8 skeletons, health 1000, XP 0; Luck10 forces actual critical overlaps (no hero exemption)"
        } else {
            "8 skeletons, health 1000, XP 0"
        }
    } else {
        "no enemies"
    };
    if lightning {
        eprintln!(
            "lightning-check: {strikes} actual positioned strikes, {accent_strikes} accented; camera-window 4.4..{} s, zero-enemy casts draw/play no strikes",
            if thunder { 7.2 } else { 5.8 }
        );
    }
    if evolved {
        eprintln!(
            "evolution-check {}: {} stills plus labelled transformation and {} video frames at30fps, 1280x720, synthetic128BPM, {shots} actual matching shots/{heavy} accent; {setup}; {actors}; silent, no pacing/survival/rollback claim",
            fixture.unwrap().0.as_str(),
            if thunder { 3 } else { 2 },
            clip.count()
        );
    } else {
        eprintln!(
            "weapon-check: {} frames at 30 fps, 1280x720, 128 BPM, {shots} matching real shots/{heavy} accent; {setup}; synthetic known beats/forecast, {actors}, no pacing/survival/rollback claim, silent capture",
            clip.count()
        );
    }
    Ok(())
}
