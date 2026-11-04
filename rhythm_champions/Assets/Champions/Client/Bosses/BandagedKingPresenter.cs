using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The Bandaged King's sandstorm (ps-8z4.19; boss-rush.md, The roster: "a sandstorm drops what you can see to a radius
    /// around the hero ... sweeping particle bands of sand"), from <see cref="BandagedKingModule.Storm"/>:
    /// - <b>The sight</b>: each frame it publishes the world's <c>_RC_Storm</c> (World.hlsl's StormSight): the floor's grid
    ///   and the arena's props, floor pieces and backdrop fade beyond <see cref="BandagedKingModule.SightM"/> of the hero as
    ///   the storm thickens. The King, his telegraphs and the walls draw through other materials and always glow. Still air
    ///   (strength 0) whenever his fight isn't on, so no other fight or the main game ever sees it.
    /// - <b>The sand</b>: long thin bands in his hue (palette.json's type colour, never the danger red) streaming across the
    ///   court on the wind, tapering at both ends, turned to the camera, more of them and brighter as the storm thickens,
    ///   none at the hero's feet. Seeded by band index and placed by song time alone, so a frame always looks the same.
    ///   Three brightness tiers (one dynamic mesh each) in the world's Glow material; an effect to the stills' type-ID pass.
    /// </summary>
    public sealed class BandagedKingPresenter : IDisposable
    {
        /// <summary>The bands: how many at full storm, the box round the court they wrap in (half-size, m), their speeds and lengths.</summary>
        const int Bands = 90, Segments = 6;
        const float BoxM = 30f, MinMps = 9f, MaxMps = 15f, MinLengthM = 3f, MaxLengthM = 8f, WidthM = 0.05f, SwayM = 0.35f;
        /// <summary>No band within this of the hero's feet (the hero always reads).</summary>
        const float ClearM = 2.2f;
        /// <summary>What shows of the floor and props beyond the sight in a full storm.</summary>
        const float Beyond = 0.06f;
        static readonly float[] TierBrightness = { 0.2f, 0.4f, 0.75f };
        /// <summary>The wind: across the court from the King's upper left toward the hero's lower right.</summary>
        static readonly Vector2 Wind = new Vector2(0.94f, -0.34f).normalized;

        static readonly int StormId = Shader.PropertyToID("_RC_Storm");

        readonly Material[] _tiers = new Material[3];
        readonly Mesh[] _meshes = new Mesh[3];
        readonly List<Vector3>[] _verts = { new List<Vector3>(1024), new List<Vector3>(1024), new List<Vector3>(1024) };
        readonly List<int>[] _tris = { new List<int>(2048), new List<int>(2048), new List<int>(2048) };
        readonly int _layer;
        readonly Color _hue;
        bool _drawn;

        BandagedKingPresenter(Palette palette, int layer)
        {
            var shared = Resources.Load<Material>(HeroMarker.MaterialPath)
                         ?? throw new InvalidOperationException($"BandagedKingPresenter: no material at Resources/{HeroMarker.MaterialPath}");
            _hue = palette.ForType(BandagedKingModule.ModuleId);
            for (int i = 0; i < 3; i++)
            {
                _tiers[i] = new Material(shared) { name = "Sand " + i };
                _tiers[i].SetFloat(IdPass.Id, IdPass.Effect);
                _meshes[i] = new Mesh { name = "Sand " + i };
                _meshes[i].MarkDynamic();
            }
            _layer = layer;
            Shader.SetGlobalVector(StormId, Vector4.zero);
        }

        /// <summary>The presenter, or null when the content has no Bandaged King.</summary>
        public static BandagedKingPresenter Create(Palette palette, EnemyRules enemies, int layer = 0) =>
            enemies.IndexOf(BandagedKingModule.ModuleId) < 0 ? null : new BandagedKingPresenter(palette, layer);

        /// <summary>The storm at song time <paramref name="song"/> for <paramref name="camera"/>: still air unless his fight is on.</summary>
        public void Present(double song, Simulation sim, Vector2 hero, Camera camera)
        {
            _drawn = false;
            var fight = sim.Fight;
            float storm = fight.Module is BandagedKingModule king && fight.InFight ? (float)king.Storm(fight, song) : 0f;
            Shader.SetGlobalVector(StormId, new Vector4((float)BandagedKingModule.SightM, (float)BandagedKingModule.SightFeatherM, storm, Beyond));
            if (storm <= 0.01f) return;
            var k = (BandagedKingModule)fight.Module;
            var toCamera = camera != null ? camera.transform.forward * -1f : new Vector3(0, 0.87f, -0.5f);
            Sand(song, storm, new Vector2((float)k.OriginX, (float)k.OriginY), hero, toCamera);
            for (int i = 0; i < 3; i++)
            {
                _tiers[i].SetColor("_Color", _hue.linear * TierBrightness[i] * storm);
                var m = _meshes[i];
                m.Clear();
                m.SetVertices(_verts[i]);
                m.SetTriangles(_tris[i], 0);
                m.RecalculateBounds();
            }
            _drawn = true;
            Queue(camera);
        }

        /// <summary>A band's fixed draw from its index: a hash in [0, 1).</summary>
        static float Hash(int band, int salt)
        {
            uint h = (uint)(band * 747796405 + salt * 2891336453u + 12345);
            h = (h ^ (h >> 16)) * 0x45d9f3b;
            h ^= h >> 16;
            return (h & 0xffffff) / (float)0x1000000;
        }

        void Sand(double song, float storm, Vector2 origin, Vector2 hero, Vector3 toCamera)
        {
            for (int i = 0; i < 3; i++) { _verts[i].Clear(); _tris[i].Clear(); }
            int n = Mathf.RoundToInt(Bands * Mathf.Clamp01(storm));
            var across = new Vector2(-Wind.y, Wind.x);
            for (int band = 0; band < n; band++)
            {
                float speed = Mathf.Lerp(MinMps, MaxMps, Hash(band, 1)), length = Mathf.Lerp(MinLengthM, MaxLengthM, Hash(band, 2));
                float lane = (Hash(band, 3) * 2 - 1) * BoxM, height = Mathf.Lerp(0.25f, 2.6f, Hash(band, 4) * Hash(band, 4));
                float turn = (Hash(band, 5) - 0.5f) * 0.25f, phase = Hash(band, 6) * 2 * BoxM, sway = Hash(band, 7) * 6.28f;
                // Along the wind it wraps through the box, so it leaves one side as it comes in the other.
                float along = (float)((phase + song * speed) % (2 * BoxM)) - BoxM;
                var dir = new Vector2(Wind.x * Mathf.Cos(turn) - Wind.y * Mathf.Sin(turn), Wind.x * Mathf.Sin(turn) + Wind.y * Mathf.Cos(turn));
                var head = origin + Wind * along + across * lane;
                int tier = band % 3;
                var verts = _verts[tier];
                var tris = _tris[tier];
                int first = verts.Count;
                for (int s = 0; s <= Segments; s++)
                {
                    float u = (float)s / Segments;
                    var at = head - dir * (length * u) + across * (SwayM * Mathf.Sin(sway + u * 3.1f + (float)song * 1.7f));
                    // Clear of the hero's feet: the band thins away round them.
                    float clear = Mathf.Clamp01((Vector2.Distance(at, hero) - ClearM) / 1.5f);
                    float w = WidthM * Mathf.Sin(Mathf.PI * u) * clear;
                    var p = new Vector3(at.x, height + 0.15f * Mathf.Sin(sway + u * 2f), at.y);
                    var side = Vector3.Cross(new Vector3(dir.x, 0, dir.y), toCamera).normalized * w;
                    verts.Add(p - side);
                    verts.Add(p + side);
                    if (s == 0) continue;
                    int a = first + 2 * (s - 1);
                    tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                    tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
                }
            }
        }

        /// <summary>Queues the latest frame's sand again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera)
        {
            if (!_drawn) return;
            for (int i = 0; i < 3; i++)
            {
                if (_meshes[i].vertexCount == 0) continue;
                var rp = new RenderParams(_tiers[i]) { layer = _layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, camera = camera };
                Graphics.RenderMesh(rp, _meshes[i], 0, Matrix4x4.identity);
            }
        }

        public void Dispose()
        {
            Shader.SetGlobalVector(StormId, Vector4.zero);
            for (int i = 0; i < 3; i++)
                foreach (var o in new UnityEngine.Object[] { _tiers[i], _meshes[i] })
                {
                    if (o == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                    else UnityEngine.Object.DestroyImmediate(o);
                }
        }
    }
}
