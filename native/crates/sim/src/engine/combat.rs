//! Swings, hits, damage, cleaves and the weapons (simulate.ts's swings, enemies' damage, weapons
//! and chain sections).

use std::collections::HashSet;
use std::rc::Rc;

use super::{
    CLEAVE_AIM_AHEAD, ChainState, Ev, HERO_RATE, HITSTOP_MAX, HITSTOP_MIN, KNOCK_TIME, KNOCKBACK,
    PendingCleave, Sim, Swing, WaveState,
};
use crate::aim::{AimTarget, ArcReach, aim, catches, swing_arc};
use crate::content::types::{EnemyDef, Size, Subdivision, WeaponDef, WeaponKind};
use crate::core::geometry::{Shape, TAU};
use crate::core::shaping::{clamp, lerp};
use crate::core::songmap::{bar_of_beat_index, energy_at, time_of_beat_index};
use crate::jsmath::{PI, abs, atan2, ceil, cos, floor, hypot, max, min, round, sin};
use crate::recorder::{AimSnapshot, ChainPoint, Fate, Hit, HitSource, PlanSwing, Reach, Weight};
use crate::tiers::{
    chain_at, heavy_arc_at, orbit_at, swing_arc_at, tier_damage, trail_radius_at, wave_reach_at,
};
use crate::timing::{CountedRating, NEVER_LATE, nearest_index};
use crate::upgrades::evolved_orbit;

/// Steps per beat for a subdivision, given the bar length (`subdivisionBeats`).
pub fn subdivision_beats(sub: Subdivision, beats_per_bar: f64) -> f64 {
    match sub {
        Subdivision::Sixteenth => 0.25,
        Subdivision::Eighth => 0.5,
        Subdivision::Beat => 1.0,
        Subdivision::Bar => beats_per_bar,
        Subdivision::TwoBar => beats_per_bar * 2.0,
    }
}

/// A hit before its id, time, section, energy and intensity are filled in.
pub(super) struct HitSpec {
    pub source: HitSource,
    pub what: String,
    pub weight: Weight,
    pub primary: bool,
    pub shape: Shape,
    pub dir: f64,
    pub accent: f64,
    pub damage: f64,
    pub group: Option<f64>,
}

impl Sim {
    pub(super) fn start_hit_stop(&mut self, len: f64) {
        if self.st.survivor.is_some() && self.env.input.zen {
            return;
        }
        let s = self.st.s;
        if self.in_stop(s) {
            return;
        }
        // The finale blast's bands never wait for a hit-stop.
        if self.blast_s().is_some_and(|b| s >= b - 1e-6) {
            return;
        }
        // A swing already armed strikes on time: the stop ends a sample before its strike.
        let mut end = s + min(len, HITSTOP_MAX);
        for &i in &self.st.swings {
            let sw = &self.st.swing_pool[i];
            if !sw.struck && sw.s_strike > s {
                end = min(end, sw.s_strike - 1.0 / HERO_RATE);
            }
        }
        if end - s < HITSTOP_MIN {
            return;
        }
        self.st.stop_start = s;
        self.st.stop_end = end;
        self.rec.hit_stop(s, end);
        self.st.q.push(end, Ev::HitstopEnd);
    }

    pub(super) fn weight_of(&self, def: &EnemyDef, formation: bool) -> f64 {
        let w = &self.env.input.lib.hero.aim_weights;
        if def.size == Size::Boss {
            w.boss
        } else if formation {
            w.formation
        } else if def.size == Size::Elite {
            w.elite
        } else {
            w.regular
        }
    }

