using System;

namespace PS.Game.Sim
{
    /// <summary>How a music-driven event's enemy moves in place of the horde's pursuit (<see cref="Scripts"/>).</summary>
    internal enum ScriptKind : byte
    {
        /// <summary>The horde's own pursuit down the flow field.</summary>
        None,
        /// <summary>Straight along a heading, its role's step times a scale each step (a swarm, the stampede, a retreat).</summary>
        Line,
        /// <summary>On a ray from a fixed centre, its radius closing on a schedule of main beats (the ring, the flower wall).</summary>
        Ring,
        /// <summary>A place beside the hero, eased in from a start over an entrance of main beats (the kick line, the wedge).</summary>
        Slot,
    }

    /// <summary>
    /// The music-driven events' hold on their enemies (<see cref="MusicEvents"/>), by enemy slot: the
    /// group an enemy belongs to, and the script it moves by instead of the flow field. A scripted
    /// enemy still steps only on main beats its gait steps on, as a step decided at the beat's
    /// decision time (<see cref="Horde"/>), and separation doesn't bend its step. A flier crosses
    /// structures; the rest stop short of them. Knockback and lunges are the horde's, as for any
    /// enemy. A death or a leave clears the slot.
    /// </summary>
    internal sealed class Scripts
    {
        readonly ScriptKind[] kind;
        readonly int[] group;
        readonly bool[] flies;
        // Line: the heading (ax, ay) and the step's scale. Ring: the centre (ax, ay), the ray (bx, by),
        // radii r0 to r1 from beat `from` over `beats`. Slot: the start (ax, ay) and place (bx, by)
        // beside the hero, eased from beat `from` over `beats`.
        readonly double[] ax, ay, bx, by, scale, r0, r1;
        readonly int[] from, beats;

        public Scripts(int cap)
        {
            kind = new ScriptKind[cap];
            group = new int[cap];
            flies = new bool[cap];
            ax = new double[cap];
            ay = new double[cap];
            bx = new double[cap];
            by = new double[cap];
            scale = new double[cap];
            r0 = new double[cap];
            r1 = new double[cap];
            from = new int[cap];
            beats = new int[cap];
            for (int i = 0; i < cap; i++) group[i] = -1;
        }

        /// <summary>The group enemy <paramref name="i"/> belongs to, or -1.</summary>
        public int GroupOf(int i) => group[i];
        public bool Scripted(int i) => kind[i] != ScriptKind.None;
        public bool Flies(int i) => flies[i];

        /// <summary>Enemy <paramref name="i"/> is the horde's alone: it spawned, died or left, or its group let it go.</summary>
        public void Clear(int i)
        {
            kind[i] = ScriptKind.None;
            group[i] = -1;
        }

        /// <summary>Enemy <paramref name="i"/> joins group <paramref name="g"/>, pursuing as the horde does until scripted.</summary>
        public void Join(int i, int g)
        {
            kind[i] = ScriptKind.None;
            group[i] = g;
        }

        public void Line(int i, double dirX, double dirY, double stepScale, bool flier)
        {
            kind[i] = ScriptKind.Line;
            ax[i] = dirX;
            ay[i] = dirY;
            scale[i] = stepScale;
            flies[i] = flier;
        }

        public void Ring(int i, double cx, double cy, double rayX, double rayY, double radius, bool flier)
        {
            kind[i] = ScriptKind.Ring;
            ax[i] = cx;
            ay[i] = cy;
            bx[i] = rayX;
            by[i] = rayY;
            r0[i] = r1[i] = radius;
            from[i] = 0;
            beats[i] = 0;
            flies[i] = flier;
        }

        /// <summary>A ring member's radius closes from where it is now to <paramref name="to"/>, from main beat <paramref name="beat"/> over <paramref name="over"/> beats.</summary>
        public void Close(int i, double to, int beat, int over)
        {
            r0[i] = r1[i];
            r1[i] = to;
            from[i] = beat;
            beats[i] = over;
        }

        public void Slot(int i, double startX, double startY, double placeX, double placeY, int beat, int over, bool flier)
        {
            kind[i] = ScriptKind.Slot;
            ax[i] = startX;
            ay[i] = startY;
            bx[i] = placeX;
            by[i] = placeY;
            from[i] = beat;
            beats[i] = over;
            flies[i] = flier;
        }

        /// <summary>
        /// Where scripted enemy <paramref name="i"/>, at (x, y), steps on main beat <paramref name="beat"/>:
        /// a line's role <paramref name="step"/> along its heading, a ring's radius for the beat, a slot's
        /// place for the beat beside the hero at (hx, hy), the entrance easing out.
        /// </summary>
        public void Target(int i, int beat, double step, double hx, double hy, double x, double y, out double toX, out double toY)
        {
            switch (kind[i])
            {
                case ScriptKind.Line:
                    toX = x + ax[i] * step * scale[i];
                    toY = y + ay[i] * step * scale[i];
                    return;
                case ScriptKind.Ring:
                {
                    double r = r0[i] + (r1[i] - r0[i]) * Progress(i, beat);
                    toX = ax[i] + bx[i] * r;
                    toY = ay[i] + by[i] * r;
                    return;
                }
                default:
                {
                    double u = Progress(i, beat), ease = 1 - (1 - u) * (1 - u);
                    toX = hx + ax[i] + (bx[i] - ax[i]) * ease;
                    toY = hy + ay[i] + (by[i] - ay[i]) * ease;
                    return;
                }
            }
        }

        double Progress(int i, int beat) => beats[i] <= 0 ? 1 : Math.Max(0, Math.Min(1, (beat - from[i]) / (double)beats[i]));
    }
}
