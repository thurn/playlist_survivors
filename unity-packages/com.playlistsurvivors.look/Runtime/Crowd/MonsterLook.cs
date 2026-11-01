using System.Collections.Generic;
using UnityEngine;

namespace PS.Look.Crowd
{
    /// <summary>
    /// How monsters are drawn (neon-look.md, The Axes &gt; Monsters): the materials a type's bodies
    /// (<see cref="BoneTextureCrowd"/>) and its shards (<see cref="MonsterShatter"/>) draw with. Every look draws the
    /// same instance buffer, palettes and beat globals; the caller owns each material it gets and destroys it.
    /// </summary>
    public interface IMonsterLook
    {
        /// <summary>The look's option on the monsters axis (<see cref="LookAxes.Monsters"/>), or "crystal" for the stand-in.</summary>
        string Name { get; }

        /// <summary>A new material for the type's bodies: MonsterCommon.hlsl's front end over the crowd's buffers.</summary>
        Material Body(CrowdLibrary lib, CrowdType type);

        /// <summary>A new material for the type's shards: MonsterShards.hlsl's front end over the shatter's buffers.</summary>
        Material Shards(CrowdLibrary lib, CrowdType type);
    }

    /// <summary>
    /// The monster looks by option. Until a look is built for an option, it draws <see cref="Crystal"/>, today's
    /// refractive crystal (MonsterCrystal and MonsterShard), so nothing changes for a preset that names it.
    /// </summary>
    public static class MonsterLooks
    {
        /// <summary>The stand-in: the crystal shaders the library names.</summary>
        public static readonly IMonsterLook Crystal = new CrystalLook();

        static readonly Dictionary<string, IMonsterLook> Built = new Dictionary<string, IMonsterLook>
        {
            [EdgeLook.Option] = new EdgeLook(),
            [SolidLook.Option] = new SolidLook(),
            [HologramLook.Option] = new HologramLook(false),
            [HologramLook.AllEdgesOption] = new HologramLook(true),
        };

        /// <summary>Registers the look built for <paramref name="look"/>'s option (list the option in Axes/Monsters.cs too).</summary>
        public static void Register(IMonsterLook look) => Built[look.Name] = look;

        /// <summary>The look for a monsters-axis option, or <see cref="Crystal"/> for null or an option without one.</summary>
        public static IMonsterLook Of(string option) =>
            option != null && Built.TryGetValue(option, out var look) ? look : Crystal;

        /// <summary>The applied preset's look (<see cref="ResolvedLook.Active"/>), or <see cref="Crystal"/> before one.</summary>
        public static IMonsterLook Active => Of(ResolvedLook.Active?.Option(LookAxes.Monsters));

        sealed class CrystalLook : IMonsterLook
        {
            public string Name => "crystal";

            public Material Body(CrowdLibrary lib, CrowdType type) =>
                new Material(lib.Crystal) { name = type.Id + "_crystal", enableInstancing = false };

            public Material Shards(CrowdLibrary lib, CrowdType type) =>
                new Material(lib.ShardShader) { name = type.Id + "_shards", enableInstancing = false };
        }
    }
}
