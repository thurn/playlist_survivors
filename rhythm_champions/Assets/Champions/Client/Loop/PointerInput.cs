using System;
using Champions.Client.UI;
using Champions.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Champions.Client.Loop
{
    /// <summary>
    /// Click-to-move from the Input System's mouse events (design.md, Controls; plan.md, The Client > Input): the left
    /// button's press walks the hero straight to the ground point under the pointer, and while the button is held the hero
    /// follows the pointer, one target a frame. Each target is the pointer cast through the run's camera onto the ground
    /// (y 0, <see cref="Ground"/>) and given as <see cref="Simulation.Target"/>, heard at the clock's keySong of the press's
    /// own timestamp (a held frame's: the frame's), so the sim records it like any input and a session replays it. A
    /// movement key drops the target in the sim.
    ///
    /// Never through the UI: a press over an element that takes the pointer (a card, a menu row:
    /// <see cref="UiPanel.TakesPointer"/>) is the UI's, and so is the hold it begins. No targets while a menu is over the
    /// run (the keys suspended) or the world is held (the cards up, a pause): either ends the hold, so a button held across
    /// them walks nobody after; the hero follows again from the next press.
    /// </summary>
    public sealed class PointerInput : IDisposable
    {
        public const int Capacity = 64;

        struct Press
        {
            /// <summary>The pointer, in screen pixels from the bottom left.</summary>
            public Vector2 At;
            /// <summary>The event's timestamp: real time.</summary>
            public double Real;
        }

        readonly Press[] _queue = new Press[Capacity];
        int _head, _count;
        Vector2 _at;
        bool _down, _following;

        public PointerInput() => InputSystem.onEvent += OnEvent;

        public void Dispose() => InputSystem.onEvent -= OnEvent;

        /// <summary>Presses dropped because the queue was full.</summary>
        public int Dropped { get; private set; }

        /// <summary>A device's event, as the Input System hands it over (the tests hand mouse events in directly).</summary>
        internal void OnEvent(InputEventPtr ev, InputDevice device)
        {
            if (!(device is Mouse mouse) || !(ev.IsA<StateEvent>() || ev.IsA<DeltaStateEvent>())) return;
            if (mouse.position.ReadValueFromEvent(ev, out var at)) _at = at;
            if (!mouse.leftButton.ReadValueFromEvent(ev, out float value)) return;
            bool down = value >= 0.5f;
            if (down == _down) return;
            _down = down;
            if (!down) return;
            if (_count == Capacity)
            {
                Dropped++;
                return;
            }
            _queue[(_head + _count++) % Capacity] = new Press { At = _at, Real = ev.time };
        }

        /// <summary>
        /// Gives the sim the frame's targets through <paramref name="camera"/>: each press's, then, if the button is held
        /// and was not pressed this frame, the pointer's ground point at real time <paramref name="real"/>. None while
        /// <paramref name="suspended"/> (a menu over the run) or the world is held. How many it gave.
        /// </summary>
        public int Drain(Simulation sim, IGameClock clock, Camera camera, bool suspended, double real)
        {
            bool blocked = suspended || sim.State.Held, pressed = _count > 0;
            int n = 0;
            while (TryTake(out var press))
            {
                _following = !blocked && !UiPanel.TakesPointer(press.At);
                if (_following && Give(sim, camera, press.At, clock.KeySong(press.Real))) n++;
            }
            if (blocked) _following = false;
            if (!pressed && _down && _following && Give(sim, camera, _at, clock.KeySong(real))) n++;
            return n;
        }

        bool TryTake(out Press press)
        {
            press = _queue[_head];
            if (_count == 0) return false;
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }

        static bool Give(Simulation sim, Camera camera, Vector2 at, double heard)
        {
            if (!Ground(camera, at, out var point)) return false;
            sim.Target(point.x, point.y, heard);
            return true;
        }

        /// <summary>
        /// The ground point under screen point <paramref name="at"/> (pixels from the bottom left) as
        /// <paramref name="camera"/> sees it, in the sim's plane (the world's x and z); false if the pointer is above the
        /// horizon.
        /// </summary>
        public static bool Ground(Camera camera, Vector2 at, out Vector2 point)
        {
            var ray = camera.ScreenPointToRay(at);
            point = default;
            if (ray.direction.y > -1e-4f || ray.origin.y <= 0) return false;
            var hit = ray.GetPoint(-ray.origin.y / ray.direction.y);
            point = new Vector2(hit.x, hit.z);
            return true;
        }
    }
}
