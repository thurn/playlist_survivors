using System;

namespace Champions.Sim
{
    /// <summary>A type's part in the level (design.md, "The cast" and Decision 14).</summary>
    public enum EnemyRole : byte
    {
        /// <summary>Hurts only by touch.</summary>
        Fodder = 0,
        /// <summary>Fodder that comes in swarms (the bat).</summary>
        Swarmer = 1,
        /// <summary>Shoots (the mushroom's spores).</summary>
        Ranged = 2,
        /// <summary>Tougher, with one telegraphed move.</summary>
        Elite = 3,
        /// <summary>A duel boss (the bosses' bead spawns and runs it).</summary>
        Boss = 4,
    }

    /// <summary>An elite's or ranged type's one telegraphed move (design.md, "Threats").</summary>
    public enum EliteMove : byte
    {
        None = 0,
        /// <summary>The big ork's: a line over two beats, an 8 m charge on the next.</summary>
        Charge = 1,
        /// <summary>The troll's: a 3 m ring over two beats, a slam on the next.</summary>
        Slam = 2,
        /// <summary>The mushroom's: its cap flashes a beat before it fires a spore.</summary>
        Spores = 3,
    }

    /// <summary>One type of the cast (Content/enemies.json's <c>cast</c>; design.md, "The cast").</summary>
    public sealed class EnemyType
    {
        /// <summary>The name the content and the client know it by ("skeleton").</summary>
        public string Id;
        public EnemyRole Role;
        /// <summary>Its palette family slot ("keyLight", "keyDeep", "accent", "counter", "boss"): the client's.</summary>
        public string Slot;
        /// <summary>The model's size (a walker's height, a flyer's span): the client's scale.</summary>
        public double SizeM;
        /// <summary>The body's radius on the ground: what blows reach, what touches the hero, what dark gaps keep apart.</summary>
        public double RadiusM;
        /// <summary>How many beats from one step to the next: 0.5 (half beats), 1 or 2. Bosses: 0, they move by their own rules.</summary>
        public double StepBeats;
        /// <summary>The average speed, whatever the tempo (Decision 4): a step is this times its interval.</summary>
        public double SpeedMps;
        /// <summary>Health as a multiple of the level's fodder health at its spawn (<see cref="LevelRules.Toughness"/>); a boss's is fixed (<see cref="Health"/>).</summary>
        public double Toughness;
        /// <summary>A boss's health in Sword Slash's base hits; NaN for the rest.</summary>
        public double Health = double.NaN;
        /// <summary>Experience for the kill, on its impact frame (design.md, "Experience"): the progression's.</summary>
        public double Experience;
        /// <summary>How many spawn together at one point (a swarm of bats).</summary>
        public int Group = 1;
        public EliteMove Move;

        public bool IsBoss => Role == EnemyRole.Boss;
    }

    /// <summary>The big ork's charge (design.md, "Threats").</summary>
    public sealed class ChargeRules
    {
        public int TelegraphBeats = 2;
        public double LengthM = 8;
        /// <summary>How long the charge's run takes, from its landing beat.</summary>
        public double DurationS = 0.25;
        public double Damage = 20;
        /// <summary>It starts a charge when the hero is this close.</summary>
        public double TriggerM = 9;
        /// <summary>From one move's landing until the next can start.</summary>
        public double CooldownS = 6;
    }

    /// <summary>The troll's slam (design.md, "Threats").</summary>
    public sealed class SlamRules
    {
        public int TelegraphBeats = 2;
        public double RadiusM = 3;
        public double Damage = 20;
        /// <summary>It starts a slam when the hero is this close: inside the ring, so a hero who stands is caught.</summary>
        public double TriggerM = 3;
        public double CooldownS = 6;
    }

