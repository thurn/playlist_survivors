using System.Collections.Generic;

namespace PS.Game.Sim.Content
{
    public enum SwarmKind { BatSwarm, GhostSwarm, FlowerWall }

    /// <summary>A minute's swarm event (Mad Forest's): it waits for the minute's first loud jump.</summary>
    public sealed class SwarmEvent
    {
        public SwarmKind Kind;
        public string Role;
        public double DelayS;
        public int ExtraRepeats;
        /// <summary>The percent chance it fires, or NaN for always.</summary>
        public double Chance;
        public double DurationS;
        public int Count;

        internal static SwarmEvent Read(ContentReader r)
        {
            var e = new SwarmEvent();
            string kind = r.String("kind");
            if (kind == "bat-swarm") e.Kind = SwarmKind.BatSwarm;
            else if (kind == "ghost-swarm") e.Kind = SwarmKind.GhostSwarm;
            else if (kind == "flower-wall") e.Kind = SwarmKind.FlowerWall;
            else throw ContentReader.Fail(r.Child("kind"), "unknown swarm kind '" + kind + "'");
            e.Role = r.String("role");
            e.DelayS = r.Number("delayS");
            e.ExtraRepeats = r.Int("extraRepeats");
            e.Chance = r.NumberOrNull("chance");
            e.DurationS = r.Number("durationS");
            e.Count = r.Int("count");
            return e;
        }
    }

    /// <summary>A minute's treasure: what a boss's chest holds.</summary>
    public sealed class Treasure
    {
        /// <summary>Percent chances, rolled in turn for tier 3, then 2, then 1; none means no chest.</summary>
        public double Tier3, Tier2, Tier1;
        /// <summary>A scripted tier (1-3) that skips the rolls, or -1.</summary>
        public int Tier;
        /// <summary>Whether this chest may evolve a weapon.</summary>
        public bool Evolves;

        internal static Treasure Read(ContentReader r)
        {
            var c = r.Object("chances");
            var t = new Treasure { Tier3 = c.Number("tier3"), Tier2 = c.Number("tier2"), Tier1 = c.Number("tier1") };
            c.End();
            t.Tier = r.IntOrNull("tier");
            if (t.Tier != -1 && (t.Tier < 1 || t.Tier > 3)) throw ContentReader.Fail(r.Child("tier"), "expected tier 1, 2 or 3");
            t.Evolves = r.Bool("evolves");
            return t;
        }
    }

    /// <summary>
    /// One minute of the run clock (Vampire Survivors' Stage.HandleSpawning): every
    /// <see cref="IntervalS"/> one of each roster role spawns, and whole rosters while fewer than
    /// <see cref="Minimum"/> are alive; bosses spawn once at the minute's start.
    /// </summary>
    public sealed class SpawnMinute
    {
        public int Minute;
        public int Minimum;
        public double IntervalS;
        public string[] Enemies;
        public string[] Bosses;
        public SwarmEvent[] Events;
        /// <summary>The bosses' chest, or null.</summary>
        public Treasure Treasure;

        internal static SpawnMinute Read(ContentReader r)
        {
            var m = new SpawnMinute
            {
                Minute = r.Int("minute"),
                Minimum = r.Int("minimum"),
                IntervalS = r.Number("intervalS"),
                Enemies = r.Strings("enemies"),
                Bosses = r.Strings("bosses"),
                Events = r.Objects("events", SwarmEvent.Read),
            };
            var t = r.ObjectOrNull("treasure");
            if (t != null)
            {
                m.Treasure = Treasure.Read(t);
                t.End();
            }
            if (m.IntervalS <= 0) throw ContentReader.Fail(r.Child("intervalS"), "expected a positive interval");
            return m;
        }
    }

    public sealed class Stampede
    {
        public double FromS;
        public string Role;
        public int Count, Rows;
        /// <summary>It follows a loudness jump of at least this many dB after <see cref="QuietS"/> of quiet.</summary>
        public double JumpDb, QuietS;
        /// <summary>The line charges at this multiple of its role's speed.</summary>
        public double SpeedScale;
    }

