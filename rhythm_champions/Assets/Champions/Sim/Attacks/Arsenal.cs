using System;

namespace Champions.Sim
{
    /// <summary>
    /// The hero's attacks and mods (design.md, "Attacks" and "Mods"; Decisions 8, 9 and 11): the build, the
    /// system that fires the attacks on the beat grid, and the mods on every blow the hero deals.
    ///
    /// - <b>The build</b>: up to three attacks (rank 1-3, 4 the evolution) and four mods (rank 1-2), set by
    ///   the run's loadout and the progression (<see cref="Set"/>).
    /// - <b>Sword Slash</b> strikes on the sim's attack cadence (<see cref="Simulation.Cadence"/>: 1, 2 or 4
    ///   beats nearest 1 s, a 1 s pulse beatless). Its swing is planned <see cref="SwordSlashRules.WindupS"/>
    ///   before the strike (<see cref="SimEventKind.Swing"/>): aimed at the densest group in reach, else the
    ///   nearest, and held through the swing; with nothing within reach plus <see cref="AttackRules.AimSlackM"/>
    ///   the hero holds a ready stance instead (<see cref="SimEventKind.SwingHold"/>) and the count carries on.
    /// - <b>The other attacks</b> fire on the half-beat grid (<see cref="AttackGrid"/>) or, for Orbit Blades,
    ///   circle continuously at a lap a bar; Phantom repeats Sword Slash a beat later.
    /// - <b>Extra strikes</b> (Tempo's, Twin Strike's echoes, summons', phantoms', afterimages') are flagged
    ///   <see cref="BlowFlags.Spectral"/>: the hero's own swing stays one clip per strike at natural speed.
    /// - <b>Blows</b> land through <see cref="Strikes"/>, which asks the mods here to change each by its tags
    ///   (<see cref="Modify"/>) and to follow it up (<see cref="Landed"/>); the dashes' mods tune the dash
    ///   rules' hooks (<see cref="Dashes.CooldownS"/>, <see cref="Dashes.DamageScale"/>,
    ///   <see cref="Dashes.ExtraParts"/>, <see cref="Dashes.Dashed"/>).
    ///
    /// The sim drives it in order: the beat grid's beats and state as they fire, the cadence's strikes, then
    /// <see cref="Tick"/> after the hero's systems, which takes everything due in time order. While the world
    /// is held nothing fires and nothing in flight moves; what fell due then is dropped.
    /// </summary>
    public sealed partial class Arsenal
    {
        /// <summary>A planned swing further than this from the strike that comes is planned again.</summary>
        const double PlanTolerance = 0.1;

        enum LaterKind : byte
        {
            /// <summary>Land a copy of a blow (an echo, a repeat).</summary>
            Copy,
            /// <summary>Tempo's spectral Sword Slash between two of the hero's.</summary>
            TempoSlash,
            /// <summary>The phantoms repeat a Sword Slash (its reach, arc, damage and flags).</summary>
            PhantomSlash,
            /// <summary>An afterimage or phantom repeats a dash's move (drawn only; its blows come as copies).</summary>
            DashEcho,
        }

        struct Later
        {
            public double T;
            public long Seq;
            public LaterKind Kind;
            public Blow Blow;
            /// <summary>A slash's reach and arc; a dash echo's duration.</summary>
            public double Reach, Arc;
            /// <summary>A dash echo's: who repeats it (-1 an afterimage, k phantom k).</summary>
            public int Who;
        }

        struct CadenceStrike
        {
            public double T;
            public int Beat;
        }

        readonly AttackRules rules;
        readonly ModRules mods;
        readonly Enemies enemies;
        readonly AttackGrid grid;
        readonly int[] attackRanks = new int[Upgrade.AttackCount];
        readonly int[] modRanks = new int[Upgrade.ModCount];
        readonly StructList<CadenceStrike> strikes = new StructList<CadenceStrike>(8);
        int strikeHead;
        readonly StructList<Later> later = new StructList<Later>(256);
        long laterSeq;
        // Query scratch: near for an aim or a candidate list, near2 for a count inside one.
        readonly StructList<int> near, near2;

        // Sword Slash's plan for its next strike.
        bool planned, plannedHold, plannedSpin;
        double plannedImpact, aimX, aimY;
        int cadenceStrikes;

