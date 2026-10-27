//! The hero: its pose, strides, stomps, presses and releases, and its samples with the camera
//! (simulate.ts's hero, strides, input and sampling sections). A reactive level's one section is step
//! movement (`load_level` refuses free movement), so free movement's walks are not ported.

use std::rc::Rc;

use super::{
    Ev, HEADING_BEATS, HEADING_MIN, HERO_ANIMS, HERO_HISTORY, HERO_RATE, PendingSettle, Sim,
};
use crate::core::shaping::{clamp, frac, smoothstep};
use crate::core::songmap::beat_index_at;
use crate::jsmath::{PI, abs, atan2, cos, exp, floor, hypot, max, min, round, sin};
use crate::movement::{
    Dir, InputKey, Key, Rock, Step, StepPose, TakenPress, bend_step, move_pose, no_walls, opposite,
};
use crate::player::KeyEvent;
use crate::reactive::director::{by_level, level_blend};
use crate::reactive::settle::{SettlePress, judgement_label};
use crate::recorder::{Landing, PlanInput};
use crate::schedule::step_span;
use crate::strides::{budget_at, held_direction, interrupt, period_at, start_stride, striding};
use crate::timing::{
    CountedRating, PressClass, PressJudgement, classify_press, index_key, key_group,
    rate_press_on_grid,
};

/// `facingIndex`: 0 up, 1 left, 2 down, 3 right (the atlas's order).
pub fn facing_index(dir: f64) -> f64 {
    let a = atan2(sin(dir), cos(dir));
    if a > -PI / 4.0 && a <= PI / 4.0 {
        return 3.0;
    }
    if a > PI / 4.0 && a <= (3.0 * PI) / 4.0 {
        return 2.0;
    }
    if a <= -PI / 4.0 && a > (-3.0 * PI) / 4.0 {
        return 0.0;
    }
    1.0
}

pub(super) fn key_name(k: InputKey) -> &'static str {
    k.name()
}

pub(super) fn rating_name(r: CountedRating) -> &'static str {
    match r {
        CountedRating::Perfect => "perfect",
        CountedRating::Great => "great",
        CountedRating::Good => "good",
        CountedRating::Loose => "loose",
    }
}

pub(super) fn class_name(c: PressClass) -> &'static str {
    match c {
        PressClass::Partner => "partner",
        PressClass::Offbeat => "offbeat",
        PressClass::Counted => "counted",
        PressClass::Double => "double",
        PressClass::Stray => "stray",
    }
}

impl Sim {
    /// Song beat index at song time t.
    pub(super) fn sbi_at(&self, t: f64) -> f64 {
        beat_index_at(&self.st.song, t)
    }

    /// The hero at world time `at_w`: on its latest step (which ends standing still at its landing
    /// spot), or at rest.
    pub(super) fn hero_pose(&self, at_w: f64) -> StepPose {
        match &self.st.step {
            Some(step) => move_pose(step, at_w, self.env.rule.duration_s, self.env.rule.hop),
            None => StepPose {
                x: self.st.rest_pose.0,
                y: self.st.rest_pose.1,
                z: 0.0,
                u: 1.0,
            },
        }
    }

    /// One stride at song time s: the walk speed times the stride period.
    pub(super) fn one_stride(&self, s: f64) -> f64 {
        let period = if self.env.input.level.survivors.is_some()
            && !self.log.beats[..self.st.ingested.beats]
                .last()
                .is_some_and(|b| b.t > s + 1e-9)
        {
            self.env.rule.free_period_s
        } else {
            period_at(&self.st.sch.stride_steps, s)
        };
        self.env.rule.speed * period
    }

    fn hold(&mut self, key: InputKey) {
        let Some(k) = key.direction() else { return };
        self.st.held.retain(|x| *x != k);
        self.st.held.push(k);
    }

    /// Would this key bend the step (or press stride) in progress into a diagonal?
    fn bend_of(&self, key: InputKey) -> Option<Step> {
        let k = key.direction()?;
        let step = self.st.step.as_ref()?;
        bend_step(
            &self.env.input.lib.hero,
            step,
            self.st.w,
            k,
            &no_walls,
            self.env.rule.duration_s,
        )
    }

