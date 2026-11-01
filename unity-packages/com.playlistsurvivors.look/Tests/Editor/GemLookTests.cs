using NUnit.Framework;
using PS.Look.Crowd;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The refractive-gem look (GemLook): its option, its palette colours and the opaque texture it needs.</summary>
    public sealed class GemLookTests
    {
        [Test]
        public void TheGemDrawsItsOptionAndRefracts()
        {
            Assert.IsTrue(LookAxes.Monsters.IsBuilt(GemLook.Option));
            Assert.IsInstanceOf<GemLook>(MonsterLooks.Of("refractive-gem"));
            Assert.IsTrue(MonsterLooks.Refracts(MonsterLooks.Of(GemLook.Option)));
            Assert.IsTrue(MonsterLooks.Refracts(MonsterLooks.Classic), "the crystal refracts too");
            Assert.IsFalse(MonsterLooks.Refracts(MonsterLooks.Of(EdgeLook.Option)), "the edge look's glass is opaque");
        }

        [Test]
        public void TheGemDrawsInTheTypesSlotWithFringesInItsNeighbouringHues()
        {
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            foreach (var id in new[] { "goblin", "dragon" })
            {
                var type = new CrowdType { Id = id };
                var body = MonsterLooks.Of(GemLook.Option).Body(lib, type);
                var shards = MonsterLooks.Of(GemLook.Option).Shards(lib, type);
                Assert.AreEqual("Look/MonsterGem", body.shader.name);
                Assert.AreEqual("Look/MonsterGemShard", shards.shader.name);
                var slot = id == "dragon" ? palette.BossBody : palette.Enemies[2];
                var want = (Vector4)EdgeLook.Level(slot.linear);
                Assert.That(Vector4.Distance(want, body.GetColor("_Tint")), Is.LessThan(1e-4f), id);
                Assert.That(Vector4.Distance(want, shards.GetColor("_Tint")), Is.LessThan(1e-4f), id);
                Color.RGBToHSV(slot, out float h, out _, out _);
                foreach (var (fringe, turn) in new[] { ("_FringeA", -GemLook.FringeDeg), ("_FringeB", GemLook.FringeDeg) })
                {
                    var c = body.GetColor(fringe);
                    Color.RGBToHSV(c.gamma, out float fh, out _, out _);
                    Assert.That(Mathf.DeltaAngle(h * 360, fh * 360), Is.EqualTo(turn).Within(1.5f), $"{id} {fringe}");
                    Assert.That(Luma(c), Is.EqualTo(EdgeLook.HueLuminance).Within(1e-3f), $"{id} {fringe} levelled with the slot's hue");
                }
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(shards);
            }
            Object.DestroyImmediate(lib);
        }

        static float Luma(Color linear) => linear.r * 0.2126f + linear.g * 0.7152f + linear.b * 0.0722f;
    }
}
