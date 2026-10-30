using System;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The `--monster-style` style meshes (crowd.md, Monster styles), a second asset beside the
    /// library so a player loads them only for a style that draws them (<see cref="MonsterStyle.UsesStyleMesh"/>):
    /// a ScriptableObject loads everything it references, and the library loads on every run.
    /// </summary>
    public sealed class CrowdStyleMeshes : ScriptableObject
    {
        public const string ResourcePath = "CrowdStyleMeshes";

        public Entry[] Types = Array.Empty<Entry>();

        /// <summary>
        /// A type's welded LOD0 to LOD2 unwelded (CrowdBaker.Unweld): the face normal in the
        /// tangent, barycentrics plus a value per triangle in UV3.
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public Mesh Lod0, Lod1, Lod2;
        }

        public static CrowdStyleMeshes Load() =>
            Resources.Load<CrowdStyleMeshes>(ResourcePath)
            ?? throw new InvalidOperationException(
                "CrowdStyleMeshes: Assets/Baked/Crowd/Resources/CrowdStyleMeshes.asset is missing (run `demo3d unity BakeCrowd`)");

        public Entry Of(string id) =>
            Array.Find(Types, e => e.Id == id)
            ?? throw new InvalidOperationException($"CrowdStyleMeshes: no style mesh for {id} (re-run `demo3d unity BakeCrowd`)");
    }
}
