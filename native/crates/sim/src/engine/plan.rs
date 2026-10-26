//! Reactive mode's director and set pieces as a state machine (`src/reactive/sections.ts`,
//! `director.ts`'s zone waves, `setpieces.ts`, and the simulation's init for them).
//!
//! TS planned every set piece from the whole stream before the run and queued their events at init.
//! Here each decision is made as soon as the entries it reads have been taken in, and each event is
//! queued with the key TS gave it at init (docs/design/native-app.md, "Set pieces as a state
//! machine"):
//!
//! - **Detected sections** are found in `knownAt` order as structure entries arrive; each takes its
//!   downbeat once that beat is known (one past the stream's last committed beat is dropped).
//! - **Candidates** (a boss, ring and wedge on section starts, stampedes on jumps, the kick line on a
//!   run of medium beats, volleys on loud stretches) are generated at their triggers. Each starts on
//!   the effective downbeat after its trigger, and a later trigger never gives an earlier downbeat,
//!   so a candidate is decided, in TS's order, once no candidate still to come could sort before it.
//! - **Parts known later** wait for the entries they read: a formation's release beat, a ring's
//!   close (the first accent known after it starts, or its last beat), the kick line's release (the
//!   first strong onset after its dance), each boss slam and each archer volley (decided at its mark
//!   or draw, at the key TS gave that slam or volley), a fallback zone wave (decided at its beat).
//! - **The finale** fires on a confirmed end: an `ends` entry once 3 s of quiet below the gate have
//!   held, from the first quiet tick within 3 s after it, after 30 s of judged time; it ends the run
//!   `epilogueS` later. Nothing starts at or
//!   after it: a set piece accepted before the finale was known that would start after it is revoked.

use std::rc::Rc;

use super::{Ev, Sim, phase};
use crate::content::level::Director;
use crate::content::types::{FormationShape, Release};
use crate::jsmath::{max, min, round};
use crate::reactive::director::tick_index_in;
use crate::schedule::{RateStep, step_rate};
use stream::Level;

/// A stretch with loud ticks under this share still counts as not loud (for a stampede).
const QUIET_BLIP: f64 = 0.05;
/// The boss drops in this long before it lands.
pub(super) const BOSS_DROP_S: f64 = 0.55;
/// How long the loud, clear stretch must have been over before another volley group can start.
const VOLLEY_REARM_S: f64 = 4.0;
/// Seconds of notice a downbeat effect needs after its trigger is known.
pub(super) const DOWNBEAT_NOTICE: f64 = 0.35;
/// A confirmed end: the quiet held this long from its start (tuned in D1).
const CONFIRM_S: f64 = 3.0;
/// The quiet that confirms an end starts within this long after it (the evidence trails off for
/// a second or so after the analyzer's end).
const QUIET_WITHIN: f64 = 3.0;
/// No end is confirmed before this much judged time in the run.
const MIN_JUDGED_S: f64 = 30.0;
/// The camera punch on a detected section's start.
pub(super) const SECTION_PUNCH: f64 = 0.035;

/// A detected section start: its downbeat (a committed beat's time and index) and when it was known.
#[derive(Clone, Copy, Debug, PartialEq)]
pub(super) struct DetectedSection {
    pub s: f64,
    pub beat: usize,
    pub known_at: f64,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub(super) enum Kind {
    Boss,
    Ring,
    Stampede,
    Wedge,
    KickLine,
    Volleys,
}

impl Kind {
    fn priority(self) -> u8 {
        match self {
            Kind::Boss => 0,
            Kind::Ring => 1,
            Kind::Stampede => 2,
            Kind::Wedge | Kind::KickLine => 3,
            Kind::Volleys => 4,
        }
    }
}

/// A set piece that may start (`Candidate`), with TS's array order as the last tie-break.
#[derive(Clone, Debug)]
struct Candidate {
    kind: Kind,
    s: f64,
    /// A volley retry sorts after every other candidate of its time and kind.
    retry: bool,
    /// TS's array order: the generating group (sections, stampedes, kick line, volleys), its index,
    /// and the order within it.
    group: u8,
    index: usize,
    sub: u8,
    /// Kind's data: the boss's landing time, the ring's section.
    s_enter: f64,
    section: usize,
}

impl Candidate {
    fn key(&self) -> (f64, u8, bool, u8, usize, u8) {
        (
            self.s,
            self.kind.priority(),
            self.retry,
            self.group,
            self.index,
            self.sub,
        )
    }

