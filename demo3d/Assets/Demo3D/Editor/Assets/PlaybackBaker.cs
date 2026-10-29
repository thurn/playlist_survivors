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
            var music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Baked/Audio/action.ogg");
            if (music != null)
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath("Assets/Baked/Audio/action.ogg");
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
            data.annotations = Text("Assets/Demo3D/Data/clip-annotations.json");
            data.roster = Text(ChoreographyData + "/roster.json");
            data.footprints = Text(ChoreographyData + "/footprints.json");
            data.layouts = files.Where(f => f.EndsWith("-layout.json")).Select(Text).ToArray();
            data.stressScripts = files.Where(f => Path.GetFileName(f).StartsWith("stress-")).Select(Text).ToArray();
            var path = Dir + "/" + PlaybackData.ResourcePath + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"PlaybackBaker: {data.layouts.Length} layouts, {data.stressScripts.Length} stress scripts, music {(music != null ? "yes" : "MISSING")}");
        }
    }
}
