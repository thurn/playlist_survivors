using System;

namespace Champions.Sim
{
    /// <summary>A boss's move (design.md, "Fat Ogre" and "Dragon").</summary>
    public enum BossMove : byte
    {
        None = 0,
        /// <summary>The Ogre's overhead smash: the club along a line in front of it.</summary>
        Smash = 1,
        /// <summary>The Ogre's stomp: a shockwave ring round it, landing on a downbeat.</summary>
        Stomp = 2,
        /// <summary>The Ogre's charge: it runs the length of a long line.</summary>
        Charge = 3,
        /// <summary>The Ogre's boulders: marked circles near the hero, landing together.</summary>
        Boulders = 4,
        /// <summary>The dragon's fire breath: a cone that burns for a beat (sweeping in phase two).</summary>
        Breath = 5,
        /// <summary>The dragon's tail sweep: a ring strike round it, landing on a downbeat.</summary>
        Tail = 6,
        /// <summary>The dragon's dive: it takes off and lands on the hero's marked spot.</summary>
        Dive = 7,
        /// <summary>The dragon's fireballs: a circle marked at the hero on each of three beats, each landing in turn.</summary>
        Fireballs = 8,
    }

    /// <summary>What a boss's death sets off (design.md, "Level 1, minute by minute").</summary>
    public enum BossDeath : byte
    {
        /// <summary>Treasure, then spawning resumes with the second half's mix (the Ogre).</summary>
        Treasure = 0,
        /// <summary>The finale: everything left on screen shatters and the run is won (the dragon).</summary>
        Finale = 1,
    }

    /// <summary>One move's numbers (Content/bosses.json's <c>moves</c>). Pairs are by phase: [phase one, phase two].</summary>
    public sealed class BossMoveRules
    {
        public BossMove Move;
        /// <summary>The whole beats its telegraph fills over, by phase (phase two's are one beat but the dive's).</summary>
        public int[] TelegraphBeats = { 2, 1 };
        /// <summary>What it deals the hero.</summary>
        public double Damage;
        /// <summary>Chosen only with the hero within this of the boss's centre (its reach); infinite for a ranged move.</summary>
        public double MaxM = double.PositiveInfinity;
        /// <summary>A line's length from the boss (smash, charge), and the smash's reach either side of it.</summary>
        public double LengthM, WidthM;
        /// <summary>A circle's or ring's radius (stomp, boulders, tail, dive, fireballs).</summary>
        public double RadiusM;
        /// <summary>How many circles (boulders, fireballs), and how far the boulders beyond the first land from it.</summary>
        public int Count = 1;
        public double SpreadM;
        /// <summary>The charge's run, s.</summary>
        public double DurationS;
        /// <summary>The breath's cone: its range and half angle (radians), its sweep by phase (radians), and its burn in beats.</summary>
        public double RangeM, HalfArc;
        public double[] Sweep = { 0, 0 };
        public int BurnBeats = 1;
        /// <summary>In phase two, the move that follows this one's landing at once (the Ogre's smash then stomp; the dragon's dive twice).</summary>
        public BossMove ChainTwo;

        public int Beats(int phase) => TelegraphBeats[Math.Min(phase, 2) - 1];

        public BossMoveRules Clone()
        {
            var c = (BossMoveRules)MemberwiseClone();
            c.TelegraphBeats = (int[])TelegraphBeats.Clone();
            c.Sweep = (double[])Sweep.Clone();
            return c;
        }
    }

    /// <summary>One boss's duel (Content/bosses.json's <c>bosses</c>): its body, health and speed are its cast row's (enemies.json).</summary>
    public sealed class BossType
    {
        /// <summary>Its cast id ("fatOgre") and index (<see cref="EnemyRules.Cast"/>).</summary>
        public string Id;
        public int Cast;
        /// <summary>It follows the hero until the gap between their bodies is this.</summary>
        public double KeepM;
        /// <summary>The beats it walks between moves, by phase.</summary>
        public int[] RestBeats = { 2, 1 };
        public BossDeath Death;
        /// <summary>The floor turns to the warm zone at its entrance, for good (the dragon's; design.md, "Zones").</summary>
        public bool WarmZone;
        /// <summary>Its moves, in the order it takes them (one out of reach for a bar is skipped).</summary>
        public BossMoveRules[] Moves = new BossMoveRules[0];

        public int Rest(int phase) => RestBeats[Math.Min(phase, 2) - 1];

        /// <summary>Its rules for <paramref name="move"/>, or null.</summary>
        public BossMoveRules Find(BossMove move) => Array.Find(Moves, m => m.Move == move);

        public BossType Clone()
        {
            var c = (BossType)MemberwiseClone();
            c.RestBeats = (int[])RestBeats.Clone();
            c.Moves = Array.ConvertAll(Moves, m => m.Clone());
            return c;
        }
    }

