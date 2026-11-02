using System;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The world presenter's commands (plan.md, The Client: presenters built against small command interfaces, the
    /// EventDispatch maps the sim's events onto them). Positions are ground metres (the sim's x, y: Unity's x, z);
    /// times are song times, given as soon as known.
    /// </summary>
    public interface IWorldCommands
    {
        /// <summary>A committed beat at <paramref name="t"/>, <paramref name="period"/> s long: a beat ring when it is reliable.</summary>
        void Beat(double t, double period, bool downbeat, bool reliable);

        /// <summary>The beat stopped being reliable: no beat rings until it is again.</summary>
        void Beatless(double t);

        /// <summary>An event ring (a special moment, which also kicks the camera and flashes the post).</summary>
        void Ring(RingKind kind, Vector2 at, double t, Color colour, float weight = 1f);

        /// <summary>The zone key moves to <paramref name="key"/> at <paramref name="t"/>, swept out from the hero over <paramref name="fadeS"/>.</summary>
        void Zone(double t, int key, double fadeS);

        /// <summary>A boss duel begins or ends: the camera eases out or back.</summary>
        void Duel(bool on);
    }

    /// <summary>
    /// The world (design.md, The World, The Look &gt; The beat wave, Camera, Restraint, Quality tiers): the endless grid,
    /// the beat wave, the event rings, the zone key, the camera and the post, presented once a frame
    /// (<see cref="Present"/>) at the frame's song time with the hero's feet. It knows nothing of the sim: the client's
    /// world presenter feeds it commands (<see cref="IWorldCommands"/>).
    /// </summary>
    public sealed class World : IWorldCommands, IDisposable
    {
        static readonly int HeroId = Shader.PropertyToID("_RC_Hero");

        public readonly BeatWave Wave = new BeatWave();
        public readonly ImpulseField Rings = new ImpulseField();
        public readonly ZoneSweep Zones;
        public readonly WorldCamera Camera;
        public readonly GridFloor Floor;
        public readonly WorldPost Post;

        /// <summary>The world on <paramref name="camera"/>, starting on zone key <paramref name="zone"/>.</summary>
        public World(Camera camera, int zone = 0)
        {
            Zones = new ZoneSweep(zone);
            Camera = new WorldCamera(camera);
            Floor = new GridFloor(camera != null ? camera.gameObject.layer : 0);
            Post = new WorldPost();
            Rings.Moments = r => Camera.Special(r.Start, r.Strength);
        }

        /// <summary>The hero's feet at the latest frame (ground x, z).</summary>
        public Vector2 Hero { get; private set; }

        public void Beat(double t, double period, bool downbeat, bool reliable)
        {
            if (reliable) Wave.Beat(t, period, downbeat);
        }

        public void Beatless(double t) => Wave.Beatless(t);

        public void Ring(RingKind kind, Vector2 at, double t, Color colour, float weight = 1f) =>
            Rings.Add(ImpulseField.Ring(kind, at, t, colour, weight));

        public void Zone(double t, int key, double fadeS) => Zones.Change(t, key, fadeS, Wave);

        public void Duel(bool on) => Camera.Duel(on);

        /// <summary>
        /// Draws the frame at song time <paramref name="now"/>, <paramref name="dt"/> s after the last: the hero's feet at
        /// <paramref name="hero"/> moving at <paramref name="velocity"/> (ground x, z; m and m/s).
        /// </summary>
        public void Present(double now, Vector2 hero, Vector2 velocity, float dt)
        {
            Hero = hero;
            Shader.SetGlobalVector(HeroId, new Vector4(hero.x, hero.y, (float)now, 0));
            Wave.Publish(now, hero);
            Rings.Publish(now);
            Zones.Publish(now, hero);
            Camera.Update(now, hero, velocity, dt);
            Floor.Follow(Camera.Camera);
            Post.Present(Rings.Special);
        }

        public void Dispose()
        {
            Floor.Dispose();
            Post.Dispose();
        }
    }
}
