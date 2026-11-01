using System;
using PS.Game.Sim.Content;

namespace PS.Game.Sim
{
    /// <summary>The hero's attacks, the kind in its attack events (unity-game.md, The Sword's presentation).</summary>
    public enum HeroAttackKind
    {
        /// <summary>Ordinary main-beat strikes alternate left and right slashes.</summary>
        SlashLeft = 0,
        SlashRight = 1,
        /// <summary>A subdivision strike: a quick follow-up from the previous swing's follow-through.</summary>
        FollowUp = 2,
        /// <summary>A heavy strike, on an accent.</summary>
        Spin = 3,
        /// <summary>The first downbeat after a section change; heavy too when it falls on an accent.</summary>
        Slam = 4,
    }

    /// <summary>What dealt a hit to an enemy, the source in its hit events.</summary>
    public enum WeaponHit
    {
        Strike = 0,
        HeavyStrike = 1,
        Cleave = 2,
        /// <summary>A lightning bolt: a strike from the sky or a chain's jump (<see cref="SimEventKind.Bolt"/>).</summary>
        Bolt = 3,
        /// <summary>The finale's blast (<see cref="Horde.NoWeapon"/>'s): no weapon's hit.</summary>
        Blast = 4,
    }

    /// <summary>What a dying enemy drops.</summary>
    public enum ItemKind
    {
        Gem = 0,
        /// <summary>Easy's floor chicken: the hero heals on touching it (<see cref="FloorChickens"/>).</summary>
        Chicken = 1,
        /// <summary>A scheduled boss's chest, its value the schedule minute whose treasure it holds (the chests' to open).</summary>
        Chest = 2,
    }

