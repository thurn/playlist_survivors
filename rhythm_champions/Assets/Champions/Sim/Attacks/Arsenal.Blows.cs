using System;

namespace Champions.Sim
{
    // The mods on every blow (by tag before it lands; echoes, repeats and the kill mods after), the effects
    // that move through the crowd (waves, orbiting swords), and the aims.
    public sealed partial class Arsenal
    {
        /// <summary>The waves in flight at most: each keeps the targets it struck.</summary>
        const int MaxWaves = 64;

        struct Burst
        {
            public double X, Y, Damage, T;
        }

        struct Wave
        {
            public double X, Y, DirX, DirY;
            public bool Ring;
            public double Range, HalfWidth, Speed, Damage, Front, Last;
            public HitTags Tags;
            public BlowSource Source;
            public BlowFlags Flags;
            /// <summary>Its list in <see cref="struckPool"/>.</summary>
            public int Struck;
        }

        struct Spot
        {
            public double X, Y;
        }

        readonly StructList<Wave> waves;
        readonly StructList<long>[] struckPool = new StructList<long>[MaxWaves];
        readonly StructList<int> freeStruck = new StructList<int>(MaxWaves);
        readonly StructList<Burst> bursts;
        bool shattering;
        readonly StructList<Spot> spots;
        // Blade Rain's candidates' neighbour counts, beside their ids in near (-1: taken or too close to a spot).
        readonly StructList<int> counts;
        int lifebloodKills;
        int encoreStrikes;
        double encoreUntil;
        // The latest dash, and its swept path parts already echoed or repeated (a bit a part): a path's
        // tick-by-tick blows are copied once, whole.
        int copiedDash = -1, copiedParts, echoedParts;

        // ---- The mods on blows (Strikes calls these) ----

        /// <summary>
        /// The mods by tag on a blow about to land: Wide Arcs' area on what carries its tag (radii and reach,
        /// as an on-beat dash's 1.5x area is "half again as wide"), Keen Edge's damage (twice that on a
        /// downbeat), and Encore's on the strikes it boosts. Momentum's is the dash rules' (<see cref="Dashes.DamageScale"/>).
        /// </summary>
        internal void Modify(ref Blow b)
        {
            if ((b.Flags & BlowFlags.Effect) == 0)
            {
                double grow = Area(b.Tags);
                b.Shape.Radius *= grow;
                b.Shape.Inner *= grow;
            }
            double scale = 1;
            int keen = Rank(ModKind.KeenEdge);
            if (keen > 0 && (b.Tags & mods.KeenEdge.Tag) != 0)
            {
                scale *= mods.KeenEdge.Damage[keen - 1];
                if (grid.OnDownbeat(b.T)) scale *= mods.KeenEdge.Downbeat[keen - 1];
            }
            // Encore boosts the strikes after the dash, not the dash's own, and never an echo.
            if (encoreStrikes > 0 && (b.Tags & (HitTags.Strike | HitTags.Dash)) == HitTags.Strike && (b.Flags & BlowFlags.Echo) == 0)
            {
                if (b.T > encoreUntil + 1e-9) encoreStrikes = 0;
                else
                {
                    scale *= mods.Encore.Damage;
                    encoreStrikes--;
                }
            }
            b.Damage *= scale;
        }

        /// <summary>
        /// A blow landed (<paramref name="original"/> as dealt, <paramref name="landed"/> after the mods) and struck
        /// <paramref name="struck"/> targets: an attack's or a copy's <see cref="SimEventKind.Strike"/>, Twin
        /// Strike's echo, the dash repeats, and the shards of what it killed.
        /// </summary>
        internal void Landed(Simulation sim, in Blow original, in Blow landed, int struck)
        {
            if ((landed.Flags & BlowFlags.Effect) == 0 && (landed.Source != BlowSource.Dash || (landed.Flags & (BlowFlags.Repeat | BlowFlags.Echo)) != 0))
                sim.Emit(SimEvent.Strike(landed.T, landed, struck));
            Echo(sim, original);
            RepeatDash(sim, original);
            Shatter(sim);
        }

