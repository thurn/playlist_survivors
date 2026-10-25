//! The baked atlases: characters (sprites with directional animations and their hit-flash
//! frames), effects (one-shot animations) and world (ground tiles and props). Each atlas JSON
//! names its pages; regions here carry a page index into [`Atlases::pages`], all atlases' pages
//! together.

use std::collections::HashMap;
use std::path::Path;

use serde::Deserialize;

/// A rectangle of a page, in pixels.
#[derive(Clone, Copy, Debug, PartialEq, Deserialize)]
pub struct Region {
    pub page: u32,
    pub x: u32,
    pub y: u32,
    pub w: u32,
    pub h: u32,
}

/// The facings of a character animation, in the atlas's order.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Facing {
    Up = 0,
    Left = 1,
    Down = 2,
    Right = 3,
}

impl Facing {
    /// The facing nearest the direction (dx, dy), y down.
    pub fn toward(dx: f32, dy: f32) -> Facing {
        if dx.abs() > dy.abs() {
            if dx < 0.0 {
                Facing::Left
            } else {
                Facing::Right
            }
        } else if dy < 0.0 {
            Facing::Up
        } else {
            Facing::Down
        }
    }
}

/// One character animation: its frames per facing (and their white hit-flash twins), all of
/// one size, drawn with the anchor at the character's position.
#[derive(Clone, Debug)]
pub struct Animation {
    pub frame_w: u32,
    pub frame_h: u32,
    pub anchor_x: f32,
    pub anchor_y: f32,
    pub frames: [Vec<Region>; 4],
    pub flash: [Vec<Region>; 4],
}

impl Animation {
    /// Frame `n` (wrapping) facing `facing`, or its flash twin.
    pub fn frame(&self, facing: Facing, n: usize, flash: bool) -> Region {
        let list = if flash && !self.flash[facing as usize].is_empty() {
            &self.flash[facing as usize]
        } else {
            &self.frames[facing as usize]
        };
        list[n % list.len()]
    }

