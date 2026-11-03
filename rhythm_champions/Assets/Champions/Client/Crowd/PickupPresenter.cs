using System;
using Champions.Render;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>Low hovering hollow diamonds for XP, hollow crosses for recovery.
    /// White-blue outlines, dark centers and ground-hugging motion distinguish them from yellow airborne shots.
    /// Preview capacity bounds rendering only; all unseen value stays in the sim.</summary>
    public sealed class PickupPresenter : IDisposable
    {
        public const int Limit = 384;
        readonly Pickup[] preview = new Pickup[Limit];
        readonly Mesh diamond, cross;
        readonly Material material;
        readonly int layer;
        public int Drawn { get; private set; }

        public PickupPresenter(int layer)
        {
            this.layer = layer;
            material = new Material(Resources.Load<Material>(HeroMarker.MaterialPath));
            material.SetColor("_Color", new Color(0.55f, 0.72f, 1f) * 1.4f);
            material.SetFloat(IdPass.Id, IdPass.Effect);
            diamond = Outline(new[] { new Vector2(0, .28f), new Vector2(.18f, 0), new Vector2(0, -.28f), new Vector2(-.18f, 0) });
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
            var rp = new RenderParams(material) { layer = layer, camera = camera, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (int i = 0; i < Drawn; i++)
            {
                var p = pickups[i];
                if (!(p.Value > 0)) continue;
                float hover = .38f + .045f * Mathf.Sin((float)song * 2 + p.Id);
                var matrix = Matrix4x4.TRS(new Vector3((float)p.X, hover, (float)p.Y), Quaternion.Euler(45, 0, 0), Vector3.one);
                Graphics.RenderMesh(rp, p.Kind == PickupKind.Experience ? diamond : cross, 0, matrix);
            }
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
            foreach (var o in new UnityEngine.Object[] { diamond, cross, material })
                if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
