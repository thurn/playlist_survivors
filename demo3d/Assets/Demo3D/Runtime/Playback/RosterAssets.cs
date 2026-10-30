using System;
using UnityEngine;

namespace Demo3D.Runtime.Playback
{
    /// <summary>
    /// What the player needs of the roster at runtime, baked by the editor (RosterBaker) into
    /// the gitignored Assets/Baked/Roster/: each type's ready prefab (scaled to its demo height,
    /// URP materials, weapons attached, no vendor scripts) and its clips in roster order. A
    /// `--roster` set's index lives under Assets/Baked/Rosters/NAME/ (<see cref="RosterSet"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Demo3D/Roster Assets")]
    public sealed class RosterAssets : ScriptableObject
    {
        public const string ResourcePath = "Demo3DRosterAssets";

        [Serializable]
        public sealed class TypeAssets
        {
            public string id;
            public string kind;
            public GameObject prefab;
            public string[] roles;
            public AnimationClip[] clips;
            public float radius;
            public float heightM;
            public float hoverM;

            public int ClipIndex(string role) => Array.IndexOf(roles, role);
        }

        public TypeAssets[] types;

        public TypeAssets Find(string id) => Array.Find(types, t => t.id == id);

        /// <summary>The active `--roster` set's index.</summary>
        public static RosterAssets Load()
        {
            var set = RosterSet.Active;
            return Resources.Load<RosterAssets>(set.AssetsResource)
                ?? throw new InvalidOperationException(set.IsDefault
                    ? "RosterAssets: not baked (run demo3d unity BakeAll)"
                    : $"RosterAssets: --roster {set.Name} is not baked (run demo3d setup --roster {set.Name}, then demo3d unity BakeRoster -roster {set.Name})");
        }
    }
}