        /// <summary>An enemy died to <paramref name="blow"/> at (<paramref name="x"/>, <paramref name="y"/>): Lifeblood's count, Shatterburst's shards.</summary>
        internal void Killed(Simulation sim, in Blow blow, double x, double y)
        {
            Kills++;
            int life = Rank(ModKind.Lifeblood);
            if (life > 0 && ++lifebloodKills % mods.Lifeblood.Kills == 0)
                sim.Health.Heal(sim, mods.Lifeblood.Heal[life - 1] * sim.State.Hero.MaxHealth, HealReason.Mod);
            int burst = Rank(ModKind.Shatterburst);
            if (burst > 0) bursts.Add(new Burst { X = x, Y = y, Damage = blow.Damage * mods.Shatterburst.Share[burst - 1], T = blow.T });
        }

        /// <summary>Wide Arcs' area (radii and reach grow by it) for a blow carrying <paramref name="tags"/>; 1 without it.</summary>
        double Area(HitTags tags)
        {
            int r = Rank(ModKind.WideArcs);
            return r > 0 && (tags & mods.WideArcs.Tag) != 0 ? mods.WideArcs.Area[r - 1] : 1;
        }

        /// <summary>Shatterburst: each kill's shards cut its neighbours for a share of the killing hit, and so on for what they kill.</summary>
        void Shatter(Simulation sim)
        {
            if (shattering) return;
            shattering = true;
            for (int i = 0; i < bursts.Count; i++)
            {
                var s = bursts[i];
                sim.Strikes.Land(sim, Attack(BlowSource.Shards, HitTags.None, BlowFlags.Effect,
                    HitShape.Circle(s.X, s.Y, mods.Shatterburst.RadiusM), s.T, s.Damage));
            }
            bursts.Clear();
            shattering = false;
        }

        /// <summary>Twin Strike: a strike echoes half a beat later, spectral, at a share of its damage.</summary>
        void Echo(Simulation sim, in Blow blow)
        {
            int r = Rank(ModKind.TwinStrike);
            if (r == 0 || (blow.Tags & mods.TwinStrike.Tag) == 0 || (blow.Flags & (BlowFlags.Echo | BlowFlags.Effect)) != 0) return;
            var echo = blow;
            if (!CopyOnce(sim, ref echo, ref echoedParts)) return;
            echo.Damage *= mods.TwinStrike.Damage[r - 1];
            echo.Flags |= BlowFlags.Echo | BlowFlags.Spectral;
            AddLater(new Later { Kind = LaterKind.Copy, T = grid.After(echo.T, mods.TwinStrike.AfterBeats), Blow = echo });
        }

        /// <summary>Afterimage and Phantom (rank 2): a dash's blow is repeated, path and damage, a beat or two later.</summary>
        void RepeatDash(Simulation sim, in Blow blow)
        {
            if ((blow.Tags & HitTags.Dash) == 0 || (blow.Flags & (BlowFlags.Repeat | BlowFlags.Echo)) != 0) return;
            int after = Rank(ModKind.Afterimage);
            bool afterimages = after > 0 && (blow.Tags & mods.Afterimage.Tag) != 0, phantoms = PhantomRepeatsDash;
            if (!afterimages && !phantoms) return;
            var repeat = blow;
            if (!CopyOnce(sim, ref repeat, ref copiedParts)) return;
            repeat.Flags |= BlowFlags.Repeat | BlowFlags.Spectral;
            if (afterimages)
                foreach (double beats in mods.Afterimage.Beats[after - 1])
                    AddLater(new Later { Kind = LaterKind.Copy, T = grid.After(repeat.T, beats), Blow = repeat });
            if (!phantoms) return;
            var p = rules.Phantom;
            var hero = sim.State.Hero;
            int n = PhantomCount;
            repeat.Tags |= HitTags.Summon;
            repeat.Damage *= p.Damage;
            for (int k = 0; k < n; k++)
            {
                PhantomLayout.Offset(k, n, hero.FacingX, hero.FacingY, p.OffsetM, out double dx, out double dy);
                AddLater(new Later { Kind = LaterKind.Copy, T = grid.After(repeat.T, p.DelayBeats), Blow = Offset(repeat, dx, dy) });
            }
        }

