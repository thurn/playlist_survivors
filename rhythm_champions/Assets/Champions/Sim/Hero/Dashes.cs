using System;

namespace Champions.Sim
{
    /// <summary>A dash as it was made (<see cref="Dashes.Dashed"/>): the mods' beads repeat and answer it.</summary>
    public struct DashRecord
    {
        /// <summary>The run's dash count when it was made, from 0.</summary>
        public int Index;
        /// <summary>The dash type (<see cref="DashRules.Kinds"/>) and rank (4: the evolution).</summary>
        public int Kind, Rank;
        /// <summary>When its press was heard, and its motion's span.</summary>
        public double Heard, Start, End;
        /// <summary>Where it started and the way it went (a unit vector).</summary>
        public double FromX, FromY, DirX, DirY;
        /// <summary>Its fixed distance; where it would end with nothing in the way.</summary>
        public double DistanceM;
        public bool OnBeat;
        public JudgedBy By;
        /// <summary>A press in the cooldown's last moments that fired when the dash was ready.</summary>
        public bool Buffered;
    }

    /// <summary>
    /// The dash (design.md, "The Dash" and "Dashes"; Decisions 5 and 6): Space, the hero's one key-driven
    /// ability, data-driven from dashes.json.
    ///
    /// - <b>From heard time.</b> A press heard at h, taken at the sim's time F, starts the dash at h (no
    ///   earlier than the walk's history or a hold allows): the hero is re-walked from then with the dash
    ///   (<see cref="HeroWalk.Dash"/>), and the dash's blows the sim has passed land at once, each at its own
    ///   time, so the client, drawing trailing by the lead, shows it leave on the key-down frame.
    /// - <b>Cooldown</b> <see cref="CooldownS"/> (0.6 s, Quickstep's to lower) from a dash's start to the next.
    ///   A press heard within <see cref="DashRules.BufferS"/> of the dash being ready fires the moment it is,
    ///   judged by when it was heard; an earlier press is dropped (<see cref="SimEventKind.DashDropped"/>, the
    ///   pip's blink). A later press in the buffer replaces a waiting one unless that one was on the beat.
    /// - <b>Fixed distance and duration</b> per dash form, on the beat or off, at every tempo.
    /// - <b>Direction</b>: the held keys, else toward the click target, else the facing
    ///   (<see cref="HeroWalk.Direction"/>), from where the hero was at the start. A dash passing its click
    ///   target clears it; structures stop and slide a dash, and a damaging one breaks through
    ///   (<see cref="HeroWalk"/>). Enemies never stop it.
    /// - <b>I-frames</b> from the start to <see cref="DashRules.IFramesAfterS"/> after the end
    ///   (<see cref="HeroHealth.Shield"/>).
    /// - <b>On the beat</b> (<see cref="Judgement"/>, from the heard time): the blows deal
    ///   <see cref="DashRules.OnBeatDamage"/> times their damage over shapes <see cref="DashRules.OnBeatArea"/>
    ///   times as wide; the events say so for the client's bigger effect.
    /// - <b>Blows</b> (<see cref="DashPart"/>): a path part strikes each target its reach covers as the
    ///   hero passes, swept tick by tick along the re-walked path; the others land at the dash's start,
    ///   midpoint or end, or on the beats after it (as a telegraph does: 0.5 s a beat beatless), centred
    ///   where the hero was then. Each lands through <see cref="Strikes"/>.
    /// - While the world is held no dash starts, a waiting press is dropped and no blow lands.
    /// </summary>
    internal sealed class Dashes
    {
        /// <summary>A form's path parts at most, mods' included.</summary>
        const int MaxPathParts = 4;

        /// <summary>A blow waiting for its moment.</summary>
        struct Landing
        {
            public DashPart Part;
            public int PartIndex;
            public int Dash;
            public double T;
            /// <summary>The beat it lands on (re-read as beats arrive), or -1.</summary>
            public int Beat;
            /// <summary>When the place it lands is fixed: the dash's start, midpoint or end.</summary>
            public double Anchor;
            public bool Placed;
            /// <summary>Where the dash started (a path's start), and where the blow lands (a path's end).</summary>
            public double FromX, FromY, X, Y, DirX, DirY;
            public bool OnBeat;
        }

