# FR8 — Early signature and musical tour

A single minotaur stages around90 run-seconds: a reliable-music warning, a
short known-main-beat descent, standing-stones arena, targeted in-place slam
markers, main-beat slams and the existing boss death/reward feedback. It retains
catalog175HP and the existing Survivors art-role mapping(speed160, power20,
XP50); existing contact, slam, food and damage-lockout formulas remain. No new
chest, projectile encounter or repeatable reward was added. Later scheduled
bosses/chests remain. The signature shares300 actors, reclaims an ordinary slot,
keeps one identity, and retreats after90 run-seconds with bounded retirement.

Missing confidence holds the descent un-hittable and replans a future main
beat; pauses cancel marked slams and preserve run life. There is no fallback
travel/slam or catch-up volley. Descent phase is held across a pause and replanned without restarting the drop;
an actual Engine view-phase regression covers that continuity.
The live projection retains bounded landmark,
slam, arrival and health identities across rollback; actual standing-stones.tmj
supplies10 stones/4 braziers and its330px clear area. Marked rings show the
actual28px slam radius. Effects use held world/presentation time.

Entrance notice is min350ms,75% of the known beat period. The prior350ms-only
rule exceeded the344.8ms one-beat horizon at174BPM, silently starving fast-song
formations. Both signature and FR7 entrances now admit real known fast beats;
no inferred main beats were added. Existing long warning text fades after6s
if an entrance must keep waiting, and begins only on reliable music.

Fallback zones tour every60s for the opening five waves, then240s. The last
wave's run timestamp chooses the interval, preserving the fifth wave across
300s and menus. Victory still forces infernal. Known section/track transitions
add a small400ms world ring; they do not end the run, grant rewards or warp time.

The private zone-authoring diagnostic now captures its actual opening-minute
tour at2/62/122/182/242s, explicitly suppressing unrelated combat. zones/ keeps
all five labelled stills and raw clocks; a61-frame silent24fps sweep stays in
the main checkout captures/feel-fr8/. The later240s cadence and forced victory
are exercised by the actual sim clock tests, including beatless and paused runs.

## Kept checks and limits

signature-tests.txt: actual catalog minotaur at70/128/174, first land90s and
slams96.857/93.75/92.759; full300 field, real source warning plus conflicting
landing, snapshot/pause during mark/descent, confidence loss with continuing
sword attacks, normal rank-one sword damage/death and actual XP feedback.
The long-life/tempo fixtures explicitly disable hero damage and sword damage
except the actual damage/death test. No synthetic lethal hit/HP retune.

rollback.txt: actual Rust three-minute timestamp fixture,150s through Engine,
30ms late keys and237ms lead, identical settled recording(hash13884521ca78de8b), one standing-stones
landmark and actual slam records. Musical tests cover production174BPM
entrances; complete focused sim/eval/app tests and clippy are retained.

stills/: House95s in zen/legacy is honestly a waiting-for-beat state,
not proof of a landed boss. HangLow98s uses its public timestamped music stream,
normal first-card autopicks and explicitly damage-exempt control; it visibly
shows a damaged minotaur and the actual stone/brazier arena. Stills are silent,
not listening auditions or survival claims. FR9 addresses the bright dense-hit
hierarchy; FR10 keeps integrated comparisons, performance and actual sound.
Subjective timing/feel and speaker/System checks remain operator-pending.

Cold review found two P2 issues, both fixed: the signature warning now resets
the previous source boss's completed camera envelope; every actual boss landing
has a separate timestamp, so pending aggregate warnings cannot hide a same-beat
source landing. Regressions cover the camera and real source notice path.
No confirmation review. Source stats initialization was inspected and a proposed
missing-stats concern was retracted (SimInput maps all Survivors roles).

Playable from native/:
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`
(add `--legacy` for judged Survivors).
