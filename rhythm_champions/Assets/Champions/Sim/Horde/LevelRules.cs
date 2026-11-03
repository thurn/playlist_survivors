using System;

namespace Champions.Sim
{
    /// <summary>A number over the run clock: points (seconds, value) joined by straight lines, held past both ends.</summary>
    public sealed class Curve
    {
        public readonly double[] Ts, Vs;

        public Curve(params (double T, double V)[] points)
        {
            if (points.Length == 0) throw new ArgumentException("a curve needs a point");
            Ts = new double[points.Length];
            Vs = new double[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Ts[i] = points[i].T;
                Vs[i] = points[i].V;
            }
        }

        public double At(double t)
        {
            if (t <= Ts[0]) return Vs[0];
            for (int i = 1; i < Ts.Length; i++)
            {
                if (t > Ts[i]) continue;
                double u = (t - Ts[i - 1]) / (Ts[i] - Ts[i - 1]);
                return Vs[i - 1] + (Vs[i] - Vs[i - 1]) * u;
            }
            return Vs[Vs.Length - 1];
        }

        /// <summary>Reads an array of [seconds, value] pairs, the seconds ascending.</summary>
        internal static Curve Read(ContentReader r, string key)
        {
            var items = r.Array(key);
            var points = new (double, double)[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                string at = r.Child(key) + "[" + i + "]";
                if (p.Type != JsonType.Array || p.Items.Count != 2 || p.Items[0].Type != JsonType.Number || p.Items[1].Type != JsonType.Number)
                    throw ContentReader.Fail(at, "expected [seconds, value]");
                points[i] = (p.Items[0].Number, p.Items[1].Number);
                if (i > 0 && !(points[i].Item1 > points[i - 1].Item1)) throw ContentReader.Fail(at, "seconds ascending");
            }
            if (points.Length == 0) throw ContentReader.Fail(r.Child(key), "at least one point");
            return new Curve(points);
        }
    }

    /// <summary>One type in the spawner's mix: from when on the run clock it joins, and its weight among those joined.</summary>
    public struct MixEntry
    {
        /// <summary>Its cast index (<see cref="EnemyRules.Cast"/>).</summary>
        public int Type;
        public double FromS;
        /// <summary>A pick's weight; a pick spawns the type's whole group (<see cref="EnemyType.Group"/>).</summary>
        public double Weight;
    }

    /// <summary>A boss on the level's timeline: its cast index, and the run clock that calls it (spawning stops; it arrives on its downbeat).</summary>
    public struct LevelBoss
    {
        public int Cast;
        public double CallAtS;
    }

    /// <summary>The formed waves' shapes (design.md, "Spawning").</summary>
    public enum WaveKind : byte
    {
        /// <summary>A ring closing on the hero.</summary>
        Ring = 0,
        /// <summary>A line sweeping across, broadside.</summary>
        Line = 1,
        /// <summary>A swarm of bats.</summary>
        Swarm = 2,
    }

    /// <summary>
    /// Level 1's spawner (Content/level1.json; design.md, "Spawning", "Toughness" and Decision 17): the ramp
    /// from the opening density to the late-game density from a ring just off-screen, the toughness ramp, the mix as types
    /// join, loud surges toward the cap of 1,000 and quiet thinning, and formed waves on strong section
    /// changes, and its bosses' calls (<see cref="Bosses"/>: each stops the spawner, and the Ogre's death resumes it with
    /// the second half's mix). Seconds are horde time (run time in archived fixtures).
    /// </summary>
    public sealed class LevelRules
    {
        public const string File = "level1.json";

        // Where and how fast.
        /// <summary>Spawns come from an ellipse around the hero this far out across and up the screen, just off-screen.</summary>
        public double RingXM = 21, RingYM = 14;
        /// <summary>Up to this much further out.</summary>
        public double RingJitterM = 3;
        /// <summary>At most this many spawns a second.</summary>
        public double PerS = 60;
        /// <summary>A mob this far from the hero has been left behind: it goes, and the ring brings a new one.</summary>
        public double DespawnM = 45;
        /// <summary>Never more mobs alive than this.</summary>
        public int Cap = 1000;
        /// <summary>The mobs alive the spawner keeps, over the run clock.</summary>
        public Curve Ramp = new Curve((0, 150), (720, 600));
        /// <summary>After the Ogre, the ramp times this (design.md: "denser").</summary>
        public double SecondHalfDensity = 1.15;

        /// <summary>
        /// Fodder health in Sword Slash's base hits over the run clock: one hit until the first level-up's time,
        /// four by minute 12.
        /// </summary>
        public Curve Toughness = new Curve((0, 1), (90, 1), (720, 3.5));

        /// <summary>The first half's mix, and the second half's (after the Ogre: more elites, mushrooms).</summary>
        public MixEntry[] Mix = new MixEntry[0];
        public MixEntry[] SecondHalfMix = new MixEntry[0];

        // Surges: the analyzer's loudness level, smoothed.
        /// <summary>The loud and quiet shares follow the music over about this long.</summary>
        public double SurgeSmoothS = 4;
        /// <summary>A fully loud stretch raises the target by this share of the ramp (never past the cap).</summary>
        public double SurgeStrength = 0.6;
        /// <summary>A fully quiet stretch scales the target by this.</summary>
        public double QuietScale = 0.6;

