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
        /// PX6's arena: the 2D kit and the tile sheet's catalogue (the layout is among
        /// <see cref="layouts"/>), the props' textures by name, and the MainLev sheet's sliced tiles
        /// with each one's cell (column, row from the sheet's top). The client composes the Tilemaps
        /// from these at load, as the sim generates its placements, so nothing derived is baked.
        /// </summary>
        public TextAsset forestProps, forestTiles;
        public Texture2D[] propSheets;
        public Sprite[] sheetSprites;
        public Vector2Int[] sheetCells;

        /// <summary>
        /// PX7's effect library and night (Data/effects.json, night-lighting.json), the library's
        /// CFXR prefabs (vendor) by its paths, and the fireflies' glow shader.
        /// </summary>
        public TextAsset effects, night;
        public string[] effectPaths;
        public GameObject[] effectPrefabs;
        public Shader glow;

        public static PlaybackData Load() =>
            Resources.Load<PlaybackData>(ResourcePath)
            ?? throw new InvalidOperationException("PlaybackData: not baked (run demo2d unity BakeSprites, then BakePlayback)");

        public TextAsset Layout(string name) => Array.Find(layouts, l => l.name == name + "-layout");
    }
}
