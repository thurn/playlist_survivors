using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// Maps the sim's structure events onto the structures as the player sees them (design.md, The World &gt; Structures,
    /// The Look &gt; Shatter), drawn by an <see cref="IStructureView"/>: the edge look over the zone fill
    /// (<see cref="EdgeStructures"/>), or stand-ins without baked structures. A structure stands when its chunk loads
    /// (<see cref="SimEventKind.StructureSpawn"/>) and goes when it unloads, a hit flashes it, and a break shatters it on the
    /// break's own frame, thrown along the blow. Breaks at one instant send one structure-break ring to the world (a
    /// special moment, design.md, Restraint) from their middle; a boss's breaks send none, its own entrance or attack rings
    /// for them. The assembly's EventDispatch hands it the events.
    /// </summary>
    public sealed class StructurePresenter : IDisposable
    {
        const double SameInstant = 1e-4;

        struct Pending
        {
            public int Id;
            public double T;
            public Vector2 Blow;
            public float Power;
        }

        readonly StructureRules _rules;
        readonly IStructureView _view;
        readonly IWorldCommands _world;
        readonly Dictionary<int, Vector3> _standing = new Dictionary<int, Vector3>();
        readonly List<Pending> _pending = new List<Pending>();
        double _ringT = double.NaN;
        int _ringBreaks;
        Vector2 _ringSum;

        /// <summary>Draws the sim's <paramref name="rules"/>' kinds through <paramref name="view"/>, sending rings to <paramref name="world"/>.</summary>
        public StructurePresenter(StructureRules rules, IStructureView view, IWorldCommands world)
        {
            _rules = rules;
            _view = view;
            _world = world;
        }

        /// <summary>
        /// The presenter on <paramref name="look"/>'s baked structures (`--look trailer`: the Synty models unsimplified), or
        /// on stand-ins on <paramref name="layer"/> when none is baked.
        /// </summary>
        public static StructurePresenter Create(StructureRules rules, Palette palette, IWorldCommands world, int layer = 0, MonsterLook look = MonsterLook.Default)
        {
            IStructureView view = EdgeStructures.Create(look, rules);
            if (view == null)
            {
                Debug.LogWarning("StructurePresenter: no baked structures (champions structure bake all, then build): they draw as stand-ins");
                view = new StandInStructures(palette, layer);
            }
            return new StructurePresenter(rules, view, world);
        }

        /// <summary>The structures standing.</summary>
        public int Standing => _standing.Count;

        /// <summary>Where structure <paramref name="id"/> stands (its footprint's middle on the ground), if it stands.</summary>
        public bool TryGet(int id, out Vector3 position) => _standing.TryGetValue(id, out position);

        /// <summary>Breaks this frame showed (their impact frame).</summary>
        public int BrokeThisFrame { get; private set; }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            if (!double.IsNaN(_ringT) && (e.Kind != SimEventKind.StructureBreak || Math.Abs(e.T - _ringT) > SameInstant)) Flush();
            switch (e.Kind)
            {
                case SimEventKind.StructureSpawn:
                {
                    var at = new Vector2((float)e.D0, (float)e.D1);
                    _standing[e.I0] = new Vector3(at.x, 0, at.y);
                    _view.Stand(e.I0, _rules.Kinds[e.I1], at, (float)e.D2);
                    break;
                }
                case SimEventKind.StructureGone:
                    _standing.Remove(e.I0);
                    _view.Remove(e.I0);
                    break;
                case SimEventKind.StructureHit:
                    _view.Hit(e.I0, e.T);
                    break;
                case SimEventKind.StructureBreak:
                {
                    // A strong blow (a 2x on-beat dash's) throws hardest; a boss's at full.
                    float power = e.I2 == (int)BreakCause.Blow ? Mathf.Clamp(0.6f + 0.2f * (float)e.D2, 0.6f, 1.3f) : 1.2f;
                    int i = _pending.Count;
                    while (i > 0 && _pending[i - 1].T > e.T) i--;
                    _pending.Insert(i, new Pending { Id = e.I0, T = e.T, Blow = new Vector2((float)e.D0, (float)e.D1), Power = power });
                    if (e.I2 != (int)BreakCause.Blow) break;
                    if (double.IsNaN(_ringT)) _ringT = e.T;
                    _ringBreaks++;
                    _ringSum += new Vector2((float)e.D3, (float)e.D4);
                    break;
                }
            }
        }

        /// <summary>Ends a batch of events: the breaks at one instant send their ring.</summary>
        public void Flush()
        {
            if (double.IsNaN(_ringT)) return;
            _world?.Ring(RingKind.StructureBreak, _ringSum / _ringBreaks, _ringT, Color.white, Mathf.Clamp01(_ringBreaks / 6f));
            _ringT = double.NaN;
            _ringBreaks = 0;
            _ringSum = Vector2.zero;
        }

        /// <summary>The frame at song time <paramref name="song"/>: the breaks it reaches shatter, then the structures draw for <paramref name="camera"/>.</summary>
        public void Present(double song, Camera camera)
        {
            BrokeThisFrame = 0;
            while (_pending.Count > 0 && _pending[0].T <= song)
            {
                var p = _pending[0];
                _pending.RemoveAt(0);
                if (!_standing.Remove(p.Id)) continue;
                _view.Break(p.Id, p.T, p.Blow, p.Power);
                BrokeThisFrame++;
            }
            _view.Present(song, camera);
        }

        /// <summary>Queues this frame's draws for the camera again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera) => _view.Queue(camera);

        public void Dispose() => _view.Dispose();
    }
}
