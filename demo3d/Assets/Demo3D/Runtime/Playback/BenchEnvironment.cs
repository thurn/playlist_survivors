using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The bench scene (benchmarks.md, Stress scenes): flat ground under the default sun, with
    /// ambient light and the post-processing stack. Stress scenes and the song run here until
    /// TD9's arena exists. Built in code, so nothing vendor-derived is involved.
    /// </summary>
    public static class BenchEnvironment
    {
        public static Light Build()
        {
            var root = new GameObject("BenchEnvironment");
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.5f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.32f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.14f, 0.12f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(40, 1, 40); // 400 m: well past any view
            Object.Destroy(ground.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.24f, 0.27f, 0.22f));
            mat.SetFloat("_Smoothness", 0.15f);
            ground.GetComponent<Renderer>().sharedMaterial = mat;

            var volume = new GameObject("Post").AddComponent<Volume>();
            volume.transform.SetParent(root.transform);
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.6f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            profile.Add<Vignette>(true).intensity.Override(0.2f);
            volume.sharedProfile = profile;
            return sun;
        }
    }

    /// <summary>
    /// The game camera (camera-and-sound.md): 55° pitch, 30° vertical field of view, height from
    /// the view width at 16:9. Its framing comes from the timeline's framing track.
    /// </summary>
    public sealed class GameCameraRig
    {
        public const float Pitch = 55f, VerticalFov = 30f;
        public readonly Camera Camera;

        public GameCameraRig()
        {
            var go = new GameObject("GameCamera");
            Camera = go.AddComponent<Camera>();
            Camera.fieldOfView = VerticalFov;
            Camera.nearClipPlane = 0.5f;
            Camera.farClipPlane = 400f;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None; // MSAA 4x lives in the pipeline asset
            go.AddComponent<AudioListener>();
        }

        public static float Distance(float viewWidth)
        {
            float hfov = 2f * Mathf.Atan(Mathf.Tan(VerticalFov * 0.5f * Mathf.Deg2Rad) * 16f / 9f);
            return viewWidth / (2f * Mathf.Tan(hfov * 0.5f));
        }

        public void Frame(Vector2 focus, float viewWidth)
        {
            var rot = Quaternion.Euler(Pitch, 0, 0);
            var target = new Vector3(focus.x, 0, focus.y);
            Camera.transform.SetPositionAndRotation(target - rot * Vector3.forward * Distance(viewWidth), rot);
        }

        /// <summary>True if a ground point (with a height) projects inside the viewport.</summary>
        public bool OnScreen(Vector3 p, float height)
        {
            var v = Camera.WorldToViewportPoint(p + Vector3.up * height * 0.5f);
            return v.z > 0 && v.x >= -0.02f && v.x <= 1.02f && v.y >= -0.02f && v.y <= 1.02f;
        }
    }
}
