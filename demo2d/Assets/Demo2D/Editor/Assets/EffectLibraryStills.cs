using System.IO;
using Demo2D.Choreography;
using Demo2D.Runtime.Playback;
using UnityEditor;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX7's effect library, one still per entry (`library-&lt;name&gt;.jpg`): the entry as the
    /// player sets it up (EffectLayer.Make, its scale at the entry's own event scale, its sorting)
    /// at three ages, over forest ground under the moon with its light cue at that age and the
    /// knight at game height for scale; a flight crosses its panel at 10 m/s. Kept at the grid's size.
    /// </summary>
    public static class EffectLibraryStills
    {
        const int PanelW = 15, PanelH = 9;

        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            var library = EffectLibrary.Parse(File.ReadAllText(CharacterPacks.DataDir + "/" + EffectLibrary.File));
            var catalog = ForestTiles.Load();
            var knight = CharacterPacks.Load()[0];
            var hero = EffectGallery.HeroSprite(knight.Frames()["idle_1"][0], knight.GameHeightPx);
            var glow = AssetDatabase.LoadAssetAtPath<Shader>(PlaybackBaker.GlowPath);
            foreach (var spec in library.All())
            {
                bool flight = library.Flights.ContainsKey(spec.Name);
                double[] ages = spec.Sweep ? new[] { 0.2, 0.7, 1.2 } : flight ? new[] { 0.1, 0.3, 0.6 } : new[] { 0.06, 0.2, 0.45 };
                var root = new GameObject("Library Still");
                var holder = new GameObject("Holder");
                holder.SetActive(false);
                try
                {
                    var world = new RectInt(0, 0, PanelW * ages.Length, PanelH);
                    var ground = EffectGallery.Ground(catalog, root.transform, world);
                    foreach (var r in ground.GetComponentsInChildren<SpriteRenderer>()) r.sortingOrder = -2000;
                    PixelShots.GlobalLight(root.transform, true);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectGallery.VendorPath(spec.Prefab))
                                 ?? throw new FileNotFoundException("EffectLibraryStills: no prefab " + spec.Prefab);
                    for (int i = 0; i < ages.Length; i++)
                    {
                        var foot = new Vector2(i * PanelW + PanelW / 2f, 2.5f);
                        var h = new GameObject("Knight").AddComponent<SpriteRenderer>();
                        h.transform.SetParent(root.transform, false);
                        h.transform.localPosition = foot + (spec.Sweep ? Vector2.zero : new Vector2(-2.5f, 0));
                        h.sprite = hero;
                        h.sharedMaterial = PixelShots.SpriteLit;
                        var inst = EffectLayer.Make(prefab, spec, holder.transform, root.transform, glow);
                        inst.Go.SetActive(true);
                        int order = spec.Ground ? EffectLayer.GroundOrder : EffectLayer.OverOrder;
                        for (int r = 0; r < inst.Renderers.Length; r++) inst.Renderers[r].sortingOrder = order + inst.AuthoredOrder[r];
                        if (inst.Beam != null) { inst.Beam[0].sortingOrder = order + 6; inst.Beam[1].sortingOrder = order + 7; }
                        // As the player places them: ground effects at the feet, the rest a metre up; the beam along +x from the hero.
                        var at = spec.Ground ? foot : foot + new Vector2(0, 1f);
                        // The beam sweeps from east, a turn per 1.55 s (the climax's bar), half its length to fit the panel.
                        float yaw = Mathf.PI / 2 + 2 * Mathf.PI * (float)ages[i] / 1.55f;
                        var aim = spec.Aim == EffectAim.Yaw ? Quaternion.Euler(0, 0, Mathf.Atan2(Mathf.Cos(yaw), Mathf.Sin(yaw)) * Mathf.Rad2Deg - 90) : Quaternion.identity;
                        inst.T.localRotation = aim * inst.AuthoredRotation;
                        inst.T.localScale = inst.AuthoredScale * spec.Scale;
                        float age = (float)ages[i];
                        EffectLayer.SimulateTo(inst, 1234, age, tau =>
                            inst.T.localPosition = flight ? (Vector3)(at + new Vector2((float)(10 * (tau - age)), 0)) : (Vector3)at);
                        if (spec.Beam != null)
                            EffectLayer.DrawBeam(inst, spec, at, yaw, spec.Beam.LengthM * 0.5f, 1f, 1234, age);
                        if (spec.Light != null)
                            PixelShots.PointLight(root.transform, at, new Color(spec.Light.R, spec.Light.G, spec.Light.B), spec.Light.RadiusM, spec.Light.At(age));
                    }
                    var px = PixelShots.Render(world, PixelShots.NightSky, hdr: true);
                    int w = world.width * PixelShots.PixelsPerMetre, hgt = world.height * PixelShots.PixelsPerMetre;
                    var white = new Color32(235, 235, 235, 255);
                    var label = $"px7 effect library: {spec.Name} ({EffectGallery.Short(spec.Prefab)}, scale {spec.Scale:0.##}{(spec.Tint != null ? ", tinted" : "")}, {(spec.Ground ? "ground" : "over")})";
                    PixelText.Draw(px, w, hgt, 3, hgt - 3, label, white, 1);
                    for (int i = 0; i < ages.Length; i++)
                        PixelText.Draw(px, w, hgt, i * PanelW * PixelShots.PixelsPerMetre + 3, hgt - 12, $"{ages[i]:0.##} s", white, 1);
                    var tex = PixelShots.Crop(px, w, new RectInt(0, 0, w, hgt));
                    var path = Path.Combine(dir, $"library-{spec.Name}.jpg");
                    Runtime.RenderTag.Save(tex, path, "PX7 effect library " + spec.Name);
                    Object.DestroyImmediate(tex);
                    Debug.Log("EffectLibraryStills: wrote " + path);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Object.DestroyImmediate(holder);
                }
            }
        }
    }
}