    fn before(&self, b: &Candidate) -> bool {
        let (x, y) = (self.key(), b.key());
        x.0 < y.0 || (x.0 == y.0 && (x.1, x.2, x.3, x.4, x.5) < (y.1, y.2, y.3, y.4, y.5))
    }
}

/// A formation set piece: a wedge or the kick line.
#[derive(Clone, Debug)]
pub(super) struct FormationPlan {
    pub def: String,
    pub s_start: f64,
    pub s_release: Option<f64>,
    pub kick: Option<KickPlan>,
}

/// The kick line's release: the first strong onset at or after beat `from_beat`, or `fallback_beat`.
#[derive(Clone, Copy, Debug)]
pub(super) struct KickPlan {
    from_beat: usize,
    fallback_beat: usize,
}

#[derive(Clone, Debug)]
pub(super) struct RingPlan {
    pub s: f64,
    pub enemy: String,
    pub count: f64,
    pub gaps: f64,
    /// When the close starts and where it ends, once known.
    pub k_close: Option<f64>,
    pub s_close: Option<f64>,
    /// The ring's place in push order for its close, reserved when it entered (None: not yet, or no
    /// members, so no close).
    pub close_seq: Option<u64>,
    pub entered: bool,
    fallback_beat: usize,
    after: f64,
}

#[derive(Clone, Debug)]
pub(super) struct StampedePlan {
    pub s: f64,
    pub enemy: String,
    pub count: f64,
    pub rows: f64,
}

#[derive(Clone, Copy, Debug)]
pub(super) struct VolleyPlan {
    /// When its archers started drawing (its check's time).
    pub s_draw: f64,
    pub s_release: f64,
    pub s_arrive: f64,
    pub archers: f64,
    pub group: usize,
}

#[derive(Clone, Copy, Debug)]
pub(super) struct ArcherGroup {
    pub s: f64,
    pub archers: f64,
}

#[derive(Clone, Copy, Debug)]
pub(super) struct Slam {
    pub s_land: f64,
}

#[derive(Clone, Debug)]
pub(super) struct BossPlan {
    pub kind: String,
    pub s_enter: f64,
    pub slams: Vec<Slam>,
    pub s_retreat: f64,
    /// Slam marks checked so far (the key of the next check).
    checks: u64,
}

#[derive(Clone, Debug)]
pub(super) struct FinalePlan {
    pub s: f64,
    pub blast: String,
}

/// The planner's state (snapshot state).
#[derive(Clone, Debug, Default)]
pub(super) struct Plan {
    // Detected sections.
    next_change: usize,
    next_jump: usize,
    pub sections: Vec<DetectedSection>,
    last_section_known_at: Option<f64>,
    // Zone waves.
    pub(super) zone_last: f64,
    pub(super) zone_section: usize,
    pub zone_waves: u64,
    pub(super) zone_check: Option<f64>,
    // Candidates.
    sections_seen: usize,
    jumps_seen: usize,
    kick_beat: usize,
    kick_run: f64,
    kick_taken: bool,
    volley_tick: usize,
    volley_on: bool,
    volley_off_since: f64,
    cands: Vec<Candidate>,
    /// Volley retries whose downbeats are not known yet (from when): no candidate at or after the
    /// earliest may be decided.
    retry_from: Vec<f64>,
    retries: usize,
    last: f64,
    last_other: f64,
    boss_taken: bool,
    // The schedule.
    pub formations: Vec<FormationPlan>,
    pub rings: Vec<RingPlan>,
    pub stampedes: Vec<StampedePlan>,
    pub archer_groups: Vec<ArcherGroup>,
    pub volleys: Vec<VolleyPlan>,
    pub boss: Option<BossPlan>,
    pub finale: Option<FinalePlan>,
    pub landmarks: u64,
    /// `ends` entries tried for a confirmed end.
    ends_seen: usize,
}

impl Plan {
    pub(super) fn new() -> Plan {
        Plan {
            last: f64::NEG_INFINITY,
            last_other: f64::NEG_INFINITY,
            ..Plan::default()
        }
    }
}

/// A beat-time question asked of the stream known so far: answered, or not yet.
enum Known<T> {
    Yes(T),
    NotYet,
}

impl Sim {
    fn director(&self) -> &Director {
        &self.env.director
    }

    /// The ingestion horizon: every entry known by then has been taken in.
    fn horizon(&self) -> f64 {
        self.st.horizon
    }

    /// The latest known tick's level at or before s (medium before the first, or without levels).
    fn level_at(&self, s: f64) -> Level {
        let ticks = &self.log.ticks[..self.st.ingested.ticks];
        let i = tick_index_in(ticks, s);
        if i < 0 {
            Level::Medium
        } else {
            ticks[i as usize].level.unwrap_or(Level::Medium)
        }
    }

    fn evidence_known_at(&self, s: f64) -> f64 {
        let ticks = &self.log.ticks[..self.st.ingested.ticks];
        let i = tick_index_in(ticks, s);
        if i < 0 {
            0.0
        } else {
            ticks[i as usize].evidence
        }
    }

    /// The index of the latest committed beat at or before s (within 1e-6), if the beats known show
    /// it (`beatIndexAtOrBefore`).
    fn beat_at_or_before(&self, s: f64) -> Known<isize> {
        let beats = &self.log.beats[..self.st.ingested.beats];
        let whole = false;
        let n = beats.partition_point(|b| b.t <= s + 1e-6);
        if n < beats.len() || whole {
            Known::Yes(n as isize - 1)
        } else {
            Known::NotYet
        }
    }

    /// The effective downbeat after an event known at k (`effectiveDownbeat`): the first committed
    /// beat on a bar's first beat at least DOWNBEAT_NOTICE after k; its index.
    fn effective_downbeat(&self, k: f64) -> Known<usize> {
        let mut i = match self.beat_at_or_before(k + DOWNBEAT_NOTICE - 2e-6) {
            Known::Yes(j) => (j + 1) as usize,
            Known::NotYet => return Known::NotYet,
        };
        if i % 2 != 0 {
            i += 1;
        }
        // (Live first: the stream's end is never known, so a beat past those known is pending.)
        if i < self.st.ingested.beats {
            Known::Yes(i)
        } else {
            Known::NotYet
        }
    }

    /// The time of the effective downbeat after k (`downbeatAfter` with its notice).
    fn downbeat_after(&self, k: f64, notice: f64) -> Known<f64> {
        match self.effective_downbeat(k + notice) {
            Known::Yes(i) => Known::Yes(self.log.beats[i].t),
            Known::NotYet => Known::NotYet,
        }
    }

    /// The committed beat a time falls on or after: its index (`beatAtOrAfter`).
    fn beat_at_or_after(&self, t: f64) -> usize {
        self.log.beats[..self.st.ingested.beats].partition_point(|b| b.t < t - 1e-6)
    }

    /// The set pieces' end of starts: the finale, once known.
    fn until(&self) -> f64 {
        self.st.plan.finale.as_ref().map_or(f64::INFINITY, |f| f.s)
    }

    /// Takes in what the entries taken in so far decide (called after each ingestion).
    pub(super) fn plan(&mut self) {
        if self.st.freeze.frozen {
            return;
        }
        self.detect_sections();
        self.plan_zone_waves();
        if self.director().set_pieces.is_none() {
            return;
        }
        self.plan_finale();
        // Survivors keeps the music-driven zones, but retires the reactive set pieces.
        if self.env.input.level.survivors.is_some() {
            return;
        }
        self.generate_candidates();
        self.decide_candidates();
        self.plan_rings();
        self.plan_kick_lines();
    }

