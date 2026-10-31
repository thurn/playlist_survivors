using System;
using PS.Game.Sim;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using SimKey = PS.Game.Sim.Key;

namespace Game.Client.Loop
{
    /// <summary>
    /// The sim's keys from the Input System's keyboard events, with each event's own timestamp
    /// (unity-game.md, Key Presses): the arrows and WASD steer, Space stomps. An
    /// event's time is on real time's timeline (Time.realtimeSinceStartup), which the clock maps
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
        }

        static readonly (string path, SimKey key)[] Bindings =
        {
            ("upArrow", SimKey.Up), ("w", SimKey.Up),
            ("downArrow", SimKey.Down), ("s", SimKey.Down),
            ("leftArrow", SimKey.Left), ("a", SimKey.Left),
            ("rightArrow", SimKey.Right), ("d", SimKey.Right),
            ("space", SimKey.Space),
        };

        readonly bool[] _physical = new bool[Bindings.Length];
        readonly int[] _held = new int[(int)SimKey.Space + 1];
        readonly Change[] _queue = new Change[Capacity];
        readonly KeyControl[] _controls = new KeyControl[Bindings.Length];
        Keyboard _keyboard;
        int _head, _count;

        public KeyInput() => InputSystem.onEvent += OnEvent;

        public void Dispose() => InputSystem.onEvent -= OnEvent;

        /// <summary>Key changes dropped because the queue was full.</summary>
        public int Dropped { get; private set; }

        void OnEvent(InputEventPtr ev, InputDevice device)
        {
            if (!(device is Keyboard keyboard) || !(ev.IsA<StateEvent>() || ev.IsA<DeltaStateEvent>())) return;
            if (keyboard != _keyboard)
            {
                _keyboard = keyboard;
                for (int i = 0; i < Bindings.Length; i++) _controls[i] = (KeyControl)keyboard[Bindings[i].path];
            }
            for (int i = 0; i < Bindings.Length; i++)
            {
                if (!_controls[i].ReadValueFromEvent(ev, out float value)) continue;
                bool down = value >= 0.5f;
                if (down == _physical[i]) continue;
                _physical[i] = down;
                int k = (int)Bindings[i].key;
                _held[k] += down ? 1 : -1;
                if (down ? _held[k] == 1 : _held[k] == 0) Enqueue(Bindings[i].key, down, ev.time);
            }
        }

        void Enqueue(SimKey key, bool down, double real)
        {
            if (_count == Capacity)
            {
                Dropped++;
                return;
            }
            _queue[(_head + _count++) % Capacity] = new Change { Key = key, Down = down, Real = real };
        }

        /// <summary>Presses and releases every collected change in the sim, heard at the clock's keySong.</summary>
        public int Drain(Simulation sim, IGameClock clock)
        {
            int n = _count;
            for (; _count > 0; _count--, _head = (_head + 1) % Capacity)
            {
                ref var c = ref _queue[_head];
                double heard = clock.KeySong(c.Real);
                if (c.Down) sim.Press(c.Key, heard);
                else sim.Release(c.Key, heard);
            }
            return n;
        }
    }
}
