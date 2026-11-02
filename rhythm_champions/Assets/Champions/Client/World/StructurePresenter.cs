using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Render.Structures;
using Champions.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Champions.Client
{
    /// <summary>
    /// Maps the sim's structure events onto the structure field and its shatter (design.md, The World &gt; Structures, The
    /// Look &gt; Shatter): a structure stands when its chunk loads (<see cref="SimEventKind.StructureSpawn"/>) and goes when it
    /// unloads, a hit flashes it, and a break takes it away and throws its pieces on the break's own frame, thrown along the
    /// blow. Breaks at one instant send one structure-break ring to the world (a special moment, design.md, Restraint) from
    /// their middle; a boss's breaks send none, its own entrance or attack rings for them. The sim's kinds and the library's
    /// types meet by id (structures.json's and the recipes'). The assembly's EventDispatch hands it the events.
    /// </summary>
    public sealed class StructurePresenter : IDisposable
    {
        public const int CapacityPerType = 512;
        const double SameInstant = 1e-4;

        struct Pending
        {
            public int Id;
            public double T;
            public Vector2 Blow;
            public float Power;
        }

        readonly StructureRenderer _field;
        readonly StructureShatter _shatter;
        readonly IWorldCommands _world;
        readonly int[] _typeOfKind;
        readonly List<Pending> _pending = new List<Pending>();
        double _ringT = double.NaN;
        int _ringBreaks;
        Vector2 _ringSum;

        /// <summary>
        /// Draws <paramref name="lib"/>'s structures for the sim's <paramref name="rules"/>' kinds, sending rings to
        /// <paramref name="world"/>; the pieces fly in <paramref name="physicsScene"/> when given (an EditMode test's), else
        /// in a scene of their own.
        /// </summary>
        public StructurePresenter(StructureLibrary lib, StructureRules rules, IWorldCommands world, Scene? physicsScene = null)
        {
            _world = world;
            _field = new StructureRenderer(lib, CapacityPerType);
            _shatter = new StructureShatter(lib, physicsScene: physicsScene);
            _typeOfKind = new int[rules.Kinds.Length];
            for (int k = 0; k < _typeOfKind.Length; k++)
            {
                _typeOfKind[k] = lib.IndexOf(rules.Kinds[k].Id);
                if (_typeOfKind[k] < 0) Debug.LogWarning($"StructurePresenter: no baked cut for the structure {rules.Kinds[k].Id} (champions edgecut {rules.Kinds[k].Id}): it stands unseen");
            }
        }

        /// <summary>The presenter for <paramref name="rules"/>, or null with why when no structure is baked (a checkout without the vendor files).</summary>
        public static StructurePresenter Create(StructureRules rules, IWorldCommands world, out string why)
        {
            var lib = StructureLibrary.Load();
            why = lib == null || lib.Types.Length == 0 ? "no baked structures (champions edgecut <kind> for each in structures.json)" : null;
            return why == null ? new StructurePresenter(lib, rules, world) : null;
        }

        public StructureRenderer Field => _field;
        public StructureShatter Shatter => _shatter;

        /// <summary>Breaks this frame showed (their impact frame), and every break shown.</summary>
        public int BrokeThisFrame { get; private set; }

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            if (!double.IsNaN(_ringT) && (e.Kind != SimEventKind.StructureBreak || Math.Abs(e.T - _ringT) > SameInstant)) Flush();
            switch (e.Kind)
            {
                case SimEventKind.StructureSpawn:
                {
                    int type = _typeOfKind[e.I1];
                    if (type >= 0) _field.Add(e.I0, type, new Vector2((float)e.D0, (float)e.D1), (float)e.D2);
                    break;
                }
                case SimEventKind.StructureGone:
                    _field.Remove(e.I0);
                    break;
                case SimEventKind.StructureHit:
                    _field.Flash(e.I0, e.T, 1f);
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

        /// <summary>The frame at song time <paramref name="song"/>: the breaks it reaches shatter, then the field and the pieces queue for <paramref name="camera"/>.</summary>
        public void Present(double song, Camera camera)
        {
            BrokeThisFrame = 0;
            while (_pending.Count > 0 && _pending[0].T <= song)
            {
                var p = _pending[0];
                _pending.RemoveAt(0);
                if (!_field.TryGet(p.Id, out int type, out var standing)) continue;
                _field.Remove(p.Id);
                _shatter.Launch(type, standing.Position, standing.Angle, p.T, p.Blow, p.Power);
                BrokeThisFrame++;
            }
            _shatter.Present(song);
            _field.Draw(camera);
            _shatter.Draw(camera);
        }

        /// <summary>Queues this frame's draws for the camera again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera)
        {
            _field.Queue(camera);
            _shatter.Draw(camera);
        }

        public void Dispose()
        {
            _field.Dispose();
            _shatter.Dispose();
        }
    }
}
