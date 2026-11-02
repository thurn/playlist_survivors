using System.Collections.Generic;
using System.Globalization;
using Champions.Client.Loop;
using Champions.Render;
using Champions.Render.Crowd;
using Champions.Sim;
using UnityEngine;

namespace Champions.Client.Scenes
{
    /// <summary>
    /// `level`: level 1 as it plays, through the run's own presentation (<see cref="RunPresentation"/>), the reference player
    /// (`--walk reference`) on a scripted 128 BPM song: the sim runs ahead to minute 4 without drawing, then draws. Its stills:
    /// the crowd at minute 4 (`crowd`, the big orks just joined), a loud surge with the formed wave its section change
    /// brings (`surge`, 8 s into a loud stretch), and a card moment over the held world (`cards`: the sim runs ahead again to
    /// the next level-up's cards, then the scene holds the pick a second; the song's 4 s gap is a quiet moment for one).
    /// </summary>
    sealed class LevelScene : IStillScene, INamedStills
    {
        public const string Name = "level";
        public const double Bpm = 128, CrowdAt = 240.5, LoudFrom = 244, SurgeAt = 252, GapFrom = 256, GapTo = 260, SongEnd = 420, CardsShownS = 0.5;
        /// <summary>The scene gives up on a card moment by then.</summary>
        public const double GiveUpAt = 400;

        readonly GameContent _content;
        readonly Palette _palette;
        readonly string _contentDir;
        readonly MonsterLook _look;
        LevelSceneDriver _driver;

        public LevelScene(GameContent content, Palette palette, string contentDir, MonsterLook look)
        {
            _content = content;
            _palette = palette;
            _contentDir = contentDir;
            _look = look;
        }

        /// <summary>The scene ends when its driver is done: a far time, kept as its last still (`end`).</summary>
        public double[] Times => new[] { _driver != null && _driver.Done ? 0 : double.MaxValue };

        public string Moment => _driver?.Moment;

        public string Label(int index) => "end";

        public void Begin(Transform root, Camera camera, Stills.View view)
        {
            _driver = root.gameObject.AddComponent<LevelSceneDriver>();
            _driver.Begin(camera, _content, _palette, _contentDir, _look, view);
            for (int c = 0; c < _content.Enemies.Cast.Length; c++)
            {
                var type = _content.Enemies.Cast[c];
                view.Ids[IdPass.Monster(c)] = (type.Id, type.Role.ToString().ToLowerInvariant(), (float)type.SizeM);
            }
            view.Ids[IdPass.Hero] = ("hero", "hero", 1.85f);
            view.Ids[IdPass.Structure] = ("structures", "structure", 3f);
            view.Ids[IdPass.Effect] = ("hero effects, telegraphs, spores and shards", "effect", 1.8f);
        }
    }

    /// <summary>Plays <see cref="LevelScene"/>: runs ahead, then draws a frame a frame; torn down with its root.</summary>
    [DefaultExecutionOrder(-100)]
    sealed class LevelSceneDriver : MonoBehaviour
    {
        const double Frame = 1.0 / Boot.FixedFps;
        static readonly HashSet<string> RunningAhead = new HashSet<string>(RunPresentation.Keepers) { "script", "ui" };

        SimDriver _driver;
        RunPresentation _presentation;
        WorldPresenter _world;
        ScriptedPlayer _script;
        List<StreamEntry> _entries;
        Camera _camera;
        Stills.View _view;
        double _frame, _offerAt = double.NaN;
        int _next;
        bool _crowdKept, _surgeKept, _cardsKept, _ranAhead;

        public string Moment { get; private set; }
        public bool Done { get; private set; }

        public void Begin(Camera camera, GameContent content, Palette palette, string contentDir, MonsterLook look, Stills.View view)
        {
            _camera = camera;
            _view = view;
            _entries = new ScriptedSong().Beats(0, LevelScene.GapFrom, LevelScene.Bpm).Gap(LevelScene.GapFrom, LevelScene.GapTo)
                .Beats(LevelScene.GapTo, LevelScene.SongEnd, LevelScene.Bpm).Loud(LevelScene.LoudFrom, LevelScene.GapFrom)
                .Change(LevelScene.LoudFrom).Entries();
            var run = new RunInputs { Seed = 21, Level = true, Start = RunStart.FirstOffer };
            var config = run.Build(content);
            _driver = new SimDriver(config, new FrameClock());
            _script = ScriptedPlayer.Parse("reference", null, content.Dashes);
            _world = new WorldPresenter(camera, palette);
            var ui = new UI.RunUi(transform, palette, content, UI.CardFaces.Load(contentDir, content), camera.gameObject.layer);
            _presentation = new RunPresentation(_driver.Sim, config, content, contentDir, palette, camera, _world, ui, null, gameObject, look);
            var sim = _driver.Sim;
            _presentation.Dispatch.Add("script", (in SimEvent e) => _script.Take(sim, e));
            view.Requeue = () => _presentation.Queue(_camera);
            Debug.Log($"Level: level 1 on a scripted {LevelScene.Bpm} BPM song, the reference player, running ahead to {LevelScene.CrowdAt - 0.5} s");
        }

