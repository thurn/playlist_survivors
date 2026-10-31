using System;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Demo3D.Runtime.Crowd
{
    /// <summary>
    /// The hero's glowing outline (crowd.md, The Hero; ps-b5z.1): a thin HDR line round the hero's
    /// silhouette that blooms into a halo, so a hero drawn at the humanoid monsters' size still reads
    /// among them at a trailer's wide views. An intensity's feel turns it on (BlowFeel.Amounts.HeroOutline).
    ///
    /// Each of the hero's renderers (its body and shield) draws its mesh a second time with
    /// HeroOutline.shader, an inverted hull, as an extra material. The vendor meshes aren't readable,
    /// so the hull pushes along their own normals; their few hard edges split it into inner lines,
    /// which show only at close views (the duel's 20 m).
    /// </summary>
    public sealed class HeroOutline : IDisposable
    {
        /// <summary>`--hero-outline px[,r,g,b]`: the line's width in pixels at 1080p (0: none) and its HDR
        /// colour, over a trailer look's (TrailerStyle.HeroOutline, in the same form), the intensity's
        /// width and the shader's cyan.</summary>
        public const string Flag = "--hero-outline";

        static readonly int WidthId = Shader.PropertyToID("_WidthPx"), ColorId = Shader.PropertyToID("_OutlineColor");

        readonly Material _material;

        /// <summary>The outline at <paramref name="widthPx"/> pixels, or as `--hero-outline` or else
        /// <paramref name="look"/> names it; null at 0 pixels.</summary>
        public static HeroOutline Create(CrowdLibrary lib, float widthPx, string look)
        {
            Color? color = null;
            if ((Args.Get(Flag) ?? look) is string spec)
            {
                var v = Array.ConvertAll(spec.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
                if (v.Length != 1 && v.Length != 4) throw new ArgumentException($"{Flag}: px or px,r,g,b, not '{spec}'");
                widthPx = v[0];
                if (v.Length == 4) color = new Color(v[1], v[2], v[3]);
                Debug.Log($"HeroOutline: {spec}");
            }
            return widthPx > 0 ? new HeroOutline(lib, widthPx, color) : null;
        }

        HeroOutline(CrowdLibrary lib, float widthPx, Color? color)
        {
            _material = new Material(lib.StyleShader("Demo3D/Styles/Shells/HeroOutline")) { name = "hero_outline", enableInstancing = false };
            _material.SetFloat(WidthId, widthPx);
            if (color is Color c) _material.SetColor(ColorId, c);
        }

        /// <summary>Outlines the hero's renderers (StockCrowd.Created, before HeroBlade.Attach, which turns
        /// the broadsword's renderer off, outline and all, and whose glowing blade needs none).</summary>
        public void Attach(GameObject hero)
        {
            int n = 0;
            foreach (var r in hero.GetComponentsInChildren<Renderer>())
            {
                // A material past the mesh's submeshes draws its last submesh again (the models have one).
                var materials = r.sharedMaterials;
                Array.Resize(ref materials, materials.Length + 1);
                materials[materials.Length - 1] = _material;
                r.sharedMaterials = materials;
                n++;
            }
            Debug.Log($"HeroOutline: {n} renderers outlined at {_material.GetFloat(WidthId):F1} px");
        }

        public void Dispose() => Object.Destroy(_material);
    }
}
