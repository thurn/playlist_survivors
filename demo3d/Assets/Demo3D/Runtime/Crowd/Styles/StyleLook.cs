using System.Collections.Generic;
using UnityEngine;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// Each roster type's palette for the `--monster-style` treatments (crowd.md, Monster styles),
    /// copied from stylelab's Looks.cs and keyed by roster type id; a type without one (evilgod)
    /// takes the skeleton's. Colours are written as sRGB hex and converted to linear; HDR colours
    /// are linear values times an intensity, set on materials as vectors so Unity doesn't
    /// gamma-convert them a second time.
    /// </summary>
    public sealed class StyleLook
    {
        public Color[] ramp;       // painted: the colour strip, dark to light
        public Color shadow;       // painted: the shadow side's tint (multiplies the strip)
        public Color rim;          // painted: the complementary rim (HDR)
        public Color spot;         // painted: glow spots (HDR)
        public float spotAmount = 0.7f;
        public Color gem;          // crystal
        public Color ember, flame, core; // fire (HDR)
        public float synthHue;     // spectrum
        public Color holo;         // hologram (HDR)
        public Color chrome;       // chrome tint
        public Color voidRim;      // void edge (HDR)
        public Color nebulaA, nebulaB;
        public Color particle;     // the treatment-neutral burst colour (HDR)

        public static StyleLook Of(string id) => All.TryGetValue(id, out var l) ? l : All["skeleton"];

        static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c.linear : Color.magenta;
        static Color H(string hex, float intensity) { var c = C(hex) * intensity; c.a = 1; return c; }
        static Color[] Ramp(params string[] hex) => System.Array.ConvertAll(hex, C);

        static readonly Dictionary<string, StyleLook> All = new Dictionary<string, StyleLook>
        {
            ["skeleton"] = new StyleLook
            {
                ramp = Ramp("#1b0f3a", "#2f6f9e", "#7fe3d9", "#f2fff8"), shadow = C("#5a46c8"),
                rim = H("#ff8a3d", 1.6f), spot = H("#5cf2ff", 2.5f), spotAmount = 0.72f,
                gem = C("#8be9ff"), ember = H("#0a2a8a", 0.8f), flame = H("#2e8bff", 3f), core = H("#c9f4ff", 6f),
                synthHue = 0.5f, holo = H("#33d6ff", 1.6f), chrome = C("#e8eef5"),
                voidRim = H("#7a4dff", 3f), nebulaA = C("#3d0a66"), nebulaB = C("#006b80"), particle = H("#7fe3d9", 2.5f),
            },
            ["goblin"] = new StyleLook
            {
                ramp = Ramp("#0f2407", "#3f8f12", "#b6f03c", "#fbffcf"), shadow = C("#2f8a6a"),
                rim = H("#ff4fa3", 1.5f), spot = H("#ffb627", 2.5f),
                gem = C("#6dff8a"), ember = H("#0b3a05", 0.8f), flame = H("#3dff2e", 3f), core = H("#e8ffc4", 6f),
                synthHue = 0.25f, holo = H("#7dff6a", 1.5f), chrome = C("#ffd27a"),
                voidRim = H("#3dff9e", 3f), nebulaA = C("#0a4a2a"), nebulaB = C("#4a5a00"), particle = H("#b6f03c", 2.5f),
            },
            ["slime"] = new StyleLook
            {
                ramp = Ramp("#2a0630", "#b3197a", "#ff6fc4", "#ffe3f6"), shadow = C("#6a2ab4"),
                rim = H("#3df5ff", 1.6f), spot = H("#ffffff", 2f), spotAmount = 0.75f,
                gem = C("#ff5cbf"), ember = H("#4a0030", 0.8f), flame = H("#ff2ea6", 3f), core = H("#ffe0f4", 6f),
                synthHue = 0.85f, holo = H("#ff4fd8", 1.6f), chrome = C("#ffb3c6"),
                voidRim = H("#ff3dc8", 3f), nebulaA = C("#5a0a4a"), nebulaB = C("#1a0a6a"), particle = H("#ff6fc4", 2.5f),
            },
            ["bat"] = new StyleLook
            {
                ramp = Ramp("#0e0624", "#4a2a9e", "#a98bff", "#efe8ff"), shadow = C("#3a2aa0"),
                rim = H("#b6ff3d", 1.5f), spot = H("#ff3df0", 2.5f),
                gem = C("#a77bff"), ember = H("#2a0a5a", 0.8f), flame = H("#b04dff", 3f), core = H("#f2dcff", 6f),
                synthHue = 0.7f, holo = H("#a066ff", 1.6f), chrome = C("#9fb7ff"),
                voidRim = H("#9d5cff", 3f), nebulaA = C("#2a0a5a"), nebulaB = C("#0a2a6a"), particle = H("#a98bff", 2.5f),
            },
            ["mushroom"] = new StyleLook
            {
                ramp = Ramp("#2a0702", "#c2380f", "#ff9a3d", "#fff0c9"), shadow = C("#a0285a"),
                rim = H("#3dfff0", 1.4f), spot = H("#3dffd0", 3f), spotAmount = 0.66f,
                gem = C("#ffb23d"), ember = H("#5a0e00", 0.8f), flame = H("#ff6a00", 3f), core = H("#fff2c4", 6f),
                synthHue = 0.08f, holo = H("#ff9a3d", 1.5f), chrome = C("#ff9f6b"),
                voidRim = H("#ff8a3d", 3f), nebulaA = C("#5a1a0a"), nebulaB = C("#0a4a4a"), particle = H("#ff9a3d", 2.5f),
            },
            ["demon"] = new StyleLook
            {
                ramp = Ramp("#1a0204", "#a3101c", "#ff5a3d", "#ffd38a"), shadow = C("#7a1460"),
                rim = H("#ffd23d", 1.6f), spot = H("#ff6a00", 3f),
                gem = C("#ff3d3d"), ember = H("#4a0000", 0.8f), flame = H("#ff2a00", 3.2f), core = H("#ffd9a0", 6f),
                synthHue = 0.0f, holo = H("#ff3d3d", 1.6f), chrome = C("#d8c2a8"),
                voidRim = H("#ff2a2a", 3f), nebulaA = C("#4a0010"), nebulaB = C("#3a0a4a"), particle = H("#ff5a3d", 2.5f),
            },
            ["bigork"] = new StyleLook
            {
                ramp = Ramp("#07201a", "#127a5a", "#5cf0b0", "#e8fff4"), shadow = C("#1f5a8a"),
                rim = H("#ff7a2e", 1.5f), spot = H("#d6ff3d", 2.5f),
                gem = C("#3dffc8"), ember = H("#00402a", 0.8f), flame = H("#1affb0", 3f), core = H("#e0fff4", 6f),
                synthHue = 0.42f, holo = H("#3dffc0", 1.5f), chrome = C("#ffcf6b"),
                voidRim = H("#3dffd0", 3f), nebulaA = C("#003a2a"), nebulaB = C("#1a0a5a"), particle = H("#5cf0b0", 2.5f),
            },
            ["troll"] = new StyleLook
            {
                ramp = Ramp("#050d2e", "#1f4fcf", "#6fb6ff", "#e6f4ff"), shadow = C("#4a2aa0"),
                rim = H("#ff5cc8", 1.5f), spot = H("#3dd6ff", 2.5f),
                gem = C("#4d8bff"), ember = H("#001a4a", 0.8f), flame = H("#3d9eff", 3f), core = H("#e6f7ff", 6f),
                synthHue = 0.6f, holo = H("#4d9eff", 1.6f), chrome = C("#b8c8ff"),
                voidRim = H("#4d6bff", 3.5f), nebulaA = C("#0a1a5a"), nebulaB = C("#3a0a5a"), particle = H("#6fb6ff", 2.5f),
            },
            ["dragon"] = new StyleLook
            {
                ramp = Ramp("#260b00", "#c45a0a", "#ffc23d", "#fff6d6"), shadow = C("#a02a4a"),
                rim = H("#a85cff", 1.6f), spot = H("#ffef5c", 3f),
                gem = C("#ffd23d"), ember = H("#5a1000", 0.8f), flame = H("#ff7a10", 3.2f), core = H("#fff4c9", 6.5f),
                synthHue = 0.12f, holo = H("#ffc23d", 1.5f), chrome = C("#ffd890"),
                voidRim = H("#ffb03d", 3.5f), nebulaA = C("#5a2a00"), nebulaB = C("#4a0a3a"), particle = H("#ffc23d", 2.5f),
            },
            ["hero"] = new StyleLook
            {
                ramp = Ramp("#1a120c", "#7a5232", "#e8a060", "#fff4e6"), shadow = C("#5a4a8a"),
                rim = H("#ffe0a0", 1.1f), spot = H("#ffb060", 0f), spotAmount = 2f,
                gem = C("#ffffff"), ember = H("#5a1000", 0.8f), flame = H("#ff7a10", 3f), core = H("#fff4c9", 6f),
                synthHue = 0.1f, holo = H("#ffffff", 1f), chrome = C("#ffffff"),
                voidRim = H("#ffffff", 2f), nebulaA = C("#000000"), nebulaB = C("#000000"), particle = H("#ffd0a0", 2.5f),
            },
        };
    }
}
