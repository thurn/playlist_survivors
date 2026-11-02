//! The art native draws with (`--art`, epic ps-4c8): its own LPC look, the default, or the
//! HeroEditor feel test's (docs/archive/design/heroeditor-native.md), set once at startup for the process
//! and its checks.

use std::path::PathBuf;
use std::sync::OnceLock;

use render::atlas::Atlases;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Art {
    #[default]
    Lpc,
    /// The painted dusk ground (`painted`) and the HeroEditor characters.
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

/// Where `herocap bake` leaves the HeroEditor characters: `PS_HEROEDITOR_ASSETS`, else the demos'
/// per-machine cache (`DEMO3D_CACHE`, `~/Library/Caches/playlist-survivors/demo3d`).
pub fn heroeditor_assets() -> PathBuf {
    if let Ok(p) = std::env::var("PS_HEROEDITOR_ASSETS") {
        return PathBuf::from(p);
    }
    let cache = std::env::var("DEMO3D_CACHE").map_or_else(
        |_| {
            PathBuf::from(std::env::var("HOME").unwrap_or_default())
                .join("Library/Caches/playlist-survivors/demo3d")
        },
        PathBuf::from,
    );
    cache.join("heroeditor/assets")
}

/// The atlases for the art chosen: native's own, with the baked HeroEditor characters in place
/// of the LPC ones under HeroEditor (a clear error if they haven't been baked).
pub fn atlases() -> Result<Atlases, String> {
    let dir = super::live::resource("assets", "../../../public/assets");
    if current() == Art::Lpc {
        return Atlases::load(&dir);
    }
    let over = heroeditor_assets();
    if !over.join("characters.json").is_file() {
        return Err(format!(
            "--art heroeditor: no baked HeroEditor characters in {}; `just herocap` captures and \
             bakes them (docs/archive/design/heroeditor-native.md)",
            over.display()
        ));
    }
    Atlases::load_with(&dir, Some(&over))
}
