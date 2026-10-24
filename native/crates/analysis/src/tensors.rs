//! The tensor files `ps-analysis export-models` writes (`native/models/`): a little-endian u32
//! header length, a JSON header `{name: {dtype, shape, offset}}`, then little-endian f32, f64 or
//! i32 data.

use std::collections::HashMap;
use std::fmt;
use std::path::Path;

/// A loaded tensor file.
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

/// Why a tensor file or tensor could not be read.
#[derive(Debug)]
pub enum TensorError {
    Io(String, std::io::Error),
    Format(String),
    Missing(String),
    Dtype {
        name: String,
        want: &'static str,
        have: String,
    },
}

impl fmt::Display for TensorError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            TensorError::Io(path, e) => write!(f, "{path}: {e}"),
            TensorError::Format(m) => write!(f, "malformed tensor file: {m}"),
            TensorError::Missing(name) => write!(f, "missing tensor {name}"),
            TensorError::Dtype { name, want, have } => {
                write!(f, "tensor {name} is {have}, not {want}")
            }
        }
    }
}

impl std::error::Error for TensorError {}

impl Tensors {
    pub fn load(path: &Path) -> Result<Tensors, TensorError> {
        let bytes =
            std::fs::read(path).map_err(|e| TensorError::Io(path.display().to_string(), e))?;
        Tensors::parse(bytes)
    }

    pub fn parse(mut bytes: Vec<u8>) -> Result<Tensors, TensorError> {
        let len = bytes
            .get(..4)
            .ok_or_else(|| TensorError::Format("no header length".into()))?;
        let n = u32::from_le_bytes([len[0], len[1], len[2], len[3]]) as usize;
        let json = bytes
            .get(4..4 + n)
            .ok_or_else(|| TensorError::Format("short header".into()))?;
        let header: HashMap<String, Entry> =
            serde_json::from_slice(json).map_err(|e| TensorError::Format(e.to_string()))?;
        let data = bytes.split_off(4 + n);
        for (name, e) in &header {
            let size = match e.dtype.as_str() {
                "f32" | "i32" => 4,
                "f64" => 8,
                other => return Err(TensorError::Format(format!("{name}: dtype {other}"))),
            };
            let end = e
                .shape
                .iter()
                .try_fold(size, |n: usize, &d| n.checked_mul(d))
                .and_then(|n| n.checked_add(e.offset));
            if end.is_none_or(|end| end > data.len()) {
                return Err(TensorError::Format(format!("{name} runs past the data")));
            }
        }
        Ok(Tensors { header, data })
    }

    fn entry(&self, name: &str, want: &'static str) -> Result<&Entry, TensorError> {
        let e = self
            .header
            .get(name)
            .ok_or_else(|| TensorError::Missing(name.into()))?;
        if e.dtype != want {
            return Err(TensorError::Dtype {
                name: name.into(),
                want,
                have: e.dtype.clone(),
            });
        }
        Ok(e)
    }

    pub fn shape(&self, name: &str) -> Result<&[usize], TensorError> {
        self.header
            .get(name)
            .map(|e| e.shape.as_slice())
            .ok_or_else(|| TensorError::Missing(name.into()))
    }

    fn bytes<const N: usize>(
        &self,
        name: &str,
        want: &'static str,
    ) -> Result<impl Iterator<Item = [u8; N]> + '_, TensorError> {
        let e = self.entry(name, want)?;
        let len: usize = e.shape.iter().product();
        Ok(self.data[e.offset..e.offset + N * len]
            .as_chunks::<N>()
            .0
            .iter()
            .copied())
    }

    pub fn f32(&self, name: &str) -> Result<Vec<f32>, TensorError> {
        Ok(self
            .bytes::<4>(name, "f32")?
            .map(f32::from_le_bytes)
            .collect())
    }

    pub fn f64(&self, name: &str) -> Result<Vec<f64>, TensorError> {
        Ok(self
            .bytes::<8>(name, "f64")?
            .map(f64::from_le_bytes)
            .collect())
    }

    pub fn i32(&self, name: &str) -> Result<Vec<i32>, TensorError> {
        Ok(self
            .bytes::<4>(name, "i32")?
            .map(i32::from_le_bytes)
            .collect())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn file(header: &str, data: &[u8]) -> Vec<u8> {
        let mut b = (header.len() as u32).to_le_bytes().to_vec();
        b.extend_from_slice(header.as_bytes());
        b.extend_from_slice(data);
        b
    }

    #[test]
    fn reads_each_dtype() {
        let mut data = 1.5f32.to_le_bytes().to_vec();
        data.extend_from_slice(&(-2.25f64).to_le_bytes());
        data.extend_from_slice(&7i32.to_le_bytes());
        let t = Tensors::parse(file(r#"{"a":{"dtype":"f32","shape":[1],"offset":0},"b":{"dtype":"f64","shape":[1],"offset":4},"c":{"dtype":"i32","shape":[1,1],"offset":12}}"#, &data)).unwrap();
        assert_eq!(t.f32("a").unwrap(), [1.5]);
        assert_eq!(t.f64("b").unwrap(), [-2.25]);
        assert_eq!(t.i32("c").unwrap(), [7]);
        assert_eq!(t.shape("c").unwrap(), [1, 1]);
        assert!(matches!(t.f64("a"), Err(TensorError::Dtype { .. })));
        assert!(matches!(t.f32("missing"), Err(TensorError::Missing(_))));
    }

    #[test]
    fn rejects_malformed_files() {
        assert!(Tensors::parse(vec![1, 0]).is_err());
        assert!(Tensors::parse(file("{", &[])).is_err());
        assert!(
            Tensors::parse(file(
                r#"{"a":{"dtype":"f32","shape":[2],"offset":0}}"#,
                &[0; 4]
            ))
            .is_err()
        );
        assert!(
            Tensors::parse(file(
                r#"{"a":{"dtype":"u8","shape":[1],"offset":0}}"#,
                &[0; 4]
            ))
            .is_err()
        );
        // (A shape whose size overflows is malformed, not a wrapped bounds check.)
        let huge = format!(
            r#"{{"a":{{"dtype":"f64","shape":[{},3],"offset":0}}}}"#,
            usize::MAX / 2
        );
        assert!(Tensors::parse(file(&huge, &[0; 8])).is_err());
    }
}
