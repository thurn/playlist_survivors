using System;
using System.Collections.Generic;
using System.Globalization;

namespace Champions.Sim
{
    /// <summary>
    /// Deterministic input routines shared by the client and headless tuning. Reference follows the
    /// survivor pickup route, reacts to crowds and telegraphs, and voluntarily dashes to the beat.
    /// Positioning uses the same movement without deliberate rhythm inputs in horde segments. Both
    /// cross traveling boss fronts on arrival, not launch. Idle never moves; key schedules use
    /// right@9-11,up@11-12.5,space@14-14.1. Picks are 1,3,2 (last repeats), or reference preferences.
    /// Archived content retains its historical reference routine and preferences.
    /// Each frame takes settled events before Frame, using only client-visible state.
    /// </summary>
    public sealed class ScriptedPlayer
    {
        public enum Routine { Keys, Idle, Reference }

        /// <summary>The reference player's ring round the start, m.</summary>
        public const double RingM = 15;
        /// <summary>In a duel it walks back to within this of the boss, stopping this far short.</summary>
        public const double ReachM = 4.5, ShortM = 3;
        /// <summary>An elite's move within this of the hero is dodged; a spore passing within this is dashed through.</summary>
        public const double ThreatM = 10, SporeMissM = 1.2;
        /// <summary>The horde's beat dash waits when a threat lands within this after it.</summary>
        public const double KeepDashS = 0.75;

        static readonly (string Name, Key Key)[] KeyNames =
        {
            ("up", Key.Up), ("down", Key.Down), ("left", Key.Left), ("right", Key.Right), ("space", Key.Space), ("enter", Key.Enter),
        };

        /// <summary>The reference build's preferences, best first: dashes by id, attacks and mods by their content ids.</summary>
        public static readonly string[] ReferencePreferences =
        {
            "lifeblood", "bladeRush", "swordSlash", "orbitBlades", "keenEdge", "wideArcs", "bladeRain", "quickstep", "momentum",
            "twinStrike", "crescent", "phantom", "tempo", "shatterburst", "encore", "afterimage", "leapSlam", "shieldCharge", "blinkStrike",
        };

        public static readonly string[] SurvivorPreferences =
            { "sword", "might", "regeneration", "armor", "tempo", "reach", "bow", "hammer", "orbitBlades", "lightning", "sentinel" };

        readonly Pickup[] _nearPickups = new Pickup[256];
        readonly Routine _routine;
        readonly List<(double At, Key Key, bool Down)> _keys = new List<(double, Key, bool)>();
        readonly int[] _pickList;
        readonly bool _pickReference;
        readonly DashRules _dashes;
        readonly List<double> _landings = new List<double>(), _predicted = new List<double>(), _planned = new List<double>();
        int _nextKey, _picks, _beats;
        double _headX = 1, _headY, _pendingBeat = double.NaN, _lastTarget = double.NegativeInfinity, _pressedFor = double.NaN;
        bool _released = true;

        ScriptedPlayer(Routine routine, int[] picks, bool pickReference, DashRules dashes)
        {
            _routine = routine;
            _pickList = picks;
            _pickReference = pickReference;
            _dashes = dashes;
            Preferences = dashes.LegacyForms ? ReferencePreferences : SurvivorPreferences;
            if (!dashes.LegacyForms) { Danger = 0; Ring = 12; }
        }

        public Routine Kind => _routine;

        /// <summary>The reference player's ring (tuning experiments change it).</summary>
        public double Ring = RingM;

        /// <summary>The picks' preferences, best first (tuning experiments change them).</summary>
        public string[] Preferences = ReferencePreferences;

        /// <summary>Disable voluntary rhythm inputs in horde segments for survivor balance checks.</summary>
        public bool HordeRhythm = true;

        /// <summary>Favor acquiring distinct weapons before deep upgrades, for broad-build checks.</summary>
        public bool PreferNewWeapons;

        /// <summary>The kite: monsters within Danger m push it away by Flee against its heading's weight Inertia (tuning).</summary>
        public double Danger = 3, Flee = 3, Inertia = 1;

        /// <summary>The cards it picked.</summary>
        public int Picks => _picks;

