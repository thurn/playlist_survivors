using UnityEditor;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// Caps the `--roster heroic` pack's textures at import (ps-f8a.3): the pack ships 4K maps,
    /// mostly imported at 2K but some at 4K. The elites' and the boss's maps stay at most 2K,
    /// since at 1K their hides soften in close stills (they fill a third of the frame); the
    /// fodder's take 1K, where close stills show no difference. Like KitTextureImport, it overrides
    /// at import only, so the vendor files and their synced .meta files stay as they are.
    /// </summary>
    public sealed class RosterTextureImport : AssetPostprocessor
    {
        const string HeroicPack = "Assets/Vendor/HeroicFantasyCreaturesFullPackVol1/";
        public const int LargeMax = 2048, FodderMax = 1024;
        static readonly string[] Large = { "/Troll/", "/Ogre/", "/Mountain Dragon/" };

        public override uint GetVersion() => 3;

        public static int MaxSize(string path)
        {
            if (!path.StartsWith(HeroicPack, System.StringComparison.Ordinal)) return 0;
            foreach (var l in Large)
                if (path.Contains(l)) return LargeMax;
            return FodderMax;
        }

        void OnPreprocessTexture()
        {
            int max = MaxSize(assetPath);
            if (max == 0) return;
            var importer = (TextureImporter)assetImporter;
            if (importer.maxTextureSize > max) importer.maxTextureSize = max;
            foreach (var platform in new[] { "Standalone", "Windows", "OSX", "iPhone", "Android" })
            {
                var s = importer.GetPlatformTextureSettings(platform);
                if (!s.overridden || s.maxTextureSize <= max) continue;
                s.maxTextureSize = max;
                importer.SetPlatformTextureSettings(s);
            }
        }
    }
}
