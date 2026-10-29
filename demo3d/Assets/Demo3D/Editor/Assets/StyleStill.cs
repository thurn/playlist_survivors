using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// TD2's style stills: the roster and the kit together under night lighting from the game
    /// camera (environment.md, The kit), and the two boss candidates side by side. The scene is
    /// built in memory each run and never saved; only the tagged JPEGs are kept.
    /// </summary>
    public static class StyleStill
    {
        const float Pitch = 55f, VerticalFov = 30f;

        public static void Run(string evidenceDir)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AnimationMode.StartAnimationMode();
            try
            {
                var roster = Roster.Load().ToDictionary(t => t.id);
                var night = Night();
                var kit = Kit();
                var crowd = Crowd(roster);
                Render(GameCamera(Vector3.zero, 30f), Path.Combine(evidenceDir, "style-still-30m.jpg"));
                Render(GameCamera(Vector3.zero, 40f), Path.Combine(evidenceDir, "style-still-40m.jpg"));
                UnityEngine.Object.DestroyImmediate(crowd);
                Poses.Clear();

                var bosses = new GameObject("Bosses");
                Place(roster["dragon"], new Vector3(-3.5f, 0, 0), 200, "idle", 0.4f, bosses);
                Place(roster["evilgod"], new Vector3(3.5f, 0, 0), 160, "idle", 0.4f, bosses);
                Place(roster["hero"], new Vector3(0, 0, -5), 0, "idle", 0.3f, bosses);
                Torch(new Vector3(0, 1.5f, -3.5f), new Color(1f, 0.55f, 0.25f), 6f, bosses);
                Render(GameCamera(new Vector3(0, 0, -1), 20f), Path.Combine(evidenceDir, "boss-candidates.jpg"));
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
        }

        static GameObject Night()
        {
            var root = new GameObject("Night");
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(root.transform);
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.66f, 1f);
            moon.intensity = 0.45f;
            moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(32, -35, 0);
            RenderSettings.sun = moon;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.13f, 0.22f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.08f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.04f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.04f, 0.06f, 0.10f);
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.skybox = null;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(16, 1, 16);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.075f, 0.10f, 0.08f));
            mat.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<Renderer>().sharedMaterial = mat;

            var volume = new GameObject("Post").AddComponent<Volume>();
            volume.transform.SetParent(root.transform);
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.65f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.2f);
            color.contrast.Override(12f);
            color.saturation.Override(8f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.28f);
            volume.sharedProfile = profile;
            return root;
        }

        /// <summary>Spawns a vendor prefab with URP materials.</summary>
        static GameObject Prop(string nameEnd, Vector3 at, float yaw, float scale, GameObject parent, float targetHeight = 0)
        {
            var path = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Vendor" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith("/" + nameEnd + ".prefab", StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("StyleStill: no prefab " + nameEnd);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.SetParent(parent.transform);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale *= scale;
            if (targetHeight > 0)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (b.size.y > 0.01f) go.transform.localScale *= targetHeight * scale / b.size.y;
                }
            }
            UrpMaterials.Convert(go);
            return go;
        }

        static void Torch(Vector3 at, Color color, float range, GameObject parent)
        {
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(parent.transform);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = 3.5f;
            l.shadows = LightShadows.None;
        }

        static GameObject Kit()
        {
            var root = new GameObject("Kit");
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            string[] trees =
            {
                "SM_Env_Tree_Round_01", "SM_Env_Tree_Round_02", "SM_Env_Tree_Round_03", "SM_Env_Tree_Large_01",
                "SM_Env_Tree_Pine_Cluster_01", "SM_Env_TreePine_01", "SM_Env_TreePine_02", "SM_Env_Tree_02", "SM_Env_Tree_04",
            };
            string[] edge =
            {
                "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bushes_01", "SM_Env_Fern_01", "SM_Env_Rock_01",
                "SM_Env_Rock_Chunk_02", "SM_Generic_Small_Rocks_01", "SM_Env_TreeLog_01", "SM_Env_Flowers_02",
            };
            // Forest ring and edge band around a 12 m clearing; open toward the camera so the
            // fighting stays in view.
            for (int i = 0; i < 70; i++)
            {
                float a = R(0, Mathf.PI * 2), r = R(14, 26);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r * 0.8f + 4);
                if (p.z < -9) continue;
                Prop(trees[rng.Next(trees.Length)], p, R(0, 360), R(0.85f, 1.25f), root, 8f);
            }
            for (int i = 0; i < 80; i++)
            {
                float a = R(0, Mathf.PI * 2), r = R(10.5f, 14.5f);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r * 0.8f + 2);
                var name = edge[rng.Next(edge.Length)];
                float h = name.Contains("Rock") ? 0.9f : name.Contains("Log") ? 0.7f : name.Contains("Flower") ? 0.4f : 1.3f;
                Prop(name, p, R(0, 360), R(0.8f, 1.3f), root, h);
            }
            // Landmarks: a glowing elven tree, ruin walls, a small graveyard, braziers.
            Prop("SM_Env_Tree_Glowing_01", new Vector3(-9, 0, 8), 30, 1f, root);
            Prop("SM_Env_Tree_Glowing_Canopy_01", new Vector3(-9, 0, 8), 30, 1f, root);
            Torch(new Vector3(-9, 4, 8), new Color(0.4f, 0.8f, 1f), 10f, root);
            Prop("SM_Bld_Wall_Ruin_02", new Vector3(9, 0, 7), -30, 1f, root);
            Prop("SM_Bld_Wall_Ruin_05", new Vector3(12.5f, 0, 4.5f), -60, 1f, root);
            Prop("SM_Bld_Ruin_03", new Vector3(11, 0, 10), -20, 1f, root);
            Prop("SM_Prop_Tomb_01", new Vector3(8, 0, -6), 10, 1f, root);
            Prop("SM_Prop_Tomb_Stone_01", new Vector3(10, 0, -4), -15, 1f, root);
            Prop("SM_Prop_Tomb_02", new Vector3(11.5f, 0, -7), 25, 1f, root);
            Prop("SM_Env_Tree_Dead_01", new Vector3(13, 0, -2), 0, 1f, root);
            Prop("SM_Prop_Candle_02", new Vector3(9, 0, -5.2f), 0, 1f, root);
            Torch(new Vector3(9, 0.8f, -5.2f), new Color(1f, 0.6f, 0.3f), 4f, root);
            Prop("SM_Prop_Brazier_01", new Vector3(-6, 0, -4), 0, 1f, root);
            Torch(new Vector3(-6, 1.6f, -4), new Color(1f, 0.5f, 0.2f), 9f, root);
            Prop("SM_Prop_Brazier_01", new Vector3(5, 0, 6), 0, 1f, root);
            Torch(new Vector3(5, 1.6f, 6), new Color(1f, 0.5f, 0.2f), 9f, root);
            Prop("SM_Prop_Statue_01", new Vector3(-12, 0, -2), 70, 1f, root);
            return root;
        }

        // AnimationMode reverts whatever a sampling pass doesn't sample, so every pose in a still
        // is sampled in one pass, just before rendering.
        static readonly List<(GameObject go, AnimationClip clip, float time)> Poses = new List<(GameObject, AnimationClip, float)>();

        static GameObject Place(Roster.Type t, Vector3 at, float yaw, string role, float phase, GameObject parent)
        {
            var go = Roster.Spawn(t, at, yaw);
            go.transform.SetParent(parent.transform, true);
            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null && t.clips.TryGetValue(role, out var c))
            {
                var clip = Roster.LoadClip(c);
                Poses.Add((animator.gameObject, clip, clip.length * phase));
            }
            return go;
        }

        static void ApplyPoses()
        {
            AnimationMode.BeginSampling();
            foreach (var (go, clip, time) in Poses)
                if (go != null) AnimationMode.SampleAnimationClip(go, clip, time);
            AnimationMode.EndSampling();
        }

        static GameObject Crowd(Dictionary<string, Roster.Type> roster)
        {
            var root = new GameObject("Crowd");
            var rng = new System.Random(3);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Place(roster["hero"], new Vector3(0, 0, 0), 20, "slashR", 0.4f, root);
            Torch(new Vector3(0.6f, 1.2f, 1.2f), new Color(1f, 0.75f, 0.45f), 5f, root);
            string[] fodder = { "skeleton", "goblin", "slime", "bat", "mushroom", "demon" };
            string[] roles = { "walk", "walk", "attack", "walk", "hit" };
            for (int i = 0; i < 42; i++)
            {
                float a = R(-0.4f, Mathf.PI + 0.4f), r = R(2.2f, 8.5f);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r + 0.5f);
                var id = fodder[i % fodder.Length];
                float yaw = Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg + R(-15, 15);
                Place(roster[id], p, yaw, roles[rng.Next(roles.Length)], R(0, 1), root);
            }
            Place(roster["bigork"], new Vector3(-4.5f, 0, 5), 160, "attack", 0.45f, root);
            Place(roster["troll"], new Vector3(5, 0, 4.5f), 210, "walk", 0.3f, root);
            return root;
        }

        /// <summary>The game camera: 55° pitch, 30° vertical FOV, height from the view width.</summary>
        static Camera GameCamera(Vector3 focus, float viewWidth)
        {
            var go = new GameObject("GameCamera");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = VerticalFov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            float hfov = 2f * Mathf.Atan(Mathf.Tan(VerticalFov * 0.5f * Mathf.Deg2Rad) * 16f / 9f);
            float distance = viewWidth / (2f * Mathf.Tan(hfov * 0.5f));
            var dir = Quaternion.Euler(Pitch, 0, 0) * Vector3.forward;
            go.transform.position = focus - dir * distance;
            go.transform.rotation = Quaternion.Euler(Pitch, 0, 0);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            return cam;
        }

        static void Render(Camera cam, string path)
        {
            ApplyPoses();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTag.Save(tex, path, "TD2 style still");
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            Debug.Log("StyleStill: wrote " + path);
        }
    }
}
