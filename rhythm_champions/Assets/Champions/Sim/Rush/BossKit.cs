using System;

namespace Champions.Sim
{
    /// <summary>
    /// The bosses' shared building blocks (boss-rush.md, "The common language"). Two layers:
    ///
    /// - <b>Shapes</b>: the main game's moves' ground, as pure functions; <see cref="Bosses"/> draws its own moves with
    ///   them, so a Boss Rush boss reusing one lands exactly where the main game's would.
    /// - <b>Pieces</b>: whole attacks laid in a <see cref="BossFight"/>, each one attack (one <see cref="BossFight.NextAttack"/>),
    ///   planned on the beat by <see cref="BossFight.Plan"/> and returning that attack's number.
    ///
    /// <code>
    /// int a = BossKit.Stomp(f, body: 0, radiusM: 6, damage: 20, beats: 3);  // a disc round the body, on a downbeat
    /// BossKit.Volley(f, 0, count: 12, speedMps: 9, radiusM: 0.5, rangeM: 24, damage: 10, beats: 3, turn: 0.2); // a ring of shots
    /// if (!f.Pending(a)) ...                                                     // the stomp is done
    /// </code>
    /// </summary>
    public static class BossKit
    {
        // ---- Shapes (the main game's moves) ----

        /// <summary>The Ogre's smash: a line from the body's front (half its radius out) to <paramref name="lengthM"/> past its edge.</summary>
        public static HitShape SmashLine(double x, double y, double aimX, double aimY, double bodyR, double lengthM, double widthM)
        {
            double near = bodyR * 0.5, far = bodyR + lengthM;
            return HitShape.Path(x + aimX * near, y + aimY * near, x + aimX * far, y + aimY * far, widthM);
        }

        /// <summary>The Ogre's charge: its body's width along <paramref name="lengthM"/> of its aim.</summary>
        public static HitShape ChargeLine(double x, double y, double aimX, double aimY, double lengthM, double bodyR) =>
            HitShape.Path(x, y, x + aimX * lengthM, y + aimY * lengthM, bodyR);

        /// <summary>
        /// Boulder <paramref name="k"/> of <paramref name="count"/>: the first on (<paramref name="hx"/>, <paramref name="hy"/>),
        /// the rest evenly round it at <paramref name="spreadM"/>, from angle <paramref name="turnFrom"/>.
        /// </summary>
        public static HitShape Boulder(double hx, double hy, int k, int count, double turnFrom, double spreadM, double radiusM)
        {
            if (k == 0) return HitShape.Circle(hx, hy, radiusM);
            double a = turnFrom + 2 * Math.PI * (k - 1) / (count - 1);
            return HitShape.Circle(hx + SimMath.Cos(a) * spreadM, hy + SimMath.Sin(a) * spreadM, radiusM);
        }

        /// <summary>Fan lane <paramref name="k"/> (-1, 0, 1): narrow cones 0.65 rad apart with walking-width gaps, turned by <paramref name="offset"/>.</summary>
        public static HitShape Fan(double x, double y, double facing, int k, double offset, double rangeM, double halfArc)
        {
            double a = facing + k * 0.65 + offset;
            return HitShape.Cone(x, y, SimMath.Cos(a), SimMath.Sin(a), rangeM, halfArc);
        }

        // ---- Pieces ----

        static void Aim(BossFight f, int body, out double ax, out double ay, out double hx, out double hy)
        {
            ref var b = ref f.Body(body);
            f.HeroAt(f.Now, out hx, out hy);
            double dx = hx - b.X, dy = hy - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            ax = d > 1e-9 ? dx / d : SimMath.Cos(b.Facing);
            ay = d > 1e-9 ? dy / d : SimMath.Sin(b.Facing);
            b.Facing = SimMath.Atan2(ay, ax);
        }