    public sealed class KickLine
    {
        public double FromS;
        public string Role;
        public int Count;
        public double SpacingM;
        /// <summary>It follows this many main beats of sustained medium loudness.</summary>
        public int MediumBeats, EntranceBeats, MinDanceBeats, MaxDanceBeats;
    }

    public sealed class Wedge
    {
        public double FromS;
        public string Role;
        public int Count;
        public double SpacingM;
        /// <summary>It comes on every this many beats in quiet passages.</summary>
        public int EveryBeats, EntranceBeats, DanceBeats;
    }

    public sealed class Ring
    {
        public double FromS;
        public string Role;
        public int Count;
        /// <summary>The ring leaves <see cref="Gaps"/> exits of <see cref="GapPlaces"/> places each.</summary>
        public int Gaps, GapPlaces;
        /// <summary>It comes on every this many beats in loud passages.</summary>
        public int EveryBeats, EntranceBeats;
        public double CloseToM;
    }

    /// <summary>The musical set pieces (engine/musical.rs): one group at a time within the cap.</summary>
    public sealed class SetPieces
    {
        public int GroupCap;
        public double GapS, LifeS;
        public Stampede Stampede;
        public KickLine KickLine;
        public Wedge Wedge;
        public Ring Ring;

        internal static SetPieces Read(ContentReader r)
        {
            var p = new SetPieces { GroupCap = r.Int("groupCap"), GapS = r.Number("gapS"), LifeS = r.Number("lifeS") };
            var s = r.Object("stampede");
            p.Stampede = new Stampede
            {
                FromS = s.Number("fromS"), Role = s.String("role"), Count = s.Int("count"), Rows = s.Int("rows"),
                JumpDb = s.Number("jumpDb"), QuietS = s.Number("quietS"), SpeedScale = s.Number("speedScale"),
            };
            s.End();
            var k = r.Object("kickLine");
            p.KickLine = new KickLine
            {
                FromS = k.Number("fromS"), Role = k.String("role"), Count = k.Int("count"), SpacingM = k.Number("spacingM"),
                MediumBeats = k.Int("mediumBeats"), EntranceBeats = k.Int("entranceBeats"),
                MinDanceBeats = k.Int("minDanceBeats"), MaxDanceBeats = k.Int("maxDanceBeats"),
            };
            k.End();
            var w = r.Object("wedge");
            p.Wedge = new Wedge
            {
                FromS = w.Number("fromS"), Role = w.String("role"), Count = w.Int("count"), SpacingM = w.Number("spacingM"),
                EveryBeats = w.Int("everyBeats"), EntranceBeats = w.Int("entranceBeats"), DanceBeats = w.Int("danceBeats"),
            };
            w.End();
            var g = r.Object("ring");
            p.Ring = new Ring
            {
                FromS = g.Number("fromS"), Role = g.String("role"), Count = g.Int("count"), Gaps = g.Int("gaps"),
                GapPlaces = g.Int("gapPlaces"), EveryBeats = g.Int("everyBeats"), EntranceBeats = g.Int("entranceBeats"),
                CloseToM = g.Number("closeToM"),
            };
            g.End();
            return p;
        }
    }

    /// <summary>The signature minotaur (engine/signature.rs): a scripted mini-boss from <see cref="FromS"/>.</summary>
    public sealed class Signature
    {
        public string Role;
        public double FromS;
        public int SlamBeats;
        public double SlamDamage, SlamRadiusM;
        public double RetreatS;
    }

    /// <summary>The finale blast at 30:00 and the victory card after it.</summary>
    public sealed class Finale
    {
        public double RadiusM, Damage;
        public int Beats;
        public double VictoryCardS;
    }

    /// <summary>
    /// The five zones (unity-game.md, Music-Driven Events): a section change at least
    /// <see cref="MinGapS"/> after the last zone change moves on, else the fallback does.
    /// </summary>
    public sealed class ZoneRules
    {
        public string[] Ids;
        public string Victory;
        public double JumpDb, MinGapS;
        /// <summary>The fallback is <see cref="OpeningFallbackS"/> through the first <see cref="OpeningS"/>
        /// of the run, then <see cref="FallbackS"/>.</summary>
        public double OpeningFallbackS, OpeningS, FallbackS;
    }

    public sealed class EasyRules
    {
        /// <summary>Enemy contact damage's multiplier.</summary>
        public double ContactDamage;
        public double BruteChickenChance;
        public double BossChickens;
    }

