using System;
using Champions.Sim;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using SimKey = Champions.Sim.Key;

namespace Champions.Client.Loop
{
    /// <summary>
    /// The sim's keys from the Input System's keyboard events, each with its event's own timestamp (ported from game/'s
    /// KeyInput; design.md, Controls): WASD and the arrows move, Space dashes, F cleaves, and 1-3, the arrows and Enter or Space
    /// answer a card moment, which the sim reads (a card's click taps its number, <see cref="Tap"/>). An event's time is on
    /// real time's timeline (Time.realtimeSinceStartup), which the clock maps to the song time heard
    /// (<see cref="IGameClock.KeySong"/>). Two physical keys on one direction press it once and release it when both are
    /// up. Esc is no sim key: the pause menu's (UI.Flow).
    ///
    /// Events are collected as the Input System processes them, before the frame's scripts run; <see cref="Drain"/>
    /// hands them to the sim in order.
    /// </summary>
    public sealed class KeyInput : IDisposable
    {
        public const int Capacity = 256;
        /// <summary>Raw key-event and mapped heard timestamps for local diagnostics.</summary>
        public Action<SimKey, double, double> ActionTimestamp;

        public struct Change
        {
            public SimKey Key;
            public bool Down;
            /// <summary>The event's timestamp: real time.</summary>
            public double Real;
            /// <summary>A click's tap (<see cref="Tap"/>): pressed and let go at once.</summary>
            public bool Tapped;
        }

        static readonly (string path, SimKey key)[] Bindings =
        {
            ("upArrow", SimKey.Up), ("w", SimKey.Up),
            ("downArrow", SimKey.Down), ("s", SimKey.Down),
            ("leftArrow", SimKey.Left), ("a", SimKey.Left),
            ("rightArrow", SimKey.Right), ("d", SimKey.Right),
            ("space", SimKey.Space), ("f", SimKey.F),
            ("enter", SimKey.Enter), ("numpadEnter", SimKey.Enter),
            ("1", SimKey.One), ("numpad1", SimKey.One),
            ("2", SimKey.Two), ("numpad2", SimKey.Two),
            ("3", SimKey.Three), ("numpad3", SimKey.Three),
        };

        readonly bool[] _physical = new bool[Bindings.Length];
        readonly int[] _held = new int[(int)SimKey.F + 1];
        readonly bool[] _pressed = new bool[(int)SimKey.F + 1]; // as the sim has them
        readonly Change[] _queue = new Change[Capacity];
        readonly KeyControl[] _controls = new KeyControl[Bindings.Length];
        Keyboard _keyboard;
        int _head, _count;

        public KeyInput() => InputSystem.onEvent += OnEvent;

        public void Dispose() => InputSystem.onEvent -= OnEvent;

        /// <summary>Key changes dropped because the queue was full.</summary>
        public int Dropped { get; private set; }

        /// <summary>While suspended (a menu over the run), changes are dropped and the sim gets none.</summary>
        public bool Suspended { get; private set; }

        /// <summary>A device's event, as the Input System hands it over (the tests hand keyboard events in directly).</summary>
        internal void OnEvent(InputEventPtr ev, InputDevice device)
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
                if (down ? _held[k] == 1 : _held[k] == 0) Enqueue(new Change { Key = Bindings[i].key, Down = down, Real = ev.time });
            }
        }

        void Enqueue(in Change change)
        {
            if (Suspended) return;
            if (_count == Capacity)
            {
                Dropped++;
                return;
            }
            _queue[(_head + _count++) % Capacity] = change;
        }

        /// <summary>
        /// Taps <paramref name="key"/> at real time <paramref name="real"/> as if pressed and let go, for a card's click: the
        /// sim records it like any key, so a session replays it.
        /// </summary>
        public void Tap(SimKey key, double real) => Enqueue(new Change { Key = key, Down = true, Real = real, Tapped = true });

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

        /// <summary>Presses and releases every collected change in the sim, heard at the clock's keySong; how many it gave.</summary>
        public int Drain(Simulation sim, IGameClock clock)
        {
            int n = 0;
            while (TryTake(out var c))
            {
                double heard = clock.KeySong(c.Real);
                if (c.Down && (c.Key == SimKey.F || c.Key == SimKey.Space)) ActionTimestamp?.Invoke(c.Key, c.Real, heard);
                Apply(sim, c.Key, c.Down, heard);
                n++;
                if (!c.Tapped) continue;
                Apply(sim, c.Key, false, heard);
                n++;
            }
            return n;
        }

        void Apply(Simulation sim, SimKey key, bool down, double heard)
        {
            if (_pressed[(int)key] == down) return; // a tap of a key held, or a release the sim never pressed
            _pressed[(int)key] = down;
            if (down) sim.Press(key, heard);
            else sim.Release(key, heard);
        }

        /// <summary>
        /// A menu opens over the run: the changes collected go to the sim, every key it has down is let go at
        /// <paramref name="heard"/>, and changes are dropped until <see cref="Resume"/>, so a key held over the menu walks
        /// nobody after it.
        /// </summary>
        public void Suspend(Simulation sim, IGameClock clock, double heard)
        {
            Drain(sim, clock);
            for (int k = 0; k < _pressed.Length; k++)
                if (_pressed[k]) Apply(sim, (SimKey)k, false, heard);
            Suspended = true;
        }

        /// <summary>The menu closed: keys count again from their next press.</summary>
        public void Resume() => Suspended = false;
    }
}
