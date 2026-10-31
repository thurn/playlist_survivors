using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// A trailer shot's boss fight as the client draws it (trailer.md, "The boss fight"; the timeline's
    /// <see cref="BossAttack"/>s). Each attack's zone glows on the ground from its windup to its impact in its own procedural
    /// mesh (a breath's cone, a sweep's disc) and the Demo3D/BossTelegraph shader: a rim, a fill charging outward to reach
    /// the rim on the impact, a throb on each beat, and the blow's flash. On the impact the post flashes and the camera
    /// shakes as for the heaviest break (<see cref="Flashes"/>, <see cref="Shakes"/>, BreakMoments' maxima). The hero's
    /// dodge kicks up dust where it starts and lands (<see cref="Bursts"/>) and plays the hero's run clip (<see cref="Dodge"/>).
    /// A shot's zones show only while it is on screen (<see cref="Show"/>).
    /// </summary>
    public sealed class BossTelegraphs : IDisposable
    {
        /// <summary>The breath's fire and the sweep's blood-red (linear, HDR: past the bloom's knee).</summary>
        public static readonly Color BreathColor = new Color(4.0f, 0.95f, 0.12f), SweepColor = new Color(3.6f, 0.22f, 0.32f);
        /// <summary>How long a zone's flash lasts after its blow lands (s).</summary>
        public const double FlashS = 0.3;
        /// <summary>The zone's height above the ground (m).</summary>
        public const float LiftM = 0.06f;

        static readonly int ColorId = Shader.PropertyToID("_Color"), ProgressId = Shader.PropertyToID("_Progress"),
            PulseId = Shader.PropertyToID("_Pulse"), FlashId = Shader.PropertyToID("_Flash"), FadeId = Shader.PropertyToID("_Fade"),
            ConeId = Shader.PropertyToID("_Cone");

        sealed class Zone
        {
            public BossAttack Attack;
            public MeshRenderer Renderer;
        }

        readonly Timeline _tl;
        readonly GameObject _root;
        readonly Material _material;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly Zone[] _zones;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        public int Count => _zones.Length;

        public BossTelegraphs(Timeline tl, Transform parent)
        {
            _tl = tl;
            _root = new GameObject("BossTelegraphs");
            _root.transform.SetParent(parent, false);
            _root.SetActive(false);
            var shader = Resources.Load<Shader>("BossTelegraph") ?? throw new InvalidOperationException("BossTelegraphs: no Resources/BossTelegraph shader");
            _material = new Material(shader) { name = "BossTelegraph" };
            _owned.Add(_material);
            _zones = new Zone[tl.BossAttacks.Length];
            for (int i = 0; i < _zones.Length; i++)
            {
                var a = tl.BossAttacks[i];
                var mesh = a.Kind == BossAttackKind.Breath ? Cone(a) : Disc(a);
                mesh.name = "BossTelegraph" + (i + 1);
                _owned.Add(mesh);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.enabled = false;
                _zones[i] = new Zone { Attack = a, Renderer = r };
            }
        }

        /// <summary>Shows the zones (the shot is on screen) or hides them all.</summary>
        public void Show(bool on) => _root.SetActive(on);

        /// <summary>Each zone at song time <paramref name="t"/>: lit from its windup, charging to its impact, flashing after it.</summary>
        public void Frame(double t)
        {
            var map = _tl.Map;
            double x = map.BeatIndexAt(t);
            float beat = (float)(x - Math.Floor(x));
            foreach (var z in _zones)
            {
                var a = z.Attack;
                bool on = t >= a.Windup && t < a.Impact + FlashS;
                z.Renderer.enabled = on;
                if (!on) continue;
                bool landed = t >= a.Impact;
                float progress = Mathf.Clamp01((float)((t - a.Windup) / (a.Impact - a.Windup)));
                float flash = landed ? 1f - (float)((t - a.Impact) / FlashS) : 0f;
                float fade = landed ? flash : Mathf.Clamp01((float)((t - a.Windup) / 0.08));
                _block.SetColor(ColorId, a.Kind == BossAttackKind.Breath ? BreathColor : SweepColor);
                _block.SetFloat(ProgressId, progress);
                _block.SetFloat(PulseId, landed ? 0f : (1 - beat) * (1 - beat));
                _block.SetFloat(FlashId, landed ? Mathf.Sqrt(flash) : 0f);
                _block.SetFloat(FadeId, fade);
                _block.SetFloat(ConeId, a.Kind == BossAttackKind.Breath ? 1f : 0f);
                z.Renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>Is the hero dodging at song time <paramref name="t"/>? Then <paramref name="since"/> is when the dodge began.</summary>
        public bool Dodge(double t, out double since)
        {
            foreach (var a in _tl.BossAttacks)
                if (t >= a.DodgeFrom && t < a.DodgeTo + 0.04) { since = a.DodgeFrom; return true; }
            since = double.NaN;
            return false;
        }

        /// <summary>Each blow's post flash (PostFeel): the heaviest a break gets.</summary>
        public IEnumerable<(double time, float strength)> Flashes()
        {
            foreach (var a in _tl.BossAttacks) yield return (a.Impact, BreakMoments.MaxFlash);
        }

        /// <summary>Each blow's camera shake (CameraFeel): the heaviest a break gets.</summary>
        public IEnumerable<(double time, float amplitude)> Shakes()
        {
            foreach (var a in _tl.BossAttacks) yield return (a.Impact, BreakMoments.MaxShake);
        }

        /// <summary>The dodges' dust (EffectDriver.SetBursts): where each dodge kicks off, and where it lands.</summary>
        public List<BreakMoments.Burst> Bursts()
        {
            var bursts = new List<BreakMoments.Burst>();
            for (int i = 0; i < _tl.BossAttacks.Length; i++)
            {
                var a = _tl.BossAttacks[i];
                var from = _tl.PositionAt(0, a.DodgeFrom);
                var to = _tl.PositionAt(0, a.DodgeTo);
                var blow = new Vector3(to.X - from.X, 0, to.Y - from.Y).normalized;
                uint seed = (uint)(i + 1) * 2246822519u;
                bursts.Add(new BreakMoments.Burst { Time = a.DodgeFrom, Effect = "dust-puff", Position = new Vector3(from.X, 0.1f, from.Y), Blow = -blow, Scale = 2.2f, Seed = seed });
                bursts.Add(new BreakMoments.Burst { Time = a.DodgeTo, Effect = "dust-puff", Position = new Vector3(to.X, 0.1f, to.Y), Blow = blow, Scale = 1.6f, Seed = seed ^ 0x5A5Au });
            }
            return bursts;
        }

        const int Rings = 12;

        /// <summary>A breath's cone: rings out from its apex, each a fan across its angle; uv.x out, uv.y across.</summary>
        static Mesh Cone(BossAttack a)
        {
            const int across = 24;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int k = 0; k <= Rings; k++)
            {
                float u = k / (float)Rings;
                for (int j = 0; j <= across; j++)
                {
                    float v = j / (float)across * 2f - 1f;
                    var d = Easing.Dir(Easing.Yaw(a.Direction) + v * a.HalfAngle);
                    var p = a.Origin + d * (a.RadiusM * u);
                    verts.Add(new Vector3(p.X, LiftM, p.Y));
                    uvs.Add(new Vector2(u, v));
                }
            }
            Grid(tris, Rings, across);
            return Build(verts, uvs, tris);
        }

        /// <summary>A sweep's disc: rings out from the dragon; uv.x out.</summary>
        static Mesh Disc(BossAttack a)
        {
            const int round = 72;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int k = 0; k <= Rings; k++)
            {
                float u = k / (float)Rings;
                for (int j = 0; j <= round; j++)
                {
                    var d = Easing.Dir(j / (float)round * 2f * Mathf.PI);
                    var p = a.Origin + d * (a.RadiusM * u);
                    verts.Add(new Vector3(p.X, LiftM, p.Y));
                    uvs.Add(new Vector2(u, 0f));
                }
            }
            Grid(tris, Rings, round);
            return Build(verts, uvs, tris);
        }

        static void Grid(List<int> tris, int rings, int columns)
        {
            int row = columns + 1;
            for (int k = 0; k < rings; k++)
                for (int j = 0; j < columns; j++)
                {
                    int i0 = k * row + j, i1 = i0 + 1, i2 = i0 + row, i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
        }

        static Mesh Build(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