    /// Deals damage to one enemy from a hit; records the fate.
    pub(super) fn damage(&mut self, fi: usize, h: &Hit) {
        if !self.hittable(fi) {
            return;
        }
        let w = self.st.w;
        let position = self.pos(fi, w);
        let (critical, applied) = if h.what == "bloody-tear"
            && let Some(p) = &self.st.survivor
        {
            use crate::core::rng::{Part, Rng};
            let tear = crate::survivors::weapon_catalog()
                .get(crate::survivors::WeaponId::BloodyTear)
                .level(1);
            let luck = p.stats.finalized().luck;
            let critical = tear.crit_enabled
                && Rng::new(&[
                    Part::Num(self.env.input.seed),
                    Part::Str("bloody-tear-critical"),
                    Part::Num(h.id),
                    Part::Num(self.st.foes[fi].id as f64),
                ])
                .next()
                    < tear.crit_chance * luck;
            (
                critical,
                h.damage * if critical { tear.crit_multiplier } else { 1.0 },
            )
        } else {
            (false, h.damage)
        };
        let f = &mut self.st.foes[fi];
        f.health -= applied;
        let lethal = f.health <= 1e-9;
        let fate = Fate {
            critical,
            w,
            x: position.0,
            y: position.1,
            hit: h.id,
            lethal,
            damage: applied,
        };
        f.last_fate = Some(fate);
        let boss = f.survivor_boss;
        self.rec.fate(fi, fate);
        if self.st.survivor.is_some() {
            let at = self.next_enemy_sixteenth(self.st.s);
            self.st.q.push(
                at,
                Ev::EnemyImpact {
                    boss,
                    foe: fi,
                    fate,
                    hit: Box::new(h.clone()),
                },
            );
        }
        if critical && self.st.run_s >= self.st.last_bloody_heal + 1.0 - 1e-9 {
            self.st.last_bloody_heal = self.st.run_s;
            self.heal(8.0, "bloody-tear");
        }
        if h.what == "soul-eater"
            && let Some(p) = &self.st.survivor
        {
            // Documented demo adaptation: each actual aura contact heals a source
            // Little Heart amount instead of rolling a heart only on a kill.
            let recovery = p.stats.finalized().recovery;
            self.heal(1.0 + recovery, "soul-eater");
        }
        if self.st.boss_foe == Some(fi) {
            let f = &self.st.foes[fi];
            let frac = max(0.0, f.health) / f.health0;
            self.rec.boss_health(self.st.s, frac);
        }
        if lethal {
            self.kill(fi);
            self.on_death(fi, fate, h);
        } else if self.st.survivor.is_some() {
            let data = self.env.input.lib.enemies[self.st.foes[fi].def]
                .survivors
                .expect("survivor enemy stats");
            let weapon_kb = crate::survivors::WeaponId::from_name(&h.what).map_or(5.0, |id| {
                let rank = self
                    .st
                    .survivor
                    .as_ref()
                    .expect("survivors")
                    .arsenal
                    .inventory()
                    .weapons
                    .rank(id)
                    .unwrap_or(1);
                crate::survivors::weapon_catalog()
                    .get(id)
                    .level(rank)
                    .knockback
            });
            let f = &mut self.st.foes[fi];
            if matches!(h.what.as_str(), "garlic" | "soul-eater")
                && data.res_debuffs < 1.0
                && f.knockback < data.max_knockback
            {
                // Source tests before adding: the last increment may overshoot.
                f.knockback += 0.3;
            }
            if f.knockback * weapon_kb > 0.0 {
                let at = self.next_enemy_sixteenth(self.st.s);
                self.st.q.push(
                    at,
                    Ev::Recoil {
                        foe: fi,
                        multiplier: self.st.foes[fi].knockback * weapon_kb,
                    },
                );
            }
        } else {
            let f = &self.st.foes[fi];
            if !(f.knock_t > 0.0
                && (hypot(f.knock_x, f.knock_y) * f.knock_t) / KNOCK_TIME > KNOCKBACK)
            {
                // A white flash and a knockback away from the hero.
                let hp = self.hero_pose(w);
                let dx = fate.x - hp.x;
                let dy = fate.y - hp.y;
                let dl = hypot(dx, dy);
                let dl = if dl == 0.0 || dl.is_nan() { 1.0 } else { dl };
                let f = &mut self.st.foes[fi];
                f.knock_x = (dx / dl) * KNOCKBACK;
                f.knock_y = (dy / dl) * KNOCKBACK;
                f.knock_t = KNOCK_TIME;
            }
        }
    }

