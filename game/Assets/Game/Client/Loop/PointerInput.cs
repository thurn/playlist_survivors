using System;
using PS.Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Loop
{
    /// <summary>
    /// The pointer's targets for a mode with one (<see cref="ModeRules.Pointer"/>, docs/design/game-modes.md): a left
    /// click on the arena sends the hero to the ground point under the cursor, and while the button is held, each
    /// frame the point under the cursor moves <see cref="StepM"/> or more (the cursor, or the camera following the
    /// hero) sends it again, so the hero follows the cursor. Each is a <see cref="Simulation.Target"/>, heard at the
    /// frame's keySong, which the sim records like a key.
    ///
    /// Only a played run's clicks count: a click that starts the run, lands on the HUD's pause button, or comes
    /// while the world is frozen (the cards, the pause menu) sends nothing, and holding it on sends nothing either.
    /// </summary>
    public sealed class PointerInput
    {
        /// <summary>A held cursor's point must move this far (m) to send a new target: the camera's drift alone sends one a frame.</summary>
        public const float StepM = 0.05f;

        bool _held;
        Vector2 _last;

        /// <summary>
        /// Sends this frame's target, if any, for the cursor over <paramref name="camera"/>'s view; a press for which
        /// <paramref name="taken"/> says the HUD has the point never steers. Returns how many it sent.
        /// </summary>
        public int Apply(Simulation sim, IGameClock clock, Camera camera, Func<Vector2, bool> taken)
        {
            var mouse = Mouse.current;
            if (mouse == null || camera == null) return 0;
            var button = mouse.leftButton;
            var screen = mouse.position.ReadValue();
            bool playing = sim.State.Phase == RunPhase.Playing && !sim.State.Frozen;
            if (button.wasPressedThisFrame) _held = playing && !taken(screen);
            if (!button.isPressed || !playing) _held = false;
            if (!_held) return 0;
            var ray = camera.ScreenPointToRay(screen);
            if (ray.direction.y > -1e-4f) return 0; // at or above the horizon
            var hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            var ground = new Vector2(hit.x, hit.z);
            if (!button.wasPressedThisFrame && (ground - _last).sqrMagnitude < StepM * StepM) return 0;
            _last = ground;
            sim.Target(ground.x, ground.y, clock.KeySong(Time.realtimeSinceStartupAsDouble));
            return 1;
        }
    }
}