    /// <summary>
    /// The sword (unity-game.md, The Sword and "Attacks and Steps Land on the Beat").
    ///
    /// - <b>Strikes</b>: one on every <b>strike beat</b>, plus one more for each point of Amount, on
    ///   the strike's even subdivisions: <c>n = max(1, floor(rank amount + Amount))</c> strikes at
    ///   <c>T + k * p / n</c>, p being the interval to the next strike beat when it is known, else
    ///   its period (twice the beat's on the half-time pulse). With no beat known ahead, the same
    ///   pulse repeats every rank interval times Cooldown, counted from the latest pulse.
    /// - <b>Strike beats</b>: every committed beat, unless the tracker's period is under
    ///   <see cref="HalfTimeBelow"/>: then the <b>half-time pulse</b> strikes on every other beat.
    ///   Either way a beat strikes only 0.75 of its strike interval (one period, or two on the
    ///   pulse) or more after the previous strike beat, so a tracker flipping to the half tempo and
    ///   back keeps the pulse, pausing once rather than swinging twice when the flip shifts its
    ///   phase. The accents choose the half-time pulse's phase:
    ///   at <see cref="PhaseShiftAccents"/> accented beats the pulse skipped, each within
    ///   <see cref="PhaseShiftBeats"/> beats of the one before and with no accented strike beat
    ///   between, the pulse moves onto that skipped beat, never striking a beat after the last
    ///   strike: when the last accent is known at the previous beat's planning, that beat gives way
    ///   and the accent strikes; else the pulse pauses past the accent to the beat after the next.
    ///   Every beat is asked in order, even one the previous pulse's subdivisions reach.
    /// - <b>Planning</b> at <c>T - windup</c> (or when the beat is revealed, if later; a beat
    ///   revealed at or after its time is missed): the beat's strikes are queued, each winding up at
    ///   <c>max(impact - windup, the previous impact)</c>, so strikes closer than a wind-up follow on.
    /// - <b>Wind-up</b>: the strike's kind and heaviness are decided, its direction is chosen from
    ///   the enemies there then, and the attack event is emitted with the impact time, always ahead
    ///   of it. A main strike is <b>heavy</b> when the music is clear and a revealed strong onset or
    ///   accent forecast lies within <see cref="AccentWindow"/> of its beat, and the previous main
    ///   strike on a beat was not heavy; the decision is per beat, so a forecast and its onset make
    ///   one heavy strike.
    /// - <b>Impact</b>: every enemy whose body reaches the circle of radius
    ///   <c>shape radius * rank area * Area</c> around the hero (x 1.3 heavy) takes
    ///   <c>rank power * Might</c> (x 2.5 heavy), and is knocked back or dies. A heavy strike sends
    ///   out a cleave wave, its bands one sixteenth apart from the next eighth, hitting each enemy it
    ///   passes once at the wave's damage. Strikes and bands hit crystal structures in reach too
    ///   (<see cref="Structures"/>): a heavy strike and its wave break them, as does Bloody Tear's
    ///   strike within <see cref="Accents.Window"/> of an accented beat; others only glint.
    /// - <b>Bloody Tear</b>, the evolution, is this sword with its own numbers, and its blows may be
    ///   critical (<see cref="Hit"/>).
    /// - Nothing new winds up while the world is frozen or the hero has fallen; an attack already
    ///   emitted lands, as every settled event is final, unless the hero has fallen.
    /// - <b>On the attack key</b> (<see cref="SwordTrigger.Press"/>, a mode's): no beat plans a strike. A press heard
    ///   at h, taken at F, winds up from h (its attack event's wind-up start, so the hero moves on the key-down
    ///   frame; from the previous impact if that is later) and <b>hits at once</b>: its main impact lands at
    ///   <c>h + PressImpactS</c>, beat or no beat; the music grades the payoff, never the timing. <b>In heard
    ///   time</b>: a live key reaches the sim about the lead after it was heard, past its impact, so the moments the
    ///   sim has passed (its wind-up, its blows, its cleave's first bands) resolve at once when it takes the key,
    ///   each against the world as it was at its own time (the free hero's path, the horde's history:
    ///   <see cref="Horde.Rewound"/>), the damage applied at F and the events stamped at their own times. Nothing
    ///   simulated is rewound: what the sim's systems did in between stands (an enemy the blow killed may still have
    ///   bitten, or been hit by another weapon). Never across a hold (frozen, fallen) nor further back than
    ///   <see cref="PressBackS"/>: a key heard before either winds up from there, its grade still from when it was heard.
    ///   It is at least <see cref="SwingS"/> after the previous press's main impact; a press that would wait more
    ///   than <see cref="SwingBufferS"/> past its own landing for that (one in the swing before's strike, before
    ///   its impact) is dropped while that swing shows: mashing swings no faster than the swing, and a press in
    ///   the swing before's follow-through cuts it short into the next. Its reach and damage are scaled by
    ///   <see cref="BeatGrade"/>, measured on the press, which its attack event carries for the client's payoff; a
    ///   full-grade press on a beat that is accented (as a beat strike's heaviness asks) is heavy, once per beat,
    ///   its cleave wave's bands spreading over the swing after its impact. Amount adds follow-ups spread evenly
    ///   over the swing, which never slow the next press. It never slams.
    /// - <b>The beat grid</b> (<see cref="ModeRules.BeatGrid"/>, a mode's): over a <see cref="HalfBeatsAbove"/> period
    ///   a strike beat's interval holds twice the strikes, the one on the half beat a slash of its own, so slow songs
    ///   swing on the eighths rather than leave the hero standing between beats; and every strike after a beat's first
    ///   lands on the beats it spans, a share of each beat interval as its beats-on count says (a half-time pulse's
    ///   on its skipped beat), not evenly over an estimated interval, which a tracked beat's jitter sets off the beat.
    ///   A strike planned before the beats it needs are revealed takes the estimate, and moves onto them at its
    ///   wind-up if they are known then.
    /// - <b>Loudness</b> (<see cref="ModeRules.Loudness"/>, a mode's): the tick level at a strike beat, known at its
    ///   planning, scales its strikes' reach and damage (<see cref="QuietScale"/>, 1, <see cref="LoudScale"/>); a loud
    ///   beat adds a strike to each of its half beats or its beat, on the subdivisions, and its accent may be heavy
    ///   though the strike beat before was.
    /// </summary>
    internal sealed class Sword : Weapon
    {
        /// <summary>A strong onset or a forecast this close to a beat makes its strike heavy (native's FORECAST_WINDOW).</summary>
        public const double AccentWindow = Accents.Window;
        /// <summary>
        /// Under this tracker period (150 BPM) the sword strikes on the half-time pulse: a beat tracked
        /// at double time (Sunflower's 90 BPM heard as 180) would swing back to back with no follow-through.
        /// </summary>
        public const double HalfTimeBelow = 0.4;
        /// <summary>Accents in a row on the beats the half-time pulse skips that move it onto them...</summary>
        public const int PhaseShiftAccents = 2;
        /// <summary>...each this many beats or fewer after the one before: one stray accent never counts toward a later one.</summary>
        public const int PhaseShiftBeats = 8;
        /// <summary>The client plays a wind-up up to this much faster, so this is the shortest it fits.</summary>
        public const double FastestRetime = 4.0 / 3.0;
        public const int AimDirections = 16;
        /// <summary>The arc the aim scores enemies in: native's light swing, 150 degrees.</summary>
        public static readonly double AimHalfAngle = 75 * Math.PI / 180;
        /// <summary>Native's aim weights: a boss outweighs a crowd of hundreds.</summary>
        public const double BossWeight = 1000, BruteWeight = 2;
        /// <summary>Bloody Tear's heal on a critical blow, and the run clock between two (native's).</summary>
        public const double TearHeal = 8, TearHealEveryS = 1;
        /// <summary>
        /// A press's impact this long after it was heard at the soonest (s): two 60 Hz frames, so the strike shows a
        /// sliver of its swing on the key-down frame and the next before its blow. A live key reaches the sim after
        /// that (about the lead later), and the press resolves in heard time (see the class's On the attack key).
        /// </summary>
        public const double PressImpactS = 1.0 / 30;
        /// <summary>
        /// The furthest before the sim's time a press resolves (s): past any lead (Bluetooth's is about 0.35 s), and
        /// inside the free hero's kept path (<see cref="FreeHero.History"/>, 0.53 s).
        /// </summary>
        public const double PressBackS = 0.5;
        /// <summary>The swing's length (s): presses' main impacts are at least this far apart.</summary>
        public const double SwingS = 0.15;
        /// <summary>
        /// The longest a press waits for the swing before it (s): as long as the swing, so only a press before the
        /// swing before's impact, while its strike shows, is dropped; one in its follow-through cuts it short.
        /// </summary>
        public const double SwingBufferS = 0.15;
        /// <summary>
        /// Over this tracker period (80 BPM) a mode with <see cref="ModeRules.BeatGrid"/> strikes every half beat: slashes
        /// then come 0.375 s apart or more, as on the half-time pulse they come 0.8 s apart or less.
        /// </summary>
        public const double HalfBeatsAbove = 0.75;
        /// <summary>A quiet strike beat's reach and damage, with <see cref="ModeRules.Loudness"/>.</summary>
        public const double QuietScale = 0.75;
        /// <summary>A loud strike beat's reach and damage, with <see cref="ModeRules.Loudness"/>.</summary>
        public const double LoudScale = 1.25;
        const int QueueCapacity = 64;
        const int WaveCapacity = 4;

