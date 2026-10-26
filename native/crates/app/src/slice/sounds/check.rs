//! Audio-free recording replay through the real scheduler/mixer, below CI's priority.
use super::{At, Command, Path, Sounds};
use crate::slice::World;
use eval::session::{AppliedKey, Arrival, rebase_append};
use serde::de::DeserializeOwned;
use sim::movement::InputKey;
fn lines<T: DeserializeOwned>(path: &Path) -> Result<Vec<T>, String> {
    std::fs::read_to_string(path)
        .map_err(|e| e.to_string())?
        .lines()
        .filter(|l| !l.trim().is_empty())
        .map(|l| serde_json::from_str(l).map_err(|e| e.to_string()))
        .collect()
}
pub fn run(dir: &Path, until_run_s: f64) -> Result<(), String> {
    if std::env::var_os("PS_NATIVE_BATCH").is_none() {
        let status = std::process::Command::new("taskpolicy")
            .arg("-b")
            .arg(std::env::current_exe().map_err(|e| e.to_string())?)
            .args(std::env::args_os().skip(1))
            .env("PS_NATIVE_BATCH", "1")
            .status()
            .map_err(|e| format!("taskpolicy -b: {e}"))?;
        return if status.success() {
            Ok(())
        } else {
            Err(format!("sound-check child: {status}"))
        };
    }
    if !until_run_s.is_finite() || until_run_s <= 0.0 {
        return Err("sound-check seconds must be positive".into());
    }
    let meta: serde_json::Value = serde_json::from_str(
        &std::fs::read_to_string(dir.join("meta.json")).map_err(|e| e.to_string())?,
    )
    .map_err(|e| e.to_string())?;
    let run = meta["runs"]
        .as_array()
        .and_then(|r| r.first())
        .ok_or("no recorded run")?;
    let s0 = run["s0"].as_f64().ok_or("no run start")?;
    let end = run["end"].as_f64().ok_or("no run end")?;
    let arrivals: Vec<Arrival> = lines(&dir.join("stream.jsonl"))?;
    let keys: Vec<AppliedKey> = lines(&dir.join("keys.jsonl"))?;
    let mut world = World::load(None)?;
    if let Some(text) = run["level"].as_str() {
        world.level.text = text.into();
    }
    let mut engine = world.engine(run["zen"].as_bool().unwrap_or(true))?;
    if run["noHeroDamage"].as_bool().unwrap_or(false) {
        engine.sim.disable_hero_damage_for_check();
    }
    let rate = 8000.0;
    let (mut sounds, bank) = Sounds::load(
        &crate::slice::live::resource("sfx", "../../../public/assets/sfx"),
        rate,
        &world.lib,
        true,
    )?;
    let mut mixer = audio::mixer::Mixer::new(rate, bank, None, 1.0);
    mixer.apply(Command::SurvivorsMix(true), &mut |_| {});
    // Synthetic 0.1 RMS; this diagnostic measures scheduling, not an ear-level mix verdict.
    mixer.apply(Command::MusicLevel(0.1), &mut |_| {});
    let (mut next, mut next_key, mut played, mut minute, mut peak) = (0, 0, 0usize, 0usize, 0usize);
    let mut reached = 0.0f64;
    let mut reports = [0u64; 4];
    let mut report = |r| match r {
        audio::mixer::Report::Started { .. } => reports[0] += 1,
        audio::mixer::Report::Skipped { .. } => reports[1] += 1,
        audio::mixer::Report::Dropped { .. } => reports[2] += 1,
        audio::mixer::Report::Cancelled { .. } => reports[3] += 1,
        _ => {}
    };
    let mut buffer = Vec::with_capacity(134);
    for frame in 1..=(((end - s0 + 0.5) * 120.0).ceil() as usize) {
        let t = frame as f64 / 120.0;
        let first = next;
        while next < arrivals.len() && arrivals[next].arrived - s0 <= t {
            next += 1;
        }
        if next > first {
            engine.feed(|log| {
                for a in &arrivals[first..next] {
                    rebase_append(log, &a.entry, s0);
                }
            });
        }
        while let Some(k) = keys.get(next_key).filter(|k| k.s <= t) {
            if k.run == 0 {
                engine.key(k.s, InputKey::parse_recorded(&k.key)?, k.pressed);
            }
            next_key += 1;
        }
        engine.frame(t, 0.3);
        let caused: Vec<_> = engine.causes().cloned().collect();
        let camera = engine.view.borrow().camera;
        let final_before = engine.sound_final_before();
        let at = At {
            seed: engine.seed,
            final_before,
            run_start: s0,
            beats: &engine.stream().beats,
            gate: 0.2,
            camera,
            clock: (s0, 1000.0),
            l_out: 0.0,
            never_late: 0.06,
            now: 1000.0 + played as f64 / rate,
            ahead: 0.0,
        };
        let mut out = Vec::new();
        sounds.reconcile(&caused, &at, &mut out);
        for command in out {
            mixer.apply(command, &mut report);
        }
        let frames = (frame as f64 * rate / 120.0).floor() as usize - played;
        buffer.resize(frames * 2, 0.0);
        mixer.render(1000.0 + played as f64 / rate, &mut buffer, &mut report);
        played += frames;
        let v = engine.view.borrow();
        peak = peak.max(v.alive().count());
        reached = reached.max(v.run.map_or(0.0, |r| r.elapsed));
        if let Some(run) = v.run
            && run.elapsed / 60.0 >= minute as f64
        {
            eprintln!(
                "sound-check: minute {minute}, run {:.3}s, alive {}, peak {}, damage_exempt {}",
                run.elapsed,
                v.alive().count(),
                peak,
                meta["runs"][0]["noHeroDamage"].as_bool().unwrap_or(false)
            );
            sounds.log_budget();
            minute += 1;
        }
        // A diagnostic of the late-run scheduler, not another full survival gate.
        if v.run.is_some_and(|r| r.elapsed >= until_run_s) {
            break;
        }
    }
    sounds.log_budget();
    eprintln!(
        "sound-check: mixer started {}, skipped {}, dropped {}, cancelled {}; reached minute {}, peak enemies {peak}",
        reports[0],
        reports[1],
        reports[2],
        reports[3],
        minute.saturating_sub(1)
    );
    if reached < until_run_s {
        return Err(format!(
            "recording reached {reached:.3}s, needed {until_run_s:.3}s"
        ));
    }
    if reports[2] > 0 {
        return Err("mixer dropped admitted voices".into());
    }
    Ok(())
}