        /// <summary>
        /// The script `--walk` <paramref name="walk"/> and `--picks` <paramref name="picks"/> name (either may be null, not
        /// both); the dashes name the dash cards for the reference's preferences. Throws ArgumentException on a bad one.
        /// </summary>
        public static ScriptedPlayer Parse(string walk, string picks, DashRules dashes)
        {
            if (walk == null && picks == null) throw new ArgumentException("a scripted player needs --walk or --picks");
            var routine = walk == null ? Routine.Keys
                : walk == "idle" ? Routine.Idle
                : walk == "reference" || walk == "positioning" ? Routine.Reference
                : Routine.Keys;
            bool reference = picks == "reference" || picks == null && routine != Routine.Idle;
            int[] list = null;
            if (picks != null && picks != "reference")
            {
                var parts = picks.Split(',');
                list = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out list[i]) || list[i] < 1 || list[i] > Offer.Cards)
                        throw new ArgumentException($"--picks: '{parts[i]}' is not a card's number (1-{Offer.Cards}) or 'reference'");
            }
            else if (!reference) list = new[] { 1 };
            var player = new ScriptedPlayer(routine, list, reference, dashes ?? new DashRules());
            if (walk == "positioning") player.HordeRhythm = false;
            if (routine == Routine.Keys && walk != null) player.ParseKeys(walk);
            return player;
        }

        void ParseKeys(string schedule)
        {
            foreach (var item in schedule.Split(','))
            {
                var parts = item.Trim().Split('@');
                int i = parts.Length == 2 ? Array.FindIndex(KeyNames, n => n.Name == parts[0].Trim()) : -1;
                var span = i >= 0 ? parts[1].Split('-') : null;
                if (span == null || span.Length != 2
                    || !double.TryParse(span[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double from)
                    || !double.TryParse(span[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double to) || to < from)
                    throw new ArgumentException($"--walk: '{item}' is not idle, reference, or a key ({string.Join(", ", Array.ConvertAll(KeyNames, n => n.Name))}) @ from-to seconds");
                _keys.Add((from, KeyNames[i].Key, true));
                _keys.Add((to, KeyNames[i].Key, false));
            }
            _keys.Sort((a, b) => a.At.CompareTo(b.At));
        }

        /// <summary>
        /// One settled event of <paramref name="sim"/>'s, in log order: the beats it dashes on, the landings it dodges (a
        /// boss's, an elite's near it) and the spores it sees coming, and the telegraphs it keeps its dash for.
        /// </summary>
        public void Take(Simulation sim, in SimEvent e)
        {
            if (_routine != Routine.Reference) return;
            switch (e.Kind)
            {
                case SimEventKind.Beat when e.BeatReliability == Reliability.Reliable:
                    // Every other beat, heard exactly on it.
                    if (++_beats % 2 == 0) _pendingBeat = e.T;
                    break;
                case SimEventKind.Pulse when e.I0 < 0:
                    // No reliable beat: the attacks' seconds pulse instead (every press counts as on the beat then).
                    _pendingBeat = e.T;
                    break;
                case SimEventKind.BossTelegraph:
                    _planned.Add(e.D0);
                    break;
                case SimEventKind.EliteTelegraph when Near(sim, e.T, e.D2, e.D3, ThreatM):
                    _planned.Add(e.D1);
                    break;
                case SimEventKind.BossMove when e.I2 == 0 && !Bosses.IsWave((BossMove)(e.I1 & 255)):
                case SimEventKind.EliteMove when e.I2 == 0 && Near(sim, e.T, e.D0, e.D1, ThreatM):
                    // Each landing, dodged on its beat once it is heard (a hit settles after the inputs heard by then, so
                    // the press still counts); parts of one move landing together count once.
                    if (!_landings.Contains(e.T)) _landings.Add(e.T);
                    break;
                case SimEventKind.Spore:
                {
                    // Where it passes the hero (going on as it goes now): dashed through just before, if it comes within a metre.
                    sim.HeroWas(e.T, out double hx, out double hy);
                    sim.HeroWas(e.T - 0.1, out double px, out double py);
                    double vx = e.D2 - (hx - px) / 0.1, vy = e.D3 - (hy - py) / 0.1;
                    double rx = e.D0 - hx, ry = e.D1 - hy, v2 = vx * vx + vy * vy;
                    if (v2 < 1e-9) break;
                    double tc = -(rx * vx + ry * vy) / v2;
                    if (tc < 0 || tc * Math.Sqrt(e.D2 * e.D2 + e.D3 * e.D3) > e.D4) break;
                    double mx = rx + vx * tc, my = ry + vy * tc;
                    if (mx * mx + my * my > SporeMissM * SporeMissM) break;
                    _predicted.Add(e.T + tc - 0.08);
                    _planned.Add(e.T + tc);
                    break;
                }
            }
        }

        static bool Near(Simulation sim, double t, double x, double y, double within)
        {
            sim.HeroWas(t, out double hx, out double hy);
            return (x - hx) * (x - hx) + (y - hy) * (y - hy) <= within * within;
        }

        /// <summary>The frame's inputs, the player hearing song time <paramref name="heard"/> now.</summary>
        public void Frame(Simulation sim, double heard)
        {
            var s = sim.State;
            if (!_released)
            {
                ReleaseAll(sim, heard);
                _released = true;
            }
            if (s.Phase == RunPhase.Ended) return;
            if (s.Offer.Up)
            {
                Pick(sim, s.Offer, heard);
                return;
            }
            if (s.Held) return;
            switch (_routine)
            {
                case Routine.Keys:
                    for (; _nextKey < _keys.Count && _keys[_nextKey].At <= heard + 1e-9; _nextKey++)
                    {
                        var k = _keys[_nextKey];
                        if (k.Down) sim.Press(k.Key, k.At);
                        else sim.Release(k.Key, k.At);
                    }
                    break;
                case Routine.Reference:
                    Reference(sim, heard);
                    break;
            }
        }

        void Pick(Simulation sim, in Offer offer, double heard)
        {
            int slot;
            if (_pickReference) slot = Preferred(offer);
            else slot = Math.Min(offer.Count, _pickList[Math.Min(_picks, _pickList.Length - 1)]) - 1;
            var key = (Key)((int)Key.One + slot);
            sim.Press(key, heard);
            sim.Release(key, heard);
            _picks++;
        }

        /// <summary>The card the reference build wants most: its thing's place in <see cref="ReferencePreferences"/>, evolutions first.</summary>
        int Preferred(in Offer offer)
        {
            int best = 0;
            double bestScore = double.MaxValue;
            for (int i = 0; i < offer.Count; i++)
            {
                var c = offer[i];
                string id = c.Kind == CardKind.Weapon ? CombatRules.Id(c.Weapon) : c.Kind == CardKind.Support ? CombatRules.Id(c.Support) : c.Kind == CardKind.Dash ? (c.Id < _dashes.Kinds.Length ? _dashes.Kinds[c.Id].Id : "")
                    : c.Kind == CardKind.Attack ? Upgrade.IdOf(c.Attack) : Upgrade.IdOf(c.Mod);
                var preferences = Preferences;
                int place = Array.IndexOf(preferences, id);
                double score = (PreferNewWeapons && c.Kind == CardKind.Weapon && c.New ? -20 : 0) + (place < 0 ? preferences.Length : place) - (c.Evolution ? 100 : 0) - 0.1 * c.Rank;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        void Reference(Simulation sim, double heard)
        {
            var hero = sim.State.Hero;
            bool duel = sim.Bosses.InDuel && sim.Bosses.Id >= 0;
            // A dodge: a landing as soon as the sim knows it (a player sees it coming over its telegraph's beats), pressed on
            // it, or a spore just before it passes; if the dash is ready then (in a duel through the boss).
            _landings.Sort();
            _predicted.Sort();
            while (_landings.Count > 0 && _landings[0] <= sim.Now + 1e-9 || _predicted.Count > 0 && _predicted[0] <= heard + 1e-9)
            {
                bool landing = _landings.Count > 0 && _landings[0] <= sim.Now + 1e-9;
                var list = landing ? _landings : _predicted;
                double at = list[0];
                list.RemoveAt(0);
                if (at == _pressedFor || hero.DashReadyAt > at + 1e-9) continue;
                _pressedFor = at;
                if (duel)
                {
                    // Through the boss: its reach is all round it, and past it is where the dash ends clear.
                    ref readonly var b = ref sim.Enemies[sim.Bosses.Id];
                    sim.Target(b.X, b.Y, at);
                }
                Dash(sim, at);
                return;
            }
            _planned.RemoveAll(t => t < heard - 1);
            if (duel)
            {
                // Traveling fronts must be crossed when they reach the hero, rather than at their launch cue.
                foreach (var mark in sim.Bosses.Marks)
                {
                    double soon = sim.Now + 0.05;
                    if (!Bosses.IsWave(mark.Move) || soon < sim.Telegraphs.LandTime(mark.Telegraph)
                        || hero.DashReadyAt > sim.Now) continue;
                    sim.HeroWas(sim.Now, out double px, out double py);
                    if (!sim.Bosses.WaveShape(mark, soon).Touches(px, py, 0.6)) continue;
                    double wx = mark.Move == BossMove.Wall ? 0 : mark.Shape.X - px;
                    double wy = mark.Move == BossMove.Wall ? -1 : mark.Shape.Y - py;
                    double length = Math.Max(0.01, Math.Sqrt(wx * wx + wy * wy));
                    sim.Target(px + wx / length * 8, py + wy / length * 8, heard);
                    Dash(sim, heard);
                    return;
                }
                if (heard - _lastTarget < 0.25) return;
                _lastTarget = heard;
                ref readonly var b = ref sim.Enemies[sim.Bosses.Id];
                sim.HeroWas(heard, out double hx, out double hy);
                double dx = b.X - hx, dy = b.Y - hy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > ReachM) sim.Target(b.X - dx / d * ShortM, b.Y - dy / d * ShortM, heard);
                return;
            }
            if (!_dashes.LegacyForms && hero.DashReadyAt <= sim.Now)
            {
                sim.HeroWas(sim.Now, out double px, out double py);
                bool crowded = false;
                for (int i = 0; i < sim.Enemies.Count; i++)
                {
                    var m = sim.Enemies[i];
                    double dx = m.X - px, dy = m.Y - py;
                    if (m.Alive && dx * dx + dy * dy < 1.6 * 1.6) { crowded = true; break; }
                }
                if (crowded)
                {
                    double best = double.NegativeInfinity, tx = px, ty = py;
                    for (int direction = 0; direction < 16; direction++)
                    {
                        double angle = direction * Math.PI / 8;
                        double x = px + Math.Cos(angle) * 4, y = py + Math.Sin(angle) * 4;
                        double clearance = 100;
                        for (int i = 0; i < sim.Enemies.Count; i++)
                        {
                            var m = sim.Enemies[i];
                            if (!m.Alive) continue;
                            double dx = m.X - x, dy = m.Y - y;
                            clearance = Math.Min(clearance, dx * dx + dy * dy);
                        }
                        if (clearance > best) { best = clearance; tx = x; ty = y; }
                    }
                    sim.Target(tx, ty, heard);
                    Dash(sim, heard);
                    return;
                }
            }
            // The horde: away from the monsters within Danger m, keeping its heading where none is, ten times a second,
            // and a dash on every other beat once it is heard.
            if (heard - _lastTarget >= 0.1)
            {
                _lastTarget = heard;
                sim.HeroWas(heard, out double x, out double y);
                double fx = 0, fy = 0;
                for (int id = 0; id < sim.Enemies.Count; id++)
                {
                    ref readonly var m = ref sim.Enemies[id];
                    if (!m.Alive) continue;
                    double dx = x - m.X, dy = y - m.Y, d2 = dx * dx + dy * dy;
                    if (d2 > Danger * Danger || d2 < 1e-6) continue;
                    fx += dx / d2;
                    fy += dy / d2;
                }
                // Pulled back toward a ring round the start, so it circles there instead of wandering off.
                double r = Math.Sqrt(x * x + y * y);
                double tx = r > 0.5 ? -y / r : 1, ty = r > 0.5 ? x / r : 0;
                double pull = (Ring - r) / Ring;
                double hx = Inertia * _headX + tx * 0.5 + (r > 0.5 ? x / r * pull : 0), hy = Inertia * _headY + ty * 0.5 + (r > 0.5 ? y / r * pull : 0);
                if (!_dashes.LegacyForms)
                {
                    // Route through visible cleared ground instead of abandoning all growth behind the kite.
                    int count = sim.Pickups.Preview(x, y, 12, _nearPickups);
                    double best = double.PositiveInfinity, qx = 0, qy = 0;
                    for (int i = 0; i < count; i++)
                    {
                        var p = _nearPickups[i];
                        double ax = p.X - x, ay = p.Y - y, d2 = ax * ax + ay * ay;
                        if (d2 < 9 || d2 >= best) continue;
                        best = d2; qx = ax; qy = ay;
                    }
                    if (best < double.PositiveInfinity)
                    {
                        double distance = Math.Sqrt(best);
                        hx = qx / distance * 2; hy = qy / distance * 2;
                    }
                }
                double f = Math.Sqrt(fx * fx + fy * fy);
                if (f > 0) { hx += Flee * fx / f; hy += Flee * fy / f; }
                double h = Math.Sqrt(hx * hx + hy * hy);
                if (h > 1e-6)
                {
                    _headX = hx / h;
                    _headY = hy / h;
                }
                sim.Target(x + 3 * _headX, y + 3 * _headY, heard);
            }
            if (HordeRhythm && !double.IsNaN(_pendingBeat) && _pendingBeat <= heard + 1e-9)
            {
                double beat = _pendingBeat;
                _pendingBeat = double.NaN;
                bool keep = _planned.Exists(t => t >= beat && t <= beat + KeepDashS);
                if (!keep && hero.DashReadyAt <= beat + 1e-9) Dash(sim, beat);
            }
        }

        void Dash(Simulation sim, double heard)
        {
            sim.Press(Key.Space, heard);
            _released = false;
        }

        void ReleaseAll(Simulation sim, double heard) => sim.Release(Key.Space, heard);
    }
}
