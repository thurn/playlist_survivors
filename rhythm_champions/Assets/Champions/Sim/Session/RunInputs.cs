using System;
using System.IO;

namespace Champions.Sim
{
    /// <summary>
    /// A run's inputs besides the stream and the player's: built once from the launch's flags, or read
    /// back from a session. The one value that makes a run's <see cref="SimConfig"/> (<see cref="Build"/>)
    /// and the one a session writes and reads, so a replay builds exactly what the run did. Every other
    /// config field is the content's or fixed.
    /// </summary>
    public sealed class RunInputs
    {
        static readonly SimConfig Defaults = new SimConfig();

        /// <summary>`--seed`.</summary>
        public ulong Seed = Defaults.Seed;
        public double StartTime = Defaults.StartTime;
        public double KnownAtBudget = Defaults.KnownAtBudget;
        public RunStart Start = Defaults.Start;

        /// <summary>The build the run starts with: Sword Slash at rank 1 (design.md, "The attacks"), or a scripted one.</summary>
        public Upgrade[] Loadout = StartLoadout();

        /// <summary>Standing enemies placed at the start: scenes for tests, stills and scripted checks (none in play).</summary>
        public Dummy[] Dummies = new Dummy[0];

        /// <summary>The design's starting build.</summary>
        public static Upgrade[] StartLoadout() => new[] { Upgrade.Of(AttackKind.SwordSlash, 1) };

        /// <summary>The run's config on <paramref name="content"/> (null: the defaults, which the content files repeat; dashes.json adds the dashes past the plain one).</summary>
        public SimConfig Build(GameContent content)
        {
            return new SimConfig
            {
                Seed = Seed,
                StartTime = StartTime,
                KnownAtBudget = KnownAtBudget,
                Start = Start,
                Beat = content != null ? content.Beat.Clone() : new BeatRules(),
                Hero = content != null ? content.Hero.Clone() : new HeroRules(),
                Dashes = content != null ? content.Dashes.Clone() : new DashRules(),
                Attacks = content != null ? content.Attacks.Clone() : new AttackRules(),
                Mods = content != null ? content.Mods.Clone() : new ModRules(),
                Loadout = (Upgrade[])Loadout.Clone(),
                Dummies = (Dummy[])Dummies.Clone(),
            };
        }

        /// <summary>Writes the inputs as <c>inputs.bin</c> holds them.</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(Seed);
            w.Write(StartTime);
            w.Write(KnownAtBudget);
            w.Write((byte)Start);
            w.Write(Loadout.Length);
            foreach (var u in Loadout)
            {
                w.Write((byte)u.Group);
                w.Write(u.Id);
                w.Write(u.Rank);
            }
            w.Write(Dummies.Length);
            foreach (var d in Dummies)
            {
                w.Write(d.X);
                w.Write(d.Y);
                w.Write(d.Radius);
                w.Write(d.Health);
            }
        }

        /// <summary>Reads the inputs of an <c>inputs.bin</c> of <paramref name="version"/> (version 1: the start loadout, no dummies).</summary>
        public static RunInputs Read(BinaryReader r, int version, string path)
        {
            var run = new RunInputs
            {
                Seed = r.ReadUInt64(),
                StartTime = r.ReadDouble(),
                KnownAtBudget = r.ReadDouble(),
            };
            byte start = r.ReadByte();
            if (start > (byte)RunStart.OnControl) throw new FormatException($"{path}: run start {start}");
            run.Start = (RunStart)start;
            if (version < 2) return run;
            run.Loadout = new Upgrade[Count(r, path, "loadout")];
            for (int i = 0; i < run.Loadout.Length; i++)
            {
                var u = new Upgrade { Group = (UpgradeGroup)r.ReadByte(), Id = r.ReadByte(), Rank = r.ReadByte() };
                if (!u.Valid) throw new FormatException($"{path}: loadout {i}: group {u.Group}, id {u.Id}, rank {u.Rank}");
                run.Loadout[i] = u;
            }
            run.Dummies = new Dummy[Count(r, path, "dummies")];
            for (int i = 0; i < run.Dummies.Length; i++)
                run.Dummies[i] = new Dummy { X = r.ReadDouble(), Y = r.ReadDouble(), Radius = r.ReadDouble(), Health = r.ReadDouble() };
            return run;
        }

        static int Count(BinaryReader r, string path, string what)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 1 << 16) throw new FormatException($"{path}: {n} {what}");
            return n;
        }
    }
}
