//! Silencing routes A (session mute) and B (tiny session volume), with the previous state
//! saved to a file before anything changes, so `restore` (or the guard) can undo it.

use std::path::PathBuf;

use serde_json::{Value, json};

use crate::{R, dev};

pub fn state_path() -> PathBuf {
    let base = std::env::var_os("LOCALAPPDATA").map_or_else(std::env::temp_dir, PathBuf::from);
    let dir = base.join("spike-windows");
    let _ = std::fs::create_dir_all(&dir);
    dir.join("state.json")
}

#[derive(Clone, Copy, Debug)]
pub enum Mode {
    Mute,
    Volume(f32),
}

impl Mode {
    pub fn parse(s: &str) -> R<Mode> {
        match s {
            "mute" | "A" => Ok(Mode::Mute),
            _ => match s.strip_prefix("vol:") {
                Some(v) => Ok(Mode::Volume(v.parse()?)),
                None if s == "B" => Ok(Mode::Volume(1e-5)),
                None => Err(format!("unknown route {s:?} (mute, vol:<level>)").into()),
            },
        }
    }
}

/// Applies `mode` to every session of `pid`'s process tree, saving their state first.
/// Returns what was changed.
pub fn apply(pid: u32, mode: Mode) -> R<Value> {
    if state_path().exists() {
        return Err(format!(
            "{} exists: run `spike restore` first",
            state_path().display()
        )
        .into());
    }
    let tree = dev::tree(pid);
    let targets: Vec<dev::Session> =
        dev::sessions()?.into_iter().filter(|s| tree.contains(&s.pid)).collect();
    if targets.is_empty() {
        return Err(format!("no audio session in process tree of {pid}").into());
    }
    let saved: Vec<Value> = targets
        .iter()
        .map(|s| {
            json!({"device": s.device, "pid": s.pid, "exe": s.exe, "ident": s.ident,
                   "instance": s.instance, "volume": s.volume(), "mute": s.muted()})
        })
        .collect();
    // (Saved before any change, so a crash in between is still restorable.)
    std::fs::write(
        state_path(),
        serde_json::to_string_pretty(&json!({"mode": format!("{mode:?}"), "sessions": saved}))?,
    )?;
    for s in &targets {
        unsafe {
            match mode {
                Mode::Mute => s.vol.SetMute(true, std::ptr::null())?,
                Mode::Volume(v) => s.vol.SetMasterVolume(v, std::ptr::null())?,
            }
        }
    }
    Ok(json!({"applied": format!("{mode:?}"), "sessions": saved}))
}

/// Undoes what `apply` saved: each session found by its instance, or else by its app
/// (another run of the same executable on the same endpoint). Sessions not found stay saved.
pub fn restore() -> R<Value> {
    let path = state_path();
    let Ok(text) = std::fs::read_to_string(&path) else {
        return Ok(json!({"restore": "nothing saved"}));
    };
    let state: Value = serde_json::from_str(&text)?;
    let now = dev::sessions()?;
    let mut done = Vec::new();
    let mut left = Vec::new();
    for s in state["sessions"].as_array().cloned().unwrap_or_default() {
        let by_instance = now.iter().filter(|n| n.instance == s["instance"]).collect::<Vec<_>>();
        let found = if by_instance.is_empty() {
            now.iter().filter(|n| n.ident == s["ident"]).collect()
        } else {
            by_instance
        };
        if found.is_empty() {
            left.push(s);
            continue;
        }
        let volume = s["volume"].as_f64().unwrap_or(1.0) as f32;
        let mute = s["mute"].as_bool().unwrap_or(false);
        for n in found {
            unsafe {
                n.vol.SetMasterVolume(volume, std::ptr::null())?;
                n.vol.SetMute(mute, std::ptr::null())?;
            }
            done.push(json!({"pid": n.pid, "exe": n.exe, "volume": n.volume(), "mute": n.muted()}));
        }
    }
    if left.is_empty() {
        std::fs::remove_file(&path)?;
    } else {
        std::fs::write(
            &path,
            serde_json::to_string_pretty(&json!({"mode": state["mode"], "sessions": left}))?,
        )?;
    }
    Ok(json!({"restored": done, "not_found": left}))
}
