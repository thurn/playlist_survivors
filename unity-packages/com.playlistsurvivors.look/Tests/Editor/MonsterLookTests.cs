using NUnit.Framework;
using PS.Look.Crowd;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The monster looks by option (MonsterLooks): an option without a built look draws the stand-in, the gem; classic the crystal.</summary>
    public sealed class MonsterLookTests
    {
        sealed class Plain : IMonsterLook
        {
            public string Name => "test-plain";
            public Material Body(CrowdLibrary lib, CrowdType type) => null;
            public Material Shards(CrowdLibrary lib, CrowdType type) => null;
        }

        [Test]
        public void AnOptionWithoutALookDrawsTheGem()
        {
            Assert.IsInstanceOf<GemLook>(MonsterLooks.StandIn);
            Assert.AreSame(MonsterLooks.StandIn, MonsterLooks.Of(null));
            Assert.AreSame(MonsterLooks.StandIn, MonsterLooks.Of("not-built-yet"));
            Assert.AreSame(MonsterLooks.StandIn, MonsterLooks.Of(GemLook.Option));
        }

        [Test]
        public void TheClassicOptionDrawsTheCrystal()
        {
            var before = ResolvedLook.Active;
            try
            {
                ResolvedLook.Resolve(new LookFlags { Look = "classic" }).Apply();
                Assert.AreSame(MonsterLooks.Classic, MonsterLooks.Active);
                ResolvedLook.Resolve(new LookFlags { Look = "prism" }).Apply();
                Assert.IsInstanceOf<GemLook>(MonsterLooks.Active);
            }
            finally
            {
                before?.Apply();
            }
        }

        [Test]
        public void TheCrystalDrawsTheLibrarysShaders()
        {
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.Crystal = Shader.Find("Hidden/InternalErrorShader");
            lib.ShardShader = lib.Crystal;
            var type = new CrowdType { Id = "blob" };
            var body = MonsterLooks.Classic.Body(lib, type);
            var shards = MonsterLooks.Classic.Shards(lib, type);
            Assert.AreSame(lib.Crystal, body.shader);
            Assert.AreSame(lib.ShardShader, shards.shader);
            Assert.AreEqual("blob_crystal", body.name);
            Assert.AreEqual("blob_shards", shards.name);
            Object.DestroyImmediate(body);
            Object.DestroyImmediate(shards);
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void EveryNeonLooksBodyDrawsADarkOutline()
        {
            // The swarm-separation rule (neon-look-style.md): a clump's touching rims part on a dark seam.
            foreach (var option in new[] { EdgeLook.Option, SolidLook.Option, HologramLook.Option, GemLook.Option })
            {
                var body = MonsterLooks.Of(option).Body(null, new CrowdType { Id = "skeleton" });
                try
                {
                    Assert.That(body.FindPass("Outline"), Is.GreaterThanOrEqualTo(0), option);
                    Assert.That(body.GetFloat("_OutlinePx"), Is.GreaterThan(0), option);
                }
                finally
                {
                    Object.DestroyImmediate(body);
                }
            }
        }

        [Test]
        public void ARegisteredLookDrawsItsOption()
        {
            var look = new Plain();
            MonsterLooks.Register(look);
            Assert.AreSame(look, MonsterLooks.Of("test-plain"));
        }
    }
}