    /// <summary>
    /// The bosses' numbers (Content/bosses.json; design.md, "Bosses", Decisions 2 and 3): arrival, the entrance's
    /// shatter, Break, phase two, and each boss's moves. Seconds are run clock where they count waiting
    /// (<see cref="WaitS"/>, <see cref="WindowS"/>), song time otherwise.
    /// </summary>
    public sealed class BossRules
    {
        public const string File = "bosses.json";

        /// <summary>A boss arrives on the first strong downbeat at least this long after spawning stops (at once then, beatless).</summary>
        public double WaitS = 4;
        /// <summary>Past this long after spawning stops, it arrives on the next downbeat, strong or not.</summary>
        public double WindowS = 60;
        /// <summary>It lands this far up the screen from the hero.</summary>
        public double DistanceM = 9;
        /// <summary>After its entrance it roars this many beats (this long beatless) before its first move.</summary>
        public int RoarBeats = 4;
        public double RoarS = 2;

        /// <summary>The screen round the hero (the spawn ring's): the entrance shatters the mobs inside, and the rest go unseen.</summary>
        public double ScreenHalfWidthM = 21, ScreenHalfHeightM = 14;
        /// <summary>The entrance's shards fly as from a blow of this power (<see cref="SimEvent.Kill"/>'s).</summary>
        public double EntrancePower = 8;

        /// <summary>A perfect dodge fills this share of Break.</summary>
        public double BreakPerDodge = 0.25;
        /// <summary>A full Break staggers the boss until this many beats after the dodge's (this long beatless), taking this times the damage.</summary>
        public int StaggerBeats = 4;
        public double StaggerBeatlessS = 2;
        public double BreakDamageScale = 2;

        /// <summary>Below this share of its health a boss enters phase two.</summary>
        public double PhaseTwoBelow = 0.5;

        public BossType[] Bosses = new BossType[0];

        /// <summary>The boss whose cast index is <paramref name="cast"/>, or null.</summary>
        public BossType Of(int cast) => Array.Find(Bosses, b => b.Cast == cast);

        /// <summary>Reads bosses.json's object; its bosses name boss types of <paramref name="enemies"/>.</summary>
        public static BossRules Read(JsonValue json, string path, EnemyRules enemies)
        {
            var r = new ContentReader(json, path);
            var rules = new BossRules();
            var arrival = r.Object("arrival");
            rules.WaitS = arrival.Number("waitS");
            rules.WindowS = arrival.Number("windowS");
            rules.DistanceM = arrival.Number("distanceM");
            rules.RoarBeats = arrival.Int("roarBeats");
            rules.RoarS = arrival.Number("roarS");
            arrival.End();
            var entrance = r.Object("entrance");
            rules.ScreenHalfWidthM = entrance.Number("screenHalfWidthM");
            rules.ScreenHalfHeightM = entrance.Number("screenHalfHeightM");
            rules.EntrancePower = entrance.Number("power");
            entrance.End();
            var brk = r.Object("break");
            rules.BreakPerDodge = brk.Number("perDodge");
            rules.StaggerBeats = brk.Int("staggerBeats");
            rules.StaggerBeatlessS = brk.Number("staggerBeatlessS");
            rules.BreakDamageScale = brk.Number("damageScale");
            brk.End();
            rules.PhaseTwoBelow = r.Number("phaseTwoBelow");
            rules.Bosses = r.Objects("bosses", b => ReadBoss(b, enemies));
            r.End();
            if (!(rules.WaitS >= 0 && rules.WindowS >= rules.WaitS)) throw ContentReader.Fail(path + ".arrival", "0 <= waitS <= windowS");
            if (!(rules.DistanceM > 0)) throw ContentReader.Fail(path + ".arrival.distanceM", "positive");
            if (rules.RoarBeats < 1 || !(rules.RoarS > 0)) throw ContentReader.Fail(path + ".arrival", "a positive roar");
            if (!(rules.ScreenHalfWidthM > 0 && rules.ScreenHalfHeightM > 0 && rules.EntrancePower > 0)) throw ContentReader.Fail(path + ".entrance", "positive");
            if (!(rules.BreakPerDodge > 0 && rules.BreakPerDodge <= 1)) throw ContentReader.Fail(path + ".break.perDodge", "0 to 1");
            if (rules.StaggerBeats < 1 || !(rules.StaggerBeatlessS > 0) || !(rules.BreakDamageScale >= 1))
                throw ContentReader.Fail(path + ".break", "a positive stagger, damage scale at least 1");
            if (!(rules.PhaseTwoBelow > 0 && rules.PhaseTwoBelow < 1)) throw ContentReader.Fail(path + ".phaseTwoBelow", "between 0 and 1");
            return rules;
        }

