using System.IO;
using PS.Choreography;
using Demo3D.Runtime.Playback;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>TD4's client-side timeline evaluation on a real stress timeline.</summary>
    public class TimelineEvaluatorTests
    {
        static Timeline _tl;
        static ChoreographyData _data;
        static TimelineEvaluator _eval;

        [OneTimeSetUp]
        public void Build()
        {
            if (_tl != null) return;
            string dir = "Assets/Demo3D/Choreography/Data/";
            _data = ChoreographyData.FromJson(File.ReadAllText("Assets/Demo3D/Data/clip-annotations.json"),
                File.ReadAllText(dir + "roster.json"), File.ReadAllText(dir + "open-ground-layout.json"));
            var stress = StressScript.Parse(File.ReadAllText(dir + "stress-s2.json")).With(n: 300, seconds: 20);
            _tl = Choreographer.RunStress(stress, _data);
            _eval = new TimelineEvaluator(_tl, _data.Clips);
        }

        [Test]
        public void EveryBlowLandsOnItsImpact()
        {
            int checkedActions = 0;
            for (int i = 0; i < _tl.Actions.Length; i++)
            {
                var a = _tl.Actions[i];
                // A hit or death on the same beat interrupts an attack, which is then not drawn.
                if (a.Kind != ActionKind.Attack || a.Clip < 0 || _eval.Interrupted(i)) continue;
                var ann = _data.Clips[a.Clip];
                var st = _eval.Evaluate(a.Entity, a.Impact, 1f, 1f, 0f);
                // At the impact the playing clip (slot A) is the attack, at its annotated impact.
                Assert.That(st.A.Clip, Is.EqualTo(a.Clip), $"entity {a.Entity} at {a.Impact}");
                Assert.That(st.A.Time, Is.EqualTo(ann.Impact).Within(0.002f), $"entity {a.Entity} at {a.Impact}");
                checkedActions++;
            }
            Assert.That(checkedActions, Is.GreaterThan(20));
        }

        [Test]
        public void HitsAndDeathsPlayTheirClipsAtTheirImpacts()
        {
            int hits = 0, deaths = 0;
            for (int i = 0; i < _tl.Actions.Length; i++)
            {
                var a = _tl.Actions[i];
                if ((a.Kind != ActionKind.Hit && a.Kind != ActionKind.Death) || a.Clip < 0 || _eval.Interrupted(i)) continue;
                var st = _eval.Evaluate(a.Entity, a.Impact + 0.001, 1f, 1f, 0f);
                Assert.That(st.A.Clip, Is.EqualTo(a.Clip));
                Assert.That(st.HitFlash, Is.GreaterThan(0.9f));
                if (a.Kind == ActionKind.Hit) hits++; else deaths++;
            }
            Assert.That(hits, Is.GreaterThan(10));
            Assert.That(deaths, Is.GreaterThan(10));
        }

        [Test]
        public void DeathsHoldABeatThenDissolveOverABeat()
        {
            foreach (var a in _tl.Actions)
            {
                if (a.Kind != ActionKind.Death) continue;
                double beat = _eval.BeatLength(a.Impact);
                Assert.That(_eval.Evaluate(a.Entity, a.Impact + 0.01, 1f, 1f, 0f).Dissolve, Is.EqualTo(0f));
                var late = _eval.Evaluate(a.Entity, System.Math.Min(_tl.Entities[a.Entity].Despawn - 1e-3, a.Impact + 10), 1f, 1f, 0f);
                Assert.That(late.Dying, Is.True);
                Assert.That(beat, Is.GreaterThan(0.2).And.LessThan(1.0));
                return;
            }
            Assert.Fail("no deaths");
        }

        [Test]
        public void CrossfadesBlendFromTheLeavingClip()
        {
            int seen = 0;
            foreach (var a in _tl.Actions)
            {
                if (a.Kind != ActionKind.Attack) continue;
                // Just after an attack's clip starts, slot B still holds the clip it left.
                var start = a.ClipStart;
                var st = _eval.Evaluate(a.Entity, start + 0.03, 1f, 1f, 0f);
                if (st.Blend < 1f)
                {
                    Assert.That(st.Blend, Is.InRange(0f, 1f));
                    // The leaving slot is another clip, or the same clip at another time (attack after attack).
                    Assert.That(st.B.Clip != st.A.Clip || System.Math.Abs(st.B.Time - st.A.Time) > 0.01f, Is.True);
                    seen++;
                }
            }
            Assert.That(seen, Is.GreaterThan(5));
        }

        [Test]
        public void HitStopHoldsThenCatchesUpWithoutAJump([Values(0f, 0.6f)] float rate)
        {
            const double impact = 10.0;
            double prev = double.NegativeInfinity;
            for (double t = impact - 0.1; t < impact + 0.5; t += 0.001)
            {
                double local = TimelineEvaluator.HoldThenCatchUp(t, impact, rate);
                Assert.That(local, Is.GreaterThanOrEqualTo(prev - 1e-9), "never runs backwards");
                if (prev > double.NegativeInfinity)
                    Assert.That(local - prev, Is.LessThan(0.003), $"no jump at {t}");
                prev = local;
            }
            Assert.That(TimelineEvaluator.HoldThenCatchUp(impact, impact, rate), Is.EqualTo(impact));
            Assert.That(TimelineEvaluator.HoldThenCatchUp(impact + 0.3, impact, rate), Is.EqualTo(impact + 0.3).Within(1e-9));
        }

        [Test]
        public void TicksFlashDimmerAndShorterThanHits()
        {
            int ticks = 0;
            foreach (var a in _tl.Actions)
            {
                if (a.Kind != ActionKind.Tick) continue;
                bool alsoStruck = false;
                foreach (var b in _tl.ActionsOf(a.Entity))
                    if (b.Kind != ActionKind.Tick && b.Kind != ActionKind.Spawn && System.Math.Abs(b.Impact - a.Impact) < 0.13) alsoStruck = true;
                if (alsoStruck) continue;
                var st = _eval.Evaluate(a.Entity, a.Impact + 0.001, 1f, 1f, 0f);
                Assert.That(st.HitFlash, Is.LessThanOrEqualTo(TimelineEvaluator.TickFlash + 1e-4f));
                ticks++;
            }
            Assert.Pass($"{ticks} lone ticks checked");
        }

        [Test]
        public void PunchAndShakeStayWithinTheirBounds()
        {
            var feel = new CameraFeel(_tl);
            float maxShake = 0, minPunch = 1;
            for (double t = 0; t < _tl.Duration; t += 1.0 / 60)
            {
                maxShake = UnityEngine.Mathf.Max(maxShake, feel.Shake(t).magnitude);
                minPunch = UnityEngine.Mathf.Min(minPunch, feel.Punch(t));
                Assert.That(feel.Shake(t), Is.EqualTo(feel.Shake(t)), "a function of time");
            }
            Assert.That(maxShake, Is.GreaterThan(0f).And.LessThanOrEqualTo(CameraFeel.ShakeCap * 1.01f));
            Assert.That(minPunch, Is.InRange(1f - CameraFeel.PunchAmount - 1e-4f, 1f));
        }

        [Test]
        public void EvaluationIsAFunctionOfTime()
        {
            var e = _tl.Actions[_tl.Actions.Length / 2].Entity;
            var a = _eval.Evaluate(e, 12.345, 1.2f, 1f, 0f);
            _eval.Evaluate(e, 3.0, 1.2f, 1f, 0f);
            var b = _eval.Evaluate(e, 12.345, 1.2f, 1f, 0f);
            Assert.That(b.Position, Is.EqualTo(a.Position));
            Assert.That(b.A.Time, Is.EqualTo(a.A.Time));
            Assert.That(b.Blend, Is.EqualTo(a.Blend));
        }

        [Test]
        public void WalkPhaseFollowsDistance()
        {
            // Standing still freezes the walk; moving one stride advances it one cycle.
            for (int e = 1; e < _tl.EntityCount; e++)
            {
                if (_tl.Entities[e].Kind != EntityKind.Monster) continue;
                var segs = _tl.SegmentsOf(e);
                if (segs.Count < 3) continue;
                var s = segs.Array[segs.Offset + 1];
                float d0 = _eval.DistanceAt(e, s.T0), d1 = _eval.DistanceAt(e, s.T1);
                Assert.That(d1 - d0, Is.EqualTo(System.Numerics.Vector2.Distance(s.From, s.To)).Within(0.02f));
                return;
            }
        }
    }
}