        /// <summary>
        /// Readies <paramref name="copy"/> to be copied (later by its <see cref="Blow.T"/>). A dash's path part lands
        /// tick by tick as the hero passes: its first blow is copied as the whole planned path from the dash's
        /// start and the rest not at all (a bit a part in <paramref name="done"/>). False when this part was copied
        /// already.
        /// </summary>
        bool CopyOnce(Simulation sim, ref Blow copy, ref int done)
        {
            var dash = sim.Dashes.Latest;
            if (copy.Source != BlowSource.Dash || copy.Owner != copiedDash || copy.Owner != dash.Index || copy.Part < 0 ||
                copy.Shape.Kind != ShapeKind.Path || copy.T > dash.End + 1e-9)
                return true;
            int bit = 1 << Math.Min(copy.Part, 31);
            if ((done & bit) != 0) return false;
            done |= bit;
            copy.Shape = HitShape.Path(dash.FromX, dash.FromY, dash.FromX + dash.DirX * dash.DistanceM, dash.FromY + dash.DirY * dash.DistanceM,
                copy.Shape.Radius);
            copy.T = dash.Start;
            return true;
        }

        /// <summary>The same blow moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
        static Blow Offset(Blow b, double dx, double dy)
        {
            b.Shape.X += dx;
            b.Shape.Y += dy;
            b.Shape.X2 += dx;
            b.Shape.Y2 += dy;
            return b;
        }

        // ---- Waves ----

        /// <summary>Sets off a wave from (<paramref name="x"/>, <paramref name="y"/>): along (dirX, dirY), or a ring. Beyond <see cref="MaxWaves"/> in flight, none.</summary>
        void LaunchWave(Simulation sim, double t, double x, double y, double dirX, double dirY, bool ring, double range, double halfWidth,
            double speed, double damage, HitTags tags, BlowSource source, BlowFlags flags)
        {
            if (freeStruck.Count == 0) return;
            int struck = freeStruck.Last;
            freeStruck.RemoveLast();
            double grow = Area(tags);
            var w = new Wave
            {
                X = x, Y = y, DirX = dirX, DirY = dirY, Ring = ring, Range = range * grow, HalfWidth = halfWidth * grow, Speed = speed,
                Damage = damage, Last = t, Tags = tags, Source = source, Flags = flags | BlowFlags.Effect, Struck = struck,
            };
            waves.Add(w);
            sim.Emit(SimEvent.Wave(t, source, flags, struck, x, y, ring ? double.NaN : SimMath.Atan2(dirY, dirX), w.HalfWidth, w.Range, speed));
        }

        /// <summary>Rolls each wave's front on to <paramref name="end"/>, striking once whatever it passes.</summary>
        void AdvanceWaves(Simulation sim, double end)
        {
            double slack = rules.WaveSlackM;
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                double from = Math.Max(0, w.Front - slack);
                w.Front = Math.Min(w.Range, w.Front + w.Speed * (end - w.Last));
                w.Last = end;
                waves[i] = w;
                var shape = w.Ring
                    ? HitShape.Ring(w.X, w.Y, from, w.Front)
                    : HitShape.Path(w.X + w.DirX * from, w.Y + w.DirY * from, w.X + w.DirX * w.Front, w.Y + w.DirY * w.Front, w.HalfWidth);
                var blow = Attack(w.Source, w.Tags, w.Flags, shape, end, w.Damage);
                // A straight wave throws the way it rolls; a ring outward.
                if (!w.Ring) blow.Throw = ThrowKind.Along;
                sim.Strikes.Land(sim, blow, struckPool[w.Struck]);
                if (w.Front < w.Range) continue;
                struckPool[w.Struck].Clear();
                freeStruck.Add(w.Struck);
                waves[i] = waves.Last;
                waves.RemoveLast();
            }
        }

        // ---- Orbit Blades ----

        readonly StructList<long>[] orbitStruck = new StructList<long>[OrbitBladesRules.MaxSwords];
        int orbitSwords;
        double orbitAngle, orbitLast;
        // What the latest Orbit event told the client.
        double orbitToldSpeed = double.NaN;
        int orbitToldSwords = -1;

