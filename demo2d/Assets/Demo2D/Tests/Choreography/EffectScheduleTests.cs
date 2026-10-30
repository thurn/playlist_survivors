using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PS.Choreography;

namespace Demo2D.Choreography.Tests
{
    /// <summary>PX7's effect schedule on the song's timeline: the beam against the sim's ticks, and each kind's cap.</summary>
    public class EffectScheduleTests
    {
        static Timeline Song => Fixtures.SongTimeline;

        static EffectSchedule Schedule() =>
            new EffectSchedule(Song, EffectLibrary.Parse(File.ReadAllText(ChoreographyFiles.DataPath(Fixtures.Root, EffectLibrary.File))), Fixtures.Data.Roster);

        /// <summary>The sim's largest shape scale (Sim.MaxShapeScale).</summary>
        const float MaxShapeScale = 4f;
        static readonly Box2 Everywhere = new Box2(-1e6f, -1e6f, 1e6f, 1e6f);

        /// <summary>
        /// At each of the beam's ticks the drawn beam is live and whole, and its yaw points at the
        /// ticked monster: the monster lies ahead of the hero and within the widest line's
        /// half-width (as the sim's line query inflates it) of the beam's axis.
        /// </summary>
        [Test]
        public void TheBeamPassesOverEveryMonsterItTicks()
        {
            var schedule = Schedule();
            var roster = Fixtures.Data.Roster;
            var beam = roster.Spells.Single(m => m.Shape == HitShape.Line);
            var live = new List<LiveEffect>();
            var none = new List<int>();
            var wrong = new List<string>();
            int ticks = 0;
            double worst = 0;
            foreach (ActionEvent a in Song.Actions)
            {
                if (a.Kind != ActionKind.Tick) continue;
                ticks++;
                double t = a.Impact;
                schedule.Live(t, Everywhere, none, live);
                var fx = live.OfType<EventEffect>().LastOrDefault(e => e.Data.Effect == beam.Effect);
                if (fx == null) { wrong.Add($"no beam at the tick on {a.Entity} at {t:F3}"); continue; }
                if (schedule.BeamAlpha(fx, t) != 1f) wrong.Add($"the beam at {t:F3} is fading");
                var hero = Song.PositionAt(fx.Data.AttachTo, t);
                var d = Song.PositionAt(a.Entity, t) - hero;
                var dir = Easing.Dir(schedule.Yaw(fx, t));
                float along = d.X * dir.X + d.Y * dir.Y, across = Math.Abs(d.X * dir.Y - d.Y * dir.X);
                // The sim widens the line by its shape scale (at most 4x, Sim.MaxShapeScale), and a monster counts within half its radius.
                float inflate = 0.5f * roster.Monsters[Song.Entities[a.Entity].Type].Radius;
                float half = beam.Width / 2 * MaxShapeScale + inflate;
                worst = Math.Max(worst, across);
                if (along < -inflate || across > half) wrong.Add($"the beam at {t:F3} passes {across:F2} m from {a.Entity} (half-width {half:F2} m), {along:F2} m ahead");
                if (wrong.Count > 10) break;
            }
            TestContext.WriteLine($"{ticks} ticks; the farthest ticked monster is {worst:F2} m from the beam's axis");
            Assert.That(ticks, Is.GreaterThan(50));
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));
        }

        /// <summary>Over the song, in the camera's view every 1/30 s, no kind has more than its max live.</summary>
        [Test]
        public void NoKindExceedsItsMax()
        {
            var schedule = Schedule();
            var live = new List<LiveEffect>();
            var active = new List<int>();
            var most = new Dictionary<EffectSpec, int>();
            var count = new Dictionary<EffectSpec, int>();
            for (double t = 0; t < Song.Duration; t += 1.0 / 30)
            {
                var f = Song.FramingAt(t);
                float w = f.ViewWidth, h = w * 9 / 16;
                active.Clear();
                for (int e = 0; e < Song.EntityCount; e++) if (Song.Exists(e, t)) active.Add(e);
                schedule.Live(t, new Box2(f.Focus.X - w / 2, f.Focus.Y - h / 2, f.Focus.X + w / 2, f.Focus.Y + h / 2), active, live);
                count.Clear();
                foreach (var fx in live) count[fx.Spec] = count.TryGetValue(fx.Spec, out var n) ? n + 1 : 1;
                foreach (var kv in count) most[kv.Key] = Math.Max(most.TryGetValue(kv.Key, out var m) ? m : 0, kv.Value);
            }
            foreach (var kv in most.OrderBy(kv => kv.Key.Name)) TestContext.WriteLine($"{kv.Key.Name}: at most {kv.Value} live (max {kv.Key.Max})");
            Assert.That(most.Where(kv => kv.Value > kv.Key.Max).Select(kv => kv.Key.Name), Is.Empty);
            Assert.That(most.Any(kv => kv.Value == kv.Key.Max), "some kind reaches its max");
        }

        /// <summary>Only an event turns: the yaw takes an event, which a flight is not.</summary>
        [Test]
        public void OnlyAnEventHasAYaw()
        {
            var yaw = typeof(EffectSchedule).GetMethod(nameof(EffectSchedule.Yaw));
            Assert.That(yaw.GetParameters()[0].ParameterType, Is.EqualTo(typeof(EventEffect)));
            Assert.That(typeof(EventEffect).IsAssignableFrom(typeof(FlightEffect)), Is.False);
        }
    }
}
