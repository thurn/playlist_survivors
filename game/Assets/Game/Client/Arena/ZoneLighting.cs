using System;
using System.Collections.Generic;

namespace Game.Client.Arena
{
    /// <summary>
    /// The zones' runtime lighting (unity-game.md, "The Look": zones recolour runtime lighting with night
    /// versions of the 3D demo's palettes, eased over two bars from the change's downbeat). A zone change
    /// eases from the lighting at its downbeat to the zone's palette of game/Content/arena-layout.json (smoothstep, as the
    /// post's zone grade does), and each frame applies the lighting at frameSong through
    /// <see cref="ArenaRuntime.Relight"/>. Zone changes settle ahead of the frames, so a change arriving
    /// mid-ease starts from where that ease will be at its downbeat.
    /// </summary>
    public sealed class ZoneLighting
    {
        readonly string _layoutJson;
        readonly Action<ArenaLighting> _apply;
        readonly Dictionary<string, ArenaLighting> _palettes = new Dictionary<string, ArenaLighting>();
        readonly ArenaLighting _now = new ArenaLighting();
        ArenaLighting _from, _to;
        double _start = double.NegativeInfinity, _seconds;
        bool _settled = true;

        /// <summary>Lighting from <paramref name="initial"/> (the loaded palette's), applied by <paramref name="apply"/>.</summary>
        public ZoneLighting(string layoutJson, ArenaLighting initial, Action<ArenaLighting> apply)
        {
            _layoutJson = layoutJson;
            _apply = apply;
            _from = _to = initial;
        }

        /// <summary>The palette eased toward, by name.</summary>
        public string Palette => _to.Palette;

        /// <summary>Eases to <paramref name="palette"/>'s lighting from song time <paramref name="from"/> over <paramref name="seconds"/>.</summary>
        public void SetZone(string palette, double from, double seconds)
        {
            if (!_palettes.TryGetValue(palette, out var to))
                _palettes[palette] = to = ArenaLighting.Parse(_layoutJson, palette);
            _from = ArenaLighting.Lerp(_from, _to, Progress(from), new ArenaLighting());
            _to = to;
            _start = from;
            _seconds = Math.Max(0, seconds);
            _settled = false;
        }

        /// <summary>The ease's share at song time <paramref name="s"/>, smoothstep.</summary>
        public float Progress(double s)
        {
            if (_seconds <= 0 || s >= _start + _seconds) return s < _start ? 0 : 1;
            float x = (float)Math.Max(0, (s - _start) / _seconds);
            return x * x * (3 - 2 * x);
        }

        /// <summary>The lighting at song time <paramref name="s"/> (a shared instance, overwritten by the next call).</summary>
        public ArenaLighting At(double s) => ArenaLighting.Lerp(_from, _to, Progress(s), _now);

        /// <summary>Applies the lighting at frameSong <paramref name="s"/>, while an ease runs.</summary>
        public void Present(double s)
        {
            if (_settled) return;
            _apply(At(s));
            _settled = s >= _start + _seconds;
        }
    }
}
