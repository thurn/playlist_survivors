using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo2D.Runtime.Playback;
using UnityEditor;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// Bakes the player's PlaybackData index into Assets/Baked/Playback/Resources/ (PX5): the
    /// baked song and music, demo2d's choreography data, PX3's atlases, the shaders and materials
    /// the client creates at runtime (so the build keeps them), and the arena's vendor textures and
    /// tiles (PX6; the client composes the arena from its JSON at load, so no bake of it goes stale),
    /// the effect library's data and CFXR prefabs and the night's data (PX7), and the sound bank
    /// (PX8: demo3d's choices from the reused packs, one clip per timeline sound name).
    /// Needs `demo2d setup` (the song); bakes the atlases (SpriteBaker) when they're missing.
    /// Every build runs it.
    /// </summary>
    public static class PlaybackBaker
    {
        const string Dir = "Assets/Baked/Playback/Resources";
        const string AudioDir = "Assets/Baked/Audio";
        public const string SpriteFlashPath = "Assets/Demo2D/Runtime/Shaders/Sprite-Lit-Flash.shader";
        public const string UpscalePath = "Assets/Demo2D/Runtime/Shaders/SharpUpscale.shader";
        public const string GlowPath = "Assets/Demo2D/Runtime/Shaders/Glow.shader";
        const string SpriteBakerSource = "Assets/Demo2D/Editor/Assets/SpriteBaker.cs";
        public const string SpriteUnlitPath ="Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat";
        const string ForestDir = "Assets/Vendor/RPGW_AncientForest";

        public static void Bake()
        {
            Directory.CreateDirectory(Dir);

            // Setup writes action.ogg, or action.wav where ffmpeg has no libvorbis (the Mac).
            var musicPath = File.Exists(AudioDir + "/action.ogg") ? AudioDir + "/action.ogg" : AudioDir + "/action.wav";
            var music = AssetDatabase.LoadAssetAtPath<AudioClip>(musicPath);
            if (music != null)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(musicPath);
                var s = importer.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.Streaming)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    importer.defaultSampleSettings = s;
                    importer.SaveAndReimport();
                }
            }

            var data = ScriptableObject.CreateInstance<PlaybackData>();
            data.songMap = Load<TextAsset>(AudioDir + "/action.songmap.json");
            data.music = music;
            string Data(string file) => CharacterPacks.DataDir + "/" + file;
            data.level = Load<TextAsset>(Data("action-level.json"));
            data.annotations = Load<TextAsset>(Data("clip-annotations.json"));
            data.roster = Load<TextAsset>(Data("roster.json"));
            data.layouts = Directory.GetFiles(CharacterPacks.DataDir, "*-layout.json").OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => Load<TextAsset>(p.Replace('\\', '/'))).ToArray();

            var characters = CharacterPacks.Load();
            // The atlases follow the roster's heights, the library's clip choices and the baker
            // itself: a bake older than any of them would draw stale frames without an error.
            var inputs = new[] { Data("roster.json"), Data("clip-annotations.json"), SpriteBakerSource }.Max(File.GetLastWriteTimeUtc);
            if (characters.Any(c => !File.Exists($"{SpriteBaker.OutDir}/{c.Name}.png") ||
                                    File.GetLastWriteTimeUtc($"{SpriteBaker.OutDir}/{c.Name}.json") < inputs))
            {
                Debug.Log("PlaybackBaker: atlases missing or older than their inputs; baking them (SpriteBaker)");
                SpriteBaker.Run();
            }
            data.atlases = characters.Select(c => Load<Texture2D>($"{SpriteBaker.OutDir}/{c.Name}.png")).ToArray();
            data.atlasJson = characters.Select(c => Load<TextAsset>($"{SpriteBaker.OutDir}/{c.Name}.json")).ToArray();

            data.spriteFlash = Load<Shader>(SpriteFlashPath);
            data.upscale = Load<Shader>(UpscalePath);
            data.spriteLit = Load<Material>(ProjectSetup.SpriteLitPath);
            data.spriteUnlit = Load<Material>(SpriteUnlitPath);
            BakeArena(data);
            BakeEffects(data);
            BakeSounds(data);

            var path = Dir + "/" + PlaybackData.ResourcePath + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"PlaybackBaker: {data.atlases.Length} atlases, {data.layouts.Length} layouts, " +
                      $"{data.propSheets.Length} prop sheets, {data.sheetSprites.Length} sheet tiles, {data.soundClips.Length} sounds, music {(music != null ? "yes" : "MISSING")}");
        }

        static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("PlaybackBaker: missing " + path);

        /// <summary>The effect library, its prefabs (by the library's manifest paths) and the night's data.</summary>
        static void BakeEffects(PlaybackData data)
        {
            data.effects = Load<TextAsset>(CharacterPacks.DataDir + "/" + Demo2D.Choreography.EffectLibrary.File);
            data.night = Load<TextAsset>(CharacterPacks.DataDir + "/night-lighting.json");
            data.glow = Load<Shader>(GlowPath);
            var library = Demo2D.Choreography.EffectLibrary.Parse(data.effects.text);
            data.effectPaths = library.Prefabs().ToArray();
            data.effectPrefabs = data.effectPaths.Select(p => Load<GameObject>(EffectGallery.VendorPath(p))).ToArray();
        }

        // Per sound category, the clips' file names in the vendor folder, one per variant: demo3d's
        // SoundBaker's choices (camera-and-sound.md), so the two demos sound alike.
        static readonly Dictionary<string, string[]> SoundChoices = new Dictionary<string, string[]>
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

        /// <summary>The sound bank: a vendor clip for every timeline sound name.</summary>
        static void BakeSounds(PlaybackData data)
        {
            var byName = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Vendor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .GroupBy(p => Path.GetFileNameWithoutExtension(p))
                .ToDictionary(g => g.Key, g => g.First());
            data.soundNames = PS.Choreography.Catalog.SoundNames();
            data.soundClips = data.soundNames.Select(name =>
            {
                var parts = name.Split('/');
                var choices = SoundChoices[parts[0]];
                var file = choices[int.Parse(parts[1]) % choices.Length];
                if (!byName.TryGetValue(file, out var path))
                    throw new Exception($"PlaybackBaker: no vendor clip {file} for {name} (is it in the manifest?)");
                return Load<AudioClip>(path);
            }).ToArray();
        }

        /// <summary>The arena's inputs: the 2D kit, the sheet's catalogue and tiles, and the props' textures.</summary>
        static void BakeArena(PlaybackData data)
        {
            data.forestProps = Load<TextAsset>(CharacterPacks.DataDir + "/forest-props.json");
            data.forestTiles = Load<TextAsset>(ForestTiles.CatalogPath);
            var catalog = ForestTiles.Load();
            var sheet = ForestTiles.Sprites(catalog.Sheet, catalog.TilePx).OrderBy(kv => kv.Key.y).ThenBy(kv => kv.Key.x).ToList();
            data.sheetCells = sheet.Select(kv => kv.Key).ToArray();
            data.sheetSprites = sheet.Select(kv => kv.Value).ToArray();
            data.propSheets = new[] { catalog.Props }
                .Concat(Directory.GetFiles(ForestDir + "/Animated", "*.png").OrderBy(p => p, StringComparer.Ordinal))
                .Select(p => Load<Texture2D>(p.Replace('\\', '/'))).ToArray();
        }
    }
}
