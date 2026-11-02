using System.Collections.Generic;
using System.Globalization;
using PS.Game.Arena;
using UnityEngine;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena on its own, for windowless stills (`--arena`): the baked arena in the default zone's
    /// look (`--palette NAME` for another, `--palette ""` for the night as baked), drawn in the look's
    /// world kit (<see cref="VoidArena"/>, the grid floor in the ground's place), and the camera in the game camera's framing (<see cref="Look.CameraRig.Frame"/>) over the hero's
    /// start, with the hero's light there. `--arena-at x,z` frames another ground point;
    /// `--arena-tour S` holds each of the layout's <c>shots</c> for S seconds in turn, from time 0.
    /// `--view-width M` sets the view's width across the ground (default 30 m, the medium loudness).
    /// </summary>
    public sealed class ArenaStage : MonoBehaviour
    {
        public const float ViewWidthM = Look.CameraRig.MediumWidthM;

        struct Shot
        {
            public Vector3 At;
            public float WidthM;
        }

        Camera _camera;
        ArenaRuntime _arena;
        VoidArena _void;
        PS.Look.GridFloor _floor;
        readonly List<Shot> _shots = new List<Shot>();
        float _holdS;

        public static ArenaStage Begin(GameObject host, Camera camera)
        {
            var stage = host.AddComponent<ArenaStage>();
            stage._camera = camera;
            ArenaClock.Deterministic = LaunchOptions.Current.FixedTime;
            stage._arena = ArenaRuntime.Load(camera, LaunchOptions.Current.Palette);
            stage._arena.OnReady += stage.Ready;
            return stage;
        }

        void Ready(ArenaRuntime arena)
        {
            _void = new VoidArena(arena, PS.Look.ResolvedLook.Active, _camera);
            _floor = PS.Look.GridFloor.For(PS.Look.ResolvedLook.Active);
            if (_floor != null) PS.Look.GridFloor.Replace(arena.Ground);
            _void.Lay(_floor);
            var options = LaunchOptions.Current;
            float width = options.ViewWidth > 0 ? options.ViewWidth : ViewWidthM;
            _holdS = options.ArenaTour;
            if (_holdS > 0)
            {
                JsonValue shots = JsonValue.Parse(arena.LayoutJson)["shots"];
                for (int i = 0; i < shots.Count; i++)
                    _shots.Add(new Shot { At = Ground(shots[i]["at"]), WidthM = shots[i]["viewWidthM"].AsFloat(width) });
            }
            if (_shots.Count == 0)
            {
                var at = options.ArenaAt;
                _shots.Add(new Shot { At = at != null ? Parse(at) : Ground(JsonValue.Parse(arena.LayoutJson)["heroStart"]), WidthM = width });
                _holdS = 0;
            }
            LateUpdate();
        }

        void LateUpdate()
        {
            if (_arena == null || !_arena.Ready || _shots.Count == 0) return;
            int i = _holdS > 0 ? (int)(Time.timeSinceLevelLoad / _holdS) % _shots.Count : 0;
            Look.CameraRig.Frame(_camera, _shots[i].At, _shots[i].WidthM, (float)Stills.Width / Stills.Height);
            _arena.FollowHero(_shots[i].At);
            _floor?.Follow(_camera);
        }

        static Vector3 Ground(JsonValue xy) => new Vector3(xy[0].AsFloat(), 0, xy[1].AsFloat());

        static Vector3 Parse(string xz)
        {
            var v = xz.Split(',');
            return new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), 0, float.Parse(v[1], CultureInfo.InvariantCulture));
        }

        void OnDestroy()
        {
            _void?.Dispose();
            _floor?.Dispose();
            _arena?.Dispose();
        }
    }
}