    /// Would this key, opposite to the press whose stride is moving and within the stomp window of
    /// it, make a stomp of the two?
    fn rocks(&self, key: InputKey) -> bool {
        let Some(k) = key.direction() else {
            return false;
        };
        let Some(sp) = &self.st.step else {
            return false;
        };
        let rule = &self.env.rule;
        if sp.held == Some(true)
            || sp.rock.is_some()
            || sp.bend_e.is_some()
            || sp.keys.is_empty()
            || !striding(rule, Some(sp), self.st.w)
        {
            return false;
        }
        let since = sp.press.map(|p| p.s).or(sp.s0).unwrap_or(f64::NEG_INFINITY);
        self.st.s - since <= rule.stomp_s + 1e-9 && opposite(sp.keys[0], k)
    }

    /// A press that belongs to the one before it: a diagonal's second key, or a stomp's opposite key.
    fn partner_of(&self, key: InputKey) -> bool {
        self.bend_of(key).is_some() || self.rocks(key)
    }

    /// Stops the stride in progress where it is and sets off toward the held keys with the whole
    /// budget. `key` is the press that strides (None for a held stride). Returns the stride's length.
    fn stride_off(&mut self, key: Option<Key>) -> f64 {
        let rule = self.env.rule;
        let hero = &self.env.input.lib.hero;
        let (s, w) = (self.st.s, self.st.w);
        let one = if key.is_none() {
            self.one_stride(s - 1e-6)
        } else {
            self.one_stride(s)
        };
        let hp = self.hero_pose(w);
        let prev = self
            .st
            .step
            .clone()
            .filter(|sp| striding(&rule, Some(sp), w));
        if let Some(prev) = &prev {
            interrupt(&rule, &mut self.st.strides, prev, w, hp.x, hp.y, one, 0.0);
        }
        // (Zen mode: a tap released before its stride step still strides on it; a held key on its
        // axis wins.)
        let keys: Vec<Key> = self
            .st
            .zen_taps
            .iter()
            .chain(&self.st.held)
            .copied()
            .collect();
        let dir = held_direction(&keys).unwrap_or(Dir {
            x: cos(self.st.facing_angle),
            y: sin(self.st.facing_angle),
        });
        // A held stride taking over a press's stride within the diagonal window carries on that press.
        let press = match (&prev, key) {
            (Some(p), None)
                if p.held != Some(true)
                    && p.rock.is_none()
                    && !p.keys.is_empty()
                    && p.bend_e.is_none() =>
            {
                Some(p.press.unwrap_or(TakenPress {
                    w: p.w0,
                    s: p.s0.unwrap_or(s),
                    x: p.x0,
                    y: p.y0,
                }))
            }
            _ => None,
        };
        let carried = press.is_some_and(|p| {
            w - p.w <= hero.step.diagonal_window_ms / 1000.0 + 1e-9
                || s - p.s <= rule.stomp_s + 1e-9
        });
        let mut step = start_stride(
            &rule,
            &mut self.st.strides,
            w,
            hp.x,
            hp.y,
            dir,
            one,
            key,
            key.is_none() && !carried,
            &no_walls,
        );
        step.s0 = Some(s);
        if carried {
            step.press = press;
            step.keys = prev.expect("a carried stride's press").keys;
        }
        let len = step.len.unwrap_or(0.0);
        self.st.step = Some(step);
        self.st.landed = false;
        self.st.facing_angle = atan2(dir.y, dir.x);
        len
    }

