using System;
using System.Collections.Generic;
using Game.Client.Loop;
using Game.Client.Look;
using Game.Client.Music;
using Game.Client.Title;
using PS.Game.Sim;
using PS.Game.Sim.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Client.Hud
{
    /// <summary>
    /// The run's HUD (unity-game.md, HUD; native's slice/hud.rs, cards.rs, treasure.rs, recap.rs and
    /// damage.rs): one UI Toolkit overlay (Resources/Hud: Hud.uxml and Hud.uss) in faceted crystal,
    /// keeping native's layout and hierarchy, and the beat gem and life ring in world space
    /// (<see cref="BeatGem"/>). Everything shows frameSong:
    /// - experience and the level along the top, health top left (a bite flashes it white, and under
    ///   30% it glows red on each beat), the source tag under it, kills top right, the run clock
    ///   bottom left, and the arsenal's six weapon and six passive slots (<see cref="SlotsView"/>);
    /// - the beat track (<see cref="BeatTrack"/>) and, while the run waits, its start prompt;
    /// - damage numbers over the enemies, times 10 (<see cref="DamageNumbers"/>);
    /// - banners: MUSIC STOPPED, and PAUSED when no pause menu is up (the menu's own heading is the
    ///   banner then);
    /// - the level-up's cards flipping in on their beats (<see cref="LevelUpCards"/>), a chest's
    ///   roulette (<see cref="ChestCards"/>), and the victory or YOU FELL card with each weapon's
    ///   damage (<see cref="EndCard"/>).
    /// Kills, bites and numbers come from the sim's events, each shown once frameSong reaches it;
    /// the slots and the end card's damage read the sim's state at the frontier, which they match
    /// at the moments they change (in a freeze, after the end).
    /// </summary>
    public sealed class HudPresenter : IPresenter, IDisposable
    {
        /// <summary>A bite flashes health white this long (s); under this share of health it pulses.</summary>
        public const double BiteFlashS = 0.2, LowHealth = 0.3;

        readonly Simulation _sim;
        readonly ScreenPanel _panel;
        readonly bool _interactive;
        readonly Label _level, _kills, _timer, _prompt, _bannerText, _delay, _source;
        readonly VisualElement _xpFill, _hpFill, _hpPulse, _banner;
        readonly SlotsView _slots;
        readonly BeatTrack _track;
        readonly LevelUpCards _cards;
        readonly ChestCards _chest;
        readonly EndCard _end;
        readonly DamageNumbers _numbers;
        readonly BeatGem _gem;
        readonly Queue<double> _deaths = new Queue<double>();
        readonly List<double> _bites = new List<double>();
        readonly double[] _heldFrom;
        int _killCount;
        double _bitten = double.NegativeInfinity;

        /// <summary>The hero as drawn (the hero presenter's), else the sim's.</summary>
        public LookPresenter.HeroAt DrawnHero { get; set; }

        /// <summary>The pause menu, whose heading stands in for the PAUSED banner while it is up.</summary>
        public PauseMenu Menu { get; set; }

        public HudPresenter(Transform parent, Camera camera, Simulation sim, GameContent content, MusicSource source, bool interactive)
        {
            _sim = sim;
            _interactive = interactive;
            _panel = new ScreenPanel(parent, "HUD", "Hud/Hud", overlay: true);
            var root = _panel.Root;
            _level = root.Q<Label>("level");
            _kills = root.Q<Label>("kills");
            _timer = root.Q<Label>("timer");
            _prompt = root.Q<Label>("prompt");
            _banner = root.Q("banner");
            _bannerText = root.Q<Label>("banner-text");
            _xpFill = root.Q("xp-fill");
            _hpFill = root.Q("hp-fill");
            _hpPulse = root.Q("hp-pulse");
            _delay = root.Q<Label>("delay");
            _source = root.Q<Label>("source");
            _delay.text = source != null ? $"DELAYED {MusicPlayer.M:0} S" : "";
            _source.text = source?.Tag ?? "";
            _slots = new SlotsView(root.Q("weapons"), root.Q("passives"), content);
            _track = new BeatTrack(root.Q("track"));
            _cards = new LevelUpCards(root.Q("cards-layer"), content);
            _chest = new ChestCards(root.Q("chest-layer"), content, Args.Get("--chest-preview"));
            _end = new EndCard(root.Q("end-layer"), content);
            _numbers = new DamageNumbers(root.Q("numbers"), camera);
            _gem = new BeatGem(camera);
            _heldFrom = new double[content?.Weapons.Length ?? 0];
            for (int i = 0; i < _heldFrom.Length; i++) _heldFrom[i] = double.NaN;
        }

        public void OnEvent(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Death when e.AsDeath.Enemy >= 0:
                    _deaths.Enqueue(e.T);
                    break;
                case SimEventKind.Hit when e.AsHit.OnHero:
                    _bites.Add(e.T);
                    break;
                case SimEventKind.Hit:
                    _numbers.Add(e.AsHit);
                    break;
                case SimEventKind.Beat:
                    _numbers.Beat(e.T, e.AsBeat.Index, e.AsBeat.Period);
                    break;
                case SimEventKind.LevelUp:
                case SimEventKind.Card:
                    _cards.OnEvent(e);
                    break;
                case SimEventKind.RunEnd:
                    _end.Ended(e.T, e.AsRunEnd.Why);
                    break;
            }
        }

        public void Present(in PresentFrame frame)
        {
            double s = frame.Song;
            ref readonly var state = ref frame.State;
            var beat = HeardBeats.At(_sim.Stream, _sim.Beats, s);
            bool locked = state.Beat == Reliability.Reliable && beat.Has;
            // The beat's hit, 1 on it and decaying over it (hud.rs' exp(-8 phase)), 0 without a reliable beat.
            double since = beat.Has ? (s - beat.LastT) / Math.Max(1e-6, beat.Period) : 1;
            double hit = locked ? Math.Exp(-8 * Math.Max(0, Math.Min(1, since))) : 0;

            while (_deaths.Count > 0 && _deaths.Peek() <= s + 1e-9)
            {
                _deaths.Dequeue();
                _killCount++;
            }
            for (int i = _bites.Count - 1; i >= 0; i--)
            {
                if (_bites[i] > s + 1e-9) continue;
                _bitten = Math.Max(_bitten, _bites[i]);
                _bites.RemoveAt(i);
            }

            _level.text = "LV " + Math.Max(1, state.Level);
            _xpFill.style.width = Length.Percent((float)(100 * state.ExperienceFill));
            double health = state.Hero.MaxHealth > 0 ? Math.Max(0, Math.Min(1, state.Hero.Health / state.Hero.MaxHealth)) : 0;
            bool bitten = s - _bitten < BiteFlashS;
            _hpFill.style.width = Length.Percent((float)(100 * health));
            _hpFill.style.backgroundColor = bitten ? Color.white : health < LowHealth && hit > 0.5 ? HudText.Rgb(0xffb0b8) : HudText.Rgb(0xff4050);
            _hpPulse.style.opacity = health < LowHealth ? (float)(0.2 + 0.5 * (locked ? hit : 0.3)) : 0;
            _kills.text = _killCount.ToString();
            _timer.text = HudText.Clock(state.RunClock);
            KeepHeld(state.RunClock);
            _slots.Show(_sim.State.WeaponSlots, _sim.State.PassiveSlots);
            _track.Show(_sim.Stream, _sim.Beats, s, beat, locked);
            _prompt.text = state.Phase != RunPhase.Waiting ? "" : _interactive ? "PRESS ENTER OR SPACE" : "THE RUN STARTS ON THE BEAT";

            string banner = state.Phase == RunPhase.Ended ? null
                : state.FrozenFor(FreezeReason.MusicStopped) ? "MUSIC STOPPED"
                : state.FrozenFor(FreezeReason.Pause) && (Menu == null || !Menu.Open) ? "PAUSED"
                : null;
            _banner.style.display = banner != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (banner != null) _bannerText.text = banner;

            _cards.Present(s, state.Offer, _sim.State);
            _chest.Present(s, _sim, beat);
            _end.Present(s, state, _killCount, _sim.State, _heldFrom);

            double hx, hy;
            if (DrawnHero != null) DrawnHero(out hx, out hy, out _);
            else state.Hero.PositionAt(s, out hx, out hy);
            _numbers.Present(s, hx, hy);
            // The overhead display comes in over the run's first two seconds and goes with the end card.
            double fade = state.Phase == RunPhase.Ended ? 0 : Smooth((state.RunClock - 0.5) / 1.5);
            _gem.Present(s, new Vector3((float)hx, 0, (float)hy), beat, locked, hit, health, bitten, (float)fade);
        }

        /// <summary>When each weapon joined the slots, on the run clock (the end card's time held).</summary>
        void KeepHeld(double runClock)
        {
            foreach (var slot in _sim.State.WeaponSlots)
                if (slot.Index < _heldFrom.Length && double.IsNaN(_heldFrom[slot.Index])) _heldFrom[slot.Index] = runClock;
        }

        static double Smooth(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        public void Dispose()
        {
            _gem.Dispose();
            _panel.Dispose();
        }
    }
}