    pub(super) fn survivor_recoil(&mut self, fi: usize, multiplier: f64) {
        if !self.st.foes.get(fi).is_some_and(|f| f.alive) {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        let (x, y) = self.pos(fi, self.st.w);
        let speed = self.env.input.lib.enemies[self.st.foes[fi].def].speed * multiplier;
        let dx = x - hp.x;
        let dy = y - hp.y;
        let d = hypot(dx, dy).max(1e-9);
        let f = &mut self.st.foes[fi];
        f.x = x;
        f.y = y;
        f.attack = None;
        f.last_w = self.st.w;
        f.step = None;
        f.recoil_v = (dx / d * speed, dy / d * speed);
        f.vx = f.recoil_v.0;
        f.vy = f.recoil_v.1;
        f.recoil_until = self.st.w + crate::survivors::DAMAGE_LOCKOUT_S;
    }

    fn intensity_of(&self, energy: f64, weight: Weight, accent: f64) -> f64 {
        let level = &self.env.input.level.sections[0];
        clamp(
            level.intensity
                * (0.35 + 0.65 * energy)
                * weight.factor()
                * (if accent > 0.0 { accent } else { 1.0 }),
            0.0,
            1.0,
        )
    }

    /// Records a hit at the clock's time (`addHit`), at the song's energy now unless given.
    pub(super) fn add_hit(&mut self, h: HitSpec, energy: Option<f64>) -> Hit {
        let s = self.st.s;
        let energy = energy.unwrap_or_else(|| energy_at(&self.st.song, s));
        // The might passive: the hero's hits deal more.
        let damage = if self.st.survivor.is_some() {
            // Survivor shots already carry their final data-driven damage.
            h.damage
        } else if self.st.bonus.damage > 0.0 && h.source != HitSource::Ring {
            h.damage * (1.0 + self.st.bonus.damage)
        } else {
            h.damage
        };
        let id = self.st.n_hits as f64;
        self.st.n_hits += 1;
        let intensity = self.intensity_of(energy, h.weight, h.accent);
        let hit = Hit {
            source: h.source,
            what: h.what,
            weight: h.weight,
            primary: h.primary,
            shape: h.shape,
            dir: h.dir,
            accent: h.accent,
            damage,
            group: h.group,
            id,
            s,
            w: self.st.w,
            section: 0.0,
            energy,
            intensity,
        };
        self.rec.hit(hit.clone());
        hit
    }

    /// Everyone alive inside a shape takes the hit (`applyHit`). Returns who was hit.
    pub(super) fn apply_hit(
        &mut self,
        h: &Hit,
        filter: Option<&dyn Fn(usize) -> bool>,
    ) -> Vec<usize> {
        let mut out = Vec::new();
        let w = self.st.w;
        for fi in self.st.alive.clone() {
            if !self.hittable(fi) || filter.is_some_and(|f| !f(fi)) {
                continue;
            }
            let f = &self.st.foes[fi];
            if catches(
                &h.shape,
                f.x + f.vx * (w - f.last_w),
                f.y + f.vy * (w - f.last_w),
                f.r,
            ) {
                self.damage(fi, h);
                out.push(fi);
            }
        }
        let foes = &self.st.foes;
        self.st.alive.retain(|&i| foes[i].alive);
        out
    }

    // -------------------------------------------------------------- swings

    /// Arms a swing for a slot (one swing per slot): an automatic one (`swingFor`).
    pub(super) fn swing_for(&mut self, slot: usize, s_armed: f64) -> Option<usize> {
        if self.st.survivor.is_some() && self.survivor_sword().is_none() {
            return None;
        }
        if self.st.taken_slots.contains(&slot) {
            return None;
        }
        // The finale blast ends the sword's routine (once the finale is known and has fired).
        if self
            .blast_s()
            .is_some_and(|b| self.st.sch.grid_slots[slot].s >= b - 0.04 && self.st.s >= b - 1e-9)
        {
            return None;
        }
        self.st.taken_slots.insert(slot);
        let s_strike = self.st.sch.grid_slots[slot].s;
        Some(self.arm_swing_at(s_strike, s_armed))
    }

    pub(super) fn arm_swing_at(&mut self, s_strike: f64, s_armed: f64) -> usize {
        let hero = &self.env.input.lib.hero;
        // (Reactive mode: heavy or light is decided at the wind-up, from the forecasts known by then.)
        let (frames, impact) = (6.0, 3.0);
        let s_wind = max(
            s_armed,
            s_strike - hero.swings.windup_ms / 1000.0 / max(0.05, self.base_rate(s_strike)),
        );
        let sw = Swing {
            hit: None,
            s_armed,
            s_wind,
            s_strike,
            heavy: false,
            anim: String::new(),
            impact,
            frames,
            tier: 0.0,
            accent: 0.0,
            dir: self.st.facing_angle,
            aimed: false,
            struck: false,
            w_strike: 0.0,
            undecided: true,
            fps: 16.0,
            record: None,
        };
        let i = self.st.swing_pool.len();
        self.st.swing_pool.push(sw);
        // Only swings from the last few seconds still matter.
        if self.st.swings.len() > 64 {
            let s = self.st.s;
            let pool = &self.st.swing_pool;
            self.st.swings.retain(|&x| pool[x].s_strike >= s - 8.0);
        }
        self.st.swings.push(i);
        self.st.live_swings.push(i);
        self.st.q.push(s_wind, Ev::Wind(i));
        self.st.q.push(s_strike, Ev::Strike(i));
        i
    }

    /// Current inventory is the only source of survivor sword damage/reach.
    pub(super) fn survivor_sword(&self) -> Option<(crate::survivors::WeaponId, f64, f64)> {
        let p = self.st.survivor.as_ref()?;
        let slot = p
            .arsenal
            .inventory()
            .weapons
            .iter()
            .find(|s| s.id.is_sword())?;
        let weapon = crate::survivors::weapon_catalog()
            .get(slot.id)
            .level(slot.rank);
        let stats = p.stats.finalized();
        Some((
            slot.id,
            weapon.power * stats.might,
            crate::survivors::SWORD_BASE_RADIUS * weapon.area * stats.area,
        ))
    }

    /// Amount/free-pulse shots use the same identity and animation as main beats.
    pub(super) fn survivor_sword_shot(&mut self) -> Option<(u64, f64, f64)> {
        let i = self
            .st
            .live_swings
            .iter()
            .copied()
            .find(|&i| (self.st.swing_pool[i].s_strike - self.st.s).abs() < 1e-8)
            .unwrap_or_else(|| self.arm_swing_at(self.st.s, self.st.s));
        self.do_strike(i);
        let sw = &self.st.swing_pool[i];
        sw.hit.map(|id| (id as u64, sw.dir, sw.tier))
    }

    /// A forecast accent within `within` of song time t, known by `now` (`forecastAt`).
    fn forecast_at(&self, t: f64, now: f64, within: f64) -> Option<stream::Forecast> {
        let fc = self.log.forecasts.as_ref()?;
        self.st
            .forecasts
            .iter()
            .map(|&i| fc[i])
            .find(|f| abs(f.t - t) <= within + 1e-9 && f.known_at <= now + 1e-9)
    }

    /// Stage 5, at a swing's wind-up: heavy when its slot carries a forecast accent known by now.
    fn decide_swing(&mut self, i: usize) {
        let env = Rc::clone(&self.env);
        let hero = &env.input.lib.hero;
        let s = self.st.s;
        let s_strike = self.st.swing_pool[i].s_strike;
        let f = if self.clear_at(s) {
            self.forecast_at(s_strike, s, 1e-3)
        } else {
            None
        };
        let heavy = f.is_some();
        let anim = if heavy {
            hero.swings.heavy.animation.clone()
        } else {
            let a = &hero.swings.light.animations;
            let n = self.st.light_count;
            self.st.light_count += 1;
            a[n % a.len()].clone()
        };
        let info = env.input.hero_anims.get(&anim).copied();
        let sw = &mut self.st.swing_pool[i];
        sw.undecided = false;
        sw.heavy = heavy;
        sw.accent = f.map_or(0.0, |f| f.confidence);
        sw.impact = info.and_then(|x| x.impact_frame).unwrap_or(3.0);
        sw.frames = info.map_or(6.0, |x| x.frames);
        sw.anim = anim;
        let amt = (if heavy { 0.035 } else { 0.014 }) * env.input.level.sections[0].intensity;
        self.st.punches.push(super::Punch {
            s: s_strike,
            amt,
            w: None,
        });
    }

    fn aim_targets(&self, x: f64, y: f64, reach: f64) -> Vec<AimTarget> {
        let w = self.st.w;
        let mut out = Vec::new();
        for &fi in &self.st.alive {
            if !self.hittable(fi) {
                continue;
            }
            let f = &self.st.foes[fi];
            let (fx, fy) = self.pos(fi, w);
            if abs(fx - x) > 2.0 * reach + f.r || abs(fy - y) > 2.0 * reach + f.r {
                continue;
            }
            let limit = if self.st.survivor.is_some() {
                reach + 0.5 * f.r
            } else {
                2.0 * reach + f.r
            };
            if hypot(fx - x, fy - y) > limit {
                continue;
            }
            out.push(AimTarget {
                x: fx,
                y: fy,
                r: f.r,
                weight: if f.survivor_boss {
                    self.env.input.lib.hero.aim_weights.boss
                } else {
                    f.weight
                },
            });
        }
        out
    }

    pub(super) fn tier_reach(&self, tier: f64) -> ArcReach {
        let mut arc = swing_arc_at(&self.env.input.lib.hero, tier);
        if let Some((_, _, radius)) = self.survivor_sword() {
            let hero = &self.env.input.lib.hero;
            arc.radius = radius
                * if self.env.input.zen {
                    1.0
                } else {
                    arc.radius / swing_arc_at(hero, 3.0).radius.max(1.0)
                };
        }
        ArcReach {
            radius: arc.radius,
            half_angle: (arc.degrees * PI) / 360.0,
        }
    }

    pub(super) fn heavy_tier_reach(&self, tier: f64) -> ArcReach {
        let mut arc = heavy_arc_at(&self.env.input.lib.hero, tier);
        if let Some((_, _, radius)) = self.survivor_sword() {
            let hero = &self.env.input.lib.hero;
            arc.radius = if self.env.input.zen {
                radius * hero.swings.heavy.hit_shape.radius
                    / hero.swings.light.hit_shape.radius.max(1.0)
            } else {
                radius * arc.radius / swing_arc_at(hero, 3.0).radius.max(1.0)
            };
        }
        ArcReach {
            radius: arc.radius,
            half_angle: (arc.degrees * PI) / 360.0,
        }
    }

    fn accent_aim_at(&self, tier: f64) -> ArcReach {
        ArcReach {
            radius: wave_reach_at(&self.env.input.lib.hero, tier) + CLEAVE_AIM_AHEAD,
            half_angle: self.heavy_tier_reach(tier).half_angle,
        }
    }

    /// Launches an accent's shockwave, known at song time `known` (`launchCleave`): its first band on
    /// the first eighth-note slot at least NEVER_LATE after the launch, then one band per sixteenth.
    pub(super) fn launch_cleave(&mut self, c: PendingCleave, known: f64, press: f64) {
        self.st.cleave_times.retain(|s| *s >= self.st.s - 8.0);
        if self
            .st
            .cleave_times
            .iter()
            .any(|s| abs(*s - c.s) <= super::FORECAST_WINDOW + 1e-9)
        {
            return;
        }
        if self.st.cleave_times.len() == 32 {
            self.st.cleave_times.remove(0);
        }
        self.st.cleave_times.push(c.s);
        let env = Rc::clone(&self.env);
        let hero = &env.input.lib.hero;
        let wd = &hero.swings.heavy.wave;
        let reach = self.heavy_tier_reach(c.tier);
        let song = &self.st.song;
        let mut bi0 = ceil(self.sbi_at(known) * 2.0 - 1e-9) / 2.0;
        while time_of_beat_index(song, bi0) - known < NEVER_LATE - 1e-9 {
            bi0 += 0.5;
        }
        let to = if self.st.survivor.is_some() {
            reach.radius
                + (wave_reach_at(hero, c.tier) - heavy_arc_at(hero, c.tier).radius).max(wd.band)
        } else {
            wave_reach_at(hero, c.tier)
        };
        let bands = ceil((to - reach.radius) / wd.band - 1e-9);
        let group = self.st.waves.len();
        self.st.waves.push(WaveState {
            weapon: self.survivor_sword().map(|(id, _, _)| id),
            x: c.x,
            y: c.y,
            dir: c.dir,
            group,
            mult: self.survivor_sword().map_or_else(
                || tier_damage(hero, c.tier),
                |(_, power, _)| {
                    power
                        * if env.input.zen {
                            1.0
                        } else {
                            tier_damage(hero, c.tier)
                        }
                        / hero.swings.light.damage.max(1e-9)
                },
            ),
            inner: reach.radius,
            half_angle: reach.half_angle,
            to,
            hit: c.primary_targets.into_iter().collect(),
        });
        // (Its first band's time may be a beat ahead of those known: recorded once known.)
        let launch = self.st.s;
        self.defer_at_beat(
            bi0,
            super::Then::Cleave {
                s: c.s,
                press,
                launch,
                onset: c.gi < 0.0,
            },
        );
        // (Its bands' times may read beats a little ahead of those known: each is queued once known.)
        let mut k = 0.0;
        while k < bands {
            self.push_at_beat(
                bi0 + k * 0.25,
                Ev::Wave {
                    group,
                    band: k as usize,
                },
            );
            k += 1.0;
        }
    }

    /// Stage 5: a strong onset becomes known (with the beat clear): a cleave waiting where the hero
    /// stands, unless a heavy swing on a forecast accent near it has the cleave (`onsetAccent`).
    pub(super) fn onset_accent(&mut self, i: usize) {
        let o = self.log.onsets[self.st.strong_onsets[i]];
        let pool = &self.st.swing_pool;
        let heavy_near = self.st.swings.iter().any(|&x| {
            let sw = &pool[x];
            sw.heavy && !sw.undecided && abs(sw.s_strike - o.t) <= super::FORECAST_WINDOW + 1e-9
        });
        if heavy_near || !self.clear_at(self.st.s) {
            return;
        }
        let hp = self.hero_pose(self.st.w);
        let c = PendingCleave {
            primary_targets: Vec::new(),
            gi: -1.0,
            s: o.t,
            x: hp.x,
            y: hp.y,
            dir: self.st.facing_angle,
            tier: self.st.tier,
        };
        if self.env.input.zen {
            let s = self.st.s;
            self.launch_cleave(c, s, o.t);
            return;
        }
        let great_ms = self.env.input.lib.hero.timing.great_ms;
        match self.st.last_great {
            Some((lg_s, false)) if abs(lg_s - o.t) <= great_ms / 1000.0 + 1e-9 => {
                self.st.last_great = Some((lg_s, true));
                let s = self.st.s;
                self.launch_cleave(c, s, lg_s);
                self.st.onset_cleave = None;
            }
            _ => self.st.onset_cleave = Some((c, o.t)),
        }
    }

    pub(super) fn do_wind(&mut self, i: usize) {
        if self.st.swing_pool[i].undecided {
            self.decide_swing(i);
        }
        let hp = self.hero_pose(self.st.w);
        let tier = self.st.tier;
        let heavy = self.st.swing_pool[i].heavy;
        let reach = if heavy && self.st.survivor.is_none() {
            self.accent_aim_at(tier)
        } else if heavy {
            self.heavy_tier_reach(tier)
        } else {
            self.tier_reach(tier)
        };
        let targets = self.aim_targets(hp.x, hp.y, reach.radius);
        let mut snap = AimSnapshot {
            x: hp.x,
            y: hp.y,
            facing: self.st.facing_angle,
            targets: Vec::new(),
        };
        for t in &targets {
            snap.targets.extend([t.x, t.y, t.r, t.weight]);
        }
        let dir = aim(hp.x, hp.y, self.st.facing_angle, &targets, reach);
        self.st.facing_angle = dir;
        let record_reach = if self.st.survivor.is_some() {
            Reach {
                radius: if heavy {
                    self.heavy_tier_reach(tier).radius
                } else {
                    self.tier_reach(tier).radius
                },
                degrees: 360.0,
            }
        } else if heavy {
            Reach {
                radius: reach.radius,
                degrees: (reach.half_angle * 360.0) / PI,
            }
        } else {
            let a = swing_arc_at(&self.env.input.lib.hero, tier);
            Reach {
                radius: a.radius,
                degrees: a.degrees,
            }
        };
        let base_rate = self.base_rate(self.st.swing_pool[i].s_strike);
        let sw = &mut self.st.swing_pool[i];
        sw.tier = tier;
        sw.dir = dir;
        sw.aimed = true;
        sw.record = Some(PlanSwing {
            s: sw.s_strike,
            w: 0.0,
            s_armed: sw.s_armed,
            s_wind: sw.s_wind,
            kind: if heavy { "heavy" } else { "light" },
            anim: sw.anim.clone(),
            impact_frame: sw.impact,
            dir,
            x: 0.0,
            y: 0.0,
            hit: -1.0,
            accent: sw.accent,
            aim: snap,
            tier,
            reach: record_reach,
        });
        let wind_w = max(1.0 / 240.0, (sw.s_strike - sw.s_wind) * base_rate);
        sw.fps = if sw.s_strike <= sw.s_wind + 1e-9 {
            24.0
        } else {
            sw.impact / wind_w
        };
    }

    pub(super) fn do_strike(&mut self, i: usize) {
        if self.st.swing_pool[i].struck || (self.st.survivor.is_some() && !self.weapons_active()) {
            return;
        }
        if !self.st.swing_pool[i].aimed {
            self.do_wind(i);
        }
        let env = Rc::clone(&self.env);
        let hero = &env.input.lib.hero;
        let hp = self.hero_pose(self.st.w);
        let (heavy, tier, dir, accent, s_strike) = {
            let sw = &self.st.swing_pool[i];
            (sw.heavy, sw.tier, sw.dir, sw.accent, sw.s_strike)
        };
        let def_damage = if heavy {
            hero.swings.heavy.damage
        } else {
            hero.swings.light.damage
        };
        let reach = if heavy {
            self.heavy_tier_reach(tier)
        } else {
            self.tier_reach(tier)
        };
        let survivor = self.survivor_sword();
        let shape = if survivor.is_some() {
            Shape::Circle {
                x: hp.x,
                y: hp.y,
                radius: reach.radius,
            }
        } else {
            swing_arc(hp.x, hp.y, dir, reach)
        };
        let mult = tier_damage(hero, tier);
        let h = self.add_hit(
            HitSpec {
                source: HitSource::Move,
                what: survivor.map_or_else(
                    || (if heavy { "cleave" } else { "slash" }).into(),
                    |(id, _, _)| id.as_str().into(),
                ),
                weight: if heavy { Weight::Heavy } else { Weight::Light },
                primary: true,
                shape,
                dir,
                accent,
                damage: survivor.map_or(def_damage * mult, |(_, power, _)| {
                    power
                        * if env.input.zen { 1.0 } else { mult }
                        * if heavy {
                            hero.swings.heavy.damage / hero.swings.light.damage.max(1e-9)
                        } else {
                            1.0
                        }
                }),
                group: None,
            },
            None,
        );
        let primary_targets = self.apply_hit(&h, None);
        let w = self.st.w;
        let sw = &mut self.st.swing_pool[i];
        sw.struck = true;
        sw.hit = Some(h.id);
        sw.w_strike = w;
        let mut r = sw.record.take().expect("an aimed swing's record");
        r.w = w;
        r.x = hp.x;
        r.y = hp.y;
        r.hit = h.id;
        self.rec.swing(r);
        if heavy {
            // Press cleaves: the heavy impact is everyone's; the accent's counted press of Great or
            // better launches its shockwave, now when it came first (with a hit-stop), or when it comes.
            let gi = nearest_index(&self.st.sch.grid_times, s_strike);
            let c = PendingCleave {
                primary_targets,
                gi: gi as f64,
                s: s_strike,
                x: hp.x,
                y: hp.y,
                dir,
                tier,
            };
            let memo = if env.input.settling {
                &self.st.prov_memo
            } else {
                &self.st.memo
            };
            let counted = memo.counted.get(&(gi as i64)).copied();
            let s = self.st.s;
            // Zen mode: nothing is judged, and every accent's cleave launches as it strikes (with no
            // hit-stop, which would freeze a stride off the beat).
            if env.input.zen {
                self.launch_cleave(c, s, s);
                return;
            }
            match counted {
                Some(counted) if counted.s <= s + 1e-9 => {
                    let lg = self.st.last_great;
                    let spent = lg.is_some_and(|(ls, used)| abs(ls - counted.s) < 1e-9 && used);
                    if matches!(
                        counted.rating,
                        CountedRating::Perfect | CountedRating::Great
                    ) && !spent
                    {
                        self.start_hit_stop(min(
                            0.15,
                            (hero.swings.heavy.hit_stop_ms / 1000.0) * (0.6 + 0.4 * accent),
                        ));
                        self.launch_cleave(c, s, counted.s);
                        if let Some((ls, _)) = lg
                            && abs(ls - counted.s) < 1e-9
                        {
                            self.st.last_great = Some((ls, true));
                        }
                    }
                    self.st.pending_cleave = None;
                }
                _ => self.st.pending_cleave = Some(c),
            }
        }
    }

    pub(super) fn wave_band(&mut self, group: usize, band: usize) {
        // The wave rests during a hit-stop, like the weapons.
        if self.in_stop(self.st.s) && self.st.s > self.st.stop_start + 1e-9 {
            return;
        }
        let wd = &self.env.input.lib.hero.swings.heavy.wave;
        let (wave_damage, band_w) = (wd.damage, wd.band);
        let wv = &self.st.waves[group];
        let outer = min(wv.inner + band_w * (band as f64 + 1.0), wv.to);
        let shape = Shape::Arc {
            x: wv.x,
            y: wv.y,
            radius: outer,
            dir: wv.dir,
            half_angle: wv.half_angle,
            inner: Some(wv.inner),
        };
        let spec = HitSpec {
            source: HitSource::Wave,
            what: wv.weapon.map_or("shockwave", |id| id.as_str()).into(),
            weight: Weight::Medium,
            primary: false,
            shape,
            dir: wv.dir,
            accent: 0.0,
            damage: wave_damage * wv.mult,
            group: Some(wv.group as f64),
        };
        let h = self.add_hit(spec, None);
        self.rec.wave_hit(group, h.id as usize);
        let already = self.st.waves[group].hit.clone();
        let filter = move |fi: usize| !already.contains(&fi);
        for fi in self.apply_hit(&h, Some(&filter)) {
            self.st.waves[group].hit.insert(fi);
        }
    }

    // -------------------------------------------------------------- weapons

    pub(super) fn grant_weapon(&mut self, id: &str) {
        let s = self.st.s;
        self.rec.weapon(id, s);
        let wi = self
            .env
            .input
            .lib
            .weapons
            .get_index_of(id)
            .expect("a known weapon");
        let wd = &self.env.input.lib.weapons[wi];
        if wd.kind == WeaponKind::Blast {
            return;
        }
        let every = wd.fires.every;
        self.schedule_fires(wi, every, None);
    }

    /// A weapon's slots within committed beat bi, from song time `after` on (`fireBeat`).
    pub(super) fn fire_beat(
        &mut self,
        wi: usize,
        every: Subdivision,
        skip: Option<Subdivision>,
        bi: f64,
        after: f64,
    ) {
        let song = &self.st.song;
        if bi + 1.0 > song.beats.len() as f64 - 1.0 {
            return;
        }
        let idx = &self.st.idx;
        let bar = max(1.0, bar_of_beat_index(idx, bi + 1e-6));
        let b0 = idx.bar_start[bar as usize];
        let bpb = idx
            .beats_per_bar
            .get(bar as usize)
            .copied()
            .filter(|x| !x.is_nan())
            .unwrap_or(2.0);
        let step_b = subdivision_beats(every, bpb);
        let skip_b = skip.map(|x| subdivision_beats(x, bpb));
        let mut pushes = Vec::new();
        let mut off = ceil((bi - b0) / step_b - 1e-9) * step_b;
        while off < bi + 1.0 - b0 - 1e-9 {
            let skipped = skip_b.is_some_and(|sb| abs(off / sb - round(off / sb)) < 1e-6);
            if !skipped {
                let t = time_of_beat_index(song, b0 + off);
                if !(t < after - 1e-9 || t > self.st.end_s) {
                    pushes.push((t, b0 + off));
                }
            }
            off += step_b;
        }
        for (t, b) in pushes {
            self.st.q.push(t, Ev::Fire { weapon: wi, bi: b });
        }
    }

    /// A weapon fires on every slot of its subdivision after now: the rest of the current beat now,
    /// and each later beat queues its own as it arrives (`scheduleFires`, reactive mode).
    pub(super) fn schedule_fires(
        &mut self,
        wi: usize,
        every: Subdivision,
        skip: Option<Subdivision>,
    ) {
        self.st.fire_specs.push(super::FireSpec {
            weapon: wi,
            every,
            skip,
        });
        let s = self.st.s;
        let bi = floor(self.sbi_at(s) + 1e-9);
        if bi >= 0.0 && s > 0.0 {
            self.fire_beat(wi, every, skip, bi, s + 0.01);
        }
    }

    pub(super) fn fire(&mut self, wi: usize, bi: f64) {
        // Weapons rest during a hit-stop.
        if self.in_stop(self.st.s) {
            return;
        }
        let env = Rc::clone(&self.env);
        let hero = &env.input.lib.hero;
        let wd: &WeaponDef = &env.input.lib.weapons[wi];
        let s = self.st.s;
        self.rec.fire(&wd.id, s);
        let hp = self.hero_pose(self.st.w);
        let energy = energy_at(&self.st.song, s);
        let tier = self.st.tier;
        let mult = tier_damage(hero, tier);
        let weight = Weight::from_content(wd.weight);
        match wd.kind {
            WeaponKind::Orbit => {
                let o = wd.orbit.as_ref().expect("an orbit");
                let bar = max(1.0, bar_of_beat_index(&self.st.idx, bi));
                let bpb = self
                    .st
                    .idx
                    .beats_per_bar
                    .get(bar as usize)
                    .copied()
                    .filter(|x| !x.is_nan())
                    .unwrap_or(2.0);
                let th0 = (TAU * bi) / (o.period_bars * bpb);
                let orbit = evolved_orbit(
                    wd,
                    orbit_at(wd, tier),
                    self.st.loadout.evolved.contains(&wd.id),
                );
                let radius = wd.hit_shape.as_ref().expect("a hit shape").radius;
                let mut k = 0.0;
                while k < orbit.count {
                    let th = th0 + (k * TAU) / orbit.count;
                    let x = hp.x + cos(th) * orbit.radius;
                    let y = hp.y + sin(th) * orbit.radius * 0.8;
                    let spec = HitSpec {
                        source: HitSource::Weapon,
                        what: wd.id.clone(),
                        weight,
                        primary: false,
                        shape: Shape::Circle { x, y, radius },
                        dir: th + PI / 2.0,
                        accent: 0.0,
                        damage: wd.damage * mult,
                        group: None,
                    };
                    let h = self.add_hit(spec, Some(energy));
                    self.apply_hit(&h, None);
                    k += 1.0;
                }
            }
            WeaponKind::Chain => {
                let c = wd.chain.as_ref().expect("a chain");
                let tiered = chain_at(wd, tier);
                // An evolved chain jumps further.
                let more = if self.st.loadout.evolved.contains(&wd.id) {
                    wd.evolve.as_ref().map_or(0.0, |e| e.jumps)
                } else {
                    0.0
                };
                let jumps = tiered
                    .as_ref()
                    .map_or_else(|| round(lerp(c.jumps[0], c.jumps[1], energy)), |t| t.jumps)
                    + more;
                let first = ChainPoint {
                    w: self.st.w,
                    x: hp.x,
                    y: hp.y - 24.0,
                };
                let index = self.st.chains.len();
                self.st.chains.push(ChainState {
                    last: first,
                    index,
                    hit: HashSet::new(),
                    jumps,
                    energy,
                    bi,
                    range: tiered.map(|t| t.range),
                    mult,
                });
                self.rec.chain(first);
                self.chain_jump(wi, index, 0);
            }
            WeaponKind::Trail => {
                let tr = wd.trail.as_ref().expect("a trail");
                let beats = &self.st.song.beats;
                let at = |i: f64| beats.get(i as usize).copied().unwrap_or(f64::NAN);
                let fb = floor(bi);
                let bd = (at(min(beats.len() as f64 - 1.0, fb)) - at(max(0.0, fb - 1.0)))
                    * self.base_rate(s);
                let w = self.st.w;
                let w_from = w - tr.beats * max(0.05, bd);
                let base = self.st.hero_base;
                assert!(
                    !(base > 0 && base as f64 / HERO_RATE >= w_from),
                    "the fire trail reaches back past the kept hero samples (w={w:.2})"
                );
                let (mut last_x, mut last_y) = (f64::INFINITY, f64::INFINITY);
                let n = self.st.hero_x.len();
                let mut k = (base + n) as i64 - 1;
                while k >= base as i64 && k as f64 / HERO_RATE >= w_from {
                    if k as f64 / HERO_RATE > w - 0.12 {
                        k -= 1;
                        continue;
                    }
                    let x = self.st.hero_x[k as usize - base];
                    let y = self.st.hero_y[k as usize - base];
                    if hypot(x - last_x, y - last_y) < tr.spacing {
                        k -= 1;
                        continue;
                    }
                    last_x = x;
                    last_y = y;
                    let spec = HitSpec {
                        source: HitSource::Weapon,
                        what: wd.id.clone(),
                        weight,
                        primary: false,
                        shape: Shape::Circle {
                            x,
                            y,
                            radius: trail_radius_at(wd, tier),
                        },
                        dir: 0.0,
                        accent: 0.0,
                        damage: wd.damage * mult,
                        group: None,
                    };
                    let h = self.add_hit(spec, Some(energy));
                    self.apply_hit(&h, None);
                    k -= 1;
                }
            }
            WeaponKind::Blast => {}
        }
    }

    pub(super) fn chain_jump(&mut self, wi: usize, ci: usize, j: usize) {
        let s = self.st.s;
        if self.in_stop(s) {
            // A jump inside a hit-stop waits for the next sixteenth after it.
            let bi = floor(self.sbi_at(s) * 4.0 + 1e-6) / 4.0 + 0.25;
            self.push_at_beat(
                bi,
                Ev::Chain {
                    weapon: wi,
                    chain: ci,
                    jump: j,
                },
            );
            return;
        }
        let env = Rc::clone(&self.env);
        let wd = &env.input.lib.weapons[wi];
        let c = wd.chain.as_ref().expect("a chain");
        let w = self.st.w;
        let ch = &self.st.chains[ci];
        let fx = ch.last.x;
        let fy = if j == 0 {
            ch.last.y + 24.0
        } else {
            ch.last.y + 16.0
        };
        let mut best: Option<usize> = None;
        let mut best_d = ch.range.unwrap_or(c.range);
        for &fi in &self.st.alive {
            let f = &self.st.foes[fi];
            if !f.alive || ch.hit.contains(&fi) {
                continue;
            }
            let d = hypot(
                f.x + f.vx * (w - f.last_w) - fx,
                f.y + f.vy * (w - f.last_w) - fy,
            );
            if d < best_d {
                best_d = d;
                best = Some(fi);
            }
        }
        let Some(bf) = best else { return };
        let f = &self.st.foes[bf];
        let bx = f.x + f.vx * (w - f.last_w);
        let by = f.y + f.vy * (w - f.last_w);
        let p = ChainPoint {
            w,
            x: bx,
            y: by - 16.0,
        };
        let ch = &mut self.st.chains[ci];
        ch.hit.insert(bf);
        ch.last = p;
        let (index, energy, mult, jumps, cbi) = (ch.index, ch.energy, ch.mult, ch.jumps, ch.bi);
        self.rec.chain_point(index, p);
        let spec = HitSpec {
            source: HitSource::Weapon,
            what: wd.id.clone(),
            weight: Weight::from_content(wd.weight),
            primary: false,
            shape: Shape::Circle {
                x: bx,
                y: by,
                radius: wd.hit_shape.as_ref().expect("a hit shape").radius,
            },
            dir: atan2(by - fy, bx - fx),
            accent: 0.0,
            damage: wd.damage * mult,
            group: None,
        };
        let h = self.add_hit(spec, Some(energy));
        self.apply_hit(&h, None);
        if ((j + 1) as f64) < jumps {
            let step_b = subdivision_beats(c.step, 2.0);
            self.push_at_beat(
                cbi + (j as f64 + 1.0) * step_b,
                Ev::Chain {
                    weapon: wi,
                    chain: ci,
                    jump: j + 1,
                },
            );
        }
    }
}