    // -------------------------------------------------------------- detected sections

    /// Section starts in `knownAt` order (changes before jumps at equal times), each kept if the
    /// beat is clear at it and the previous start was known at least `minGapS` earlier.
    fn detect_sections(&mut self) {
        let h = self.horizon();
        let log = Rc::clone(&self.log);
        let (Some(changes), Some(jumps)) = (log.changes.as_ref(), log.jumps.as_ref()) else {
            return;
        };
        let rule = self.director().section_rule;
        let gate = self.env.input.gate;
        loop {
            // The next event: a change or a loud enough jump, whichever is known first.
            let mut j = self.st.plan.next_jump;
            while j < jumps.len() && jumps[j].db.abs() < rule.jump_db - 1e-9 {
                j += 1;
            }
            self.st.plan.next_jump = j;
            let c = self.st.plan.next_change;
            let change = changes.get(c).filter(|x| x.known_at <= h);
            let jump = jumps.get(j).filter(|x| x.known_at <= h);
            let (known_at, is_change) = match (change, jump) {
                (Some(a), Some(b)) => {
                    if a.known_at <= b.known_at {
                        (a.known_at, true)
                    } else {
                        (b.known_at, false)
                    }
                }
                (Some(a), None) => {
                    // (A jump known at the same time, not taken in yet, would come after it anyway.)
                    (a.known_at, true)
                }
                (None, Some(b)) => (b.known_at, false),
                (None, None) => return,
            };
            let accepted = if self.evidence_known_at(known_at) < gate
                || self
                    .st
                    .plan
                    .last_section_known_at
                    .is_some_and(|p| known_at - p < rule.min_gap_s - 1e-9)
            {
                Some(false)
            } else {
                match self.effective_downbeat(if self.env.input.level.survivors.is_some() {
                    known_at.max(self.st.s)
                } else {
                    known_at
                }) {
                    Known::Yes(beat) => {
                        let s = self.log.beats[beat].t;
                        // Music can change several times while a menu holds the world.
                        // Present its latest section once, on a future downbeat after resume.
                        if self.env.input.level.survivors.is_some()
                            && known_at < self.st.s - 1e-9
                            && self.st.plan.sections.len() > self.st.plan.zone_section
                            && self.st.plan.sections.last().is_some_and(|p| p.s == s)
                        {
                            self.st.plan.sections.pop();
                        }
                        self.st
                            .plan
                            .sections
                            .push(DetectedSection { s, beat, known_at });
                        self.st.plan.last_section_known_at = Some(known_at);
                        Some(true)
                    }
                    Known::NotYet => None,
                }
            };
            if accepted.is_none() {
                return;
            }
            if is_change {
                self.st.plan.next_change += 1;
            } else {
                self.st.plan.next_jump += 1;
            }
        }
    }

    // -------------------------------------------------------------- zone waves

    /// The first committed beat at or after t (`firstBeatAtOrAfter`).
    fn first_beat_at_or_after(&self, t: f64) -> Known<f64> {
        match self.beat_at_or_before(t - 2e-6) {
            Known::Yes(i) => {
                let i = (i + 1) as usize;
                if i < self.st.ingested.beats {
                    Known::Yes(self.log.beats[i].t)
                } else {
                    Known::NotYet
                }
            }
            Known::NotYet => Known::NotYet,
        }
    }

    /// The zone waves (`zoneWavePlan`): one on each detected section start, and one on the first
    /// beat `zoneFallbackS` after the last wave unless a start is known by then.
    pub(super) fn plan_zone_waves(&mut self) {
        let fallback_s = self.director().zone_fallback_s;
        loop {
            let last = self.st.plan.zone_last;
            let deadline = if self.env.input.level.survivors.is_some() {
                (last + fallback_s).max(self.st.s)
            } else {
                last + fallback_s
            };
            let fallback = self.first_beat_at_or_after(deadline);
            let next = self
                .st
                .plan
                .sections
                .get(self.st.plan.zone_section)
                .copied();
            if let Some(next) = next {
                let section_first = match fallback {
                    Known::Yes(f) => next.s <= f + 1e-9 || next.known_at <= f + 1e-9,
                    // (The fallback beat is at least last + fallbackS: a start known before that is first.)
                    Known::NotYet => next.known_at <= last + fallback_s - 1e-6,
                };
                if section_first {
                    let k = self.st.plan.zone_waves;
                    self.st.plan.zone_waves += 1;
                    self.st.plan.zone_section += 1;
                    self.st.plan.zone_last = next.s;
                    self.st.plan.zone_check = None;
                    let section = self.st.plan.zone_section;
                    let zone = self.zone_for(k);
                    self.st.q.push_init(
                        next.s,
                        Ev::ZoneWave {
                            zone,
                            section: Some(section),
                        },
                        phase::ZONE,
                        k,
                    );
                    self.st.punches.push(super::Punch {
                        s: next.s,
                        amt: SECTION_PUNCH,
                        w: None,
                    });
                    continue;
                }
                if matches!(fallback, Known::NotYet) {
                    return;
                }
            }
            match fallback {
                Known::Yes(f) => {
                    // Decided at the fallback's beat: a start known by then is the wave instead.
                    if self.st.plan.zone_check != Some(f) {
                        self.st.plan.zone_check = Some(f);
                        let k = self.st.plan.zone_waves;
                        self.st
                            .q
                            .push_init(f, Ev::ZoneFallback { at: f }, phase::ZONE, k);
                    }
                    return;
                }
                Known::NotYet => return,
            }
        }
    }

    /// The zone of the k-th zone wave: the level's zones in order, from the second.
    fn zone_for(&self, k: u64) -> usize {
        let n = self.env.input.level.zones.len() as u64;
        ((k + 1) % n) as usize
    }

