using NUnit.Framework;
using PS.Look.Crowd;
using UnityEngine;

namespace PS.Look.Tests
{
    /// <summary>The monster looks by option (MonsterLooks): an option without a built look draws the crystal stand-in.</summary>
    public sealed class MonsterLookTests
    {
        sealed class Plain : IMonsterLook
        {
            public string Name => "test-plain";
            public Material Body(CrowdLibrary lib, CrowdType type) => null;
            public Material Shards(CrowdLibrary lib, CrowdType type) => null;
        }

        [Test]
        public void AnOptionWithoutALookDrawsTheCrystal()
        {
            Assert.AreSame(MonsterLooks.Crystal, MonsterLooks.Of(null));
            Assert.AreSame(MonsterLooks.Crystal, MonsterLooks.Of("not-built-yet"));
            Assert.AreEqual("crystal", MonsterLooks.Crystal.Name);
        }

        [Test]
        public void ARegisteredLookDrawsItsOption()
        {
            var look = new Plain();
            MonsterLooks.Register(look);
            Assert.AreSame(look, MonsterLooks.Of("test-plain"));
        }

        [Test]
        public void TheCrystalDrawsTheLibrarysShaders()
        {
            var lib = ScriptableObject.CreateInstance<CrowdLibrary>();
            lib.Crystal = Shader.Find("Hidden/InternalErrorShader");
            lib.ShardShader = lib.Crystal;
            var type = new CrowdType { Id = "blob" };
            var body = MonsterLooks.Crystal.Body(lib, type);
            var shards = MonsterLooks.Crystal.Shards(lib, type);
            Assert.AreSame(lib.Crystal, body.shader);
            Assert.AreSame(lib.ShardShader, shards.shader);
            Assert.AreEqual("blob_crystal", body.name);
            Assert.AreEqual("blob_shards", shards.name);
            Object.DestroyImmediate(body);
            Object.DestroyImmediate(shards);
            Object.DestroyImmediate(lib);
        }
    }
}