        public Arsenal(AttackRules rules, ModRules mods, Enemies enemies, BeatServices beats, int enemyCapacity)
        {
            this.rules = rules;
            this.mods = mods;
            this.enemies = enemies;
            grid = new AttackGrid(beats, rules.BeatlessBeatS);
            near = new StructList<int>(enemyCapacity);
            near2 = new StructList<int>(enemyCapacity);
            counts = new StructList<int>(enemyCapacity);
            bursts = new StructList<Burst>(enemyCapacity);
            spots = new StructList<Spot>(16);
            waves = new StructList<Wave>(MaxWaves);
            for (int i = 0; i < struckPool.Length; i++)
            {
                struckPool[i] = new StructList<long>(256);
                freeStruck.Add(i);
            }
        }

        // ---- The build ----

        /// <summary>Attack <paramref name="kind"/>'s rank: 0 not owned, 1-3, <see cref="Upgrade.Evolution"/>.</summary>
        public int Rank(AttackKind kind) => attackRanks[(int)kind];

        /// <summary>Mod <paramref name="kind"/>'s rank: 0 not owned, 1-2.</summary>
        public int Rank(ModKind kind) => modRanks[(int)kind];

        /// <summary>The attacks owned, of <see cref="Upgrade.AttackSlots"/>.</summary>
        public int Attacks => Owned(attackRanks);

        /// <summary>The mods owned, of <see cref="Upgrade.ModSlots"/>.</summary>
        public int Mods => Owned(modRanks);

        /// <summary>Is the hero holding a ready stance: Sword Slash's latest swing skipped with nothing in reach?</summary>
        public bool Ready { get; private set; }

        /// <summary>The enemies the hero has killed.</summary>
        public int Kills { get; private set; }

        /// <summary>The dash's cooldown from its rules' <paramref name="baseS"/> (Quickstep's).</summary>
        public double DashCooldownS(double baseS)
        {
            int r = Rank(ModKind.Quickstep);
            return r > 0 ? Math.Min(baseS, mods.Quickstep.CooldownS[r - 1]) : baseS;
        }

        /// <summary>
        /// Sets an attack or mod to <paramref name="u"/>'s rank: the progression's hook (and the loadout's).
        /// A new one must fit a free slot. Emits <see cref="SimEventKind.Upgrade"/>.
        /// </summary>
        internal void Set(Simulation sim, in Upgrade u)
        {
            if (!u.Valid) throw new ArgumentException($"no such upgrade {u}");
            int[] ranks = u.Group == UpgradeGroup.Attack ? attackRanks : modRanks;
            int was = ranks[u.Id];
            if (was == 0 && Owned(ranks) >= (u.Group == UpgradeGroup.Attack ? Upgrade.AttackSlots : Upgrade.ModSlots))
                throw new InvalidOperationException($"no free slot for {u}");
            ranks[u.Id] = u.Rank;
            if (u.Group == UpgradeGroup.Mod && u.Mod == ModKind.Lifeblood && was == 0) lifebloodKills = 0;
            sim.Emit(SimEvent.Upgrade(sim.Now, u, was));
            if (u.Group == UpgradeGroup.Attack && u.Attack == AttackKind.OrbitBlades) ResizeOrbit(sim, sim.Now);
            if (u.Group == UpgradeGroup.Mod) TuneDash(sim);
        }

        static int Owned(int[] ranks)
        {
            int n = 0;
            foreach (int r in ranks) if (r > 0) n++;
            return n;
        }

        /// <summary>Does Tempo halve attack <paramref name="kind"/>'s period?</summary>
        bool Tempo(AttackKind kind)
        {
            int r = Rank(ModKind.Tempo);
            if (r == 0) return false;
            foreach (var k in mods.Tempo.Attacks[r - 1])
                if (k == kind) return true;
            return false;
        }

        /// <summary>A grid period of <paramref name="halfBeats"/>, halved by Tempo for <paramref name="kind"/> ("one step faster").</summary>
        int Every(AttackKind kind, int halfBeats) => Tempo(kind) ? Math.Max(1, halfBeats / 2) : halfBeats;

