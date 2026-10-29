using System;
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
    /// tiles (PX6; the client composes the arena from its JSON at load, so no bake of it goes stale).
    /// Needs `demo2d setup` (the song); bakes the atlases (SpriteBaker) when they're missing.
    /// Every build runs it.
    /// </summary>
    public static class PlaybackBaker
    {
        const string Dir = "Assets/Baked/Playback/Resources";
        const string AudioDir = "Assets/Baked/Audio";
        public const string SpriteFlashPath = "Assets/Demo2D/Runtime/Shaders/Sprite-Lit-Flash.shader";
        public const string UpscalePath = "Assets/Demo2D/Runtime/Shaders/SharpUpscale.shader";
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

            var path = Dir + "/" + PlaybackData.ResourcePath + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"PlaybackBaker: {data.atlases.Length} atlases, {data.layouts.Length} layouts, " +
                      $"{data.propSheets.Length} prop sheets, {data.sheetSprites.Length} sheet tiles, music {(music != null ? "yes" : "MISSING")}");
        }

        static T Load<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("PlaybackBaker: missing " + path);

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
