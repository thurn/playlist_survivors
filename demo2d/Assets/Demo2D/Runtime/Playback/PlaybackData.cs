using System;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// What the player builds and draws its timeline from, baked (`demo2d unity BakePlayback`, and
    /// by every build) into Assets/Baked/Playback/Resources/, since it references the baked song,
    /// the baked atlases and vendor tiles.
    /// </summary>
    public sealed class PlaybackData : ScriptableObject
    {
        public const string ResourcePath = "Demo2DPlaybackData";

        public TextAsset songMap;
        public AudioClip music;
        public TextAsset level, annotations, roster;
        public TextAsset[] layouts;

        /// <summary>The baked atlases and their JSON, by type ("knight" is the hero).</summary>
        public Texture2D[] atlases;
        public TextAsset[] atlasJson;

        public Shader spriteFlash, upscale;
        public Material spriteLit, spriteUnlit;

        /// <summary>
        /// The open ground (until PX6's arena): a grid of tiles from the forest sheet, row 0 at
        /// the bottom, its cell (0, 0) at <see cref="groundOrigin"/>; indices into
        /// <see cref="groundSprites"/>, -1 for no overlay.
        /// </summary>
        public Sprite[] groundSprites;
        public int groundW, groundH;
        public Vector2Int groundOrigin;
        public int[] groundBase, groundOverlay;

        public static PlaybackData Load() =>
            Resources.Load<PlaybackData>(ResourcePath)
            ?? throw new InvalidOperationException("PlaybackData: not baked (run demo2d unity BakeSprites, then BakePlayback)");

        public TextAsset Layout(string name) => Array.Find(layouts, l => l.name == name + "-layout");
    }
}
