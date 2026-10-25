//! The apps Delayed listening can tap (docs/design/native-app.md, "What the player sees and
//! hears"): Core Audio's process objects grouped by the app responsible for each (Chrome plays
//! from its helper processes, Safari from a shared WebKit GPU process: ES, hv-t8o8.36), each with
//! the process objects to tap and whether one is playing now.

use std::collections::BTreeMap;

use objc2_core_audio::{
    kAudioHardwarePropertyProcessObjectList, kAudioHardwarePropertyTranslatePIDToProcessObject,
    kAudioObjectPropertyScopeGlobal, kAudioProcessPropertyBundleID,
    kAudioProcessPropertyIsRunningOutput, kAudioProcessPropertyPID,
};

use super::coreaudio::{self, Id};

/// An app producing (or able to produce) audio.
#[derive(Clone, Debug, PartialEq)]
pub struct AudioApp {
    /// Its name (the `.app` bundle's, else the executable's), and the responsible process's id.
    pub name: String,
    pub pid: i32,
    /// The bundle id of one of its processes, if any has one.
    pub bundle: Option<String>,
    /// Its process objects, and whether any is playing now.
    pub processes: Vec<Id>,
    pub playing: bool,
}

impl AudioApp {
    /// Whether it is a web browser (its video will run `M` ahead of its sound).
    pub fn browser(&self) -> bool {
        const BROWSERS: [&str; 7] = [
            "com.apple.safari",
            "com.apple.webkit",
            "com.google.chrome",
            "org.mozilla.firefox",
            "com.microsoft.edgemac",
            "com.brave.browser",
            "company.thebrowser.browser",
        ];
        let b = self.bundle.as_deref().unwrap_or("").to_lowercase();
        BROWSERS.iter().any(|x| b.starts_with(x))
            || [
                "Safari",
                "Google Chrome",
                "Firefox",
                "Microsoft Edge",
                "Brave Browser",
                "Arc",
            ]
            .contains(&self.name.as_str())
    }
}

unsafe extern "C" {
    /// The process responsible for `pid` (an app's helpers answer the app); libSystem's.
    fn responsibility_get_pid_responsible_for_pid(pid: libc::pid_t) -> libc::pid_t;
}

/// The path of process `pid`'s executable.
fn exe_path(pid: i32) -> Option<String> {
    let mut buf = vec![0u8; libc::PROC_PIDPATHINFO_MAXSIZE as usize];
    let n = unsafe { libc::proc_pidpath(pid, buf.as_mut_ptr().cast(), buf.len() as u32) };
    (n > 0).then(|| String::from_utf8_lossy(&buf[..n as usize]).into_owned())
}

/// An app's name from its executable's path: the outermost `.app` bundle's, else the file's.
pub fn name_of(path: &str) -> String {
    let bundle = |c: &&str| {
        std::path::Path::new(c)
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("app"))
    };
    if let Some(app) = path.split('/').find(bundle) {
        return app[..app.len() - 4].to_string();
    }
    path.rsplit('/').next().unwrap_or(path).to_string()
}

/// Every app with a process object but ours, the playing ones first, then by name.
pub fn list() -> Vec<AudioApp> {
    let me = std::process::id() as i32;
    let objects: Vec<Id> = coreaudio::get_vec(
        coreaudio::SYSTEM,
        kAudioHardwarePropertyProcessObjectList,
        kAudioObjectPropertyScopeGlobal,
    )
    .unwrap_or_default();
    let mut by_app: BTreeMap<i32, AudioApp> = BTreeMap::new();
    for obj in objects {
        let Ok(pid) = coreaudio::get::<i32>(
            obj,
            kAudioProcessPropertyPID,
            kAudioObjectPropertyScopeGlobal,
        ) else {
            continue;
        };
        let owner = match unsafe { responsibility_get_pid_responsible_for_pid(pid) } {
            p if p > 0 => p,
            _ => pid,
        };
        if pid == me || owner == me {
            continue;
        }
        let playing = coreaudio::get::<u32>(
            obj,
            kAudioProcessPropertyIsRunningOutput,
            kAudioObjectPropertyScopeGlobal,
        )
        .is_ok_and(|v| v != 0);
        let bundle = coreaudio::get_string(
            obj,
            kAudioProcessPropertyBundleID,
            kAudioObjectPropertyScopeGlobal,
        )
        .ok()
        .map(|s| s.to_string())
        .filter(|s| !s.is_empty());
        let app = by_app.entry(owner).or_insert_with(|| AudioApp {
            name: exe_path(owner).map_or_else(|| format!("process {owner}"), |p| name_of(&p)),
            pid: owner,
            bundle: None,
            processes: Vec::new(),
            playing: false,
        });
        app.processes.push(obj);
        app.playing |= playing;
        if app.bundle.is_none() || owner == pid {
            app.bundle = bundle.or(app.bundle.take());
        }
    }
    let mut apps: Vec<AudioApp> = by_app.into_values().collect();
    apps.sort_by(|a, b| b.playing.cmp(&a.playing).then_with(|| a.name.cmp(&b.name)));
    apps
}

/// The app named `want` (case-insensitively, by name, bundle id or a part of either), playing
/// ones first.
pub fn find(want: &str) -> Option<AudioApp> {
    let w = want.to_lowercase();
    let apps = list();
    let hit = |a: &AudioApp| {
        a.name.to_lowercase() == w || a.bundle.as_deref().is_some_and(|b| b.to_lowercase() == w)
    };
    let part = |a: &AudioApp| {
        a.name.to_lowercase().contains(&w)
            || a.bundle
                .as_deref()
                .is_some_and(|b| b.to_lowercase().contains(&w))
    };
    apps.iter()
        .find(|a| hit(a))
        .or_else(|| apps.iter().find(|a| part(a)))
        .cloned()
}

/// Process `pid` alone as an app (its process object, once it has connected to Core Audio).
pub fn of_pid(pid: i32) -> Option<AudioApp> {
    let obj: Id = coreaudio::get_q(
        coreaudio::SYSTEM,
        kAudioHardwarePropertyTranslatePIDToProcessObject,
        kAudioObjectPropertyScopeGlobal,
        Some(&pid),
    )
    .ok()
    .filter(|&o| o != 0)?;
    Some(AudioApp {
        name: exe_path(pid).map_or_else(|| format!("process {pid}"), |p| name_of(&p)),
        pid,
        bundle: None,
        processes: vec![obj],
        playing: true,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_app_is_named_by_its_bundle_or_executable() {
        assert_eq!(
            name_of(
                "/Applications/Google Chrome.app/Contents/Frameworks/Google Chrome Helper.app/Contents/MacOS/Google Chrome Helper"
            ),
            "Google Chrome"
        );
        assert_eq!(name_of("/usr/bin/afplay"), "afplay");
    }

    #[test]
    fn browsers_are_known_by_bundle_or_name() {
        let app = |name: &str, bundle: Option<&str>| AudioApp {
            name: name.into(),
            pid: 1,
            bundle: bundle.map(str::to_string),
            processes: vec![],
            playing: true,
        };
        assert!(app("Google Chrome", Some("com.google.Chrome.helper")).browser());
        assert!(app("Safari", None).browser());
        assert!(!app("Music", Some("com.apple.Music")).browser());
    }
}
