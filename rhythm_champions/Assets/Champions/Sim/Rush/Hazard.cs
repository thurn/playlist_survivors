using System;

namespace Champions.Sim
{
    /// <summary>A hazard's flags (<see cref="Hazard.Flags"/>).</summary>
    [Flags]
    public enum HazardFlags : byte
    {
        None = 0,
        /// <summary>A dash-through wave: walking can't escape it (boss-rush.md, "The common language").</summary>
        DashWave = 1,
        /// <summary>It travels after launching (its telegraph is the launch's warning).</summary>
        Projectile = 2,
    }

    /// <summary>How a hazard moves the body that casts it (<see cref="Hazard.BodyMove"/>).</summary>
    public enum HazardBodyMove : byte
    {
        None = 0,
        /// <summary>The body flies to (<see cref="Hazard.ToX"/>, <see cref="Hazard.ToY"/>) across the telegraph, landing on its mark (a dive).</summary>
        DuringTelegraph = 1,
        /// <summary>The body runs there from the landing over <see cref="Hazard.MoveS"/> (a charge).</summary>
        AtLand = 2,
    }

    /// <summary>Where a <see cref="BossFight.Plan"/> telegraph lands.</summary>
    public enum Landing : byte
    {
        Beat = 0,
        /// <summary>Half a beat after the counted beat (off-beat impacts, boss-rush.md Decision 7).</summary>
        HalfBeat = 1,
        Downbeat = 2,
    }

    /// <summary>
    /// One damaging thing a Boss Rush boss puts on the ground (boss-rush.md, "The common language"): an orange/red shape
    /// that fills over <see cref="Telegraph"/> and lands on its beat or half beat. After landing it may linger
    /// (<see cref="ActiveS"/>: a burn, a lava pool), travel (<see cref="Vx"/>, <see cref="Vy"/>: a projectile launched on
    /// the grid), grow (<see cref="GrowMps"/>: a shockwave ring) or spin about its origin (<see cref="SpinRadPerS"/>: a
    /// spiral arm, a laser sweep). It hits the hero at most once, the first time its shape touches the hero's body from
    /// its landing on.
    ///
    /// <code>
    /// var tel = fight.Plan(3);                       // three beats, on the beat; never under the act's minimum
    /// fight.Add(new Hazard { Shape = HitShape.Circle(hx, hy, 2), Telegraph = tel, Damage = 15, Attack = attack });
    /// </code>
    /// </summary>
    public struct Hazard
    {
        /// <summary>Its mark number (<see cref="BossMark.Id"/>, its events'); <see cref="BossFight.Add"/> sets it.</summary>
        public int Id;
        /// <summary>The attack it belongs to (<see cref="BossFight.NextAttack"/>): the fairness harness checks attacks whole.</summary>
        public int Attack;
        /// <summary>The body casting it (its index in <see cref="BossFight.Bodies"/>), or -1 for the arena's own.</summary>
        public int Body;
        /// <summary>Which of its attack's marks it is (the presentation's).</summary>
        public int Part;
        /// <summary>The move it presents as: one of the main game's (<see cref="BossMove.Smash"/> ...) or <see cref="BossMove.Hazard"/>.</summary>
        public BossMove Move;
        public Telegraph Telegraph;
        /// <summary>The ground at its landing.</summary>
        public HitShape Shape;
        public double Damage;
        /// <summary>How long it hurts after landing; 0: the impact frame alone.</summary>
        public double ActiveS;
        /// <summary>Its travel after landing, m/s.</summary>
        public double Vx, Vy;
        /// <summary>A circle's or ring's growth after landing, m/s (a ring keeps its thickness).</summary>
        public double GrowMps;
        /// <summary>A cone's or line's turn about its origin after landing, radians/s.</summary>
        public double SpinRadPerS;
        public HazardFlags Flags;
        public HazardBodyMove BodyMove;
        public double ToX, ToY, MoveS;

        internal bool Landed, HitHero;

        /// <summary>The song time its last moment of danger ends.</summary>
        public readonly double Until => Telegraph.Land + ActiveS;

        public readonly bool DashWave => (Flags & HazardFlags.DashWave) != 0;

        /// <summary>Its ground at song time <paramref name="t"/>: as marked until it lands, then travelled, grown and turned.</summary>
        public readonly HitShape ShapeAt(double t)
        {
            var s = Shape;
            double dt = t - Telegraph.Land;
            if (dt <= 0) return s;
            if (Vx != 0 || Vy != 0)
            {
                double ox = Vx * dt, oy = Vy * dt;
                s.X += ox; s.Y += oy; s.X2 += ox; s.Y2 += oy;
            }
            if (GrowMps != 0 && (s.Kind == ShapeKind.Circle || s.Kind == ShapeKind.Ring))
            {
                s.Radius += GrowMps * dt;
                if (s.Kind == ShapeKind.Ring) s.Inner = Math.Max(0, s.Inner + GrowMps * dt);
            }
            if (SpinRadPerS != 0)
            {
                double a = SpinRadPerS * dt, c = Math.Cos(a), sn = Math.Sin(a);
                if (s.Kind == ShapeKind.Cone)
                {
                    double dx = s.DirX * c - s.DirY * sn, dy = s.DirX * sn + s.DirY * c;
                    s.DirX = dx; s.DirY = dy;
                }
                else if (s.Kind == ShapeKind.Path)
                {
                    double rx = s.X2 - s.X, ry = s.Y2 - s.Y;
                    s.X2 = s.X + rx * c - ry * sn;
                    s.Y2 = s.Y + rx * sn + ry * c;
                }
            }
            return s;
        }

        /// <summary>The mark its events carry.</summary>
        public readonly BossMark Mark => new BossMark { Id = Id, Move = Move, Part = Part, Telegraph = Telegraph, Shape = Shape };
    }

    /// <summary>One gem a boss sheds (boss-rush.md, "Progression"): where it lands, and its share of the burst's XP.</summary>
    public struct GemSpot
    {
        public double X, Y, Share;

        public GemSpot(double x, double y, double share = 1)
        {
            X = x;
            Y = y;
            Share = share;
        }
    }

    /// <summary>One body of a fight (<see cref="BossFight.Bodies"/>): a multi-body boss (the Wraith Council) has several, each with its own health.</summary>
    public struct FightBody
    {
        /// <summary>Its enemy id (<see cref="Simulation.Enemies"/>) and cast index (enemies.json).</summary>
        public int Enemy, Cast;
        public double X, Y, Facing;
        public double MaxHealth;
        public bool Alive;
        /// <summary>It moved this tick (the client's walk or idle clip).</summary>
        public bool Walking;
    }
}