        static int Lay(BossFight f, int body, BossMove move, int part, in Telegraph tel, in HitShape shape, double damage, int attack,
            HazardFlags flags = HazardFlags.None)
        {
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Part = part, Move = move, Telegraph = tel, Shape = shape, Damage = damage, Flags = flags,
            });
            return attack;
        }

        /// <summary>The smash: a line at the hero from the body's front.</summary>
        public static int Smash(BossFight f, int body, double lengthM, double widthM, double damage, int beats)
        {
            Aim(f, body, out double ax, out double ay, out _, out _);
            ref readonly var b = ref f.Bodies[body];
            return Lay(f, body, BossMove.Smash, 0, f.Plan(beats), SmashLine(b.X, b.Y, ax, ay, f.TypeOf(body).RadiusM, lengthM, widthM), damage, f.NextAttack());
        }

        /// <summary>The stomp: a disc round the body, landing on a downbeat.</summary>
        public static int Stomp(BossFight f, int body, double radiusM, double damage, int beats)
        {
            ref readonly var b = ref f.Bodies[body];
            return Lay(f, body, BossMove.Stomp, 0, f.Plan(beats, Landing.Downbeat), HitShape.Circle(b.X, b.Y, radiusM), damage, f.NextAttack());
        }

        /// <summary>The boulders: a circle on the hero and the rest round it, landing together.</summary>
        public static int Boulders(BossFight f, int body, int count, double radiusM, double spreadM, double damage, int beats)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            var tel = f.Plan(beats);
            int attack = f.NextAttack();
            double turnFrom = f.Random.NextDouble() * 2 * Math.PI;
            for (int k = 0; k < count; k++) Lay(f, body, BossMove.Boulders, k, tel, Boulder(hx, hy, k, count, turnFrom, spreadM, radiusM), damage, attack);
            return attack;
        }

        /// <summary>The charge: a line through the hero; on its beat the body runs it over <paramref name="runS"/>.</summary>
        public static int Charge(BossFight f, int body, double lengthM, double runS, double damage, int beats)
        {
            Aim(f, body, out double ax, out double ay, out _, out _);
            ref readonly var b = ref f.Bodies[body];
            var shape = ChargeLine(b.X, b.Y, ax, ay, lengthM, f.TypeOf(body).RadiusM);
            int attack = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Move = BossMove.Charge, Telegraph = f.Plan(beats), Shape = shape, Damage = damage,
                BodyMove = HazardBodyMove.AtLand, ToX = shape.X2, ToY = shape.Y2, MoveS = runS,
            });
            return attack;
        }

        /// <summary>The dive: the hero's spot marked as the body takes off; it flies there across the telegraph and lands on it.</summary>
        public static int Dive(BossFight f, int body, double radiusM, double damage, int beats)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            int attack = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Move = BossMove.Dive, Telegraph = f.Plan(beats), Shape = HitShape.Circle(hx, hy, radiusM),
                Damage = damage, BodyMove = HazardBodyMove.DuringTelegraph, ToX = hx, ToY = hy,
            });
            return attack;
        }

        /// <summary>
        /// A burning cone from the body at the hero that sweeps <paramref name="sweepRad"/> across <paramref name="burnBeats"/>
        /// (0: still); its telegraph covers the cone's start. A breath, a laser fan.
        /// </summary>
        public static int Breath(BossFight f, int body, double rangeM, double halfArc, double sweepRad, int burnBeats, double damage, int beats)
        {
            Aim(f, body, out double ax, out double ay, out _, out _);
            ref readonly var b = ref f.Bodies[body];
            double a = b.Facing - sweepRad / 2, burn = burnBeats * f.BeatS;
            int attack = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Move = BossMove.Breath, Telegraph = f.Plan(beats),
                Shape = HitShape.Cone(b.X, b.Y, SimMath.Cos(a), SimMath.Sin(a), rangeM, halfArc), Damage = damage,
                ActiveS = burn, SpinRadPerS = burn > 0 ? sweepRad / burn : 0,
            });
            return attack;
        }

        /// <summary>
        /// A beam: a line of <paramref name="lengthM"/> from (<paramref name="x"/>, <paramref name="y"/>) at
        /// <paramref name="angle"/>, turning <paramref name="spinRadPerS"/> for <paramref name="activeS"/> after it lands
        /// (a clock-hand laser, a spiral arm).
        /// </summary>
        public static int Beam(BossFight f, int body, double x, double y, double angle, double lengthM, double widthM, double spinRadPerS,
            double activeS, double damage, int beats, Landing landing = Landing.Beat, int attack = -1)
        {
            if (attack < 0) attack = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Telegraph = f.Plan(beats, landing),
                Shape = HitShape.Path(x, y, x + SimMath.Cos(angle) * lengthM, y + SimMath.Sin(angle) * lengthM, widthM),
                Damage = damage, ActiveS = activeS, SpinRadPerS = spinRadPerS,
            });
            return attack;
        }

        /// <summary>
        /// A shockwave ring from (<paramref name="x"/>, <paramref name="y"/>) growing at <paramref name="speedMps"/> to
        /// <paramref name="radiusM"/>, <paramref name="thickM"/> thick, landing on a downbeat; <paramref name="fronts"/> rings in
        /// turn, spaced the dash cooldown and a beat apart (boss-rush.md, "Fair fights"). A dash wave unless walking outruns it.
        /// <paramref name="move"/> is how it presents (its body's clip); none: a plain hazard.
        /// </summary>
        public static int Wave(BossFight f, int body, double x, double y, double radiusM, double speedMps, double thickM, double damage, int beats,
            bool dashWave = true, int fronts = 1, int attack = -1, HazardFlags flags = HazardFlags.None, BossMove move = BossMove.None)
        {
            if (attack < 0) attack = f.NextAttack();
            var tel = f.Plan(beats, Landing.Downbeat);
            double beatS = BeatOf(f, tel);
            int spacing = (int)Math.Ceiling(f.Sim.Dashes.Rules.CooldownS / beatS - 1e-9) + 1;
            for (int k = 0; k < fronts; k++)
            {
                var front = k == 0 ? tel : new Telegraph(tel.Start, tel.Land + k * spacing * beatS, tel.StartBeat, tel.LandBeat + k * spacing, true);
                f.Add(new Hazard
                {
                    Attack = attack, Body = body, Part = k, Move = move, Telegraph = front, Shape = HitShape.Ring(x, y, 0, thickM),
                    Damage = damage, ActiveS = radiusM / speedMps, GrowMps = speedMps, Flags = flags | (dashWave ? HazardFlags.DashWave : HazardFlags.None),
                });
            }
            return attack;
        }

        /// <summary>
        /// A wall front: a line <paramref name="halfWidthM"/> either side of the hero, starting below it far enough that
        /// walking can't outrun it, sweeping up the screen at <paramref name="speedMps"/> (at least 18) for
        /// <paramref name="activeS"/>. Always a dash wave.
        /// </summary>
        public static int Wall(BossFight f, int body, double halfWidthM, double speedMps, double activeS, double damage, int beats)
        {
            f.HeroAt(f.Now, out double hx, out double hy);
            var tel = f.Plan(beats);
            double y = hy - (tel.Land - f.Now) * f.HeroSpeedMps - 2;
            int attack = f.NextAttack();
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Telegraph = tel, Shape = HitShape.Path(hx - halfWidthM, y, hx + halfWidthM, y, 0.6),
                Damage = damage, ActiveS = activeS, Vy = Math.Max(18, speedMps), Flags = HazardFlags.DashWave,
            });
            return attack;
        }

        /// <summary>A beat's length by <paramref name="tel"/>'s own beats, or the fight's beat now.</summary>
        static double BeatOf(BossFight f, in Telegraph tel) => tel.OnBeat && tel.Beats > 0 ? (tel.Land - tel.Start) / tel.Beats : f.BeatS;

        /// <summary>
        /// One projectile launched from (<paramref name="x"/>, <paramref name="y"/>) on its telegraph's beat toward
        /// <paramref name="angle"/>, travelling continuously for <paramref name="rangeM"/>.
        /// </summary>
        public static int Shot(BossFight f, int body, double x, double y, double angle, double speedMps, double radiusM, double rangeM,
            double damage, in Telegraph tel, int attack, int part = 0)
        {
            f.Add(new Hazard
            {
                Attack = attack, Body = body, Part = part, Telegraph = tel, Shape = HitShape.Circle(x, y, radiusM), Damage = damage,
                ActiveS = rangeM / speedMps, Vx = SimMath.Cos(angle) * speedMps, Vy = SimMath.Sin(angle) * speedMps,
                Flags = HazardFlags.Projectile,
            });
            return attack;
        }

        /// <summary>A ring of <paramref name="count"/> shots from the body, evenly spaced from angle <paramref name="turn"/>, launching together.</summary>
        public static int Volley(BossFight f, int body, int count, double speedMps, double radiusM, double rangeM, double damage, int beats,
            double turn = 0, Landing landing = Landing.Beat)
        {
            ref readonly var b = ref f.Bodies[body];
            var tel = f.Plan(beats, landing);
            int attack = f.NextAttack();
            for (int k = 0; k < count; k++)
                Shot(f, body, b.X, b.Y, turn + 2 * Math.PI * k / count, speedMps, radiusM, rangeM, damage, tel, attack, k);
            return attack;
        }

        /// <summary>The fans: three narrow lanes at the hero with walking-width gaps, alternating by <paramref name="volley"/>.</summary>
        public static int Fans(BossFight f, int body, double rangeM, double halfArc, double damage, int beats, int volley)
        {
            Aim(f, body, out _, out _, out _, out _);
            ref readonly var b = ref f.Bodies[body];
            var tel = f.Plan(beats);
            int attack = f.NextAttack();
            double offset = volley % 2 == 0 ? 0.2 : -0.2;
            for (int k = -1; k <= 1; k++) Lay(f, body, BossMove.Fans, k + 1, tel, Fan(b.X, b.Y, b.Facing, k, offset, rangeM, halfArc), damage, attack);
            return attack;
        }
    }
}
