using System.Collections.Generic;
using System.Globalization;
using PS.Game.Arena;
using UnityEngine;

namespace Game.Client.Arena
{
    /// <summary>
    /// The arena on its own, for windowless stills (`--arena`): the baked arena in the default zone's
    /// look (`--palette NAME` for another, `--palette ""` for the night as baked), its structures in
    /// crystal, and the camera in the game camera's framing (<see cref="Frame"/>) over the hero's
    /// start, with the hero's light there. `--arena-at x,z` frames another ground point;
    /// `--arena-tour S` holds each of the layout's <c>shots</c> for S seconds in turn, from time 0.
    /// `--view-width M` sets the view's width across the ground (default 30 m, the medium loudness).
    /// </summary>
    public sealed class ArenaStage : MonoBehaviour
    {
        /// <summary>The game camera's pitch and vertical field of view (unity-game.md, "Post-processing and Camera").</summary>
        public const float PitchDeg = 55f, FovDeg = 30f;
        public const float ViewWidthM = 30f;

        struct Shot
        {
            public Vector3 At;
            public float WidthM;
        }

        Camera _camera;
        ArenaRuntime _arena;
        CrystalStructures _crystal;
        readonly List<Shot> _shots = new List<Shot>();
        float _holdS;

        public static ArenaStage Begin(GameObject host, Camera camera)
        {
            var stage = host.AddComponent<ArenaStage>();
            stage._camera = camera;
            ArenaClock.Deterministic = Args.Has("--fixed-time");
            stage._arena = ArenaRuntime.Load(camera, Args.Get("--palette", ArenaLighting.DefaultPalette));
            stage._arena.OnReady += stage.Ready;
            return stage;
        }

        void Ready(ArenaRuntime arena)
        {
            _crystal = new CrystalStructures();
            _crystal.Apply(arena, _camera);
            float width = Args.GetFloat("--view-width", ViewWidthM);
            _holdS = Args.GetFloat("--arena-tour", 0f);
            if (_holdS > 0)
            {
                JsonValue shots = JsonValue.Parse(arena.LayoutJson)["shots"];
                for (int i = 0; i < shots.Count; i++)
                    _shots.Add(new Shot { At = Ground(shots[i]["at"]), WidthM = shots[i]["viewWidthM"].AsFloat(width) });
            }
            if (_shots.Count == 0)
            {
                var at = Args.Get("--arena-at");
                _shots.Add(new Shot { At = at != null ? Parse(at) : Ground(JsonValue.Parse(arena.LayoutJson)["heroStart"]), WidthM = width });
                _holdS = 0;
            }
            LateUpdate();
        }

        void LateUpdate()
        {
            if (_arena == null || !_arena.Ready || _shots.Count == 0) return;
            int i = _holdS > 0 ? (int)(Time.timeSinceLevelLoad / _holdS) % _shots.Count : 0;
            Frame(_camera, _shots[i].At, _shots[i].WidthM, (float)Stills.Width / Stills.Height);
            _arena.FollowHero(_shots[i].At);
        }

        /// <summary>
        /// The game camera over <paramref name="focus"/>: <see cref="PitchDeg"/> down, looking north, at
        /// <see cref="FovDeg"/>, back along its view axis so the view is <paramref name="widthM"/> wide
        /// across the ground through the focus.
        /// </summary>
        public static void Frame(Camera camera, Vector3 focus, float widthM, float aspect)
        {
            camera.fieldOfView = FovDeg;
            float halfWidthTan = Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad) * aspect;
            float distance = widthM * 0.5f / halfWidthTan;
            var rotation = Quaternion.Euler(PitchDeg, 0, 0);
            camera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }

        static Vector3 Ground(JsonValue xy) => new Vector3(xy[0].AsFloat(), 0, xy[1].AsFloat());

        static Vector3 Parse(string xz)
        {
            var v = xz.Split(',');
            return new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), 0, float.Parse(v[1], CultureInfo.InvariantCulture));
        }

        void OnDestroy()
        {
            _crystal?.Dispose();
            _arena?.Dispose();
        }
    }
}
