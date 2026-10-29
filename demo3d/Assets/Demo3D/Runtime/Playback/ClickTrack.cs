using Demo3D.Choreography;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// The `--click` track (qa.md): a short click on every beat of the song map, louder and
    /// higher on downbeats, rendered into one clip and scheduled with the music.
    /// </summary>
    public static class ClickTrack
    {
        public static AudioClip Build(SongMap map, int rate = 48000)
        {
            int length = (int)((map.Duration + 1.0) * rate);
            var data = new float[length];
            int beatsPerBar = map.MeterBeatsPerBar.Length > 0 ? map.MeterBeatsPerBar[0] : 4;
            for (int b = 0; b < map.Beats.Length; b++)
            {
                bool down = ((b - map.FirstDownbeatIndex) % beatsPerBar + beatsPerBar) % beatsPerBar == 0;
                float freq = down ? 1600f : 1000f, gain = down ? 0.8f : 0.45f;
                int at = (int)(map.Beats[b] * rate);
                int n = (int)(0.03f * rate);
                for (int i = 0; i < n && at + i < length; i++)
                {
                    if (at + i < 0) continue;
                    float env = 1f - (float)i / n;
                    data[at + i] += gain * env * env * Mathf.Sin(2f * Mathf.PI * freq * i / rate);
                }
            }
            var clip = AudioClip.Create("click", length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
