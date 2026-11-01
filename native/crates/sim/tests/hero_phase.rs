//! The hero's phase (its frame unfloored, for art with more frames than the atlas the sim times)
//! agrees with its frame in every pose of a live run with presses.

mod common;

use std::cell::RefCell;
use std::rc::Rc;

use common::{Spec, input_for, stream_file, with_silent_tail};
use sim::engine::{HERO_ANIMS, Sim};
use sim::recorder::Recorder;

#[derive(Default)]
struct Poses {
    phase: f64,
    /// (anim, frame, phase) of every pose.
    seen: Vec<(usize, f64, f64)>,
}

impl Recorder for Poses {
    fn hero_phase(&mut self, phase: f64) {
        self.phase = phase;
    }
    fn hero(&mut self, _x: f64, _y: f64, _z: f64, _facing: f64, anim: f64, frame: f64) {
        self.seen.push((anim as usize, frame, self.phase));
    }
}

#[test]
fn the_hero_s_phase_runs_through_each_frame_it_names() {
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
}