    /// A fallback zone wave's beat has come: unless a section start was known by then, it is the wave.
    pub(super) fn zone_fallback(&mut self, at: f64) {
        if self.st.plan.zone_check != Some(at) {
            return;
        }
        self.plan_zone_waves();
        if self.st.plan.zone_check != Some(at) {
            return;
        }
        let k = self.st.plan.zone_waves;
        self.st.plan.zone_waves += 1;
        self.st.plan.zone_last = at;
        self.st.plan.zone_check = None;
        let zone = self.zone_for(k);
        self.zone_wave(zone, None);
        self.plan_zone_waves();
    }

    // -------------------------------------------------------------- the finale

    /// The finale, on a confirmed end (docs/design/native-app.md, "A confirmed end"; TS's
    /// `reactiveFinale` read the song's length instead): an `ends` entry counts once the quiet it
    /// marks has held for CONFIRM_S from its start with the evidence below the gate throughout,
    /// after at least MIN_JUDGED_S of judged time. A breakdown that resumes within the window
    /// cancels it; the next entry is tried.
    fn plan_finale(&mut self) {
        if self.st.plan.finale.is_some() {
            return;
        }
        let Some(f) = self
            .director()
            .set_pieces
            .as_ref()
            .map(|sp| sp.finale.clone())
        else {
            return;
        };
        let s = if let Some(run) = self.env.input.level.survivors {
            // Read only committed beats already ingested at their knownAt. The deadline is
            // expressed through the run clock so a later freeze can hold it independently.
            let due = self.st.s + (run.duration_s - self.st.run_s).max(0.0);
            let Some(beat) = self.log.beats[..self.st.ingested.beats]
                .iter()
                .enumerate()
                .find(|(i, b)| i % 2 == 0 && b.t >= due - 1e-9 && b.t >= self.st.s - 1e-9)
                .map(|(_, b)| b)
            else {
                return;
            };
            beat.t
        } else {
            let h = self.horizon();
            let gate = self.env.input.gate;
            let log = Rc::clone(&self.log);
            let ends = log.ends.as_deref().unwrap_or(&[]);
            let mut confirmed = None;
            let ticks = &log.ticks[..self.st.ingested.ticks];
            'entries: while let Some(e) = ends.get(self.st.plan.ends_seen) {
                if e.known_at > h {
                    return;
                }
                // The quiet starts at the first tick below the gate at or after the entry (the evidence
                // trails off for a second or so after the analyzer's end): within QUIET_WITHIN of it,
                // and again after any loud tick that breaks it.
                let mut from = e.t - 1e-9;
                loop {
                    let Some(q) = ticks.iter().find(|k| k.t >= from && k.evidence < gate) else {
                        if ticks.last().is_some_and(|k| k.t > e.t + QUIET_WITHIN) {
                            self.st.plan.ends_seen += 1;
                            continue 'entries;
                        }
                        return;
                    };
                    if q.t > e.t + QUIET_WITHIN {
                        self.st.plan.ends_seen += 1;
                        continue 'entries;
                    }
                    let at = e.known_at.max(q.t + CONFIRM_S);
                    let loud = ticks
                        .iter()
                        .find(|k| k.t > q.t && k.t <= at + 1e-9 && k.evidence >= gate);
                    if let Some(l) = loud {
                        from = l.t + 1e-9;
                        continue;
                    }
                    if h < at {
                        return;
                    }
                    self.st.plan.ends_seen += 1;
                    if self.judged_clock(at) >= MIN_JUDGED_S {
                        confirmed = Some(at);
                        break 'entries;
                    }
                    continue 'entries;
                }
            }
            let Some(s) = confirmed.map(|at| at.max(self.st.s)) else {
                return;
            };
            s
        };
        self.st.plan.finale = Some(FinalePlan {
            s,
            blast: f.blast.clone(),
        });
        self.revoke_from(s);
        // The run ends when the finale's epilogue has played out.
        self.st.end_s = min(self.st.until_s, s + f.epilogue_s);
        // Its blast's bands, from the finale on.
        let lib = Rc::clone(&self.env);
        let wd = &lib.input.lib.weapons[f.blast.as_str()];
        let b = wd.blast.as_ref().expect("the finale's weapon is a blast");
        let step_b = super::combat::subdivision_beats(b.step, 2.0);
        let bands = max(1.0, round(b.beats / step_b));
        let bi0 = self.sbi_at(s);
        let mut k = 0.0;
        while k < bands {
            self.push_init_at_beat(
                bi0 + k * step_b,
                Ev::Blast { band: k as usize },
                phase::BLAST,
                k as u64,
            );
            k += 1.0;
        }
        if let Some(id) = &f.landmark {
            let i = self.st.plan.landmarks;
            self.st.plan.landmarks += 1;
            self.st
                .q
                .push_init(s, Ev::Landmark { id: id.clone() }, phase::LANDMARK, i);
        }
        // The base rate with the finale's swell: it falls to `swell` and back over `swellS`.
        if self.env.input.level.survivors.is_none() {
            self.st.swell_steps = Some(with_swell(&self.st.sch.base_steps, s, f.swell, f.swell_s));
        }
    }

