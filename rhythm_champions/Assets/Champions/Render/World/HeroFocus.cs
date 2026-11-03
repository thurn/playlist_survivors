using System;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>A small camera-facing contrast pocket behind the live hero. Warnings draw after it.</summary>
    public sealed class HeroFocus : IDisposable
    {
        readonly Material material;
        readonly Mesh quad;
        Matrix4x4 matrix;
        bool shown;

        public HeroFocus()
        {
            material = new Material(Resources.Load<Shader>("Crowd/HeroFocus"));
            quad = new Mesh { name = "Hero contrast pocket" };
            quad.vertices = new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
        }

        public void Present(Vector3 feet, Camera camera, bool visible)
        {
            shown = visible && camera != null;
            if (!shown) return;
            matrix = Matrix4x4.TRS(feet + Vector3.up, camera.transform.rotation, new Vector3(1.25f, 1.65f, 1));
            Queue(camera);
        }

        public void Queue(Camera camera)
        {
            if (!shown) return;
            var rp = Effects.Params(material, null, camera);
            Graphics.RenderMesh(rp, quad, 0, matrix);
        }

        public void Dispose()
        {
            foreach (var o in new UnityEngine.Object[] { material, quad })
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
