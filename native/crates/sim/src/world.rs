//! `src/sim/world.ts`: the open field, generated in chunks around the hero. The hero can walk
//! anywhere and props never block; spawns, the camera lead and formations follow the hero's heading.

/// A point and a unit heading.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Frame {
    pub x: f64,
    pub y: f64,
    pub tx: f64,
    pub ty: f64,
}

/// The open field: no walls (`crate::movement::no_walls`), and the hero starts at the origin heading
/// right (`OpenWorld`).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct OpenWorld;

impl OpenWorld {
    /// The wall rule for steps: none block in the open field.
    pub fn clamp(&self, x: f64, y: f64, dx: f64, dy: f64, len: f64) -> f64 {
        crate::movement::no_walls(x, y, dx, dy, len)
    }

    /// Where the hero starts.
    pub fn pace_point(&self, _s: f64) -> Frame {
        Frame {
            x: 0.0,
            y: 0.0,
            tx: 1.0,
            ty: 0.0,
        }
    }
}