    /// Set pieces accepted before the finale was known that would start at or after it: TS never
    /// started them (nothing starts after the finale).
    fn revoke_from(&mut self, until: f64) {
        let plan = &mut self.st.plan;
        plan.cands.retain(|c| c.s < until);
        let mut gone = Vec::new();
        if plan
            .boss
            .as_ref()
            .is_some_and(|b| b.s_enter - BOSS_DROP_S >= until - 1e-9)
        {
            plan.boss = None;
            gone.push(Gone::Boss);
        }
        for (f, x) in plan.formations.iter().enumerate() {
            if x.s_start >= until {
                gone.push(Gone::Formation(f));
            }
        }
        plan.formations.retain(|x| x.s_start < until);
        for (k, x) in plan.rings.iter().enumerate() {
            if x.s >= until {
                gone.push(Gone::Ring(k));
            }
        }
        plan.rings.retain(|x| x.s < until);
        for (k, x) in plan.stampedes.iter().enumerate() {
            if x.s >= until {
                gone.push(Gone::Stampede(k));
            }
        }
        plan.stampedes.retain(|x| x.s < until);
        for (g, x) in plan.archer_groups.iter().enumerate() {
            if x.s >= until {
                gone.push(Gone::Archers(g));
            }
        }
        plan.archer_groups.retain(|x| x.s < until);
        if gone.is_empty() {
            return;
        }
        self.st.q.remove(&|ev| {
            gone.iter().any(|g| match (g, ev) {
                (
                    Gone::Boss,
                    Ev::BossSpawn | Ev::BossLand | Ev::BossRetreat | Ev::SlamCheck { .. },
                ) => true,
                (Gone::Formation(f), Ev::Formation { f: x } | Ev::FormationRelease { f: x }) => {
                    f == x
                }
                (Gone::Ring(k), Ev::Ring { k: x }) | (Gone::Stampede(k), Ev::Stampede { k: x }) => {
                    k == x
                }
                (Gone::Archers(g), Ev::Archers { group } | Ev::DrawCheck { group, .. }) => {
                    g == group
                }
                _ => false,
            })
        });
        self.st.deferred.retain(|d| {
            !gone.iter().any(|g| match (g, &d.then) {
                (Gone::Formation(f), super::Then::Push(Ev::FormationRelease { f: x })) => f == x,
                (Gone::Archers(g), super::Then::Push(Ev::DrawCheck { group, .. })) => g == group,
                (Gone::Boss, super::Then::Push(Ev::SlamCheck { .. })) => true,
                _ => false,
            })
        });
    }

    // -------------------------------------------------------------- candidates

    fn add_candidate(&mut self, c: Candidate) {
        if c.s >= self.until() {
            return;
        }
        let cands = &mut self.st.plan.cands;
        let at = cands
            .iter()
            .position(|x| c.before(x))
            .unwrap_or(cands.len());
        cands.insert(at, c);
    }

    /// Candidates from the triggers taken in, in TS's order within each kind of trigger.
    fn generate_candidates(&mut self) {
        let Some(sp) = self.director().set_pieces.clone() else {
            return;
        };
        let h = self.horizon();
        let gate = self.env.input.gate;
        // Section starts: the boss (once), a ring on a loud one, a bee wedge on any.
        while let Some(st) = self
            .st
            .plan
            .sections
            .get(self.st.plan.sections_seen)
            .copied()
        {
            let loud = self.level_at(st.known_at) == Level::Loud;
            let i = self.st.plan.sections_seen;
            let mut boss = None;
            if loud && st.known_at >= sp.boss.after_s - 1e-9 {
                match self.downbeat_after(st.known_at, sp.boss.land_after_s - DOWNBEAT_NOTICE) {
                    Known::Yes(s_enter) => boss = Some(s_enter),
                    Known::NotYet => return,
                }
            }
            self.st.plan.sections_seen += 1;
            if let Some(s_enter) = boss {
                self.add_candidate(Candidate {
                    kind: Kind::Boss,
                    s: st.s,
                    retry: false,
                    group: 0,
                    index: i,
                    sub: 0,
                    s_enter,
                    section: i,
                });
            }
            if loud {
                self.add_candidate(Candidate {
                    kind: Kind::Ring,
                    s: st.s,
                    retry: false,
                    group: 0,
                    index: i,
                    sub: 1,
                    s_enter: 0.0,
                    section: i,
                });
            }
            self.add_candidate(Candidate {
                kind: Kind::Wedge,
                s: st.s,
                retry: false,
                group: 0,
                index: i,
                sub: 2,
                s_enter: 0.0,
                section: i,
            });
        }
        // Stampedes: a rising jump after at least quietS that were not loud, known with the beat clear.
        let log = Rc::clone(&self.log);
        if let Some(jumps) = log.jumps.as_ref() {
            while let Some(j) = jumps
                .get(self.st.plan.jumps_seen)
                .filter(|j| j.known_at <= h)
            {
                let i = self.st.plan.jumps_seen;
                let st = &sp.stampede;
                if j.db < st.jump_db - 1e-9
                    || j.t < st.quiet_s
                    || self.evidence_known_at(j.known_at) < gate
                {
                    self.st.plan.jumps_seen += 1;
                    continue;
                }
                let ticks = &log.ticks[..self.st.ingested.ticks];
                let (mut n, mut loud) = (0.0, 0.0);
                let mut k = tick_index_in(ticks, j.t);
                while k >= 0 && ticks[k as usize].t > j.t - st.quiet_s - 1e-9 {
                    if ticks[k as usize].level == Some(Level::Loud) {
                        loud += 1.0;
                    }
                    n += 1.0;
                    k -= 1;
                }
                if loud > QUIET_BLIP * n {
                    self.st.plan.jumps_seen += 1;
                    continue;
                }
                match self.downbeat_after(j.known_at, 0.0) {
                    Known::Yes(s) => self.add_candidate(Candidate {
                        kind: Kind::Stampede,
                        s,
                        retry: false,
                        group: 1,
                        index: i,
                        sub: 0,
                        s_enter: 0.0,
                        section: 0,
                    }),
                    Known::NotYet => return,
                }
                self.st.plan.jumps_seen += 1;
            }
        }
        // The kick line: after mediumBeats committed beats in a row at medium level, the last with the beat clear.
        while self.st.plan.kick_beat < self.st.ingested.beats {
            let i = self.st.plan.kick_beat;
            let b = log.beats[i];
            if b.t > h {
                break;
            }
            let run = if self.level_at(b.t) == Level::Medium {
                self.st.plan.kick_run + 1.0
            } else {
                0.0
            };
            if run >= sp.kick_line.medium_beats && self.evidence_known_at(b.t) >= gate {
                match self.downbeat_after(b.t, 0.0) {
                    Known::Yes(s) => self.add_candidate(Candidate {
                        kind: Kind::KickLine,
                        s,
                        retry: false,
                        group: 2,
                        index: i,
                        sub: 0,
                        s_enter: 0.0,
                        section: 0,
                    }),
                    Known::NotYet => return,
                }
                self.st.plan.kick_run = 0.0;
            } else {
                self.st.plan.kick_run = run;
            }
            self.st.plan.kick_beat += 1;
        }
        // Volleys: from the effective downbeat after the level turns loud with the beat clear.
        while self.st.plan.volley_tick < self.st.ingested.ticks {
            let i = self.st.plan.volley_tick;
            let k = &log.ticks[i];
            let now = k.level == Some(Level::Loud) && k.evidence >= gate;
            if now
                && !self.st.plan.volley_on
                && k.t - self.st.plan.volley_off_since >= VOLLEY_REARM_S - 1e-9
            {
                match self.downbeat_after(k.t, 0.0) {
                    Known::Yes(s) => self.add_candidate(Candidate {
                        kind: Kind::Volleys,
                        s,
                        retry: false,
                        group: 3,
                        index: i,
                        sub: 0,
                        s_enter: 0.0,
                        section: 0,
                    }),
                    Known::NotYet => return,
                }
            }
            if !now && self.st.plan.volley_on {
                self.st.plan.volley_off_since = k.t;
            }
            self.st.plan.volley_on = now;
            self.st.plan.volley_tick += 1;
        }
    }

