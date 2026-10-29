using System;
using System.Collections.Generic;
using Demo2D.Choreography;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// Draws the hero, the horde and projectiles as flipbook sprites (PX5): per entity its frame,
    /// flip, flash, tint and fade, drawn back to front by its feet's y, over a pixel-blob shadow.
    ///
    /// Sprites are pooled SpriteRenderers rather than hand-instanced quads: URP's 2D renderer
    /// draws them across every type's atlas and lights them with its Light2Ds, and 400 of them
    /// batch into a few draws per atlas. The flash is one shared material per level
    /// (<see cref="FlashLevels"/> steps, a pixel-art flash), so no renderer needs its own block.
    /// Positions snap to whole texels, so the art never lands between grid pixels.
    ///
    /// The draw order is explicit: each frame ranks the sprites by their feet's texel row, then
    /// by entity, into their sorting order. Snapped feet often share a row, and the renderer
    /// breaks such ties by its own registration order, which pooling changes, so a seek and a
    /// run from the start would otherwise overlap a crowd differently.
    /// </summary>
    public sealed class SpriteCrowd : IDisposable
    {
        public const int FlashLevels = 4;
        /// <summary>A projectile flies this high over its ground position.</summary>
        public const float ProjectileHeightM = 1f;
        const float ShadowAlpha = 0.42f, ShadowRimAlpha = 0.22f;

        readonly SpriteEvaluator _eval;
        readonly Timeline _tl;
        readonly Sprite[][] _clipSprites;    // per annotation
        readonly Sprite[] _shadowOf;         // per timeline type, then the hero's last
        readonly Sprite _bolt, _boltShadow;
        readonly Material[] _flash;
        readonly Material _unlit;
        readonly GameObject _root;
        readonly List<SpriteRenderer> _bodies = new List<SpriteRenderer>(), _shadows = new List<SpriteRenderer>();
        readonly List<Texture2D> _made = new List<Texture2D>();
        int _used;
        long[] _sortKeys = new long[64];
        int[] _sortIndex = new int[64];

        public int Drawn => _used;

        /// <param name="clipSprites">Each annotation's frames.</param>
        /// <param name="heroRadiusM">The hero's footprint (the timeline's radii are the roster types').</param>
        public SpriteCrowd(SpriteEvaluator eval, Sprite[][] clipSprites, Shader flash, Material unlit, float heroRadiusM, Transform parent)
        {
            _eval = eval;
            _tl = eval.Timeline;
            _clipSprites = clipSprites;
            _unlit = unlit;
            _flash = new Material[FlashLevels + 1];
            for (int k = 0; k <= FlashLevels; k++)
            {
                _flash[k] = new Material(flash) { name = "Sprite Flash " + k };
                _flash[k].SetFloat("_Flash", (float)k / FlashLevels);
            }
            _root = new GameObject("Sprite Crowd");
            _root.transform.SetParent(parent, false);
            int types = _tl.TypeNames.Length;
            _shadowOf = new Sprite[types + 1];
            for (int i = 0; i < types; i++) _shadowOf[i] = Shadow(_tl.TypeRadii[i]);
            _shadowOf[types] = Shadow(heroRadiusM);
            _bolt = Bolt();
            _boltShadow = Shadow(0.2f);
        }

        /// <summary>Draws <paramref name="entities"/> (sorted by id) at song time t.</summary>
        public void Draw(List<int> entities, double t)
        {
            _used = 0;
            foreach (int e in entities)
            {
                var info = _tl.Entities[e];
                var st = _eval.Evaluate(e, t);
                int i = _used++;
                if (i == _bodies.Count) Add();
                var body = _bodies[i];
                var shadow = _shadows[i];
                var feet = Snap(new Vector2(st.Position.X, st.Position.Y));
                if (i == _sortKeys.Length)
                {
                    Array.Resize(ref _sortKeys, i * 2);
                    Array.Resize(ref _sortIndex, i * 2);
                }
                // Back to front: higher rows first, then by entity (ids are below 2^20).
                _sortKeys[i] = -(long)Mathf.RoundToInt(feet.y * CharacterAtlas.PixelsPerMetre) << 20 | (long)e;
                _sortIndex[i] = i;
                body.gameObject.SetActive(true);
                shadow.gameObject.SetActive(true);
                shadow.transform.localPosition = feet;
                if (info.Kind == EntityKind.Projectile)
                {
                    body.sprite = _bolt;
                    body.flipX = false;
                    body.color = Color.white;
                    body.sharedMaterial = _unlit;
                    body.transform.localPosition = feet;
                    shadow.sprite = _boltShadow;
                    shadow.color = Color.white;
                    continue;
                }
                var frames = _clipSprites[st.Clip];
                body.sprite = frames[Mathf.Min(st.Frame, frames.Length - 1)];
                body.flipX = st.FlipX;
                float tint = info.Kind == EntityKind.Hero ? 1f : 0.88f + 0.12f * ClipSchedule.Jitter(info.Seed, 2);
                body.color = new Color(tint, tint, tint, st.Alpha);
                body.sharedMaterial = _flash[Mathf.Clamp(Mathf.RoundToInt(st.Flash * FlashLevels), 0, FlashLevels)];
                body.transform.localPosition = feet;
                shadow.sprite = _shadowOf[info.Kind == EntityKind.Hero ? _shadowOf.Length - 1 : info.Type];
                shadow.color = new Color(1, 1, 1, st.Alpha);
            }
            Array.Sort(_sortKeys, _sortIndex, 0, _used);
            for (int rank = 0; rank < _used; rank++) _bodies[_sortIndex[rank]].sortingOrder = rank;
            for (int i = _used; i < _bodies.Count; i++)
            {
                if (!_bodies[i].gameObject.activeSelf) break;
                _bodies[i].gameObject.SetActive(false);
                _shadows[i].gameObject.SetActive(false);
            }
        }

        static Vector3 Snap(Vector2 p) =>
            new Vector3(Mathf.Round(p.x * CharacterAtlas.PixelsPerMetre), Mathf.Round(p.y * CharacterAtlas.PixelsPerMetre)) / CharacterAtlas.PixelsPerMetre;

        void Add()
        {
            var body = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            body.transform.SetParent(_root.transform, false);
            var shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(_root.transform, false);
            shadow.sharedMaterial = _unlit;
            shadow.sortingOrder = -1; // under every character
            _bodies.Add(body);
            _shadows.Add(shadow);
        }

        /// <summary>A pixel blob under a footprint of radius r: a darker core and a lighter rim.</summary>
        Sprite Shadow(float radiusM)
        {
            // Even sizes, so the centred pivot falls on a texel edge and the blob stays on the grid.
            int w = Mathf.Max(6, Mathf.RoundToInt(radiusM * 2.4f * CharacterAtlas.PixelsPerMetre)) & ~1;
            int h = Mathf.Max(4, Mathf.RoundToInt(w * 0.36f)) & ~1;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f - w / 2f) / (w / 2f), v = (y + 0.5f - h / 2f) / (h / 2f);
                float d = u * u + v * v;
                float a = d <= 0.55f ? ShadowAlpha : d <= 1f ? ShadowRimAlpha : 0f;
                px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255));
            }
            return Make(px, w, h, new Vector2(0.5f, 0.5f), "Shadow");
        }

        /// <summary>A caster's bolt: a hot core in an orange glow, pivoted 1 m under itself (on its ground position).</summary>
        Sprite Bolt()
        {
            const int s = 7;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Max(Mathf.Abs(x - 3), Mathf.Abs(y - 3)) + 0.5f * Mathf.Min(Mathf.Abs(x - 3), Mathf.Abs(y - 3));
                px[y * s + x] = d < 1.2f ? new Color32(255, 250, 220, 255) : d < 2.4f ? new Color32(255, 170, 60, 255) : d < 3.2f ? new Color32(220, 80, 30, 160) : new Color32(0, 0, 0, 0);
            }
            return Make(px, s, s, new Vector2(3f / s, -ProjectileHeightM * CharacterAtlas.PixelsPerMetre / s), "Bolt");
        }

        Sprite Make(Color32[] px, int w, int h, Vector2 pivot, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _made.Add(tex);
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, CharacterAtlas.PixelsPerMetre, 0, SpriteMeshType.FullRect);
        }

        public void Dispose()
        {
            SongPlayback.Destroy(_root);
            foreach (var m in _flash) SongPlayback.Destroy(m);
            foreach (var t in _made) SongPlayback.Destroy(t);
        }
    }
}
