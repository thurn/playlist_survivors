using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools.Crowd
{
    /// <summary>
    /// Close-up stills of the armed roster types' grips (unity-game.md, The crowd, Weapons; demo3d's
    /// GripStills, copied and owned): each type spawned as the crowd bake spawns it (Roster.Spawn, so
    /// with the roster's weapon grips), posed in its idle, walk, attack and hit, one sheet per type: a
    /// column per pose, the front above and the right side below. The body draws grey and every rigid
    /// piece (weapons, shields, helmets) orange, so a weapon's line through the fist reads.
    /// </summary>
    public static class GripStills
    {
        const int Cell = 300;

        /// <summary>The poses: a clip role and where in it (a fraction of its length, or of its impact).</summary>
        static readonly (string Label, string Role, float At, bool Impact)[] Poses =
        {
            ("idle", "idle", 0.3f, false),
            ("walk", "walk", 0.25f, false),
            ("walk", "walk", 0.75f, false),
            ("attack wind-up", "attack", 0.6f, true),
            ("attack impact", "attack", 1f, true),
            ("hit", "hit", 0.5f, false),
        };

        /// <summary>The camera's turns round the type (degrees from its front): a little toward its right
        /// hand, then its right side.</summary>
        static readonly float[] Views = { 20, 90 };

        /// <summary>Writes grips-TYPE.jpg into `dir` for each id (or every type with a weapon).</summary>
        public static void Run(string dir, IReadOnlyCollection<string> ids)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(dir);
            var types = Roster.Load().Where(t => ids.Count > 0 ? ids.Contains(t.id) : t.weapons.Count > 0).ToList();
            if (types.Count == 0) throw new Exception("GripStills: no types to render");
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? throw new Exception("GripStills: no URP Lit shader");
            var body = new Material(lit) { color = new Color(0.55f, 0.56f, 0.6f) };
            var rigid = new Material(lit) { color = new Color(1f, 0.45f, 0.1f) };
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
                    var go = Roster.Spawn(t);
                    var animator = go.GetComponentInChildren<Animator>();
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = Enumerable.Repeat(r is SkinnedMeshRenderer ? body : rigid, r.sharedMaterials.Length).ToArray();
                    // Every render skins afresh: by default a skinned mesh keeps its first skinning of an
                    // editor frame, so it lagged the rigid weapons posed after it.
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                    // A generic rig's clips move its root bone, not the animator: the camera follows it.
                    var root = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.rootBone).FirstOrDefault(b => b != null) ?? go.transform;
                    float h = Mathf.Max(0.5f, t.heightM);
                    var sheet = new Texture2D(Poses.Length * Cell, Views.Length * Cell, TextureFormat.RGB24, false);
                    for (int c = 0; c < Poses.Length; c++)
                    {
                        var (_, role, at, impact) = Poses[c];
                        if (!t.clips.TryGetValue(role, out var clipRef)) continue;
                        var clip = Roster.LoadClip(clipRef);
                        float time = impact && clipRef.impact is float hit ? at * hit : at * clip.length;
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(animator.gameObject, clip, time);
                        AnimationMode.EndSampling();
                        for (int v = 0; v < Views.Length; v++)
                        {
                            var target = new Vector3(root.position.x, h * 0.55f, root.position.z);
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
                UnityEngine.Object.DestroyImmediate(body);
                UnityEngine.Object.DestroyImmediate(rigid);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
