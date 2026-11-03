using System;
using System.Collections.Generic;
using Champions.Render.Crowd;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>One orbit and one finite roster (at most three knights), independent of pulse/Tempo/Echo count.</summary>
    public sealed class ConstructVisuals : IDisposable
    {
        readonly MeleeLibrary weapons;
        readonly Material edge;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly List<(Matrix4x4 Matrix, float Light)> blades = new List<(Matrix4x4, float)>();
        readonly MonsterType knight;
        readonly MonsterFigure figures;
        readonly RigPose pose;
        readonly Vector4[] rows;
        EffectShot orbit, sentinel;
        int orbitForm, sentinelForm, idleLevel, figureCount;
        public float AuraArea = 1;
        float idleArea = 1;
        bool hasOrbit, hasSentinel;
        public ConstructVisuals(Palette palette, MeleeLibrary library = null)
        {
            weapons = library != null ? library : MeleeLibrary.Load();
            if (weapons != null) edge = new Material(weapons.Edge);
            block.SetFloat(IdPass.Id, IdPass.Effect);
            knight = MonsterLibrary.LoadFigure(MonsterLook.Default, MonsterLibrary.HeroId);
            if (knight == null) return;
            var material = EdgeLook.Hero(knight, MonsterLook.Default, palette);
            material.SetFloat("_CoreGlow", .12f);
            material.SetFloat("_Edge", .7f);
            figures = new MonsterFigure(knight, material, Champions.Sim.ConstructWeapons.MaxSentinels);
            figures.SetIdCode(IdPass.Effect);
            pose = new RigPose(knight.Rig);
            rows = new Vector4[knight.BoneCount * 3];
        }
        double heldAt = double.NaN;
        public void Hold(double t, bool held)
        {
            if (held && double.IsNaN(heldAt)) heldAt = t;
            if (held || double.IsNaN(heldAt)) return;
            orbit.T += t - heldAt; sentinel.T += t - heldAt;
            heldAt = double.NaN;
        }
        public static bool Handles(EffectCue cue) => cue >= EffectCue.OrbitCircle && cue <= EffectCue.SentinelFormation;
        public void Orbit(int level, float area) { idleLevel = level; idleArea = area; }
        public void Fire(EffectCue cue, in EffectShot shot)
        {
            if (cue <= EffectCue.OrbitCrown)
            { orbit = shot; orbitForm = cue - EffectCue.OrbitCircle; hasOrbit = true; }
            else
            { sentinel = shot; sentinelForm = cue - EffectCue.SentinelSentry; hasSentinel = true; }
        }
        public int Shapes => blades.Count + figureCount;
        public int Draws => blades.Count * (weapons == null ? 0 : weapons.Sword.subMeshCount) + (figureCount > 0 ? 1 : 0);
        public void Clear() { hasOrbit = hasSentinel = false; idleLevel = figureCount = 0; blades.Clear(); heldAt = double.NaN; }
        public void Present(double now, Vector2 hero)
        {
            if (!double.IsNaN(heldAt)) now = Math.Min(now, heldAt);
            blades.Clear(); figureCount = 0;
            if (hasOrbit && now - orbit.T > .5) hasOrbit = false;
            if (idleLevel > 0 || hasOrbit)
            {
                int form = idleLevel > 0 ? (idleLevel >= 8 ? 3 : idleLevel >= 5 ? 2 : idleLevel >= 3 ? 1 : 0) : orbitForm;
                float basis = (form == 0 ? 2.5f : 3.4f) * Mathf.Sqrt(idleArea);
                float age = hasOrbit ? (float)(now - orbit.T) : 1;
                float pulse = hasOrbit ? Mathf.Clamp01(1 - Mathf.Abs(age) / .5f) : 0;
                float r = basis;
                var at = idleLevel > 0 ? hero : orbit.At;
                int count = form == 0 ? 2 : form == 1 ? 3 : 4;
                float angle = hasOrbit ? age * Mathf.PI + (orbit.Mirrored ? Mathf.PI / 2 : 0) : (float)now * Mathf.PI;
                for (int k = 0; k < count; k++)
                {
                    float a = angle + k * Mathf.PI * 2 / count;
                    var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    float outwards = form == 0 ? r * .65f : Mathf.Lerp(r * .55f, r, pulse);
                    if (form == 2 && k % 2 == 1) outwards = r * .35f;
                    var root = new Vector3(at.x, .8f, at.y);
                    var pos = root + radial * outwards;
                    var along = form == 3 ? radial : Quaternion.Euler(0, 70 - pulse * 110, 0) * radial;
                    Blade(pos, along, r * (form == 0 ? .65f : .55f), .85f + pulse * .85f);
                    if (form == 3)
                        Blade(root + radial * r * .2f, radial, outwards - r * .2f, .55f + pulse * .4f);
                }
                // Retained rotating inner guard, including while the crown opens and during empowerment.
                if (form >= 1)
                    for (int k = 0; k < 2; k++)
                    {
                        float a = -angle + k * Mathf.PI;
                        var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        Blade(new Vector3(at.x, .55f, at.y), d, r * .65f, .7f);
                    }
            }
            if (!hasSentinel) return;
            float elapsed = (float)(now - sentinel.T);
            if (elapsed > sentinel.Duration) { hasSentinel = false; return; }
            if (elapsed < -.35f) return;
            float radius = sentinel.Radius;
            radius = radius / (sentinel.Power == EffectPower.OnBeat ? Mathf.Sqrt(2) : 1) * Mathf.Sqrt(AuraArea);
            var aim = sentinel.Facing.sqrMagnitude > .001f ? sentinel.Facing.normalized : Vector2.up;
            var facing = new Vector3(aim.x, 0, aim.y);
            var side = new Vector3(-aim.y, 0, aim.x);
            var center = new Vector3(sentinel.At.x, 0, sentinel.At.y) + facing * radius * .6f;
            int members = sentinelForm == 3 ? Champions.Sim.ConstructWeapons.MaxSentinels : 1;
            float p = Mathf.Clamp01(elapsed / .35f);
            for (int k = 0; k < members; k++)
            {
                var at = center;
                bool leap = sentinelForm >= 2 && (sentinel.Phase == 2 || k > 0);
                if (k > 0) at += side * radius * .75f * (k == 1 ? -1 : 1) + facing * radius * p;
                if (leap && k == 0) at += facing * radius * p;
                float lift = leap ? Mathf.Sin(p * Mathf.PI) * radius * .65f : 0;
                string role = leap ? "leap" : sentinelForm >= 1 && sentinel.Phase == 1 ? "parry" : "slashA";
                if (pose != null)
                {
                    int clip = knight.Rig.Clip(role);
                    if (clip < 0) clip = knight.Rig.Clip("slashA");
                    if (clip >= 0)
                    {
                        var c = knight.Rig.Clips[clip];
                        // Body flourish follows the beat pulse at natural speed; independent spectral blades own damage.
                        pose.Sample(clip, Math.Max(0, elapsed + c.Impact - .35f));
                        pose.Palette(rows, 0);
                        figures.Set(figureCount++, new MonsterInstance { Position = at + Vector3.up * lift,
                            Yaw = Mathf.Atan2(aim.x, aim.y), Scale = .85f,
                            Tint = MonsterInstance.Pack(new Color32(228, 238, 255, 255)), Flags = (uint)CrowdFlags.Alive }, rows);
                    }
                }
                if (elapsed <= .4f)
                {
                    var d = Quaternion.Euler(0, Mathf.Lerp(-110, 110, p), 0) * facing;
                    Blade(at + Vector3.up * (.9f + lift), d, radius * .9f, 1.2f);
                }
            }
            // Close guard survives the leaping/advancing additions.
            if (sentinelForm >= 2 && elapsed <= .4f)
                Blade(center + Vector3.up * .7f, Quaternion.Euler(0, elapsed * 360, 0) * facing, radius, .75f);
        }
        void Blade(Vector3 at, Vector3 direction, float length, float light)
        {
            if (weapons == null) return;
            blades.Add((Matrix4x4.TRS(at, Quaternion.FromToRotation(Vector3.up, direction), Vector3.one * length), light));
        }
        public void Queue(Camera camera)
        {
            if (weapons != null)
                foreach (var b in blades)
                {
                    block.SetColor("_Light", AuraArea > 1 ? new Color(b.Light, b.Light * .78f, b.Light * .03f) : new Color(b.Light, b.Light, b.Light));
                    var rp = Effects.Params(edge, block, camera);
                    for (int sub = 0; sub < weapons.Sword.subMeshCount; sub++) Graphics.RenderMesh(rp, weapons.Sword, sub, b.Matrix);
                }
            figures?.Draw(camera, figureCount);
        }
        public EffectCost Cost(EffectCue cue)
        {
            int form = cue <= EffectCue.OrbitCrown ? cue - EffectCue.OrbitCircle : cue - EffectCue.SentinelSentry;
            bool summon = cue >= EffectCue.SentinelSentry;
            int members = summon ? (form == 3 ? 3 : 1) : 0;
            int swords = summon ? members + (form >= 2 ? 1 : 0) : form == 0 ? 2 : form == 1 ? 5 : form == 2 ? 6 : 10;
            return new EffectCost { Shapes = members + swords,
                Draws = (weapons != null ? swords * weapons.Sword.subMeshCount : 0) + (members > 0 && knight != null ? 1 : 0),
                Triangles = (weapons != null ? swords * weapons.Sword.triangles.Length / 3 : 0) + (knight != null ? members * (int)knight.Lods[0].GetIndexCount(0) / 3 : 0) };
        }
        public void Dispose() { figures?.Dispose(); Effects.Destroy(edge); }
    }
}