    /// A stomp plants the hero: a stride that started within the stomp window rocks back to where it
    /// began; an older one stops where it is; then, unless rocking, both feet stamp.
    fn stomp_here(&mut self, input: &mut PlanInput) {
        input.stomp = Some(true);
        let rule = self.env.rule;
        let (s, w) = (self.st.s, self.st.w);
        let hp = self.hero_pose(w);
        let moving = self
            .st
            .step
            .clone()
            .filter(|sp| striding(&rule, Some(sp), w));
        // (Zen mode's stomps land on stride steps, just after a stride starts: they stamp in place.)
        let rocking = !self.env.input.zen
            && moving.as_ref().is_some_and(|m| {
                (m.held == Some(true) || !m.keys.is_empty())
                    && m.rock.is_none()
                    && s - m.press.map(|p| p.s).or(m.s0).unwrap_or(f64::NEG_INFINITY)
                        <= rule.stomp_s + 1e-9
            });
        let home = moving
            .as_ref()
            .map(|m| m.press.map_or((m.x0, m.y0), |p| (p.x, p.y)));
        if let Some(m) = &moving {
            let one = self.one_stride(s);
            let (hx, hy) = home.expect("a home for the moving stride");
            let back = if rocking {
                hypot(m.x0 - hx, m.y0 - hy)
            } else {
                0.0
            };
            interrupt(&rule, &mut self.st.strides, m, w, hp.x, hp.y, one, back);
        }
        if rocking {
            let m = moving.expect("a rocking stride");
            let (tx, ty) = home.expect("a home for the rock step");
            self.st.step = Some(Step {
                ex: 0.0,
                ey: 0.0,
                rock: Some(Rock {
                    w,
                    x: hp.x,
                    y: hp.y,
                    tx,
                    ty,
                }),
                ..m
            });
            self.st.landed = true;
            input.rock = Some(true);
        } else {
            let fa = self.st.facing_angle;
            self.st.step = Some(Step {
                w0: w,
                x0: hp.x,
                y0: hp.y,
                dx: cos(fa),
                dy: sin(fa),
                len1: 0.0,
                ex: 0.0,
                ey: 0.0,
                bend_e: None,
                keys: Vec::new(),
                len: Some(0.0),
                held: Some(false),
                s0: Some(s),
                press: None,
                rock: None,
            });
            self.st.landed = true;
        }
        self.rec.landing(Landing {
            s,
            w,
            x: hp.x,
            y: hp.y,
            stomp: Some(true),
        });
        self.st.stomp_target = Some(self.stomp_target(s));
    }

    /// `stompCancelsUntil` as an index into the stride steps: the held strides of one grid step from
    /// the first step at or after s. The steps near s are final (the beat after s is known), but the
    /// index can reach into a beat not known yet.
    fn stomp_target(&self, s: f64) -> super::StompTarget {
        let steps = &self.st.sch.stride_steps;
        let t = &steps.s;
        let mut j = 0usize;
        while j < t.len() && t[j] < s - 1e-9 {
            j += 1;
        }
        if j >= t.len() {
            return super::StompTarget::Time(s);
        }
        let at = if t[j] <= s + 1e-9 {
            j
        } else {
            j.saturating_sub(1)
        };
        let k = j as isize + isize::from(steps.per[at]) - 1;
        if k < 0 {
            super::StompTarget::Time(f64::NAN)
        } else {
            super::StompTarget::Index(k as usize)
        }
    }

    /// The song time held strides are cancelled until: a step not known yet lies past every step a
    /// held stride is queued at.
    fn stomp_until(&self) -> f64 {
        match self.st.stomp_target {
            None => self.st.strides.stomp_until,
            Some(super::StompTarget::Time(t)) => t,
            Some(super::StompTarget::Index(k)) => {
                let t = &self.st.sch.stride_steps.s;
                if k < self.st.sch.stride_steps.tail() {
                    t[k]
                } else {
                    f64::INFINITY
                }
            }
        }
    }

    /// A step of the stride period: a held key strides, except over one a press started at this very
    /// moment; a stomp cancels the held strides of one grid step.
    pub(super) fn held_stride(&mut self) {
        if self.env.input.zen {
            self.zen_step();
            return;
        }
        if self.st.held.is_empty() || self.st.s <= self.stomp_until() + 1e-9 {
            return;
        }
        if let Some(step) = &self.st.step
            && step.held != Some(true)
            && abs(self.st.w - step.w0) < 1e-9
        {
            return;
        }
        self.stride_off(None);
    }