        struct Strike
        {
            public double Impact, Windup, Period;
            /// <summary>A press's wind-up start, its attack event's: when it was heard (at most PressBackS back, never before a hold), or the impact before it if later.</summary>
            public double From;
            public int Beat;
            public bool Main, HalfTime, Wound, Cancelled, Heavy, Pressed;
            /// <summary>A half beat's strike (<see cref="ModeRules.BeatGrid"/>): a slash, never a follow-up.</summary>
            public bool Half;
            /// <summary>On a loud beat (<see cref="ModeRules.Loudness"/>): heavy though the strike beat before was.</summary>
            public bool Loud;
            /// <summary>Beats on from its strike beat (<see cref="ModeRules.BeatGrid"/>): where on the beats it lands.</summary>
            public double On;
            /// <summary>The reach's and damage's scale: a press's grade (0.5 to 1), else 1.</summary>
            public double Scale;
            public double Reach, DirX, DirY;
        }

        struct Wave
        {
            public bool Active;
            public int Id, Band, Bands;
            public double X, Y, Inner, Outer, First, Every, Damage;
        }

        readonly BeliefLog log;
        readonly BeatClock clock;
        readonly SimState state;
        readonly Horde horde;
        readonly Structures structures;
        readonly Zones zones;
        readonly EnemyRole[] roles;
        readonly double windup;
        readonly bool onPress, beatGrid, loudness;
        // The beat the latest heavy press was measured from: one heavy per beat.
        double lastHeavyPress = double.NaN;
        // The latest press's main impact: the next press's lands a swing after it, its follow-ups between.
        double lastPress = double.NegativeInfinity;
        // The latest tick end the sword held (frozen, fallen or out of play): a press never resolves before it.
        double heldUntil = double.NegativeInfinity;

        readonly Strike[] queue = new Strike[QueueCapacity];
        int head, count;
        int planBeat;
        double lastImpact = double.NegativeInfinity;
        double lastPulse = double.NegativeInfinity;
        int slashes;
        bool forcedAccent, lastHeavy;
        // The latest strike beat's time, and the half-time pulse's accents in a row off its phase, the latest at lastOffAccent.
        double lastStrikeBeat = double.NegativeInfinity, lastOffAccent = double.NegativeInfinity;
        int offAccents;
        readonly Accents accents;
        double lastTearHeal = double.NegativeInfinity;

        readonly Wave[] waves = new Wave[WaveCapacity];
        readonly bool[][] waveHit = new bool[WaveCapacity][];
        int waveCount;

        // The enemies in a hit's reach, and the aim's distances and angles to them.
        readonly int[] reached;
        readonly double[] aimAngle, aimDist;