    pub fn len(&self) -> usize {
        self.frames[Facing::Down as usize].len()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

/// A one-shot effect animation.
#[derive(Clone, Debug)]
pub struct Effect {
    pub frames: Vec<Region>,
    pub anchor_x: f32,
    pub anchor_y: f32,
    pub fps: f32,
}

/// A prop variant: its region, anchored at its base.
#[derive(Clone, Copy, Debug)]
pub struct Prop {
    pub region: Region,
    pub anchor_x: f32,
    pub anchor_y: f32,
}

/// One page's pixels, RGBA8.
pub struct Page {
    pub name: String,
    pub width: u32,
    pub height: u32,
    pub rgba: Vec<u8>,
}

/// All three atlases and their pages.
pub struct Atlases {
    pub pages: Vec<Page>,
    /// Character sprite → animation name → animation.
    pub characters: HashMap<String, HashMap<String, Animation>>,
    pub effects: HashMap<String, Effect>,
    pub tiles: HashMap<String, Vec<Region>>,
    pub props: HashMap<String, Vec<Prop>>,
    /// The procedural textures (`crate::proc`), on the last page.
    pub proc: HashMap<String, Region>,
}

#[derive(Deserialize)]
struct RawFrame {
    #[serde(flatten)]
    region: Region,
    flash: Option<Region>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawAnimation {
    frame_w: u32,
    frame_h: u32,
    anchor_x: f32,
    anchor_y: f32,
    frames: HashMap<String, Vec<RawFrame>>,
}

#[derive(Deserialize)]
struct RawSprite {
    animations: HashMap<String, RawAnimation>,
}

#[derive(Deserialize)]
struct RawCharacters {
    pages: Vec<String>,
    sprites: HashMap<String, RawSprite>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawEffect {
    frames: Vec<Region>,
    anchor_x: f32,
    anchor_y: f32,
    fps: f32,
}

#[derive(Deserialize)]
struct RawEffects {
    pages: Vec<String>,
    anims: HashMap<String, RawEffect>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct RawVariant {
    rect: Region,
    anchor_x: f32,
    anchor_y: f32,
}

#[derive(Deserialize)]
struct RawProp {
    variants: Vec<RawVariant>,
}

#[derive(Deserialize)]
struct RawWorld {
    pages: Vec<String>,
    tiles: HashMap<String, Vec<Region>>,
    props: HashMap<String, RawProp>,
}

fn read_json<T: for<'de> Deserialize<'de>>(path: &Path) -> Result<T, String> {
    let text = std::fs::read_to_string(path).map_err(|e| format!("{}: {e}", path.display()))?;
    serde_json::from_str(&text).map_err(|e| format!("{}: {e}", path.display()))
}

/// Decodes an RGBA8 PNG.
pub fn read_png(path: &Path) -> Result<Page, String> {
    let file = std::fs::File::open(path).map_err(|e| format!("{}: {e}", path.display()))?;
    let mut decoder = png::Decoder::new(std::io::BufReader::new(file));
    decoder.set_transformations(png::Transformations::normalize_to_color8());
    let mut reader = decoder
        .read_info()
        .map_err(|e| format!("{}: {e}", path.display()))?;
    let size = reader
        .output_buffer_size()
        .ok_or_else(|| format!("{}: too large", path.display()))?;
    let mut buf = vec![0; size];
    let info = reader
        .next_frame(&mut buf)
        .map_err(|e| format!("{}: {e}", path.display()))?;
    let rgba = match info.color_type {
        png::ColorType::Rgba => buf[..info.buffer_size()].to_vec(),
        png::ColorType::Rgb => buf[..info.buffer_size()]
            .as_chunks::<3>()
            .0
            .iter()
            .flat_map(|p| [p[0], p[1], p[2], 255])
            .collect(),
        other => {
            return Err(format!(
                "{}: unsupported colour type {other:?}",
                path.display()
            ));
        }
    };
    Ok(Page {
        name: path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default(),
        width: info.width,
        height: info.height,
        rgba,
    })
}

impl Atlases {
    /// Loads the atlases from `dir` (`public/assets`, or the bundle's copy of it).
    pub fn load(dir: &Path) -> Result<Atlases, String> {
        let chars: RawCharacters = read_json(&dir.join("characters.json"))?;
        let effects: RawEffects = read_json(&dir.join("effects.json"))?;
        let world: RawWorld = read_json(&dir.join("world.json"))?;
        let mut pages = Vec::new();
        let mut offsets = [0u32; 3];
        for (k, names) in [&chars.pages, &effects.pages, &world.pages]
            .into_iter()
            .enumerate()
        {
            offsets[k] = pages.len() as u32;
            for name in names {
                pages.push(read_png(&dir.join(name))?);
            }
        }
        let check = |r: Region, base: u32, pages: &[Page]| -> Result<Region, String> {
            let page = r.page + base;
            let p = pages
                .get(page as usize)
                .ok_or_else(|| format!("page {page} missing"))?;
            if r.x + r.w > p.width || r.y + r.h > p.height {
                return Err(format!("region {r:?} outside {}", p.name));
            }
            Ok(Region { page, ..r })
        };
        let mut characters = HashMap::new();
        for (id, sprite) in chars.sprites {
            let mut anims = HashMap::new();
            for (name, a) in sprite.animations {
                let mut frames: [Vec<Region>; 4] = Default::default();
                let mut flash: [Vec<Region>; 4] = Default::default();
                for (k, facing) in ["up", "left", "down", "right"].iter().enumerate() {
                    for f in a.frames.get(*facing).into_iter().flatten() {
                        frames[k].push(check(f.region, offsets[0], &pages)?);
                        if let Some(fl) = f.flash {
                            flash[k].push(check(fl, offsets[0], &pages)?);
                        }
                    }
                }
                if frames.iter().any(Vec::is_empty) {
                    return Err(format!("{id}.{name}: a facing has no frames"));
                }
                anims.insert(
                    name,
                    Animation {
                        frame_w: a.frame_w,
                        frame_h: a.frame_h,
                        anchor_x: a.anchor_x,
                        anchor_y: a.anchor_y,
                        frames,
                        flash,
                    },
                );
            }
            characters.insert(id, anims);
        }
        let mut fx = HashMap::new();
        for (name, e) in effects.anims {
            let frames = e
                .frames
                .into_iter()
                .map(|r| check(r, offsets[1], &pages))
                .collect::<Result<Vec<_>, _>>()?;
            fx.insert(
                name,
                Effect {
                    frames,
                    anchor_x: e.anchor_x,
                    anchor_y: e.anchor_y,
                    fps: e.fps,
                },
            );
        }
        let mut tiles = HashMap::new();
        for (name, list) in world.tiles {
            let list = list
                .into_iter()
                .map(|r| check(r, offsets[2], &pages))
                .collect::<Result<Vec<_>, _>>()?;
            tiles.insert(name, list);
        }
        let mut props = HashMap::new();
        for (name, p) in world.props {
            let list = p
                .variants
                .into_iter()
                .map(|v| {
                    Ok(Prop {
                        region: check(v.rect, offsets[2], &pages)?,
                        anchor_x: v.anchor_x,
                        anchor_y: v.anchor_y,
                    })
                })
                .collect::<Result<Vec<_>, String>>()?;
            props.insert(name, list);
        }
        let (proc_page, proc) = crate::proc::page(pages.len() as u32);
        pages.push(proc_page);
        Ok(Atlases {
            pages,
            characters,
            effects: fx,
            tiles,
            props,
            proc,
        })
    }

    /// A character's animation.
    pub fn animation(&self, sprite: &str, anim: &str) -> Option<&Animation> {
        self.characters.get(sprite)?.get(anim)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn assets() -> std::path::PathBuf {
        Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../public/assets")
    }

    #[test]
    fn the_baked_atlases_load() {
        let a = Atlases::load(&assets()).unwrap();
        // (The three baked atlases and the procedural textures.)
        assert_eq!(a.pages.len(), 4);
        assert_eq!((a.pages[0].width, a.pages[0].height), (4096, 2560));
        let walk = a.animation("hero", "walk").unwrap();
        assert_eq!((walk.frame_w, walk.frame_h, walk.len()), (64, 64, 8));
        assert_eq!(walk.frame(Facing::Down, 9, false), walk.frames[2][1]);
        let bat = a.animation("bat", "walk").unwrap();
        assert_ne!(
            bat.frame(Facing::Left, 0, true),
            bat.frame(Facing::Left, 0, false)
        );
        assert_eq!(a.effects["sparks"].frames[0].page, 1);
        assert_eq!(a.tiles["grass-summer"][0].page, 2);
        assert!(!a.props["tree-summer"].is_empty());
    }

    #[test]
    fn facings_follow_the_larger_axis() {
        assert_eq!(Facing::toward(-3.0, 1.0), Facing::Left);
        assert_eq!(Facing::toward(0.5, -2.0), Facing::Up);
        assert_eq!(Facing::toward(0.0, 0.0), Facing::Down);
    }
}