        // ---- Driven by the sim ----

        internal void Begin(Simulation sim)
        {
            grid.Begin(sim.Now);
            orbitLast = sim.Now;
            sim.Dashes.Dashed += Dashed;
        }

        /// <summary>Committed beat <paramref name="index"/> fired at <paramref name="t"/>; the grid takes it when the beat is reliable.</summary>
        internal void Beat(Simulation sim, double t, int index, bool downbeat, int bar)
        {
            if (sim.State.Beat == Reliability.Reliable) grid.Beat(t, index, downbeat, bar);
        }

        /// <summary>The beat's state changed at <paramref name="t"/>.</summary>
        internal void BeatState(double t, Reliability now) => grid.Reliable(t, now == Reliability.Reliable);

        /// <summary>The attack cadence struck at <paramref name="t"/> (on <paramref name="beat"/>, -1 on the pulse): Sword Slash's.</summary>
        internal void Cadenced(double t, int beat) => strikes.Add(new CadenceStrike { T = t, Beat = beat });

        /// <summary>Takes everything due by <paramref name="end"/>, the tick's end, in time order; moves what is in flight.</summary>
        internal void Tick(Simulation sim, double end)
        {
            grid.Flush(end);
            bool held = sim.State.Held;
            if (held) planned = false;
            while (true)
            {
                double tg = grid.Next;
                double ts = strikeHead < strikes.Count ? strikes[strikeHead].T : double.PositiveInfinity;
                int li = NextLater(end);
                double tl = li >= 0 ? later[li].T : double.PositiveInfinity;
                if (tg > end && ts > end && tl > end) break;
                if (tg <= ts && tg <= tl)
                {
                    var step = grid.Take();
                    if (!held) Step(sim, step);
                }
                else if (ts <= tl)
                {
                    var s = strikes[strikeHead++];
                    if (!held) CadenceStruck(sim, s.T, s.Beat);
                }
                else
                {
                    var l = later[li];
                    later[li] = later.Last;
                    later.RemoveLast();
                    if (!held) Run(sim, l);
                }
            }
            strikes.Clear();
            strikeHead = 0;
            if (held)
            {
                Hold(end);
                return;
            }
            AdvanceWaves(sim, end);
            AdvanceOrbit(sim, end);
            Plan(sim, end);
        }

        int NextLater(double end)
        {
            int best = -1;
            for (int i = 0; i < later.Count; i++)
            {
                ref var l = ref later[i];
                if (l.T > end) continue;
                if (best < 0 || l.T < later[best].T || (l.T == later[best].T && l.Seq < later[best].Seq)) best = i;
            }
            return best;
        }

        void AddLater(Later l)
        {
            l.Seq = laterSeq++;
            later.Add(l);
        }

        void Run(Simulation sim, in Later l)
        {
            switch (l.Kind)
            {
                case LaterKind.Copy:
                    var copy = l.Blow;
                    copy.T = l.T;
                    sim.Strikes.Land(sim, copy);
                    break;
                case LaterKind.TempoSlash:
                    TempoSlash(sim, l.T);
                    break;
                case LaterKind.PhantomSlash:
                    PhantomSlash(sim, l);
                    break;
                case LaterKind.DashEcho:
                    var path = l.Blow.Shape;
                    sim.Emit(SimEvent.DashEcho(l.T, l.Who, path.X, path.Y, path.X2, path.Y2, l.Reach));
                    break;
            }
        }

        // ---- Sword Slash ----

        SwordSlashRules.Rank SlashRank => rules.SwordSlash.Ranks[Rank(AttackKind.SwordSlash) - 1];

        /// <summary>The next strike's swing is a spin: every <see cref="SwordSlashRules.Rank.SpinEvery"/>th of the cadence's strikes.</summary>
        bool NextIsSpin
        {
            get
            {
                int every = SlashRank.SpinEvery;
                return every > 0 && cadenceStrikes % every == every - 1;
            }
        }

        /// <summary>Plans the next strike's swing once it is within the wind-up.</summary>
        void Plan(Simulation sim, double end)
        {
            if (Rank(AttackKind.SwordSlash) == 0) return;
            if (planned && end > plannedImpact + PlanTolerance) planned = false;
            if (planned) return;
            double impact = sim.Cadence.NextStrike(end + 1e-9);
            if (double.IsNaN(impact) || impact - rules.SwordSlash.WindupS > end + 1e-9) return;
            PlanSwing(sim, end, impact);
        }

