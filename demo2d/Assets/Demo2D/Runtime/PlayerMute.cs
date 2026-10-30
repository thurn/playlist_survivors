using UnityEngine;

namespace Demo2D.Runtime
{
    /// <summary>
    /// A built player starts muted, so the runs agents and checks start never play music at whoever
    /// is at the desk. `--sound` (which `just`'s play recipes pass) turns it up; the editor keeps
    /// its sound.
    /// </summary>
    public static class PlayerMute
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (!Application.isEditor && !Args.Has("--sound")) AudioListener.volume = 0f;
        }
    }
}