        readonly DashRules rules;
        readonly HeroState state;
        readonly HeroWalk walk;
        readonly HeroHealth health;
        readonly Strikes strikes;
        readonly BeatServices beats;
        readonly double beatlessBeat;
        readonly int beatsPerBar;

        // A press waiting for the dash to be ready.
        bool waiting;
        double waitingHeard;
        Judged waitingJudged;

        // The latest dash, and its form's parts with the mods' (copied at its start).
        DashRecord current;
        bool any;
        readonly DashPart[] parts = new DashPart[16];
        int partCount;

        // The latest dash's path parts being swept: up to where, and who each has struck.
        bool sweeping;
        double sweepT, sweepX, sweepY;
        readonly int[] pathParts = new int[MaxPathParts];
        int pathCount;
        readonly StructList<long>[] pathStruck = new StructList<long>[MaxPathParts];
        // Structures the latest dash ran into, when it has no path part (each struck once a dash).
        readonly StructList<long> walls = new StructList<long>(8);

        readonly StructList<Landing> landings = new StructList<Landing>(32);

        public Dashes(DashRules rules, SimState sim, HeroWalk walk, HeroHealth health, Strikes strikes, BeatServices beats, BeatRules beatRules)
        {
            this.rules = rules;
            state = sim.Hero;
            this.walk = walk;
            this.health = health;
            this.strikes = strikes;
            this.beats = beats;
            beatlessBeat = beatRules.TelegraphBeatlessBeatS;
            beatsPerBar = beatRules.BeatsPerBar;
            for (int i = 0; i < MaxPathParts; i++) pathStruck[i] = new StructList<long>(64);
            CooldownS = rules.CooldownS;
            DamageScale = 1;
            state.Dash = rules.IndexOf(rules.Start);
            state.DashRank = 1;
            state.DashCooldownS = CooldownS;
        }

        public DashRules Rules => rules;

        /// <summary>The cooldown from one dash's start to the next (Quickstep lowers it).</summary>
        public double CooldownS { get; set; }

        /// <summary>Every dash blow's damage times this (Momentum's 2x).</summary>
        public double DamageScale { get; set; }

        /// <summary>Parts every dash adds to its form's (Momentum's trail), base hits off the beat.</summary>
        public StructList<DashPart> ExtraParts { get; } = new StructList<DashPart>(4);

        /// <summary>Called as each dash starts (Afterimage, Encore and Phantom answer it).</summary>
        public event Action<Simulation, DashRecord> Dashed;

        /// <summary>The latest dash (<see cref="HeroState.Dashes"/> &gt; 0).</summary>
        public DashRecord Latest => current;

        public DashKind Kind => rules.Kinds[state.Dash];

