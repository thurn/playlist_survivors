//! `src/planner/camera.ts`'s camera rig: a critically damped spring that follows the hero, plus the
//! lead along the route. A spring only reacts to past motion, so the camera is causal.

/// Camera samples per world second.
pub const CAMERA_RATE: f64 = 60.0;
const OMEGA: f64 = 5.5;
pub const VIEW_W: f64 = 960.0;
pub const VIEW_H: f64 = 540.0;

/// The spring, stepped once per camera sample (1/60 world second).
#[derive(Clone, Debug, PartialEq)]
pub struct CameraRig {
    pub x: f64,
    pub y: f64,
    pub vx: f64,
    pub vy: f64,
    /// The latest sample's zoom.
    pub zoom: f64,
    started: bool,
}

impl Default for CameraRig {
    fn default() -> CameraRig {
        CameraRig {
            x: 0.0,
            y: 0.0,
            vx: 0.0,
            vy: 0.0,
            zoom: 1.0,
            started: false,
        }
    }
}

impl CameraRig {
    /// Advances one sample toward the target.
    pub fn sample(&mut self, tx: f64, ty: f64, zoom: f64) {
        if self.started {
            let dt = 1.0 / CAMERA_RATE;
            // Integrated in small substeps for stability.
            for _ in 0..4 {
                let h = dt / 4.0;
                let ax = OMEGA * OMEGA * (tx - self.x) - 2.0 * OMEGA * self.vx;
                let ay = OMEGA * OMEGA * (ty - self.y) - 2.0 * OMEGA * self.vy;
                self.vx += ax * h;
                self.vy += ay * h;
                self.x += self.vx * h;
                self.y += self.vy * h;
            }
        } else {
            self.x = tx;
            self.y = ty;
            self.started = true;
        }
        self.zoom = zoom;
    }

    /// The latest view rectangle: [minX, minY, maxX, maxY].
    pub fn view(&self) -> [f64; 4] {
        let hw = VIEW_W / 2.0 / self.zoom;
        let hh = VIEW_H / 2.0 / self.zoom;
        [self.x - hw, self.y - hh, self.x + hw, self.y + hh]
    }
}