        public Sword(Arsenal arsenal, WeaponDef def, int index, double windupS, ModeRules rules, GameContent content, BeliefLog log, BeatClock clock,
            SimState state, Horde horde, Structures structures, Zones zones)
            : base(arsenal, state, def, index)
        {
            if (def.Shape.Kind != ShapeKind.Sword) throw new ArgumentException(def.Id + " is not a sword");
            if (!(windupS > 0)) throw new ArgumentException("a positive wind-up");
            this.log = log;
            this.clock = clock;
            accents = new Accents(log, clock);
            this.state = state;
            this.horde = horde;
            this.structures = structures;
            this.zones = zones;
            windup = windupS;
            onPress = rules.Sword == SwordTrigger.Press;
            beatGrid = rules.BeatGrid;
            loudness = rules.Loudness;
            roles = content.Roles;
            int cap = state.EnemyStore.Length;
            reached = new int[cap];
            aimAngle = new double[cap];
            aimDist = new double[cap];
            for (int w = 0; w < WaveCapacity; w++) waveHit[w] = new bool[cap];
        }

        public override bool OnBeats => true;

        /// <summary>Strikes per beat: one, and one more per point of Amount.</summary>
        public int StrikesPerBeat => Amount;

        /// <summary>A strike's damage before the heavy multiplier: the rank's power times Might, at any tempo.</summary>
        public double Reach(bool heavy) =>
            Def.Shape.RadiusM * Numbers.Area * Stats.Area * (heavy ? Def.Shape.HeavyReachScale : 1);

        /// <summary>Committed beats revealed too late to wind up for.</summary>
        public int Missed { get; private set; }

        ref Strike At(int k) => ref queue[(head + k) % QueueCapacity];

        /// <summary>The next of the sword's exact times at or after <paramref name="floor"/>.</summary>
        internal override double NextTime(double floor)
        {
            double t = PlanTime(floor);
            if (count > 0) t = Math.Min(t, At(0).Impact);
            for (int k = 0; k < count; k++)
            {
                if (At(k).Wound) continue;
                t = Math.Min(t, Math.Max(At(k).Windup, floor));
                break;
            }
            for (int w = 0; w < WaveCapacity; w++)
                if (waves[w].Active) t = Math.Min(t, Math.Max(BandTime(w), floor));
            return t;
        }

        /// <summary>When the next committed beat is planned; beats too late to wind up for are skipped.</summary>
        double PlanTime(double floor)
        {
            if (onPress) return double.PositiveInfinity;
            var beats = log.Beats;
            while (planBeat < beats.Length)
            {
                double t = beats[planBeat].T;
                if (t > floor + 1e-9) return Math.Max(t - windup, floor);
                Missed++;
                planBeat++;
            }
            return double.PositiveInfinity;
        }

        double BandTime(int w) => waves[w].First + waves[w].Band * waves[w].Every;

        /// <summary>
        /// One of the sword's exact times (<see cref="NextTime"/>'s): an impact, a cleave band, a
        /// beat's plan or a wind-up, in that order at one time. Times compare exactly, so each
        /// happens at its own time, never at a neighbour's a rounding away.
        /// </summary>
        internal override void Fire(Simulation sim, double t)
        {
            if (count > 0 && At(0).Impact <= t)
            {
                var s = At(0);
                head = (head + 1) % QueueCapacity;
                count--;
                Impact(sim, s, t);
                return;
            }
            for (int w = 0; w < WaveCapacity; w++)
                if (waves[w].Active && BandTime(w) <= t)
                {
                    Band(sim, w, t);
                    return;
                }
            if (PlanTime(t) <= t)
            {
                // Every beat is asked, in order, even one the previous pulse's strikes reach (on the
                // half-time pulse, a skipped beat holds a subdivision): its accent still counts.
                int b = planBeat++;
                if (Holding || !StrikesOn(b, t, out bool halfTime) || log.Beats[b].T <= lastImpact + 1e-9) return;
                Pulse(t, log.Beats[b].T, StrikePeriod(b, halfTime), b, halfTime);
                return;
            }
            for (int k = 0; k < count; k++)
            {
                ref var s = ref At(k);
                if (s.Wound) continue;
                if (s.Windup <= t) WindUp(sim, ref s, t);
                return;
            }
        }

