//! Actual grid strike position; presentation never chooses targets.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct LightningStrike {
    pub id: usize,
    pub weapon: super::WeaponId,
    pub song: f64,
    pub x: f64,
    pub y: f64,
    pub radius: f64,
    pub accent: bool,
}
