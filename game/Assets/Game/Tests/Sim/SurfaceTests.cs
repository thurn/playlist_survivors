using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace PS.Game.Sim.Tests
{
    /// <summary>
    /// The client's side of the sim's contract (unity-game.md, "The Sim's Contract"): only the
    /// contract's mutators change the run. Everything else the client can reach is a read, safe to
    /// make at any time, at any song time, as often as it likes.
    /// </summary>
    public class SurfaceTests
    {
        static readonly string[] Mutators = { "Add", "AddKnown", "Press", "Release", "StepTo", "DiscardEventsBefore" };

        /// <summary>The objects the client reaches from a sim: the sim's own classes behind its public properties.</summary>
        static List<object> Surface(Simulation sim)
        {
            var found = new List<object> { sim };
            for (int i = 0; i < found.Count; i++)
                foreach (var p in found[i].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var type = p.PropertyType;
                    if (!type.IsClass || type.Assembly != typeof(Simulation).Assembly || p.GetIndexParameters().Length > 0) continue;
                    object value = p.GetValue(found[i]);
                    if (value != null && !found.Contains(value)) found.Add(value);
                }
            return found;
        }

        [Test]
        public void ReadingEverythingEveryFrameDoesNotChangeTheRun()
        {
            var entries = EntryDump.Load(EntryDump.House128, out double duration);
            var quiet = new Simulation(HordeFixture.Config());
            Drive.Feed(quiet, entries, duration);
            var called = new HashSet<string>();
            var curious = new Simulation(HordeFixture.Config());
            Drive.Feed(curious, entries, duration, eachFrame: sim => ReadEverything(sim, called));
            Assert.Greater(Drive.Events(quiet, SimEventKind.Beat).Count, 40);
            Assert.Greater(Drive.Events(quiet, SimEventKind.EnemyStep).Count, 100);
            Assert.AreEqual(quiet.Hash, curious.Hash);
            CollectionAssert.AreEqual(quiet.EventsSince(0).ToArray(), curious.EventsSince(0).ToArray());
            CollectionAssert.IsSupersetOf(called, new[] { "BeatClock.StateAt", "BeatClock.ClearAt", "BeatClock.Continues",
                "BeatClock.BarOf", "StrideGrid.Recent", "BeliefLog.KnownAt", "SimState.get_Beat", "Judging.LockAtOrBefore",
                "ArenaGrid.IsOpen", "HeroState.get_TileX" });
        }

        [Test]
        public void TheClientCanWriteNothingAndCannotReachTheGenerator()
        {
            var surface = Surface(new Simulation(HordeFixture.Config()));
            Assert.That(surface.ConvertAll(o => o.GetType().Name), Is.SupersetOf(new[]
                { "Simulation", "SimState", "BeliefLog", "BeatClock", "Judging", "StrideGrid", "ArenaGrid", "HeroState" }));
            foreach (object o in surface)
            {
                var type = o.GetType();
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    Assert.IsTrue(f.IsLiteral || f.IsInitOnly, $"{type.Name}.{f.Name} is writable");
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    Assert.IsNull(p.GetSetMethod(), $"{type.Name}.{p.Name} has a public setter");
                    Assert.AreNotEqual(typeof(SimRandom), p.PropertyType, $"{type.Name}.{p.Name}");
                }
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                    Assert.AreNotEqual(typeof(SimRandom), m.ReturnType, $"{type.Name}.{m.Name}");
            }
        }

        /// <summary>
        /// Calls every public method and property getter on the surface except the mutators, with
        /// song times a second either side of the frontier, small indices and every kind; then
        /// walks every span it exposes.
        /// </summary>
        static void ReadEverything(Simulation sim, HashSet<string> called)
        {
            foreach (object target in Surface(sim))
                foreach (var m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.DeclaringType == typeof(object)) continue;
                    if (target is Simulation && Array.IndexOf(Mutators, m.Name) >= 0) continue;
                    // Spans and refs can't go through reflection: read below.
                    if (m.ReturnType.IsByRefLike || m.ReturnType.IsByRef) continue;
                    foreach (object[] args in Arguments(m.GetParameters(), sim))
                    {
                        try
                        {
                            m.Invoke(m.IsStatic ? null : target, args);
                            called.Add(target.GetType().Name + "." + m.Name);
                        }
                        catch (TargetInvocationException e)
                            when (e.InnerException is IndexOutOfRangeException || e.InnerException is ArgumentOutOfRangeException)
                        {
                        }
                    }
                }
            double sum = 0;
            var stream = sim.Stream;
            for (var kind = EntryKind.Beat; kind <= EntryKind.Timbre; kind++)
            {
                foreach (var e in stream.Revealed(kind)) sum += e.T;
                if (stream.Count(kind) > 0) sum += stream.Get(kind, 0).T;
            }
            foreach (var e in stream.Beats) sum += e.V0;
            foreach (var e in stream.Ticks) sum += e.V0;
            foreach (double t in sim.Beats.Judging.Locks) sum += t;
            foreach (var k in sim.Keys) sum += k.Heard;
            foreach (var e in sim.EventsSince(0)) sum += e.T;
            foreach (var e in sim.State.Enemies)
            {
                e.PositionAt(sim.Now - 0.5, out double x, out double y);
                sum += e.X + x + y;
            }
            Assert.IsFalse(double.IsNaN(sum));
        }

        static IEnumerable<object[]> Arguments(ParameterInfo[] parameters, Simulation sim)
        {
            var choices = new object[parameters.Length][];
            for (int i = 0; i < parameters.Length; i++)
            {
                var type = parameters[i].ParameterType;
                if (type == typeof(double)) choices[i] = new object[] { sim.Now - 1, sim.Now + 1 };
                else if (type == typeof(int)) choices[i] = new object[] { 0, 3, sim.State.LastBeat };
                else if (type == typeof(EntryKind)) choices[i] = KindChoices();
                else if (parameters[i].IsOut) choices[i] = new object[] { null };
                else throw new NotSupportedException($"a read taking a {type.Name}: give it values here");
            }
            var args = new object[parameters.Length];
            return Product(choices, args, 0);
        }

        static object[] KindChoices()
        {
            var list = new List<object>();
            for (var kind = EntryKind.Beat; kind <= EntryKind.Timbre; kind++) list.Add(kind);
            return list.ToArray();
        }

        static IEnumerable<object[]> Product(object[][] choices, object[] args, int i)
        {
            if (i == choices.Length)
            {
                yield return (object[])args.Clone();
                yield break;
            }
            foreach (object c in choices[i])
            {
                args[i] = c;
                foreach (var a in Product(choices, args, i + 1)) yield return a;
            }
        }
    }
}
