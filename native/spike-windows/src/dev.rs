//! Endpoints, audio sessions and processes.

use windows::Win32::Devices::FunctionDiscovery::PKEY_Device_FriendlyName;
use windows::Win32::Foundation::CloseHandle;
use windows::Win32::Media::Audio::{
    DEVICE_STATE_ACTIVE, EDataFlow, IAudioSessionControl2, IAudioSessionManager2, IMMDevice,
    IMMDeviceEnumerator, ISimpleAudioVolume, MMDeviceEnumerator, eConsole,
};
use windows::Win32::System::Com::{
    CLSCTX_ALL, COINIT_MULTITHREADED, CoCreateInstance, CoInitializeEx, CoTaskMemFree, STGM_READ,
};
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
};
use windows::Win32::System::Threading::{
    OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
    QueryFullProcessImageNameW,
};
use windows::core::{Interface, PWSTR};

use crate::R;

/// Joins the multithreaded apartment (every thread that touches COM).
pub fn com() {
    unsafe {
        let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
    }
}

fn enumerator() -> R<IMMDeviceEnumerator> {
    Ok(unsafe { CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)? })
}

pub struct Device {
    pub dev: IMMDevice,
    pub name: String,
}

fn describe(dev: IMMDevice) -> R<Device> {
    unsafe {
        let store = dev.OpenPropertyStore(STGM_READ)?;
        let pv = store.GetValue(&PKEY_Device_FriendlyName)?;
        let name = pv.Anonymous.Anonymous.Anonymous.pwszVal.to_string().unwrap_or_default();
        Ok(Device { dev, name })
    }
}

/// The active endpoints of one direction.
pub fn devices(flow: EDataFlow) -> R<Vec<Device>> {
    let c = unsafe { enumerator()?.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE)? };
    (0..unsafe { c.GetCount()? })
        .map(|i| describe(unsafe { c.Item(i)? }))
        .collect()
}

/// The default endpoint, or the first whose name contains `name` (case-insensitive).
pub fn find(flow: EDataFlow, name: Option<&str>) -> R<Device> {
    match name {
        None => describe(unsafe { enumerator()?.GetDefaultAudioEndpoint(flow, eConsole)? }),
        Some(n) => devices(flow)?
            .into_iter()
            .find(|d| d.name.to_lowercase().contains(&n.to_lowercase()))
            .ok_or_else(|| format!("no active endpoint matching {n:?}").into()),
    }
}

pub struct Session {
    pub device: String,
    pub pid: u32,
    pub exe: String,
    pub ident: String,
    pub instance: String,
    pub state: i32,
    pub vol: ISimpleAudioVolume,
}

impl Session {
    pub fn volume(&self) -> f32 {
        unsafe { self.vol.GetMasterVolume() }.unwrap_or(f32::NAN)
    }
    pub fn muted(&self) -> bool {
        unsafe { self.vol.GetMute() }.is_ok_and(|b| b.as_bool())
    }
}

fn take(p: windows::core::Result<PWSTR>) -> String {
    match p {
        Ok(p) if !p.is_null() => unsafe {
            let s = p.to_string().unwrap_or_default();
            CoTaskMemFree(Some(p.0 as _));
            s
        },
        _ => String::new(),
    }
}

/// Every audio session on every active render endpoint.
pub fn sessions() -> R<Vec<Session>> {
    let mut out = Vec::new();
    for d in devices(windows::Win32::Media::Audio::eRender)? {
        let mgr: IAudioSessionManager2 = unsafe { d.dev.Activate(CLSCTX_ALL, None)? };
        let en = unsafe { mgr.GetSessionEnumerator()? };
        for i in 0..unsafe { en.GetCount()? } {
            let ctl = unsafe { en.GetSession(i)? };
            let ctl2: IAudioSessionControl2 = ctl.cast()?;
            let pid = unsafe { ctl2.GetProcessId() }.unwrap_or(0);
            out.push(Session {
                device: d.name.clone(),
                pid,
                exe: exe(pid),
                ident: take(unsafe { ctl2.GetSessionIdentifier() }),
                instance: take(unsafe { ctl2.GetSessionInstanceIdentifier() }),
                state: unsafe { ctl.GetState() }.map_or(-1, |s| s.0),
                vol: ctl.cast()?,
            });
        }
    }
    Ok(out)
}

/// A process's image path, or empty.
pub fn exe(pid: u32) -> String {
    if pid == 0 {
        return String::new();
    }
    unsafe {
        let Ok(h) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) else {
            return String::new();
        };
        let mut buf = [0u16; 1024];
        let mut n = buf.len() as u32;
        let ok = QueryFullProcessImageNameW(h, PROCESS_NAME_WIN32, PWSTR(buf.as_mut_ptr()), &mut n);
        let _ = CloseHandle(h);
        if ok.is_ok() { String::from_utf16_lossy(&buf[..n as usize]) } else { String::new() }
    }
}

/// `root` and its descendants.
pub fn tree(root: u32) -> Vec<u32> {
    let mut pairs = Vec::new();
    unsafe {
        if let Ok(h) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) {
            let mut e = PROCESSENTRY32W {
                dwSize: size_of::<PROCESSENTRY32W>() as u32,
                ..Default::default()
            };
            let mut ok = Process32FirstW(h, &mut e).is_ok();
            while ok {
                pairs.push((e.th32ProcessID, e.th32ParentProcessID));
                ok = Process32NextW(h, &mut e).is_ok();
            }
            let _ = CloseHandle(h);
        }
    }
    let mut out = vec![root];
    let mut i = 0;
    while i < out.len() {
        let p = out[i];
        for &(c, pp) in &pairs {
            if pp == p && c != p && !out.contains(&c) {
                out.push(c);
            }
        }
        i += 1;
    }
    out
}