        // Formed waves.
        /// <summary>A section change this strong or stronger brings a wave on the next downbeat.</summary>
        public double WaveMinStrength = 0;
        /// <summary>At least this long between waves.</summary>
        public double WaveMinGapS = 20;
        public int RingCount = 40;
        public double RingRadiusM = 20;
        public int LineCount = 30;
        public double LineLengthM = 36;
        /// <summary>A line starts this far from the hero and marches across for twice that.</summary>
        public double LineOffsetM = 22;
        public int SwarmCount = 48;
        public double SwarmRadiusM = 3;

        /// <summary>
        /// The bosses in order and the horde clock calling each (current content: 3/6/9 minutes); a boss is called only once the one before it is dead.
        /// </summary>
        public LevelBoss[] Bosses = new LevelBoss[0];

        /// <summary>Reads level1.json's object; the mixes name types of <paramref name="enemies"/>.</summary>
        public static LevelRules Read(JsonValue json, string path, EnemyRules enemies)
        {
            var r = new ContentReader(json, path);
            var rules = new LevelRules();
            var spawn = r.Object("spawn");
            rules.RingXM = spawn.Number("ringXM");
            rules.RingYM = spawn.Number("ringYM");
            rules.RingJitterM = spawn.Number("ringJitterM");
            rules.PerS = spawn.Number("perS");
            rules.DespawnM = spawn.Number("despawnM");
            rules.Cap = spawn.Int("cap");
            rules.Ramp = Curve.Read(spawn, "ramp");
            rules.SecondHalfDensity = spawn.Number("secondHalfDensity");
            spawn.End();
            rules.Toughness = Curve.Read(r, "toughness");
            rules.Mix = r.Objects("mix", m => ReadMix(m, enemies));
            rules.SecondHalfMix = r.Objects("secondHalfMix", m => ReadMix(m, enemies));
            var surge = r.Object("surge");
            rules.SurgeSmoothS = surge.Number("smoothS");
            rules.SurgeStrength = surge.Number("strength");
            rules.QuietScale = surge.Number("quietScale");
            surge.End();
            var waves = r.Object("waves");
            rules.WaveMinStrength = waves.Number("minStrength");
            rules.WaveMinGapS = waves.Number("minGapS");
            var ring = waves.Object("ring");
            rules.RingCount = ring.Int("count");
            rules.RingRadiusM = ring.Number("radiusM");
            ring.End();
            var line = waves.Object("line");
            rules.LineCount = line.Int("count");
            rules.LineLengthM = line.Number("lengthM");
            rules.LineOffsetM = line.Number("offsetM");
            line.End();
            var swarm = waves.Object("swarm");
            rules.SwarmCount = swarm.Int("count");
            rules.SwarmRadiusM = swarm.Number("radiusM");
            swarm.End();
            waves.End();
            rules.Bosses = r.Objects("bosses", b => ReadBoss(b, enemies));
            r.End();
            rules.Validate(path);
            return rules;
        }

        static MixEntry ReadMix(ContentReader r, EnemyRules enemies)
        {
            string id = r.String("type");
            int type = enemies.IndexOf(id);
            if (type < 0) throw ContentReader.Fail(r.Child("type"), "no type " + id + " in " + EnemyRules.File);
            if (enemies.Cast[type].IsBoss) throw ContentReader.Fail(r.Child("type"), id + " is a boss");
            return new MixEntry { Type = type, FromS = r.Number("fromS"), Weight = r.Number("weight") };
        }

        static LevelBoss ReadBoss(ContentReader r, EnemyRules enemies)
        {
            string id = r.String("boss");
            int cast = enemies.IndexOf(id);
            if (cast < 0 || !enemies.Cast[cast].IsBoss) throw ContentReader.Fail(r.Child("boss"), id + " is no boss in " + EnemyRules.File);
            var boss = new LevelBoss { Cast = cast, CallAtS = r.Number("callAtS") };
            r.End();
            return boss;
        }

        void Validate(string path)
        {
            if (!(RingXM > 0 && RingYM > 0 && RingJitterM >= 0)) throw ContentReader.Fail(path + ".spawn", "a positive ring");
            if (!(PerS > 0)) throw ContentReader.Fail(path + ".spawn.perS", "positive");
            if (!(DespawnM > Math.Max(RingXM, RingYM) + RingJitterM)) throw ContentReader.Fail(path + ".spawn.despawnM", "beyond the ring");
            if (Cap < 1) throw ContentReader.Fail(path + ".spawn.cap", "at least 1");
            if (!(SurgeSmoothS > 0)) throw ContentReader.Fail(path + ".surge.smoothS", "positive");
            foreach (var mix in new[] { Mix, SecondHalfMix })
                foreach (var m in mix)
                    if (!(m.Weight >= 0)) throw ContentReader.Fail(path, "mix weights at least 0");
            for (int i = 1; i < Bosses.Length; i++)
                if (!(Bosses[i].CallAtS > Bosses[i - 1].CallAtS)) throw ContentReader.Fail(path + ".bosses", "called in order");
        }

        /// <summary>A copy, so a run's config never shares the content's.</summary>
        public LevelRules Clone()
        {
            var copy = (LevelRules)MemberwiseClone();
            copy.Mix = (MixEntry[])Mix.Clone();
            copy.SecondHalfMix = (MixEntry[])SecondHalfMix.Clone();
            copy.Bosses = (LevelBoss[])Bosses.Clone();
            return copy;
        }
    }
}