    /// <summary>schedule.json: the 30-minute run (levels/survivors.yaml and the hooks native keeps in code).</summary>
    public sealed class Schedule
    {
        public double DurationS;
        /// <summary>The most enemies alive at once.</summary>
        public int Cap;
        public EasyRules Easy;
        /// <summary>Minutes[i] is minute i.</summary>
        public SpawnMinute[] Minutes;
        public double SwarmJumpDb, SwarmLateS;
        public SetPieces SetPieces;
        public Signature Signature;
        public Finale Finale;
        public ZoneRules Zones;

        /// <summary>Every role id the schedule names, with where.</summary>
        public IEnumerable<(string role, string path)> RoleRefs()
        {
            for (int i = 0; i < Minutes.Length; i++)
            {
                foreach (string e in Minutes[i].Enemies) yield return (e, "minutes[" + i + "].enemies");
                foreach (string b in Minutes[i].Bosses) yield return (b, "minutes[" + i + "].bosses");
                foreach (var e in Minutes[i].Events) yield return (e.Role, "minutes[" + i + "].events");
            }
            yield return (SetPieces.Stampede.Role, "setPieces.stampede.role");
            yield return (SetPieces.KickLine.Role, "setPieces.kickLine.role");
            yield return (SetPieces.Wedge.Role, "setPieces.wedge.role");
            yield return (SetPieces.Ring.Role, "setPieces.ring.role");
            yield return (Signature.Role, "signature.role");
        }

        internal static Schedule Read(ContentReader r)
        {
            var s = new Schedule();
            var run = r.Object("run");
            s.DurationS = run.Number("durationS");
            s.Cap = run.Int("cap");
            run.End();
            var easy = r.Object("easy");
            s.Easy = new EasyRules
            {
                ContactDamage = easy.Number("contactDamage"),
                BruteChickenChance = easy.Number("bruteChickenChance"),
                BossChickens = easy.Number("bossChickens"),
            };
            easy.End();
            s.Minutes = r.Objects("minutes", SpawnMinute.Read);
            for (int i = 0; i < s.Minutes.Length; i++)
            {
                string path = r.Child("minutes") + "[" + i + "]";
                if (s.Minutes[i].Minute != i) throw ContentReader.Fail(path, "minutes must run from 0 without gaps");
                if (s.Minutes[i].Minimum > s.Cap) throw ContentReader.Fail(path, "minimum above the cap " + s.Cap);
            }
            if (s.Minutes.Length == 0) throw ContentReader.Fail(r.Child("minutes"), "no minutes");
            var swarms = r.Object("swarms");
            s.SwarmJumpDb = swarms.Number("jumpDb");
            s.SwarmLateS = swarms.Number("lateS");
            swarms.End();
            var pieces = r.Object("setPieces");
            s.SetPieces = SetPieces.Read(pieces);
            pieces.End();
            var sig = r.Object("signature");
            s.Signature = new Signature
            {
                Role = sig.String("role"), FromS = sig.Number("fromS"), SlamBeats = sig.Int("slamBeats"),
                SlamDamage = sig.Number("slamDamage"), SlamRadiusM = sig.Number("slamRadiusM"), RetreatS = sig.Number("retreatS"),
            };
            sig.End();
            var fin = r.Object("finale");
            s.Finale = new Finale
            {
                RadiusM = fin.Number("radiusM"), Damage = fin.Number("damage"), Beats = fin.Int("beats"),
                VictoryCardS = fin.Number("victoryCardS"),
            };
            fin.End();
            var z = r.Object("zones");
            s.Zones = new ZoneRules
            {
                Ids = z.Strings("ids"), Victory = z.String("victory"), JumpDb = z.Number("jumpDb"), MinGapS = z.Number("minGapS"),
                OpeningFallbackS = z.Number("openingFallbackS"), OpeningS = z.Number("openingS"), FallbackS = z.Number("fallbackS"),
            };
            if (System.Array.IndexOf(s.Zones.Ids, s.Zones.Victory) < 0)
                throw ContentReader.Fail(z.Child("victory"), "names no zone");
            z.End();
            return s;
        }
    }
}