        /// <summary>One frame of song: entries in, the script's inputs, the sim stepped, its events out (to the keepers alone when running ahead).</summary>
        bool Step(bool drawn)
        {
            _frame += Frame;
            double needed = _driver.StampNeededFor(FrameClock.FrontierAt(_frame));
            while (_next < _entries.Count && _entries[_next].Stamp <= needed) _driver.AddSettled(_entries[_next++]);
            var sim = _driver.Sim;
            if (_driver.Clock.Ready && sim.State.Phase != RunPhase.Ended)
            {
                // The card moment's pick waits a little, so the cards are seen.
                bool holding = sim.State.Offer.Up && sim.State.Offer.Kind == OfferKind.LevelUp && drawn
                               && (double.IsNaN(_offerAt) || _frame < _offerAt + 2 * LevelScene.CardsShownS);
                if (!holding) _script.Frame(sim, _driver.Times.Heard);
            }
            if (!_driver.Step(new ClockReadings { Real = Time.realtimeSinceStartupAsDouble, Frame = _frame })) return false;
            if (drawn) _presentation.Drain();
            else _presentation.Drain(RunningAhead);
            return true;
        }

        void Update()
        {
            Moment = null;
            if (_driver == null || Done) return;
            var sim = _driver.Sim;
            if (!_ranAhead)
            {
                _ranAhead = true;
                var started = Time.realtimeSinceStartupAsDouble;
                while (_frame < LevelScene.CrowdAt - 0.5) Step(drawn: false);
                Log("Level: ran ahead to song {0:F3} s in {1:F1} s: {2} alive, level {3}, health {4:F0}", _driver.Times.FrameSong,
                    Time.realtimeSinceStartupAsDouble - started, sim.Enemies.Alive, sim.State.Level, sim.State.Hero.Health);
            }
            // After the surge the scene runs ahead again, to the next level-up's cards (unpicked: the script picks before a step).
            if (_surgeKept && !_cardsKept && double.IsNaN(_offerAt))
            {
                while (!LevelUpOffered(sim) && _frame < LevelScene.GiveUpAt && sim.State.Phase != RunPhase.Ended) Step(drawn: false);
                if (LevelUpOffered(sim)) _offerAt = _frame;
            }
            Step(drawn: true);
            double song = _driver.Times.FrameSong;
            _presentation.Present(song, _driver.Times.Frontier, (float)Frame, _camera);
            sim.Snapshot().HeroAt(song, out double hx, out double hy);
            _view.Hero = new Vector3((float)hx, 0, (float)hy);
            _view.Impact = _world.World.Rings.Special > 0;
            if (!_crowdKept && song >= LevelScene.CrowdAt) Keep(ref _crowdKept, "crowd");
            else if (!_surgeKept && song >= LevelScene.SurgeAt) Keep(ref _surgeKept, "surge");
            else if (!_cardsKept && !double.IsNaN(_offerAt) && _frame >= _offerAt + LevelScene.CardsShownS) Keep(ref _cardsKept, "cards");
            if (_cardsKept || song >= LevelScene.GiveUpAt || sim.State.Phase == RunPhase.Ended)
            {
                if (!_cardsKept) Log("Level: no card moment by song {0:F1} s (the run {1}): no cards still", song, sim.State.Phase);
                Done = true;
            }
        }

        static bool LevelUpOffered(Simulation sim) => sim.State.Offer.Up && sim.State.Offer.Kind == OfferKind.LevelUp;

        void Keep(ref bool kept, string label)
        {
            kept = true;
            Moment = label;
            var sim = _driver.Sim;
            Log("Level: {0} at song {1:F3} s: {2} alive, level {3}, health {4:F0}, run clock {5:F1} s", label, _driver.Times.FrameSong,
                sim.Enemies.Alive, sim.State.Level, sim.State.Hero.Health, sim.State.RunClock);
        }

        void OnDestroy()
        {
            _presentation?.Dispose();
            _presentation?.Ui?.Dispose();
            _world?.Dispose();
        }

        static void Log(string format, params object[] args) => Debug.Log(string.Format(CultureInfo.InvariantCulture, format, args));
    }
}