    /// Zen mode, on a step of the stride period: a waiting stomp stamps, else the held (or tapped)
    /// keys stride, unless a stomp has this grid step (taps then wait for the step after it).
    fn zen_step(&mut self) {
        if self.st.zen_stomp {
            self.st.zen_stomp = false;
            // (Its press was recorded when it came.)
            self.stomp_here(&mut PlanInput::default());
            return;
        }
        if (self.st.held.is_empty() && self.st.zen_taps.is_empty())
            || self.st.s <= self.stomp_until() + 1e-9
        {
            return;
        }
        self.stride_off(None);
        self.st.zen_taps.clear();
    }

    /// Is a stride step known after song time s (a beat to move on)? The last grid time's own step
    /// is known, though its subdivisions wait for the next beat. (Its stride is queued only once
    /// the next beat arrives: if none ever does, a press waiting for it never moves, as there is
    /// no beat to move on.)
    fn step_ahead(&self, s: f64) -> bool {
        let steps = &self.st.sch.stride_steps;
        steps.s.get(steps.tail()).is_some_and(|&t| t > s + 1e-9)
    }

    /// Zen mode: a press is never judged. It waits for the next stride step, or, with no beat to
    /// move on, moves at once.
    fn zen_press(&mut self, e: &KeyEvent) {
        let s = self.st.s;
        if self.in_stop(s) {
            self.st.stop_end = s;
            self.rec.hit_stop_cut(s);
        }
        let mut input = PlanInput {
            s,
            w: self.st.w,
            kind: "press",
            key: key_name(e.key),
            steps: true,
            unjudged: Some(true),
            ..PlanInput::default()
        };
        self.st.n_inputs += 1;
        self.hold(e.key);
        let ahead = self.step_ahead(s) || self.env.input.level.survivors.is_some();
        match e.key.direction() {
            None if ahead => self.st.zen_stomp = true,
            None => self.stomp_here(&mut input),
            Some(k) if ahead => {
                self.st.zen_taps.retain(|x| *x != k);
                self.st.zen_taps.push(k);
            }
            Some(k) => input.stride = Some(self.stride_off(Some(k))),
        }
        self.rec.input(input);
    }

    /// The budget left after a stride (`strideTrace`, for the press's trace) is not recorded.
    fn stride_input(&mut self, key: InputKey, input: &mut PlanInput) {
        if key == InputKey::Space {
            self.stomp_here(input);
        } else {
            input.stride = Some(self.stride_off(key.direction()));
        }
    }

    /// Stage 2: a press settles 2 s after it; partners settle as partners, changing nothing. It settles
    /// unjudged if presses were not judged at it.
    fn queue_settle(&mut self, key: InputKey, partner: bool, judged: Option<&PressJudgement>) {
        if !self.env.input.settling {
            return;
        }
        let s = self.st.s;
        let unclear = !self.clear_at(s);
        if !unclear {
            self.st.press_log.push((s, partner));
        }
        if !unclear && !partner {
            let steady = judged.is_some_and(|j| {
                j.class == PressClass::Counted
                    && matches!(
                        j.rating,
                        Some(CountedRating::Perfect | CountedRating::Great)
                    )
            });
            self.st.steady_run = if steady {
                self.st.steady_run + 1.0
            } else {
                0.0
            };
        }
        let steady = !unclear && !partner && self.st.steady_run >= super::FAIR.steady_run;
        let provisional = if unclear {
            "unjudged".to_string()
        } else if partner {
            "partner".to_string()
        } else {
            judgement_label(judged)
        };
        let provisional_change = if unclear || partner {
            0.0
        } else {
            judged.map_or(0.0, |j| j.change)
        };
        let press = SettlePress {
            s,
            partner,
            group: key_group(key),
            unclear,
        };
        self.st.q.push(
            s + crate::reactive::settle::SETTLE_DELAY,
            Ev::Settle(Box::new(PendingSettle {
                press,
                provisional,
                provisional_change,
                steady,
            })),
        );
    }

