using System.Collections.Generic;
using System.IO;
using Game.Client.Arena;
using Game.EditorTools.Arena;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Tests
{
    /// <summary>
    /// The arena's static shadow map end to end on the GPU (demo3d's ArenaTests, with the game's
    /// regional redraw): two cubes, the night's moon apart, render into a small map; a ground quad in
    /// Game/LitStaticShadow is drawn top-down with no realtime shadows and the cubes hidden from the
    /// camera, so only the map shades it. One cube breaks (its renderer off) and its region is
    /// redrawn: its shadow goes and the other's stays; it regrows and its shadow comes back.
    /// </summary>
    public class StaticShadowMapTests
    {
        const int Hidden = 31; // the cubes' layer, which the camera doesn't draw

        [Test]
        public void ARedrawnRegionDropsABrokenStructuresShadowAndKeepsTheRest()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var l = ArenaLighting.Parse(File.ReadAllText(ArenaBaker.LayoutPath));
            var objects = new List<Object>();
            StaticShadowMap map = null;
            RenderTexture rt = null;
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // a cold editor would draw placeholder shaders while compiling
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.fog = false;
                var sun = new GameObject("Moon").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.rotation = l.MoonRotation;
                sun.intensity = 1f;
                sun.shadows = LightShadows.None;
                RenderSettings.sun = sun;
                objects.Add(sun.gameObject);

                var toLight = -(l.MoonRotation * Vector3.forward);
                var across = Vector3.Cross(Vector3.up, new Vector3(toLight.x, 0, toLight.z)).normalized;
                var cubeMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                objects.Add(cubeMat);
                Renderer Cube(Vector3 at)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.layer = Hidden;
                    cube.transform.position = at;
                    cube.transform.localScale = new Vector3(2, 2, 2);
                    var r = cube.GetComponent<Renderer>();
                    r.sharedMaterial = cubeMat;
                    objects.Add(cube);
                    return r;
                }
                var kept = Cube(new Vector3(0, 3, 0) + across * 7);
                var broken = Cube(new Vector3(0, 3, 0) - across * 7);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90, 0, 0));
                ground.transform.localScale = new Vector3(40, 40, 1);
                var groundMat = new Material(Shader.Find("Game/LitStaticShadow"));
                groundMat.SetColor("_BaseColor", Color.white);
                groundMat.SetFloat("_Smoothness", 0f);
                ground.GetComponent<Renderer>().sharedMaterial = groundMat;
                ground.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                objects.Add(ground);
                objects.Add(groundMat);

                var cam = new GameObject("Top").AddComponent<Camera>();
                objects.Add(cam.gameObject);
                cam.orthographic = true;
                cam.orthographicSize = 20;
                cam.transform.SetPositionAndRotation(new Vector3(0, 50, 0), Quaternion.Euler(90, 0, 0));
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 1;
                cam.farClipPlane = 100;
                cam.cullingMask = ~(1 << Hidden);
                rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                var tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
                objects.Add(tex);
                // One frame first: a cold editor sets the pipeline up on its first camera, which must
                // not come after the map is published.
                cam.Render();

                var casters = new[] { kept, broken };
                map = StaticShadowMap.Render(casters, l.MoonRotation, new Bounds(new Vector3(0, 5, 0), new Vector3(40, 10, 40)), 512);

                Vector3 Shade(Renderer r)
                {
                    var p = r.transform.position;
                    var s = p - toLight * (p.y / toLight.y);
                    return new Vector3(s.x, 0, s.z);
                }
                var open = Shade(kept) + (Shade(broken) - Shade(kept)) * 0.5f; // between the two shadows
                (float kept, float broken, float open) Shot()
                {
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    float Lum(Vector3 world)
                    {
                        var c = tex.GetPixel(Mathf.RoundToInt((world.x + 20) / 40 * 256), Mathf.RoundToInt((world.z + 20) / 40 * 256));
                        return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    }
                    return (Lum(Shade(kept)), Lum(Shade(broken)), Lum(open));
                }

                var whole = Shot();
                TestContext.WriteLine($"standing: kept {whole.kept:F3}, broken {whole.broken:F3}, open {whole.open:F3}");
                Assert.That(whole.open, Is.GreaterThan(0.1f), "the ground is lit between the shadows");
                Assert.That(whole.kept, Is.LessThan(0.35f * whole.open), "each cube's shadow falls where the moon throws it");
                Assert.That(whole.broken, Is.LessThan(0.35f * whole.open));

                broken.enabled = false;
                map.Redraw(casters, new[] { broken.bounds });
                var after = Shot();
                TestContext.WriteLine($"broken: kept {after.kept:F3}, broken {after.broken:F3}, open {after.open:F3}");
                Assert.That(after.broken, Is.GreaterThan(0.8f * after.open), "the broken cube's shadow is gone");
                Assert.That(after.kept, Is.LessThan(0.35f * after.open), "the other's, outside the region, stays");
                Assert.That(map.RedrawnCasters, Is.EqualTo(0), "the region holds only the broken cube");

                broken.enabled = true;
                map.Redraw(casters, new[] { broken.bounds });
                var regrown = Shot();
                Assert.That(regrown.broken, Is.LessThan(0.35f * regrown.open), "the regrown cube shadows again");
                Assert.That(regrown.kept, Is.LessThan(0.35f * regrown.open));
                Assert.That(map.RedrawnCasters, Is.EqualTo(1));
            }
            finally
            {
                map?.Dispose();
                ShaderUtil.allowAsyncCompilation = async;
                if (rt != null) rt.Release();
                foreach (var o in objects) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
