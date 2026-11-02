using System;
using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// Which drawing of the monsters (plan.md, Rendering &gt; The edge look): <see cref="Trailer"/> is exactly the neon
    /// edge trailer's, the vendor meshes in MonsterEdge with <see cref="EdgeGains.Trailer"/>, and never changes;
    /// <see cref="Default"/> starts identical and is where the monster meshes and tuning land. `--look trailer` picks the
    /// first in the player, the stills and `champions monster review`.
    /// </summary>
    public enum MonsterLook
    {
        Default,
        Trailer,
    }

    public static class MonsterLooks
    {
        /// <summary>The look's name on the command line and in the baked folders ("default", "trailer").</summary>
        public static string Name(MonsterLook look) => look == MonsterLook.Trailer ? "trailer" : "default";

        /// <summary>The look a `--look` value names; anything but "trailer" (or nothing) is the default.</summary>
        public static MonsterLook Parse(string value) =>
            string.Equals(value, "trailer", StringComparison.OrdinalIgnoreCase) ? MonsterLook.Trailer : MonsterLook.Default;
    }

    /// <summary>
    /// The baked monster types of one look (<see cref="MonsterType"/>, from Resources/MonsterTypes/LOOK/, which
    /// `champions monster bake` writes) and the crowd's compute shaders (Resources/Crowd/). Types are in id order;
    /// presenters find theirs with <see cref="IndexOf"/>.
    /// </summary>
    public sealed class MonsterLibrary
    {
        /// <summary>The bone textures' rate: the trailer's crowd's.</summary>
        public const float ClipRate = 30f;

        /// <summary>Where a look's types load from, under a Resources folder.</summary>
        public static string TypesPath(MonsterLook look) => "MonsterTypes/" + MonsterLooks.Name(look);

        public readonly MonsterLook Look;
        public readonly MonsterType[] Types;
        public readonly ComputeShader Cull, Palette, Shards;

        /// <summary>A library of the given types (the tests' synthetic ones); <see cref="Load"/> for the baked.</summary>
        internal MonsterLibrary(MonsterLook look, MonsterType[] types)
        {
            Look = look;
            Types = types;
            Cull = Load<ComputeShader>("Crowd/CrowdCull");
            Palette = Load<ComputeShader>("Crowd/CrowdPalette");
            Shards = Load<ComputeShader>("Crowd/MonsterShards");
        }

        /// <summary>The look's baked types, or null when none is baked (a checkout without the vendor files).</summary>
        public static MonsterLibrary Load(MonsterLook look)
        {
            var types = Resources.LoadAll<MonsterType>(TypesPath(look));
            if (types.Length == 0) return null;
            Array.Sort(types, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new MonsterLibrary(look, types);
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i].Id == id) return i;
            return -1;
        }

        internal static T Load<T>(string path) where T : UnityEngine.Object =>
            Resources.Load<T>(path) ?? throw new InvalidOperationException($"MonsterLibrary: no Resources/{path}");
    }
}
