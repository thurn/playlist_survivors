//! Content files (`content/*.yaml`): the zod schema as values ([`zod`], [`schema`]), the loader
//! ([`load`], `src/content/loader.ts`'s `loadContent`) and the definitions as Rust types
//! ([`types`]). The level script's schema and loader join when the sim needs them.

pub mod level;
pub mod level_schema;
pub mod load;
pub mod schema;
pub mod types;
pub mod zod;

pub use load::{ContentError, ContentIssue, ContentLibrary, SourceFile, load_content};
