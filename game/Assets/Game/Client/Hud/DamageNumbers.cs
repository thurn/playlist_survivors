using System;
using System.Collections.Generic;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// Damage numbers over the enemies (native's slice/damage.rs), times 10: each hit shows when
    /// frameSong reaches it, summed per enemy and beat into one number that rises from the enemy,
    /// pops as it grows, and fades over <see cref="HoldS"/> after its last hit. A heavy strike's
    /// number is gold and larger. Past <see cref="Dense"/> numbers the newest go first, at most 12
    /// near the hero and 20 farther out, one to a screen cell, the far plain ones dimmer. Positions
    /// project the world through the camera into the panel, so they follow the camera's moves.
    /// </summary>
    public sealed class DamageNumbers
    {
        public const double HoldS = 0.75;
        public const int Cap = 128, Dense = 32, Labels = 40;
        const double NearM = 6, RiseM = 0.6, BaseM = 1.5, FreeBeatS = 0.5;

        struct Number
        {
            public int Foe;
            public long Beat;
            public double Value, X, Y, Born, Last;
            public bool Heavy;
            public string Text;
        }

        readonly VisualElement _layer;
        readonly Camera _camera;
        readonly List<Number> _live = new List<Number>(Cap);
        readonly List<HitEvent> _pending = new List<HitEvent>();
        readonly List<(double T, int Index, double Period)> _beats = new List<(double, int, double)>();
        readonly Label[] _labels = new Label[Labels];
        readonly HashSet<long> _cells = new HashSet<long>();

        public DamageNumbers(VisualElement layer, Camera camera)
        {
            _layer = layer;
            _camera = camera;
            for (int i = 0; i < Labels; i++)
            {
                var l = LevelUpCards.Text("", "number");
                l.style.display = DisplayStyle.None;
                _layer.Add(l);
                _labels[i] = l;
            }
        }

        public void Add(in HitEvent hit)
        {
            if (hit.Damage > 0 && !double.IsNaN(hit.X)) _pending.Add(hit);
        }

        public void Beat(double t, int index, double period)
        {
            _beats.Add((t, index, period));
            if (_beats.Count > 64) _beats.RemoveAt(0);
        }

        /// <summary>
        /// The beat a hit at <paramref name="t"/> sums into: the latest beat at or before it while
        /// that beat continues (1.5 periods), else a free half-second bucket.
        /// </summary>
        public static long BeatKey(IReadOnlyList<(double T, int Index, double Period)> beats, double t)
        {
            for (int i = beats.Count - 1; i >= 0; i--)
            {
                if (beats[i].T > t + 1e-9) continue;
                if (t - beats[i].T <= 1.5 * Math.Max(0.1, beats[i].Period)) return beats[i].Index;
                break;
            }
            return -1 - (long)Math.Floor(t / FreeBeatS);
        }

        public void Present(double s, double heroX, double heroY)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].T > s + 1e-9) continue;
                Merge(_pending[i]);
                _pending.RemoveAt(i--);
            }
            _live.RemoveAll(n => s - n.Last >= HoldS);
            Draw(s, heroX, heroY);
        }

        void Merge(in HitEvent hit)
        {
            long beat = BeatKey(_beats, hit.T);
            for (int i = 0; i < _live.Count; i++)
            {
                var n = _live[i];
                if (n.Foe != hit.Target || n.Beat != beat) continue;
                n.Value += hit.Damage;
                n.Heavy |= hit.WeaponHit == WeaponHit.HeavyStrike;
                n.Last = hit.T;
                n.X = hit.X;
                n.Y = hit.Y;
                n.Text = HudText.Damage(n.Value);
                _live[i] = n;
                return;
            }
            if (_live.Count >= Cap) _live.RemoveAt(0);
            _live.Add(new Number
            {
                Foe = hit.Target, Beat = beat, Value = hit.Damage, X = hit.X, Y = hit.Y, Born = hit.T, Last = hit.T,
                Heavy = hit.WeaponHit == WeaponHit.HeavyStrike, Text = HudText.Damage(hit.Damage),
            });
        }

        void Draw(double s, double heroX, double heroY)
        {
            float w = _layer.layout.width, h = _layer.layout.height;
            int used = 0, near = 0, far = 0;
            bool dense = _live.Count > Dense;
            _cells.Clear();
            for (int k = 0; k < _live.Count && used < Labels && _camera != null && w > 0; k++)
            {
                var n = _live[dense ? _live.Count - 1 - k : k];
                bool isNear = Math.Sqrt((n.X - heroX) * (n.X - heroX) + (n.Y - heroY) * (n.Y - heroY)) <= NearM;
                if (dense && ((isNear && near >= 12) || (!isNear && far >= 20))) continue;
                double age = Math.Max(0, s - n.Last);
                float rise = (float)(BaseM + Math.Min(s - n.Born, 1.2) * RiseM);
                var v = _camera.WorldToViewportPoint(new Vector3((float)n.X, rise, (float)n.Y));
                if (v.z <= 0) continue;
                float x = v.x * w, y = (1 - v.y) * h;
                if (dense && !_cells.Add(((long)Mathf.FloorToInt(x / 128) << 32) ^ (uint)Mathf.FloorToInt(y / 64))) continue;
                if (isNear) near++;
                else far++;
                float fade = Mathf.Min(1, (float)(1 - age / HoldS)) * (dense && !isNear && !n.Heavy ? 0.55f : 1);
                float pop = 1 + 0.25f * Mathf.Exp(-(float)age * 18);
                var l = _labels[used++];
                l.style.display = DisplayStyle.Flex;
                if (l.text != n.Text) l.text = n.Text;
                l.EnableInClassList("number--heavy", n.Heavy);
                l.style.left = x;
                l.style.top = y;
                l.style.opacity = fade;
                l.style.scale = new Scale(new Vector3(pop, pop, 1));
            }
            for (int i = used; i < Labels; i++) _labels[i].style.display = DisplayStyle.None;
        }
    }
}