    /// <summary>The mushroom's spores (design.md, "Threats").</summary>
    public sealed class SporeRules
    {
        /// <summary>The cap flashes this many beats before the spore fires.</summary>
        public int TelegraphBeats = 1;
        public double SpeedMps = 7;
        public double RangeM = 14;
        /// <summary>One spore every this many bars (8 beats with 4-beat bars; seconds at 0.5 s a beat beatless).</summary>
        public int EveryBars = 2;
        public double Damage = 10;
        /// <summary>The spore's body.</summary>
        public double RadiusM = 0.3;
        /// <summary>A mushroom stops closing in this near the hero: it shoots from range.</summary>
        public double KeepAwayM = 8;
    }

    /// <summary>
    /// The horde's numbers (Content/enemies.json; design.md, "The Horde", Decisions 14 and 16): the cast
    /// (the Fat Ogre's and the dragon's rows are the bosses' bead's), steps, dark gaps, touch and the elite
    /// moves. The defaults are the design's numbers with an empty cast: a sim without content has no horde
    /// to spawn.
    /// </summary>
    public sealed class EnemyRules
    {
        public const string File = "enemies.json";

        public EnemyType[] Cast = new EnemyType[0];

        /// <summary>A step's motion settles within this share of its interval (design.md, "Moving to the beat").</summary>
        public double SettleShare = 0.4;

        /// <summary>
        /// Dark gaps: two bodies' centres keep at least their radii's sum times one plus this apart, so a crowd
        /// reads as lit shapes with black between them (design.md, "Moving to the beat").
        /// </summary>
        public double GapShare = 0.25;

        /// <summary>A touch's bite (design.md, "Threats").</summary>
        public double TouchDamage = 5;
        /// <summary>With no reliable beat, a touching monster bites this often.</summary>
        public double TouchBeatlessS = 0.5;
        /// <summary>A body this close to the hero's touches it.</summary>
        public double TouchSlackM = 0.1;

        public ChargeRules Charge = new ChargeRules();
        public SlamRules Slam = new SlamRules();
        public SporeRules Spores = new SporeRules();

        /// <summary>The cast index of <paramref name="id"/>, or -1.</summary>
        public int IndexOf(string id)
        {
            for (int i = 0; i < Cast.Length; i++)
                if (Cast[i].Id == id) return i;
            return -1;
        }

        /// <summary>The largest step a mob of the cast takes at a beat period of <paramref name="periodS"/>.</summary>
        public double LongestStep(double periodS)
        {
            double most = 0;
            foreach (var t in Cast)
                if (!t.IsBoss) most = Math.Max(most, t.SpeedMps * t.StepBeats * periodS);
            return most;
        }

        /// <summary>Reads enemies.json's object.</summary>
        public static EnemyRules Read(JsonValue json, string path)
        {
            var r = new ContentReader(json, path);
            var rules = new EnemyRules();
            var steps = r.Object("steps");
            rules.SettleShare = steps.Number("settleShare");
            steps.End();
            var separation = r.Object("separation");
            rules.GapShare = separation.Number("gapShare");
            separation.End();
            var touch = r.Object("touch");
            rules.TouchDamage = touch.Number("damage");
            rules.TouchBeatlessS = touch.Number("beatlessEveryS");
            rules.TouchSlackM = touch.Number("slackM");
            touch.End();
            var charge = r.Object("charge");
            rules.Charge = new ChargeRules
            {
                TelegraphBeats = charge.Int("telegraphBeats"),
                LengthM = charge.Number("lengthM"),
                DurationS = charge.Number("durationS"),
                Damage = charge.Number("damage"),
                TriggerM = charge.Number("triggerM"),
                CooldownS = charge.Number("cooldownS"),
            };
            charge.End();
            var slam = r.Object("slam");
            rules.Slam = new SlamRules
            {
                TelegraphBeats = slam.Int("telegraphBeats"),
                RadiusM = slam.Number("radiusM"),
                Damage = slam.Number("damage"),
                TriggerM = slam.Number("triggerM"),
                CooldownS = slam.Number("cooldownS"),
            };
            slam.End();
            var spores = r.Object("spores");
            rules.Spores = new SporeRules
            {
                TelegraphBeats = spores.Int("telegraphBeats"),
                SpeedMps = spores.Number("speedMps"),
                RangeM = spores.Number("rangeM"),
                EveryBars = spores.Int("everyBars"),
                Damage = spores.Number("damage"),
                RadiusM = spores.Number("radiusM"),
                KeepAwayM = spores.Number("keepAwayM"),
            };
            spores.End();
            rules.Cast = r.Objects("cast", ReadType);
            r.End();
            rules.Validate(path);
            return rules;
        }

