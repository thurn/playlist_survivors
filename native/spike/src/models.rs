//! The tensor file `export_models.py` writes: a u32 header length, a JSON
//! header, then little-endian f32 / f64 / i32 data.

use std::collections::HashMap;
use std::path::Path;

pub struct Tensors {
    header: HashMap<String, Entry>,
    data: Vec<u8>,
}

#[derive(serde::Deserialize)]
struct Entry {
    dtype: String,
    shape: Vec<usize>,
    offset: usize,
}

impl Tensors {
    pub fn load(path: &Path) -> Tensors {
        let bytes = std::fs::read(path).unwrap_or_else(|e| {
            panic!(
                "{}: {e} (run native/spike/export_models.py first)",
                path.display()
            )
        });
        let n = u32::from_le_bytes(bytes[..4].try_into().unwrap()) as usize;
        let header: HashMap<String, Entry> = serde_json::from_slice(&bytes[4..4 + n]).unwrap();
        Tensors {
            header,
            data: bytes[4 + n..].to_vec(),
        }
    }

    fn entry(&self, name: &str) -> &Entry {
        self.header
            .get(name)
            .unwrap_or_else(|| panic!("missing tensor {name}"))
    }

    pub fn has(&self, name: &str) -> bool {
        self.header.contains_key(name)
    }

    pub fn shape(&self, name: &str) -> &[usize] {
        &self.entry(name).shape
    }

    pub fn f32(&self, name: &str) -> Vec<f32> {
        let e = self.entry(name);
        assert_eq!(e.dtype, "f32", "{name}");
        let len: usize = e.shape.iter().product();
        self.data[e.offset..e.offset + 4 * len]
            .chunks_exact(4)
            .map(|c| f32::from_le_bytes(c.try_into().unwrap()))
            .collect()
    }

    pub fn f64(&self, name: &str) -> Vec<f64> {
        let e = self.entry(name);
        assert_eq!(e.dtype, "f64", "{name}");
        let len: usize = e.shape.iter().product();
        self.data[e.offset..e.offset + 8 * len]
            .chunks_exact(8)
            .map(|c| f64::from_le_bytes(c.try_into().unwrap()))
            .collect()
    }

    pub fn i32(&self, name: &str) -> Vec<i32> {
        let e = self.entry(name);
        assert_eq!(e.dtype, "i32", "{name}");
        let len: usize = e.shape.iter().product();
        self.data[e.offset..e.offset + 4 * len]
            .chunks_exact(4)
            .map(|c| i32::from_le_bytes(c.try_into().unwrap()))
            .collect()
    }
}

pub fn models_dir() -> std::path::PathBuf {
    if let Ok(d) = std::env::var("PS_SPIKE_MODELS") {
        return d.into();
    }
    // Inside the bundle, Resources/models; from cargo, the crate's models/.
    if let Ok(exe) = std::env::current_exe()
        && let Some(macos) = exe.parent()
    {
        let res = macos.join("../Resources/models");
        if res.join("models.bin").exists() {
            return res;
        }
    }
    Path::new(env!("CARGO_MANIFEST_DIR")).join("models")
}
