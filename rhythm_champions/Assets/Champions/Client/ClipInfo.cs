using System;

namespace Champions.Client
{
    /// <summary>
    /// One animation clip's annotations, which the hero's and the bosses' motion read (<see cref="Hero.HeroMotion"/>,
    /// <see cref="BossAnimator"/>): its role (what the motion plays it for: idle, run, slashA, smash, ...), its name, its
    /// length in seconds and whether it loops, and its impact, the end of its follow-through and its stride (s, s, m;
    /// negative where absent).
    /// </summary>
    [Serializable]
    public struct ClipInfo
    {
        public string Role;
        public string Name;
        public float Length;
        public bool Loop;
        public float Impact, End, StrideM;

        public ClipInfo(string role, string name, float length, float impact = -1, float end = -1, bool loop = false, float strideM = -1)
        {
            Role = role;
            Name = name;
            Length = length;
            Loop = loop;
            Impact = impact;
            End = end;
            StrideM = strideM;
        }
    }
}