        /// <summary>Chooses the swing at song time <paramref name="now"/> for its impact at <paramref name="impact"/>: its aim, or a hold.</summary>
        void PlanSwing(Simulation sim, double now, double impact)
        {
            var rank = SlashRank;
            planned = true;
            plannedImpact = impact;
            plannedSpin = NextIsSpin;
            sim.HeroWas(now, out double hx, out double hy);
            double reach = rank.ReachM * Area(rules.SwordSlash.Tags);
            double arc = plannedSpin ? 2 * Math.PI : Radians(rank.ArcDeg);
            plannedHold = !AimAt(sim, hx, hy, reach, arc, out aimX, out aimY);
            if (plannedHold)
            {
                if (Ready) return;
                Ready = true;
                sim.Emit(SimEvent.SwingHold(now, impact));
                return;
            }
            Ready = false;
            sim.Emit(SimEvent.Swing(now, impact, aimX, aimY, plannedSpin, Rank(AttackKind.SwordSlash)));
        }

        /// <summary>The cadence struck: the hero's swing (planned, or planned now), and Tempo's spectral one between.</summary>
        void CadenceStruck(Simulation sim, double t, int beat)
        {
            if (Rank(AttackKind.SwordSlash) == 0) return;
            if (!planned || Math.Abs(plannedImpact - t) > PlanTolerance) PlanSwing(sim, t, t);
            planned = false;
            cadenceStrikes++;
            if (!plannedHold)
            {
                var rank = SlashRank;
                sim.HeroWas(t, out double hx, out double hy);
                double damage = rules.SwordSlash.Damage * (plannedSpin ? rules.SwordSlash.SpinDamage : 1);
                Slash(sim, t, hx, hy, aimX, aimY, plannedSpin ? 2 * Math.PI : Radians(rank.ArcDeg), rank.ReachM, damage,
                    plannedSpin ? BlowFlags.Spin : BlowFlags.None);
            }
            if (!Tempo(AttackKind.SwordSlash)) return;
            // "One step faster on the beat grid": a strike halfway to the next, on the grid where it is known.
            int count = sim.Cadence.Beats;
            double mid = beat >= 0 && count > 0 ? sim.Beats.BeatTime(beat + 0.5 * count) : double.NaN;
            if (double.IsNaN(mid) || mid <= t) mid = t + 0.5 * sim.Cadence.PeriodS;
            AddLater(new Later { Kind = LaterKind.TempoSlash, T = mid });
        }

        void TempoSlash(Simulation sim, double t)
        {
            if (Rank(AttackKind.SwordSlash) == 0) return;
            var rank = SlashRank;
            sim.HeroWas(t, out double hx, out double hy);
            double arc = Radians(rank.ArcDeg);
            if (!AimAt(sim, hx, hy, rank.ReachM * Area(rules.SwordSlash.Tags), arc, out double ax, out double ay)) return;
            Slash(sim, t, hx, hy, ax, ay, arc, rank.ReachM, rules.SwordSlash.Damage, BlowFlags.Spectral);
        }

        /// <summary>A Sword Slash strike: its blow, Tempest's wave at the evolution, and the phantoms' repeat a beat on.</summary>
        void Slash(Simulation sim, double t, double x, double y, double dirX, double dirY, double arc, double reach, double damage, BlowFlags flags)
        {
            var s = rules.SwordSlash;
            sim.Strikes.Land(sim, Attack(BlowSource.SwordSlash, s.Tags, flags, Sector(x, y, dirX, dirY, reach, arc), t, damage));
            if (SlashRank.Tempest)
                LaunchWave(sim, t, x, y, dirX, dirY, false, s.Tempest.RangeM, s.Tempest.HalfWidthM, s.Tempest.SpeedMps, s.Tempest.Damage,
                    s.Tempest.Tags, BlowSource.Tempest, flags & BlowFlags.Spectral);
            if (Rank(AttackKind.Phantom) > 0)
                AddLater(new Later
                {
                    Kind = LaterKind.PhantomSlash, T = grid.After(t, rules.Phantom.DelayBeats), Reach = reach, Arc = arc,
                    Blow = new Blow { Damage = damage, Flags = flags & BlowFlags.Spin },
                });
        }

