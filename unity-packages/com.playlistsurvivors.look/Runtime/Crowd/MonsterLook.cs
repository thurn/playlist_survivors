using System;
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
        /// <summary>The look's option on the monsters axis (<see cref="LookAxes.Monsters"/>), or "classic" for the crystal.</summary>
        string Name { get; }

        /// <summary>A new material for the type's bodies: MonsterCommon.hlsl's front end over the crowd's buffers.</summary>
        Material Body(CrowdLibrary lib, CrowdType type);

        /// <summary>A new material for the type's shards: MonsterShards.hlsl's front end over the shatter's buffers.</summary>
        Material Shards(CrowdLibrary lib, CrowdType type);
    }

    /// <summary>
    /// The monster looks by option. Until a look is built for an option, it draws <see cref="StandIn"/>, the
    /// refractive gem (the old crystal's successor, ps-wnj.13); the classic option draws <see cref="Classic"/>.
    /// </summary>
    public static class MonsterLooks
    {
        /// <summary>The look for an option without one: the refractive gem.</summary>
        public static readonly IMonsterLook StandIn = new GemLook();

        /// <summary>
        /// The monsters' <see cref="LookAxis.Classic"/> option: the crystal before the epic (MonsterCrystal and
        /// MonsterShard, the shaders the library names), for parity comparisons, and nothing else.
        /// </summary>
        public static readonly IMonsterLook Classic = new CrystalLook();

        static readonly Dictionary<string, IMonsterLook> Built = new Dictionary<string, IMonsterLook>
        {
            [EdgeLook.Option] = new EdgeLook(),
            [SolidLook.Option] = new SolidLook(),
            [HologramLook.Option] = new HologramLook(false),
            [HologramLook.AllEdgesOption] = new HologramLook(true),
            [GemLook.Option] = StandIn,
        };

        /// <summary>Registers the look built for <paramref name="look"/>'s option (list the option in Axes/Monsters.cs too).</summary>
        public static void Register(IMonsterLook look) => Built[look.Name] = look;

        /// <summary>The look for a monsters-axis option, or <see cref="StandIn"/> for null or an option without one.</summary>
        public static IMonsterLook Of(string option) =>
            option != null && Built.TryGetValue(option, out var look) ? look : StandIn;

        /// <summary>
        /// The applied preset's look (<see cref="ResolvedLook.Active"/>): <see cref="Classic"/> for the classic option,
        /// <see cref="StandIn"/> before a look is applied.
        /// </summary>
        public static IMonsterLook Active
        {
            get
            {
                var look = ResolvedLook.Active;
                return look?.Requested(LookAxes.Monsters) == LookAxis.Classic ? Classic : Of(look?.Option(LookAxes.Monsters));
            }
        }

        /// <summary>Whether <paramref name="look"/> refracts the camera's opaque texture, which the renderers leave off.</summary>
        public static bool Refracts(IMonsterLook look) => look is GemLook || look == Classic;

        sealed class CrystalLook : IMonsterLook
        {
            public string Name => LookAxis.Classic;

            public Material Body(CrowdLibrary lib, CrowdType type) =>
                new Material(lib.Crystal ? lib.Crystal : throw new InvalidOperationException("CrowdLibrary has no crystal shader (re-bake the crowd)"))
                    { name = type.Id + "_crystal", enableInstancing = false };

            public Material Shards(CrowdLibrary lib, CrowdType type) =>
                new Material(lib.ShardShader ? lib.ShardShader : throw new InvalidOperationException("CrowdLibrary has no crystal shard shader (re-bake the crowd)"))
                    { name = type.Id + "_shards", enableInstancing = false };
        }
    }
}
