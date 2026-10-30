using System;
using System.Collections.Generic;
using Demo2D.Choreography;
using PS.Choreography;
using UnityEngine;

namespace Demo2D.Runtime.Playback
{
    /// <summary>
    /// The song's playback (PX5), apart from its clock: builds the timeline from the baked
    /// PlaybackData, then draws any song time on request (<see cref="Frame"/>): the arena (PX6),
    /// the sprite crowd, the effects and the night's lights (PX7), the camera's follow, punch and
    /// shake, and the pixel pipeline. Everything it draws is a function of the time, so the player
    /// (DemoMain, which owns the clock) and the seek test share it.
    /// </summary>
    public sealed class SongPlayback : IDisposable
    {
        public readonly Timeline Timeline;
        public readonly SpriteEvaluator Evaluator;
        public readonly PixelPipeline Pipeline;
        readonly SpriteCrowd _crowd;
        readonly ArenaProps _props;
        readonly List<Vector2> _feet = new List<Vector2>();
        readonly CameraFeel _feel;
        readonly GameObject _root;
        public readonly EffectLayer Effects;
        public readonly NightLighting Night;
        readonly int[] _bySpawn;
        readonly List<int> _active = new List<int>();
        int _nextSpawn;
        double _lastT = double.NegativeInfinity;
        readonly List<UnityEngine.Object> _made = new List<UnityEngine.Object>();

        /// <summary>Props this far beyond the view still draw (the camera's punch and shake).</summary>
        const float PropMarginM = 2f;

        public bool Feel = true;
        /// <summary>Draw the hero alone and unflashed (`--empty`): the arena and its night without the horde.</summary>
        public bool Empty;
        public double BuildSeconds { get; }
        public int OnScreenAlive { get; private set; }
        public int OnScreenDying { get; private set; }
        public int Drawn => _crowd.Drawn;
        public float ViewWidth { get; private set; }
        public readonly ForestArena Arena;

        public SongPlayback(PlaybackData data, string zoom, Transform parent)
        {
            _root = new GameObject("Song Playback");
            _root.transform.SetParent(parent, false);
            var level = LevelScript.Parse(data.level.text);
            var layout = data.Layout(level.Layout) ?? throw new InvalidOperationException("SongPlayback: no layout " + level.Layout);
            Arena = ForestArena.Parse(layout.text, data.forestProps.text);
            var choreo = Arena.Data(data.annotations.text, data.roster.text);
            var started = Time.realtimeSinceStartupAsDouble;
            Timeline = Choreographer.Run(SongMap.Parse(data.songMap.text), level, choreo);
            BuildSeconds = Time.realtimeSinceStartupAsDouble - started;

            // Each annotation's frames, from its owner's atlas ("hero" is the knight's).
            var atlases = new Dictionary<string, CharacterAtlas>(StringComparer.Ordinal);
            for (int i = 0; i < data.atlases.Length; i++)
            {
                var a = new CharacterAtlas(data.atlases[i], data.atlasJson[i].text);
                atlases[a.Type] = a;
            }
            var clips = choreo.Clips;
            var sprites = new Sprite[clips.Count][];
            var frames = new int[clips.Count];
            for (int c = 0; c < clips.Count; c++)
            {
                var use = clips[c].Use;
                var owner = use.Substring(0, use.LastIndexOf(' '));
                var type = owner == "hero" ? "knight" : owner;
                if (!atlases.TryGetValue(type, out var atlas)) throw new InvalidOperationException($"SongPlayback: no atlas for {type} (run demo2d unity BakeSprites)");
                sprites[c] = atlas.ForLibraryClip(clips[c].Clip) ?? throw new InvalidOperationException($"SongPlayback: {type}'s atlas has no {clips[c].Clip}");
                frames[c] = sprites[c].Length;
                foreach (var s in sprites[c]) _made.Add(s);
            }
            var walk = Array.ConvertAll(Timeline.TypeNames, n => clips.Require(n + " walk"));
            Evaluator = new SpriteEvaluator(Timeline, clips, frames, FrameRate(atlases), walk, clips.Require("hero walk"), clips.Find("hero idle"));

            float heroRadius = JsonValue.Parse(data.roster.text)["hero"]["radius"].AsFloat(0.5f);
            _crowd = new SpriteCrowd(Evaluator, sprites, data.spriteFlash, data.spriteUnlit, heroRadius, _root.transform);
            var boss = Array.Find(level.Sections, s => s.Boss != null)?.Boss;
            _feel = new CameraFeel(Timeline, boss != null ? Array.IndexOf(Timeline.TypeNames, boss) : -1);
            float widest = 0;
            foreach (var f in Timeline.Framing) widest = Mathf.Max(widest, f.ViewWidth);
            Pipeline = new PixelPipeline(data.upscale, widest, zoom, _root.transform);
            ArenaGround.Build(Arena, data, _root.transform, _made);
            _props = new ArenaProps(Arena, data.propSheets, data.spriteLit, _root.transform);

            var library = EffectLibrary.Parse(data.effects.text);
            var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            for (int i = 0; i < data.effectPaths.Length; i++) prefabs[data.effectPaths[i]] = data.effectPrefabs[i];
            Effects = new EffectLayer(Timeline, library, prefabs, data.roster.text, data.glow, _root.transform);
            Night = new NightLighting(data.night.text, Arena, data.glow, _root.transform);

            _bySpawn = new int[Timeline.EntityCount];
            for (int i = 0; i < _bySpawn.Length; i++) _bySpawn[i] = i;
            Array.Sort(_bySpawn, (a, b) =>
            {
                int c = Timeline.Entities[a].Spawn.CompareTo(Timeline.Entities[b].Spawn);
                return c != 0 ? c : a.CompareTo(b);
            });
        }

