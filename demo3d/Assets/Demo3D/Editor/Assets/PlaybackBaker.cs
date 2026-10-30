using System.IO;
using System.Linq;
using Demo3D.Runtime.Playback;
using UnityEditor;
using UnityEngine;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>Bakes the player's PlaybackData index into Assets/Baked/Playback/Resources/.</summary>
    public static class PlaybackBaker
    {
        const string Dir = "Assets/Baked/Playback/Resources";
        const string ChoreographyData = "Assets/Demo3D/Choreography/Data";

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("Playback", Bake);

        public static void Bake()
        {
            Directory.CreateDirectory(Dir);
            // Setup writes action.ogg, or action.wav where ffmpeg has no libvorbis (the Mac).
            var musicPath = File.Exists("Assets/Baked/Audio/action.ogg") ? "Assets/Baked/Audio/action.ogg" : "Assets/Baked/Audio/action.wav";
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
            TextAsset Text(string path) => AssetDatabase.LoadAssetAtPath<TextAsset>(path)
                ?? throw new System.Exception("PlaybackBaker: missing " + path);
            var files = Directory.GetFiles(ChoreographyData, "*.json").Select(p => p.Replace('\\', '/')).ToArray();
            var data = ScriptableObject.CreateInstance<PlaybackData>();
            data.songMap = Text("Assets/Baked/Audio/action.songmap.json");
            data.music = music;
            data.level = Text(ChoreographyData + "/action-level.json");
            data.levels = files.Where(f => Path.GetFileName(f).StartsWith("action-level-")).Select(Text).ToArray();
            data.annotations = Text("Assets/Demo3D/Data/clip-annotations.json");
            data.roster = Text(ChoreographyData + "/roster.json");
            data.footprints = Text(ChoreographyData + "/footprints.json");
            data.layouts = files.Where(f => f.EndsWith("-layout.json")).Select(Text).ToArray();
            data.environments = files.Where(f => Path.GetFileName(f).StartsWith(PlaybackData.EnvironmentPrefix)).Select(Text).ToArray();
            data.stressScripts = files.Where(f => Path.GetFileName(f).StartsWith("stress-")).Select(Text).ToArray();
            var path = Dir + "/" + PlaybackData.ResourcePath + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"PlaybackBaker: {data.levels.Length} level variants, {data.layouts.Length} layouts, {data.environments.Length} environments, {data.stressScripts.Length} stress scripts, music {(music != null ? "yes" : "MISSING")}");
        }
    }
}