        static EnemyType ReadType(ContentReader r)
        {
            var t = new EnemyType
            {
                Id = r.String("id"),
                Role = ParseRole(r, r.String("role")),
                Slot = r.String("slot"),
                SizeM = r.Number("sizeM"),
                RadiusM = r.Number("radiusM"),
                SpeedMps = r.Number("speedMps"),
                Experience = r.Number("experience"),
            };
            if (t.IsBoss)
            {
                t.Health = r.Number("health");
                return t;
            }
            t.StepBeats = r.Number("stepBeats");
            t.Toughness = r.Number("toughness");
            t.Group = r.Int("group");
            string move = r.StringOrNull("move");
            t.Move = move == null ? EliteMove.None
                : move == "charge" ? EliteMove.Charge
                : move == "slam" ? EliteMove.Slam
                : move == "spores" ? EliteMove.Spores
                : throw ContentReader.Fail(r.Child("move"), "not a move (charge, slam, spores)");
            return t;
        }

        static EnemyRole ParseRole(ContentReader r, string role)
        {
            switch (role)
            {
                case "fodder": return EnemyRole.Fodder;
                case "swarmer": return EnemyRole.Swarmer;
                case "ranged": return EnemyRole.Ranged;
                case "elite": return EnemyRole.Elite;
                case "boss": return EnemyRole.Boss;
                default: throw ContentReader.Fail(r.Child("role"), "not a role (fodder, swarmer, ranged, elite, boss)");
            }
        }

        void Validate(string path)
        {
            if (!(SettleShare > 0 && SettleShare <= 1)) throw ContentReader.Fail(path + ".steps.settleShare", "above 0, at most 1");
            if (!(GapShare >= 0)) throw ContentReader.Fail(path + ".separation.gapShare", "at least 0");
            if (!(TouchBeatlessS > 0)) throw ContentReader.Fail(path + ".touch.beatlessEveryS", "positive");
            if (Charge.TelegraphBeats < 1 || Slam.TelegraphBeats < 1 || Spores.TelegraphBeats < 1)
                throw ContentReader.Fail(path, "telegraphs take at least one beat");
            if (Spores.EveryBars < 1) throw ContentReader.Fail(path + ".spores.everyBars", "at least 1");
            if (!(Spores.SpeedMps > 0) || !(Spores.RangeM > 0)) throw ContentReader.Fail(path + ".spores", "a positive speed and range");
            for (int i = 0; i < Cast.Length; i++)
            {
                var t = Cast[i];
                string at = path + ".cast[" + i + "]";
                if (IndexOf(t.Id) != i) throw ContentReader.Fail(at + ".id", "duplicate " + t.Id);
                if (!(t.RadiusM > 0)) throw ContentReader.Fail(at + ".radiusM", "positive");
                if (!(t.SpeedMps >= 0)) throw ContentReader.Fail(at + ".speedMps", "at least 0");
                if (t.IsBoss)
                {
                    if (!(t.Health > 0)) throw ContentReader.Fail(at + ".health", "positive");
                    continue;
                }
                if (t.StepBeats != 0.5 && t.StepBeats != 1 && t.StepBeats != 2) throw ContentReader.Fail(at + ".stepBeats", "0.5, 1 or 2");
                if (!(t.Toughness > 0)) throw ContentReader.Fail(at + ".toughness", "positive");
                if (t.Group < 1) throw ContentReader.Fail(at + ".group", "at least 1");
            }
        }

        /// <summary>A copy, so a run's config never shares the content's.</summary>
        public EnemyRules Clone()
        {
            var copy = (EnemyRules)MemberwiseClone();
            copy.Cast = (EnemyType[])Cast.Clone();
            return copy;
        }
    }
}
