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
    /// gitignored): a ready prefab per type and the RosterAssets index the player loads. A
    /// `--roster` set bakes only when asked for (`demo3d unity BakeRoster -roster NAME`), into its
    /// own Assets/Baked/Rosters/NAME/, so BakeAll never needs its packs.
    /// </summary>
    public static class RosterBaker
    {
        public const string Dir = "Assets/Baked/Roster";
        public const string SetsDir = "Assets/Baked/Rosters";

        [InitializeOnLoadMethod]
        static void Register() => Bakers.Register("Roster", Bake);

        public static void Bake() => Bake("default");

        public static void Bake(string set)
        {
            var rosterSet = RosterSet.Parse(set);
            var dir = rosterSet.IsDefault ? Dir : $"{SetsDir}/{set}";
            var materialDir = dir + "/Materials";
            if (!rosterSet.IsDefault) AssetDatabase.DeleteAsset(dir); // a rebake leaves no stale materials
            Directory.CreateDirectory(materialDir);
            Directory.CreateDirectory(dir + "/Resources");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var saved = new Dictionary<Material, Material>();
            var types = new List<RosterAssets.TypeAssets>();
            foreach (var t in Roster.Load(set).Where(t => t.kind != "rejected-boss"))
            {
                var go = Roster.Spawn(t, Vector3.zero, 0);
                go.transform.position = Vector3.zero; // hover is applied per instance
                go.name = t.id;
                if (!rosterSet.IsDefault) UrpMaterials.ConvertParticles(go); // a pack's auras and trails
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
                            // The hero takes the arena's static shadow map (TD9): URP Lit's
                            // passes and properties with the shared static-shadow lighting.
                            if (t.kind == "hero" && asset.shader != null && asset.shader.name == "Universal Render Pipeline/Lit")
                                asset.shader = Shader.Find("Demo3D/LitStaticShadow") ?? asset.shader;
                            // A set's glow maps: URP keeps _EMISSION on save only for a material
                            // flagged emissive, and the converted copies are flagged None.
                            if (!rosterSet.IsDefault && asset.HasProperty("_EmissionMap") && asset.GetTexture("_EmissionMap") != null)
                            {
                                asset.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                                asset.EnableKeyword("_EMISSION");
                            }
                            var path = AssetDatabase.GenerateUniqueAssetPath($"{materialDir}/{Safe(m.name)}.mat");
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
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/{t.id}.prefab");
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
            var indexPath = $"{dir}/Resources/{rosterSet.AssetsResource}.asset";
            AssetDatabase.DeleteAsset(indexPath);
            AssetDatabase.CreateAsset(index, indexPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"RosterBaker: baked {types.Count} types and {saved.Count} URP materials into {dir}");
        }

        static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
    }
}
