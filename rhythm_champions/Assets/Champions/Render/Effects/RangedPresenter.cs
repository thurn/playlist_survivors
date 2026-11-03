using System;
using System.Collections.Generic;
using Champions.Sim;
using UnityEngine;
namespace Champions.Render
{
    /// <summary>Rigid source weapons over dark closed facets; poses follow authoritative simulation flights.</summary>
    public sealed class RangedPresenter : IDisposable
    {
        readonly RangedLibrary library;
        readonly Material material;
        readonly List<(Mesh Mesh, Matrix4x4 Matrix, bool Empowered)> draws = new List<(Mesh, Matrix4x4, bool)>();
        readonly PieceBatch batch = new PieceBatch();
        readonly Color ordinary;
        public RangedPresenter(Palette palette)
        {
            library = RangedLibrary.Load();
            if (library == null) return;
            material = new Material(library.Shader);
            ordinary = palette.HeroEdge.linear * 2;
            material.SetColor("_Body", palette.VoidBody.linear);
        }
        public void Present(RangedWeapons ranged, Camera camera)
        {
            draws.Clear();
            if (library == null || ranged == null) return;
            foreach (var f in ranged.Flights)
            {
                bool spear = f.Effect.Activation.Kind == WeaponKind.Spear;
                double age = Math.Max(0, f.Age - f.Delay);
                f.Pose(age, out double x, out double y, out double height);
                var direction = new Vector3((float)(f.ToX - f.X), 0, (float)(f.ToY - f.Y)).normalized;
                if (f.Arc)
                    direction = (direction + Vector3.up * (float)(4 * f.Height * (1 - 2 * age / f.Duration) /
                        Math.Max(1, new Vector2((float)(f.ToX-f.X), (float)(f.ToY-f.Y)).magnitude))).normalized;
                if (spear && f.Form >= 2 && f.Age < f.Delay)
                    direction = Quaternion.AngleAxis((float)(60 * (1 - f.Age / f.Delay)), Vector3.up) * direction;
                var at = new Vector3((float)x, (float)height, (float)y);
                float width = (float)Math.Sqrt(f.Effect.Activation.AreaFactor);
                draws.Add((spear ? library.Spear : library.Arrow, Matrix4x4.TRS(at,
                    Quaternion.LookRotation(direction), new Vector3(width, width, spear ? 2.8f : 1.5f)), f.Effect.Activation.EmpowermentArea > 1));
                if (!spear && !f.Effect.ImpactTarget.HasValue && (f.Form == 3 || f.Effect.Projectile == 0) && f.Age < f.Delay + 0.3)
                {
                    // Battery bows rotate from their ring tangent into the captured aim, then recoil naturally.
                    var aim = Quaternion.LookRotation(direction);
                    if (f.Form == 3) aim = Quaternion.Slerp(Quaternion.Euler(0, (float)(f.Effect.Projectile * 45 + f.Effect.Activation.Sequence * 26), 0), aim,
                        Mathf.Clamp01((float)(f.Age / Math.Max(0.04, f.Delay + 0.04))));
                    var bowAt = new Vector3((float)f.X, 1, (float)f.Y) - direction * (0.16f * Mathf.Sin((float)age / 0.3f * Mathf.PI));
                    draws.Add((library.Bow, Matrix4x4.TRS(bowAt, aim, Vector3.one * 1.8f), f.Effect.Activation.EmpowermentArea > 1));
                }
            }
            Queue(camera);
        }
        public void Queue(Camera camera)
        {
            if (material == null) return;
            batch.Clear();
            foreach (var draw in draws) batch.Add(draw.Mesh, draw.Matrix, draw.Empowered ? new Color(2, 1.6f, .05f) : ordinary);
            batch.Queue(material, camera);
        }
        public void Dispose()
        {
            batch.Dispose();
            if (material != null) UnityEngine.Object.Destroy(material);
        }
    }
}
