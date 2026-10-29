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
    /// the client creates at runtime (so the build keeps them), and the open ground's tiles.
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
        /// <summary>The open ground's extent in metres (tiles): the open-ground layout's goals with room around them.</summary>
        public static readonly RectInt GroundRect = new RectInt(-48, -84, 96, 120);

        public static void Bake()
        {
            Directory.CreateDirectory(Dir);
            T Load<T>(string path) where T : UnityEngine.Object =>
                AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new Exception("PlaybackBaker: missing " + path);

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
            BakeGround(data);

            var path = Dir + "/" + PlaybackData.ResourcePath + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"PlaybackBaker: {data.atlases.Length} atlases, {data.layouts.Length} layouts, " +
                      $"ground {data.groundW}x{data.groundH} of {data.groundSprites.Length} tiles, music {(music != null ? "yes" : "MISSING")}");
        }

        /// <summary>PX3's forest grass with moss and dirt over <see cref="GroundRect"/>, as tile indices.</summary>
        static void BakeGround(PlaybackData data)
        {
            var catalog = ForestTiles.Load();
            var r = GroundRect;
            int patches = Mathf.RoundToInt(7f * r.width * r.height / (30 * 17)); // the stills' density
            var map = SpriteStills.GroundMap(catalog, r.width, r.height, 3, patches);
            var sheet = ForestTiles.Sprites(catalog.Sheet, catalog.TilePx);
            var cells = new List<Vector2Int>();
            var index = new Dictionary<Vector2Int, int>();
            int Index(Vector2Int cell)
            {
                if (index.TryGetValue(cell, out int i)) return i;
                if (!sheet.ContainsKey(cell)) throw new Exception($"PlaybackBaker: the sheet has no tile at {cell}");
                cells.Add(cell);
                return index[cell] = cells.Count - 1;
            }
            data.groundW = r.width;
            data.groundH = r.height;
            data.groundOrigin = r.position;
            data.groundBase = new int[r.width * r.height];
            data.groundOverlay = new int[r.width * r.height];
            for (int row = 0; row < r.height; row++)
            for (int x = 0; x < r.width; x++)
            {
                int i = (r.height - 1 - row) * r.width + x; // the map's rows run from the top
                data.groundBase[i] = Index(map.Base);
                data.groundOverlay[i] = map.Overlay[x, row] is Vector2Int cell ? Index(cell) : -1;
            }
            data.groundSprites = cells.Select(c => sheet[c]).ToArray();
        }
    }
}
