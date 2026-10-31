using System;
using System.IO;
using System.Linq;
using Game.Client.Hero;
using Game.EditorTools.Arena;
using PS.Game.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools.Hero
{
    /// <summary>
    /// Bakes the hero for the player (unity-game.md, The Hero; demo3d's RosterBaker for its hero),
    /// from game/Content/hero-clips.json. `game unity BakeHero`; `game build` runs it when its inputs
    /// change. Into the gitignored Assets/Baked/Hero/ (derived from vendor assets,
    /// docs/tech-demo-3d/vendor-assets.md): the knight with its shield in its left hand, scaled to the
    /// hero's height, its materials converted to URP (the arena's MaterialConverter, with the emission
    /// on for the low-health pulse), with no controller or vendor scripts, as a prefab; and the
    /// player's <see cref="HeroBake"/>, with the clips in <see cref="HeroClip"/> order and the blade's shader.
    /// </summary>
    public static class HeroBaker
    {
        public const string Dir = "Assets/Baked/Hero";
        public const string MaterialDir = Dir + "/Materials";
        public const string BladeShaderPath = "Assets/Game/Client/Hero/Shaders/CrystalBlade.shader";

        public static void Bake()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Dir + "/Resources");
            string json = File.ReadAllText(HeroClips.ContentPath);
            var table = HeroClips.Parse(json);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(table.prefab, "the knight"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "Knight";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            float native = MeasureHeight(go);
            if (native < 0.05f) throw new Exception("HeroBaker: the knight has no skinned mesh to measure");
            go.transform.localScale *= table.heightM / native;

            var animator = go.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) throw new Exception("HeroBaker: the knight has no humanoid Animator");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // The shield rides the left hand at the character's scale: the hand bone's would draw it about 1 cm wide.
            var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var shield = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(table.shield, "the shield"));
            PrefabUtility.UnpackPrefabInstance(shield, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            shield.transform.SetParent(leftHand, false);
            shield.transform.localScale = Vector3.one * (go.transform.lossyScale.x / leftHand.lossyScale.x);

            foreach (var tr in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(mb); // vendor demo scripts never run in the player
            var converter = new MaterialConverter(MaterialDir, JsonValue.Parse("{}"));
            converter.Convert(go);
            foreach (var m in go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
            {
                if (m.IsKeywordEnabled("_EMISSION")) continue;
                m.SetColor("_EmissionColor", Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(m);
            }
            var knight = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/Knight.prefab");
            UnityEngine.Object.DestroyImmediate(go);

            var bake = ScriptableObject.CreateInstance<HeroBake>();
            bake.knight = knight;
            bake.clips = table.clips.Select(c => LoadClip(c.guid, c.clip)).ToArray();
            bake.clipsJson = json;
            bake.blade = AssetDatabase.LoadAssetAtPath<Shader>(BladeShaderPath) ?? throw new Exception("HeroBaker: no blade shader at " + BladeShaderPath);
            var path = $"{Dir}/Resources/{HeroBake.ResourcePath}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(bake, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"HeroBaker: the knight ({native:F2} m tall, scaled to {table.heightM} m), {converter.Count} URP materials and " +
                      $"{bake.clips.Length} clips into {Dir}");
        }

        static T Load<T>(string guid, string what) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid))
            ?? throw new Exception($"HeroBaker: {what} ({guid}) is missing; run `game setup`");

        static AnimationClip LoadClip(string guid, string name)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            return clips.FirstOrDefault(c => c.name == name) ?? clips.FirstOrDefault()
                ?? throw new Exception($"HeroBaker: no clip {name} in {guid} ({path}); run `game setup`");
        }

        /// <summary>The character's height in its bind pose: its skinned meshes' bounds through their transforms.</summary>
        static float MeasureHeight(GameObject go)
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (r.sharedMesh == null) continue;
                var b = r.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
                    float y = r.transform.TransformPoint(c).y;
                    top = Mathf.Max(top, y);
                    bottom = Mathf.Min(bottom, y);
                }
            }
            return top == float.MinValue ? 0 : top - Mathf.Min(bottom, go.transform.position.y);
        }
    }
}