    /// The earliest any candidate still to come could start: the effective downbeat after the
    /// horizon (later than every known beat when that beat is not known yet).
    fn earliest_new_start(&self) -> f64 {
        match self.downbeat_after(self.horizon(), 0.0) {
            Known::Yes(s) => s,
            Known::NotYet => f64::INFINITY,
        }
    }

    /// Decides candidates in order (`reactiveSetPieces`' spacing and priority), as far as no
    /// candidate still to come could sort before the next.
    fn decide_candidates(&mut self) {
        let Some(sp) = self.director().set_pieces.clone() else {
            return;
        };
        let gate = self.env.input.gate;
        loop {
            // Pending volley retries' downbeats, once known (in the order they were turned away).
            while let Some(&from) = self.st.plan.retry_from.first() {
                match self.downbeat_after(from, 0.0) {
                    Known::Yes(s) => {
                        self.st.plan.retry_from.remove(0);
                        let index = self.st.plan.retries;
                        self.st.plan.retries += 1;
                        self.add_candidate(Candidate {
                            kind: Kind::Volleys,
                            s,
                            retry: true,
                            group: 4,
                            index,
                            sub: 0,
                            s_enter: 0.0,
                            section: 0,
                        });
                    }
                    Known::NotYet => break,
                }
            }
            let Some(c) = self.st.plan.cands.first().cloned() else {
                return;
            };
            let bound = self.earliest_new_start();
            let decidable = c.s < bound || (c.kind == Kind::Boss && c.s <= bound);
            if !decidable || self.st.plan.retry_from.iter().any(|&f| c.s >= f) {
                return;
            }
            // (A retry starts only while the level is loud with the beat clear at its downbeat.)
            if c.retry {
                if self.horizon() < c.s {
                    return;
                }
                let ticks = &self.log.ticks[..self.st.ingested.ticks];
                let k = tick_index_in(ticks, c.s);
                let ok = c.s < self.until()
                    && k >= 0
                    && ticks[k as usize].level == Some(Level::Loud)
                    && ticks[k as usize].evidence >= gate;
                self.st.plan.cands.remove(0);
                if !ok {
                    continue;
                }
            } else {
                self.st.plan.cands.remove(0);
            }
            let spaced = if c.kind == Kind::Volleys {
                self.st.plan.last
            } else {
                self.st.plan.last_other
            };
            if c.s < spaced + sp.gap_s - 1e-9 {
                if c.kind == Kind::Volleys {
                    // Turned away by the spacing, volleys try again on the first downbeat it allows.
                    self.st
                        .plan
                        .retry_from
                        .push(self.st.plan.last + sp.gap_s - DOWNBEAT_NOTICE);
                }
                continue;
            }
            if !self.accept(&c) {
                continue;
            }
            self.st.plan.last = c.s;
            if c.kind != Kind::Volleys {
                self.st.plan.last_other = c.s;
            }
        }
    }