        /// <summary>The packs' frame rate (every atlas has the same, 15).</summary>
        static float FrameRate(Dictionary<string, CharacterAtlas> atlases)
        {
            foreach (var a in atlases.Values) return a.FrameRate;
            return 15;
        }

        /// <summary>
        /// Draws song time t on an output of outW × outH pixels; <paramref name="still"/> simulates
        /// the effects exactly to t (a still: EffectLayer), where live play lets them run.
        /// </summary>
        public void Frame(double t, int outW, int outH, bool still = false)
        {
            Activate(t);
            if (Empty) _active.RemoveAll(e => e != 0);
            _crowd.NoFlash = Empty;
            var framing = Timeline.FramingAt(Math.Max(0, t));
            float punch = Feel ? _feel.Punch(t) : 1f;
            var centre = new Vector2(framing.Focus.X, framing.Focus.Y) + (Feel ? _feel.Shake(t) : Vector2.zero);
            ViewWidth = framing.ViewWidth;

            // The props on screen (with a margin for the punch and the shake), faded over the characters behind them.
            float w = framing.ViewWidth * Mathf.Max(1f, punch) + 2 * PropMarginM, h = w * 9 / 16 + 2 * PropMarginM;
            _feet.Clear();
            foreach (int e in _active)
                if (Timeline.Entities[e].Kind != EntityKind.Projectile)
                {
                    var p = Timeline.PositionAt(e, t);
                    _feet.Add(new Vector2(p.X, p.Y));
                }
            var shown = new Rect(centre.x - w / 2, centre.y - h / 2, w, h);
            _props.Draw(t, shown, _feet);
            _crowd.Draw(_active, t, _props);
            Effects.Frame(t, shown, _active, still);
            var hero = Timeline.PositionAt(0, t);
            Night.Frame(t, shown, new Vector2(hero.X, hero.Y));
            Pipeline.Frame(centre, framing.ViewWidth * punch, framing.ViewWidth, outW, outH);

            // On-screen counts as the sim counts them: monsters inside the framing's view.
            var view = Timeline.View(framing.ViewWidth);
            OnScreenAlive = OnScreenDying = 0;
            foreach (int e in _active)
            {
                if (Timeline.Entities[e].Kind != EntityKind.Monster) continue;
                if (!view.Contains(framing.Focus, Timeline.PositionAt(e, t))) continue;
                if (IsDying(e, t)) OnScreenDying++; else OnScreenAlive++;
            }
        }

        bool IsDying(int e, double t)
        {
            var acts = Timeline.ActionsOf(e);
            for (int k = acts.Count - 1; k >= 0; k--)
            {
                var a = acts.Array[acts.Offset + k];
                if (a.Kind == ActionKind.Death) return a.Impact <= t;
            }
            return false;
        }

        /// <summary>The entities alive at t, in id order. Seeks rebuild it; forward play adds and drops.</summary>
        void Activate(double t)
        {
            if (t < _lastT)
            {
                _active.Clear();
                _nextSpawn = 0;
            }
            _lastT = t;
            bool added = false;
            while (_nextSpawn < _bySpawn.Length && Timeline.Entities[_bySpawn[_nextSpawn]].Spawn <= t)
            {
                int e = _bySpawn[_nextSpawn++];
                if (Timeline.Entities[e].Despawn > t) { _active.Add(e); added = true; }
            }
            _active.RemoveAll(e => Timeline.Entities[e].Despawn <= t);
            if (added) _active.Sort();
        }

        public void Dispose()
        {
            _crowd.Dispose();
            _props.Dispose();
            Effects.Dispose();
            Night.Dispose();
            Pipeline.Dispose();
            Destroy(_root);
            foreach (var o in _made) Destroy(o);
        }

        /// <summary>Destroys an object in a player, or at once in the editor (the tests).</summary>
        public static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
