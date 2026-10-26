//! Temporary frames for bounded headless evidence clips. No raw frames enter the repository.
use std::{
    path::{Path, PathBuf},
    sync::atomic::{AtomicU64, Ordering},
};
pub struct Clip {
    path: PathBuf,
    width: u32,
    height: u32,
    fps: u32,
    count: u32,
}
impl Clip {
    pub fn new(width: u32, height: u32, fps: u32) -> Result<Self, String> {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        loop {
            let path = std::env::temp_dir().join(format!(
                "ps-clip-{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::Relaxed)
            ));
            match std::fs::create_dir(&path) {
                Ok(()) => {
                    return Ok(Self {
                        path,
                        width,
                        height,
                        fps,
                        count: 0,
                    });
                }
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {}
                Err(e) => return Err(e.to_string()),
            }
        }
    }
    pub fn frame(&mut self, rgba: &[u8]) -> Result<(), String> {
        render::capture::write_png(
            &self.path.join(format!("{:04}.png", self.count)),
            self.width,
            self.height,
            rgba,
        )?;
        self.count += 1;
        Ok(())
    }
    pub fn count(&self) -> u32 {
        self.count
    }
    pub fn encode(&self, path: &Path) -> Result<(), String> {
        if self.count == 0 {
            return Err("clip has no frames".into());
        }
        let status = std::process::Command::new("ffmpeg")
            .args(["-v", "error", "-y", "-framerate"])
            .arg(self.fps.to_string())
            .arg("-i")
            .arg(self.path.join("%04d.png"))
            .args([
                "-an", "-c:v", "libx264", "-crf", "20", "-pix_fmt", "yuv420p",
            ])
            .arg(path)
            .status()
            .map_err(|e| e.to_string())?;
        if status.success() {
            Ok(())
        } else {
            Err(format!("ffmpeg: {status}"))
        }
    }
}
impl Drop for Clip {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.path);
    }
}
