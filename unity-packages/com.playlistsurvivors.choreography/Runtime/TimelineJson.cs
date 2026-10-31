using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PS.Choreography
{
    /// <summary>
    /// The timeline's debug dump (choreography.md: "A debug command dumps it to JSON for tests and
    /// inspection"). Numbers have fixed decimals (times 6, positions 4, angles 4), entities and
    /// events keep the timeline's order, and tick timing and build time are left out, so the same
    /// inputs give byte-identical dumps on one runtime.
    /// </summary>
    public static class TimelineJson
    {
        const int T = 6, P = 4, A = 4;

        /// <summary>Writes the dump; <paramref name="from"/> and <paramref name="to"/> limit events and segments to a window of song time.</summary>
        public static void Write(Timeline tl, TextWriter w, double from = double.NegativeInfinity, double to = double.PositiveInfinity)
        {
            var o = new JsonOut(w);
            o.BeginObject().Newline();
            o.Field("format", LayerInfo.TimelineFormatVersion).Field("name", tl.Name).Field("seed", tl.Seed)
             .Field("duration", tl.Duration, T).Field("tickRate", Choreographer.TickRate, 0).Field("framingRate", Timeline.FramingRate, 0).Newline();
            Names(o, "types", tl.TypeNames);
            Names(o, "projectiles", tl.ProjectileNames);
            Names(o, "clips", tl.ClipNames);
            Names(o, "effectNames", tl.EffectNames);
            Names(o, "soundNames", tl.SoundNames);

            o.Key("sections").BeginArray().Newline();
            foreach (SectionStats s in tl.Sections)
            {
                o.BeginObject().Field("label", s.Label).Field("feel", s.Feel == SectionFeel.Free ? "free" : "pulsed")
                 .Field("start", s.Start, T).Field("end", s.End, T).Field("load", s.Load).Field("goal", s.Goal)
                 .Field("targetOnScreen", s.TargetOnScreen).Field("viewWidthM", s.ViewWidthM, 2)
                 .Field("targetDeathsPerS", s.TargetDeathsPerSecond, 3).Field("deaths", s.Deaths).Field("hits", s.Hits)
                 .Field("ticks", s.Ticks).Field("spawns", s.Spawns).Field("attacks", s.Attacks).Field("parries", s.Parries)
                 .Field("heroActions", s.HeroActions).Field("heroStrides", s.HeroStrides)
                 .Field("meanOnScreen", s.MeanOnScreen, 1).Field("maxOnScreen", s.MaxOnScreen)
                 .Field("goalReachedAt", s.GoalReachedAt, T).EndObject().Newline();
            }
            o.EndArray().Newline();

            o.Key("entities").BeginArray().Newline();
            foreach (EntityInfo e in tl.Entities)
            {
                if (e.Despawn < from || e.Spawn > to) continue;
                o.BeginObject().Field("id", e.Id).Field("kind", KindName(e.Kind)).Field("type", e.Type)
                 .Field("spawn", e.Spawn, T).Field("despawn", e.Despawn, T).Field("seed", e.Seed).EndObject().Newline();
            }
            o.EndArray().Newline();

            o.Key("segments").BeginArray().Newline();
            foreach (MotionSegment s in tl.Segments)
            {
                if (s.T1 < from || s.T0 > to) continue;
                o.BeginObject().Field("entity", s.Entity).Field("t0", s.T0, T).Field("t1", s.T1, T);
                o.Key("from").BeginArray().Value(s.From.X, P).Value(s.From.Y, P).EndArray();
                o.Key("to").BeginArray().Value(s.To.X, P).Value(s.To.Y, P).EndArray();
                o.Key("facing").BeginArray().Value(s.FacingFrom, A).Value(s.FacingTo, A).EndArray();
                o.Field("ease", s.Ease == Ease.Pulse ? "pulse" : "linear").EndObject().Newline();
            }
            o.EndArray().Newline();

            o.Key("actions").BeginArray().Newline();
            foreach (ActionEvent a in tl.Actions)
            {
                if (a.Impact < from || a.Impact > to) continue;
                o.BeginObject().Field("entity", a.Entity).Field("kind", ActionName(a.Kind)).Field("impact", a.Impact, T)
                 .Field("clip", a.Clip).Field("target", a.Target).Field("strength", a.Strength, 3);
                if (a.HasClipTiming) o.Field("clipStart", a.ClipStart, T).Field("clipSpeed", a.ClipSpeed, 4);
                o.EndObject().Newline();
            }
            o.EndArray().Newline();

            o.Key("effects").BeginArray().Newline();
            foreach (EffectEvent e in tl.Effects)
            {
                if (e.Time < from || e.Time > to) continue;
                o.BeginObject().Field("effect", e.Effect).Field("attachTo", e.AttachTo).Field("seed", e.Seed).Field("time", e.Time, T);
                o.Key("position").BeginArray().Value(e.Position.X, P).Value(e.Position.Y, P).Value(e.Position.Z, P).EndArray();
                o.Field("yaw", e.Yaw, A).Field("scale", e.Scale, P).EndObject().Newline();
            }
            o.EndArray().Newline();

            o.Key("sounds").BeginArray().Newline();
            foreach (SoundEvent s in tl.Sounds)
            {
                if (s.Time < from || s.Time > to) continue;
                o.BeginObject().Field("sound", s.Sound).Field("time", s.Time, T).Field("gain", s.Gain, 3).EndObject().Newline();
            }
            o.EndArray().Newline();

            if (tl.Breaks.Length > 0)   // only under a structure-breaks overlay, so other dumps stay as they were
            {
                o.Key("breaks").BeginArray().Newline();
                foreach (BreakEvent b in tl.Breaks)
                {
                    if (b.Time < from || b.Time > to) continue;
                    o.BeginObject().Field("time", b.Time, T).Field("placement", b.Placement).Field("target", b.Target).Field("projectile", b.Projectile);
                    o.Key("origin").BeginArray().Value(b.Origin.X, P).Value(b.Origin.Y, P).EndArray();
                    o.Key("point").BeginArray().Value(b.Point.X, P).Value(b.Point.Y, P).EndArray().EndObject().Newline();
                }
                o.EndArray().Newline();
            }

            o.Key("framing").BeginArray().Newline();
            for (int i = 0; i < tl.Framing.Length; i++)
            {
                double t = i / Timeline.FramingRate;
                if (t < from || t > to) continue;
                FramingSample f = tl.Framing[i];
                o.BeginArray().Value(f.Focus.X, P).Value(f.Focus.Y, P).Value(f.ViewWidth, 3).EndArray().Newline();
            }
            o.EndArray().Newline();
            o.EndObject().Newline();
        }

        public static string ToJson(Timeline tl, double from = double.NegativeInfinity, double to = double.PositiveInfinity)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            Write(tl, sw, from, to);
            return sw.ToString();
        }

        public static void WriteFile(Timeline tl, string path, double from = double.NegativeInfinity, double to = double.PositiveInfinity)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16))
                Write(tl, w, from, to);
        }

        /// <summary>A 64-bit FNV-1a hash of the whole dump and its length in characters, without holding it in memory.</summary>
        public static ulong Hash(Timeline tl, out long chars)
        {
            var hw = new HashingWriter();
            Write(tl, hw);
            chars = hw.Chars;
            return hw.Value;
        }

        static void Names(JsonOut o, string key, string[] names)
        {
            o.Key(key).BeginArray();
            foreach (string n in names) o.Value(n);
            o.EndArray().Newline();
        }

        static string KindName(EntityKind k) => k == EntityKind.Hero ? "hero" : k == EntityKind.Monster ? "monster" : "projectile";

        static string ActionName(ActionKind k)
        {
            switch (k)
            {
                case ActionKind.Spawn: return "spawn";
                case ActionKind.Attack: return "attack";
                case ActionKind.Hit: return "hit";
                case ActionKind.Tick: return "tick";
                case ActionKind.Death: return "death";
                default: return "parry";
            }
        }

        sealed class HashingWriter : TextWriter
        {
            public ulong Value = 14695981039346656037UL;
            public long Chars;
            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                Value = (Value ^ value) * 1099511628211UL;
                Chars++;
            }

            public override void Write(string value)
            {
                if (value == null) return;
                for (int i = 0; i < value.Length; i++) Value = (Value ^ value[i]) * 1099511628211UL;
                Chars += value.Length;
            }
        }
    }
}
