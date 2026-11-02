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
        /// <summary>Its first piece in the pieces buffer: 0, the identity block, standing.</summary>
        public uint PieceBase;
        /// <summary>1 standing; shards fade as they sink.</summary>
        public float Fade;

        public const int Stride = 32;
    }

    /// <summary>
    /// The structures' look inputs (StructureCommon.hlsl's globals): the fill's and edges' gains over the zone's colours
    /// (docs/champions/style.md: fill L* 10-25, edges 40-60, which the palette's zones keep), the beat wave's flare on the
    /// edges, a flash's lift, and the lines' width and lift.
    /// </summary>
    public struct StructureLook
    {
        public float FillGain, EdgeGain, Flare, FlashLift;
        public float LineWidthPx, LiftM;

        public static StructureLook Default => new StructureLook
        {
            FillGain = 1f,
            EdgeGain = 1f,
            Flare = 0.8f,
            FlashLift = 1.2f,
            LineWidthPx = 1.6f,
            LiftM = 0.05f,
        };

        static readonly int IdLook = Shader.PropertyToID("_StructureLook");
        static readonly int IdLines = Shader.PropertyToID("_StructureLines");

        public void Apply()
        {
            Shader.SetGlobalVector(IdLook, new Vector4(FillGain, EdgeGain, Flare, FlashLift));
            Shader.SetGlobalVector(IdLines, new Vector4(LineWidthPx, LiftM, 0, 0));
        }
    }
}