        /// <summary>An attack's blow: no dash's, thrown away from its shape.</summary>
        static Blow Attack(BlowSource source, HitTags tags, BlowFlags flags, in HitShape shape, double t, double damage) => new Blow
        {
            Shape = shape, T = t, Damage = damage, Tags = tags, Source = source, Flags = flags, Owner = -1, Part = -1, Throw = ThrowKind.Away,
        };

        /// <summary>A sector of <paramref name="arc"/> radians (a circle at 2π).</summary>
        static HitShape Sector(double x, double y, double dirX, double dirY, double reach, double arc) =>
            arc >= 2 * Math.PI - 1e-9 ? HitShape.Circle(x, y, reach) : HitShape.Cone(x, y, dirX, dirY, reach, 0.5 * arc);

        // ---- Phantom ----

        /// <summary>The phantoms standing beside the hero.</summary>
        public int PhantomCount => Rank(AttackKind.Phantom) > 0 ? rules.Phantom.Ranks[Rank(AttackKind.Phantom) - 1].Phantoms : 0;

        bool PhantomRepeatsDash => Rank(AttackKind.Phantom) > 0 && rules.Phantom.Ranks[Rank(AttackKind.Phantom) - 1].RepeatsDash;

        /// <summary>Each phantom repeats a Sword Slash from where it stands, aimed afresh, at its share of the damage.</summary>
        void PhantomSlash(Simulation sim, in Later slash)
        {
            int n = PhantomCount;
            if (n == 0) return;
            var p = rules.Phantom;
            var hero = sim.State.Hero;
            sim.HeroWas(slash.T, out double hx, out double hy);
            for (int k = 0; k < n; k++)
            {
                PhantomLayout.Offset(k, n, hero.FacingX, hero.FacingY, p.OffsetM, out double dx, out double dy);
                double x = hx + dx, y = hy + dy;
                if (!AimAt(sim, x, y, slash.Reach * Area(p.Tags), slash.Arc, out double ax, out double ay)) continue;
                sim.Strikes.Land(sim, Attack(BlowSource.Phantom, p.Tags, BlowFlags.Spectral | slash.Blow.Flags,
                    Sector(x, y, ax, ay, slash.Reach, slash.Arc), slash.T, slash.Blow.Damage * p.Damage));
            }
        }

        // ---- The grid's attacks ----

        void Step(Simulation sim, in AttackGrid.Step step)
        {
            int rain = Rank(AttackKind.BladeRain);
            if (rain > 0)
            {
                var k = rules.BladeRain.Ranks[rain - 1];
                if (step.Pos % Every(AttackKind.BladeRain, k.EveryHalfBeats) == 0) DropSwords(sim, step.T, k.Swords);
            }
            int crescent = Rank(AttackKind.Crescent);
            if (crescent > 0)
            {
                var k = rules.Crescent.Ranks[crescent - 1];
                if (step.Pos % Every(AttackKind.Crescent, k.EveryHalfBeats) == 0) Crescent(sim, step.T, k);
            }
        }

        /// <summary>Blade Rain: swords drop on the densest spots within reach of the hero, each its impact apart.</summary>
        void DropSwords(Simulation sim, double t, int swords)
        {
            var b = rules.BladeRain;
            sim.HeroWas(t, out double hx, out double hy);
            int found = DensestSpots(hx, hy, b.RangeM, b.ImpactM * Area(b.Tags), swords);
            for (int i = 0; i < found; i++)
                sim.Strikes.Land(sim, Attack(BlowSource.BladeRain, b.Tags, BlowFlags.Spectral, HitShape.Circle(spots[i].X, spots[i].Y, b.ImpactM), t, b.Damage));
        }

