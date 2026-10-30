using System.Collections.Generic;
using UnityEngine;

namespace StyleLab.Runtime
{
    /// <summary>
    /// The hero's sword arcs and slam shock (additive ribbons), and the particles: a burst on
    /// every death in the treatment's colours, plus each treatment's ambient particles (embers
    /// off fire, glints off crystal on the beat, rainbow motes off spectrum, star motes off void).
    /// </summary>
    public sealed class Effects
    {
        sealed class Ribbon
        {
            public GameObject Go;
            public Material Material;
            public float Age;
            public float Sweep; // seconds to sweep
            public float Life;
            public bool Grow;
            public float Radius;
        }

        static readonly int AgeId = Shader.PropertyToID("_Age");
        static readonly int SweepId = Shader.PropertyToID("_Sweep");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly Shader _slash;
        readonly Transform _parent;
        readonly List<Ribbon> _ribbons = new List<Ribbon>();
        readonly ParticleSystem _rise, _fall;
        readonly System.Random _rng = new System.Random(7);
        int _lastBeat = -1;

        public Effects(StyleLabData data, Transform parent)
        {
            _parent = parent;
            _slash = data.Shader("StyleLab/Slash");
            var particle = new Material(data.Shader("StyleLab/Particle")) { name = "Particles" };
            _rise = MakeSystem("Rising particles", particle, -0.25f);
            _fall = MakeSystem("Falling particles", particle, 1.2f);
        }

