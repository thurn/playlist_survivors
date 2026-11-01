using System;
using Game.Client.Loop;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Hud
{
    /// <summary>
    /// The HUD's marks in world space (hud.rs' overhead display; Resources/Hud/HudMark: shader
    /// Game/HudMark), drawn each frame:
    /// - over the hero's head, facing the camera and over everything, the beat gem: a faceted crystal
    ///   that swells and flashes white on each beat, while a ring closes on it toward the next beat,
    ///   lands on it on the beat's own frame and bursts outward; before a downbeat the ring is warm
    ///   gold and heavier, and the downbeat's burst is gold. Without a reliable beat the gem is a pale
    ///   blue disc that breathes, and no ring closes;
    /// - on the ground under the hero, the life ring: an arc as full as the hero's health, flashing
    ///   white on a bite and pulsing pale on each beat under 30%.
    /// A headless player draws nothing.
    /// </summary>
    public sealed class BeatGem : IDisposable
    {
        public const string ResourcePath = "Hud/HudMark";
        /// <summary>Where the gem floats above the hero's feet, and the life ring's radius and height (m).</summary>
        public const float HeadM = 2.75f, LifeRadiusM = 1.0f, LifeHeightM = 0.08f;
        /// <summary>The ring's radius at a beat's start and as it lands on the gem (m).</summary>
        public const float RingFarM = 1.0f, RingNearM = 0.34f;
        /// <summary>For this share of a period after a beat the landed ring shows on the gem.</summary>
        public const double LandedShare = 0.1;

        enum Shape { Ring = 0, Diamond = 1, Outline = 2 }

        static readonly Color Gem = HudText.Rgb(0x60c0ff), Ring = HudText.Rgb(0xd8e4ff), Downbeat = HudText.Rgb(0xffc040),
            Free = HudText.Rgb(0x9fb8ff), Life = HudText.Rgb(0xff4050), LifeLow = HudText.Rgb(0xffb0b8), Dark = new Color(0.02f, 0.016f, 0.04f);
        static readonly int ColorId = Shader.PropertyToID("_Color"), ShapeId = Shader.PropertyToID("_Shape"),
            InnerId = Shader.PropertyToID("_Inner"), FillId = Shader.PropertyToID("_Fill"), CoreId = Shader.PropertyToID("_Core");

        readonly Camera _camera;
        readonly Material _over, _ground;
        readonly Mesh _quad;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        int _ringMarked = -1; // the latest beat whose landed ring is in the on-the-beat trace (Moments)

        public BeatGem(Camera camera)
        {
            _camera = camera;
            if (camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var material = Resources.Load<Material>(ResourcePath);
            if (material == null)
            {
                Debug.LogWarning($"BeatGem: no material at Resources/{ResourcePath}");
                return;
            }
            _over = new Material(material);
            _over.SetFloat("_ZTest", (float)CompareFunction.Always);
            _over.renderQueue = (int)RenderQueue.Overlay;
            _ground = new Material(material);
            _ground.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            _quad = Quad();
        }

        /// <param name="feet">The hero's feet as drawn.</param>
        /// <param name="since">The periods since the latest beat (1 or more for a beat the lock came after: it never lands).</param>
        /// <param name="hit">The beat's hit, 1 on a reliable beat decaying over it (0 without one).</param>
        /// <param name="health">The hero's health, 0 to 1.</param>
        /// <param name="fade">The whole display's strength, 0 to 1.</param>
        public void Present(double s, Vector3 feet, in HeardBeat beat, bool locked, double sinceBeat, double hit, double health, bool bitten, float fade)
        {
            if (_over == null || fade <= 0) return;
            var head = feet + Vector3.up * HeadM;
            var face = _camera.transform.rotation;
            if (!locked)
            {
                // Free movement: the gem breathes.
                float breathe = Mathf.Pow(0.5f + 0.5f * Mathf.Cos((float)s * 3), 2);
                float r = 0.16f + 0.07f * breathe;
                Draw(_over, Behind(head, 2), face, r + 0.14f * breathe, Shape.Ring, Alpha(Free, 0.25f * fade), inner: 0);
                Draw(_over, Behind(head, 1), face, r + 0.04f, Shape.Ring, Alpha(Dark, 0.8f * fade), inner: 0);
                Draw(_over, head, face, r, Shape.Ring, Alpha(Free * 1.6f, fade), inner: 0);
            }
            else
            {
                float phase = (float)beat.Phase(s);
                float since = (float)sinceBeat;
                // The ring closing toward the next beat, heavier and gold before a downbeat.
                var ringColour = beat.NextDown ? Downbeat * 1.3f : Ring * 1.5f;
                float ringR = RingNearM + (RingFarM - RingNearM) * (1 - phase);
                Draw(_over, Behind(head, 4), face, ringR, Shape.Ring, Alpha(ringColour, 0.85f * Smooth(phase / 0.55f) * fade), inner: 1 - (beat.NextDown ? 0.09f : 0.05f) / ringR);
                // On the beat: the ring lands on the gem, then the burst throws out a diamond's edge.
                if (since < LandedShare)
                {
                    if (beat.Last > _ringMarked)
                    {
                        _ringMarked = beat.Last;
                        Moments.Mark(MomentKind.GemRing, beat.LastT, beat.Last);
                    }
                    // In front of the gem, its rim just outside the swollen gem's.
                    var landed = beat.LastDown ? Downbeat * 1.6f : Ring * 2f;
                    Draw(_over, Behind(head, -1), face, RingNearM + 0.04f, Shape.Ring, Alpha(landed, (1 - since / (float)LandedShare) * fade), inner: 1 - (beat.LastDown ? 0.1f : 0.07f) / (RingNearM + 0.04f));
                }
                float burst = Mathf.Max(0, 1 - since * 2);
                if (burst > 0)
                {
                    float b2 = burst * burst;
                    float r = (beat.LastDown ? 0.36f : 0.3f) + (beat.LastDown ? 0.75f : 0.55f) * (1 - b2);
                    var tint = beat.LastDown ? Downbeat * 1.4f : Gem * 1.8f;
                    Draw(_over, Behind(head, 3), face, r * 0.9f, Shape.Diamond, Alpha(tint, 0.3f * b2 * fade), core: 0);
                    Draw(_over, Behind(head, 2), face, r, Shape.Outline, Alpha(tint, b2 * fade), inner: 1 - 0.06f / r);
                }
                float gemR = 0.24f + 0.06f * (float)hit;
                Draw(_over, Behind(head, 1), face, gemR + 0.045f, Shape.Diamond, Alpha(Dark, 0.8f * fade), core: 0);
                var gem = (hit > 0.6 ? Color.white : Gem) * (1.4f + 1.6f * (float)hit);
                Draw(_over, head, face, gemR, Shape.Diamond, Alpha(gem, fade), core: 0.35f + 0.25f * (float)hit);
            }

            // The life ring, flat on the ground.
            var ground = feet + Vector3.up * LifeHeightM;
            var flat = Quaternion.Euler(90, 0, 0);
            Draw(_ground, ground, flat, LifeRadiusM, Shape.Ring, Alpha(Dark, 0.55f * fade), inner: 0.86f);
            var life = bitten ? Color.white * 2.5f : (health < HudPresenter.LowHealth && hit > 0.5 ? LifeLow : Life) * 1.7f;
            Draw(_ground, ground + Vector3.up * 0.005f, flat, LifeRadiusM * 0.985f, Shape.Ring, Alpha(life, 0.9f * fade), inner: 0.885f, fill: (float)health);
        }

        /// <summary>A mark's place <paramref name="layer"/> steps behind <paramref name="at"/> from the camera, so the transparent sort draws it first.</summary>
        Vector3 Behind(Vector3 at, int layer) => at + _camera.transform.forward * (0.03f * layer);

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3 - 2 * x);
        }

        void Draw(Material material, Vector3 at, Quaternion rotation, float radius, Shape shape, Color colour,
            float inner = 0.8f, float fill = 1, float core = 0.4f)
        {
            if (colour.a <= 0.002f || radius <= 0) return;
            _block.SetColor(ColorId, colour);
            _block.SetFloat(ShapeId, (float)shape);
            _block.SetFloat(InnerId, inner);
            _block.SetFloat(FillId, fill);
            _block.SetFloat(CoreId, core);
            var rp = new RenderParams(material)
            {
                camera = _camera,
                matProps = _block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(at, Vector3.one * (2 * radius + 1)),
            };
            Graphics.RenderMesh(rp, _quad, 0, Matrix4x4.TRS(at, rotation, Vector3.one * radius));
        }

        /// <summary>A quad from -1 to 1 in x and y, its uv the same.</summary>
        static Mesh Quad()
        {
            var mesh = new Mesh { name = "HudMark quad" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            if (_over == null) return;
            UnityEngine.Object.Destroy(_over);
            UnityEngine.Object.Destroy(_ground);
            UnityEngine.Object.Destroy(_quad);
        }
    }
}
