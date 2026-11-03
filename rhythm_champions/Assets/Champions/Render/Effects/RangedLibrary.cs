using UnityEngine;
namespace Champions.Render
{
    // Local source references and generated facet meshes live only under ignored Assets/Baked.
    public sealed class RangedLibrary : ScriptableObject
    {
        public Mesh Spear, Bow, Arrow;
        public Shader Shader;
        public static RangedLibrary Load() => Resources.Load<RangedLibrary>("RangedWeapons");
    }
}
