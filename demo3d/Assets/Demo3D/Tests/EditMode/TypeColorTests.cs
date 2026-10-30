using System;
using System.IO;
using System.Linq;
using Demo3D.EditorTools.Assets;
using LevelScript = PS.Choreography.LevelScript;
using NUnit.Framework;

namespace Demo3D.Tests
{
    /// <summary>The look pass's signature colour per monster type (ps-e8r.8, roster.json's hues).</summary>
    public class TypeColorTests
    {
        const float MinHueGapDeg = 35f;

        [Test]
        public void EveryMonsterTypeHasAHue()
        {
            foreach (var t in Roster.Load().Where(t => t.kind != "hero"))
                Assert.That(t.hue, Is.Not.Null.And.InRange(0f, 360f), t.id);
            Assert.That(Roster.TypeColors().dye, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
        }

        [Test]
        public void TypesThatShareASectionDifferInHue()
        {
            var roster = Roster.Load();
            var level = LevelScript.Parse(File.ReadAllText("Assets/Demo3D/Choreography/Data/action-level.json"));
            float HueOf(string type) =>
                (roster.Find(t => t.id == type) ?? roster.Single(t => t.kind == "boss")).hue
                ?? throw new Exception($"{type} has no hue");
            int pairs = 0;
            foreach (var s in level.Sections)
            {
                var types = s.Mix.Select(m => m.Type).Concat(s.Boss != null ? new[] { s.Boss } : new string[0]).Distinct().ToArray();
                for (int a = 0; a < types.Length; a++)
                for (int b = a + 1; b < types.Length; b++)
                {
                    float d = Math.Abs(HueOf(types[a]) - HueOf(types[b])) % 360f;
                    Assert.That(Math.Min(d, 360f - d), Is.GreaterThanOrEqualTo(MinHueGapDeg), $"{s.Section}: {types[a]} and {types[b]}");
                    pairs++;
                }
            }
            Assert.That(pairs, Is.GreaterThan(30), "the climax alone pairs nine types");
        }
    }
}
