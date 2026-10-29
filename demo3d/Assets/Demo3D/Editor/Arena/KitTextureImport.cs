using Demo3D.Runtime.Arena;
using UnityEditor;

namespace Demo3D.EditorTools.Arena
{
    /// <summary>
    /// The import override behind rung E5's texture cap (TD14; environment.md, "texture caps are
    /// applied by our code"): every texture of the kit's four classic packs imports into the
    /// <see cref="ArenaPipeline.KitTextureGroup"/> mipmap limit group, whose limit the runtime sets
    /// (<see cref="ArenaPipeline"/>), so the cap needs no edit to a vendor file or its import
    /// settings: the override applies at import, and the vendor .meta files stay as synced. With
    /// the group's limit at 0 (the default) nothing changes.
    /// </summary>
    public sealed class KitTextureImport : AssetPostprocessor
    {
        static readonly string[] Packs =
        {
            "Assets/Vendor/PolygonFantasyKingdom/", "Assets/Vendor/PolygonAdventure/",
            "Assets/Vendor/PolygonElvenRealm/", "Assets/Vendor/PolygonDarkFantasy/",
        };

        public override uint GetVersion() => 1;

        public static bool IsKit(string path)
        {
            foreach (var p in Packs)
                if (path.StartsWith(p, System.StringComparison.Ordinal)) return true;
            return false;
        }

        void OnPreprocessTexture()
        {
            if (!IsKit(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            importer.mipmapLimitGroupName = ArenaPipeline.KitTextureGroup;
            importer.ignoreMipmapLimit = false;
        }
    }
}