        /// <summary>The orbiting swords now.</summary>
        public int OrbitSwords => orbitSwords;
        /// <summary>The first sword's angle (radians, from +x) at the frontier; the rest are evenly spaced after it.</summary>
        public double OrbitAngle => orbitAngle;
        /// <summary>The ring's radius at the frontier.</summary>
        public double OrbitRadius { get; private set; }

        /// <summary>Orbit Blades' swords for its rank, each striking a target once a lap.</summary>
        void ResizeOrbit(Simulation sim, double t)
        {
            int r = Rank(AttackKind.OrbitBlades);
            orbitSwords = r > 0 ? rules.OrbitBlades.Ranks[r - 1].Swords : 0;
            for (int k = 0; k < orbitSwords; k++)
            {
                if (orbitStruck[k] == null) orbitStruck[k] = new StructList<long>(256);
                orbitStruck[k].Clear();
            }
            TellOrbit(sim, t);
        }

        /// <summary>The ring's radius at <paramref name="t"/>: breathing with the beat, pushed out on downbeats, or steady.</summary>
        double OrbitRadiusAt(in OrbitBladesRules.Rank k, double t)
        {
            if (k.Breathes)
            {
                double mid = 0.5 * (k.BreatheFromM + k.BreatheToM), amp = 0.5 * (k.BreatheToM - k.BreatheFromM);
                return mid + amp * SimMath.Cos(2 * Math.PI * grid.Phase(t));
            }
            double since = (t - grid.LastDownbeatT) / grid.Period;
            if (k.Pushes && since >= 0 && since < 1) return k.RadiusM + (k.DownbeatRadiusM - k.RadiusM) * (1 - since) * (1 - since);
            return k.RadiusM;
        }

        double OrbitSpeed => 2 * Math.PI / (rules.OrbitBlades.LapBeats * grid.Period / (Tempo(AttackKind.OrbitBlades) ? 2 : 1));

        /// <summary>Turns the ring on to <paramref name="end"/>; each sword strikes what it touches once a lap.</summary>
        void AdvanceOrbit(Simulation sim, double end)
        {
            int r = Rank(AttackKind.OrbitBlades);
            if (r == 0)
            {
                orbitLast = end;
                return;
            }
            var o = rules.OrbitBlades;
            var k = o.Ranks[r - 1];
            double from = orbitAngle, to = from + OrbitSpeed * (end - orbitLast);
            orbitLast = end;
            OrbitRadius = OrbitRadiusAt(k, end);
            sim.HeroWas(end, out double hx, out double hy);
            for (int s = 0; s < orbitSwords; s++)
            {
                double offset = 2 * Math.PI * s / orbitSwords;
                // A new lap: the sword may strike again what it struck last lap.
                if (Math.Floor((to + offset) / (2 * Math.PI)) != Math.Floor((from + offset) / (2 * Math.PI))) orbitStruck[s].Clear();
                double a = to + offset;
                var shape = HitShape.Circle(hx + OrbitRadius * SimMath.Cos(a), hy + OrbitRadius * SimMath.Sin(a), o.HitRadiusM);
                sim.Strikes.Land(sim, Attack(BlowSource.OrbitBlades, o.Tags, BlowFlags.Spectral | BlowFlags.Effect, shape, end, o.Damage), orbitStruck[s]);
            }
            orbitAngle = to - 2 * Math.PI * Math.Floor(to / (2 * Math.PI));
            TellOrbit(sim, end);
        }

        /// <summary>Emits <see cref="SimEventKind.Orbit"/> when the ring's swords or speed (by more than 1%) changed since it last did.</summary>
        void TellOrbit(Simulation sim, double t)
        {
            int r = Rank(AttackKind.OrbitBlades);
            double speed = r > 0 ? OrbitSpeed : 0;
            if (orbitSwords == orbitToldSwords && Math.Abs(speed - orbitToldSpeed) <= 0.01 * Math.Abs(orbitToldSpeed)) return;
            orbitToldSwords = orbitSwords;
            orbitToldSpeed = speed;
            sim.Emit(SimEvent.Orbit(t, orbitSwords, r, orbitAngle, speed, r > 0 ? OrbitRadiusAt(rules.OrbitBlades.Ranks[r - 1], t) : 0));
        }