    pub(super) fn press(&mut self, e: &KeyEvent) {
        if matches!(e.key, InputKey::Confirm | InputKey::Pause) {
            return;
        }
        if self.env.input.zen {
            self.zen_press(e);
            return;
        }
        let env = Rc::clone(&self.env);
        let hero = &env.input.lib.hero;
        let settling = env.input.settling;
        let s = self.st.s;
        // Every press in step movement is classed against the grid as known now, less its key
        // group's drift; one while the beat is unclear (or before any beat) is unjudged.
        let grid: Vec<f64> = self.grid_known_at(s).to_vec();
        let unjudged = !self.clear_at(s) || grid.is_empty();
        let judged = if unjudged {
            None
        } else {
            let partner = self.partner_of(e.key);
            let memo = if settling {
                &mut self.st.prov_memo
            } else {
                &mut self.st.memo
            };
            Some(classify_press(
                hero,
                &grid,
                s,
                partner,
                memo,
                key_group(e.key),
                &mut index_key,
            ))
        };
        // Press cleaves: the counted press of an accent whose heavy impact has struck launches its
        // shockwave now when Great or better.
        let great = judged.as_ref().is_some_and(|j| {
            j.class == PressClass::Counted
                && matches!(
                    j.rating,
                    Some(CountedRating::Perfect | CountedRating::Great)
                )
        });
        let mut launched = false;
        if let Some(pc) = self.st.pending_cleave.clone()
            && let Some(j) = judged.as_ref().filter(|j| j.class == PressClass::Counted)
            && j.grid_index as f64 == pc.gi
        {
            if great {
                self.launch_cleave(pc, s, s);
                launched = true;
            }
            self.st.pending_cleave = None;
        }
        // Stage 5: a counted press of Great or better within the Great window of an unforecast strong
        // onset launches its cleave (one cleave a press).
        if great {
            if let Some((c, t)) = self.st.onset_cleave.clone()
                && !launched
                && abs(s - t) <= hero.timing.great_ms / 1000.0 + 1e-9
            {
                self.launch_cleave(c, s, s);
                launched = true;
                self.st.onset_cleave = None;
            }
            self.st.last_great = Some((s, launched));
        }
        if self.in_stop(s) {
            // A press ends the hit-stop early: world time resumes at the press.
            self.st.stop_end = s;
            self.rec.hit_stop_cut(s);
        }
        let mut input = PlanInput {
            s,
            w: self.st.w,
            kind: "press",
            key: key_name(e.key),
            steps: true,
            ..PlanInput::default()
        };
        self.st.n_inputs += 1;
        self.hold(e.key);
        // A perpendicular key just after the first bends the stride into a diagonal.
        if let Some(bent) = self.bend_of(e.key) {
            self.st.step = Some(bent);
            input.bend = Some(true);
            self.queue_settle(e.key, true, judged.as_ref());
            if let Some(j) = &judged {
                input.class = Some(class_name(j.class));
            }
            if unjudged {
                input.unjudged = Some(true);
            }
            self.rec.input(input);
            return;
        }
        // An opposite key just after the first makes the two one stomp.
        if self.rocks(e.key) {
            self.stomp_here(&mut input);
            self.queue_settle(e.key, true, judged.as_ref());
            if let Some(j) = &judged {
                input.class = Some(class_name(j.class));
            }
            if unjudged {
                input.unjudged = Some(true);
            }
            self.rec.input(input);
            return;
        }
        self.stride_input(e.key, &mut input);
        self.queue_settle(e.key, false, judged.as_ref());
        if unjudged {
            input.unjudged = Some(true);
            self.rec.input(input);
            return;
        }
        let j = judged.expect("a judged press");
        if j.class != PressClass::Counted {
            input.class = Some(class_name(j.class));
            input.extra = Some(true);
            if j.class == PressClass::Offbeat {
                input.offbeat = j.rating.map(rating_name);
            }
            if !settling {
                self.meter_change(j.change);
            }
            self.rec.input(input);
            return;
        }
        // Rate the press against the timing grid, less its drift as its class was.
        let (rated, _gi) = rate_press_on_grid(hero, s - j.drift, &grid);
        input.rating = rated.rating.map(|r| match r {
            crate::timing::Rating::Perfect => "perfect",
            crate::timing::Rating::Great => "great",
            crate::timing::Rating::Good => "good",
        });
        input.delta = Some(rated.delta);
        self.st.last_press_s = s;
        input.class = Some(class_name(j.class));
        if !settling {
            self.meter_change(j.change);
        }
        self.rec.input(input);
    }

