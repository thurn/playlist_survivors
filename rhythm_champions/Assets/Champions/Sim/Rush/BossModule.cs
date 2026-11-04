using System;
using System.Collections.Generic;

namespace Champions.Sim
{
    /// <summary>One body a <see cref="BossModule"/> fields: its cast row (enemies.json), where it arrives and its share of the health.</summary>
    public struct BodySpec
    {
        /// <summary>Its cast id in enemies.json (a boss row).</summary>
        public string Cast;
        /// <summary>Where it arrives, from the arena's centre (+y up the screen).</summary>
        public double X, Y;
        /// <summary>Its share of the fight's health (the shares are normalised).</summary>
        public double Share;

        public BodySpec(string cast, double x = 0, double y = 6, double share = 1)
        {
            Cast = cast;
            X = x;
            Y = y;
            Share = share;
        }
    }

    /// <summary>How a fight starts (<see cref="BossFight.Start"/>): the mode shell's per-boss numbers, or a test's.</summary>
    public sealed class FightSetup
    {
        /// <summary>The arena's centre; NaN: the hero's position at the start (the arena forms round the hero).</summary>
        public double CentreX = double.NaN, CentreY = double.NaN;
        /// <summary>The fight's whole health, split over the bodies by their shares; NaN: the bodies' cast health.</summary>
        public double Health = double.NaN;
        /// <summary>Its act, 1 to 5: acts 1 and 2 warn at least 3 beats, later acts 2 (boss-rush.md, "Fair fights").</summary>
        public int Act = 1;
    }

    /// <summary>
    /// One Boss Rush boss's fight (boss-rush.md, "Fights"): a self-contained sim module over <see cref="BossFight"/>'s
    /// building blocks, so the main game can adopt it. The fight calls it; it never steps the sim itself.
    ///
    /// - <see cref="Bodies"/>, <see cref="PhasesBelow"/>, <see cref="TransitionBeats"/>, <see cref="EntranceBeats"/> and
    ///   <see cref="ShedThresholds"/> declare its shape; <see cref="BossFight.Start"/> reads them once.
    /// - <see cref="Begin"/> runs at its arrival, <see cref="Tick"/> every tick while it fights (not in a transition),
    ///   <see cref="Beat"/> on every beat from its arrival, <see cref="Landed"/> as each of its hazards lands.
    /// - <see cref="PhaseBegins"/> opens a transition (at most 4 beats, untouchable); <see cref="BodyDied"/> tells a
    ///   multi-body boss one fell; <see cref="ShedSpots"/> authors each XP burst's geometry.
    ///
    /// <code>
    /// sealed class Bloom : BossModule
    /// {
    ///     public override string Id => "bloom";
    ///     public override double[] PhasesBelow => new[] { 0.5 };
    ///     protected internal override void Beat(BossFight f, double t, int beat, bool downbeat) { if (downbeat) Spiral(f); }
    /// }
    /// </code>
    ///
    /// A module's state lives on its instance: <see cref="BossModules.Create"/> makes a fresh one per fight. It draws its
    /// randomness from <see cref="BossFight.Random"/> alone.
    /// </summary>
    public abstract class BossModule
    {
        /// <summary>Its boss id (boss-rush.md's roster; bossrush.json's).</summary>
        public abstract string Id { get; }

        /// <summary>Its bodies: by default one, its <see cref="Id"/>'s cast row, six metres up the screen from the centre.</summary>
        public virtual BodySpec[] Bodies => new[] { new BodySpec(Id) };

        /// <summary>The health shares (of the whole fight's) below which each next phase begins, descending; none by default.</summary>
        public virtual double[] PhasesBelow => Array.Empty<double>();

        /// <summary>A phase transition's beats: the boss can't be damaged through them (at most 4, Decision 8).</summary>
        public virtual int TransitionBeats => 4;

        /// <summary>The beats between its arrival and its first move (its entrance).</summary>
        public virtual int EntranceBeats => 4;

        /// <summary>The XP bursts it sheds, evenly spaced in health (9: at 90%, 80% ... 10%).</summary>
        public virtual int ShedThresholds => 9;

        /// <summary>At its arrival, <paramref name="t"/>.</summary>
        protected internal virtual void Begin(BossFight f, double t) { }

        /// <summary>The tick ending at <paramref name="end"/>, while it fights.</summary>
        protected internal virtual void Tick(BossFight f, double end) { }

        /// <summary>Beat <paramref name="beat"/> at <paramref name="t"/>, from its arrival to its death.</summary>
        protected internal virtual void Beat(BossFight f, double t, int beat, bool downbeat) { }

        /// <summary>Its hazard <paramref name="h"/> landed at <paramref name="t"/>.</summary>
        protected internal virtual void Landed(BossFight f, in Hazard h, double t) { }

        /// <summary>
        /// Phase <paramref name="phase"/> begins at <paramref name="t"/>: the fight has called off every hazard and holds the
        /// boss untouchable until <paramref name="until"/>. The module may lay a set-piece meanwhile (an arena reshape).
        /// </summary>
        protected internal virtual void PhaseBegins(BossFight f, int phase, double t, double until) { }

        /// <summary>Body <paramref name="body"/> fell at <paramref name="t"/>, others still standing (the survivors may enrage).</summary>
        protected internal virtual void BodyDied(BossFight f, int body, double t) { }

        /// <summary>
        /// Burst <paramref name="burst"/> (0 first) of <see cref="ShedThresholds"/>'s gems at <paramref name="t"/>: by default a
        /// ring of six round the first standing body. A boss authors its own (a spray in front, gems across a hazard lane).
        /// </summary>
        protected internal virtual void ShedSpots(BossFight f, int burst, double t, List<GemSpot> into)
        {
            ref readonly var b = ref f.Lead;
            const int n = 6;
            double turn = burst * 0.5;
            for (int k = 0; k < n; k++)
            {
                double a = turn + 2 * Math.PI * k / n;
                into.Add(new GemSpot(b.X + 3.5 * SimMath.Cos(a), b.Y + 3.5 * SimMath.Sin(a), 1.0 / n));
            }
        }
    }

    /// <summary>
    /// The Boss Rush bosses by id: each fight bead adds its line. A fresh module per fight, so their state never leaks
    /// between fights or runs.
    /// </summary>
    public static class BossModules
    {
        static readonly Dictionary<string, Func<BossModule>> Factories = new Dictionary<string, Func<BossModule>>
        {
            // The framework's proof: the main game's Fat Ogre on the building blocks.
            { OgreModule.ModuleId, () => new OgreModule() },
        };

        /// <summary>The ids registered, in a fixed order.</summary>
        public static IEnumerable<string> Ids
        {
            get
            {
                var ids = new List<string>(Factories.Keys);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        public static bool Has(string id) => id != null && Factories.ContainsKey(id);

        /// <summary>Registers a boss outside the table (the tests' probes); a fight bead adds its line to the table instead.</summary>
        internal static void Register(string id, Func<BossModule> make) => Factories[id] = make;

        /// <summary>A fresh module for boss <paramref name="id"/>.</summary>
        public static BossModule Create(string id)
        {
            if (id == null || !Factories.TryGetValue(id, out var make)) throw new ArgumentException($"no Boss Rush boss {id}");
            return make();
        }
    }
}