        /// <summary>Equips dash type <paramref name="kind"/> at <paramref name="rank"/> (4: the evolution), replacing the one held.</summary>
        public void Equip(Simulation sim, int kind, int rank)
        {
            if (kind < 0 || kind >= rules.Kinds.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            if (!rules.Kinds[kind].HasRank(rank)) throw new ArgumentOutOfRangeException(nameof(rank), $"{rules.Kinds[kind].Id} has no rank {rank}");
            state.Dash = kind;
            state.DashRank = rank;
            sim.Emit(SimEvent.DashEquip(sim.Now, kind, rank));
        }

        /// <summary>Space, heard at <paramref name="heard"/>: a dash from then, a wait for the cooldown's end, or a drop.</summary>
        public void Press(Simulation sim, double heard)
        {
            if (sim.State.Held) return;
            double now = sim.Now;
            double start = Math.Min(now, Math.Max(heard, walk.Earliest(now)));
            var judged = sim.Judge(heard);
            double ready = state.DashReadyAt;
            if (start >= ready - 1e-9)
            {
                waiting = false;
                state.DashWaiting = false;
                Begin(sim, start, heard, judged, false);
                return;
            }
            if (ready - Math.Max(heard, start) <= rules.BufferS + 1e-9)
            {
                if (waiting && waitingJudged.OnBeat && !judged.OnBeat) return;
                waiting = true;
                waitingHeard = heard;
                waitingJudged = judged;
                state.DashWaiting = true;
                if (ready <= now + 1e-9) Fire(sim);
                return;
            }
            sim.Emit(SimEvent.DashDropped(now, heard, ready));
        }

        /// <summary>The tick's end (after the walk's): a waiting press fires, the path sweeps on, blows land.</summary>
        public void Tick(Simulation sim, double now)
        {
            if (sim.State.Held)
            {
                if (waiting)
                {
                    waiting = false;
                    state.DashWaiting = false;
                    sim.Emit(SimEvent.DashDropped(now, waitingHeard, state.DashReadyAt));
                }
                sweeping = false;
                landings.Clear();
                return;
            }
            if (waiting && state.DashReadyAt <= now + 1e-9) Fire(sim);
            Advance(sim, now);
        }

        /// <summary>
        /// A damaging dash ran into structure <paramref name="blocker"/> at (<paramref name="x"/>, <paramref name="y"/>):
        /// it strikes it first (once a dash); true when that broke it, so the dash carries on through.
        /// </summary>
        public bool BreakThrough(Simulation sim, int dash, int blocker, double t, double x, double y)
        {
            var structures = sim.Structures;
            if (structures == null || !any || dash != current.Index) return false;
            double damage = 0;
            HitTags tags = HitTags.Dash;
            for (int i = 0; i < partCount; i++)
                if (parts[i].Damage > damage)
                {
                    damage = parts[i].Damage;
                    tags = parts[i].Tags | HitTags.Dash;
                }
            if (!(damage > 0)) return false;
            // Shared with the first path part's, so the path's sweep doesn't strike it again.
            var struck = pathCount > 0 ? pathStruck[0] : walls;
            var blow = new Blow
            {
                Shape = HitShape.Circle(x, y, walk.Radius),
                T = t,
                Damage = damage * DamageScale * (current.OnBeat ? rules.OnBeatDamage : 1),
                Tags = tags,
                OnBeat = current.OnBeat,
                Source = BlowSource.Dash,
                Owner = dash,
                Part = -1,
            };
            return strikes.StrikeOne(sim, structures, blocker, blow, struck);
        }

        void Fire(Simulation sim)
        {
            waiting = false;
            state.DashWaiting = false;
            double start = Math.Max(state.DashReadyAt, walk.Earliest(sim.Now));
            Begin(sim, start, waitingHeard, waitingJudged, true);
        }

        void Begin(Simulation sim, double start, double heard, Judged judged, bool buffered)
        {
            // The dash before is cut short here: its path is swept up to the new one's start.
            if (sweeping) Sweep(sim, Math.Min(start, current.End));
            sweeping = false;
            var kind = rules.Kinds[state.Dash];
            var form = kind.Form(state.DashRank);
            walk.PositionWas(start, out double x, out double y);
            walk.Direction(x, y, out double dx, out double dy);
            double end = start + form.DurationS;
            current = new DashRecord
            {
                Index = state.Dashes,
                Kind = state.Dash,
                Rank = state.DashRank,
                Heard = heard,
                Start = start,
                End = end,
                FromX = x,
                FromY = y,
                DirX = dx,
                DirY = dy,
                DistanceM = form.DistanceM,
                OnBeat = judged.OnBeat,
                By = judged.By,
                Buffered = buffered,
            };
            any = true;
            state.Dashes++;
            state.DashStart = start;
            state.DashEnd = end;
            state.DashOnBeat = judged.OnBeat;
            state.DashReadyAt = start + CooldownS;
            state.DashCooldownS = CooldownS;
            walls.Clear();

            partCount = 0;
            foreach (var p in form.Parts) AddPart(p);
            for (int i = 0; i < ExtraParts.Count; i++) AddPart(ExtraParts[i]);
            bool damaging = false;
            for (int i = 0; i < partCount; i++) damaging |= parts[i].Damage > 0;

            sim.Emit(SimEvent.HeroDash(sim.Now, current.Index, current.Kind, current.Rank, judged.By, buffered, start, end, x, y, dx, dy));
            Plan(sim, form);
            health.Shield(current.Index, start, end + rules.IFramesAfterS, heard, judged.By);
            double speed = form.DistanceM / form.DurationS;
            walk.Dash(sim, current.Index, start, end, dx * speed, dy * speed, damaging);
            Dashed?.Invoke(sim, current);
            Advance(sim, sim.Now);
        }

        void AddPart(in DashPart p)
        {
            if (partCount == parts.Length) throw new InvalidOperationException("a dash of more than " + parts.Length + " parts");
            parts[partCount++] = p;
        }

        /// <summary>The dash's blows: its path parts swept from its start, the rest waiting for their moments.</summary>
        void Plan(Simulation sim, DashForm form)
        {
            pathCount = 0;
            double area = current.OnBeat ? rules.OnBeatArea : 1;
            for (int i = 0; i < partCount; i++)
            {
                var p = parts[i];
                if (p.Shape == ShapeKind.Path && p.When == PartWhen.Dash)
                {
                    if (pathCount == MaxPathParts) throw new InvalidOperationException("a dash of more than " + MaxPathParts + " path parts");
                    pathStruck[pathCount].Clear();
                    pathParts[pathCount++] = i;
                    double toX = current.FromX + current.DirX * form.DistanceM, toY = current.FromY + current.DirY * form.DistanceM;
                    sim.Emit(SimEvent.DashPart(current.Start, current.Index, i, current.OnBeat,
                        HitShape.Path(current.FromX, current.FromY, toX, toY, p.RadiusM * area), Damage(p)));
                    continue;
                }
                for (int k = 1; k <= p.Repeat; k++)
                {
                    var landing = new Landing
                    {
                        Part = p,
                        PartIndex = i,
                        Dash = current.Index,
                        OnBeat = current.OnBeat,
                        FromX = current.FromX,
                        FromY = current.FromY,
                        DirX = current.DirX,
                        DirY = current.DirY,
                        Beat = -1,
                        Anchor = p.At == PartAt.Start ? current.Start : p.At == PartAt.Mid ? (current.Start + current.End) / 2 : current.End,
                    };
                    switch (p.When)
                    {
                        case PartWhen.Dash:
                            landing.T = landing.Anchor;
                            break;
                        case PartWhen.NextBeat:
                            landing.T = AfterBeats(current.End, k, false, out landing.Beat);
                            break;
                        default:
                            landing.T = AfterBeats(current.End, k, true, out landing.Beat);
                            break;
                    }
                    landings.Add(landing);
                }
            }
            sweeping = pathCount > 0;
            sweepT = current.Start;
            sweepX = current.FromX;
            sweepY = current.FromY;
        }

        /// <summary>
        /// The <paramref name="k"/>-th beat (or downbeat) after <paramref name="t"/> and its index: predicted from
        /// the beats known, re-read as they arrive; 0.5 s a beat beatless (index -1).
        /// </summary>
        double AfterBeats(double t, int k, bool downbeat, out int index)
        {
            index = -1;
            if (beats.Reliable(t))
            {
                int first = -1;
                if (downbeat && beats.HasBars)
                {
                    beats.NextDownbeat(t + 1e-6, out first);
                    if (first >= 0) first += (k - 1) * beatsPerBar;
                }
                else
                {
                    beats.NextBeat(t + 1e-6, out first);
                    if (first >= 0) first += k - 1;
                }
                double at = first >= 0 ? beats.PredictBeat(first) : double.NaN;
                if (!double.IsNaN(at))
                {
                    index = first;
                    return at;
                }
            }
            return t + k * beatlessBeat;
        }

        /// <summary>Sweeps the path and lands every blow due by <paramref name="upTo"/>.</summary>
        void Advance(Simulation sim, double upTo)
        {
            if (sweeping) Sweep(sim, Math.Min(upTo, current.End));
            for (int i = 0; i < landings.Count; i++)
            {
                ref var l = ref landings[i];
                if (l.Beat >= 0)
                {
                    double t = beats.PredictBeat(l.Beat);
                    if (!double.IsNaN(t)) l.T = t;
                }
                if (!l.Placed && l.Anchor <= upTo + 1e-9) Place(ref l);
            }
            while (true)
            {
                int next = -1;
                for (int i = 0; i < landings.Count; i++)
                    if (landings[i].Placed && landings[i].T <= upTo + 1e-9 && (next < 0 || landings[i].T < landings[next].T)) next = i;
                if (next < 0) break;
                var l = landings[next];
                landings[next] = landings.Last;
                landings.RemoveLast();
                Land(sim, l);
            }
        }

        /// <summary>Fixes where a waiting blow lands, once its anchor has passed: where the hero was then (a path: start to end).</summary>
        void Place(ref Landing l)
        {
            double x, y;
            if (l.Part.At == PartAt.Start || l.Part.At == PartAt.Mid || !walk.DashEnded(l.Dash, out x, out y))
                walk.PositionWas(l.Anchor, out x, out y);
            l.X = x;
            l.Y = y;
            l.Placed = true;
        }

        void Land(Simulation sim, in Landing l)
        {
            var p = l.Part;
            double area = l.OnBeat ? rules.OnBeatArea : 1;
            HitShape shape;
            switch (p.Shape)
            {
                case ShapeKind.Path: shape = HitShape.Path(l.FromX, l.FromY, l.X, l.Y, p.RadiusM); break;
                case ShapeKind.Ring: shape = HitShape.Ring(l.X, l.Y, p.InnerM, p.RadiusM); break;
                case ShapeKind.Cone: shape = HitShape.Cone(l.X, l.Y, l.DirX, l.DirY, p.RadiusM, p.HalfArc); break;
                default: shape = HitShape.Circle(l.X, l.Y, p.RadiusM); break;
            }
            var blow = Blow(p, l.PartIndex, l.Dash, l.OnBeat, shape.Scaled(area), l.T);
            sim.Emit(SimEvent.DashPart(l.T, l.Dash, l.PartIndex, l.OnBeat, blow.Shape, blow.Damage));
            strikes.Land(sim, blow);
        }

        /// <summary>The path parts' blows along the hero's re-walked path, tick by tick, up to <paramref name="upTo"/>.</summary>
        void Sweep(Simulation sim, double upTo)
        {
            double area = current.OnBeat ? rules.OnBeatArea : 1;
            while (sweeping && sweepT < upTo - 1e-12)
            {
                double t = Math.Min(upTo, (Math.Floor(sweepT * Simulation.TicksPerSecond + 1e-9) + 1) * Simulation.TickSeconds);
                double x, y;
                if (t < current.End - 1e-12 || !walk.DashEnded(current.Index, out x, out y)) walk.PositionWas(t, out x, out y);
                for (int k = 0; k < pathCount; k++)
                {
                    var p = parts[pathParts[k]];
                    var blow = Blow(p, pathParts[k], current.Index, current.OnBeat, HitShape.Path(sweepX, sweepY, x, y, p.RadiusM * area), t);
                    strikes.Land(sim, blow, pathStruck[k]);
                }
                sweepT = t;
                sweepX = x;
                sweepY = y;
                if (t >= current.End - 1e-12) sweeping = false;
            }
        }

        Blow Blow(in DashPart p, int part, int dash, bool onBeat, HitShape shape, double t)
        {
            return new Blow
            {
                Shape = shape,
                T = t,
                Damage = p.Damage * DamageScale * (onBeat ? rules.OnBeatDamage : 1),
                Tags = p.Tags | HitTags.Dash,
                OnBeat = onBeat,
                Source = BlowSource.Dash,
                Owner = dash,
                Part = part,
                Throw = p.Throw,
                ThrownDamage = p.ThrownDamage * DamageScale * (onBeat ? rules.OnBeatDamage : 1),
                ThrownShatter = p.ThrownShatter,
                Shatter = p.Shatter,
            };
        }

        double Damage(in DashPart p) => p.Damage * DamageScale * (current.OnBeat ? rules.OnBeatDamage : 1);
    }
}
