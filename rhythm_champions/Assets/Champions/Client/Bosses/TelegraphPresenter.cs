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
    /// <c>warning</c> orange, filling from its start to its beat; a landing flashes it (a breath's burn holding its glow and
    /// sweeping when it sweeps), a call-off fades it. A Boss Rush fight's hazards (<see cref="BossFight"/>) are boss marks
    /// too: the same danger red, held through their active span, reshaped as they travel, grow and turn. A mushroom's spore has no ground mark (its cap flashes). The assembly's
    /// EventDispatch hands it the events.
    /// </summary>
    public sealed class TelegraphPresenter : IDisposable
    {
        /// <summary>How far past the palette's colour a mark's lines are drawn (linear, for the bloom).</summary>
        public const float Intensity = 2.5f;

        const int ArrivalId = int.MinValue;
        bool _arrival, _arrivalShown;
        double _calledAt;
        bool _held;
        double _heldAt;
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
                case SimEventKind.BossCall:
                    _arrival = true; _arrivalShown = false; _calledAt = e.T;
                    break;
                case SimEventKind.BossArrive:
                    _arrival = false;
                    if (_arrivalShown) _marks.CallOff(ArrivalId, e.T);
                    break;
                case SimEventKind.Hold:
                    bool held = e.I2 != 0;
                    if (held && !_held) _heldAt = e.T;
                    if (!held && _held)
                    {
                        _marks.Shift(e.T - _heldAt);
                        if (_arrival) _calledAt += e.T - _heldAt;
                    }
                    _held = held;
                    break;
                case SimEventKind.BossTelegraph:
                    _marks.Add(e.I0, e.MarkGround(), e.T, e.D0, e.MarkMove == BossMove.Reinforcements ? _warning : _boss);
                    break;
                case SimEventKind.BossMove:
                    if (e.I2 != 0) _marks.CallOff(e.I0, e.T);
                    // A breath, a wave and a Boss Rush hazard hold their glow for their active span (D4).
                    else _marks.Land(e.I0, e.T, e.MarkMove == BossMove.Breath || Bosses.IsWave(e.MarkMove) || e.I0 >= BossFight.FirstMarkId ? e.D4 : 0, e.D5);
                    break;
                case SimEventKind.EliteTelegraph:
                {
                    var move = (EliteMove)e.I1;
                    // A mob has one move at a time: its marks are numbered below the bosses'.
                    int id = -1 - e.I0;
                    if (move == EliteMove.Charge)
                    {
                        double length = _enemies.Charge.LengthM;
                        _marks.Add(id, HitShape.Path(e.D2, e.D3, e.D2 + e.D4 * length, e.D3 + e.D5 * length, _orkRadius), e.D0, e.D1, _warning, warning: true);
                    }
                    else if (move == EliteMove.Slam) _marks.Add(id, HitShape.Circle(e.D2, e.D3, _enemies.Slam.RadiusM), e.D0, e.D1, _warning, warning: true);
                    break;
                }
                case SimEventKind.EliteMove:
                    if (e.I2 != 0) _marks.CallOff(-1 - e.I0, e.T);
                    else _marks.Land(-1 - e.I0, e.T);
                    break;
            }
        }

        /// <summary>The frame at song time <paramref name="song"/>: the marks queue for <paramref name="camera"/>.</summary>
        public void Present(double song, Camera camera, Simulation sim = null)
        {
            if (sim != null && !_held)
            {
                if (_arrival)
                {
                    sim.HeroWas(song, out double x, out double y);
                    var warning = HitShape.Ring(x, y + sim.Bosses.Rules.DistanceM, 2.1, 2.4);
                    if (!_arrivalShown)
                    {
                        _marks.Add(ArrivalId, warning, _calledAt, _calledAt + sim.Bosses.Rules.WaitS, _warning);
                        _arrivalShown = true;
                    }
                    else _marks.Reshape(ArrivalId, warning);
                }
                if (sim.Bosses.ReinforcementWarning is BossMark group)
                    _marks.Retime(group.Id, group.Telegraph.Start, sim.Telegraphs.LandTime(group.Telegraph));
                foreach (var m in sim.Bosses.Marks)
                {
                    _marks.Retime(m.Id, m.Telegraph.Start, sim.Telegraphs.LandTime(m.Telegraph));
                    if (Bosses.IsWave(m.Move)) _marks.Reshape(m.Id, sim.Bosses.WaveShape(m, song));
                }
                // A Boss Rush fight's hazards: retimed, and reshaped as they travel, grow and turn.
                foreach (var h in sim.Fight.Hazards)
                {
                    _marks.Retime(h.Id, h.Telegraph.Start, h.Telegraph.Land);
                    if (h.Vx != 0 || h.Vy != 0 || h.GrowMps != 0 || h.SpinRadPerS != 0) _marks.Reshape(h.Id, h.ShapeAt(song));
                }
                var mobs = sim.Horde.Mobs;
                for (int i = 0; i < mobs.Length; i++)
                    if (mobs[i].Owned && mobs[i].Stage == MoveStage.Telegraph)
                        _marks.Retime(-1 - i, mobs[i].Telegraph.Start, sim.Telegraphs.LandTime(mobs[i].Telegraph));
            }
            _marks.Present(_held ? Math.Min(song, _heldAt) : song, camera);
        }

        /// <summary>Queues this frame's marks for the camera again (the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera) => _marks.Queue(camera);

        public void Dispose() => _marks.Dispose();
    }
}
