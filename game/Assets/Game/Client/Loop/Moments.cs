using System;
using System.Globalization;
using System.IO;

namespace Game.Client.Loop
{
    /// <summary>The key moments the presenters show on the beat (unity-game.md, Time and Sync), as the on-the-beat audit reads them.</summary>
    public enum MomentKind
    {
        /// <summary>The hero's hop touching down: its step's end (a step cut short by the next never lands).</summary>
        HeroLand,
        /// <summary>A press stride's hop setting off, from the press heard.</summary>
        HeroPress,
        /// <summary>A held key's stride setting off, on its stride step.</summary>
        HeroStride,
        /// <summary>The blade's flare on a sword impact.</summary>
        Blade,
        /// <summary>A monster's walk planting its foot (a step's or a charge's footplant).</summary>
        Footfall,
        /// <summary>A monster's death seeding its GPU shatter from its pose.</summary>
        Death,
        /// <summary>A crystal structure shattering into shards.</summary>
        Break,
        /// <summary>A crystal structure starting to regrow, erupting from its footprint.</summary>
        Regrow,
        /// <summary>A particle effect emitted (the sword's slashes, deaths' bursts, hits, the weapons' casts and hits).</summary>
        Effect,
        /// <summary>The overhead beat gem's ring landing on it.</summary>
        GemRing,
        /// <summary>A level-up card's flip coming to rest face up.</summary>
        CardFlip,
    }

    /// <summary>
    /// `--moments-out FILE`, the on-the-beat audit's trace (`game beat`): each presenter marks a key moment on the
    /// frame it first shows it, with the moment's own song time, and each row keeps that frame's frameSong and the
    /// previous frame's. A moment is on its frame when it is the first frame at or after its time:
    /// <c>prev &lt; t &lt;= song</c>. Off unless the flag opens it, when marking costs a comparison.
    /// </summary>
    public static class Moments
    {
        static StreamWriter _out;
        static int _frame = -1;
        static double _song = double.NaN, _prev = double.NaN;

        /// <summary>Whether the trace is being written.</summary>
        public static bool On => _out != null;

        public static void Open(string path)
        {
            Close();
            _out = new StreamWriter(path);
            _out.WriteLine("frame,song,prev,kind,t,id,detail");
            _frame = -1;
            _song = _prev = double.NaN;
        }

        /// <summary>A frame about to be presented: its index and frameSong.</summary>
        public static void Frame(int frame, double song)
        {
            if (_out == null) return;
            _prev = _song;
            _song = song;
            _frame = frame;
        }

        /// <summary>A moment at song time <paramref name="t"/> shown on this frame, with an id and a detail (an effect's name) to tell it by.</summary>
        public static void Mark(MomentKind kind, double t, int id = -1, string detail = null)
        {
            if (_out == null) return;
            _out.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3},{4:R},{5},{6}",
                _frame, _song, _prev, kind, t, id, detail ?? ""));
        }

        public static void Close()
        {
            _out?.Dispose();
            _out = null;
        }
    }
}
