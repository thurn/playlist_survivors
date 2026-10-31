//! The wgpu renderer and offscreen capture for stills.
//!
//! - [`atlas`]: the baked sprite atlases (`public/assets/{characters,effects,world}.json` and
//!   their pages), as `src/runtime/renderer.ts` reads them.
//! - [`sprites`]: instanced, textured quads from the atlas pages (and solid ones), drawn in order.
//! - [`ground`]: the painted ground (`--art heroeditor`), noise in world space under the sprites.
//! - [`post`]: the game's look over them: lights, bloom, the colour grade, distortion.
//! - [`capture`]: the same drawing into an offscreen texture, written to PNG.

pub mod atlas;
pub mod capture;
pub mod font;
pub mod ground;
pub mod post;
pub mod proc;
pub mod sprites;
