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

        /// <summary>The run's config on <paramref name="content"/> (null: the defaults, which beat.json and hero.json repeat; dashes.json adds the dashes past the plain one).</summary>
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
            };
        }

        /// <summary>Writes the inputs as <c>inputs.bin</c> holds them.</summary>
        public void Write(BinaryWriter w)
        {
            w.Write(Seed);
            w.Write(StartTime);
            w.Write(KnownAtBudget);
            w.Write((byte)Start);
        }

        /// <summary>Reads the inputs of an <c>inputs.bin</c> of <paramref name="version"/>.</summary>
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
            return run;
        }
    }
}
