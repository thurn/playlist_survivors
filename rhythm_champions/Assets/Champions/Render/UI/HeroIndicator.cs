using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render
{
    /// <summary>What the hero's indicator shows on a frame (<see cref="HeroIndicator"/>).</summary>
    public struct IndicatorLook
    {
        public bool Shown;
        /// <summary>The next two beats' ticks: each one's place from the centre (0, on its beat) to the strip's ends (1), and its alpha (0: none).</summary>
        public float Tick1, Tick1Alpha, Tick2, Tick2Alpha;
        /// <summary>The dash's cooldown done, 0 to 1, and whether it is ready (the pip solid).</summary>
        public float Cooldown;
        public bool Ready;
        /// <summary>An on-beat dash's flash and a dropped press's blink, each 1 when it begins and 0 once over.</summary>
        public float Flash, Blink;
        /// <summary>Health, 0 to 1, and a hurt's flash on the bar, 1 when it begins.</summary>
        public float Health, Hurt;
    }

    /// <summary>
    /// The hero's indicator (design.md, The Dash > The indicator, Decision 7), drawn in the world: a quad over the hero's
    /// head, <see cref="AboveM"/> above its feet, facing the camera, which Indicator.shader fills with the strip
    /// (<see cref="WidthM"/> wide), the beats' ticks, the dash's pip and the health bar, all in the
    /// hero's white (hero.body), the bar's hurt in hero.hurt. The stills' type-ID pass sees it as one of the hero's effects.
    /// </summary>
    public sealed class HeroIndicator : IDisposable
    {
        public const string MaterialPath = "UI/Indicator";
        public const float WidthM = 1.2f, AboveM = 2.5f;
        const float QuadHalfX = 0.8f, QuadHalfY = 0.3f;

        static readonly int Body = Shader.PropertyToID("_Body"), HurtColour = Shader.PropertyToID("_Hurt"),
            Ticks = Shader.PropertyToID("_Ticks"), Pip = Shader.PropertyToID("_Pip"), State = Shader.PropertyToID("_State");

        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MeshRenderer _renderer;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        public HeroIndicator(Palette palette, int layer = 0)
        {
            var shared = Resources.Load<Material>(MaterialPath)
                         ?? throw new InvalidOperationException($"HeroIndicator: no material at Resources/{MaterialPath}");
            _material = new Material(shared) { name = "Hero indicator" };
            _material.SetColor(Body, palette.HeroBody.linear);
            _material.SetColor(HurtColour, palette.HeroHurt.linear);
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _mesh = new Mesh { name = "Hero indicator" };
            _mesh.vertices = new[]
            {
                new Vector3(-QuadHalfX, -QuadHalfY, 0), new Vector3(QuadHalfX, -QuadHalfY, 0),
                new Vector3(QuadHalfX, QuadHalfY, 0), new Vector3(-QuadHalfX, QuadHalfY, 0),
            };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _mesh.RecalculateBounds();
            _go = new GameObject("Hero indicator") { layer = layer };
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _go.SetActive(false);
        }

        /// <summary>The frame: over the hero's <paramref name="feet"/> (world), facing <paramref name="camera"/>, showing <paramref name="look"/>.</summary>
        public void Present(Vector3 feet, Camera camera, in IndicatorLook look)
        {
            _go.SetActive(look.Shown);
            if (!look.Shown) return;
            _go.transform.SetPositionAndRotation(feet + Vector3.up * AboveM, camera.transform.rotation);
            _block.SetVector(Ticks, new Vector4(look.Tick1, look.Tick1Alpha, look.Tick2, look.Tick2Alpha));
            _block.SetVector(Pip, new Vector4(Mathf.Clamp01(look.Cooldown), look.Ready ? 1 : 0, Mathf.Clamp01(look.Flash), Mathf.Clamp01(look.Blink)));
            _block.SetVector(State, new Vector4(Mathf.Clamp01(look.Health), Mathf.Clamp01(look.Hurt), 0, 1));
            _renderer.SetPropertyBlock(_block);
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { _go, _mesh, _material })
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }
}
