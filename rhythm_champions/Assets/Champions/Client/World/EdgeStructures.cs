using System.Collections.Generic;
using Champions.Render.Crowd;
using Champions.Render.Structures;
using Champions.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Champions.Client
{
    /// <summary>
    /// The structures in the edge look over their zone fill (design.md, The World &gt; Structures): each kind its baked
    /// Synty model (<see cref="StructureLibrary"/>, matched to the sim's kinds by id) standing in the structure field
    /// (<see cref="StructureRenderer"/>), flashing when hit, and on its break's frame shattering into crack-outlined chunks
    /// that fly in their own physics (<see cref="StructureShatter"/>). A kind without a bake stands unseen, with a warning.
    /// </summary>
    public sealed class EdgeStructures : IStructureView
    {
        public const int CapacityPerType = 512;

        readonly StructureRenderer _field;
        readonly StructureShatter _shatter;
        readonly Dictionary<string, int> _typeOfKind = new Dictionary<string, int>();

        /// <summary>
        /// Draws <paramref name="lib"/>'s structures for <paramref name="rules"/>' kinds; the pieces fly in
        /// <paramref name="physicsScene"/> when given (an EditMode test's), else in a scene of their own.
        /// </summary>
        public EdgeStructures(StructureLibrary lib, StructureRules rules, Scene? physicsScene = null)
        {
            var gains = StructureGains.Default;
            _field = new StructureRenderer(lib, CapacityPerType, gains);
            _shatter = new StructureShatter(lib, gains, physicsScene: physicsScene);
            foreach (var kind in rules.Kinds)
            {
                int type = lib.IndexOf(kind.Id);
                _typeOfKind[kind.Id] = type;
                if (type < 0) Debug.LogWarning($"EdgeStructures: no baked {kind.Id} (champions structure bake {kind.Id}): it stands unseen");
            }
        }

        /// <summary>The structures on <paramref name="look"/>'s baked kinds, or null when none is baked (a checkout without the vendor files).</summary>
        public static EdgeStructures Create(MonsterLook look, StructureRules rules)
        {
            var lib = StructureLibrary.Load(look);
            return lib == null ? null : new EdgeStructures(lib, rules);
        }

        public StructureRenderer Field => _field;
        public StructureShatter Shatter => _shatter;

        public void Stand(int id, StructureKind kind, Vector2 at, float angle)
        {
            int type = _typeOfKind.TryGetValue(kind.Id, out int t) ? t : -1;
            if (type >= 0) _field.Add(id, type, at, angle);
        }

        public bool Remove(int id) => _field.Remove(id);

        public void Hit(int id, double t) => _field.Flash(id, t, 1f);

        public bool Break(int id, double t, Vector2 blow, float power)
        {
            if (!_field.TryGet(id, out int type, out var standing)) return false;
            _field.Remove(id);
            _shatter.Launch(type, standing.Position, standing.Angle, t, blow, power);
            return true;
        }

        public void Present(double song, Camera camera)
        {
            _shatter.Present(song);
            _field.Draw(camera);
            _shatter.Draw(camera);
        }

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
