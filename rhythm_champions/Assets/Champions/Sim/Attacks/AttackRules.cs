using System;

namespace Champions.Sim
{
    /// <summary>
    /// The attacks' numbers (Content/attacks.json; design.md, "Attacks" and Decision 9). Damage is relative
    /// to Sword Slash's base hit (1x). Each attack lists its ranks 1-3 and its evolution (rank 4) in full,
    /// since every rank is a change in kind. The defaults are the design's; attacks.json holds the same.
    /// Timings on the grid count half beats (<see cref="AttackGrid"/>): 8 is a bar of four beats, 2 a beat.
    /// </summary>
    public sealed class AttackRules
    {
        public const string File = "attacks.json";

        /// <summary>
        /// With no reliable beat the attacks' grid runs on a fixed pulse: beats this long, in bars of the
        /// beat services' <see cref="BeatRules.BeatsPerBar"/> (the telegraphs' beatless beat).
        /// </summary>
        public double BeatlessBeatS = 0.5;
        /// <summary>An aim looks this far past an attack's reach: with nothing that close, the swing holds.</summary>
        public double AimSlackM = 1.0;
        /// <summary>A wave hits what its front passed in the tick, and what stands this far behind the front.</summary>
        public double WaveSlackM = 0.5;

        public SwordSlashRules SwordSlash = new SwordSlashRules();
        public OrbitBladesRules OrbitBlades = new OrbitBladesRules();
        public BladeRainRules BladeRain = new BladeRainRules();
        public CrescentRules Crescent = new CrescentRules();
        public PhantomRules Phantom = new PhantomRules();

        /// <summary>Reads attacks.json's object.</summary>
        public static AttackRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new AttackRules
            {
                BeatlessBeatS = r.Number("beatlessBeatS"),
                AimSlackM = r.Number("aimSlackM"),
                WaveSlackM = r.Number("waveSlackM"),
            };
            rules.SwordSlash = SwordSlashRules.Read(r.Object("swordSlash"));
            rules.OrbitBlades = OrbitBladesRules.Read(r.Object("orbitBlades"));
            rules.BladeRain = BladeRainRules.Read(r.Object("bladeRain"));
            rules.Crescent = CrescentRules.Read(r.Object("crescent"));
            rules.Phantom = PhantomRules.Read(r.Object("phantom"));
            r.End();
            if (!(rules.BeatlessBeatS > 0)) throw ContentReader.Fail(path + ".beatlessBeatS", "positive");
            return rules;
        }

        /// <summary>A copy, so a run's config never shares the content's.</summary>
        public AttackRules Clone()
        {
            var copy = (AttackRules)MemberwiseClone();
            copy.SwordSlash = SwordSlash.Clone();
            copy.OrbitBlades = OrbitBlades.Clone();
            copy.BladeRain = BladeRain.Clone();
            copy.Crescent = Crescent.Clone();
            copy.Phantom = Phantom.Clone();
            return copy;
        }

        /// <summary>A rank's numbers from an array of four (ranks 1-3, the evolution).</summary>
        internal static T[] Ranks<T>(ContentReader r, Func<ContentReader, T> read)
        {
            var ranks = r.Objects("ranks", read);
            if (ranks.Length != Upgrade.Evolution) throw r.Fail("ranks: ranks 1-3 and the evolution, four in all");
            return ranks;
        }

        internal static void Positive(ContentReader r, string what, double v)
        {
            if (!(v > 0)) throw r.Fail(what + ": positive");
        }

