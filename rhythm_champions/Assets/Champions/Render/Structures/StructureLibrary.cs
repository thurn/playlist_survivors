using System;
using Champions.Render.Crowd;
using UnityEngine;

namespace Champions.Render.Structures
{
    /// <summary>
    /// The baked structure kinds of one look (<see cref="StructureType"/>, from Resources/StructureTypes/LOOK/, which
    /// `champions structure bake` writes) and the shaders that draw them (Resources/Structures/). `--look trailer` loads
    /// the vendor models unsimplified, the default look the tuned ones. Types are in id order; the presenter matches
    /// them to the sim's kinds by id.
    /// </summary>
    public sealed class StructureLibrary
    {
        public const string BodyShader = "Structures/StructureEdge", ShardShader = "Structures/StructureEdgeShard";

        /// <summary>Where a look's types load from, under a Resources folder.</summary>
        public static string TypesPath(MonsterLook look) => "StructureTypes/" + MonsterLooks.Name(look);

        public readonly StructureType[] Types;
        public readonly Shader Body, Shard;

        /// <summary>A library of the given types (the tests' synthetic ones); <see cref="Load"/> for the baked.</summary>
        public StructureLibrary(StructureType[] types)
        {
            Types = types;
            Body = MonsterLibrary.Load<Shader>(BodyShader);
            Shard = MonsterLibrary.Load<Shader>(ShardShader);
        }

        /// <summary>The look's baked kinds, or null when none is baked (a checkout without the vendor files).</summary>
        public static StructureLibrary Load(MonsterLook look)
        {
            var types = Resources.LoadAll<StructureType>(TypesPath(look));
            if (types.Length == 0) return null;
            Array.Sort(types, (a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new StructureLibrary(types);
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Types.Length; i++)
                if (Types[i].Id == id) return i;
            return -1;
        }

        /// <summary>The most pieces any type breaks into.</summary>
        public int MaxPieces
        {
            get
            {
                int n = 1;
                foreach (var t in Types) n = Math.Max(n, t.Pieces.Length);
                return n;
            }
        }
    }
}
