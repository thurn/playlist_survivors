using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Render
{
    /// <summary>A cue's draw and compute cost when it fires (<see cref="Effects.Cost"/>).</summary>
    public struct EffectCost
    {
        /// <summary>The draws it adds to a frame with nothing else up: one per mesh its shapes use, one for its sparks.</summary>
        public int Draws;
        public int Shapes, Triangles, Sparks;
        /// <summary>Its sparks' seed dispatch: thread groups (one per burst, 64 threads each); 0 without sparks.</summary>
        public int ComputeGroups;
    }

    /// <summary>
    /// The hero's weapon effects (design.md, The Look &gt; Weapon effects; plan.md, Rendering &gt; Pipeline: Slash and
    /// Particle), driven by commands (<see cref="IEffectCommands"/>) and presented once a frame at the frame's song time.
    /// A cue expands its recipe (<see cref="EffectRecipes"/>) once, when it fires: each layer into shapes (Hovl's slash
    /// meshes, a strip, Hovl's sword) and spark bursts, records the shaders animate over their whole life, so a live effect
    /// costs the CPU nothing until it ends. Shapes are drawn instanced, one draw per mesh in use (Slash.shader: white core,
    /// faint white-blue edge); sparks in one compute dispatch on the frames bursts start and one draw (<see cref="Sparks"/>).
    /// Orbit Blades' ring is rebuilt each frame round the hero. Time is the song clock: a fixed-time run draws the same
    /// frames; a seek backwards clears everything.
    /// </summary>
    public sealed class Effects : IEffectCommands, IDisposable
    {
        public const int StripSegments = 24;

        static readonly int IdShapes = Shader.PropertyToID("_Shapes");
        static readonly int IdFrame = Shader.PropertyToID("_RingFrame");
        static readonly int IdInner = Shader.PropertyToID("_RingInner");
        static readonly int IdNow = Shader.PropertyToID("_EffectNow");

        /// <summary>The GPU's record of every shape on one mesh.</summary>
        sealed class Bucket
        {
            public string Name;
            public Mesh Mesh;
            public readonly List<EffectShape> Shapes = new List<EffectShape>();
            public int Persistent; // Shapes[0..Persistent) live on; the rest are this frame's (the orbit)
            public GraphicsBuffer Buffer;
            public MaterialPropertyBlock Block;
            public bool Dirty;
            public int Drawn;
        }

        struct Orbiting
        {
            public int Swords;
            public double T, Angle, Speed;
            public float Target, Radius;
            public bool Evolved;
        }

        readonly EffectRecipes _recipes;
        readonly Material _material;
        readonly Bucket[] _buckets;
        readonly int _strip;
        readonly Mesh _stripMesh;
        readonly Sparks _sparks;
        readonly List<(int Bucket, EffectShape Shape)> _shapes = new List<(int, EffectShape)>();
        readonly List<SparkBurst> _bursts = new List<SparkBurst>();
        readonly MeleeVisuals _melee = new MeleeVisuals();
        readonly ConstructVisuals _constructs;
        Orbiting _orbit;
        double _last = double.NegativeInfinity;

        /// <summary>The effects in <paramref name="lib"/>'s meshes and shaders, by <paramref name="recipes"/>, in the palette's hero white.</summary>
        public Effects(EffectLibrary lib, EffectRecipes recipes, Palette palette)
        {
            _recipes = recipes;
            _constructs = new ConstructVisuals(palette);
            if (lib.Slash == null || lib.Spark == null || lib.Sparks == null) throw new InvalidOperationException("EffectLibrary: missing its shaders");
            _material = new Material(lib.Slash) { name = "Slash", enableInstancing = false };
            _material.SetColor("_CoreColor", palette.HeroBlade.linear);
            _material.SetColor("_EdgeColor", palette.HeroEdge.linear);
            if (lib.Noise != null) _material.SetTexture("_Noise", lib.Noise);
            _material.SetFloat(IdPass.Id, IdPass.Effect);
            _stripMesh = Strip();
            _buckets = new Bucket[lib.Meshes.Length + 1];
            for (int i = 0; i < lib.Meshes.Length; i++)
                _buckets[i] = NewBucket(lib.Meshes[i].Name, lib.Meshes[i].Mesh, lib.Meshes[i].Frame, lib.Meshes[i].Inner);
            _strip = lib.Meshes.Length;
            _buckets[_strip] = NewBucket("strip", _stripMesh, Matrix4x4.identity, 0);
            foreach (var cue in recipes.Cues)
                foreach (var l in cue) CheckMesh(l);
            foreach (var l in recipes.Orbit) CheckMesh(l);
            foreach (var l in recipes.Halo) CheckMesh(l);
            _sparks = new Sparks(lib.Sparks, lib.Spark, palette.HeroBlade, palette.HeroEdge);
        }

        void CheckMesh(EffectLayer l)
        {
            if (l.Mesh != null && BucketOf(l) < 0)
                throw new InvalidOperationException($"{EffectRecipes.File}: no mesh {l.Mesh} in the effect library (re-run the effects bake)");
        }

        Bucket NewBucket(string name, Mesh mesh, Matrix4x4 frame, float inner)
        {
            var b = new Bucket { Name = name, Mesh = mesh, Block = new MaterialPropertyBlock() };
            b.Block.SetMatrix(IdFrame, frame);
            b.Block.SetFloat(IdInner, inner);
            return b;
        }

        /// <summary>Shapes alive now, across every mesh; sparks drawn by the latest frame (the ring's live span).</summary>
        public int LiveShapes { get; private set; }
        public int LiveSparks => _sparks.Drawn;
        /// <summary>The draws the latest frame queued.</summary>
        public int Draws { get; private set; }
        public Sparks Sparks => _sparks;

        public void Fire(EffectCue cue, in EffectShot shot)
        {
            if (MeleeVisuals.Handles(cue)) _melee.Fire(cue, shot);
            if (ConstructVisuals.Handles(cue)) _constructs.Fire(cue, shot);
            Expand(_recipes.Cues[(int)cue], shot);
            foreach (var (bucket, shape) in _shapes)
            {
                var b = _buckets[bucket];
                b.Shapes.Insert(b.Persistent++, shape);
                b.Dirty = true;
            }
            foreach (var s in _bursts)
                _sparks.Burst(s.Origin, s.Born, s.Direction, s.Spread, (int)s.Count, s.Speed, s.Life, s.Size, s.Drag, s.Gravity, s.Up, s.Brightness);
        }

        /// <summary>What <paramref name="cue"/> adds to a frame when it fires at <paramref name="power"/>: its shapes and sparks, the draws and compute.</summary>
        public EffectCost Cost(EffectCue cue, EffectPower power = EffectPower.Plain)
        {
            Expand(_recipes.Cues[(int)cue], new EffectShot { At = Vector2.zero, To = new Vector2(0, 4), Facing = Vector2.up, Power = power });
            var cost = new EffectCost { Shapes = _shapes.Count };
            var used = new HashSet<int>();
            foreach (var (bucket, _) in _shapes)
            {
                used.Add(bucket);
                cost.Triangles += (int)(_buckets[bucket].Mesh.GetIndexCount(0) / 3);
            }
            foreach (var s in _bursts)
            {
                cost.Sparks += (int)s.Count;
                cost.Triangles += 2 * (int)s.Count;
            }
            cost.ComputeGroups = _bursts.Count;
            cost.Draws = used.Count + (_bursts.Count > 0 ? 1 : 0);
            if (MeleeVisuals.Handles(cue))
            {
                var rigid = _melee.Cost(cue);
                cost.Draws += rigid.Draws; cost.Shapes += rigid.Shapes; cost.Triangles += rigid.Triangles;
            }
            if (ConstructVisuals.Handles(cue))
            {
                var construct = _constructs.Cost(cue);
                cost.Draws += construct.Draws; cost.Shapes += construct.Shapes; cost.Triangles += construct.Triangles;
            }
            return cost;
        }

        public void ConstructOrbit(int level, float area) => _constructs.Orbit(level, area);

        public void Orbit(double t, int swords, double angle, double speed, float radius, bool evolved = false)
        {
            bool fresh = _orbit.Swords == 0;
            _orbit.Swords = Math.Max(0, swords);
            _orbit.T = t;
            _orbit.Angle = angle;
            _orbit.Speed = speed;
            _orbit.Target = radius;
            if (fresh) _orbit.Radius = radius;
            _orbit.Evolved = evolved;
        }

        /// <summary>
        /// Draws the frame at song time <paramref name="now"/>, <paramref name="dt"/> s after the last, the hero's feet at
        /// <paramref name="hero"/> (ground x, y): ends what has run its life, rebuilds the orbit, seeds the frame's sparks and
        /// queues the draws for <paramref name="camera"/>.
        /// </summary>
        public void Present(double now, Vector2 hero, float dt, Camera camera)
        {
            if (now < _last) Clear();
            _last = now;
            int live = 0;
            foreach (var b in _buckets)
            {
                if (b.Shapes.Count > b.Persistent)
                {
                    b.Shapes.RemoveRange(b.Persistent, b.Shapes.Count - b.Persistent);
                    b.Dirty = true;
                }
                int kept = 0;
                for (int i = 0; i < b.Persistent; i++)
                    if (b.Shapes[i].End >= now) b.Shapes[kept++] = b.Shapes[i];
                if (kept != b.Persistent)
                {
                    b.Shapes.RemoveRange(kept, b.Persistent - kept);
                    b.Persistent = kept;
                    b.Dirty = true;
                }
            }
            if (_orbit.Swords > 0) OrbitShapes(now, hero, dt);
            foreach (var b in _buckets)
            {
                live += b.Shapes.Count;
                if (!b.Dirty) continue;
                b.Dirty = false;
                b.Drawn = b.Shapes.Count;
                if (b.Drawn == 0) continue;
                if (b.Buffer == null || b.Buffer.count < b.Drawn)
                {
                    b.Buffer?.Dispose();
                    b.Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.NextPowerOfTwo(Math.Max(16, b.Drawn)), EffectShape.Stride)
                        { name = "Effects " + b.Name };
                    b.Block.SetBuffer(IdShapes, b.Buffer);
                }
                b.Buffer.SetData(b.Shapes);
            }
            _melee.Present(now);
            _constructs.Present(now, hero);
            LiveShapes = live + _melee.Draws + _constructs.Shapes;
            foreach (var b in _buckets) b.Block.SetFloat(IdNow, (float)now);
            _sparks.Present(now, null);
            if (camera != null) Queue(camera);
        }

        /// <summary>Queues the frame's draws for the camera (again, for a second drawing of the same frame: the stills' ID pass).</summary>
        public void Queue(Camera camera)
        {
            int draws = 0;
            foreach (var b in _buckets)
            {
                if (b.Drawn == 0) continue;
                Graphics.RenderMeshPrimitives(Params(_material, b.Block, camera), b.Mesh, 0, b.Drawn);
                draws++;
            }
            _sparks.Queue(camera);
            _melee.Queue(camera);
            _constructs.Queue(camera);
            Draws = draws + _melee.Draws + _constructs.Draws + (_sparks.Drawn > 0 ? 1 : 0);
        }

        /// <summary>Drops every live effect (a seek backwards does it itself).</summary>
        public void Clear()
        {
            foreach (var b in _buckets)
            {
                b.Shapes.Clear();
                b.Persistent = 0;
                b.Dirty = true;
            }
            _sparks.Clear();
            _melee.Clear();
            _constructs.Clear();
            _orbit = default;
            _last = double.NegativeInfinity;
        }

        void OrbitShapes(double now, Vector2 hero, float dt)
        {
            _orbit.Radius += (_orbit.Target - _orbit.Radius) * (1 - Mathf.Exp(-Math.Max(dt, 0) / 0.12f));
            var layers = _orbit.Evolved ? _recipes.Halo : _recipes.Orbit;
            float sign = _orbit.Speed < 0 ? -1 : 1;
            for (int k = 0; k < _orbit.Swords; k++)
            {
                double a = _orbit.Angle + _orbit.Speed * (now - _orbit.T) + 2 * Math.PI * k / _orbit.Swords;
                var radial = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                var tangent = new Vector2(-radial.y, radial.x) * sign;
                foreach (var l in layers)
                {
                    var s = Steady(now, l);
                    if (l.Shape == LayerShape.Solid)
                    {
                        var at = hero + radial * _orbit.Radius;
                        s.Position = new Vector3(at.x, 0, at.y);
                        s.Facing = tangent;
                        s.Radius = l.Radius;
                    }
                    else
                    {
                        // The trail: an arc round the hero just behind the sword.
                        float half = l.Arc * 0.5f * Mathf.Deg2Rad;
                        s.Position = new Vector3(hero.x, 0, hero.y);
                        s.Facing = Rotate(radial, -sign * half);
                        s.Radius = _orbit.Radius + l.Radius;
                        s.Flags = sign < 0 ? EffectShape.Mirrored : 0;
                    }
                    _buckets[BucketOf(l)].Shapes.Add(s);
                }
            }
            foreach (var l in layers) _buckets[BucketOf(l)].Dirty = true;
        }

        /// <summary>A layer's shape held at full light (the orbit's, rebuilt each frame).</summary>
        static EffectShape Steady(double now, EffectLayer l) => new EffectShape
        {
            Born = (float)(now - 1), Life = 1e4f, HeadIn = 1e-7f, TravelIn = 1e-7f, Fade = 1e-3f, Arc = l.Arc * Mathf.Deg2Rad, Width = l.Width,
            Brightness = l.Brightness, Tilt = l.Tilt * Mathf.Deg2Rad, Height = l.Height, Grow = 1, Streak = l.Streak, Taper = l.Taper,
            Mode = l.Shape == LayerShape.Solid ? EffectShape.Solid : EffectShape.Arc0,
        };

        int BucketOf(EffectLayer l)
        {
            if (l.Shape == LayerShape.Ribbon || l.Shape == LayerShape.Upright) return _strip;
            for (int i = 0; i < _strip; i++)
                if (_buckets[i].Name == l.Mesh) return i;
            return -1;
        }

        /// <summary>Expands a recipe for a shot into <see cref="_shapes"/> and <see cref="_bursts"/>.</summary>
        void Expand(EffectLayer[] layers, in EffectShot shot)
        {
            _shapes.Clear();
            _bursts.Clear();
            var power = _recipes.Powers[(int)shot.Power];
            var path = shot.To - shot.At;
            float pathLength = path.magnitude;
            var pathWay = pathLength > 1e-4f ? path / pathLength : Way(shot.Facing);
            foreach (var l in layers)
            {
                bool onPath = l.Shape == LayerShape.Ribbon && l.Radius <= 0 || l.TravelPath || l.At == LayerPlace.Path;
                var facing = onPath ? pathWay : Way(shot.Facing);
                float radius = l.RadiusShot > 0 && shot.Radius > 0 ? shot.Radius * l.RadiusShot : l.Radius * power.Scale;
                for (int i = 0; i < l.Repeat; i++)
                {
                    double born = shot.T + l.Delay + i * l.Every;
                    float yaw = l.Yaw + i * l.YawStep;
                    var way = Rotate(facing, yaw * Mathf.Deg2Rad);
                    bool mirrored = shot.Mirrored ^ (l.Alternate && i % 2 == 1);
                    Vector2 at = l.At switch
                    {
                        LayerPlace.To => shot.To,
                        LayerPlace.Path => l.Repeat == 1 ? shot.At + path * 0.5f : shot.At + path * ((float)i / (l.Repeat - 1)),
                        LayerPlace.Around => shot.At + way * l.Spacing,
                        _ => shot.At,
                    };
                    if (l.Shape == LayerShape.Sparks)
                    {
                        Burst(l, shot, power, born, at, way, radius, mirrored);
                        continue;
                    }
                    float sweep = l.SweepShot && shot.Duration > 0 ? shot.Duration : l.Sweep;
                    float travel = l.TravelShot && shot.Duration > 0 ? shot.Duration : float.IsNaN(l.TravelS) ? sweep : l.TravelS;
                    float life = Mathf.Max(sweep, travel) + l.Linger;
                    float fade = float.IsNaN(l.Fade) ? l.Linger : Mathf.Min(l.Fade, life);
                    var s = new EffectShape
                    {
                        Born = (float)born, Facing = way, Radius = radius, Life = life, HeadIn = sweep / life, Fade = fade / life,
                        TravelIn = travel / life,
                        Width = l.Width, Brightness = l.Brightness * power.Brightness, Tilt = l.Tilt * Mathf.Deg2Rad, Height = l.Height,
                        Grow = l.Grow, Streak = l.Streak, Taper = l.Taper, Flags = mirrored ? EffectShape.Mirrored : 0,
                    };
                    var move = l.TravelPath ? path : way * l.TravelM;
                    s.Travel = new Vector3(move.x, l.TravelUpM, move.y);
                    switch (l.Shape)
                    {
                        case LayerShape.Arc:
                            s.Mode = EffectShape.Arc0;
                            s.Arc = (l.ArcShot && shot.ArcDeg > 0 ? shot.ArcDeg : l.Arc) * Mathf.Deg2Rad;
                            break;
                        case LayerShape.Solid:
                            s.Mode = EffectShape.Solid;
                            s.Radius = l.Radius;
                            break;
                        default:
                            s.Mode = l.Shape == LayerShape.Upright ? EffectShape.Upright : EffectShape.Ribbon;
                            s.Arc = l.Arc * power.Scale; // half width
                            if (l.Shape == LayerShape.Ribbon && l.Radius <= 0) s.Radius = Mathf.Max(pathLength, 0.1f);
                            else if (l.Shape == LayerShape.Upright) s.Radius = l.Radius;
                            break;
                    }
                    s.Position = new Vector3(at.x, 0, at.y);
                    _shapes.Add((BucketOf(l), s));
                }
            }
        }

        void Burst(EffectLayer l, in EffectShot shot, EffectRecipes.Power power, double born, Vector2 at, Vector2 way, float radius, bool mirrored)
        {
            Vector3 dir;
            if (l.At == LayerPlace.Edge)
            {
                // Off the blade where its sweep ends: out on the arc, along the sweep.
                float half = (shot.ArcDeg > 0 ? shot.ArcDeg : 150f) * 0.5f * Mathf.Deg2Rad * (mirrored ? -1 : 1);
                var radial = Rotate(way, half);
                at = shot.At + radial * (radius * 0.85f);
                way = Rotate(radial, mirrored ? -Mathf.PI / 2 : Mathf.PI / 2);
            }
            switch (l.Way)
            {
                case SparkWay.Up: dir = Vector3.up; break;
                case SparkWay.Out:
                    var o = at - shot.At;
                    var w = o.sqrMagnitude > 1e-6f ? o.normalized : way;
                    dir = new Vector3(w.x, 0, w.y);
                    break;
                case SparkWay.Back: dir = new Vector3(-way.x, 0, -way.y); break;
                default: dir = new Vector3(way.x, 0, way.y); break;
            }
            _bursts.Add(new SparkBurst
            {
                Origin = new Vector3(at.x, l.Height, at.y), Born = (float)born, Direction = dir, Spread = l.Spread * Mathf.Deg2Rad,
                Count = Mathf.Round(l.Count * power.Sparks), Speed = l.Speed, Life = l.Life, Size = l.Size, Drag = l.Drag,
                Gravity = l.Gravity, Up = l.Up, Brightness = l.Brightness * power.Brightness,
            });
        }

        static Vector2 Way(Vector2 v) => v.sqrMagnitude > 1e-8f ? v.normalized : Vector2.up;

        /// <summary>v turned counterclockwise (seen from above, the sim's x right and y up the screen) by <paramref name="radians"/>.</summary>
        static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The strip ribbons draw on: x along it (0 to 1), y across (-1 to 1), in <see cref="StripSegments"/> pieces.</summary>
        static Mesh Strip()
        {
            var vertices = new Vector3[(StripSegments + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var indices = new int[StripSegments * 6];
            for (int i = 0; i <= StripSegments; i++)
            {
                float x = (float)i / StripSegments;
                vertices[2 * i] = new Vector3(x, -1, 0);
                vertices[2 * i + 1] = new Vector3(x, 1, 0);
                normals[2 * i] = normals[2 * i + 1] = Vector3.forward;
                uvs[2 * i] = new Vector2(x, 0);
                uvs[2 * i + 1] = new Vector2(x, 1);
                if (i == StripSegments) break;
                int k = 6 * i, a = 2 * i;
                indices[k] = a; indices[k + 1] = a + 2; indices[k + 2] = a + 1;
                indices[k + 3] = a + 1; indices[k + 4] = a + 2; indices[k + 5] = a + 3;
            }
            var mesh = new Mesh { name = "Effect strip", vertices = vertices, normals = normals, uv = uvs, triangles = indices };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            return mesh;
        }

        internal static RenderParams Params(Material material, MaterialPropertyBlock block, Camera camera) =>
            new RenderParams(material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                matProps = block,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                camera = camera,
                layer = camera != null ? camera.gameObject.layer : 0,
            };

        internal static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        public void Dispose()
        {
            foreach (var b in _buckets) b.Buffer?.Dispose();
            _sparks.Dispose();
            _melee.Dispose();
            _constructs.Dispose();
            Destroy(_material);
            Destroy(_stripMesh);
        }
    }
}
