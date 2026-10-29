using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Runtime.Playback;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// Bakes the roster for the player into Assets/Baked/Roster/ (derived from vendor prefabs, so
    /// gitignored): a ready prefab per type and the RosterAssets index the player loads.
    /// </summary>
    public static class RosterBaker
    {
        public const string Dir = "Assets/Baked/Roster";
        const string MaterialDir = "Assets/Baked/Roster/Materials";

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("Roster", Bake);

        public static void Bake()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Dir + "/Resources");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var saved = new Dictionary<Material, Material>();
            var types = new List<RosterAssets.TypeAssets>();
            foreach (var t in Roster.Load().Where(t => t.kind != "rejected-boss"))
            {
                var go = Roster.Spawn(t, Vector3.zero, 0);
                go.transform.position = Vector3.zero; // hover is applied per instance
                go.name = t.id;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || AssetDatabase.Contains(m)) continue;
                        if (!saved.TryGetValue(m, out var asset))
                        {
                            asset = new Material(m);
                            var path = AssetDatabase.GenerateUniqueAssetPath($"{MaterialDir}/{Safe(m.name)}.mat");
                            AssetDatabase.CreateAsset(asset, path);
                            saved[m] = asset;
                        }
                        mats[i] = asset;
                    }
                    r.sharedMaterials = mats;
                }
                foreach (var tr in go.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    Object.DestroyImmediate(mb); // vendor demo scripts never run in the player
                var animator = go.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{t.id}.prefab");
                Object.DestroyImmediate(go);
                var roles = t.clips.Keys.ToArray();
                types.Add(new RosterAssets.TypeAssets
                {
                    id = t.id, kind = t.kind, prefab = prefab, roles = roles,
                    clips = roles.Select(role => Roster.LoadClip(t.clips[role])).ToArray(),
                    radius = t.radius, heightM = t.heightM, hoverM = t.hoverM,
                });
            }
            var index = ScriptableObject.CreateInstance<RosterAssets>();
            index.types = types.ToArray();
            var indexPath = $"{Dir}/Resources/{RosterAssets.ResourcePath}.asset";
            AssetDatabase.DeleteAsset(indexPath);
            AssetDatabase.CreateAsset(index, indexPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"RosterBaker: baked {types.Count} types and {saved.Count} URP materials into {Dir}");
        }

        static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
    }
}
