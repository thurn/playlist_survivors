using System;

namespace PS.Game.Sim
{
    /// <summary>
    /// The dash (`--dash`, docs/design/game-modes.md, "The dash"): a sparse, optional rhythm verb for a free hero, on
    /// Space. The frequent verbs (the walk, the sword) stay unjudged; this one is judged, binary, and never a dead press.
    ///
    /// - <b>On the beat</b> (heard within <see cref="WindowS"/> of a beat, <see cref="BeatGrade.OnBeat"/>; every press with
    ///   no reliable beat): a long lunge, <see cref="LongM"/> over <see cref="LongS"/>, striking each enemy whose body
    ///   comes within <see cref="ReachM"/> of the hero on the way once, for a heavy strike's damage, and throwing it aside
    ///   of the path (<see cref="Sword.Lunge"/>); and the sword's next beat strike landing after it is a heavy spin
    ///   (<see cref="Sword.SpinAfter"/>): lunge in on the beat, land, spin out on the next.
    /// - <b>Off the beat</b>: a short hop, <see cref="ShortM"/> over <see cref="ShortS"/>, striking nothing.
    /// - Either way the hero can't be hurt from the dash's start to <see cref="AfterS"/> past its end.
    /// - <b>Direction</b>: the held arrows, else toward the pointer's target, else the walk's, else the facing
    ///   (<see cref="FreeHero.DashDirection"/>). The free walk's box rules stop it at a blocked tile or the arena's edge.
    /// - <b>Heard time</b>: as a free press's strike, the dash starts when its key was heard (never before a hold nor
    ///   further back than <see cref="Sword.PressBackS"/>): the hero's path from then is walked again with the dash
    ///   (<see cref="FreeHero.Dash"/>), and the lunge's blows the sim has passed resolve at once, each at its own time
    ///   against the world as it was (<see cref="Horde.Rewound"/>). A hit the hero took in between stands.
    /// - <b>Rate</b>: a dash starts at least the sword's strike period after the one before (a beat; two on the
    ///   half-time pulse, half of one on the beat grid's slow songs: <see cref="StrikePeriod"/>), less twice the window,
    ///   so two presses each on its beat both dash. A press sooner waits for it, judged by when it was heard, the latest
    ///   such press replacing an earlier: mashing never runs faster than the music's pulse, and no press is lost.
    /// - Frozen or fallen, no dash; a press waiting is dropped, as it is when a later press is a finisher's.
    /// </summary>
    internal sealed class Dash
    {
        /// <summary>The lunge on the beat: three tiles (m) over this long (s).</summary>
        public const double LongM = 4.5, LongS = 0.15;
        /// <summary>The hop off the beat: a tile (m) over this long (s).</summary>
        public const double ShortM = 1.5, ShortS = 0.1;
        /// <summary>A press heard this close to a beat (s), either side, lunges: BeatGrade's full window.</summary>
        public const double WindowS = BeatGrade.FullWindowS;
        /// <summary>The hero can't be hurt for this long after a dash ends (s), a beat-short moment to land.</summary>
        public const double AfterS = 0.1;
        /// <summary>The lunge strikes each enemy whose body comes this near the hero's centre on its path (m).</summary>
        public const double ReachM = 1.2;
        /// <summary>The strike period with no beat revealed (s): 120 BPM's.</summary>
        public const double NoBeatPeriodS = 0.5;

        readonly FreeHero hero;
        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly SimState state;
        readonly HeroHealth health;
        readonly Horde horde;
        readonly bool beatGrid;
        int dashes;
        // The earliest the next dash may start.
        double nextFrom = double.NegativeInfinity;
        // A press that came too soon: when it was heard, and when it dashes.
        bool waiting;
        double waitingHeard, waitingDue;
        // The latest tick end held (frozen or fallen): no dash starts before it.
        double heldUntil = double.NegativeInfinity;
        // The lunge in flight: its direction, end, the next time on its path to strike from, and who it has struck.
        bool sweeping;
        double dirX, dirY, lungeEnd, nextSample;
        readonly bool[] struck;
        readonly int[] reached;

        internal Dash(FreeHero hero, BeliefLog log, BeatClock clock, SimState state, HeroHealth health, Horde horde, ModeRules rules)
        {
            this.hero = hero;
            this.log = log;
            this.clock = clock;
            this.state = state;
            this.health = health;
            this.horde = horde;
            beatGrid = rules.BeatGrid;
            struck = new bool[state.EnemyStore.Length];
            reached = new int[state.EnemyStore.Length];
        }

