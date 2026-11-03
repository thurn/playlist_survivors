using System;
using System.Collections.Generic;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>White closed prisms and dark cores: no filled ground disks or stock particle shaders.</summary>
    public sealed class MagicVisuals : IDisposable
    {
        struct Action { public EffectCue Cue; public EffectShot Shot; }
        readonly List<Action> actions = new List<Action>();
        readonly List<(Mesh Mesh, Matrix4x4 Matrix, float Light)> draws = new List<(Mesh, Matrix4x4, float)>();
        readonly MagicLibrary library;
        readonly Material material;
        readonly PieceBatch batch = new PieceBatch();
        public MagicVisuals()
        {
            library = MagicLibrary.Load();
            if (library == null) return;
            material = new Material(library.Edge);
        }
        public static bool Handles(EffectCue cue) => cue >= EffectCue.LightningBolt && cue <= EffectCue.LightningLink;
        public int Draws => draws.Count;
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
        public void Fire(EffectCue cue, in EffectShot shot)
        {
            // A field has one idle outline, regardless of pulse/Tempo/Echo count. Replace at the same anchor.
            var anchor = shot.At;
            if (cue >= EffectCue.EmberSeal && cue <= EffectCue.EmberProcession)
                actions.RemoveAll(a => a.Cue >= EffectCue.EmberSeal && a.Cue <= EffectCue.EmberProcession && a.Shot.At == anchor);
            actions.Add(new Action { Cue = cue, Shot = shot });
        }
        public void Clear() { actions.Clear(); draws.Clear(); heldAt = double.NaN; }
        public float AuraArea = 1, AuraRemaining;
        public Vector2 Hero;
        public void Present(double now)
        {
            draws.Clear();
            if (!double.IsNaN(heldAt)) now = Math.Min(now, heldAt);
            if (library != null && AuraRemaining > 0)
                Ring(Ground(Hero), 1.05f + .04f * Mathf.Sin((float)now * 7), .07f, 2 * Mathf.Clamp01(AuraRemaining / .3f));
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var a = actions[i]; float age = (float)(now - a.Shot.T);
                float life = a.Cue >= EffectCue.EmberSeal && a.Cue <= EffectCue.EmberProcession ? Mathf.Max(.05f, a.Shot.Duration) : .6f;
                if (age > life) { actions.RemoveAt(i); continue; }
                if (age < 0 || library == null) continue;
                Pose(a.Cue, a.Shot, age, life);
            }
        }
        void Pose(EffectCue cue, EffectShot s, float age, float life)
        {
            var at = Ground(s.At); var to = Ground(s.To);
            float light = s.Spectral ? 1.15f : 1.7f;
            float r = s.Radius > 0 ? s.Radius : 1;
            if (cue == EffectCue.LightningLink)
            {
                if (age > .24f) return;
                var start = at + Vector3.up * .9f; var end = to + Vector3.up * .9f;
                var side = Vector3.Cross(end - start, Vector3.up).normalized;
                var last = start;
                for (int k = 1; k <= 5; k++)
                {
                    var next = Vector3.Lerp(start, end, k / 5f);
                    if (k < 5) next += side * (k % 2 == 0 ? -.22f : .22f);
                    Link(last, next, .045f, light * (1 - age / .3f)); last = next;
                }
                Node(end, .13f, light);
                return;
            }
            if (cue <= EffectCue.StormConstellation)
            {
                int form = cue - EffectCue.LightningBolt;
                float fade = 1 - age / life;
                if (form == 0) Node(at + Vector3.up, .18f, light * fade);
                if (form == 1)
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var tip = at + new Vector3(k * r * .65f, 1.5f, r * .55f);
                        Link(at + Vector3.up * .5f, tip, .055f, light * fade); Node(tip, .16f, light * fade);
                    }
                if (form >= 2)
                {
                    Ring(to, r * .8f, .12f, light * fade);
                    if (form == 2)
                        for (int k = 0; k < 3; k++) Link(to, to + Radial(k * 120) * r * .8f, .025f, light * fade);
                    if (form == 3)
                    {
                        // Travel from the previous node to this activation's sim-authored discharge node.
                        var previous = at + Quaternion.Euler(0, -90, 0) * (to - at);
                        var current = Vector3.Lerp(previous, to, Mathf.Clamp01(age / .25f));
                        for (int k = 0; k < 3; k++)
                        {
                            var pos = current + Radial(k * 120 + age * 120) * r * .55f + Vector3.up * (.7f + k * .18f);
                            Link(current + Vector3.up * .7f, pos, .025f, light * fade); Node(pos, .18f, light * fade);
                        }
                    }
                }
                return;
            }
            int f = cue - EffectCue.EmberSeal;
            var aim = s.Facing.sqrMagnitude > .001f ? Ground(s.Facing).normalized : Vector3.forward;
            var across = Vector3.Cross(Vector3.up, aim);
            float pulse = Mathf.Clamp01(1 - age / .45f);
            if (s.RestRadius > 0) r = s.RestRadius * Mathf.Sqrt(AuraArea);
            float steady = .35f + .65f * pulse;
            // Empowerment swells the pulse footprint only; the idle seal returns to base after the beat.
            Ring(at, r, .035f, light * steady);
            if (f >= 1)
            {
                var last = at;
                for (int k = 1; k <= 4; k++)
                {
                    var next = at + aim * (r * .35f * k) + across * (k % 2 == 0 ? -.12f : .12f) * r;
                    Link(last + Vector3.up * .08f, next + Vector3.up * .08f, .035f, light * steady);
                    if (pulse > .05f) Node(next + Vector3.up * (.28f * pulse), new Vector3(.13f, .5f * pulse, .13f), light * pulse);
                    last = next;
                }
            }
            if (f >= 2)
            {
                var bowl = at + across * r * .7f + Vector3.up * (.7f + pulse * .2f);
                Add(library.Bowl, bowl, Quaternion.AngleAxis(pulse * 35, aim), Vector3.one * r * .3f, light * steady);
                var pour = at + across * r;
                Link(bowl, Vector3.Lerp(bowl, pour, Mathf.Clamp01(age / .2f)), .055f, light * steady);
                Link(at, pour, .035f, light * steady);
            }
            if (f == 3)
            {
                var node = at + aim * s.Advance * (s.Radius > 0 ? r / s.Radius : 1);
                Ring(node, r * .8f, .06f, light * steady);
                for (int k = 0; k < 3; k++)
                {
                    var pos = node + Radial(k * 120) * r * .45f;
                    Node(pos + Vector3.up * pulse * .4f, new Vector3(.2f, .85f * pulse + .05f, .2f), light * steady);
                }
            }
        }
        static Vector3 Ground(Vector2 p) => new Vector3(p.x, 0, p.y);
        static Vector3 Radial(float angle) => Quaternion.Euler(0, angle, 0) * Vector3.forward;
        void Ring(Vector3 at, float radius, float height, float light)
        {
            for (int k = 0; k < 8; k++)
                Link(at + Radial(k * 45) * radius + Vector3.up * height,
                    at + Radial((k + 1) * 45) * radius + Vector3.up * height, .025f, light);
        }
        void Link(Vector3 a, Vector3 b, float width, float light)
        {
            float length = (b - a).magnitude;
            if (length < .001f) return;
            Add(library.Link, (a + b) * .5f, Quaternion.FromToRotation(Vector3.up, b - a), new Vector3(width, length, width), light);
        }
        void Node(Vector3 at, float size, float light) => Node(at, Vector3.one * size, light);
        void Node(Vector3 at, Vector3 size, float light) => Add(library.Node, at, Quaternion.identity, size, light);
        void Add(Mesh mesh, Vector3 at, Quaternion turn, Vector3 size, float light) =>
            draws.Add((mesh, Matrix4x4.TRS(at, turn, size), light));
        public EffectCost Cost(EffectCue cue)
        {
            if (library == null) return default;
            // Maximum pose cost, independent of current playback age; includes all closed primitives.
            int links = cue == EffectCue.LightningLink ? 5 : cue == EffectCue.StormSigil ? 11 :
                cue == EffectCue.StormConstellation ? 11 : cue == EffectCue.LightningFork ? 2 :
                cue == EffectCue.LightningBolt ? 0 : cue == EffectCue.EmberSeal ? 8 :
                cue == EffectCue.EmberFissure ? 12 : cue == EffectCue.EmberBrazier ? 14 : 22;
            int nodes = cue == EffectCue.LightningBolt || cue == EffectCue.LightningLink ? 1 : cue == EffectCue.LightningFork ? 2 :
                cue == EffectCue.StormConstellation ? 3 : cue >= EffectCue.EmberFissure && cue <= EffectCue.EmberProcession ? (cue == EffectCue.EmberProcession ? 7 : 4) : 0;
            int bowls = cue == EffectCue.EmberBrazier || cue == EffectCue.EmberProcession ? 1 : 0;
            int count = links + nodes + bowls;
            return new EffectCost { Draws = count, Shapes = count, Triangles = links * library.Link.triangles.Length / 3 +
                nodes * library.Node.triangles.Length / 3 + bowls * library.Bowl.triangles.Length / 3 };
        }
        public void Queue(Camera camera)
        {
            if (material == null) return;
            batch.Clear();
            foreach (var d in draws)
                batch.Add(d.Mesh, d.Matrix, AuraRemaining > 0 ? new Color(d.Light, d.Light * .78f, d.Light * .03f) : new Color(d.Light, d.Light, d.Light));
            batch.Queue(material, camera);
        }
        public void Dispose()
        {
            batch.Dispose();
            Effects.Destroy(material);
        }
    }
}
