using System;
using System.Collections.Generic;
using Game.Client.Effects;
using Game.Client.Loop;
using Game.Client.Sound;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Weapons
{
    /// <summary>
    /// The lightning's bolts (unity-game.md, The Other Weapons and Weapon and Ability Effects), each
    /// from its bolt event (<see cref="SimEventKind.Bolt"/>) at its song time:
    /// - **Lightning Ring**'s strike: a jagged bolt from the sky onto its point, white-hot at the core
    ///   in the ring's electric blue, with two short forks, sparks and a flash of light where it lands
    ///   (strike-sparks), and its thunder (lightning-strike);
    /// - **Thunder Loop**'s return (the evolution's layer): the same in its magenta, thicker, longer
    ///   lived and crowned with a third fork, its thunder lower;
    /// - **Chain Lightning**'s jump: a jagged arc from the hero, or the enemy before, to its enemy,
    ///   bowed up over the crowd between them, in lemon yellow, a few sparks there (jump-sparks) and a
    ///   zap climbing the major pentatonic jump by jump (lightning-jump); each lasts past the next
    ///   jump, so the chain reads as one.
    /// Their hits crackle (lightning-hit) in place of the sword's hit. A bolt flashes overbright, then
    /// fades and flickers through its short life, its path re-drawn <see cref="Flickers"/> times a
    /// second from the bolt and the song time alone, so a still at a fixed time draws the same bolt.
    /// All bolts are one camera-facing ribbon mesh, rebuilt each frame, drawn additive in HDR so
    /// bloom makes the glow (Resources/Weapons/LightningBolt); a headless player draws nothing.
    /// </summary>
    public sealed class LightningView : IPresenter, IDisposable
    {
        public const string ResourcePath = "Weapons/LightningBolt";
        /// <summary>A strike falls from this high above its point, its top this far aside at most.</summary>
        public const float SkyM = 14f, SkySwayM = 2.5f;
        /// <summary>A jump arcs between bodies at this height (the hero's chest, then the enemies'), bowed up by this share of its length, at least <see cref="BowMinM"/>.</summary>
        public const float BodyM = 1f, Bow = 0.3f, BowMinM = 0.6f;
        /// <summary>Paths re-drawn this many times a second.</summary>
        public const float Flickers = 30f;
        /// <summary>A chain's jumps climb the major pentatonic from its root, an octave and more.</summary>
        static readonly float[] JumpSteps = { 1f, 9f / 8, 5f / 4, 3f / 2, 5f / 3, 2f, 9f / 4, 5f / 2 };
        const int Capacity = 128, StrikeSegments = 12, JumpSegments = 8, ForkSegments = 4;

        struct Bolt
        {
            public double T;
            public BoltKind Kind;
            public Vector3 From, To;
            public Color Color;
            public uint Seed;
        }

        readonly WeaponLooks _looks;
        readonly EffectsPresenter _effects;
        readonly SoundPresenter _sound;
        readonly Camera _camera;
        readonly Material _material;
        readonly Mesh _mesh;
        readonly int _strikeSparks, _jumpSparks;
        readonly List<Bolt> _pending = new List<Bolt>(64);
        readonly List<Bolt> _live = new List<Bolt>(Capacity);
        readonly List<Vector3> _verts = new List<Vector3>(4096);
        readonly List<Vector2> _uvs = new List<Vector2>(4096);
        readonly List<Color> _colors = new List<Color>(4096);
        readonly List<int> _tris = new List<int>(6144);
        readonly Vector3[] _path = new Vector3[StrikeSegments + 1];

        /// <param name="effects">The effects, or null without compute.</param>
        /// <param name="sound">The sounds, or null without them; the lightning's hits are this view's to sound.</param>
        public LightningView(Camera camera, WeaponLooks looks, EffectsPresenter effects, SoundPresenter sound)
        {
            _camera = camera;
            _looks = looks;
            _effects = effects;
            _sound = sound;
            for (int w = 0; w < looks.Count; w++)
                if (IsLightning(w)) sound?.VoiceHitsElsewhere(w);
            _strikeSparks = effects?.IndexOf("strike-sparks") ?? -1;
            _jumpSparks = effects?.IndexOf("jump-sparks") ?? -1;
            if (camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var material = Resources.Load<Material>(ResourcePath);
            if (material == null)
            {
                Debug.LogWarning($"LightningView: no material at Resources/{ResourcePath}");
                return;
            }
            _material = new Material(material);
            _mesh = new Mesh { name = "Lightning bolts" };
            _mesh.MarkDynamic();
        }

        /// <summary>Bolts drawn or still to come.</summary>
        public int Count => _pending.Count + _live.Count;

        public static float Life(BoltKind kind) => kind == BoltKind.Return ? 0.3f : kind == BoltKind.Strike ? 0.22f : 0.26f;

        bool IsLightning(int weapon) => _looks.Of(weapon) == WeaponLooks.Family.Ring || _looks.Of(weapon) == WeaponLooks.Family.Chain;

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Bolt:
                {
                    var b = e.AsBolt;
                    bool jump = b.Kind == BoltKind.Jump;
                    var to = new Vector3((float)b.X, jump ? BodyM : 0f, (float)b.Y);
                    uint seed = EffectSystem.Hash((uint)(b.T * 10000) ^ (uint)b.Index * 0x9E3779B1u, (uint)(b.X * 64) ^ (uint)(b.Y * 4096) ^ (uint)b.Kind);
                    Vector3 from;
                    if (jump) from = new Vector3((float)b.FromX, BodyM, (float)b.FromY);
                    else
                    {
                        float a = Rand(seed, 1) * 2 * Mathf.PI, r = SkySwayM * Mathf.Sqrt(Rand(seed, 2));
                        from = to + new Vector3(Mathf.Cos(a) * r, SkyM, Mathf.Sin(a) * r);
                    }
                    var color = _looks.Colour(b.Weapon);
                    _pending.Add(new Bolt { T = b.T, Kind = b.Kind, From = from, To = to, Color = color, Seed = seed });
                    var dir = to - from;
                    _effects?.Cues.Spark(b.T, jump ? _jumpSparks : _strikeSparks, to, new Vector2(dir.x, dir.z).normalized,
                        b.Kind == BoltKind.Return ? 1.3f : 1f, color);
                    if (jump) _sound?.Request(SoundKind.LightningJump, b.T, 1f, JumpSteps[Math.Min(b.Index, JumpSteps.Length - 1)], 0.6f);
                    else _sound?.Request(SoundKind.LightningStrike, b.T, 1f, b.Kind == BoltKind.Return ? 0.8f : 1f, b.Index == 0 ? 1f : 0.7f);
                    break;
                }
                case SimEventKind.Hit:
                {
                    var h = e.AsHit;
                    if (h.OnHero || h.Damage <= 0 || !IsLightning(h.Weapon)) break;
                    _sound?.Request(SoundKind.LightningHit, e.T, 1f, 1f, 0.5f);
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame)
        {
            double now = frame.Song;
            for (int i = 0; i < _pending.Count;)
            {
                var b = _pending[i];
                if (b.T > now) { i++; continue; }
                _pending[i] = _pending[_pending.Count - 1];
                _pending.RemoveAt(_pending.Count - 1);
                if (_live.Count == Capacity) _live.RemoveAt(0); // the oldest
                _live.Add(b);
            }
            for (int i = 0; i < _live.Count;)
            {
                if (now - _live[i].T >= Life(_live[i].Kind)) _live.RemoveAt(i);
                else i++;
            }
            if (_material == null || _live.Count == 0) return;
            _verts.Clear();
            _uvs.Clear();
            _colors.Clear();
            _tris.Clear();
            var eye = _camera.transform.position;
            foreach (var b in _live)
            {
                float age = (float)(now - b.T);
                if (age >= 0) Draw(b, age, Life(b.Kind), eye);
            }
            if (_tris.Count == 0) return;
            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0, false);
            _mesh.RecalculateBounds();
            var rp = new RenderParams(_material)
            {
                camera = _camera,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = _mesh.bounds,
            };
            Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
        }

        /// <summary>Bolt <paramref name="b"/> at <paramref name="age"/> seconds: its main path and forks as ribbons.</summary>
        void Draw(in Bolt b, float age, float life, Vector3 eye)
        {
            // Overbright for its first frames, then fading with a flicker; a new path each flicker.
            uint seed = EffectSystem.Hash(b.Seed, (uint)Mathf.FloorToInt(age * Flickers));
            float fade = (1f - age / life) * (1f - age / life);
            float intensity = (age < 0.04f ? 2.2f : 1f) * fade * (0.7f + 0.3f * Rand(seed, 3));
            bool jump = b.Kind == BoltKind.Jump;
            float width = b.Kind == BoltKind.Return ? 1f : jump ? 0.7f : 0.8f;
            int n = jump ? JumpSegments : StrikeSegments;
            Path(b.From, b.To, n, jump ? 0.12f : 0.07f, seed, _path);
            if (jump)
            {
                float bow = Mathf.Max(BowMinM, Bow * (b.To - b.From).magnitude);
                for (int k = 1; k < n; k++) _path[k].y += bow * Mathf.Sin(Mathf.PI * k / n);
            }
            Ribbon(_path, n + 1, width, intensity, b.Color, eye);
            if (jump) return;
            // Forks off the upper half, falling away from the main path.
            int forks = b.Kind == BoltKind.Return ? 3 : 2;
            Span<Vector3> fork = stackalloc Vector3[ForkSegments + 1];
            for (int f = 0; f < forks; f++)
            {
                int at = 2 + (int)(Rand(seed, 10u + (uint)f) * (n / 2));
                var start = _path[at];
                float a = Rand(seed, 20u + (uint)f) * 2 * Mathf.PI, len = 1.5f + 2f * Rand(seed, 30u + (uint)f);
                var end = start + new Vector3(Mathf.Cos(a) * len, -len * 1.2f, Mathf.Sin(a) * len);
                Path(start, end, ForkSegments, 0.15f, seed ^ (0xF0Au + (uint)f), fork);
                Ribbon(fork, ForkSegments + 1, width * 0.5f, intensity * 0.6f, b.Color, eye);
            }
        }

        /// <summary>A jagged path of <paramref name="n"/> segments from a to b: each inner point pushed aside by up to <paramref name="jag"/> of the length, less near the ends.</summary>
        static void Path(Vector3 a, Vector3 b, int n, float jag, uint seed, Span<Vector3> into)
        {
            var d = b - a;
            float len = d.magnitude;
            var side = Vector3.Cross(d, Mathf.Abs(d.y) > 0.9f * len ? Vector3.right : Vector3.up).normalized;
            var other = Vector3.Cross(d, side).normalized;
            for (int k = 0; k <= n; k++)
            {
                float u = (float)k / n;
                float taper = Mathf.Sin(u * Mathf.PI);
                float s = (Rand(seed, 100u + (uint)k) - 0.5f) * 2 * jag * len * taper;
                float o = (Rand(seed, 200u + (uint)k) - 0.5f) * 2 * jag * len * taper;
                into[k] = a + d * u + side * s + other * o;
            }
        }

        /// <summary>A camera-facing ribbon along <paramref name="count"/> points: uv.x across (-1 to 1), uv.y its intensity.</summary>
        void Ribbon(ReadOnlySpan<Vector3> points, int count, float width, float intensity, Color color, Vector3 eye)
        {
            int first = _verts.Count;
            for (int k = 0; k < count; k++)
            {
                var along = (k + 1 < count ? points[k + 1] : points[k]) - (k > 0 ? points[k - 1] : points[k]);
                var side = Vector3.Cross(along, eye - points[k]).normalized * (0.5f * width);
                _verts.Add(points[k] - side);
                _verts.Add(points[k] + side);
                _uvs.Add(new Vector2(-1, intensity));
                _uvs.Add(new Vector2(1, intensity));
                _colors.Add(color);
                _colors.Add(color);
                if (k == 0) continue;
                int v = first + 2 * k;
                _tris.Add(v - 2); _tris.Add(v - 1); _tris.Add(v);
                _tris.Add(v); _tris.Add(v - 1); _tris.Add(v + 1);
            }
        }

        static float Rand(uint seed, uint salt) => (EffectSystem.Hash(seed, salt) >> 8) * (1f / 16777216f);

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
