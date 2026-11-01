using NUnit.Framework;
using PS.Look.Crowd;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The solid-emissive look (SolidLook): its option, its palette colours and its outline pass.</summary>
    public sealed class SolidLookTests
    {
        [Test]
        public void TheSolidLookDrawsItsOptionInTheTypesPaletteSlotWithADarkOutline()
        {
            Assert.IsTrue(LookAxes.Monsters.IsBuilt(SolidLook.Option));
            Assert.IsInstanceOf<SolidLook>(MonsterLooks.Of("solid-emissive"));
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            foreach (var id in new[] { "skeleton", "goblin", "troll", "dragon" })
            {
                var type = new CrowdType { Id = id };
                var body = MonsterLooks.Of(SolidLook.Option).Body(null, type);
                var shards = MonsterLooks.Of(SolidLook.Option).Shards(null, type);
                try
                {
                    Assert.AreEqual("Look/MonsterSolid", body.shader.name);
                    Assert.AreEqual("Look/MonsterSolidShard", shards.shader.name);
                    var want = (Vector4)MonsterSlots.Colour(palette, id).linear;
                    Assert.That(Vector4.Distance(want, body.GetColor("_Tint")), Is.LessThan(1e-4f), id);
                    Assert.That(Vector4.Distance(want, shards.GetColor("_Tint")), Is.LessThan(1e-4f), id);
                    var outline = (Vector4)palette.VoidFloor.linear;
                    Assert.That(Vector4.Distance(outline, body.GetColor("_Outline")), Is.LessThan(1e-4f), id);
                    Assert.That(body.FindPass("Outline"), Is.GreaterThanOrEqualTo(0), "the body draws its outline hull");
                    Assert.That(shards.FindPass("Outline"), Is.GreaterThanOrEqualTo(0), "the shards keep the outline");
                }
                finally
                {
                    Object.DestroyImmediate(body);
                    Object.DestroyImmediate(shards);
                }
            }
        }

        [Test]
        public void TheSolidPresetNamesTheSolidLook()
        {
            var look = ResolvedLook.Resolve(LookFlags.Parse(new[] { "--look", "solid" }));
            Assert.AreEqual(SolidLook.Option, look.Option(LookAxes.Monsters));
        }
    }
}
