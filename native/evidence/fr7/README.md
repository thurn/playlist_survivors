# FR7 — Musical pressure and formations

Quiet/loud source-roster refills use the existing causal smoothed loudness scale,
bounded0.6..1.3. Quiet approaches retain curling, shorter admitted main-beat
steps. There is one population budget of300: at most40 optional formation
members, one optional shape at a time, at least30 run-seconds between entrances,
and at most48 run-seconds of life. Existing scheduled source encounters win;
mandatory bosses reclaim ordinary/optional slots instead of starving at the cap.

Eligibility follows the early roster tour: stampede120s, kick180s, wedge240s,
ring300s. A fresh6dB drop after8s without loud music chooses a stampede;
16 medium admitted beats choose a kick line; quiet/loud16-beat boundaries
choose wedge/ring. Entrances need a known reliable main beat with350ms notice,
expire after2 run-seconds, and are discarded on a menu or missed confidence
window. There are no invented travel pulses, archers or ranged encounters.

Catalog roles/layouts reuse FoeSlot/RingPath. Stampedes have a3-slot crossing
gap; goblin rings three5-slot gaps at120px inner radius; source flower walls have a60-degree exit.
Kick dancers release on an eligible strong onset or bounded main-beat count;
wedge and ring phases use admitted main beats. Retirement never awards fake XP
or recycles these actors into the regular roster. Existing shapes hold on menus.

Evidence: musical-tests.txt covers all four actual catalog layouts at70/128/174,
240Hz displacement sampling, cap/boss precedence, pause/snapshot identity,
confidence deadline cancellation, real knownAt trigger paths and loudness refill
counts9/20 against the source minimum15. sim-tests.txt is the full sim suite;
clock-only fixtures explicitly suppress musical combat while retaining victory.
The rollback fixture disables damage/XP so unrelated cards cannot truncate its
400s tour; assertions require all four shapes' catalog roles and dance start/end.
rollback.txt compares actual Engine settled recording under identical keys with
30ms delayed delivery and237ms lead across400s of scripted music. This is
synthetic timestamped music and real catalog choreography, not a listening test.
clippy.txt retains focused warnings-as-errors validation.

Cold review found two confirmed defects, both fixed: final goblin gap clearance
was50.73px against56px combined bite reach; it is now80.65px with a16px
minimum-clearance regression. Pausing a released kick line now shifts its
remaining charge deadline; the regression pauses during the actual charge.
Self-review also connected dance start/end to rollbackable live records and
alternating kick/kick-alt poses, so the actual app displays the choreography.
Neither fix changes health, damage, immunity or rewards. No confirmation review.

stills/house-minute3.png is an actual House timestamp-stream run at183 song
seconds,179.8 run seconds, first-card autopicks, damage-exempt. It shows the
source boss warning and current combat; it is not a claim that all four optional
shapes occurred. Full real-music comparisons/dense qualification are FR10.
Subjective feel and speakers/System audition remain operator-pending.

Playable from native/:
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`
(add `--legacy` for judged Survivors).
