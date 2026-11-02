using System;
using Champions.Render;
using Champions.Render.Telegraphs;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client
{
    /// <summary>
    /// Maps the sim's telegraphs onto the ground's marks (<see cref="TelegraphRenderer"/>; design.md, Bosses &gt; Every boss
    /// and The Horde &gt; Threats): each boss mark (<see cref="SimEventKind.BossTelegraph"/>) in the palette's
    /// <c>boss.telegraph</c> red, each big ork's charge and troll's slam (<see cref="SimEventKind.EliteTelegraph"/>) in its
    /// <c>warning</c> yellow, filling from its start to its beat; a landing flashes it (a breath's burn holding its glow and
    /// sweeping when it sweeps), a call-off fades it. A mushroom's spore has no ground mark (its cap flashes). The assembly's
    /// EventDispatch hands it the events.
    /// </summary>
    public sealed class TelegraphPresenter : IDisposable
    {
        /// <summary>How far past the palette's colour a mark's lines are drawn (linear, for the bloom).</summary>
        public const float Intensity = 2.5f;

        readonly TelegraphRenderer _marks;
        readonly EnemyRules _enemies;
        readonly Color _boss, _warning;
        readonly double _orkRadius;

        public TelegraphPresenter(Palette palette, EnemyRules enemies, int layer = 0)
        {
            _marks = new TelegraphRenderer(layer);
            _enemies = enemies;
            _boss = palette.BossTelegraph.linear * Intensity;
            _warning = palette.Warning.linear * Intensity;
            int ork = Array.FindIndex(enemies.Cast, t => t.Move == EliteMove.Charge);
            _orkRadius = ork >= 0 ? enemies.Cast[ork].RadiusM : 0.8;
        }

        public TelegraphRenderer Marks => _marks;

        /// <summary>One settled event, in log order.</summary>
        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.BossTelegraph:
                    _marks.Add(e.I0, e.MarkGround(), e.T, e.D0, _boss);
                    break;
                case SimEventKind.BossMove:
                    if (e.I2 != 0) _marks.CallOff(e.I0, e.T);
                    else _marks.Land(e.I0, e.T, e.MarkMove == BossMove.Breath ? e.D4 : 0, e.D5);
                    break;
                case SimEventKind.EliteTelegraph:
                {
                    var move = (EliteMove)e.I1;
                    // A mob has one move at a time: its marks are numbered below the bosses'.
                    int id = -1 - e.I0;
                    if (move == EliteMove.Charge)
                    {
                        double length = _enemies.Charge.LengthM;
                        _marks.Add(id, HitShape.Path(e.D2, e.D3, e.D2 + e.D4 * length, e.D3 + e.D5 * length, _orkRadius), e.D0, e.D1, _warning);
                    }
                    else if (move == EliteMove.Slam) _marks.Add(id, HitShape.Circle(e.D2, e.D3, _enemies.Slam.RadiusM), e.D0, e.D1, _warning);
                    break;
                }
                case SimEventKind.EliteMove:
                    if (e.I2 != 0) _marks.CallOff(-1 - e.I0, e.T);
                    else _marks.Land(-1 - e.I0, e.T);
                    break;
            }
        }

        /// <summary>The frame at song time <paramref name="song"/>: the marks queue for <paramref name="camera"/>.</summary>
        public void Present(double song, Camera camera) => _marks.Present(song, camera);

        /// <summary>Queues this frame's marks for the camera again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera) => _marks.Queue(camera);

        public void Dispose() => _marks.Dispose();
    }
}