    /// Fills in an accepted set piece and queues its events; false if it adds nothing.
    fn accept(&mut self, c: &Candidate) -> bool {
        let env = Rc::clone(&self.env);
        let sp = env.director.set_pieces.as_ref().expect("set pieces");
        let lib = &env.input.lib;
        match c.kind {
            Kind::Boss => {
                if self.st.plan.boss_taken || c.s_enter - BOSS_DROP_S >= self.until() - 1e-9 {
                    return false;
                }
                self.st.plan.boss_taken = true;
                let b = &sp.boss;
                let s_retreat = c.s_enter + b.retreat_s;
                self.st.plan.boss = Some(BossPlan {
                    kind: b.kind.clone(),
                    s_enter: c.s_enter,
                    slams: Vec::new(),
                    s_retreat,
                    checks: 0,
                });
                let rate = max(0.05, step_rate(&self.st.sch.base_steps, c.s_enter));
                self.st.q.push_init(
                    max(0.0, c.s_enter - BOSS_DROP_S / rate),
                    Ev::BossSpawn,
                    phase::BOSS,
                    0,
                );
                self.st.q.push_init(c.s_enter, Ev::BossLand, phase::BOSS, 1);
                if let Some(id) = &b.landmark {
                    let i = self.st.plan.landmarks;
                    self.st.plan.landmarks += 1;
                    self.st.q.push_init(
                        c.s_enter,
                        Ev::Landmark { id: id.clone() },
                        phase::LANDMARK,
                        i,
                    );
                }
                self.st
                    .q
                    .push_init(s_retreat, Ev::BossRetreat, phase::RETREAT, 0);
                let i0 = self.beat_at_or_after(c.s_enter);
                self.queue_slam_check(i0 + b.slam_beats as usize);
                true
            }
            Kind::Ring => {
                let st = self.st.plan.sections[c.section];
                let r = &sp.ring;
                let k = self.st.plan.rings.len();
                let fallback_beat = st.beat + r.max_beats as usize;
                self.st.plan.rings.push(RingPlan {
                    s: st.s,
                    enemy: r.enemy.clone(),
                    count: r.count,
                    gaps: r.gaps,
                    k_close: None,
                    s_close: None,
                    close_seq: None,
                    entered: false,
                    fallback_beat,
                    after: r.after_s,
                });
                self.st
                    .q
                    .push_init(st.s, Ev::Ring { k }, phase::RINGS, k as u64);
                true
            }
            Kind::Wedge => {
                let def = &lib.formations[sp.wedge.as_str()];
                let release_beats = match def.release {
                    Release::AfterBeats { beats, .. } => beats,
                    Release::FinalAccentOfPhrase { .. } => 0.0,
                };
                let i = self.beat_at_or_after(c.s);
                let j = i + (def.entrance_beats + release_beats) as usize;
                let f = self.st.plan.formations.len();
                self.st.plan.formations.push(FormationPlan {
                    def: sp.wedge.clone(),
                    s_start: c.s,
                    s_release: None,
                    kick: None,
                });
                self.st
                    .q
                    .push_init(c.s, Ev::Formation { f }, phase::FORMATIONS, 2 * f as u64);
                self.push_init_at_beat(
                    j as f64,
                    Ev::FormationRelease { f },
                    phase::FORMATIONS,
                    2 * f as u64 + 1,
                );
                true
            }
            Kind::Stampede => {
                let st = &sp.stampede;
                let k = self.st.plan.stampedes.len();
                self.st.plan.stampedes.push(StampedePlan {
                    s: c.s,
                    enemy: st.enemy.clone(),
                    count: st.count,
                    rows: st.rows,
                });
                self.st
                    .q
                    .push_init(c.s, Ev::Stampede { k }, phase::STAMPEDES, k as u64);
                true
            }
            Kind::KickLine => {
                if self.st.plan.kick_taken {
                    return false;
                }
                self.st.plan.kick_taken = true;
                let kl = &sp.kick_line;
                let def = &lib.formations[kl.formation.as_str()];
                debug_assert_eq!(def.shape, FormationShape::KickLine);
                let i = self.beat_at_or_after(c.s);
                let from_beat = i + (def.entrance_beats + kl.min_dance_beats) as usize;
                let fallback_beat = i + (def.entrance_beats + kl.max_dance_beats) as usize;
                let f = self.st.plan.formations.len();
                self.st.plan.formations.push(FormationPlan {
                    def: kl.formation.clone(),
                    s_start: c.s,
                    s_release: None,
                    kick: Some(KickPlan {
                        from_beat,
                        fallback_beat,
                    }),
                });
                self.st
                    .q
                    .push_init(c.s, Ev::Formation { f }, phase::FORMATIONS, 2 * f as u64);
                true
            }
            Kind::Volleys => {
                let v = &sp.volleys;
                let group = self.st.plan.archer_groups.len();
                self.st.plan.archer_groups.push(ArcherGroup {
                    s: c.s,
                    archers: v.archers,
                });
                self.st
                    .q
                    .push_init(c.s, Ev::Archers { group }, phase::ARCHERS, group as u64);
                let i0 = self.beat_at_or_after(c.s);
                self.queue_draw_check(group, i0 + v.every_beats as usize);
                true
            }
        }
    }

    // -------------------------------------------------------------- slams and volleys

    /// The next boss slam's mark, a beat before its landing beat i: checked at the mark.
    fn queue_slam_check(&mut self, i: usize) {
        let Some(b) = self.st.plan.boss.as_mut() else {
            return;
        };
        let n = b.checks;
        b.checks += 1;
        // (It reads the landing beat i.)
        self.push_init_at_beat_needing(
            (i - 1) as f64,
            i,
            Ev::SlamCheck { i },
            phase::BOSS,
            2 + 2 * n,
        );
    }

    /// A slam's mark has come: it slams if the level is loud there, unless the finale has come or
    /// its landing would be after the retreat (then no more slams).
    pub(super) fn slam_check(&mut self, i: usize) {
        let Some(b) = self.st.plan.boss.clone() else {
            return;
        };
        let beats = &self.log.beats[..self.st.ingested.beats];
        let s_land = beats[i].t;
        let s_mark = beats[i - 1].t;
        if s_mark >= self.until() || s_land >= b.s_retreat {
            return;
        }
        // (Live: a landing beat that arrived too late to mark for is skipped.)
        if self.level_at(s_mark) == Level::Loud && self.st.s < s_land - 1e-6 {
            let k = b.slams.len();
            self.st
                .plan
                .boss
                .as_mut()
                .expect("a boss")
                .slams
                .push(Slam { s_land });
            self.st
                .q
                .push_init(s_land, Ev::SlamLand { k }, phase::BOSS, 3 + 2 * k as u64);
            self.slam_mark(k);
        }
        let step = self
            .director()
            .set_pieces
            .as_ref()
            .expect("set pieces")
            .boss
            .slam_beats as usize;
        self.queue_slam_check(i + step);
    }

    /// The group's next volley, released on beat i: decided at its draw, a beat before.
    fn queue_draw_check(&mut self, group: usize, i: usize) {
        if i == 0 {
            return;
        }
        let v = self.st.plan.volleys.len() as u64;
        // (It reads the release and arrival beats i and i + 1.)
        self.push_init_at_beat_needing(
            (i - 1) as f64,
            i + 1,
            Ev::DrawCheck { group, i },
            phase::VOLLEYS,
            3 * v,
        );
    }

