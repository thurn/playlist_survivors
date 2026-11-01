using NUnit.Framework;
using PS.Look.Crowd;
using UnityEditor;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The wireframe hologram look (HologramLook): its two options, its shaders and its colours.</summary>
    public sealed class HologramLookTests
    {
        [TestCase(HologramLook.Option, 0f)]
        [TestCase(HologramLook.AllEdgesOption, 1f)]
        public void EachWireframeOptionDrawsTheTypesSlotOnItsEdges(string option, float allEdges)
        {
            Assert.IsTrue(LookAxes.Monsters.IsBuilt(option));
            var look = MonsterLooks.Of(option);
            Assert.IsInstanceOf<HologramLook>(look);
            Assert.AreEqual(option, look.Name);
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            var palette = ResolvedLook.Active?.Palette ?? ResolvedLook.FallbackPalette;
            foreach (var id in new[] { "skeleton", "dragon" })
            {
                var type = new CrowdType { Id = id };
                var body = look.Body(lib, type);
                var shards = look.Shards(lib, type);
                Assert.AreEqual("Look/MonsterHologram", body.shader.name);
                Assert.AreEqual("Look/MonsterHologramShard", shards.shader.name);
                foreach (var shader in new[] { body.shader, shards.shader })
                    Assert.IsFalse(ShaderUtil.ShaderHasError(shader), $"{shader.name} compiles");
                var want = (Vector4)EdgeLook.Level((id == "dragon" ? palette.BossBody : palette.Enemies[0]).linear);
                foreach (var m in new[] { body, shards })
                {
                    Assert.That(Vector4.Distance(want, m.GetColor("_Tint")), Is.LessThan(1e-4f), $"{id} {m.name}");
                    Assert.AreEqual(allEdges, m.GetFloat("_AllEdges"), $"{id} {m.name}");
                }
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(shards);
            }
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void TheHologramPresetDrawsTheWireframeLook()
        {
            var look = ResolvedLook.Resolve(LookFlags.Parse(new[] { "player", "--look", "hologram" }, null));
            Assert.AreEqual(HologramLook.Option, look.Option(LookAxes.Monsters));
            Assert.IsInstanceOf<HologramLook>(MonsterLooks.Of(look.Option(LookAxes.Monsters)));
        }
    }
}
