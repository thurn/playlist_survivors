using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The horde's stand-in drawing until the trailer's crowd is ported (ps-6y2.28): each mob a rim-lit spindle of its type's
    /// height and radius whose silhouette glows in its palette colour over a near-black body (the StandIn shader), a flyer
    /// hovering, one mesh per type rebuilt each frame in world space (one draw per type). It shows no hit flash, flare or
    /// shards: a kill just ends it. Each type writes its monster code in the stills' type-ID pass.
    /// </summary>
    public sealed class StandInCrowd : ICrowdView
    {
        public const int Sides = 8;
        /// <summary>The spindle's profile: (radius share, height share) from the feet up.</summary>
        static readonly Vector2[] Profile = { new Vector2(0, 0), new Vector2(0.75f, 0.06f), new Vector2(1, 0.4f), new Vector2(0.7f, 0.8f), new Vector2(0, 1) };

        sealed class Kind
        {
            public Material Material;
            public Mesh Mesh;
            public float Height, Radius, Hover;
            public readonly List<Vector3> Vertices = new List<Vector3>(), Normals = new List<Vector3>();
            public readonly List<int> Indices = new List<int>();
            public int Count;
        }

        readonly Kind[] _kinds;
        readonly int _layer;
        readonly Vector3[] _unit, _unitNormals;

        public StandInCrowd(EnemyRules cast, Palette palette, int layer = 0)
        {
            _layer = layer;
            _kinds = new Kind[cast.Cast.Length];
            for (int c = 0; c < _kinds.Length; c++)
            {
                var type = cast.Cast[c];
                if (type.IsBoss) continue;
                var mesh = new Mesh { name = "Stand-in " + type.Id };
                mesh.MarkDynamic();
                bool flyer = type.Role == EnemyRole.Swarmer;
                _kinds[c] = new Kind
                {
                    Material = StandInMaterial("Stand-in " + type.Id, palette.ForType(type.Id), palette.VoidBody, IdPass.Monster(c)),
                    Mesh = mesh, Height = (float)Math.Max(0.3, type.SizeM), Radius = (float)type.RadiusM, Hover = flyer ? 1.2f : 0,
                };
            }
            // One spindle of unit radius and height, smooth-shaded so the rim reads round.
            _unit = new Vector3[Profile.Length * Sides];
            _unitNormals = new Vector3[_unit.Length];
            for (int p = 0; p < Profile.Length; p++)
            {
                var before = Profile[Math.Max(0, p - 1)];
                var after = Profile[Math.Min(Profile.Length - 1, p + 1)];
                var tangent = after - before;
                var outward = new Vector2(tangent.y, -tangent.x).normalized;
                for (int s = 0; s < Sides; s++)
                {
                    float a = 2 * Mathf.PI * s / Sides;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    _unit[p * Sides + s] = d * Profile[p].x + Vector3.up * Profile[p].y;
                    _unitNormals[p * Sides + s] = (d * outward.x + Vector3.up * outward.y).normalized;
                }
            }
        }

        static Material StandInMaterial(string name, Color line, Color body, int idCode)
        {
            var shader = Resources.Load<Shader>("Champions/StandIn") ?? throw new InvalidOperationException("StandInCrowd: no Champions/StandIn shader");
            var m = new Material(shader) { name = name };
            m.SetColor("_Color", line);
            m.SetColor("_Body", body);
            m.SetFloat(IdPass.Id, idCode);
            return m;
        }

        public void Begin(double song)
        {
            foreach (var k in _kinds)
            {
                if (k == null) continue;
                k.Vertices.Clear();
                k.Normals.Clear();
                k.Indices.Clear();
                k.Count = 0;
            }
        }

        public void Mob(in CrowdMob mob)
        {
            var k = mob.Cast < _kinds.Length ? _kinds[mob.Cast] : null;
            if (k == null) return;
            int first = k.Vertices.Count;
            var scale = new Vector3(k.Radius, k.Height, k.Radius);
            var origin = mob.Position + Vector3.up * k.Hover;
            for (int i = 0; i < _unit.Length; i++)
            {
                k.Vertices.Add(origin + Vector3.Scale(_unit[i], scale));
                k.Normals.Add(_unitNormals[i]);
            }
            for (int p = 0; p + 1 < Profile.Length; p++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = first + p * Sides + s, b = first + p * Sides + (s + 1) % Sides;
                    int c = a + Sides, d = b + Sides;
                    k.Indices.Add(a); k.Indices.Add(c); k.Indices.Add(b);
                    k.Indices.Add(b); k.Indices.Add(c); k.Indices.Add(d);
                }
            k.Count++;
        }

        public void Break(in CrowdMob mob, double time, Vector2 blow, float power)
        {
            // No shards for a stand-in: it is gone on its kill's frame.
        }

        public void Draw(double song, Camera camera)
        {
            foreach (var k in _kinds)
            {
                if (k == null) continue;
                k.Mesh.Clear();
                if (k.Count == 0) continue;
                k.Mesh.indexFormat = k.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                k.Mesh.SetVertices(k.Vertices);
                k.Mesh.SetNormals(k.Normals);
                k.Mesh.SetTriangles(k.Indices, 0, calculateBounds: true);
            }
            Queue(camera);
        }

        public void Queue(Camera camera)
        {
            foreach (var k in _kinds)
            {
                if (k == null || k.Count == 0) continue;
                var rp = new RenderParams(k.Material) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
                Graphics.RenderMesh(rp, k.Mesh, 0, Matrix4x4.identity);
            }
        }

        public void Dispose()
        {
            foreach (var k in _kinds)
            {
                if (k == null) continue;
                Destroy(k.Mesh);
                Destroy(k.Material);
            }
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