    /// A volley's draw has come: it flies if the level is loud with the beat clear there (and the
    /// finale has not come); otherwise the group's volleys end.
    pub(super) fn draw_check(&mut self, group: usize, i: usize) {
        let gate = self.env.input.gate;
        let beats = &self.log.beats[..self.st.ingested.beats];
        let s_draw = beats[i - 1].t;
        // (Live: a volley whose release beat arrived too late to draw for fails too.)
        let late = self.st.s >= beats[i].t - 1e-6;
        if late
            || s_draw >= self.until()
            || self.level_at(s_draw) != Level::Loud
            || self.evidence_known_at(s_draw) < gate
        {
            return;
        }
        let archers = self
            .director()
            .set_pieces
            .as_ref()
            .expect("set pieces")
            .volleys
            .archers;
        let every = self
            .director()
            .set_pieces
            .as_ref()
            .expect("set pieces")
            .volleys
            .every_beats as usize;
        let v = self.st.plan.volleys.len();
        let (s_release, s_arrive) = (beats[i].t, beats[i + 1].t);
        self.st.plan.volleys.push(VolleyPlan {
            s_draw: self.st.s,
            s_release,
            s_arrive,
            archers,
            group,
        });
        self.st.q.push_init(
            s_release,
            Ev::Release { v },
            phase::VOLLEYS,
            3 * v as u64 + 1,
        );
        self.st
            .q
            .push_init(s_arrive, Ev::Arrive { v }, phase::VOLLEYS, 3 * v as u64 + 2);
        self.draw(v);
        self.queue_draw_check(group, i + every);
    }

    // -------------------------------------------------------------- rings and the kick line

    /// Each ring's close (`ring`): the `knownAt` of the first forecast (confident) or strong onset
    /// at least `afterS` after it starts, known after it starts, or its last beat if that comes
    /// first; it ends on the next downbeat known by then (`closeEnd`).
    fn plan_rings(&mut self) {
        let h = self.horizon();
        let log = Rc::clone(&self.log);
        for k in 0..self.st.plan.rings.len() {
            let r = self.st.plan.rings[k].clone();
            if r.k_close.is_some() || r.fallback_beat >= self.st.ingested.beats {
                continue;
            }
            let mut k_close = log.beats[r.fallback_beat].t;
            let qualifies =
                |t: f64, known_at: f64| t >= r.s + r.after - 1e-9 && known_at >= r.s - 1e-9;
            if let Some(fc) = log.forecasts.as_ref() {
                for f in fc.iter().take_while(|f| f.known_at <= h) {
                    if f.confidence >= 0.6 - 1e-9 && qualifies(f.t, f.known_at) {
                        k_close = min(k_close, f.known_at);
                    }
                }
            }
            for o in log.onsets.iter().take_while(|o| o.known_at <= h) {
                if o.strong && qualifies(o.t, o.known_at) {
                    k_close = min(k_close, o.known_at);
                }
            }
            // Certain once every accent known by then is in.
            if h < k_close {
                continue;
            }
            let s_close = self.close_end(k_close);
            let plan = &mut self.st.plan.rings[k];
            plan.k_close = Some(k_close);
            plan.s_close = Some(s_close);
            if let Some(seq) = plan.close_seq {
                self.st.q.push_reserved(k_close, Ev::RingClose { k }, seq);
            }
        }
    }

    /// Where a close starting at k ends (`closeEnd`): the first downbeat at least DOWNBEAT_NOTICE
    /// after k among the beats committed by k, or a bar of the latest committed period after it.
    fn close_end(&self, k: f64) -> f64 {
        let beats = &self.log.beats[..self.st.ingested.beats];
        let mut last: isize = -1;
        for (i, b) in beats.iter().enumerate() {
            if b.known_at > k + 1e-9 {
                break;
            }
            last = i as isize;
            if i % 2 == 0 && b.t >= k + DOWNBEAT_NOTICE - 1e-9 {
                return b.t;
            }
        }
        let period = if last >= 0 {
            beats[last as usize].period
        } else {
            0.5
        };
        k + DOWNBEAT_NOTICE + 2.0 * period
    }

    /// The kick line's release (`kickLine`): the `knownAt` of the first strong onset at or after its
    /// minimum dance, or its maximum dance's last beat if that comes first.
    fn plan_kick_lines(&mut self) {
        let h = self.horizon();
        let log = Rc::clone(&self.log);
        for f in 0..self.st.plan.formations.len() {
            let fp = self.st.plan.formations[f].clone();
            let Some(kick) = fp.kick else { continue };
            if fp.s_release.is_some()
                || kick.fallback_beat >= self.st.ingested.beats
                || kick.from_beat >= self.st.ingested.beats
            {
                continue;
            }
            let from = log.beats[kick.from_beat].t;
            let fallback = log.beats[kick.fallback_beat].t;
            let onset = log
                .onsets
                .iter()
                .take_while(|o| o.known_at <= h)
                .find(|o| o.strong && o.t >= from - 1e-9);
            let s_release = match onset {
                Some(o) => min(fallback, o.known_at),
                None if h >= fallback => fallback,
                None => continue,
            };
            self.st.plan.formations[f].s_release = Some(s_release);
            self.st.q.push_init(
                s_release,
                Ev::FormationRelease { f },
                phase::FORMATIONS,
                2 * f as u64 + 1,
            );
            // Gems on the field: a vacuum on the first downbeat after the kick line's release.
            self.push_init_at_downbeat(
                s_release + 1e-6,
                Ev::VacuumDrop,
                phase::KICK_VACUUM,
                f as u64,
            );
        }
    }
}

/// What a revocation removes.
enum Gone {
    Boss,
    Formation(usize),
    Ring(usize),
    Stampede(usize),
    Archers(usize),
}

/// The finale's swell: the rate falls to `rate` and back over `dur` from s, as short steps over the
/// base rate (`withSwell`).
fn with_swell(steps: &[RateStep], s: f64, rate: f64, dur: f64) -> Vec<RateStep> {
    let n = 16.0;
    let base = |t: f64| step_rate(steps, t);
    let mut out: Vec<RateStep> = steps.iter().copied().filter(|x| x.s < s - 1e-9).collect();
    let mut k = 0.0;
    while k < n {
        let a = s + (dur * k) / n;
        let u = (k + 0.5) / n;
        out.push(RateStep {
            s: a,
            rate: round(
                base(a) * (1.0 - (1.0 - rate) * crate::jsmath::sin(crate::jsmath::PI * u)) * 1e6,
            ) / 1e6,
        });
        k += 1.0;
    }
    let after = s + dur;
    out.push(RateStep {
        s: after,
        rate: base(after),
    });
    out.extend(steps.iter().copied().filter(|x| x.s > after + 1e-9));
    out
}