        /// <summary>A grid period in half beats: 1, 2, 4 or 8 (a bar).</summary>
        internal static int HalfBeats(ContentReader r, string key)
        {
            int n = r.Int(key);
            if (n != 1 && n != 2 && n != 4 && n != 8) throw ContentReader.Fail(r.Child(key), "1, 2, 4 or 8 half beats");
            return n;
        }
    }

    /// <summary>A wave rolling out over the ground (Crescent, Tempest), hitting each enemy once as its front passes.</summary>
    public sealed class WaveRules
    {
        public HitTags Tags = HitTags.Area;
        public double Damage = 1;
        public double RangeM = 12;
        /// <summary>Half its width across its travel.</summary>
        public double HalfWidthM = 2;
        public double SpeedMps = 24;

        internal static WaveRules Read(ContentReader r)
        {
            var w = new WaveRules
            {
                Tags = TagNames.Read(r, "tags"),
                Damage = r.Number("damage"),
                RangeM = r.Number("rangeM"),
                HalfWidthM = r.Number("halfWidthM"),
                SpeedMps = r.Number("speedMps"),
            };
            r.End();
            AttackRules.Positive(r, "rangeM", w.RangeM);
            AttackRules.Positive(r, "speedMps", w.SpeedMps);
            return w;
        }

        public WaveRules Clone() => (WaveRules)MemberwiseClone();
    }

    /// <summary>
    /// Sword Slash (design.md, "The attacks"; Decision 9): an arc on the attack cadence, aimed at the densest
    /// group in reach, holding with nothing in reach plus <see cref="AttackRules.AimSlackM"/>.
    /// </summary>
    public sealed class SwordSlashRules
    {
        public struct Rank
        {
            public double ArcDeg, ReachM;
            /// <summary>Every this many cadence strikes the swing is a spin (0: never).</summary>
            public int SpinEvery;
            /// <summary>Every slash throws a <see cref="SwordSlashRules.Tempest"/> wave.</summary>
            public bool Tempest;

            public Rank(double arcDeg, double reachM, int spinEvery, bool tempest)
            {
                ArcDeg = arcDeg;
                ReachM = reachM;
                SpinEvery = spinEvery;
                Tempest = tempest;
            }
        }

        public HitTags Tags = HitTags.Strike | HitTags.Area;
        public double Damage = 1;
        /// <summary>
        /// The swing is planned (and its aim held) this long before its impact on the beat: the wind-up of the
        /// hero's clip, which the client starts on the <see cref="SimEventKind.Swing"/> event. LightCombo01A's impact
        /// frame (0.35 s in; LightCombo01B's is 0.267 s), so both slashes play from their first frame at natural speed;
        /// the spin's flourish, longer, enters part-way (ps-6y2.20).
        /// </summary>
        public double WindupS = 0.35;
        public double SpinDamage = 1.5;
        public Rank[] Ranks =
        {
            new Rank(150, 3.0, 0, false),
            new Rank(210, 3.8, 0, false),
            new Rank(210, 3.8, 4, false),
            new Rank(210, 3.8, 4, true),
        };
        /// <summary>The evolution's crescent wave (Tempest), thrown along the aim.</summary>
        public WaveRules Tempest = new WaveRules();

        internal static SwordSlashRules Read(ContentReader r)
        {
            var s = new SwordSlashRules
            {
                Tags = TagNames.Read(r, "tags"),
                Damage = r.Number("damage"),
                WindupS = r.Number("windupS"),
                SpinDamage = r.Number("spinDamage"),
                Ranks = AttackRules.Ranks(r, k => new Rank(k.Number("arcDeg"), k.Number("reachM"), k.Int("spinEvery"), k.Bool("tempest"))),
                Tempest = WaveRules.Read(r.Object("tempest")),
            };
            r.End();
            if (s.WindupS < 0) throw r.Fail("windupS: not negative");
            foreach (var k in s.Ranks)
            {
                AttackRules.Positive(r, "ranks.reachM", k.ReachM);
                if (!(k.ArcDeg > 0 && k.ArcDeg <= 360)) throw r.Fail("ranks.arcDeg: 0 to 360");
                if (k.SpinEvery < 0) throw r.Fail("ranks.spinEvery: not negative");
            }
            return s;
        }

        public SwordSlashRules Clone()
        {
            var copy = (SwordSlashRules)MemberwiseClone();
            copy.Ranks = (Rank[])Ranks.Clone();
            copy.Tempest = Tempest.Clone();
            return copy;
        }
    }

    /// <summary>
    /// Orbit Blades: white spectral swords circling the hero, a lap per <see cref="LapBeats"/> beats, each
    /// hitting an enemy it touches once a lap.
    /// </summary>
    public sealed class OrbitBladesRules
    {
        public struct Rank
        {
            public int Swords;
            public double RadiusM;
            /// <summary>On each downbeat the ring pushes out to this and eases back over a beat (NaN: it doesn't).</summary>
            public double DownbeatRadiusM;
            /// <summary>The ring breathes between these with every beat, widest on the beat (NaN: it doesn't).</summary>
            public double BreatheFromM, BreatheToM;

            public Rank(int swords, double radiusM, double downbeatRadiusM = double.NaN, double breatheFromM = double.NaN, double breatheToM = double.NaN)
            {
                Swords = swords;
                RadiusM = radiusM;
                DownbeatRadiusM = downbeatRadiusM;
                BreatheFromM = breatheFromM;
                BreatheToM = breatheToM;
            }

            public bool Breathes => !double.IsNaN(BreatheFromM);
            public bool Pushes => !double.IsNaN(DownbeatRadiusM);
        }

        /// <summary>The most swords a rank may have.</summary>
        public const int MaxSwords = 8;

        public HitTags Tags = HitTags.Summon;
        public double Damage = 0.6;
        /// <summary>A sword touches what comes within this of its point.</summary>
        public double HitRadiusM = 0.6;
        /// <summary>A lap's beats: a bar.</summary>
        public int LapBeats = 4;
        public Rank[] Ranks =
        {
            new Rank(2, 2.5),
            new Rank(3, 2.5),
            new Rank(4, 2.5, downbeatRadiusM: 4.0),
            new Rank(6, 3.5, breatheFromM: 2.0, breatheToM: 5.0),
        };

        internal static OrbitBladesRules Read(ContentReader r)
        {
            var o = new OrbitBladesRules
            {
                Tags = TagNames.Read(r, "tags"),
                Damage = r.Number("damage"),
                HitRadiusM = r.Number("hitRadiusM"),
                LapBeats = r.Int("lapBeats"),
                Ranks = AttackRules.Ranks(r, k => new Rank(k.Int("swords"), k.Number("radiusM"), k.NumberOrNull("downbeatRadiusM"),
                    k.NumberOrNull("breatheFromM"), k.NumberOrNull("breatheToM"))),
            };
            r.End();
            if (o.LapBeats < 1) throw r.Fail("lapBeats: at least 1");
            foreach (var k in o.Ranks)
            {
                if (k.Swords < 1 || k.Swords > MaxSwords) throw r.Fail($"ranks.swords: 1 to {MaxSwords}");
                AttackRules.Positive(r, "ranks.radiusM", k.RadiusM);
                if (double.IsNaN(k.BreatheFromM) != double.IsNaN(k.BreatheToM)) throw r.Fail("ranks.breathe: both ends or neither");
            }
            return o;
        }

        public OrbitBladesRules Clone()
        {
            var copy = (OrbitBladesRules)MemberwiseClone();
            copy.Ranks = (Rank[])Ranks.Clone();
            return copy;
        }
    }

    /// <summary>Blade Rain: swords dropping on the densest spots within <see cref="RangeM"/> on the grid.</summary>
    public sealed class BladeRainRules
    {
        public struct Rank
        {
            /// <summary>The grid period, half beats (2 a beat, 8 a bar: on the downbeats).</summary>
            public int EveryHalfBeats;
            public int Swords;

            public Rank(int everyHalfBeats, int swords)
            {
                EveryHalfBeats = everyHalfBeats;
                Swords = swords;
            }
        }

        public HitTags Tags = HitTags.Area | HitTags.Summon;
        public double Damage = 1.2;
        public double RangeM = 10;
        public double ImpactM = 1.5;
        public Rank[] Ranks = { new Rank(4, 1), new Rank(2, 1), new Rank(2, 2), new Rank(8, 8) };

        internal static BladeRainRules Read(ContentReader r)
        {
            var b = new BladeRainRules
            {
                Tags = TagNames.Read(r, "tags"),
                Damage = r.Number("damage"),
                RangeM = r.Number("rangeM"),
                ImpactM = r.Number("impactM"),
                Ranks = AttackRules.Ranks(r, k => new Rank(AttackRules.HalfBeats(k, "everyHalfBeats"), k.Int("swords"))),
            };
            r.End();
            AttackRules.Positive(r, "rangeM", b.RangeM);
            AttackRules.Positive(r, "impactM", b.ImpactM);
            foreach (var k in b.Ranks)
                if (k.Swords < 1) throw r.Fail("ranks.swords: at least 1");
            return b;
        }

        public BladeRainRules Clone()
        {
            var copy = (BladeRainRules)MemberwiseClone();
            copy.Ranks = (Rank[])Ranks.Clone();
            return copy;
        }
    }

    /// <summary>Crescent: waves rolling out from the hero on the grid, the way it moves (or a ring).</summary>
    public sealed class CrescentRules
    {
        public struct Rank
        {
            /// <summary>The ways the waves roll: 1 forward, 2 front and back, 4 four ways; 0 a ring.</summary>
            public int Ways;
            public double RangeM;
            public int EveryHalfBeats;

            public Rank(int ways, double rangeM, int everyHalfBeats)
            {
                Ways = ways;
                RangeM = rangeM;
                EveryHalfBeats = everyHalfBeats;
            }
        }

        public HitTags Tags = HitTags.Area;
        public double Damage = 1;
        public double HalfWidthM = 2;
        public double SpeedMps = 20;
        public Rank[] Ranks = { new Rank(1, 10, 8), new Rank(2, 10, 8), new Rank(4, 10, 8), new Rank(0, 14, 8) };

        internal static CrescentRules Read(ContentReader r)
        {
            var c = new CrescentRules
            {
                Tags = TagNames.Read(r, "tags"),
                Damage = r.Number("damage"),
                HalfWidthM = r.Number("halfWidthM"),
                SpeedMps = r.Number("speedMps"),
                Ranks = AttackRules.Ranks(r, k => new Rank(k.Int("ways"), k.Number("rangeM"), AttackRules.HalfBeats(k, "everyHalfBeats"))),
            };
            r.End();
            AttackRules.Positive(r, "speedMps", c.SpeedMps);
            foreach (var k in c.Ranks)
            {
                if (k.Ways != 0 && k.Ways != 1 && k.Ways != 2 && k.Ways != 4) throw r.Fail("ranks.ways: 0 (a ring), 1, 2 or 4");
                AttackRules.Positive(r, "ranks.rangeM", k.RangeM);
            }
            return c;
        }

        public CrescentRules Clone()
        {
            var copy = (CrescentRules)MemberwiseClone();
            copy.Ranks = (Rank[])Ranks.Clone();
            return copy;
        }
    }

    /// <summary>
    /// Phantom: white echo knights around the hero repeating each Sword Slash (and, from rank 2, the hero's
    /// dash) <see cref="DelayBeats"/> later at <see cref="Damage"/> of the original.
    /// </summary>
    public sealed class PhantomRules
    {
        public struct Rank
        {
            public int Phantoms;
            public bool RepeatsDash;

            public Rank(int phantoms, bool repeatsDash)
            {
                Phantoms = phantoms;
                RepeatsDash = repeatsDash;
            }
        }

        public HitTags Tags = HitTags.Strike | HitTags.Area | HitTags.Summon;
        /// <summary>A repeat's share of what it repeats.</summary>
        public double Damage = 0.7;
        public double DelayBeats = 1;
        /// <summary>
        /// How far from the hero the phantoms stand, evenly around it from its left (one: at its left; two:
        /// either side; three: around it).
        /// </summary>
        public double OffsetM = 1.5;
        public Rank[] Ranks = { new Rank(1, false), new Rank(1, true), new Rank(2, true), new Rank(3, true) };

        internal static PhantomRules Read(ContentReader r)
        {
            var p = new PhantomRules
            {
                Tags = TagNames.Read(r, "tags"),
                Damage = r.Number("damage"),
                DelayBeats = r.Number("delayBeats"),
                OffsetM = r.Number("offsetM"),
                Ranks = AttackRules.Ranks(r, k => new Rank(k.Int("phantoms"), k.Bool("repeatsDash"))),
            };
            r.End();
            AttackRules.Positive(r, "delayBeats", p.DelayBeats);
            foreach (var k in p.Ranks)
                if (k.Phantoms < 1 || k.Phantoms > PhantomLayout.Max) throw r.Fail($"ranks.phantoms: 1 to {PhantomLayout.Max}");
            return p;
        }

        public PhantomRules Clone()
        {
            var copy = (PhantomRules)MemberwiseClone();
            copy.Ranks = (Rank[])Ranks.Clone();
            return copy;
        }
    }

    /// <summary>Tags in the content: an array of <see cref="HitTags"/> names.</summary>
    static class TagNames
    {
        public static HitTags Read(ContentReader r, string key)
        {
            var names = r.Strings(key);
            var tags = HitTags.None;
            for (int i = 0; i < names.Length; i++) tags |= One(r, r.Child(key) + "[" + i + "]", names[i]);
            return tags;
        }

        public static HitTags ReadOne(ContentReader r, string key) => One(r, r.Child(key), r.String(key));

        static HitTags One(ContentReader r, string path, string name)
        {
            if (name.Length > 0 && char.IsLetter(name[0]) && Enum.TryParse(name, false, out HitTags tag) && tag != HitTags.None && Enum.IsDefined(typeof(HitTags), tag))
                return tag;
            throw ContentReader.Fail(path, "a tag: Strike, Area, Dash, Summon, Kill or Attacks");
        }
    }
}
