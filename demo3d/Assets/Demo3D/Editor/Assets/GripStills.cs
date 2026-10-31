using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Demo3D.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// Close-up stills of the armed roster types' grips (crowd.md, Weapons): each type spawned as the
    /// crowd baker spawns it (Roster.Spawn, so with the roster's weapon grips), posed in its idle, walk
    /// and attacks, one sheet per type: a column per pose, the front above and the right side below.
    /// </summary>
    public static class GripStills
    {
        const int Cell = 300;
        const string AnnotationsPath = "Assets/Demo3D/Data/clip-annotations.json";

        /// <summary>The poses: a clip role and where in it (a fraction of its length, or its impact).</summary>
        static readonly (string Label, string Role, float At, bool Impact)[] Poses =
        {
            ("idle", "idle", 0.3f, false),
            ("walk", "walk", 0.25f, false),
            ("walk", "walk", 0.75f, false),
            ("attack wind-up", "attack", 0.6f, true),
            ("attack impact", "attack", 1f, true),
            ("attack2 impact", "attack2", 1f, true),
        };

        /// <summary>The camera's turns round the type (degrees from its front): the front, a little toward
        /// its right hand, then its right side.</summary>
        static readonly float[] Views = { 20, 90 };

        /// <summary>Writes grips-TYPE.jpg into `dir` for each id (or every drawn type with roster weapons).</summary>
        public static void Run(string dir, string set, IReadOnlyCollection<string> ids)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(dir);
            var annotations = JObject.Parse(File.ReadAllText(AnnotationsPath));
            var types = Roster.Load(set).Where(t => ids.Count > 0 ? ids.Contains(t.id) : t.weapons.Count > 0 && Crowd.CrowdBaker.Drawn(t)).ToList();
            if (types.Count == 0) throw new Exception("GripStills: no types to render");
            var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var camera = new GameObject("GripCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            camera.fieldOfView = 30;
            camera.targetTexture = rt;
            var light = new GameObject("GripLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.2f;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.7f, 0.7f, 0.75f);
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var t in types)
                {
                    var go = Roster.Spawn(t, Vector3.zero, 0);
                    var animator = go.GetComponentInChildren<Animator>();
                    // Every render skins afresh: by default a skinned mesh keeps its first skinning of an
                    // editor frame, so it lagged the rigid weapons posed after it.
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                    float h = Mathf.Max(0.5f, t.heightM);
                    var sheet = new Texture2D(Poses.Length * Cell, Views.Length * Cell, TextureFormat.RGB24, false);
                    for (int c = 0; c < Poses.Length; c++)
                    {
                        var (_, role, at, impact) = Poses[c];
                        if (!t.clips.TryGetValue(role, out var clipRef)) continue;
                        var clip = Roster.LoadClip(clipRef);
                        float time = at * clip.length;
                        if (impact && Impact(annotations, set, t.id, role) is float hit) time = at * hit;
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
                        AnimationMode.EndSampling();
                        for (int v = 0; v < Views.Length; v++)
                        {
                            var target = new Vector3(0, h * 0.55f, 0);
                            var offset = Quaternion.Euler(0, Views[v], 0) * new Vector3(0, Mathf.Sin(0.2f), Mathf.Cos(0.2f));
                            camera.transform.position = target + offset * h * 2.2f;
                            camera.transform.LookAt(target);
                            if (c == 0 && v == 0) camera.Render(); // the first render of a new material comes out white
                            camera.Render();
                            RenderTexture.active = rt;
                            sheet.ReadPixels(new Rect(0, 0, Cell, Cell), c * Cell, (Views.Length - 1 - v) * Cell);
                            RenderTexture.active = null;
                        }
                    }
                    sheet.Apply();
                    RenderTag.Save(sheet, Path.Combine(dir, $"grips-{t.id}.jpg"), "grip stills: " + t.id);
                    UnityEngine.Object.DestroyImmediate(sheet);
                    UnityEngine.Object.DestroyImmediate(go);
                    Debug.Log($"GripStills: {t.id}: {string.Join(", ", Poses.Select(p => p.Label))}");
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                camera.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
                UnityEngine.Object.DestroyImmediate(light.gameObject);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        /// <summary>A clip's annotated impact (s): the set's own entry if it has one, else the default's.</summary>
        static float? Impact(JObject annotations, string set, string type, string role)
        {
            IEnumerable<JToken> Entries(JToken list) => list?.Where(a => (string)a["type"] == type && (string)a["role"] == role) ?? Enumerable.Empty<JToken>();
            var hit = set != "default" ? Entries(annotations["rosters"]?[set]?["annotations"]).FirstOrDefault() : null;
            hit ??= Entries(annotations["annotations"]).FirstOrDefault();
            return (float?)hit?["impact"];
        }
    }
}
