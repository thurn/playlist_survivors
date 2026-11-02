using System;
using Game.Client.Look;
using Game.Client.Loop;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Effects
{
    /// <summary>
    /// The finishers' rings (`--finishers`, docs/archive/design/game-modes.md, "Finishers"): a ring of crystal light on the ground
    /// at the hero's feet, closing from <see cref="StartRadiusM"/> to <see cref="EndRadiusM"/> over its telegraph so it
    /// arrives on the phrase downbeat, over a faint ring where it will land. It brightens as it closes and flares
    /// through its window; a hit takes it at the slam's impact (the sword's slam and its waves take over), a miss fades
    /// it after the window, a hold drops it. A retime eases onto the new downbeat over <see cref="RetimeEaseS"/> rather
    /// than jumping. Drawn on the sword's crystal wave shader and ring mesh (<see cref="CrystalWaves"/>); a function of
    /// the events and the song time. No HUD text: the trailer's rule.
    /// </summary>
    public sealed class FinisherRings : IPresenter, IDisposable
    {
        public const float StartRadiusM = 4.5f, EndRadiusM = 0.9f, Width = 0.35f, TargetWidth = 0.12f;
        /// <summary>The closing ring's light as it sets out and as it arrives, the target ring's, and the window's flare on the ring.</summary>
        public const float SetOutLight = 0.2f, ArriveLight = 0.9f, TargetLight = 0.12f, WindowLight = 1.6f;
        public const double RetimeEaseS = 0.15, FadeS = 0.2;
        const int Capacity = 4;

        struct Ring
        {
            public int Id;
            public double Start, Downbeat, Before, RetimedAt, Window;
            /// <summary>When it went: a hit's impact, a drop's time, or infinity while it stands (a miss fades after its window).</summary>
            public double Gone;
        }

        readonly Ring[] _rings = new Ring[Capacity];
        int _count;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly Camera _camera;
        static readonly int RadiusId = Shader.PropertyToID("_Radius"), WidthId = Shader.PropertyToID("_Width"),
            IntensityId = Shader.PropertyToID("_Intensity"), AgeId = Shader.PropertyToID("_Age");

        /// <summary>Where the hero is drawn, which the ring follows; the sim's position without it.</summary>
        public LookPresenter.HeroAt DrawnHero { get; set; }

        public FinisherRings(Camera camera, Shader shader)
        {
            _camera = camera;
            _material = new Material(shader) { name = "Finisher ring" };
            _mesh = CrystalWaves.Annulus();
        }

        public void OnEvent(in SimEvent e)
        {
            if (e.Kind != SimEventKind.Finisher) return;
            var f = e.AsFinisher;
            if (f.Phase == FinisherPhase.Ring)
            {
                if (_count == Capacity) Remove(0); // the oldest
                _rings[_count++] = new Ring
                {
                    Id = f.Ring, Start = f.Start, Downbeat = f.Downbeat, Before = f.Downbeat, RetimedAt = double.NegativeInfinity,
                    Window = f.Window, Gone = double.PositiveInfinity,
                };
                return;
            }
            int i = Find(f.Ring);
            if (i < 0) return;
            ref var r = ref _rings[i];
            switch (f.Phase)
            {
                case FinisherPhase.Retime:
                    // From wherever the ease had got to, onto the new downbeat.
                    r.Before = DownbeatAt(r, f.T);
                    r.Downbeat = f.Downbeat;
                    r.RetimedAt = f.T;
                    break;
                case FinisherPhase.Hit:
                    r.Gone = f.Impact;
                    break;
                case FinisherPhase.Drop:
                    r.Gone = f.T;
                    break;
            }
        }

        int Find(int id)
        {
            for (int i = 0; i < _count; i++)
                if (_rings[i].Id == id) return i;
            return -1;
        }

        static double DownbeatAt(in Ring r, double s)
        {
            double u = Math.Max(0, Math.Min(1, (s - r.RetimedAt) / RetimeEaseS));
            return r.Before + (r.Downbeat - r.Before) * u * u * (3 - 2 * u);
        }

        public void Present(in PresentFrame frame)
        {
            double s = frame.Song;
            double hx, hy;
            if (DrawnHero != null) DrawnHero(out hx, out hy, out _);
            else frame.State.Hero.PositionAt(s, out hx, out hy);
            var at = new Vector3((float)hx, CrystalWaves.Height, (float)hy);
            for (int i = 0; i < _count;)
            {
                ref var r = ref _rings[i];
                double downbeat = DownbeatAt(r, s);
                double fadeFrom = Math.Min(r.Gone, downbeat + r.Window);
                if (s >= fadeFrom + (r.Gone < double.PositiveInfinity ? 0 : FadeS))
                {
                    if (s > downbeat + 2) Remove(i);
                    else i++;
                    continue;
                }
                if (s >= r.Start)
                {
                    float u = (float)Math.Max(0, Math.Min(1, (s - r.Start) / Math.Max(1e-3, downbeat - r.Start)));
                    float fade = s > fadeFrom ? 1f - (float)((s - fadeFrom) / FadeS) : 1f;
                    bool window = Math.Abs(s - downbeat) <= r.Window;
                    float light = (SetOutLight + (ArriveLight - SetOutLight) * u * u) * (window ? WindowLight : 1f) * fade;
                    Draw(at, Mathf.Lerp(StartRadiusM, EndRadiusM, u), Width, light, (float)(s - r.Start));
                    Draw(at, EndRadiusM, TargetWidth, TargetLight * Mathf.Min(1f, 4f * u) * fade, 0f);
                }
                i++;
            }
        }

        void Draw(Vector3 at, float radius, float width, float light, float age)
        {
            if (light <= 0) return;
            _block.SetFloat(RadiusId, radius);
            _block.SetFloat(WidthId, Mathf.Min(width, radius));
            _block.SetFloat(IntensityId, light);
            _block.SetFloat(AgeId, age);
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

        void Remove(int i) => _rings[i] = _rings[--_count];

        public void Dispose()
        {
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
