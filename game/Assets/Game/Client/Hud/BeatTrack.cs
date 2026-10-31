using System;
using System.Collections.Generic;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// The beat track (hud.rs): the beats known by frameSong slide in from both sides toward a heart
    /// that beats on each of them, fading in from 2.2 s out and out just past the heart; a downbeat's
    /// shard is taller and warm. Committed beats are revealed only a little ahead of their time, so
    /// past the latest one the track shows the beats its period leads us to expect, dimmer, each
    /// brightening as it is committed. Without a reliable beat the track empties, the heart greys and
    /// WAITING FOR THE BEAT shows above it, rather than a false metronome.
    /// </summary>
    public sealed class BeatTrack
    {
        /// <summary>Pixels a second of approach covers (native's 90 HUD units, doubled), and how far out a beat shows.</summary>
        public const float PxPerS = 180, Reach = 372, Centre = 400, HeartGap = 28;
        /// <summary>An expected beat (not yet committed) shows this much as strongly as a committed one.</summary>
        public const float ExpectedAlpha = 0.4f;
        const int Pool = 48;

        readonly VisualElement _marks, _heart, _waiting;
        readonly List<VisualElement> _pool = new List<VisualElement>(Pool);

        public BeatTrack(VisualElement track)
        {
            _marks = track.Q("marks");
            _heart = track.Q("heart");
            _waiting = track.Q("waiting");
            for (int i = 0; i < Pool; i++)
            {
                var m = SlotsView.Element("mark");
                m.style.display = DisplayStyle.None;
                _marks.Add(m);
                _pool.Add(m);
            }
        }

        /// <summary>Where a beat <paramref name="dt"/> seconds away sits, either side of the centre (px), and how strongly it shows (0 hidden).</summary>
        public static float Offset(double dt) => HeartGap + (float)dt * PxPerS;

        public static float Alpha(double dt)
        {
            if ((float)dt * PxPerS > Reach - HeartGap) return 0;
            float a = Mathf.Clamp01(1 - (float)dt / 2.2f);
            if (dt < 0) a *= Mathf.Max(0, 1 + (float)dt * 8);
            return a;
        }

        public void Show(BeliefLog log, BeatClock clock, double s, in HeardBeat beat, bool locked)
        {
            int used = 0;
            if (locked)
            {
                var beats = log.Beats;
                int known = HeardBeats.KnownBy(log, s);
                for (int i = Math.Max(0, beat.Last - 2); i < known && used + 2 <= Pool; i++)
                {
                    double dt = beats[i].T - s;
                    float a = Alpha(dt);
                    if (a <= 0)
                    {
                        if (dt > 0) break;
                        continue;
                    }
                    bool down = clock.IsDownbeat(i);
                    for (int side = -1; side <= 1; side += 2) Place(_pool[used++], Centre + side * Offset(dt), a, down);
                }
                // Past the latest committed beat, the beats it leads us to expect, at its period and dimmer,
                // with no bar (not yet known): each brightens as it is committed.
                if (known > 0)
                {
                    double last = beats[known - 1].T, period = clock.PeriodOf(known - 1);
                    for (int k = 1; period > 0 && used + 2 <= Pool; k++)
                    {
                        double dt = last + k * period - s;
                        float a = Alpha(dt) * ExpectedAlpha;
                        if (a <= 0)
                        {
                            if (dt > 0) break;
                            continue;
                        }
                        for (int side = -1; side <= 1; side += 2) Place(_pool[used++], Centre + side * Offset(dt), a, false);
                    }
                }
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].style.display = DisplayStyle.None;
            double since = beat.Has ? (s - beat.LastT) / Math.Max(1e-6, beat.Period) : 1;
            float pulse = locked ? Mathf.Exp(-6 * Mathf.Clamp01((float)since)) : 0;
            float scale = 1 + 0.43f * pulse;
            _heart.style.scale = new Scale(new Vector3(scale, scale, 1));
            _heart.EnableInClassList("heart--waiting", !locked);
            _waiting.style.display = locked ? DisplayStyle.None : DisplayStyle.Flex;
        }

        static void Place(VisualElement m, float x, float a, bool down)
        {
            m.style.display = DisplayStyle.Flex;
            m.style.left = x;
            m.style.opacity = a;
            m.EnableInClassList("mark--down", down);
        }
    }
}
