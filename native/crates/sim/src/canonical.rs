//! The canonical serialization and FNV-1a 64 hash of `native/oracle/FORMAT.md`, over JSON-shaped
//! values: frames, the loaded content and the loaded level are hashed this way on both sides.

use serde_json::Value;

pub const FNV_OFFSET: u64 = 0xcbf2_9ce4_8422_2325;
const FNV_PRIME: u64 = 0x0000_0100_0000_01b3;

/// FNV-1a 64, one byte at a time.
#[derive(Clone, Copy, Debug)]
pub struct Fnv64(pub u64);

impl Default for Fnv64 {
    fn default() -> Self {
        Fnv64(FNV_OFFSET)
    }
}

impl Fnv64 {
    pub fn bytes(&mut self, bytes: &[u8]) {
        for &b in bytes {
            self.0 ^= u64::from(b);
            self.0 = self.0.wrapping_mul(FNV_PRIME);
        }
    }

    fn len(&mut self, n: usize) {
        self.bytes(&(n as u32).to_le_bytes());
    }

    /// A number: its f64 bits, little-endian; every NaN as 0x7ff8000000000000, -0 keeps its sign.
    pub fn number(&mut self, x: f64) {
        self.bytes(&[0x03]);
        let b = if x.is_nan() {
            0x7ff8_0000_0000_0000
        } else {
            x.to_bits()
        };
        self.bytes(&b.to_le_bytes());
    }

    pub fn value(&mut self, v: &Value) {
        match v {
            Value::Null => self.bytes(&[0x00]),
            Value::Bool(false) => self.bytes(&[0x01]),
            Value::Bool(true) => self.bytes(&[0x02]),
            Value::Number(n) => self.number(n.as_f64().expect("a JSON number")),
            Value::String(s) => {
                self.bytes(&[0x04]);
                self.len(s.len());
                self.bytes(s.as_bytes());
            }
            Value::Array(items) => {
                self.bytes(&[0x05]);
                self.len(items.len());
                for item in items {
                    self.value(item);
                }
            }
            Value::Object(fields) => {
                self.bytes(&[0x06]);
                self.len(fields.len());
                // (JS sort(): by UTF-16 code units; the keys are ASCII, so bytewise.)
                let mut keys: Vec<&String> = fields.keys().collect();
                keys.sort_by(|a, b| a.encode_utf16().cmp(b.encode_utf16()));
                for key in keys {
                    self.len(key.len());
                    self.bytes(key.as_bytes());
                    self.value(&fields[key]);
                }
            }
        }
    }
}

/// The canonical hash of a value, as 16 lowercase hex digits.
pub fn hash_hex(v: &Value) -> String {
    let mut h = Fnv64::default();
    h.value(v);
    format!("{:016x}", h.0)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn fnv1a64_test_vectors() {
        let hex = |s: &str| {
            let mut h = Fnv64::default();
            h.bytes(s.as_bytes());
            format!("{:016x}", h.0)
        };
        assert_eq!(hex(""), "cbf29ce484222325");
        assert_eq!(hex("a"), "af63dc4c8601ec8c");
        assert_eq!(hex("foobar"), "85944171f73967e8");
    }
}
