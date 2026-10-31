using System;
using System.Globalization;
using System.IO;
using Game.Client.Loop;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Look
{
    /// <summary>
    /// The look each frame (unity-game.md, "Post-processing and Camera"): the fast lane read at
    /// frameSong from the sim's revealed stream, the sim's heavy strikes and breaks as moments, the
    /// night volume's beat feel (<see cref="PostFeel"/>), the camera (<see cref="CameraRig"/>, with the
    /// moments' punch and shake) and the shaders' beat globals (<see cref="BeatGlobals"/>). Player flags:
    /// - `--hero-path`: the camera follows <see cref="SyntheticHero"/>'s square walk instead of the sim's hero;
    /// - `--camera-out FILE`: each frame's song time, focus, width, punch, shake and post offsets, as CSV.
    /// </summary>
    public sealed class LookPresenter : IPresenter, IDisposable
    {
        readonly Camera _camera;
        readonly Simulation _sim;
        readonly FastLane _lane = new FastLane();
        readonly FeelMoments _moments = new FeelMoments();
        readonly PostFeel _post = new PostFeel();
        readonly CameraRig _rig = new CameraRig();
        readonly bool _synthetic, _stills;
        StreamWriter _out;
        double _last = double.NaN;

        /// <summary>The volume the beat feel drives (the arena's night post), or null for none yet.</summary>
        public Volume Volume { get; set; }

        /// <summary>Called each frame with the hero's feet (the arena's hero light and canopy follow them).</summary>
        public Action<Vector3> FollowHero { get; set; }

        public PostFeel Post => _post;
        public CameraRig Rig => _rig;

        /// <summary>The latest frame's lane.</summary>
        public LaneFrame Lane { get; private set; }

        public LookPresenter(Camera camera, Simulation sim)
        {
            _camera = camera;
            _sim = sim;
            _synthetic = Args.Has("--hero-path");
            _stills = Args.Has("--capture-stills");
            var outPath = Args.Get("--camera-out");
            if (outPath != null)
            {
                _out = new StreamWriter(Args.Path(outPath));
                _out.WriteLine("song,focus_x,focus_y,width,punch,shake,level,pulse,flash,exposure,bloom,vignette,saturation,chromatic,kick,kick_pre,rise");
            }
        }

        public void OnEvent(in SimEvent e) => _moments.OnEvent(e);

        public void Present(in PresentFrame frame)
        {
            double s = frame.Song;
            double dt = double.IsNaN(_last) ? 0 : Math.Max(0, s - _last);
            _last = s;
            _moments.Forget(s);
            var lane = _lane.Read(_sim.Stream, _sim.Beats, s, _moments);
            Lane = lane;
            var offsets = _post.At(s, lane, _moments);
            _post.Apply(Volume, offsets);

            double hx, hy;
            Direction facing;
            if (_synthetic) SyntheticHero.At(s, out hx, out hy, out facing);
            else
            {
                frame.State.Hero.PositionAt(s, out hx, out hy);
                facing = frame.State.Hero.Facing;
            }
            CameraRig.Heading(facing, out double headX, out double headY);
            _rig.Step(s, dt, hx, hy, headX, headY, lane.Level, lane.Period);
            float punch = _moments.Punch(s, lane.Period), shake = _moments.ShakeAmplitude(s, lane.Period);
            // Stills draw into their own target, whose shape a windowless camera doesn't know yet.
            if (_camera != null) _rig.Apply(_camera, _stills ? (float)Stills.Width / Stills.Height : _camera.aspect, s, punch, shake);
            var feet = new Vector3((float)hx, 0, (float)hy);
            BeatGlobals.Publish(s, lane, feet);
            FollowHero?.Invoke(feet);

            _out?.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R}",
                s, _rig.FocusX, _rig.FocusY, _rig.Width(s), punch, shake, _rig.Level, lane.Pulse, _moments.Flash(s, lane.Period),
                offsets.Exposure, offsets.Bloom, offsets.Vignette, offsets.Saturation, offsets.Chromatic, lane.Kick, lane.KickPre, lane.Rise));
        }

        public void Dispose()
        {
            _out?.Dispose();
            _out = null;
        }
    }

    /// <summary>
    /// A synthetic hero for the camera's evidence (`--hero-path`): a square walk on the 1.5 m grid,
    /// 12 tiles a side, a stride every 0.25 s taking 0.11 s (native's), starting at the origin
    /// heading east, then north, west and south.
    /// </summary>
    public static class SyntheticHero
    {
        public const double TileM = 1.5, StepS = 0.25, StrideS = 0.11;
        public const int Side = 12;

        public static void At(double s, out double x, out double y, out Direction facing)
        {
            int step = Math.Max(0, (int)Math.Floor(s / StepS));
            double into = Math.Min(1, Math.Max(0, s - step * StepS) / StrideS);
            Tile(step, out int fx, out int fy, out _);
            Tile(step + 1, out int tx, out int ty, out facing);
            x = TileM * (fx + (tx - fx) * into);
            y = TileM * (fy + (ty - fy) * into);
        }

        /// <summary>The tile after <paramref name="n"/> strides, and the heading of the stride that reached it.</summary>
        static void Tile(int n, out int x, out int y, out Direction facing)
        {
            int lap = 4 * Side, k = n % lap, side = k / Side, along = k % Side;
            switch (side)
            {
                case 0: x = along; y = 0; break;
                case 1: x = Side; y = along; break;
                case 2: x = Side - along; y = Side; break;
                default: x = 0; y = Side - along; break;
            }
            int prev = (n - 1 + lap) % lap / Side;
            facing = n == 0 ? Direction.Right : prev == 0 ? Direction.Right : prev == 1 ? Direction.Up : prev == 2 ? Direction.Left : Direction.Down;
        }
    }
}
