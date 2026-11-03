using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>Rigid owned weapons with independent authored motion; never accelerates a character clip.</summary>
    public sealed class MeleeVisuals : IDisposable
    {
        struct Action { public EffectCue Cue; public EffectShot Shot; }
        readonly List<Action> actions = new List<Action>();
        readonly List<(Mesh Mesh, Matrix4x4 Matrix, float Light)> draws = new List<(Mesh, Matrix4x4, float)>();
        readonly MeleeLibrary library;
        readonly Material material;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        public MeleeVisuals(MeleeLibrary weapons = null)
        {
            library = weapons != null ? weapons : MeleeLibrary.Load();
            if (library == null) return;
            material = new Material(library.Edge);
            block.SetFloat(IdPass.Id, IdPass.Effect);
        }
        double heldAt = double.NaN;
        public void Hold(double t, bool held)
        {
            if (held && double.IsNaN(heldAt)) heldAt = t;
            if (held || double.IsNaN(heldAt)) return;
            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i]; action.Shot.T += t - heldAt; actions[i] = action;
            }
            heldAt = double.NaN;
        }
        public static bool Handles(EffectCue cue) => cue >= EffectCue.MeleeSweep && cue <= EffectCue.HammerProcession;
        public void Fire(EffectCue cue, in EffectShot shot) => actions.Add(new Action { Cue = cue, Shot = shot });
        public void Clear() { actions.Clear(); draws.Clear(); heldAt = double.NaN; }
        public int Draws => draws.Count;
        public void Present(double now)
        {
            if (!double.IsNaN(heldAt)) now = Math.Min(now, heldAt);
            draws.Clear();
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var a = actions[i];
                double age = now - a.Shot.T;
                if (age > .35) { actions.RemoveAt(i); continue; }
                if (age < -.4 || library == null) continue;
                Pose(a.Cue, a.Shot, (float)age);
            }
        }
        void Pose(EffectCue cue, EffectShot s, float age)
        {
            bool hammer = cue >= EffectCue.HammerSwing;
            int form = hammer ? cue - EffectCue.HammerSwing : cue - EffectCue.MeleeSweep;
            float radius = s.Radius > 0 ? s.Radius : hammer ? 2.6f : 3;
            var aim = s.Facing.sqrMagnitude > .001f ? s.Facing.normalized : Vector2.up;
            float heading = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
            float p = Mathf.Clamp01((age + .35f) / .35f);
            float settle = Mathf.Clamp01(age / .35f);
            float light = (1 - settle) * (s.Spectral ? 1.2f : 2f);
            var at = new Vector3(s.At.x, 0, s.At.y);
            float direction = s.Mirrored ? -1 : 1;
            if (!hammer)
            {
                int count = form == 1 ? 2 : form == 3 ? 3 : 1;
                for (int k = 0; k < count; k++)
                {
                    var root = at;
                    if (form == 3) root += new Vector3(aim.x, 0, aim.y) * (radius * .75f * k * p);
                    float sweep = form >= 2 ? Mathf.Lerp(-180, 180, p) : Mathf.Lerp(-78, 78, p) * direction;
                    if (form == 1 && k == 1) sweep = -sweep + 55;
                    var turn = Quaternion.Euler(0, heading + sweep, 0);
                    var radial = turn * Vector3.forward;
                    Add(library.Sword, root + radial * .4f + Vector3.up * (.85f + .1f * k),
                        Quaternion.FromToRotation(Vector3.up, radial), radius * .85f, light * (k == 0 ? 1 : .8f));
                }
            }
            else if (form == 0)
            {
                var turn = Quaternion.Euler(0, heading + Mathf.Lerp(-85, 65, p) * direction, 0);
                var radial = turn * Vector3.forward;
                Add(library.Hammer, at + Vector3.up * 1.05f, Quaternion.FromToRotation(Vector3.up, radial), radius * .9f, light);
            }
            else
            {
                var root = at;
                if (form == 3) root += new Vector3(aim.x, 0, aim.y) * s.Advance;
                float pitch = Mathf.Lerp(-25, 110, p * p);
                float lift = form == 2 ? Mathf.Sin(p * Mathf.PI) * radius : 0;
                var rotation = Quaternion.Euler(0, heading, 0) * Quaternion.Euler(pitch, 0, 0);
                // At contact the head is on the ground, with a heavy settle after impact.
                var finalRotation = Quaternion.Euler(0, heading, 0) * Quaternion.Euler(110, 0, 0);
                var tip = finalRotation * Vector3.up * (radius * .85f);
                var contact = root;
                Add(library.Hammer, contact - tip + Vector3.up * (.12f + lift), rotation, radius * .85f, light);
                if (form >= 2)
                    for (int k = 0; k < 4; k++)
                    {
                        var pos = at + new Vector3(aim.x, 0, aim.y) * radius * (.3f + .23f * k);
                        Add(library.Sword, pos, Quaternion.Euler(0, heading + k * 45, 14),
                            Mathf.Max(.02f, 1 - Mathf.Abs(age) * 5) * radius * .45f, light * .55f);
                    }
                if (form == 3)
                    for (int k = 1; k <= 2; k++)
                    {
                        var next = root + new Vector3(aim.x, 0, aim.y) * radius * .7f * k;
                        Add(library.Hammer, next + Vector3.up * (1.8f + k * .5f),
                            Quaternion.Euler(0, heading, 0) * Quaternion.Euler(-25, 0, 0), radius * .65f, light * .65f);
                    }
            }
        }
        void Add(Mesh mesh, Vector3 at, Quaternion rotation, float scale, float light) =>
            draws.Add((mesh, Matrix4x4.TRS(at, rotation, Vector3.one * scale), light));
        public EffectCost Cost(EffectCue cue)
        {
            if (library == null) return default;
            int count = cue == EffectCue.MeleeCross ? 2 : cue == EffectCue.MeleeDance ? 3 : cue == EffectCue.HammerProcession ? 7 : cue == EffectCue.HammerEruption ? 5 : 1;
            var mesh = cue >= EffectCue.HammerSwing ? library.Hammer : library.Sword;
            int triangles = mesh.triangles.Length / 3;
            if (cue == EffectCue.HammerEruption || cue == EffectCue.HammerProcession)
                triangles = (cue == EffectCue.HammerProcession ? 3 : 1) * triangles + 4 * library.Sword.triangles.Length / 3;
            else triangles *= count;
            return new EffectCost { Draws = count * mesh.subMeshCount, Shapes = count, Triangles = triangles };
        }
        public void Queue(Camera camera)
        {
            if (material == null) return;
            foreach (var d in draws)
            {
                block.SetColor("_Light", new Color(d.Light, d.Light, d.Light));
                var rp = Effects.Params(material, block, camera);
                for (int sub = 0; sub < d.Mesh.subMeshCount; sub++) Graphics.RenderMesh(rp, d.Mesh, sub, d.Matrix);
            }
        }
        public void Dispose() { Effects.Destroy(material); }
    }
}
