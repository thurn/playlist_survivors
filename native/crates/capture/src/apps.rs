//! The apps Delayed listening can tap (docs/design/native-app.md, "What the player sees and
//! hears"): the platform's audio processes grouped by the app responsible for each (Chrome plays
//! from its helper processes, Safari from a shared WebKit GPU process: ES, hv-t8o8.36), each with
//! the processes to tap and whether one is playing now.

use std::collections::BTreeMap;

#[cfg(target_os = "macos")]
pub use mac::{list, of_pid};

/// An app producing (or able to produce) audio.
#[derive(Clone, Debug, PartialEq)]
pub struct AudioApp {
    /// Its name (the `.app` bundle's, else the executable's), and the responsible process's id.
    pub name: String,
    pub pid: i32,
    /// The bundle id of one of its processes, if any has one.
    pub bundle: Option<String>,
    /// Its processes to tap (Core Audio process objects on the Mac), and whether any is playing
    /// now.
    pub processes: Vec<u32>,
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

/// The app named `want` in `apps` (case-insensitively, by name, bundle id or a part of either),
/// in their order.
pub fn pick(apps: &[AudioApp], want: &str) -> Option<AudioApp> {
    let w = want.to_lowercase();
    let hit = |a: &&AudioApp| {
        a.name.to_lowercase() == w || a.bundle.as_deref().is_some_and(|b| b.to_lowercase() == w)
    };
    let part = |a: &&AudioApp| {
        a.name.to_lowercase().contains(&w)
            || a.bundle
                .as_deref()
                .is_some_and(|b| b.to_lowercase().contains(&w))
    };
    apps.iter()
        .find(hit)
        .or_else(|| apps.iter().find(part))
        .cloned()
}

/// The app named `want` among those producing audio now, playing ones first (as [`pick`]).
#[cfg(target_os = "macos")]
pub fn find(want: &str) -> Option<AudioApp> {
    pick(&list(), want)
}

/// One audio process, as the platform reports it.
pub(crate) struct Process {
    /// Its handle to tap, its id, and the id of the process responsible for it (its app's).
    pub handle: u32,
    pub pid: i32,
    pub owner: i32,
    pub playing: bool,
    pub bundle: Option<String>,
}

/// The apps `processes` belong to, grouped by the process responsible for each and named by
/// `name` (of that process's id), except our own (`me`): the playing ones first, then by name.
pub(crate) fn group(
    processes: impl IntoIterator<Item = Process>,
    me: i32,
    name: impl Fn(i32) -> String,
) -> Vec<AudioApp> {
    let mut by_app: BTreeMap<i32, AudioApp> = BTreeMap::new();
    for p in processes {
        if p.pid == me || p.owner == me {
            continue;
        }
        let app = by_app.entry(p.owner).or_insert_with(|| AudioApp {
            name: name(p.owner),
            pid: p.owner,
            bundle: None,
            processes: Vec::new(),
            playing: false,
        });
        app.processes.push(p.handle);
        app.playing |= p.playing;
        let bundle = p.bundle.filter(|s| !s.is_empty());
        if app.bundle.is_none() || p.owner == p.pid {
            app.bundle = bundle.or(app.bundle.take());
        }
    }
    let mut apps: Vec<AudioApp> = by_app.into_values().collect();
    apps.sort_by(|a, b| b.playing.cmp(&a.playing).then_with(|| a.name.cmp(&b.name)));
    apps
}

#[cfg(target_os = "macos")]
mod mac {
    use objc2_core_audio::{
        kAudioHardwarePropertyProcessObjectList, kAudioHardwarePropertyTranslatePIDToProcessObject,
        kAudioObjectPropertyScopeGlobal, kAudioProcessPropertyBundleID,
        kAudioProcessPropertyIsRunningOutput, kAudioProcessPropertyPID,
    };

    use super::{AudioApp, Process, group, name_of};
    use crate::coreaudio::{self, Id};

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

    /// Every app with a process object but ours, the playing ones first, then by name.
    pub fn list() -> Vec<AudioApp> {
        let me = std::process::id() as i32;
        let objects: Vec<Id> = coreaudio::get_vec(
            coreaudio::SYSTEM,
            kAudioHardwarePropertyProcessObjectList,
            kAudioObjectPropertyScopeGlobal,
        )
        .unwrap_or_default();
        let processes = objects.into_iter().filter_map(|obj| {
            let pid = coreaudio::get::<i32>(
                obj,
                kAudioProcessPropertyPID,
                kAudioObjectPropertyScopeGlobal,
            )
            .ok()?;
            let owner = match unsafe { responsibility_get_pid_responsible_for_pid(pid) } {
                p if p > 0 => p,
                _ => pid,
            };
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
            .map(|s| s.to_string());
            Some(Process {
                handle: obj,
                pid,
                owner,
                playing,
                bundle,
            })
        });
        group(processes, me, |owner| {
            exe_path(owner).map_or_else(|| format!("process {owner}"), |p| name_of(&p))
        })
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

    fn process(handle: u32, pid: i32, owner: i32, playing: bool, bundle: &str) -> Process {
        Process {
            handle,
            pid,
            owner,
            playing,
            bundle: Some(bundle.into()),
        }
    }

    #[test]
    fn processes_group_by_their_app_and_ours_is_left_out() {
        let names = |pid: i32| match pid {
            10 => "Google Chrome".to_string(),
            20 => "Music".to_string(),
            30 => "afplay".to_string(),
            _ => format!("process {pid}"),
        };
        let apps = group(
            [
                // (Chrome plays from a helper; its own process has the app's bundle id.)
                process(1, 11, 10, true, "com.google.Chrome.helper"),
                process(2, 10, 10, false, "com.google.Chrome"),
                process(3, 20, 20, false, "com.apple.Music"),
                process(4, 30, 30, true, ""),
                process(5, 99, 99, true, "us"),
                process(6, 98, 99, true, "our helper"),
            ],
            99,
            names,
        );
        let summary: Vec<(&str, &[u32], bool, Option<&str>)> = apps
            .iter()
            .map(|a| {
                (
                    a.name.as_str(),
                    a.processes.as_slice(),
                    a.playing,
                    a.bundle.as_deref(),
                )
            })
            .collect();
        assert_eq!(
            summary,
            [
                (
                    "Google Chrome",
                    &[1, 2][..],
                    true,
                    Some("com.google.Chrome")
                ),
                ("afplay", &[4][..], true, None),
                ("Music", &[3][..], false, Some("com.apple.Music")),
            ]
        );
        assert_eq!(apps[0].pid, 10);
    }

    #[test]
    fn an_app_is_picked_by_name_or_bundle_whole_before_in_part() {
        let app = |name: &str, bundle: &str, playing: bool| AudioApp {
            name: name.into(),
            pid: 1,
            bundle: Some(bundle.into()),
            processes: vec![],
            playing,
        };
        let apps = [
            app("Music Helper", "com.example.helper", true),
            app("Music", "com.apple.Music", false),
            app("Spotify", "com.spotify.client", false),
        ];
        let name = |want| pick(&apps, want).map(|a| a.name);
        assert_eq!(name("music").as_deref(), Some("Music"));
        assert_eq!(name("COM.SPOTIFY.CLIENT").as_deref(), Some("Spotify"));
        assert_eq!(name("mus").as_deref(), Some("Music Helper"));
        assert_eq!(name("spot").as_deref(), Some("Spotify"));
        assert_eq!(name("vlc"), None);
    }
}
