using System;
using Game.Client.Look;
using Game.Client.Loop;
using PS.Game.Sim;
using PS.Look;

namespace Game.Client.Structures
{
    /// <summary>
    /// The sim's structure events on screen (unity-game.md, "Crystal Structures" and "Shatter"): each break, regrowth
    /// and glint passed to the look package's <see cref="StructureShatter"/> as it settles, ahead of its frame, a
    /// break's throw as its power (1 at a heavy strike's, <see cref="FeelMoments.HeavyThrowM"/>), and the shatter brought
    /// to each frame's song time. Its breaks and regrowths mark their moments when they show.
    /// </summary>
    public sealed class StructurePresenter : IPresenter, IDisposable
    {
        public readonly StructureShatter Shatter;

        public StructurePresenter(StructureShatter shatter)
        {
            Shatter = shatter;
            Shatter.Broke += (t, site) => Moments.Mark(MomentKind.Break, t, site);
            Shatter.Regrew += (t, site) => Moments.Mark(MomentKind.Regrow, t, site);
        }

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.StructureBreak:
                {
                    var b = e.AsStructureBreak;
                    Shatter.Break(e.T, b.Structure, new UnityEngine.Vector3((float)b.X, 0, (float)b.Y),
                        new UnityEngine.Vector3((float)b.DirX, 0, (float)b.DirY), (float)(b.Force / FeelMoments.HeavyThrowM));
                    break;
                }
                case SimEventKind.Regrowth:
                {
                    var r = e.AsRegrowth;
                    Shatter.Regrow(r.Start, r.End, r.Structure);
                    break;
                }
                case SimEventKind.Glint:
                {
                    var g = e.AsGlint;
                    Shatter.Glint(e.T, g.Structure, g.Hits > 0);
                    break;
                }
            }
        }

        public void Present(in PresentFrame frame) => Shatter.Present(frame.Song);

        public void Dispose() => Shatter.Dispose();
    }
}
