using System;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>Low hovering filled green energy diamonds for XP, red hollow crosses for recovery.
    /// Distinct silhouettes and ground-hugging motion distinguish them from orange/red airborne shots.
    /// Preview capacity bounds rendering only; all unseen value stays in the sim.</summary>
    public sealed class PickupPresenter : IDisposable
    {
        public const int Limit = 384;
        readonly Pickup[] preview = new Pickup[Limit];
        readonly Mesh diamond, cross;
        readonly Material material, xpMaterial;
        readonly PieceBatch diamonds = new PieceBatch();
        readonly int layer;
        public int Drawn { get; private set; }

        public PickupPresenter(int layer)
        {
            this.layer = layer;
            material = new Material(Resources.Load<Material>(HeroMarker.MaterialPath));
            material.SetColor("_Color", new Color(1f, .18f, .23f));
            xpMaterial = new Material(Resources.Load<Shader>("Crowd/Pickup"));
            xpMaterial.SetFloat(IdPass.Id, IdPass.Effect);
            material.SetFloat(IdPass.Id, IdPass.Effect);
            diamond = new Mesh { name = "Filled XP diamond", vertices = new[] { new Vector3(0, .30f), new Vector3(.21f, 0), new Vector3(0, -.30f), new Vector3(-.21f, 0) },
                uv = new[] { new Vector2(.5f, 1), new Vector2(1, .5f), new Vector2(.5f, 0), new Vector2(0, .5f) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            diamond.RecalculateBounds();
            cross = Outline(new[] { new Vector2(-.07f,.25f), new Vector2(.07f,.25f), new Vector2(.07f,.07f), new Vector2(.25f,.07f), new Vector2(.25f,-.07f), new Vector2(.07f,-.07f), new Vector2(.07f,-.25f), new Vector2(-.07f,-.25f), new Vector2(-.07f,-.07f), new Vector2(-.25f,-.07f), new Vector2(-.25f,.07f), new Vector2(-.07f,.07f) });
        }

        public void Present(Simulation sim, double song, Camera camera)
        {
            var hero = sim.State.Hero;
            Drawn = sim.Pickups.Preview(hero.X, hero.Y, 22, preview);
            Draw(preview, Drawn, song, camera);
        }

        public void Draw(Pickup[] pickups, int count, double song, Camera camera)
        {
            Drawn = Math.Min(count, Limit);
            xpMaterial.SetFloat("_Motion", (float)song);
            var rp = new RenderParams(material) { layer = layer, camera = camera, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            diamonds.Clear();
            for (int i = 0; i < Drawn; i++)
            {
                var p = pickups[i];
                if (!(p.Value > 0)) continue;
                float hover = .38f + .045f * Mathf.Sin((float)song * 2 + p.Id);
                var matrix = Matrix4x4.TRS(new Vector3((float)p.X, hover, (float)p.Y), Quaternion.Euler(45, 0, 0), Vector3.one);
                // The diamonds draw at once; the rare recovery crosses one by one.
                if (p.Kind == PickupKind.Experience) diamonds.Add(diamond, matrix, Color.white);
                else Graphics.RenderMesh(rp, cross, 0, matrix);
            }
            diamonds.Queue(xpMaterial, camera);
        }

        static Mesh Outline(Vector2[] points)
        {
            var v = new Vector3[points.Length * 2];
            var triangles = new int[points.Length * 6];
            for (int i = 0; i < points.Length; i++)
            {
                v[i * 2] = points[i]; v[i * 2 + 1] = points[i] * .72f;
                int a = i * 2, b = ((i + 1) % points.Length) * 2, at = i * 6;
                triangles[at] = a; triangles[at+1] = b; triangles[at+2] = a+1;
                triangles[at+3] = a+1; triangles[at+4] = b; triangles[at+5] = b+1;
            }
            var mesh = new Mesh { name = "Pickup outline", vertices = v, triangles = triangles };
            mesh.RecalculateBounds(); return mesh;
        }

        public void Dispose()
        {
            diamonds.Dispose();
            foreach (var o in new UnityEngine.Object[] { diamond, cross, material, xpMaterial })
                if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
