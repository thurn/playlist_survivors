using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PS.Look
{
    /// <summary>
    /// The void world's air (neon-look.md, The Axes &gt; World; the style guide's World): the camera clears to the
    /// palette's <c>void.floor</c> and the distance fades into <c>void.fog</c> (exponential squared, clear across a
    /// framing, gone past about 150 m, so the far floor has no moiré), and the vendor set the world replaces is hidden
    /// (<see cref="Hide"/>). The grid floor is <see cref="GridFloor"/>'s. Under `hued-zones` a zone takes the palette's
    /// zone tint in turn: <see cref="SetZone"/> eases to it, the fog takes its hue at the fog's own lightness and the
    /// <see cref="Floor"/>'s lines take it; `void-grid` and `pure-void` keep the key throughout.
    /// </summary>
    public sealed class VoidWorld
    {
        public const float FogDensity = 0.011f;
        /// <summary>How far a zone's hue tints the fog (the rest stays the void's).</summary>
        public const float FogTint = 0.6f;

        public readonly string Option;
        public readonly Palette Palette;
        Color _from, _to, _now;
        double _start = double.NegativeInfinity, _seconds;

        /// <summary>The world <paramref name="look"/> names, or null when it names none built (the arena's own).</summary>
        public static VoidWorld For(ResolvedLook look)
        {
            string option = look?.Option(LookAxes.World);
            return option == null ? null : new VoidWorld(option, look.Palette);
        }

        public VoidWorld(string option, Palette palette)
        {
            Option = option;
            Palette = palette;
            _from = _to = _now = Zoned && palette.Zones.Length > 0 ? palette.Zones[0].Tint : palette.GridLine;
        }

        /// <summary>Whether zones tint the world (`hued-zones`).</summary>
        public bool Zoned => Option == GridFloor.HuedZones;

        /// <summary>The zone's tint now (sRGB). The key's grid line outside `hued-zones`.</summary>
        public Color ZoneTint => _now;

        /// <summary>The grid floor whose lines take the zone's hue under `hued-zones`, or null.</summary>
        public GridFloor Floor { get; set; }

        /// <summary>Sets the fog and <paramref name="camera"/>'s clear colour (it draws the void); call once the arena's own night is applied.</summary>
        public void Apply(Camera camera)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.skybox = null;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Palette.VoidFloor;
            }
            ApplyFog();
        }

        /// <summary>Hides the vendor set the void replaces: its renderers drawn no more, its lights out.</summary>
        public static void Hide(IEnumerable<Renderer> renderers, IEnumerable<Light> lights = null)
        {
            foreach (var r in renderers) if (r != null) r.enabled = false;
            if (lights != null) foreach (var l in lights) if (l != null) l.enabled = false;
        }

        /// <summary>
        /// Eases to zone <paramref name="zone"/>'s tint (the palette's zones in turn) from song time <paramref name="from"/>
        /// over <paramref name="seconds"/>, smoothstep; nothing outside `hued-zones`.
        /// </summary>
        public void SetZone(int zone, double from, double seconds)
        {
            if (!Zoned || Palette.Zones.Length == 0) return;
            _from = At(from);
            _to = Palette.Zones[((zone % Palette.Zones.Length) + Palette.Zones.Length) % Palette.Zones.Length].Tint;
            _start = from;
            _seconds = System.Math.Max(0, seconds);
        }

        /// <summary>The zone's tint at song time <paramref name="s"/>, and the fog's from it; call each frame.</summary>
        public void Present(double s)
        {
            var now = At(s);
            if (now == _now) return;
            _now = now;
            ApplyFog();
            if (Zoned) Floor?.SetZoneTint(_now, 1f);
        }

        Color At(double s)
        {
            if (_seconds <= 0 || s >= _start + _seconds) return s < _start ? _from : _to;
            float x = (float)System.Math.Max(0, (s - _start) / _seconds);
            return Color.Lerp(_from, _to, x * x * (3 - 2 * x));
        }

        /// <summary>The fog: <c>void.fog</c>, its hue toward the zone's at its own lightness under `hued-zones`.</summary>
        void ApplyFog()
        {
            var fog = Palette.VoidFog;
            if (Zoned)
            {
                float target = fog.grayscale, own = Mathf.Max(_now.grayscale, 1e-3f);
                var hued = _now * (target / own);
                hued.a = 1;
                fog = Color.Lerp(fog, hued, FogTint);
            }
            RenderSettings.fogColor = fog;
        }
    }
}
