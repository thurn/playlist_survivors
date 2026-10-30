using System;
using UnityEngine;

namespace StyleLab.Runtime
{
    /// <summary>
    /// Everything the player needs that only the editor can read, baked by `stylelab unity Bake`
    /// into Assets/Baked/Resources (gitignored, since it references vendor assets): the roster's
    /// prefabs, merged style meshes, albedo textures and animator controllers, the song and its beats,
    /// and the shaders (referenced here so the build includes them).
    /// </summary>
    public sealed class StyleLabData : ScriptableObject
    {
        public const string ResourcePath = "StyleLabData";

        public TypeData[] types;
        public AudioClip song;
        public float[] beats;
        public int firstDownbeat;
        public int beatsPerBar = 4;
        public float[] energy; // per bar
        public Shader[] shaders;

        public TypeData Type(string id) => Array.Find(types, t => t.id == id)
            ?? throw new InvalidOperationException($"StyleLabData: no type {id}");

        public Shader Shader(string name) => Array.Find(shaders, s => s != null && s.name == name)
            ?? throw new InvalidOperationException($"StyleLabData: no shader {name}; rebake (stylelab unity Bake)");
    }

    [Serializable]
    public sealed class TypeData
    {
        public string id;
        public string kind; // fodder, elite, boss or hero
        public bool humanoid;
        public float radius;
        public float heightM;
        public float hoverM;
        public float nativeHeight; // the prefab's height at scale 1, in its bind pose
        public GameObject prefab;
        public WeaponData[] weapons;
        public RuntimeAnimatorController controller;
        public string[] states;
        public Mesh mesh;          // the whole figure merged into one style mesh (StyleMesh), in root space
        public Texture2D[] albedo; // one per submesh
        public string[] bones;     // the mesh's bones, as paths from the figure's root
        public Vector2 lumRange;   // the albedo's value range over the texels the mesh uses (linear)
    }

    [Serializable]
    public sealed class WeaponData
    {
        public GameObject prefab;
        public string bone;
    }
}