        /// <summary>The per-tick system, at the tick's end: with no beat known ahead, the pulse on the sword's raw interval.</summary>
        internal override void Tick(Simulation sim, double end)
        {
            if (onPress && Holding) heldUntil = end;
            if (onPress || Holding || count > 0) return;
            double interval = Interval;
            double due = lastPulse + interval;
            if (end < due - windup - 1e-9 || clock.StateAt(end) != Reliability.NoBeatAhead) return;
            double shortest = windup / FastestRetime;
            double impact = Math.Max(due, end + shortest);
            // A revealed beat close after it takes over.
            double plan = PlanTime(end);
            if (plan < double.PositiveInfinity && log.Beats[planBeat].T < impact + interval * 0.5) return;
            Pulse(end, impact, interval, -1);
        }

        /// <summary>
        /// Is beat <paramref name="b"/>, planned at <paramref name="now"/>, a strike beat, and is the
        /// sword on its half-time pulse there? Asked once per beat, in order.
        /// </summary>
        bool StrikesOn(int b, double now, out bool halfTime)
        {
            var beats = log.Beats;
            var beat = beats[b];
            halfTime = beat.V0 < HalfTimeBelow;
            bool strikes = beat.T - lastStrikeBeat >= 0.75 * (halfTime ? 2 : 1) * beat.V0 - 1e-9;
            if (!halfTime) offAccents = 0;
            else if (accents.Accented(beat.T, now))
            {
                if (strikes) offAccents = 0;
                else if (ShiftsPhase(beat))
                {
                    // Known too late to strike it the moment after the last strike: the pulse moves
                    // onto it, pausing for 1.5 beats rather than swinging twice a beat apart.
                    lastStrikeBeat = beat.T;
                    return false;
                }
            }
            else if (strikes && offAccents > 0 && b + 1 < beats.Length && WouldShift(beats[b + 1], now))
                // The next, skipped beat's accent, known now, completes the shift: this beat gives way,
                // and the next strikes on its accent, 1.5 beats after the last strike.
                return false;
            if (strikes) lastStrikeBeat = beat.T;
            return strikes;
        }

        /// <summary>Accents in a row off the half-time pulse's phase, counting one more at <paramref name="beat"/>.</summary>
        int OffAccentsWith(in StreamEntry beat) =>
            (beat.T - lastOffAccent <= PhaseShiftBeats * beat.V0 + 1e-9 ? offAccents : 0) + 1;

        /// <summary>Would an accent on the skipped <paramref name="beat"/>, known at <paramref name="now"/>, move the pulse onto it?</summary>
        bool WouldShift(in StreamEntry beat, double now) =>
            beat.V0 < HalfTimeBelow && OffAccentsWith(beat) >= PhaseShiftAccents && accents.Accented(beat.T, now);

        /// <summary>Counts the accent on the skipped <paramref name="beat"/>: does it move the pulse onto it?</summary>
        bool ShiftsPhase(in StreamEntry beat)
        {
            offAccents = OffAccentsWith(beat);
            lastOffAccent = beat.T;
            if (offAccents < PhaseShiftAccents) return false;
            offAccents = 0;
            return true;
        }

        /// <summary>The interval from strike beat <paramref name="b"/> to the next: one beat, or two on the half-time pulse.</summary>
        double StrikePeriod(int b, bool halfTime)
        {
            var beats = log.Beats;
            int beatsOn = halfTime ? 2 : 1;
            for (int k = 0; k < beatsOn; k++)
                if (!clock.StartsInterval(b + k)) return beatsOn * clock.PeriodOf(b);
            return beats[b + beatsOn].T - beats[b].T;
        }

        /// <summary>
        /// Queues a pulse's strikes from <paramref name="t0"/>, <paramref name="period"/> long, planned at <paramref name="now"/>:
        /// its beat's, or its half beats' with <see cref="ModeRules.BeatGrid"/> on a slow beat, each with Amount's subdivisions.
        /// </summary>
        void Pulse(double now, double t0, double period, int beat, bool halfTime = false)
        {
            int halves = beatGrid && beat >= 0 && log.Beats[beat].V0 > HalfBeatsAbove ? 2 : 1;
            var level = loudness && beat >= 0 ? LevelAt(t0) : LoudnessLevel.Medium;
            bool loud = level == LoudnessLevel.Loud;
            int per = StrikesPerBeat + (loud ? 1 : 0);
            int n = per * halves;
            double scale = level == LoudnessLevel.Quiet ? QuietScale : loud ? LoudScale : 1;
            lastPulse = t0;
            for (int k = 0; k < n && count < QueueCapacity; k++)
            {
                double on = (halfTime ? 2.0 : 1.0) * k / n;
                double impact = beatGrid && beat >= 0 && Spanned(beat, on, out double at) ? at : t0 + period * k / n;
                At(count++) = new Strike
                {
                    Impact = impact,
                    Windup = Math.Max(impact - windup, Math.Max(lastImpact, now)),
                    Period = period,
                    Beat = beat,
                    Main = k == 0,
                    Half = k > 0 && k % per == 0,
                    Loud = loud,
                    On = on,
                    HalfTime = halfTime,
                    Scale = scale,
                };
                lastImpact = impact;
            }
        }

