using UnityEngine;

namespace Champions.Render.Crowd
{
    /// <summary>
    /// The edge cuts' look inputs (EdgeCommon.hlsl's globals): the lines' gain, width and lift, the body's colour,
    /// the importance fade, and the beat wave the world drives. The defaults are this bead's starting values; the
    /// palette and the style guide (docs/champions/style.md) tune them.
    /// </summary>
    public struct EdgeLook
    {
        /// <summary>The lines' colour is the monster's tint times this (linear, HDR).</summary>
        public float LineGain;
        /// <summary>Line width in pixels at 1080 rows.</summary>
        public float LineWidthPx;
        /// <summary>How far the lines are drawn toward the camera, in metres at scale 1, so the body never covers them.</summary>
        public float LiftM;
        /// <summary>The body's colour (linear) and how far a hit lifts it toward the tint.</summary>
        public Color Body;
        public float BodyHitLift;
        /// <summary>From this height on screen (pixels at 1080 rows) every line shows; at <see cref="FadeMinPx"/> only
        /// those of importance <see cref="FadeImportance"/> and up.</summary>
        public float FadeFullPx, FadeMinPx, FadeImportance;

        public static EdgeLook Default => new EdgeLook
        {
            LineGain = 0.85f,
            LineWidthPx = 1.6f,
            LiftM = 0.05f,
            Body = new Color(0.004f, 0.004f, 0.006f),
            BodyHitLift = 0.15f,
            FadeFullPx = 180f,
            FadeMinPx = 45f,
            FadeImportance = 0.6f,
        };

        static readonly int IdLines = Shader.PropertyToID("_EdgeLines");
        static readonly int IdBody = Shader.PropertyToID("_EdgeBody");
        static readonly int IdFade = Shader.PropertyToID("_EdgeFade");
        static readonly int IdWave = Shader.PropertyToID("_EdgeWave");
        static readonly int IdWaveWidth = Shader.PropertyToID("_EdgeWaveWidth");
        static readonly int IdMask = Shader.PropertyToID("_EdgeIdMask");

        public void Apply()
        {
            Shader.SetGlobalVector(IdLines, new Vector4(LineGain, LineWidthPx, LiftM, 0));
            Shader.SetGlobalVector(IdBody, new Vector4(Body.r, Body.g, Body.b, BodyHitLift));
            Shader.SetGlobalVector(IdFade, new Vector4(FadeFullPx, FadeMinPx, FadeImportance, 0));
        }

        /// <summary>
        /// The beat wave at this frame: a ring round (x, z) on the ground at `radius` metres, `width` metres wide,
        /// flaring the lines it passes by `strength` (0 for none).
        /// </summary>
        public static void Wave(Vector2 centre, float radius, float width, float strength)
        {
            Shader.SetGlobalVector(IdWave, new Vector4(centre.x, centre.y, radius, strength));
            Shader.SetGlobalFloat(IdWaveWidth, width);
        }

        /// <summary>The stills' mask mode: every edge cut flat white (black share's silhouette).</summary>
        public static void Mask(bool on) => Shader.SetGlobalFloat(IdMask, on ? 1 : 0);
    }
}
