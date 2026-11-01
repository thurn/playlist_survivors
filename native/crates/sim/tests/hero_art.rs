//! The hero's art details (`Recorder::hero_art`) agree with its pose in every pose of a live run
//! with presses: the phase (its frame unfloored, for art with more frames than the atlas the sim
//! times) runs through the frame it names, and the angle points the way the facing does.

mod common;

use std::cell::RefCell;
use std::rc::Rc;

use common::{Spec, input_for, stream_file, with_silent_tail};
use sim::engine::{HERO_ANIMS, Sim};
use sim::recorder::Recorder;

#[derive(Default)]
struct Poses {
    art: (f64, f64),
    /// (anim, frame, phase) of every pose.
    seen: Vec<(usize, f64, f64)>,
    /// (facing, angle) of every pose.
    facings: Vec<(f64, f64)>,
}

impl Recorder for Poses {
    fn hero_art(&mut self, phase: f64, dir: f64) {
        self.art = (phase, dir);
    }
    fn hero(&mut self, _x: f64, _y: f64, _z: f64, facing: f64, anim: f64, frame: f64) {
        self.seen.push((anim as usize, frame, self.art.0));
        self.facings.push((facing, self.art.1));
    }
}

#[test]
fn the_hero_s_phase_and_angle_agree_with_its_frame_and_facing() {
    let file = stream_file("causality-mix");
    let log = with_silent_tail(file.log.clone(), 10.0);
    let poses = Rc::new(RefCell::new(Poses::default()));
    let spec = Spec::new("causality-mix", "mash4");
    let mut sim = Sim::new(
        input_for(&spec, &log),
        Rc::new(log.clone()),
        Box::new(Rc::clone(&poses)),
        None,
    );
    for k in common::keys(spec.track, spec.profile) {
        sim.key(k);
    }
    sim.advance_to(40.0);
    let p = poses.borrow();
    let mut swings = 0;
    for &(anim, frame, phase) in &p.seen {
        // (Frame n runs from n to n + 1; the follow-through floors with a 1e-6 nudge.)
        assert!(
            phase >= frame - 1e-5 && phase < frame + 1.0,
            "{}: frame {frame}, phase {phase}",
            HERO_ANIMS[anim]
        );
        if !matches!(HERO_ANIMS[anim], "idle" | "jump") && phase.fract() > 0.0 {
            swings += 1;
        }
    }
    assert!(
        swings > 100,
        "the run swung between frames ({swings} poses)"
    );
    // (0 up, 1 left, 2 down, 3 right, y down.)
    for &(facing, dir) in &p.facings {
        let (dx, dy) = (dir.cos(), dir.sin());
        let along = [-dy, -dx, dy, dx][facing as usize];
        assert!(
            along >= dx.abs().max(dy.abs()) - 1e-9,
            "facing {facing}, angle {dir}"
        );
    }
    let quarters: std::collections::BTreeSet<u8> =
        p.facings.iter().map(|&(f, _)| f as u8).collect();
    assert!(quarters.len() > 1, "the hero turned ({quarters:?})");
}
