using System;
using System.Collections.Generic;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// One type's baked atlas (PX3's SpriteBaker: `Assets/Baked/Characters/&lt;type&gt;.png` and its
    /// JSON): each clip's frames as Sprites pivoted at the clip's feet, at 32 grid pixels per metre.
    /// </summary>
    public sealed class CharacterAtlas
    {
        public const int PixelsPerMetre = 32;

        /// <summary>One baked frame: its crop's rectangle in the atlas and the crop's offset in the clip's frame.</summary>
        public readonly struct Crop
        {
            public readonly RectInt Rect;
            public readonly Vector2Int Offset;
            public Crop(RectInt rect, Vector2Int offset) { Rect = rect; Offset = offset; }
        }

        public readonly string Type;
        public readonly Texture2D Texture;
        public readonly int FrameRate;
        readonly Dictionary<string, Sprite[]> _clips = new Dictionary<string, Sprite[]>(StringComparer.Ordinal);

        public CharacterAtlas(Texture2D texture, string json)
        {
            var j = JsonValue.Parse(json);
            Type = j["type"].AsString();
            Texture = texture;
            FrameRate = j["frameRate"].AsInt(15);
            foreach (var (name, c) in j["clips"].Members)
            {
                var pivot = new Vector2Int(c["pivotPx"][0].AsInt(), c["pivotPx"][1].AsInt());
                var frames = c["frames"].Items;
                var sprites = new Sprite[frames.Count];
                for (int i = 0; i < sprites.Length; i++)
                {
                    var f = frames[i];
                    var crop = new Crop(new RectInt(f[0].AsInt(), f[1].AsInt(), f[2].AsInt(), f[3].AsInt()), new Vector2Int(f[4].AsInt(), f[5].AsInt()));
                    sprites[i] = Sprite(texture, crop, pivot);
                    sprites[i].name = $"{Type}-{name}_{i}";
                }
                _clips[name] = sprites;
            }
        }

        public IEnumerable<string> ClipNames => _clips.Keys;

        /// <summary>A clip's frames by its pack name ("walk", "skill_1"), or null.</summary>
        public Sprite[] Clip(string name) => _clips.TryGetValue(name, out var s) ? s : null;

        /// <summary>
        /// The frames of a clip library entry, whose `clip` is the pack's sequence
        /// (`&lt;prefix&gt;-&lt;name&gt;`): the atlas clip it ends with.
        /// </summary>
        public Sprite[] ForLibraryClip(string libraryClip)
        {
            foreach (var kv in _clips)
                if (libraryClip.EndsWith("-" + kv.Key, StringComparison.Ordinal)) return kv.Value;
            return null;
        }

        /// <summary>A Sprite for one baked frame, pivoted at its clip's feet.</summary>
        public static Sprite Sprite(Texture2D atlas, Crop crop, Vector2Int pivot)
        {
            var r = crop.Rect;
            var at = pivot - crop.Offset;
            return UnityEngine.Sprite.Create(atlas, new Rect(r.x, r.y, r.width, r.height),
                new Vector2((float)at.x / r.width, (float)at.y / r.height), PixelsPerMetre, 0, SpriteMeshType.FullRect);
        }
    }
}
