using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Demo3D.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Demo3D.EditorTools.Assets
{
    /// <summary>
    /// The clip annotator (choreography.md, Clip annotations and retiming). It samples every
    /// roster clip on its rig at 60 Hz and proposes:
    /// - attacks and combos: the impact, where the fastest end bone (a hand, weapon, head, jaw
    ///   or tail) peaks in speed relative to the body, and the end, where motion settles after it;
    /// - hits and deaths: the impact, the reaction's first speed peak, and the end, where the body
    ///   comes to rest (a death's end is when the 1-beat hold can start);
    /// - locomotion: the stride, the distance one cycle covers (root motion where the clip has
    ///   it, else the feet's fore-aft travel);
    /// - combos: the root-motion distance, from each clip's root-motion twin.
    /// It writes Assets/Demo3D/Data/clip-annotations.json and contact sheets, one row per clip,
    /// with the impact frame framed in red, to demo3d/evidence/td2/. For a `--roster` set
    /// (`-roster NAME`) it annotates only the slots the set's file lists and writes them to the
    /// file's <c>rosters.NAME</c>, which the player puts in place of the default's (RosterSet).
    /// </summary>
    public static class ClipAnnotator
    {
        public const string OutPath = "Assets/Demo3D/Data/clip-annotations.json";
        const float Rate = 60f;
        const int SheetColumns = 8, Cell = 176;

        public sealed class Annotation
        {
            public string type;
            public string role;
            public string guid;
            public string clip;
            public string use;
            public float length;
            public bool loop;
            public float? impact;
            public float? end;
            public float? strideM;
            public float? rootMotionM;
            public string impactBone;
            public float? vendorHit;
            public string method;
        }

        sealed class OutFile
        {
            public int version = 1;
            public string note;
            public Dictionary<string, TypeInfo> types = new Dictionary<string, TypeInfo>();
            public List<Annotation> annotations = new List<Annotation>();
        }

        public sealed class TypeInfo
        {
            public float heightM;
            public int bones;
            public int triangles;
            public string rig;
        }

        static readonly string[] EndBoneWords = { "hand", "wrist", "weapon", "sword", "head", "jaw", "mouth", "tail", "claw", "finger" };
        static readonly string[] FootWords = { "foot", "toe", "ankle" };

        public static void Run(string evidenceDir, string set = "default")
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AnimationMode.StartAnimationMode();
            try { RunInMode(evidenceDir, set); }
            finally { AnimationMode.StopAnimationMode(); }
        }

        static void RunInMode(string evidenceDir, string set)
        {
            var own = set == "default" ? null : new HashSet<string>(Roster.SetIds(set));
            var outFile = new OutFile
            {
                note = "Proposed by the clip annotator (TD2) from each clip sampled on its rig at 60 Hz; " +
                       "times in seconds from the clip's start. Our measurements, keyed by vendor GUID and clip name.",
            };
            var stage = new SheetStage();
            foreach (var t in Roster.Load(set))
            {
                if (own != null && !own.Contains(t.id)) continue;
                var go = Roster.Spawn(t, Vector3.zero, 0);
                var animator = go.GetComponentInChildren<Animator>();
                if (animator == null) throw new Exception($"ClipAnnotator: {t.id} has no Animator");
                outFile.types[t.id] = Describe(go, t);
                var rows = new List<(string role, AnimationClip clip, Annotation a)>();
                foreach (var kv in t.clips)
                {
                    var clip = Roster.LoadClip(kv.Value);
                    var a = Annotate(go, animator, t, kv.Key, clip, kv.Value.impact);
                    a.guid = kv.Value.guid;
                    a.clip = clip.name;
                    a.vendorHit = Roster.VendorHitTime(kv.Value) is float vh ? Round(vh) : (float?)null;
                    if (t.rootMotionClips.TryGetValue(kv.Key, out var twinRef))
                    {
                        var twin = Roster.LoadClip(twinRef);
                        float travel = Horizontal(twin.averageSpeed) * twin.length * go.transform.lossyScale.y;
                        if (IsLocomotion(kv.Key)) a.strideM = Round(travel);
                        else a.rootMotionM = Round(travel);
                        a.method += $"; travel from root-motion twin {twin.name}";
                    }
                    outFile.annotations.Add(a);
                    rows.Add((kv.Key, clip, a));
                    Debug.Log($"ClipAnnotator: {t.id}.{kv.Key} '{clip.name}' len {a.length:F3} impact {a.impact} end {a.end} stride {a.strideM} root {a.rootMotionM} ({a.method})");
                }
                stage.Sheet(go, animator, t.id, rows, Path.Combine(evidenceDir, $"contact-{t.id}.jpg"));
                UnityEngine.Object.DestroyImmediate(go);
            }
            stage.Dispose();
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Culture = CultureInfo.InvariantCulture };
            var existing = File.Exists(OutPath) ? JObject.Parse(File.ReadAllText(OutPath)) : null;
            string text;
            if (own == null)
            {
                text = JsonConvert.SerializeObject(outFile, Formatting.Indented, settings);
                if (existing?["rosters"] is JToken rosters) // the sets' own annotations survive a default run
                {
                    var root = JObject.Parse(text);
                    root["rosters"] = rosters;
                    text = root.ToString(Formatting.Indented);
                }
            }
            else
            {
                var root = existing ?? throw new Exception($"ClipAnnotator: annotate the default roster before {set}");
                if (!(root["rosters"] is JObject rosters)) root["rosters"] = rosters = new JObject();
                rosters[set] = JObject.FromObject(new { outFile.types, outFile.annotations }, JsonSerializer.Create(settings));
                text = root.ToString(Formatting.Indented);
            }
            File.WriteAllText(OutPath, text + "\n");
            AssetDatabase.ImportAsset(OutPath);
            Debug.Log($"ClipAnnotator: wrote {outFile.annotations.Count} {set} annotations to {OutPath}");
        }

        static bool IsLocomotion(string role) => role == "walk" || role == "run";

        static bool IsAttack(string role) =>
            role.StartsWith("attack") || role.StartsWith("slash") || role == "thrust" || role == "spin" || role == "slam" || role == "parry";

        static TypeInfo Describe(GameObject go, Roster.Type t)
        {
            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            int tris = 0, bones = 0;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                if (r.sharedMesh != null) tris += r.sharedMesh.triangles.Length / 3;
                bones = Math.Max(bones, r.bones.Length);
            }
            return new TypeInfo { heightM = Round(bounds.max.y - go.transform.position.y), bones = bones, triangles = tris, rig = t?.rig };
        }

        /// <summary>Samples bone positions in the character's local space at 60 Hz.</summary>
        sealed class Track
        {
            public string Name;
            public List<Vector3> Pos = new List<Vector3>();
            public List<float> Speed = new List<float>();
        }

        static Annotation Annotate(GameObject go, Animator animator, Roster.Type t, string role, AnimationClip clip, float? givenImpact = null)
        {
            var a = new Annotation
            {
                type = t.id, role = role, use = $"{(t.kind == "boss" ? "boss" : t.id)} {role}", length = Round(clip.length),
                loop = clip.isLooping || IsLocomotion(role) || role == "idle",
            };
            var endBones = new List<Transform>();
            var feet = new List<Transform>();
            if (animator.isHuman)
            {
                endBones.AddRange(new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand, HumanBodyBones.Head }
                    .Select(animator.GetBoneTransform).Where(x => x != null));
                feet.AddRange(new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                    .Select(animator.GetBoneTransform).Where(x => x != null));
            }
            else
            {
                foreach (var tr in go.GetComponentsInChildren<Transform>(true))
                {
                    var n = tr.name.ToLowerInvariant();
                    if (tr.GetComponent<Renderer>() != null) continue;
                    if (EndBoneWords.Any(n.Contains)) endBones.Add(tr);
                    else if (FootWords.Any(n.Contains)) feet.Add(tr);
                }
            }
            if (endBones.Count == 0)
            {
                // Rigs with unnamed or minimal bones (the slime, the mushroom): every skinned bone.
                var body = BodyBone(go, animator);
                endBones.AddRange(go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones).Where(b => b != null && b != body).Distinct());
            }
            var head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head)
                : endBones.FirstOrDefault(b => b.name.ToLowerInvariant().Contains("head"));

            var impactHint = t.clips.TryGetValue(role, out var hinted) && hinted.impactBone != null
                ? hinted.impactBone.Split(',').Select(x => x.Trim()).ToList() : null;
            if (impactHint != null)
                endBones.AddRange(go.GetComponentsInChildren<Transform>(true)
                    .Where(x => impactHint.Contains(x.name) && !endBones.Contains(x)));
            var tracks = Sample(go, animator, clip, endBones.Concat(feet).Distinct().ToList(), out var rootTravel);
            int n0 = tracks.Count > 0 ? tracks[0].Speed.Count : 0;

            if (IsLocomotion(role))
            {
                if (t.strideM is float given)
                {
                    a.strideM = given;
                    a.method = "stride from the roster (a flyer's feet don't step)";
                }
                else if (rootTravel > 0.05f)
                {
                    a.strideM = Round(rootTravel);
                    a.method = "stride from root travel over one cycle";
                }
                else
                {
                    var footTracks = tracks.Where(k => feet.Any(f => f.name == k.Name)).ToList();
                    // Each foot's fore-aft range is one step; a cycle has one step per foot.
                    var ranges = footTracks.Select(k => k.Pos.Max(p => p.z) - k.Pos.Min(p => p.z)).OrderByDescending(r => r).ToList();
                    if (ranges.Count >= 2)
                    {
                        a.strideM = Round(ranges[0] + ranges[1]);
                        a.method = $"stride from the feet's fore-aft range ({footTracks.Count} foot bones)";
                    }
                    else
                    {
                        float h = Describe(go, t).heightM;
                        a.strideM = Round(Mathf.Max(0.4f, h * 0.9f));
                        a.method = "stride nominal (no feet and no root travel): 0.9 x height";
                    }
                }
                return a;
            }
            if (role == "idle") { a.method = "loop"; return a; }

            if (IsAttack(role))
            {
                Track best = null;
                int bestFrame = 0;
                float bestSpeed = 0;
                int lo = (int)(0.08f * n0), hi = Math.Max(lo + 1, (int)(0.92f * n0));
                if (givenImpact is float given)
                {
                    // The roster's impact (read off the contact sheet): the fastest end bone there.
                    lo = Mathf.Clamp(Mathf.RoundToInt(given * Rate), 0, Math.Max(0, n0 - 1));
                    hi = lo + 1;
                }
                // A roster hint names the striking bone where the fastest is another (a quadruped's tail).
                var candidates = impactHint == null ? tracks.Where(k => endBones.Any(b => b.name == k.Name))
                    : tracks.Where(k => impactHint.Contains(k.Name));
                foreach (var k in candidates)
                    for (int i = lo; i < hi && i < k.Speed.Count; i++)
                        if (k.Speed[i] > bestSpeed) { bestSpeed = k.Speed[i]; best = k; bestFrame = i; }
                if (best != null)
                {
                    a.impact = Round(bestFrame / Rate);
                    a.end = Round(Settle(best.Speed, bestFrame, bestSpeed, 0.2f) / Rate);
                    a.impactBone = best.Name;
                    a.method = givenImpact != null
                        ? $"impact from the roster (its contact sheet; the peak speed came in the wind-up), end where {best.Name} settles"
                        : $"impact at the peak speed of {best.Name} ({bestSpeed:F2} m/s)";
                }
                return a;
            }

            // Hits and deaths: the reaction's first peak, then rest.
            var reaction = tracks.FirstOrDefault(k => head != null && k.Name == head.name)
                ?? tracks.OrderByDescending(k => k.Speed.Count == 0 ? 0 : k.Speed.Max()).FirstOrDefault();
            if (reaction != null)
            {
                float peak = reaction.Speed.Count == 0 ? 0 : reaction.Speed.Max();
                // The blow lands where the reaction starts: the first frame moving at a fifth of
                // the reaction's peak speed. (Its first peak would start the clip early, so a
                // monster would flinch or fall before it was struck.)
                int onset = 0;
                for (int i = 1; i < reaction.Speed.Count; i++)
                    if (reaction.Speed[i] >= 0.2f * peak) { onset = i - 1; break; }
                a.impact = Round(onset / Rate);
                a.end = Round(Settle(reaction.Speed, onset, peak, role == "death" ? 0.08f : 0.2f) / Rate);
                a.impactBone = reaction.Name;
                a.method = $"{role}: reaction onset of {reaction.Name} (a fifth of its peak speed), end where it settles";
            }
            return a;
        }

        /// <summary>The frame after which speed stays below `fraction` of the peak.</summary>
        static int Settle(List<float> speed, int from, float peak, float fraction)
        {
            int last = from;
            for (int i = from; i < speed.Count; i++)
                if (speed[i] > fraction * peak) last = i;
            return Math.Min(speed.Count - 1, last + 1);
        }

        /// <summary>The body: the hips on a humanoid, the skinned mesh's root bone otherwise.</summary>
        static Transform BodyBone(GameObject go, Animator animator)
        {
            if (animator.isHuman) return animator.GetBoneTransform(HumanBodyBones.Hips);
            var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.rootBone != null);
            return smr != null ? smr.rootBone : animator.transform;
        }

        /// <summary>Poses the character at a clip time with the editor's animation sampling.</summary>
        static void Pose(GameObject go, AnimationClip clip, float time)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(go, clip, time);
            AnimationMode.EndSampling();
        }

        static List<Track> Sample(GameObject go, Animator animator, AnimationClip clip, List<Transform> bones, out float rootTravel)
        {
            var tracks = bones.Select(b => new Track { Name = b.name }).ToList();
            int frames = Math.Max(2, Mathf.CeilToInt(clip.length * Rate) + 1);
            var root = go.transform;
            Vector3 rootStart = Vector3.zero, rootEnd = Vector3.zero;
            var hips = BodyBone(go, animator);
            for (int f = 0; f < frames; f++)
            {
                Pose(animator.gameObject, clip, Math.Min(clip.length, f / Rate));
                // World-space offsets (the character stands at the origin facing +z, at its demo
                // scale), relative to the body so a clip's own travel doesn't count as motion.
                var bodyNow = hips.position - root.position;
                var flat = new Vector3(bodyNow.x, 0, bodyNow.z);
                for (int i = 0; i < bones.Count; i++)
                    tracks[i].Pos.Add(bones[i].position - root.position - flat);
                var body = bodyNow;
                if (f == 0) rootStart = body;
                rootEnd = body;
            }
            foreach (var k in tracks)
            {
                k.Speed.Add(0);
                for (int i = 1; i < k.Pos.Count; i++) k.Speed.Add((k.Pos[i] - k.Pos[i - 1]).magnitude * Rate);
            }
            var d = rootEnd - rootStart;
            rootTravel = new Vector2(d.x, d.z).magnitude;
            return tracks;
        }

        static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        static float Round(float v) => (float)Math.Round(v, 3);

        /// <summary>Renders contact sheets: one row per clip, 8 frames, the impact in red.</summary>
        sealed class SheetStage : IDisposable
        {
            readonly Camera _camera;
            readonly Light _light;
            readonly RenderTexture _rt;

            public SheetStage()
            {
                _rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var cam = new GameObject("SheetCamera");
                _camera = cam.AddComponent<Camera>();
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
                _camera.fieldOfView = 30;
                _camera.targetTexture = _rt;
                var l = new GameObject("SheetLight");
                _light = l.AddComponent<Light>();
                _light.type = LightType.Directional;
                _light.intensity = 2.2f;
                l.transform.rotation = Quaternion.Euler(40, -30, 0);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.7f, 0.7f, 0.75f);
            }

            public void Sheet(GameObject go, Animator animator, string id, List<(string role, AnimationClip clip, Annotation a)> rows, string path)
            {
                var info = Describe(go, null);
                float h = Mathf.Max(0.5f, info.heightM);
                var sheet = new Texture2D(SheetColumns * Cell, rows.Count * Cell, TextureFormat.RGB24, false);
                for (int r = 0; r < rows.Count; r++)
                {
                    var (role, clip, a) = rows[r];
                    var times = Enumerable.Range(0, SheetColumns).Select(c => clip.length * c / (SheetColumns - 1)).ToArray();
                    int impactCol = -1;
                    if (a.impact.HasValue)
                    {
                        impactCol = Array.FindIndex(times, x => x >= a.impact.Value);
                        if (impactCol < 0) impactCol = SheetColumns - 1;
                        times[impactCol] = a.impact.Value;
                    }
                    for (int c = 0; c < SheetColumns; c++)
                    {
                        Pose(animator.gameObject, clip, times[c]);
                        float dist = h * 1.8f;
                        var b = BodyBone(go, animator).position;
                        var target = new Vector3(b.x, go.transform.position.y + h * 0.5f, b.z);
                        var offset = Quaternion.Euler(0, 35, 0) * new Vector3(0, Mathf.Sin(0.31f), Mathf.Cos(0.31f));
                        _camera.transform.position = target + offset * dist;
                        _camera.transform.LookAt(target);
                        _camera.Render();
                        RenderTexture.active = _rt;
                        sheet.ReadPixels(new Rect(0, 0, Cell, Cell), c * Cell, (rows.Count - 1 - r) * Cell);
                        RenderTexture.active = null;
                        if (c == impactCol) Frame(sheet, c * Cell, (rows.Count - 1 - r) * Cell, Color.red);
                    }
                }
                sheet.Apply();
                RenderTag.Save(sheet, path, "clip annotator contact sheet: " + id);
                UnityEngine.Object.DestroyImmediate(sheet);
            }

            static void Frame(Texture2D tex, int x0, int y0, Color c)
            {
                for (int i = 0; i < Cell; i++)
                    for (int k = 0; k < 3; k++)
                    {
                        tex.SetPixel(x0 + i, y0 + k, c);
                        tex.SetPixel(x0 + i, y0 + Cell - 1 - k, c);
                        tex.SetPixel(x0 + k, y0 + i, c);
                        tex.SetPixel(x0 + Cell - 1 - k, y0 + i, c);
                    }
            }

            public void Dispose()
            {
                _camera.targetTexture = null;
                _rt.Release();
                UnityEngine.Object.DestroyImmediate(_camera.gameObject);
                UnityEngine.Object.DestroyImmediate(_light.gameObject);
            }
        }
    }
}
