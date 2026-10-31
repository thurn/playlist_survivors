namespace PS.Game.Sim.Content
{
    /// <summary>The grid a weapon's shots snap to (unity-game.md, The Other Weapons).</summary>
    public enum FireGrid { Beat, Eighth }

    public enum ShapeKind { Sword, Missile, Aura, Orbit, Strike, Chain, Trail }

    /// <summary>
    /// A weapon's geometry before Area, in metres. Every kind has <see cref="RadiusM"/>: the sword's
    /// reach, a missile's or orbiting blade's body, the aura, a strike, a chain's jump range or the
    /// trail's width. The other fields belong to one kind each and are NaN elsewhere.
    /// </summary>
    public sealed class WeaponShape
    {
        public ShapeKind Kind;
        public double RadiusM;
        /// <summary>Missile: speed at projectile speed 1.</summary>
        public double SpeedMps = double.NaN;
        /// <summary>Orbit: the ring's radius at Area 1, growing with Area up to <see cref="OrbitAreaCap"/>.</summary>
        public double OrbitRadiusM = double.NaN;
        public double OrbitAreaCap = double.NaN;
        /// <summary>Orbit: angular speed at speed 1, before the beat snaps it.</summary>
        public double RadPerS = double.NaN;
        /// <summary>Sword: a heavy strike's reach and damage, as multiples of the ordinary strike's.</summary>
        public double HeavyReachScale = double.NaN;
        public double HeavyDamage = double.NaN;
        /// <summary>Sword: the heavy strike's cleave wave rings out this far past its reach, in bands,
        /// at this multiple of the strike's damage.</summary>
        public double WaveBeyondM = double.NaN;
        public double WaveBandM = double.NaN;
        public double WaveDamage = double.NaN;

        internal static WeaponShape Read(ContentReader r)
        {
            var s = new WeaponShape();
            string kind = r.String("kind");
            switch (kind)
            {
                case "sword":
                    s.Kind = ShapeKind.Sword;
                    s.HeavyReachScale = r.Number("heavyReachScale");
                    s.HeavyDamage = r.Number("heavyDamage");
                    s.WaveBeyondM = r.Number("waveBeyondM");
                    s.WaveBandM = r.Number("waveBandM");
                    s.WaveDamage = r.Number("waveDamage");
                    break;
                case "missile":
                    s.Kind = ShapeKind.Missile;
                    s.SpeedMps = r.Number("speedMps");
                    break;
                case "orbit":
                    s.Kind = ShapeKind.Orbit;
                    s.OrbitRadiusM = r.Number("orbitRadiusM");
                    s.OrbitAreaCap = r.Number("orbitAreaCap");
                    s.RadPerS = r.Number("radPerS");
                    break;
                case "aura": s.Kind = ShapeKind.Aura; break;
                case "strike": s.Kind = ShapeKind.Strike; break;
                case "chain": s.Kind = ShapeKind.Chain; break;
                case "trail": s.Kind = ShapeKind.Trail; break;
                default: throw ContentReader.Fail(r.Child("kind"), "unknown shape kind '" + kind + "'");
            }
            s.RadiusM = r.Number("radiusM");
            if (!(s.RadiusM > 0)) throw ContentReader.Fail(r.Child("radiusM"), "expected a positive radius");
            return s;
        }
    }

    /// <summary>One rank's full numbers (Vampire Survivors' WEAPON_DATA, resolved rank by rank).</summary>
    public sealed class WeaponRank
    {
        public double Power;
        public int Amount;
        public double IntervalS;
        public double RepeatS;
        public double Area;
        public double Speed;
        public double DurationS;
        public int Pierce;
        public double HitDelayS;
        public double Knockback;
        public double CritChance;
        public double CritMultiplier;
        public bool CritEnabled;
        public bool IntervalDependsOnDuration;

        internal static WeaponRank Read(ContentReader r)
        {
            var w = new WeaponRank
            {
                Power = r.Number("power"),
                Amount = r.Int("amount"),
                IntervalS = r.Number("intervalS"),
                RepeatS = r.Number("repeatS"),
                Area = r.Number("area"),
                Speed = r.Number("speed"),
                DurationS = r.Number("durationS"),
                Pierce = r.Int("pierce"),
                HitDelayS = r.Number("hitDelayS"),
                Knockback = r.Number("knockback"),
                CritChance = r.Number("critChance"),
                CritMultiplier = r.Number("critMultiplier"),
                CritEnabled = r.Bool("critEnabled"),
                IntervalDependsOnDuration = r.Bool("intervalDependsOnDuration"),
            };
            // weapons.rs's checks on every rank.
            if (w.Power <= 0 || w.Amount < 1 || w.IntervalS <= 0 || w.Area <= 0 || w.Speed < 0 || w.DurationS < 0
                || w.Knockback < 0 || w.RepeatS < 0 || w.HitDelayS < 0 || w.Pierce < 0)
                throw r.Fail("invalid combat numbers");
            return w;
        }
    }

    /// <summary>
    /// A weapon (weapons.json). Base weapons rise through their ranks one pick at a time; an
    /// evolution replaces <see cref="EvolutionOf"/> at its top rank when <see cref="Requires"/>, a
    /// passive, is owned, and comes from a chest.
    /// </summary>
    public sealed class WeaponDef
    {
        public string Id;
        public FireGrid Grid;
        /// <summary>The base weapon this evolves from, or null for a base weapon.</summary>
        public string EvolutionOf;
        /// <summary>The evolution's partner passive, or null for a base weapon.</summary>
        public string Requires;
        public WeaponShape Shape;
        /// <summary>Rank 1 first.</summary>
        public WeaponRank[] Ranks;

        public int MaxRank => Ranks.Length;
        public bool IsEvolution => EvolutionOf != null;

        /// <summary>A rank's numbers, from 1.</summary>
        public WeaponRank Rank(int rank) => Ranks[System.Math.Max(1, System.Math.Min(rank, Ranks.Length)) - 1];

        internal static WeaponDef Read(ContentReader r)
        {
            var w = new WeaponDef { Id = r.String("id") };
            string grid = r.String("grid");
            if (grid == "beat") w.Grid = FireGrid.Beat;
            else if (grid == "eighth") w.Grid = FireGrid.Eighth;
            else throw ContentReader.Fail(r.Child("grid"), "expected \"beat\" or \"eighth\"");
            w.EvolutionOf = r.StringOrNull("evolutionOf");
            w.Requires = r.StringOrNull("requires");
            var shape = r.Object("shape");
            w.Shape = WeaponShape.Read(shape);
            shape.End();
            w.Ranks = r.Objects("ranks", WeaponRank.Read);
            if (w.Ranks.Length < 1 || w.Ranks.Length > 8) throw ContentReader.Fail(r.Child("ranks"), "expected 1 to 8 ranks");
            if ((w.EvolutionOf == null) != (w.Requires == null))
                throw r.Fail("an evolution names both its base weapon and its partner passive, a base weapon neither");
            return w;
        }
    }
}
