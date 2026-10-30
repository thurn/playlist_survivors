using System;
using System.Collections.Generic;

namespace PS.Choreography
{
    /// <summary>
    /// One clip's annotation (choreography.md, "Clip annotations and retiming"): impact and end in
    /// seconds from the clip's start, root-motion distance for combo clips, stride for locomotion.
    /// <see cref="Use"/> names its role as "&lt;owner&gt; &lt;role&gt;" ("skeleton attack", "hero slashL").
    /// </summary>
    public readonly struct ClipAnnotation
    {
        public readonly string Guid, Clip, Use;
        public readonly float Impact, End, RootMotionM, StrideM;

        public ClipAnnotation(string guid, string clip, string use, float impact, float end, float rootMotionM, float strideM)
        {
            Guid = guid; Clip = clip; Use = use; Impact = impact; End = end; RootMotionM = rootMotionM; StrideM = strideM;
        }
    }

    /// <summary>
    /// The clip annotations the choreographer schedules with. A timeline's clip index is the
    /// annotation's index here. TD3 loads placeholder annotations; TD4 swaps in TD2's measured
    /// file (same format, keyed by vendor GUID and clip name), and the roles in <c>use</c> keep
    /// the lookup independent of which clips were chosen.
    /// </summary>
    public sealed class ClipLibrary
    {
        public const float MinSpeed = 0.75f, MaxSpeed = 1.33f;

        public readonly ClipAnnotation[] Clips;

        public ClipLibrary(ClipAnnotation[] clips) { Clips = clips; }

        /// <summary>Reads a JSON array of annotations, or an object with an "annotations" array.</summary>
        public static ClipLibrary Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            JsonValue arr = root.Type == JsonType.Array ? root : root["annotations"];
            var list = new List<ClipAnnotation>();
            for (int i = 0; i < arr.Count; i++)
            {
                JsonValue a = arr[i];
                string use = a["use"].AsString();
                if (string.IsNullOrEmpty(use)) throw new FormatException("clip annotation " + i + ": missing \"use\"");
                float impact = a["impact"].AsFloat(), end = a["end"].AsFloat(Math.Max(impact, 0.5f));
                if (end < impact) throw new FormatException("clip annotation '" + use + "': end before impact");
                list.Add(new ClipAnnotation(a["guid"].AsString(""), a["clip"].AsString(""), use, impact, end,
                    a["rootMotionM"].AsFloat(), a["strideM"].AsFloat(a["stride"].AsFloat())));
            }
            return new ClipLibrary(list.ToArray());
        }

        public int Count => Clips.Length;
        public ClipAnnotation this[int i] => Clips[i];

        /// <summary>The first clip whose use is exactly <paramref name="use"/>, or -1.</summary>
        public int Find(string use)
        {
            for (int i = 0; i < Clips.Length; i++) if (Clips[i].Use == use) return i;
            return -1;
        }

        public int Require(string use)
        {
            int i = Find(use);
            if (i < 0) throw new ArgumentException("no clip annotation with use '" + use + "'");
            return i;
        }

        /// <summary>
        /// Retiming (choreography.md): speed = clamp(impact / timeAvailable, <paramref name="minSpeed"/>, <paramref name="maxSpeed"/>),
        /// start = impact time − annotated impact / speed. <paramref name="availableFrom"/> is when the
        /// entity is free to start. False if the impact would need a speed above the cap (1.33, or the
        /// level script's <see cref="SimTuning.ComboMaxSpeed"/> for the hero's combos, whose floor is its
        /// <see cref="SimTuning.ComboMinSpeed"/>).
        /// </summary>
        public static bool Fit(ClipAnnotation clip, double availableFrom, double impactTime, out double start, out double end, out float speed,
            float maxSpeed = MaxSpeed, float minSpeed = MinSpeed)
        {
            double avail = impactTime - availableFrom;
            start = end = 0;
            speed = 1;
            if (avail <= 0 && clip.Impact > 0) return false;
            double s = clip.Impact <= 0 ? 1.0 : clip.Impact / avail;
            if (s > maxSpeed + 1e-9) return false;
            if (s < minSpeed) s = minSpeed;
            speed = (float)s;
            start = impactTime - clip.Impact / s;
            end = start + clip.End / s;
            return true;
        }

        /// <summary>
        /// Fixed-windup retiming (the hero's <c>windupS</c>): start = impact time − windup, speed =
        /// annotated impact / windup (at least 1, so a clip that strikes at once isn't slowed), so the
        /// impact frame lands on the impact time. False if the entity isn't free by the start.
        /// </summary>
        public static bool FitWindup(ClipAnnotation clip, double availableFrom, double impactTime, float windup, out double start, out double end, out float speed)
        {
            speed = (float)Math.Max(1.0, clip.Impact / windup);
            start = impactTime - clip.Impact / speed;
            end = start + clip.End / speed;
            return start >= availableFrom - 1e-9;
        }
    }
}
