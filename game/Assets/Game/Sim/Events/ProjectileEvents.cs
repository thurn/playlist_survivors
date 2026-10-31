namespace PS.Game.Sim
{
    /// <summary>A projectile event's phase (<see cref="SimEvent.Projectile"/>).</summary>
    public enum ProjectilePhase
    {
        /// <summary>A new projectile leaves the hero.</summary>
        Launch = 0,
        /// <summary>It turns at its from point for a new target: its previous leg ends there, now.</summary>
        Turn = 1,
        /// <summary>It is gone, at its from point.</summary>
        End = 2,
    }

    public partial struct SimEvent
    {
        const int WeaponShift = AttackEvent.WeaponShift;

        /// <summary>
        /// A leg of a weapon's projectile (Magic Wand's crystal bolt): I0 the projectile, I1 the enemy it
        /// flies at (-1 none), I2 its <see cref="ProjectilePhase"/> | the weapon's content index &lt;&lt; 4;
        /// D0/D1 where the leg starts at T, D2/D3 where it is aimed to arrive, D4 its arrival time, D5 its
        /// body radius. It flies straight at even speed; the hit, if any, is the enemy's hit event at the
        /// arrival, where the enemy stands then. An End's from and aim are where it ends, D4 its T.
        /// </summary>
        public static SimEvent Projectile(double t, int weapon, int projectile, ProjectilePhase phase, int target,
            double fromX, double fromY, double toX, double toY, double arrive, double radius)
        {
            var e = Of(SimEventKind.Projectile, t);
            e.I0 = projectile;
            e.I1 = target;
            e.I2 = (int)phase | (weapon << WeaponShift);
            e.D0 = fromX;
            e.D1 = fromY;
            e.D2 = toX;
            e.D3 = toY;
            e.D4 = arrive;
            e.D5 = radius;
            return e;
        }

        /// <summary>
        /// A weapon's ring of blades around the hero (King Bible's crystal shards) starts, or is cut short
        /// with its new end: I0 the ring, I1 its blades, I2 1 when its speed is snapped to the beat |
        /// the weapon's content index &lt;&lt; 4; D0 born, D1 its end (it stops hitting then, and its blades
        /// shrink away over <see cref="KingBible.FadeS"/>), D2 angular speed (radians a second,
        /// counterclockwise in x, y), D3 the ring's radius, D4 a blade's body radius at full size. Blade i
        /// stands at angle <c>D2 (t - born) + 2 pi i / blades</c> from the hero, its body growing in over
        /// <see cref="KingBible.GrowS"/>.
        /// </summary>
        public static SimEvent Orbit(double t, int weapon, int ring, int blades, bool snapped, double born, double end,
            double omega, double radius, double blade)
        {
            var e = Of(SimEventKind.Orbit, t);
            e.I0 = ring;
            e.I1 = blades;
            e.I2 = (snapped ? 1 : 0) | (weapon << WeaponShift);
            e.D0 = born;
            e.D1 = end;
            e.D2 = omega;
            e.D3 = radius;
            e.D4 = blade;
            return e;
        }

        public ProjectileEvent AsProjectile { get { Expect(SimEventKind.Projectile); return new ProjectileEvent(this); } }
        public OrbitEvent AsOrbit { get { Expect(SimEventKind.Orbit); return new OrbitEvent(this); } }
    }

    public readonly struct ProjectileEvent
    {
        readonly SimEvent e;
        public ProjectileEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Projectile => e.I0;
        public int Target => e.I1;
        public ProjectilePhase Phase => (ProjectilePhase)(e.I2 & 3);
        public int Weapon => e.I2 >> AttackEvent.WeaponShift;
        public double FromX => e.D0;
        public double FromY => e.D1;
        public double ToX => e.D2;
        public double ToY => e.D3;
        public double Arrive => e.D4;
        public double Radius => e.D5;
    }

    public readonly struct OrbitEvent
    {
        readonly SimEvent e;
        public OrbitEvent(in SimEvent e) { this.e = e; }
        public double T => e.T;
        public int Ring => e.I0;
        public int Blades => e.I1;
        public bool Snapped => (e.I2 & 1) != 0;
        public int Weapon => e.I2 >> AttackEvent.WeaponShift;
        public double Born => e.D0;
        public double End => e.D1;
        public double Omega => e.D2;
        public double Radius => e.D3;
        public double Blade => e.D4;
    }
}
