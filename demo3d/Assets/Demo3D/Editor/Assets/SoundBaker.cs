using System.Collections.Generic;
using System.IO;
using System.Linq;
using PS.Choreography;
using Demo3D.Runtime.Playback;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// Bakes the SoundBank (into Assets/Baked/Sounds/Resources/, since it references vendor
    /// clips): each timeline sound name, `category/variant`, gets one clip from TD2's chosen packs.
    /// </summary>
    public static class SoundBaker
    {
        const string Dir = "Assets/Baked/Sounds/Resources";

        // Per category, the clips' file names in the vendor folder, one per variant.
        static readonly Dictionary<string, string[]> Choices = new Dictionary<string, string[]>
        {
            ["swing"] = new[] { "SWORD_Whoosh_Hit_Armor_Hard_RR1_mono", "SWORD_Whoosh_Hit_Armor_Hard_RR3_mono", "SWORD_Whoosh_Hit_Armor_Hard_RR5_mono" },
            ["hit"] = new[] { "GORE_Blade_Chop_mono", "SWORD_Swing_Stab_Body_Gore_mono", "SWORD_Whoosh_Hit_Armor_Hard_RR6_mono" },
            ["death"] = new[] { "CREATURE_Squeel_01_mono", "CREATURE_Squeel_03_mono", "CREATURE_Squeel_05_mono" },
            ["parry"] = new[] { "SWORD_Hit_Sword_Cling_01_RR1_mono", "SWORD_Hit_Sword_Cling_01_RR2_mono", "SWORD_Hit_Sword_Cling_01_RR3_mono" },
            ["ring"] = new[] { "RPG3_FireMagicBall_HeavyImpact01", "RPG3_FireMagicEpic_Impact01", "RPG3_FireMagicBall_HeavyImpact03" },
            ["bolt"] = new[] { "RPG3_FireMagicBall_Projectile01", "RPG3_FireMagicBall_Projectile02", "RPG3_Magic2_HeavyImpact01" },
            ["lightning"] = new[] { "RPG3_ElectricMagicEpic_LightningStrikeImpact01", "RPG3_ElectricMagicEpic_LightningStrikeImpact02", "Thunder 1" },
            ["laser"] = new[] { "RPG3_FireMagicFlameThrower_Shoot01", "RPG3_FireMagicFlameThrower_Shoot02", "RPG3_FireMagicFlameThrower_Shoot03" },
        };

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("Sounds", Bake);

        public static void Bake()
        {
            var byName = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Vendor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .GroupBy(p => Path.GetFileNameWithoutExtension(p))
                .ToDictionary(g => g.Key, g => g.First());
            var names = Catalog.SoundNames();
            var clips = new AudioClip[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var parts = names[i].Split('/');
                var file = Choices[parts[0]][int.Parse(parts[1]) % Choices[parts[0]].Length];
                if (!byName.TryGetValue(file, out var path))
                    throw new System.Exception($"SoundBaker: no vendor clip {file} for {names[i]} (is it in the manifest?)");
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            Directory.CreateDirectory(Dir);
            var bank = ScriptableObject.CreateInstance<SoundBank>();
            bank.names = names;
            bank.clips = clips;
            var asset = $"{Dir}/{SoundBank.ResourcePath}.asset";
            AssetDatabase.DeleteAsset(asset);
            AssetDatabase.CreateAsset(bank, asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"SoundBaker: {names.Length} sounds");
        }
    }
}