        static BossType ReadBoss(ContentReader r, EnemyRules enemies)
        {
            var b = new BossType { Id = r.String("id") };
            b.Cast = enemies.IndexOf(b.Id);
            if (b.Cast < 0 || !enemies.Cast[b.Cast].IsBoss) throw ContentReader.Fail(r.Child("id"), b.Id + " is no boss in " + EnemyRules.File);
            b.KeepM = r.Number("keepM");
            b.RestBeats = Pair(r, "restBeats", 0);
            string death = r.String("death");
            switch (death)
            {
                case "treasure": b.Death = BossDeath.Treasure; break;
                case "finale": b.Death = BossDeath.Finale; break;
                default: throw ContentReader.Fail(r.Child("death"), "treasure or finale, not " + death);
            }
            b.WarmZone = r.Bool("warmZone");
            b.Moves = r.Objects("moves", ReadMove);
            r.End();
            if (b.Moves.Length == 0) throw ContentReader.Fail(r.Child("moves"), "at least one");
            if (!(b.KeepM >= 0)) throw ContentReader.Fail(r.Child("keepM"), "at least 0");
            foreach (var m in b.Moves)
                if (m.ChainTwo != BossMove.None && b.Find(m.ChainTwo) == null)
                    throw ContentReader.Fail(r.Child("moves"), m.Move + " chains to " + m.ChainTwo + ", which " + b.Id + " lacks");
            return b;
        }

        static BossMoveRules ReadMove(ContentReader r)
        {
            string name = r.String("move");
            var m = new BossMoveRules
            {
                Move = MoveOf(name, r.Child("move")),
                TelegraphBeats = Pair(r, "telegraphBeats", 1),
                Damage = r.Number("damage"),
                MaxM = r.NumberOr("maxM", double.PositiveInfinity),
            };
            if (r.Has("chainTwo")) m.ChainTwo = MoveOf(r.String("chainTwo"), r.Child("chainTwo"));
            switch (m.Move)
            {
                case BossMove.Smash:
                    m.LengthM = r.Number("lengthM");
                    m.WidthM = r.Number("widthM");
                    break;
                case BossMove.Charge:
                    m.LengthM = r.Number("lengthM");
                    m.DurationS = r.Number("durationS");
                    break;
                case BossMove.Boulders:
                    m.Count = r.Int("count");
                    m.RadiusM = r.Number("radiusM");
                    m.SpreadM = r.Number("spreadM");
                    break;
                case BossMove.Fireballs:
                    m.Count = r.Int("count");
                    m.RadiusM = r.Number("radiusM");
                    break;
                case BossMove.Breath:
                    m.RangeM = r.Number("rangeM");
                    m.HalfArc = r.Number("halfArcDeg") * Math.PI / 180;
                    var sweep = r.Numbers("sweepDeg");
                    if (sweep.Length != 2) throw ContentReader.Fail(r.Child("sweepDeg"), "[phase one, phase two]");
                    m.Sweep = new[] { sweep[0] * Math.PI / 180, sweep[1] * Math.PI / 180 };
                    m.BurnBeats = r.Int("burnBeats");
                    break;
                default:
                    m.RadiusM = r.Number("radiusM");
                    break;
            }
            r.End();
            if (!(m.Damage >= 0)) throw ContentReader.Fail(r.Path, "damage at least 0");
            if (!(m.MaxM > 0)) throw ContentReader.Fail(r.Child("maxM"), "positive");
            if (m.Count < 1 || m.BurnBeats < 1) throw ContentReader.Fail(r.Path, "at least one circle and a burn of a beat");
            if (m.LengthM < 0 || m.WidthM < 0 || m.RadiusM < 0 || m.SpreadM < 0 || m.DurationS < 0 || m.RangeM < 0)
                throw ContentReader.Fail(r.Path, "sizes at least 0");
            return m;
        }

        static BossMove MoveOf(string name, string at)
        {
            switch (name)
            {
                case "smash": return BossMove.Smash;
                case "stomp": return BossMove.Stomp;
                case "charge": return BossMove.Charge;
                case "boulders": return BossMove.Boulders;
                case "breath": return BossMove.Breath;
                case "tail": return BossMove.Tail;
                case "dive": return BossMove.Dive;
                case "fireballs": return BossMove.Fireballs;
                default: throw ContentReader.Fail(at, "no move " + name);
            }
        }

        /// <summary>A [phase one, phase two] pair of whole numbers, each at least <paramref name="least"/>.</summary>
        static int[] Pair(ContentReader r, string key, int least)
        {
            var pair = r.Ints(key);
            if (pair.Length != 2 || pair[0] < least || pair[1] < least)
                throw ContentReader.Fail(r.Child(key), $"[phase one, phase two], each at least {least}");
            return pair;
        }

        public BossRules Clone()
        {
            var c = (BossRules)MemberwiseClone();
            c.Bosses = Array.ConvertAll(Bosses, b => b.Clone());
            return c;
        }
    }
}