        /// <summary>
        /// The time <paramref name="on"/> beats on from beat <paramref name="b"/>: whole beats, then a share of the next
        /// interval. False while the beats it needs are not all revealed, each starting an interval.
        /// </summary>
        bool Spanned(int b, double on, out double at)
        {
            int whole = (int)Math.Floor(on + 1e-9);
            double rest = on - whole;
            at = double.NaN;
            for (int k = 0; k < whole + (rest > 1e-9 ? 1 : 0); k++)
                if (!clock.StartsInterval(b + k)) return false;
            var known = log.Beats;
            at = known[b + whole].T;
            if (rest > 1e-9) at += rest * (known[b + whole + 1].T - at);
            return true;
        }

        /// <summary>The loudness of the latest revealed tick at or before <paramref name="t"/>; medium with none.</summary>
        LoudnessLevel LevelAt(double t)
        {
            var ticks = log.Ticks;
            int lo = 0, hi = ticks.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (ticks[mid].T <= t + 1e-9) lo = mid + 1;
                else hi = mid;
            }
            return lo > 0 ? new TickEntry(ticks[lo - 1]).Level : LoudnessLevel.Medium;
        }

        void WindUp(Simulation sim, ref Strike s, double t)
        {
            s.Wound = true;
            // Onto the beats it spans, revealed since its planning.
            if (beatGrid && s.Beat >= 0 && Spanned(s.Beat, s.On, out double at) && at > t) s.Impact = at;
            if (Holding)
            {
                s.Cancelled = true;
                return;
            }
            bool onBeat = s.Main && s.Beat >= 0;
            bool heavy = s.Pressed ? s.Heavy : onBeat && (forcedAccent || (!lastHeavy || s.Loud) && accents.Accented(s.Impact, t));
            if (onBeat)
            {
                forcedAccent = false;
                lastHeavy = heavy;
            }
            bool slam = onBeat && Slams(s.Beat, s.Impact, s.HalfTime);
            var kind = slam ? HeroAttackKind.Slam
                : heavy ? HeroAttackKind.Spin
                : !s.Main && !s.Half ? HeroAttackKind.FollowUp
                : (slashes++ & 1) == 0 ? HeroAttackKind.SlashLeft : HeroAttackKind.SlashRight;
            s.Heavy = heavy;
            s.Reach = Reach(heavy) * s.Scale;
            HeroAt(sim, t, out double hx, out double hy);
            Aim(t, hx, hy, s.Reach, out s.DirX, out s.DirY);
            sim.Emit(SimEvent.SwordAttack(t, Index, kind, s.Pressed ? Math.Min(s.From, t) : t, s.Impact, s.DirX, s.DirY, heavy, s.Reach,
                s.Pressed ? s.Scale : 0));
        }

        /// <summary>
        /// The attack key, heard at <paramref name="heard"/>, in a mode whose sword waits for it: queues the press's
        /// strikes, which wind up from the press and hit at once (see the class's On the attack key).
        /// </summary>
        internal void Press(Simulation sim, double heard)
        {
            if (!onPress || Holding) return;
            double now = sim.Now;
            // Never resolved before a hold nor further back than PressBackS: a key heard earlier winds up from there.
            double start = Math.Max(heard, Math.Max(heldUntil, now - PressBackS));
            double natural = start + PressImpactS;
            double after = lastPress + SwingS;
            if (after - natural > SwingBufferS + 1e-9) return;
            // After every strike queued too: the previous press's follow-ups end within its swing.
            double impact = Math.Max(Math.Max(natural, after), lastImpact + 1e-6);
            lastPress = impact;
            double grade = BeatGrade.Of(log, clock, heard, out double point, out bool onBeat);
            bool heavy = forcedAccent || grade >= 1 && onBeat && point != lastHeavyPress && accents.Accented(point, now);
            forcedAccent = false;
            if (heavy) lastHeavyPress = point;
            int n = StrikesPerBeat;
            for (int k = 0; k < n && count < QueueCapacity; k++)
            {
                double at = impact + SwingS * k / n;
                double from = k == 0 ? Math.Max(start, lastImpact) : lastImpact;
                At(count++) = new Strike
                {
                    Impact = at,
                    Windup = from,
                    From = from,
                    // The cleave's bands spread over the swing, so the whole blow reads at once.
                    Period = SwingS,
                    Beat = -1,
                    Main = k == 0,
                    Pressed = true,
                    Heavy = k == 0 && heavy,
                    Scale = grade,
                };
                lastImpact = at;
            }
            CatchUp(sim);
        }

