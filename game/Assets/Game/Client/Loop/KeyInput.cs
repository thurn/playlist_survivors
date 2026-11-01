using System;
using System.Linq;
using PS.Game.Sim;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using SimKey = PS.Game.Sim.Key;

namespace Game.Client.Loop
{
    /// <summary>
    /// The sim's keys from the Input System's keyboard events, with each event's own timestamp
    /// (unity-game.md, Key Presses): the arrows and WASD steer, Space stomps, Z attacks in a mode whose sword waits
    /// for it (<see cref="SwordTrigger.Press"/>), and Enter (with the arrows and Space) answers a level-up's cards;
    /// a click on a card taps its keys
    /// (<see cref="Tap"/>). An event's time is on real time's timeline (Time.realtimeSinceStartup), which the clock maps
    /// to the song time heard (<see cref="IGameClock.KeySong"/>). Two physical keys on one
    /// direction press it once and release it when both are up.
    ///
    /// Events are collected as the Input System processes them, before the frame's scripts run;
    /// <see cref="Drain"/> hands them to the sim in order.
    /// </summary>
    public sealed class KeyInput : IDisposable
    {
        public const int Capacity = 256;

        public struct Change
        {
            public SimKey Key;
            public bool Down;
            /// <summary>The event's timestamp: real time.</summary>
            public double Real;
            /// <summary>
            /// A click's tap (<see cref="Tap"/>), answering the level-up offer with serial <see cref="Offer"/>:
            /// a press and a release (Down), or a release and a press again of a key held down (not Down).
            /// </summary>
            public bool Tapped;
            public int Offer;
        }

        static readonly (string path, SimKey key)[] Common =
        {
            ("upArrow", SimKey.Up), ("w", SimKey.Up),
            ("downArrow", SimKey.Down), ("s", SimKey.Down),
            ("leftArrow", SimKey.Left), ("a", SimKey.Left),
            ("rightArrow", SimKey.Right), ("d", SimKey.Right),
            ("space", SimKey.Space),
            ("enter", SimKey.Enter), ("numpadEnter", SimKey.Enter),
        };

        readonly (string path, SimKey key)[] _bindings;
        readonly bool[] _physical;
        readonly int[] _held = new int[(int)SimKey.Attack + 1];
        readonly Change[] _queue = new Change[Capacity];
        readonly KeyControl[] _controls;
        Keyboard _keyboard;
        int _head, _count;

        /// <summary>The keys of the grid mode, which every screen but a run's uses.</summary>
        public KeyInput() : this(GameModes.Of(GameMode.Grid)) { }

        /// <summary>The keys of a run on <paramref name="rules"/>: Z only where it attacks.</summary>
        public KeyInput(ModeRules rules)
        {
            _bindings = rules.Sword == SwordTrigger.Press ? Common.Append(("z", SimKey.Attack)).ToArray() : Common;
            _physical = new bool[_bindings.Length];
            _controls = new KeyControl[_bindings.Length];
            InputSystem.onEvent += OnEvent;
        }

        public void Dispose() => InputSystem.onEvent -= OnEvent;

        /// <summary>Key changes dropped because the queue was full.</summary>
        public int Dropped { get; private set; }

        void OnEvent(InputEventPtr ev, InputDevice device)
        {
            if (!(device is Keyboard keyboard) || !(ev.IsA<StateEvent>() || ev.IsA<DeltaStateEvent>())) return;
            if (keyboard != _keyboard)
            {
                _keyboard = keyboard;
                for (int i = 0; i < _bindings.Length; i++) _controls[i] = (KeyControl)keyboard[_bindings[i].path];
            }
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (!_controls[i].ReadValueFromEvent(ev, out float value)) continue;
                bool down = value >= 0.5f;
                if (down == _physical[i]) continue;
                _physical[i] = down;
                int k = (int)_bindings[i].key;
                _held[k] += down ? 1 : -1;
                if (down ? _held[k] == 1 : _held[k] == 0) Enqueue(_bindings[i].key, down, ev.time);
            }
        }

        void Enqueue(in Change change)
        {
            if (_count == Capacity)
            {
                Dropped++;
                return;
            }
            _queue[(_head + _count++) % Capacity] = change;
        }

        void Enqueue(SimKey key, bool down, double real) => Enqueue(new Change { Key = key, Down = down, Real = real });

        /// <summary>
        /// Taps a key at real time <paramref name="real"/> as if it were pressed and let go, for the
        /// mouse's picks on the level-up offer with serial <paramref name="offer"/>: the sim records it
        /// like any key, so a session replays it. A key held down meanwhile is let go and pressed
        /// again, so the sim still has it held after. <see cref="Drain"/> drops the tap unless the sim
        /// still has that offer open then: a card still on show after the sim took it (a key just
        /// before took it, or it closed) would otherwise walk the hero.
        /// </summary>
        public void Tap(SimKey key, double real, int offer) =>
            Enqueue(new Change { Key = key, Down = _held[(int)key] == 0, Real = real, Tapped = true, Offer = offer });

        /// <summary>Takes the oldest collected change, if any.</summary>
        public bool TryTake(out Change change)
        {
            change = default;
            if (_count == 0) return false;
            change = _queue[_head];
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }

        /// <summary>
        /// Presses and releases every collected change in the sim, heard at the clock's keySong, and
        /// returns how many key changes it made: a tap is two, or none once its offer is gone.
        /// </summary>
        public int Drain(Simulation sim, IGameClock clock)
        {
            int n = 0;
            while (TryTake(out var c))
            {
                double heard = clock.KeySong(c.Real);
                if (!c.Tapped)
                {
                    Apply(sim, c.Key, c.Down, heard);
                    n++;
                    continue;
                }
                var offer = sim.State.Offer;
                if (!offer.Open || offer.Serial != c.Offer) continue;
                Apply(sim, c.Key, c.Down, heard);
                Apply(sim, c.Key, !c.Down, heard);
                n += 2;
            }
            return n;
        }

        static void Apply(Simulation sim, SimKey key, bool down, double heard)
        {
            if (down) sim.Press(key, heard);
            else sim.Release(key, heard);
        }
    }
}
