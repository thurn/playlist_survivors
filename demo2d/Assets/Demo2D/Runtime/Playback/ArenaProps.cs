using System;
using System.Collections.Generic;
using Demo2D.Choreography;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The arena's props (PX6): one lit SpriteRenderer per placed tree, stump, branch and plant, drawn
    /// in the crowd's back-to-front order by its feet (<see cref="SpriteCrowd"/> ranks them with the
    /// characters). Swaying trees step through their frames; props off the view are switched off; and
    /// a tall prop fades where a character behind it would be hidden (demo3d's canopy fade). Every
    /// frame is a function of the song time and the characters' positions, so a seek draws the same.
    /// </summary>
    public sealed class ArenaProps : IDisposable
    {
        /// <summary>A prop at least this tall fades over a character behind it.</summary>
        const float FadeHeightM = 2.5f;
        /// <summary>The faded alpha, reached once a character is this far inside the cover.</summary>
        const float FadedAlpha = 0.45f, FadeDepthM = 0.6f;
        /// <summary>A character's body point, over its feet.</summary>
        const float BodyM = 0.8f;
        /// <summary>The cover starts this far over the prop's feet (its base never hides anyone).</summary>
        const float CoverBaseM = 0.6f;

        readonly GameObject _root;
        readonly Prop[] _props;
        readonly List<Sprite> _made = new List<Sprite>();
        readonly List<int> _visible = new List<int>();

        sealed class Prop
        {
            public SpriteRenderer Renderer;
            public Sprite[] Frames;
            public float Fps, Phase;
            public Vector2 Feet;
            public Rect Bounds, Cover;
            public bool Fades;
            public int Row;
        }

        /// <summary>The props drawn this frame (indices for <see cref="Renderer"/> and <see cref="SortRow"/>).</summary>
        public List<int> Visible => _visible;
        public SpriteRenderer Renderer(int prop) => _props[prop].Renderer;
        /// <summary>The prop's feet in texel rows, as the crowd sorts.</summary>
        public int SortRow(int prop) => _props[prop].Row;
        public int Count => _props.Length;

        /// <param name="sheets">The props' textures, by name (forest-props.json's "sheet").</param>
        public ArenaProps(ForestArena arena, IReadOnlyList<Texture2D> sheets, Material lit, Transform parent)
        {
            _root = new GameObject("Arena Props");
            _root.transform.SetParent(parent, false);
            var bySheet = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (var t in sheets) bySheet[t.name] = t;
            var frames = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
            var list = arena.Placements;
            _props = new Prop[arena.TerrainStart];
            for (int i = 0; i < _props.Length; i++)
            {
                var pl = list.Items[i];
                var s = arena.Sprites[list.Props[pl.Prop].Id];
                if (!frames.TryGetValue(s.Id, out var f))
                {
                    if (!bySheet.TryGetValue(s.Sheet, out var tex)) throw new InvalidOperationException("ArenaProps: no texture " + s.Sheet + " (run demo2d unity BakePlayback)");
                    f = Array.ConvertAll(s.Frames, r => MakeSprite(tex, r, s));
                    frames[s.Id] = f;
                }
                var feet = Snap(new Vector2(pl.Position.X, pl.Position.Y));
                float ppm = CharacterAtlas.PixelsPerMetre;
                var bounds = new Rect(feet.x - s.PivotX / ppm, feet.y - s.PivotY / ppm, s.W / ppm, s.H / ppm);
                var r = new GameObject(s.Id).AddComponent<SpriteRenderer>();
                r.transform.SetParent(_root.transform, false);
                r.transform.localPosition = feet;
                r.sharedMaterial = lit;
                r.sprite = f[0];
                r.enabled = false;
                _props[i] = new Prop
                {
                    Renderer = r, Frames = f, Fps = s.Fps, Feet = feet, Bounds = bounds,
                    Phase = (Rng.Hash(arena.Layout.Seed, (uint)i, 0x5A7A) >> 8) * (1f / (1 << 24)),
                    Fades = s.HeightM >= FadeHeightM,
                    Cover = Rect.MinMaxRect(bounds.xMin, feet.y + CoverBaseM, bounds.xMax, bounds.yMax),
                    Row = Mathf.RoundToInt(feet.y * ppm),
                };
            }
        }

        Sprite MakeSprite(Texture2D tex, int[] r, PropSprite s)
        {
            // The catalogue's rectangles count rows from the top; the texture's from the bottom.
            var rect = new Rect(r[0], tex.height - r[1] - r[3], r[2], r[3]);
            var sprite = Sprite.Create(tex, rect, new Vector2(s.PivotX / (float)r[2], s.PivotY / (float)r[3]), CharacterAtlas.PixelsPerMetre, 0, SpriteMeshType.FullRect);
            sprite.name = s.Id;
            _made.Add(sprite);
            return sprite;
        }

        static Vector2 Snap(Vector2 p) =>
            new Vector2(Mathf.Round(p.x * CharacterAtlas.PixelsPerMetre), Mathf.Round(p.y * CharacterAtlas.PixelsPerMetre)) / CharacterAtlas.PixelsPerMetre;

        /// <summary>
        /// Draws song time t: the props overlapping <paramref name="view"/>, swaying, each tall one faded
        /// by how deep inside it the body of a character standing behind it lies.
        /// </summary>
        /// <param name="feet">The characters' feet this frame (the hero and the horde).</param>
        public void Draw(double t, Rect view, List<Vector2> feet)
        {
            foreach (int i in _visible) _props[i].Renderer.enabled = false;
            _visible.Clear();
            for (int i = 0; i < _props.Length; i++)
            {
                var p = _props[i];
                if (!p.Bounds.Overlaps(view)) continue;
                _visible.Add(i);
                p.Renderer.enabled = true;
                if (p.Frames.Length > 1)
                {
                    int n = p.Frames.Length;
                    int k = (int)Math.Floor(t * p.Fps + p.Phase * n) % n;
                    p.Renderer.sprite = p.Frames[k < 0 ? k + n : k];
                }
                float alpha = 1;
                if (p.Fades)
                {
                    float deepest = 0;
                    foreach (var f in feet)
                    {
                        if (f.y <= p.Feet.y) continue;   // in front of the prop
                        var b = new Vector2(f.x, f.y + BodyM);
                        if (!p.Cover.Contains(b)) continue;
                        float depth = Mathf.Min(Mathf.Min(b.x - p.Cover.xMin, p.Cover.xMax - b.x), Mathf.Min(b.y - p.Cover.yMin, p.Cover.yMax - b.y));
                        deepest = Mathf.Max(deepest, depth);
                    }
                    alpha = Mathf.Lerp(1, FadedAlpha, Mathf.Clamp01(deepest / FadeDepthM));
                }
                p.Renderer.color = new Color(1, 1, 1, alpha);
            }
        }

        public void Dispose()
        {
            SongPlayback.Destroy(_root);
            foreach (var s in _made) SongPlayback.Destroy(s);
        }
    }
}