        /// <summary>
        /// The press's moments the sim has already passed (a live key's wind-up, blows and first cleave bands), each
        /// resolved now at its own time, against the world as it was then (see the class's On the attack key).
        /// </summary>
        void CatchUp(Simulation sim)
        {
            if (horde != null) horde.Rewound = true;
            for (double t = NextTime(double.NegativeInfinity); t <= sim.Now; t = NextTime(double.NegativeInfinity)) Fire(sim, t);
            if (horde != null) horde.Rewound = false;
        }

        /// <summary>Where the hero is at <paramref name="t"/>: before the sim's time (a press in heard time), where it was.</summary>
        void HeroAt(Simulation sim, double t, out double x, out double y)
        {
            if (t < sim.Now) sim.HeroWas(t, out x, out y);
            else state.Hero.PositionAt(t, out x, out y);
        }

        /// <summary>The next main strike to wind up on a beat is heavy (<see cref="DebugCommand.Accent"/>); on the attack key, the next press.</summary>
        internal void ForceAccent() => forcedAccent = true;

        /// <summary>
        /// Does the main strike on beat <paramref name="beat"/> slam: is it the first downbeat after a
        /// section change, the zone change's own downbeat when that is known at the wind-up
        /// (<see cref="Zones.Slams"/>)? The half-time pulse may never fall on a downbeat, so there
        /// the first strike beat after the zone change slams.
        /// </summary>
        bool Slams(int beat, double impact, bool halfTime)
        {
            if (zones == null) return false;
            bool downbeat = clock.IsDownbeat(beat);
            return (downbeat || halfTime) && zones.Slams(impact, shares: downbeat);
        }

        double AimWeight(in EnemyState e) => e.Boss ? BossWeight : roles[e.Role].Brute ? BruteWeight : 1;

