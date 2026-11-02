using System;
using UnityEngine;

namespace Champions.Client.Music
{
    /// <summary>
    /// Silences Unity's final mix: on the listener's object, its filter sees every source mixed just
    /// before the output and clears it, so a headless run's music plays (its audio callbacks run and the
    /// clock anchors) unheard. A zero listener volume instead may let the mixer skip the voices, and the
    /// music's filter with them.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class MuteOutput : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels) => Array.Clear(data, 0, data.Length);
    }
}