        ParticleSystem MakeSystem(string name, Material material, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = 8000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startSpeed = 0;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.6f, 0.8f), new Keyframe(1, 0)));
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.5f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        Vector3 Sphere() => new Vector3(Range(-1, 1), Range(-1, 1), Range(-1, 1)).normalized;

        // Particle colours are 8-bit, so the brightest channel goes to 1 and the rest becomes alpha,
        // which the particle shader multiplies back in.
        static Color32 Hdr(Color c)
        {
            float m = Mathf.Max(c.r, Mathf.Max(c.g, Mathf.Max(c.b, 1e-4f)));
            return new Color(c.r / m, c.g / m, c.b / m, Mathf.Clamp01(m / 4f));
        }

        Color ColorOf(Style style, TypeData type)
        {
            var look = Look.Of(type.id);
            switch (style)
            {
                case Style.Baseline: return new Color(3f, 1.3f, 0.4f);
                case Style.Fire: return look.flame;
                case Style.Crystal: return look.gem * 3.5f;
                case Style.Synth: { var h = Color.HSVToRGB(Range(0, 1), 0.85f, 1f); return h * 3f; }
                case Style.Hologram: return look.holo * 1.6f;
                case Style.Chrome: return new Color(3f, 2.8f, 2.6f);
                case Style.Void: return look.voidRim * 1.2f;
                default: return look.particle;
            }
        }

        public void Burst(Vector3 at, Style style, TypeData type, int count)
        {
            float scale = Mathf.Clamp(type.heightM / 1.6f, 0.6f, 2.5f);
            for (int i = 0; i < count; i++)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = at + Sphere() * 0.25f * scale,
                    velocity = (Sphere() + Vector3.up * 0.8f) * Range(2f, 6f) * scale,
                    startSize = Range(0.06f, 0.16f) * scale,
                    startLifetime = Range(0.4f, 0.9f),
                    startColor = Hdr(ColorOf(style, type)),
                };
                _fall.Emit(p, 1);
            }
        }

        /// <summary>Each treatment's ambient particles, from a few monsters a frame.</summary>
        public void Ambient(Horde horde, Beat beat, float dt)
        {
            bool newBeat = beat.Index != _lastBeat;
            _lastBeat = beat.Index;
            foreach (var m in horde.All)
            {
                if (!m.Alive) continue;
                var style = m.Figure.Style;
                var look = Look.Of(m.Type.id);
                var body = m.Position + Vector3.up * (m.Type.hoverM + Range(0.1f, 0.9f) * m.Type.heightM)
                           + new Vector3(Range(-1, 1), 0, Range(-1, 1)) * m.Type.radius * 0.8f;
                switch (style)
                {
                    case Style.Fire:
                        if (_rng.NextDouble() < dt * 9f * m.Type.heightM)
                            _rise.Emit(new ParticleSystem.EmitParams
                            {
                                position = body, velocity = new Vector3(Range(-0.4f, 0.4f), Range(1.5f, 3f), Range(-0.4f, 0.4f)),
                                startSize = Range(0.03f, 0.08f), startLifetime = Range(0.6f, 1.4f),
                                startColor = Hdr(Color.Lerp(look.flame, look.core, Range(0, 0.5f))),
                            }, 1);
                        break;
                    case Style.Crystal:
                        if (newBeat && _rng.NextDouble() < 0.35)
                            _rise.Emit(new ParticleSystem.EmitParams
                            {
                                position = body, velocity = Vector3.up * 0.2f, startSize = Range(0.15f, 0.3f),
                                startLifetime = Range(0.15f, 0.3f), startColor = Hdr(look.gem * 4f + Color.white),
                            }, 1);
                        break;
                    case Style.Synth:
                        if (_rng.NextDouble() < dt * 3f)
                            _rise.Emit(new ParticleSystem.EmitParams
                            {
                                position = body, velocity = new Vector3(0, Range(0.6f, 1.4f), 0), startSize = Range(0.05f, 0.1f),
                                startLifetime = Range(0.8f, 1.5f), startColor = Hdr(Color.HSVToRGB(Range(0, 1), 0.8f, 1f) * 3f),
                            }, 1);
                        break;
                    case Style.Void:
                        if (_rng.NextDouble() < dt * 2.5f)
                            _rise.Emit(new ParticleSystem.EmitParams
                            {
                                position = body, velocity = Sphere() * 0.3f, startSize = Range(0.03f, 0.06f),
                                startLifetime = Range(0.5f, 1.2f), startColor = Hdr(Color.Lerp(look.voidRim, Color.white * 3f, 0.5f)),
                            }, 1);
                        break;
                }
            }
        }

        /// <summary>A sword arc round `center`: a ribbon sweeping over `arcDeg` centred on `yaw`.</summary>
        public void Arc(Vector3 center, float yaw, float arcDeg, float radius, Color color, bool clockwise)
        {
            var mesh = RibbonMesh(arcDeg, radius * 0.55f, radius, clockwise);
            var r = MakeRibbon(mesh, color, 0.09f, 0.3f);
            r.Go.transform.SetPositionAndRotation(center, Quaternion.Euler(0, yaw, 0));
            r.Go.transform.localRotation *= Quaternion.Euler(clockwise ? 8f : -8f, 0, 0);
        }

        /// <summary>The slam's shock ring, flat on the ground, growing out to `radius`.</summary>
        public void Shock(Vector3 center, float radius, Color color)
        {
            var mesh = RibbonMesh(360f, 0.82f, 1f, true);
            var r = MakeRibbon(mesh, color, 0f, 0.45f);
            r.Grow = true;
            r.Radius = radius;
            r.Go.transform.position = center + Vector3.up * 0.05f;
            r.Go.transform.localScale = Vector3.one * 0.3f;
        }

        Ribbon MakeRibbon(Mesh mesh, Color color, float sweep, float life)
        {
            var go = new GameObject("Ribbon");
            go.transform.SetParent(_parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = new Material(_slash);
            mat.SetVector(ColorId, color);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var r = new Ribbon { Go = go, Material = mat, Sweep = sweep, Life = life };
            _ribbons.Add(r);
            return r;
        }

        static Mesh RibbonMesh(float arcDeg, float inner, float outer, bool clockwise)
        {
            int segs = Mathf.Max(8, Mathf.CeilToInt(arcDeg / 4f));
            var verts = new Vector3[(segs + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float a = (clockwise ? (u - 0.5f) : (0.5f - u)) * arcDeg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir * outer;
                uvs[i * 2] = new Vector2(u, 0);
                uvs[i * 2 + 1] = new Vector2(u, 1);
                if (i < segs)
                {
                    int t = i * 6, v = i * 2;
                    tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                    tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
                }
            }
            var mesh = new Mesh { name = "Ribbon", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Update(float dt)
        {
            for (int i = _ribbons.Count - 1; i >= 0; i--)
            {
                var r = _ribbons[i];
                r.Age += dt;
                float sweep = r.Sweep > 0 ? Mathf.Clamp01(r.Age / r.Sweep) : 1f;
                float age = Mathf.Clamp01((r.Age - r.Sweep) / r.Life);
                r.Material.SetFloat(SweepId, sweep * 1.02f);
                r.Material.SetFloat(AgeId, age);
                if (r.Grow) r.Go.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, r.Radius, 1f - Mathf.Pow(1f - age, 3f));
                if (r.Age > r.Sweep + r.Life)
                {
                    Object.Destroy(r.Go.GetComponent<MeshFilter>().sharedMesh);
                    Object.Destroy(r.Material);
                    Object.Destroy(r.Go);
                    _ribbons.RemoveAt(i);
                }
            }
        }
    }
}
