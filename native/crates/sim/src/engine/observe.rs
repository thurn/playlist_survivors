//! What a player sees when it decides (`observe` in simulate.ts): the hero, the crowd, the threats
//! it has had time to see, the set pieces, gems, orbs and chickens, from the state now. Nothing
//! here reads the stream: a player sees the game, never the music's future.

use super::pieces::{RING_CLOSE, SLAM_RADIUS};
use super::{HURTBOX_LIFT, HURTBOX_RADIUS, Sim};
use crate::core::shaping::{clamp, lerp};
use crate::jsmath::hypot;
use crate::player::{
    EnemyView, GemView, HeroView, Observation, OrbView, Point, RingView, RunnerView, ThreatKind,
    ThreatView,
};
use crate::world::Frame;

/// A threat shows to the player this long after it appears.
const REACTION: f64 = 0.15;
/// Gems within this much of the pickup radius, and vacuums and chickens within this of the hero, are in sight.
const GEM_SEEN: f64 = 300.0;
const VACUUM_SEEN: f64 = 400.0;
/// Stampede runners within this of the hero are in sight.
const RUNNERS_SEEN: f64 = 300.0;

impl Sim {
    /// What a player sees now.
    pub fn observe(&self) -> Observation {
        let st = &self.st;
        let (s, w) = (st.s, st.w);
        let hp = self.hero_pose(w);
        let alive = || st.alive.iter().copied().filter(|&fi| st.foes[fi].alive);
        let lib = &self.env.input.lib;
        let enemies = alive()
            .map(|fi| {
                let (x, y) = self.pos(fi, w);
                let f = &st.foes[fi];
                EnemyView {
                    x,
                    y,
                    kind: lib.enemies[f.def].id.clone(),
                    health: f.health,
                }
            })
            .collect();
        let boss = st
            .boss_foe
            .filter(|&b| st.foes[b].alive && !self.pinned(b))
            .map(|b| {
                let (x, y) = self.pos(b, w);
                Point { x, y }
            });
        // The middle of the formation members still in their slots.
        let (mut fx, mut fy, mut n) = (0.0, 0.0, 0.0);
        for fi in alive() {
            let f = &st.foes[fi];
            if f.slot.is_some() && s < f.slot_until {
                let (x, y) = self.pos(fi, w);
                fx += x;
                fy += y;
                n += 1.0;
            }
        }
        let formation = (n > 0.0).then(|| Point {
            x: fx / n,
            y: fy / n,
        });
        // The latest ring still closing.
        let ring = st
            .rings
            .iter()
            .rev()
            .find(|r| s >= r.s0 && s < r.s1)
            .map(|r| RingView {
                x: r.x,
                y: r.y,
                r: lerp(
                    r.r0,
                    RING_CLOSE,
                    clamp((s - r.k1) / (r.s1 - r.k1), 0.0, 1.0),
                ),
                gaps: r.gaps.clone(),
            });
        let runners: Vec<RunnerView> = alive()
            .filter_map(|fi| {
                let (vx, vy) = st.foes[fi].run?;
                let (x, y) = self.pos(fi, w);
                (hypot(x - hp.x, y - hp.y) < RUNNERS_SEEN).then_some(RunnerView { x, y, vx, vy })
            })
            .collect();
        let reach = self.pickup_radius() + GEM_SEEN;
        let gems: Vec<GemView> = st
            .field
            .iter()
            .filter(|g| hypot(g.x - hp.x, g.y - hp.y) <= reach)
            .map(|g| GemView {
                x: g.x,
                y: g.y,
                value: g.value,
            })
            .collect();
        let vacuum = st
            .vacuums
            .iter()
            .filter(|v| hypot(v.x - hp.x, v.y - hp.y) <= VACUUM_SEEN)
            .min_by(|a, b| hypot(a.x - hp.x, a.y - hp.y).total_cmp(&hypot(b.x - hp.x, b.y - hp.y)))
            .map(|v| Point { x: v.x, y: v.y });
        let orbs = st.orbs.as_ref().map(|o| {
            o.pts
                .iter()
                .zip(&o.offers)
                .map(|(&(x, y), item)| OrbView {
                    x,
                    y,
                    item: item.clone(),
                })
                .collect()
        });
        let chickens: Vec<Point> = st
            .chickens
            .iter()
            .filter(|c| hypot(c.x - hp.x, c.y - hp.y) <= VACUUM_SEEN)
            .map(|c| Point { x: c.x, y: c.y })
            .collect();
        let lu = &st.sch.level_ups;
        let level_up_at = (st.level < lu.len()
            && !st.overdue
            && st.xp_now >= self.level_req()
            && lu[st.level].s >= s
            && lu[st.level].s.is_finite())
        .then(|| lu[st.level].s);
        let (px, py, tx, ty) = self.forward();
        Observation {
            run: self.env.input.level.survivors.map(|_| self.run_state()),
            s,
            w,
            hero: HeroView {
                x: hp.x,
                y: hp.y,
                stepping: hp.u < 1.0,
                stride: Some(self.stride_now()),
                tier: Some(st.tier),
                pickup: Some(self.pickup_radius()),
                health: Some(st.health / self.health_max()),
            },
            enemies,
            threats: self.visible_threats(hp.x, hp.y),
            boss,
            formation,
            ring,
            runners: (!runners.is_empty()).then_some(runners),
            gems: (!gems.is_empty()).then_some(gems),
            vacuum,
            orbs,
            level_up_at,
            chickens: (!chickens.is_empty()).then_some(chickens),
            pace_point: Frame {
                x: px,
                y: py,
                tx,
                ty,
            },
        }
    }

    /// Threats the player can see: archers drawing, arrows in flight and slam markers, each once it
    /// has been in view for REACTION.
    fn visible_threats(&self, hx: f64, hy: f64) -> Vec<ThreatView> {
        let st = &self.st;
        let (s, w) = (st.s, st.w);
        let mut out = Vec::new();
        for v in &st.plan.volleys {
            if s < v.s_draw + REACTION - 1e-9 || s > v.s_release + 1e-9 {
                continue;
            }
            for &fi in &st.alive {
                let f = &st.foes[fi];
                if !f.alive || f.archer_group != Some(v.group) || f.drawing.is_none() {
                    continue;
                }
                let (x, y) = self.pos(fi, w);
                out.push(ThreatView {
                    kind: ThreatKind::Arrow,
                    x,
                    y: y - 24.0,
                    ax: Some(hx),
                    ay: Some(hy - HURTBOX_LIFT),
                    lands_at: v.s_arrive,
                    radius: None,
                    seen_at: v.s_draw,
                });
            }
        }
        for a in &st.flying {
            let v = &st.plan.volleys[a.v];
            if s < v.s_draw + REACTION - 1e-9 {
                continue;
            }
            out.push(ThreatView {
                kind: ThreatKind::Arrow,
                x: a.x0,
                y: a.y0,
                ax: Some(a.ax),
                ay: Some(a.ay),
                lands_at: v.s_arrive,
                radius: None,
                seen_at: v.s_draw,
            });
        }
        if let Some(b) = st.plan.boss.as_ref() {
            for &(k, x, y, s_mark, _) in &st.slam_marks {
                if s >= s_mark + REACTION - 1e-9 {
                    out.push(ThreatView {
                        kind: ThreatKind::Slam,
                        x,
                        y,
                        ax: None,
                        ay: None,
                        lands_at: b.slams[k].s_land,
                        radius: Some(SLAM_RADIUS + HURTBOX_RADIUS),
                        seen_at: s_mark,
                    });
                }
            }
        }
        out
    }
}
