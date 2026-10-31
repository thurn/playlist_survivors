//! The art native draws with (`--art`, epic ps-4c8): its own LPC look, the default, or the
//! HeroEditor feel test's (docs/design/heroeditor-native.md), set once at startup for the process
//! and its checks.

use std::sync::OnceLock;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Art {
    #[default]
    Lpc,
    /// The painted dusk ground (`painted`), and later the HeroEditor characters.
    HeroEditor,
}

impl std::str::FromStr for Art {
    type Err = String;

    fn from_str(s: &str) -> Result<Art, String> {
        match s {
            "lpc" => Ok(Art::Lpc),
            "heroeditor" => Ok(Art::HeroEditor),
            other => Err(format!("--art takes lpc or heroeditor, not {other}")),
        }
    }
}

static ART: OnceLock<Art> = OnceLock::new();

/// Chooses the art for the process (the first choice holds).
pub fn choose(art: Art) {
    let _ = ART.set(art);
}

/// The art chosen, LPC if none was.
pub fn current() -> Art {
    ART.get().copied().unwrap_or_default()
}