        public int Dashes => dashes;

        bool Held => state.Frozen || state.Hero.Fallen;

        /// <summary>Space, heard at <paramref name="heard"/>: a dash now, from when it was heard, or once the one before allows.</summary>
        internal void Press(Simulation sim, double heard)
        {
            if (Held) return;
            double start = Math.Max(Math.Max(heard, heldUntil), Math.Max(sim.Now - Sword.PressBackS, nextFrom));
            if (start > sim.Now + 1e-9)
            {
                waiting = true;
                waitingHeard = heard;
                waitingDue = start;
                return;
            }
            Begin(sim, start, heard);
        }

        /// <summary>A later Space went elsewhere (a finisher's, <see cref="Finishers"/>): a press waiting for the pulse is replaced by it, and never dashes.</summary>
        internal void Supersede() => waiting = false;

        /// <summary>The tick's end: a waiting press dashes, and the lunge strikes along the path the hero has taken.</summary>
        internal void Tick(Simulation sim, double end)
        {
            if (Held)
            {
                heldUntil = end;
                waiting = false;
                sweeping = false;
                return;
            }
            if (waiting && waitingDue <= end + 1e-9)
            {
                waiting = false;
                Begin(sim, waitingDue, waitingHeard);
            }
            Sweep(sim, end);
        }

        void Begin(Simulation sim, double start, double heard)
        {
            bool onBeat = BeatGrade.OnBeat(log, clock, heard, WindowS);
            double length = onBeat ? LongS : ShortS, end = start + length;
            sim.HeroWas(start, out double x, out double y);
            hero.DashDirection(x, y, out dirX, out dirY);
            nextFrom = start + StrikePeriod(heard) - 2 * WindowS;
            sim.Emit(SimEvent.HeroDash(sim.Now, dashes++, onBeat, FreeHero.FacingOf(dirX, dirY), start, end, x, y, dirX, dirY));
            hero.Dash(sim, start, end, dirX * (onBeat ? LongM : ShortM) / length, dirY * (onBeat ? LongM : ShortM) / length);
            health.Shield(end + AfterS);
            if (!onBeat || sim.Sword == null) return;
            sim.Sword.SpinAfter(end);
            if (horde == null) return;
            sweeping = true;
            lungeEnd = end;
            nextSample = start;
            Array.Clear(struck, 0, struck.Length);
            Sweep(sim, sim.Now);
        }

        /// <summary>
        /// The lunge's blows up to <paramref name="upTo"/>: from the hero where it was at the dash's start, each tick end
        /// and its end, the enemies in <see cref="ReachM"/> not yet struck; those the sim has passed against the world then.
        /// </summary>
        void Sweep(Simulation sim, double upTo)
        {
            while (sweeping && nextSample <= upTo + 1e-9)
            {
                double t = nextSample;
                sim.HeroWas(t, out double hx, out double hy);
                horde.Rewound = t < sim.Now - 1e-9;
                int n = horde.InReach(t, hx, hy, ReachM, reached);
                for (int k = 0; k < n; k++)
                {
                    int i = reached[k];
                    if (struck[i]) continue;
                    struck[i] = true;
                    // Aside of the path, on the enemy's side of it.
                    horde.PositionOf(i, t, out double ex, out double ey);
                    double side = (ex - hx) * -dirY + (ey - hy) * dirX >= 0 ? 1 : -1;
                    sim.Sword.Lunge(sim, i, t, -dirY * side, dirX * side);
                }
                horde.Rewound = false;
                if (t >= lungeEnd - 1e-9) sweeping = false;
                else nextSample = Math.Min(lungeEnd, (Math.Floor(t * Simulation.TicksPerSecond + 1e-9) + 1) * Simulation.TickSeconds);
            }
        }

        /// <summary>
        /// The sword's strike period at <paramref name="heard"/>: the latest beat's period, twice it on the half-time pulse
        /// (<see cref="Sword.HalfTimeBelow"/>), half on the beat grid's slow songs (<see cref="Sword.HalfBeatsAbove"/>).
        /// </summary>
        double StrikePeriod(double heard)
        {
            var beats = log.Beats;
            if (beats.Length == 0) return NoBeatPeriodS;
            double p = clock.PeriodOf(Math.Max(0, BeatClock.LastAtOrBefore(beats, heard)));
            if (!(p > 0)) return NoBeatPeriodS;
            return p < Sword.HalfTimeBelow ? 2 * p : beatGrid && p > Sword.HalfBeatsAbove ? p / 2 : p;
        }
    }
}