        /// <summary>
        /// The swing's direction (native's aim.rs): of 16 directions, the one whose arc holds the
        /// most weight of enemies in reach, weighted toward bosses and brutes, the one nearest the
        /// nearest enemy on ties. With nobody's centre in reach, the nearest enemy whose body is;
        /// with nobody, the hero's facing.
        /// </summary>
        void Aim(double t, double hx, double hy, double reach, out double dirX, out double dirY)
        {
            var enemies = state.EnemyStore;
            int n = horde != null ? horde.InReach(t, hx, hy, reach, reached) : 0;
            int nearest = -1;
            double nearestD = double.PositiveInfinity;
            for (int k = 0; k < n; k++)
            {
                horde.PositionOf(reached[k], t, out double x, out double y);
                double d = Math.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy));
                aimDist[k] = d;
                aimAngle[k] = Math.Atan2(y - hy, x - hx);
                if (d < nearestD)
                {
                    nearestD = d;
                    nearest = k;
                }
            }
            if (nearest < 0)
            {
                Facing(state.Hero.Facing, out dirX, out dirY);
                return;
            }
            double toNearest = aimAngle[nearest];
            double bestScore = 0, bestDir = 0, bestOff = double.PositiveInfinity;
            for (int j = 0; j < AimDirections; j++)
            {
                double dir = Wrap(2 * Math.PI * j / AimDirections);
                double score = 0;
                for (int k = 0; k < n; k++)
                    if (aimDist[k] <= reach && Math.Abs(Wrap(aimAngle[k] - dir)) <= AimHalfAngle + 1e-12)
                        score += AimWeight(enemies[reached[k]]);
                if (score <= 0) continue;
                double off = Math.Abs(Wrap(dir - toNearest));
                if (score > bestScore + 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && off < bestOff - 1e-9))
                {
                    bestScore = score;
                    bestDir = dir;
                    bestOff = off;
                }
            }
            double a = bestScore > 0 ? bestDir : toNearest;
            dirX = Math.Cos(a);
            dirY = Math.Sin(a);
        }

        static double Wrap(double a)
        {
            a %= 2 * Math.PI;
            if (a > Math.PI) a -= 2 * Math.PI;
            else if (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        internal static void Facing(Direction d, out double x, out double y)
        {
            x = d == Direction.Right ? 1 : d == Direction.Left ? -1 : 0;
            y = d == Direction.Up ? 1 : d == Direction.Down ? -1 : 0;
        }

        void Impact(Simulation sim, in Strike s, double t)
        {
            if (s.Cancelled || !s.Wound || state.Hero.Fallen) return;
            HeroAt(sim, t, out double hx, out double hy);
            int wave = s.Heavy ? StartWave(sim, s, t, hx, hy) : -1;
            double launch = Launch(s.Heavy ? Def.Shape.HeavyDamage : 1);
            if (s.Heavy || !Def.IsEvolution || !sim.Accents.NearAccentedBeat(t, out int accent))
                structures.Strike(sim, t, hx, hy, s.Reach, s.Heavy ? StructureHit.HeavyStrike : StructureHit.Strike, launch,
                    wave >= 0 ? waves[wave].Id : -1);
            else structures.Strike(sim, t, hx, hy, s.Reach, StructureHit.EvolvedAccent, launch, accent);
            if (horde == null) return;
            double damage = Power * (s.Heavy ? Def.Shape.HeavyDamage : 1) * s.Scale;
            var how = s.Heavy ? WeaponHit.HeavyStrike : WeaponHit.Strike;
            int n = horde.InReach(t, hx, hy, s.Reach, reached);
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                horde.PositionOf(i, t, out double x, out double y);
                double dx = x - hx, dy = y - hy;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (wave >= 0) waveHit[wave][i] = true;
                Hit(sim, i, t, damage, how, d > 1e-9 ? dx / d : s.DirX, d > 1e-9 ? dy / d : s.DirY, launch);
            }
        }

        /// <summary>
        /// A blow on enemy <paramref name="i"/>. A rank with crits (Bloody Tear's, native's combat.rs)
        /// lands critical at its crit chance times Luck, for its crit multiple, and a critical blow heals
        /// the hero <see cref="TearHeal"/>, at most once each <see cref="TearHealEveryS"/> of run clock.
        /// </summary>
        void Hit(Simulation sim, int i, double t, double damage, WeaponHit how, double dirX, double dirY, double launch)
        {
            var rank = Numbers;
            bool critical = rank.CritEnabled && sim.Random.NextDouble() < rank.CritChance * Stats.Luck;
            horde.Damage(sim, i, t, critical ? damage * rank.CritMultiplier : damage, Index, how, dirX, dirY, rank.Knockback, launch, critical);
            if (!critical || state.RunClock < lastTearHeal + TearHealEveryS - 1e-9) return;
            lastTearHeal = state.RunClock;
            sim.Health.Heal(t, TearHeal, 0);
        }

        /// <summary>A heavy strike's cleave wave from (hx, hy): its first band on the next eighth, then one each sixteenth.</summary>
        int StartWave(Simulation sim, in Strike s, double t, double hx, double hy)
        {
            int w = 0;
            while (w < WaveCapacity && waves[w].Active) w++;
            // Waves last two beats and heavy strikes come a beat apart at most: a full set is never reached.
            if (w == WaveCapacity) return -1;
            var shape = Def.Shape;
            int bands = (int)Math.Ceiling(shape.WaveBeyondM / shape.WaveBandM - 1e-9);
            waves[w] = new Wave
            {
                Active = true,
                Id = waveCount,
                Bands = bands,
                X = hx,
                Y = hy,
                Inner = s.Reach,
                Outer = s.Reach + shape.WaveBeyondM,
                First = t + 0.5 * s.Period,
                Every = 0.25 * s.Period,
                Damage = shape.WaveDamage * Power,
            };
            Array.Clear(waveHit[w], 0, waveHit[w].Length);
            sim.Emit(SimEvent.Cleave(t, waveCount++, bands, hx, hy, waves[w].Inner, waves[w].Outer, waves[w].First, waves[w].Every));
            return w;
        }

        void Band(Simulation sim, int w, double t)
        {
            ref var wv = ref waves[w];
            double outer = Math.Min(wv.Inner + Def.Shape.WaveBandM * (wv.Band + 1), wv.Outer);
            if (++wv.Band >= wv.Bands) wv.Active = false;
            if (state.Hero.Fallen) return;
            double launch = Launch(1);
            structures.Strike(sim, t, wv.X, wv.Y, outer, StructureHit.Cleave, launch, wv.Id);
            if (horde == null) return;
            var enemies = state.EnemyStore;
            var hit = waveHit[w];
            int n = horde.InReach(t, wv.X, wv.Y, outer, reached);
            for (int k = 0; k < n; k++)
            {
                int i = reached[k];
                if (hit[i]) continue;
                horde.PositionOf(i, t, out double x, out double y);
                double dx = x - wv.X, dy = y - wv.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d + 0.5 * roles[enemies[i].Role].RadiusM <= wv.Inner) continue;
                hit[i] = true;
                Hit(sim, i, t, wv.Damage, WeaponHit.Cleave, d > 1e-9 ? dx / d : 1, d > 1e-9 ? dy / d : 0, launch);
            }
        }
    }
}