        /// <summary>While the world is held nothing in flight moves.</summary>
        void Hold(double end)
        {
            for (int i = 0; i < waves.Count; i++) waves[i].Last = end;
            orbitLast = end;
        }

        // ---- Aims ----

        /// <summary>
        /// The aim from (<paramref name="x"/>, <paramref name="y"/>) for an arc of <paramref name="arc"/> radians and
        /// <paramref name="reach"/>: toward the enemy in reach whose arc holds the most enemies (ties to the
        /// nearer), else the nearest within reach plus the slack; false with nothing that close. Every enemy
        /// counts once, a boss too (design.md: bosses never weighted).
        /// </summary>
        bool AimAt(Simulation sim, double x, double y, double reach, double arc, out double dirX, out double dirY)
        {
            dirX = sim.State.Hero.FacingX;
            dirY = sim.State.Hero.FacingY;
            enemies.Near(x, y, reach + rules.AimSlackM, near);
            if (near.Count == 0) return false;
            int best = -1, bestCount = 0;
            double bestDist = double.PositiveInfinity;
            int nearest = -1;
            double nearestDist = double.PositiveInfinity;
            for (int i = 0; i < near.Count; i++)
            {
                ref readonly var c = ref enemies[near[i]];
                double cx = c.X - x, cy = c.Y - y, d = Math.Sqrt(cx * cx + cy * cy);
                if (d - c.Radius < nearestDist)
                {
                    nearest = near[i];
                    nearestDist = d - c.Radius;
                }
                if (d > reach + c.Radius) continue;
                var arcOf = d > 1e-9 ? Sector(x, y, cx / d, cy / d, reach, arc) : HitShape.Circle(x, y, reach);
                int count = 0;
                for (int j = 0; j < near.Count; j++)
                {
                    ref readonly var e = ref enemies[near[j]];
                    if (arcOf.Touches(e.X, e.Y, e.Radius)) count++;
                }
                if (count > bestCount || (count == bestCount && d - c.Radius < bestDist))
                {
                    best = near[i];
                    bestCount = count;
                    bestDist = d - c.Radius;
                }
            }
            ref readonly var t = ref enemies[best >= 0 ? best : nearest];
            double tx = t.X - x, ty = t.Y - y, len = Math.Sqrt(tx * tx + ty * ty);
            if (len > 1e-9)
            {
                dirX = tx / len;
                dirY = ty / len;
            }
            return true;
        }

        /// <summary>
        /// Up to <paramref name="max"/> spots within <paramref name="range"/> of (<paramref name="x"/>, <paramref name="y"/>),
        /// densest first: enemies with the most others within <paramref name="impact"/>, ties to the nearer, each
        /// spot at least an impact from those chosen. Into <see cref="spots"/>; returns how many.
        /// </summary>
        int DensestSpots(double x, double y, double range, double impact, int max)
        {
            spots.Clear();
            enemies.Near(x, y, range, near);
            counts.Clear();
            for (int i = 0; i < near.Count; i++)
            {
                ref readonly var c = ref enemies[near[i]];
                enemies.Near(c.X, c.Y, impact, near2);
                counts.Add(near2.Count);
            }
            while (spots.Count < max)
            {
                int best = -1;
                double bestDist = double.PositiveInfinity;
                for (int i = 0; i < near.Count; i++)
                {
                    if (counts[i] < 0) continue;
                    ref readonly var c = ref enemies[near[i]];
                    double dx = c.X - x, dy = c.Y - y, d = dx * dx + dy * dy;
                    if (best < 0 || counts[i] > counts[best] || (counts[i] == counts[best] && d < bestDist))
                    {
                        best = i;
                        bestDist = d;
                    }
                }
                if (best < 0) break;
                ref readonly var s = ref enemies[near[best]];
                spots.Add(new Spot { X = s.X, Y = s.Y });
                for (int i = 0; i < near.Count; i++)
                {
                    ref readonly var e = ref enemies[near[i]];
                    double dx = e.X - s.X, dy = e.Y - s.Y;
                    if (dx * dx + dy * dy < impact * impact) counts[i] = -1;
                }
                counts[best] = -1;
            }
            return spots.Count;
        }
    }
}
