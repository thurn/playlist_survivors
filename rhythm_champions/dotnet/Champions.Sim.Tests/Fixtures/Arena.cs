using System;
using System.Collections.Generic;
using System.Linq;

namespace Champions.Sim.Tests
{
    /// <summary>A sim with a build and standing enemies on a fixture song, for the attacks' and mods' tests.</summary>
    public static class Arena
    {
        /// <summary>Enemies too tough to die in a test.</summary>
        public const double Tough = 1e9;

        public static Upgrade[] Build(params string[] upgrades) => upgrades.Select(Upgrade.Parse).ToArray();

        /// <summary>The game's content (every dash among it).</summary>
        public static readonly GameContent Content = GameContent.Load(Checkout.In("Content"));

        /// <summary>
        /// A sim on the content playing from the start with <paramref name="build"/> and <paramref name="dummies"/>,
        /// given <paramref name="song"/>'s entries at their normal known-at times (none: no music), not yet stepped.
        /// </summary>
        public static Simulation Sim(FixtureSong song, Upgrade[] build, params Dummy[] dummies)
        {
            var sim = new Simulation(new RunInputs { Loadout = build, Dummies = dummies }.Build(Content));
            if (song != null)
                foreach (var k in Drive.Known(song.Entries())) sim.AddKnown(k.Entry, k.KnownAt);
            return sim;
        }

        /// <summary>A sim as <see cref="Sim"/> makes it, stepped to <paramref name="until"/>.</summary>
        public static Simulation Run(FixtureSong song, double until, Upgrade[] build, params Dummy[] dummies)
        {
            var sim = Sim(song, build, dummies);
            sim.StepTo(until);
            return sim;
        }

        /// <summary>A song of one tempo for <paramref name="seconds"/>, or beatless with <paramref name="bpm"/> 0.</summary>
        public static FixtureSong Song(double bpm, double seconds = 40) =>
            bpm > 0 ? new FixtureSong().Beats(0, seconds, bpm) : new FixtureSong().Gap(0, seconds);

        public static List<SimEvent> Strikes(Simulation sim, BlowSource source) =>
            Drive.Events(sim, SimEventKind.Strike).Where(e => e.I0 == (int)source).ToList();

        public static BlowFlags Flags(in SimEvent strike) => (BlowFlags)(strike.I1 & 0xFF);

        public static ShapeKind Shape(in SimEvent strike) => (ShapeKind)(strike.I1 >> 8);

        /// <summary>Hits and kills of enemy <paramref name="enemy"/> (all with -1), from <paramref name="source"/> (any with null).</summary>
        public static List<SimEvent> HitsOn(Simulation sim, int enemy = -1, BlowSource? source = null) =>
            sim.EventsSince(0).ToArray()
                .Where(e => (e.Kind == SimEventKind.Hit || e.Kind == SimEventKind.Kill) && (enemy < 0 || e.I0 == enemy) &&
                            (source == null || e.I1 == (int)source))
                .ToList();

        /// <summary>The beats fired on a reliable beat, by time.</summary>
        public static List<SimEvent> ReliableBeats(Simulation sim) =>
            Drive.Events(sim, SimEventKind.Beat).Where(e => e.BeatReliability == Reliability.Reliable).ToList();

        /// <summary>Space heard at <paramref name="heard"/>, reaching the sim a lead of 0.05 s later.</summary>
        public static void Dash(Simulation sim, double heard)
        {
            sim.StepTo(heard + 0.05);
            sim.Press(Key.Space, heard);
        }

        /// <summary>Equips dash <paramref name="id"/> at <paramref name="rank"/>.</summary>
        public static void Equip(Simulation sim, string id, int rank) => sim.Dashes.Equip(sim, Content.Dashes.IndexOf(id), rank);

        /// <summary>An attack's blow of <paramref name="tags"/> landing now on a circle around the hero.</summary>
        public static int Land(Simulation sim, HitTags tags, double damage, double radius = 2.5, BlowFlags flags = BlowFlags.None) =>
            sim.Strikes.Land(sim, new Blow
            {
                Shape = HitShape.Circle(0, 0, radius), T = sim.Now, Damage = damage, Tags = tags, Source = BlowSource.SwordSlash,
                Flags = flags, Owner = -1, Part = -1,
            });
    }
}