        /// <summary>Crescent: waves roll from the hero the way it moves (and back, and to the sides), or a ring.</summary>
        void Crescent(Simulation sim, double t, in CrescentRules.Rank k)
        {
            var c = rules.Crescent;
            sim.HeroWas(t, out double hx, out double hy);
            var hero = sim.State.Hero;
            double fx = hero.FacingX, fy = hero.FacingY;
            if (k.Ways == 0)
            {
                LaunchWave(sim, t, hx, hy, fx, fy, true, k.RangeM, c.HalfWidthM, c.SpeedMps, c.Damage, c.Tags, BlowSource.Crescent, BlowFlags.None);
                return;
            }
            for (int w = 0; w < k.Ways; w++)
            {
                // Forward, back, then the two sides.
                double dx = w == 0 ? fx : w == 1 ? -fx : w == 2 ? -fy : fy;
                double dy = w == 0 ? fy : w == 1 ? -fy : w == 2 ? fx : -fx;
                LaunchWave(sim, t, hx, hy, dx, dy, false, k.RangeM, c.HalfWidthM, c.SpeedMps, c.Damage, c.Tags, BlowSource.Crescent, BlowFlags.None);
            }
        }

        // ---- Dashes ----

        /// <summary>
        /// Tunes the dash rules' hooks to the mods: Quickstep's cooldown, and Momentum's 2x on the dashes'
        /// blows with its trail (and its strike again on the next beat) as parts every dash adds. The trail's
        /// 0.8x is its own, so its part holds it over Momentum's scale, which every dash part takes.
        /// </summary>
        void TuneDash(Simulation sim)
        {
            var dashes = sim.Dashes;
            dashes.CooldownS = DashCooldownS(dashes.Rules.CooldownS);
            int momentum = Rank(ModKind.Momentum);
            var m = mods.Momentum;
            dashes.DamageScale = momentum > 0 ? m.DashDamage : 1;
            dashes.ExtraParts.Clear();
            if (momentum == 0) return;
            var trail = new DashPart
            {
                Shape = ShapeKind.Path, At = PartAt.Path, When = PartWhen.Dash, Repeat = 1,
                RadiusM = m.TrailHalfWidthM, Damage = m.TrailDamage / m.DashDamage, Look = "trail",
            };
            dashes.ExtraParts.Add(trail);
            int repeats = m.Repeats[momentum - 1];
            if (repeats == 0) return;
            trail.At = PartAt.End;
            trail.When = PartWhen.NextBeat;
            trail.Repeat = repeats;
            dashes.ExtraParts.Add(trail);
        }

        /// <summary>
        /// A dash started (<see cref="Dashes.Dashed"/>): Encore's charge on the beat, and the afterimages' and
        /// phantoms' repeats of its move. Its blows are repeated as they land (<see cref="Landed"/>).
        /// </summary>
        void Dashed(Simulation sim, DashRecord dash)
        {
            copiedDash = dash.Index;
            copiedParts = 0;
            echoedParts = 0;
            var path = new Blow { Shape = HitShape.Path(dash.FromX, dash.FromY, dash.FromX + dash.DirX * dash.DistanceM, dash.FromY + dash.DirY * dash.DistanceM, 0) };
            double duration = dash.End - dash.Start;
            int after = Rank(ModKind.Afterimage);
            if (after > 0)
                foreach (double beats in mods.Afterimage.Beats[after - 1])
                    AddLater(new Later { Kind = LaterKind.DashEcho, T = grid.After(dash.Start, beats), Blow = path, Who = -1, Reach = duration });
            if (PhantomRepeatsDash)
            {
                var hero = sim.State.Hero;
                int n = PhantomCount;
                for (int k = 0; k < n; k++)
                {
                    PhantomLayout.Offset(k, n, hero.FacingX, hero.FacingY, rules.Phantom.OffsetM, out double dx, out double dy);
                    AddLater(new Later
                    {
                        Kind = LaterKind.DashEcho, T = grid.After(dash.Start, rules.Phantom.DelayBeats), Blow = Offset(path, dx, dy), Who = k, Reach = duration,
                    });
                }
            }
            int encore = Rank(ModKind.Encore);
            if (encore > 0 && dash.OnBeat)
            {
                encoreStrikes = mods.Encore.Strikes[encore - 1];
                encoreUntil = grid.After(dash.Start, mods.Encore.WithinBeats);
            }
        }

        static double Radians(double degrees) => degrees * (Math.PI / 180);
    }
}
