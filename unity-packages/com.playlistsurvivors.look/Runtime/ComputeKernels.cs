using System;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// The player's hook for a system's compute kernels as the system loads (the crowd's, the shatter's): the game
    /// prepares them up front (its GraphicsStates), since a cold first dispatch stalled its frame 50 to 160 ms. Does
    /// nothing until a player sets <see cref="Prepare"/>.
    /// </summary>
    public static class ComputeKernels
    {
        public static Action<ComputeShader, int[]> Prepare = (shader, kernels) => { };
    }
}
