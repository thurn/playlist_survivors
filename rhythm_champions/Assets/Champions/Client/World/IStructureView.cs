using System;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// The structures' drawing (plan.md, The Client: presenters built against small command interfaces):
    /// <see cref="StructurePresenter"/> stands, hits, breaks and unloads them by the sim's ids, then has it draw each frame.
    /// <see cref="EdgeStructures"/> draws them in the edge look over their zone fill; <see cref="StandInStructures"/> draws
    /// blocks and cylinders on their footprints without baked structures.
    /// </summary>
    public interface IStructureView : IDisposable
    {
        /// <summary>Structure <paramref name="id"/> of <paramref name="kind"/> stands at <paramref name="at"/> (ground x, z), turned <paramref name="angle"/> (the sim's: radians from +x toward +y).</summary>
        void Stand(int id, StructureKind kind, Vector2 at, float angle);

        /// <summary>Takes structure <paramref name="id"/> away (its chunk unloaded); false if it wasn't standing.</summary>
        bool Remove(int id);

        /// <summary>Structure <paramref name="id"/> was hit at song time <paramref name="t"/> and still stands: it flashes.</summary>
        void Hit(int id, double t);

        /// <summary>
        /// Structure <paramref name="id"/> breaks on this frame, at song time <paramref name="t"/>: it goes, thrown along
        /// <paramref name="blow"/> (a ground unit vector) as hard as <paramref name="power"/> (1 a strong blow's); false if it
        /// wasn't standing.
        /// </summary>
        bool Break(int id, double t, Vector2 blow, float power);

        /// <summary>The frame at song time <paramref name="song"/>, drawn for <paramref name="camera"/>.</summary>
        void Present(double song, Camera camera);

        /// <summary>Queues the frame's draws again (the stills' second drawing of a frame).</summary>
        void Queue(Camera camera);
    }
}
