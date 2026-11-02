using System.Runtime.InteropServices;
using UnityEngine;

namespace Champions.Render.Structures
{
    /// <summary>
    /// One drawn structure, or one break's shards (StructureCommon.hlsl's StructureInstance, 32 bytes): where it stands,
    /// its angle (the sim's: radians from +x toward +z seen from above), its latest flash and its pieces.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StructureInstance
    {
        public Vector3 Position;
        public float Angle;
        /// <summary>The song time of its latest hit (a break's, for shards), and that flash's strength (0 none).</summary>
        public float FlashAt, Flash;
        /// <summary>A break's first piece in the shatter's pieces buffer (0 standing: a standing body has no pieces).</summary>
        public uint PieceBase;
        /// <summary>1 standing; shards fade as they sink.</summary>
        public float Fade;

        public const int Stride = 32;
    }

    /// <summary>
    /// The structures' edge look (design.md, The World &gt; Structures: "the edge look over a dim fill in the zone's key
    /// colour, so they read as solid shapes, clearly apart from the monsters' dark bodies"): the monsters' feature lines
    /// (MonsterLines.hlsl, the same width in the trailer's capture pixels) in the zone's edge colour over the zone's dim
    /// fill, which keeps its calibrated light through the grade as the floor does (style.md, Zones: fill L* 10-25). Both
    /// looks draw with these; `--look trailer` differs only in drawing the vendor meshes unsimplified. Judged by eye.
    /// </summary>
    public struct StructureGains
    {
        /// <summary>The fill: its gain over the zone's fill colour, flat (a lit facet would read as a patch, not a solid).</summary>
        public float Fill;
        /// <summary>The lines: their gain over the zone's edge colour, width (px at 2160 rows), lift as the beat wave passes.</summary>
        public float Edge, EdgePx, Beat;
        /// <summary>A hit's or a break's flash: how far it lifts the lines and the fill toward the edge colour.</summary>
        public float Flash;
        /// <summary>The shards' lines: their gain, and their lift on the break's frame (fading as the monsters' do).</summary>
        public float ShardEdge, ShardFresh;

        public static StructureGains Default => new StructureGains
        {
            Fill = 0.5f,
            Edge = 3f, EdgePx = 1.25f, Beat = 0.6f,
            Flash = 1.2f,
            ShardEdge = 3f, ShardFresh = 2.5f,
        };

        /// <summary>A new material in <paramref name="shader"/> for type <paramref name="id"/>: the bodies' (or, with <paramref name="shards"/>, the shards') gains.</summary>
        public Material Material(Shader shader, string id, bool shards)
        {
            var m = new Material(shader) { name = id + (shards ? "_structure_shards" : "_structure"), enableInstancing = false };
            m.SetFloat("_Fill", Fill);
            m.SetFloat("_Edge", shards ? ShardEdge : Edge);
            m.SetFloat("_EdgePx", EdgePx);
            m.SetFloat("_Beat", shards ? ShardFresh : Beat);
            m.SetFloat("_Flash", Flash);
            return m;
        }
    }
}