    pub(super) fn release(&mut self, e: &KeyEvent) {
        self.st.n_inputs += 1;
        self.rec.input(PlanInput {
            s: self.st.s,
            w: self.st.w,
            kind: "release",
            key: key_name(e.key),
            steps: true,
            ..PlanInput::default()
        });
        if let Some(k) = e.key.direction() {
            self.st.held.retain(|x| *x != k);
        }
    }

    /// A director value blended over the loudness levels known by song time s (`byLevel`).
    pub(super) fn by_level(&self, s: f64, value: &dyn Fn(stream::Level) -> f64) -> f64 {
        level_blend(
            &self.log,
            self.st.ingested.ticks,
            s,
            self.env.director.smooth_s,
            value,
        )
    }

    /// One hero sample (and its camera sample) at world time wk, song time sk (`sampleHero`).
    pub(super) fn sample_hero(&mut self, wk: f64, sk: f64) {
        let env = Rc::clone(&self.env);
        let hero = &env.input.lib.hero;
        let p = self.hero_pose(wk);
        if let Some((lx, ly)) = self.st.last_sample {
            self.st.walked += hypot(p.x - lx, p.y - ly);
        }
        self.st.last_sample = Some((p.x, p.y));
        self.st.hero_x.push(p.x);
        self.st.hero_y.push(p.y);
        let cap = (2.0 * HERO_HISTORY * HERO_RATE) as usize;
        if self.st.hero_x.len() > cap {
            let drop = self.st.hero_x.len() - (HERO_HISTORY * HERO_RATE) as usize;
            self.st.hero_x.drain(..drop);
            self.st.hero_y.drain(..drop);
            self.st.hero_base += drop;
        }
        // The heading: the hero's displacement over the last two beats (kept while it stands still).
        {
            let beats = &self.st.song.beats;
            let bi = max(0.0, min(beats.len() as f64 - 2.0, floor(self.sbi_at(sk))));
            let at = |i: f64| {
                if i >= 0.0 && (i as usize) < beats.len() && i.fract() == 0.0 {
                    beats[i as usize]
                } else {
                    f64::NAN
                }
            };
            let span = HEADING_BEATS * (at(bi + 1.0) - at(bi)) * max(0.05, self.base_rate(sk));
            let last = self.st.hero_x.len() as f64 - 1.0;
            let back = max(0.0, last - round(span * HERO_RATE));
            // (A step back past the samples reads undefined in TS: no heading.)
            if !back.is_nan() && back <= last {
                let (l, b) = (last as usize, back as usize);
                let dx = self.st.hero_x[l] - self.st.hero_x[b];
                let dy = self.st.hero_y[l] - self.st.hero_y[b];
                let d = hypot(dx, dy);
                if d >= HEADING_MIN {
                    self.st.heading = (dx / d, dy / d);
                }
            }
        }
        // Animation: a swing (wind-up, impact, follow-through), a step's jump, or idle.
        let mut winding: Option<usize> = None;
        let mut following: Option<usize> = None;
        for &c in &self.st.live_swings {
            let sw = &self.st.swing_pool[c];
            if sw.s_wind > sk + 1e-9 || sw.undecided {
                continue;
            }
            if !sw.struck {
                if winding.is_none_or(|x| sw.s_strike < self.st.swing_pool[x].s_strike) {
                    winding = Some(c);
                }
            } else if sw.impact + (wk - sw.w_strike) * sw.fps < sw.frames
                && following.is_none_or(|x| sw.s_strike > self.st.swing_pool[x].s_strike)
            {
                following = Some(c);
            }
        }
        let half = 0.5 / HERO_RATE;
        let chosen = match following {
            Some(f) if wk - self.st.swing_pool[f].w_strike <= half + 1e-9 => Some(f),
            _ => winding.or(following),
        };
        let (anim, frame, facing);
        if let Some(c) = chosen {
            let sw = &self.st.swing_pool[c];
            let fr = if sw.struck {
                let since = wk - sw.w_strike;
                if since <= half + 1e-9 {
                    sw.impact
                } else {
                    clamp(
                        sw.impact + floor(since * sw.fps + 1e-6),
                        sw.impact,
                        sw.frames - 1.0,
                    )
                }
            } else {
                let to_impact_w = max(
                    1e-6 * (sw.s_strike - sk),
                    step_span(self.base_steps(), sk, sw.s_strike),
                );
                if to_impact_w <= half + 1e-9 {
                    sw.impact
                } else {
                    clamp(
                        floor((sw.impact * (sk - sw.s_wind)) / max(1e-6, sw.s_strike - sw.s_wind)),
                        0.0,
                        sw.impact - 1.0,
                    )
                }
            };
            anim = anim_idx(&sw.anim);
            frame = fr;
            facing = facing_index(if sw.aimed {
                sw.dir
            } else {
                self.st.facing_angle
            });
        } else if p.u < 1.0 {
            anim = anim_idx("jump");
            frame = clamp(floor(p.u * 5.0), 0.0, 4.0);
            facing = facing_index(self.st.facing_angle);
        } else {
            anim = anim_idx("idle");
            frame = floor(frac(self.sbi_at(sk) / 2.0) * 2.0) % 2.0;
            facing = facing_index(self.st.facing_angle);
        }
        self.rec.hero(p.x, p.y, p.z, facing, anim, frame);
        // The camera follows the hero with the loudness level's lead along the heading.
        let (tx, ty) = self.lead_dir_at(sk);
        let d = &env.director;
        let lead = self.by_level(sk, &|l| by_level(&d.camera, l).lead);
        let mut zoom = self.by_level(sk, &|l| by_level(&d.camera, l).zoom);
        for i in (0..self.st.punches.len()).rev() {
            let mut pu = self.st.punches[i];
            if pu.w.is_none() && sk >= pu.s - 1e-9 {
                pu.w = Some(self.world_of(pu.s));
                self.st.punches[i] = pu;
            }
            let age = pu.w.map_or(sk - pu.s, |pw| wk - pw);
            if age > 1.2 || age < -0.05 {
                continue;
            }
            let envl = if age < 0.0 {
                smoothstep((age + 0.05) / 0.05)
            } else {
                exp(-age / 0.22)
            };
            zoom *= 1.0 + pu.amt * envl;
        }
        // Survivors world samples use the held run clock. The enclosing stop time can
        // vary with frame cuts, so easing must read this exact sample, not st.run_s.
        zoom *= self.st.boss_presentation.zoom(wk);
        self.st
            .camera
            .sample(p.x + tx * lead, p.y + ty * lead - 12.0, zoom);
        self.rec.camera(self.st.camera.x, self.st.camera.y, zoom);
        // Retire swings whose follow-through has ended.
        let pool = &self.st.swing_pool;
        self.st.live_swings.retain(|&c| {
            let sw = &pool[c];
            !(sw.struck && sw.impact + (wk - sw.w_strike) * sw.fps >= sw.frames)
        });
        let _ = hero;
    }

    /// `strideNow` (what a press would stride now), for a player's observation.
    pub fn stride_now(&self) -> f64 {
        let rule = &self.env.rule;
        let hp = self.hero_pose(self.st.w);
        let one = self.one_stride(self.st.s);
        let back = match &self.st.step {
            Some(sp) if striding(rule, Some(sp), self.st.w) => {
                max(0.0, hypot(sp.ex, sp.ey) - hypot(hp.x - sp.x0, hp.y - sp.y0))
            }
            _ => 0.0,
        };
        min(
            one,
            budget_at(rule, &self.st.strides, self.st.w, one) + back,
        )
    }
}

fn anim_idx(name: &str) -> f64 {
    HERO_ANIMS.iter().position(|x| *x == name).unwrap_or(0) as f64
}
