using System.Collections.Generic;
using System.IO;
using Demo2D.Choreography;
using Demo2D.Runtime;
using Demo2D.Runtime.Playback;
using PS.Choreography;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo2D.EditorTools.Assets
{
    /// <summary>
    /// PX6's arena stills, without the horde: the whole arena in daylight at a quarter of the grid's
    /// size (overview.jpg), and each of the layout's other shots at the grid's size under the moon
    /// (shot-&lt;name&gt;.jpg). Built from the baked PlaybackData, as the player builds it. The
    /// ground-level stills with the horde come from the player (`--capture-stills`).
    /// </summary>
    public static class ArenaStills
    {
        public static void Write(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var data = PlaybackData.Load();
            var level = LevelScript.Parse(data.level.text);
            var arena = ForestArena.Parse(data.Layout(level.Layout).text, data.forestProps.text);
            var root = new GameObject("Arena Stills");
            var made = new List<Object>();
            ArenaProps props = null;
            try
            {
                ArenaGround.Build(arena, data, root.transform, made);
                props = new ArenaProps(arena, data.propSheets, data.spriteLit, root.transform);
                var noOne = new List<Vector2>();
                var whole = new Rect(arena.OriginX, arena.OriginY, arena.TilesW, arena.TilesH);
                props.Draw(0, whole, noOne);

                var light = PixelShots.GlobalLight(root.transform, false);
                var all = new RectInt(arena.OriginX, arena.OriginY, arena.TilesW, arena.TilesH);
                var px = PixelShots.Render(all, PixelShots.NightSky);
                Save(PixelShots.Shrink(px, all.width * PixelShots.PixelsPerMetre, all.height * PixelShots.PixelsPerMetre, 4),
                    Path.Combine(dir, "overview.jpg"), "PX6 arena overview");

                Object.DestroyImmediate(light.gameObject);
                PixelShots.GlobalLight(root.transform, true);
                foreach (LayoutShot shot in arena.Layout.Shots)
                {
                    if (shot.ViewWidthM >= arena.TilesW) continue;
                    int w = Mathf.RoundToInt(shot.ViewWidthM), h = Mathf.RoundToInt(shot.ViewWidthM * 9 / 16);
                    var r = new RectInt(Mathf.RoundToInt(shot.At.X - w / 2f), Mathf.RoundToInt(shot.At.Y - h / 2f), w, h);
                    props.Draw(0, new Rect(r.x, r.y, r.width, r.height), noOne);
                    var shotPx = PixelShots.Render(r, PixelShots.NightSky);
                    Save(PixelShots.Crop(shotPx, w * PixelShots.PixelsPerMetre, new RectInt(0, 0, w * PixelShots.PixelsPerMetre, h * PixelShots.PixelsPerMetre)),
                        Path.Combine(dir, "shot-" + shot.Name + ".jpg"), "PX6 arena shot " + shot.Name);
                }
            }
            finally
            {
                props?.Dispose();
                Object.DestroyImmediate(root);
                foreach (var o in made) Object.DestroyImmediate(o);
            }
        }

        static void Save(Texture2D tex, string path, string tool)
        {
            RenderTag.Save(tex, path, tool);
            Object.DestroyImmediate(tex);
            Debug.Log("ArenaStills: wrote " + path);
        }
    }
}
