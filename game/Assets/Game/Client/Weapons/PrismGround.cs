using System;
using Game.Client.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Weapons
{
    /// <summary>
    /// The weapons' prism light on the ground (Resources/Weapons/PrismGround: shader Game/PrismGround):
    /// a flat ring or disc in an ability's colour with a prism sheen, white-hot where it is
    /// brightest, drawn on <see cref="CrystalWaves"/>' ring mesh, additive and HDR. Each draw takes
    /// its own centre, radius, depth and weights; a build without the material, or a headless
    /// player, draws nothing.
    /// </summary>
    public sealed class PrismGround : IDisposable
    {
        public const string ResourcePath = "Weapons/PrismGround";
        /// <summary>Just above the crystal waves, so the two never fight.</summary>
        public const float Height = 0.065f;

        public struct Look
        {
            public Color Tint;
            /// <summary>The thin white-hot front at the edge, the glow through the ring, and how far that glow leans to the centre (0 the edge, 1 the centre).</summary>
            public float Edge, Fill, Core;
            public float Intensity;
        }

        readonly Camera _camera;
        readonly Material _material;
        readonly Mesh _mesh;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        static readonly int TintId = Shader.PropertyToID("_Tint"), RadiusId = Shader.PropertyToID("_Radius"),
            WidthId = Shader.PropertyToID("_Width"), IntensityId = Shader.PropertyToID("_Intensity"), AgeId = Shader.PropertyToID("_Age"),
            EdgeId = Shader.PropertyToID("_Edge"), FillId = Shader.PropertyToID("_Fill"), CoreId = Shader.PropertyToID("_Core");

        public PrismGround(Camera camera)
        {
            _camera = camera;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return; // a headless player draws nothing
            var material = Resources.Load<Material>(ResourcePath);
            if (material == null)
            {
                Debug.LogWarning($"PrismGround: no material at Resources/{ResourcePath}");
                return;
            }
            _material = new Material(material);
            _mesh = CrystalWaves.Annulus();
        }

        /// <summary>A ring <paramref name="width"/> deep inside <paramref name="radius"/> around <paramref name="centre"/> (ground x, y); a disc when the width is the radius.</summary>
        public void Draw(Vector2 centre, float radius, float width, in Look look, float age)
        {
            if (_material == null || radius <= 0 || look.Intensity <= 0) return;
            _block.SetColor(TintId, look.Tint);
            _block.SetFloat(RadiusId, radius);
            _block.SetFloat(WidthId, Mathf.Min(width, radius));
            _block.SetFloat(IntensityId, look.Intensity);
            _block.SetFloat(AgeId, age);
            _block.SetFloat(EdgeId, look.Edge);
            _block.SetFloat(FillId, look.Fill);
            _block.SetFloat(CoreId, look.Core);
            var at = new Vector3(centre.x, Height, centre.y);
            var rp = new RenderParams(_material)
            {
                camera = _camera,
                matProps = _block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(at, Vector3.one * (2 * radius + 2)),
            };
            Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.Translate(at));
        }

        public void Dispose()
        {
            if (_material == null) return;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_material);
                UnityEngine.Object.Destroy(_mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_material);
                UnityEngine.Object.DestroyImmediate(_mesh);
            }
        }
    }
}
