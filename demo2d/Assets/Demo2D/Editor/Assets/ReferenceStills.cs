using System;
using System.IO;
using System.Linq;
using Demo2D.Runtime;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX9's reference stills, from AncientForest's own demo scene (`Scenes/Demo.unity`, synced with
    /// the pack): its camera's framing at the grid's size (ref-forest-camera.jpg) and the whole scene,
    /// the seven swaying trees as the pack composes them, at a quarter of the grid's size
    /// (ref-forest-scene.jpg). The scene's sprites draw with the pack's unlit default
    /// material, so these are the art's own colours in daylight, the reference for the night's grade.
    /// CFXR's demo scene draws with the built-in pipeline's materials (pink under URP), so its
    /// reference is PX2's effect gallery instead.
    /// </summary>
    public static class ReferenceStills
    {
        public const string ForestDemo = "Assets/Vendor/RPGW_AncientForest/Scenes/Demo.unity";

        public static void Write(string dir)
        {
            if (!File.Exists(ForestDemo)) throw new FileNotFoundException("ReferenceStills: no " + ForestDemo + ": run demo2d setup (it syncs the manifest)");
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(ForestDemo, OpenSceneMode.Single);
            var cam = Camera.main ?? throw new InvalidOperationException("ReferenceStills: the demo scene has no main camera");
            cam.gameObject.SetActive(false); // PixelShots draws with its own camera

            float h = cam.orthographicSize * 2, w = h * 16 / 9f;
            var framed = new RectInt(Mathf.RoundToInt(cam.transform.position.x - w / 2), Mathf.RoundToInt(cam.transform.position.y - h / 2),
                Mathf.RoundToInt(w), Mathf.RoundToInt(h));
            var px = PixelShots.Render(framed, cam.backgroundColor);
            int fw = framed.width * PixelShots.PixelsPerMetre, fh = framed.height * PixelShots.PixelsPerMetre;
            Save(PixelShots.Crop(px, fw, new RectInt(0, 0, fw, fh)), Path.Combine(dir, "ref-forest-camera.jpg"), "PX9 reference: AncientForest Demo.unity, its camera");

            // The scene also lays out the two whole sheets (MainLev.png and Decorative.png at the pack's
            // root), which the manifest doesn't sync: the demo uses the sliced copies (PX2's evidence
            // shows them), so those renderers have no sprite and are left out.
            var bounds = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                .Where(r => r.sprite != null).Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var all = new RectInt(Mathf.FloorToInt(bounds.min.x), Mathf.FloorToInt(bounds.min.y),
                Mathf.CeilToInt(bounds.size.x) + 1, Mathf.CeilToInt(bounds.size.y) + 1);
            all.width += (4 - all.width % 4) % 4;
            all.height += (4 - all.height % 4) % 4;
            var wholePx = PixelShots.Render(all, cam.backgroundColor);
            Save(PixelShots.Shrink(wholePx, all.width * PixelShots.PixelsPerMetre, all.height * PixelShots.PixelsPerMetre, 4),
                Path.Combine(dir, "ref-forest-scene.jpg"), "PX9 reference: AncientForest Demo.unity, the whole scene");
        }

        static void Save(Texture2D tex, string path, string tool)
        {
            RenderTag.Save(tex, path, tool);
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log("ReferenceStills: wrote " + path);
        }
    }
}
