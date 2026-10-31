using System;
using PS.Game.Sim.Content;
using UnityEngine;

namespace Game.Client.Effects
{
    /// <summary>
    /// What effects and sounds need of a role (cast.json and enemies.json): its size, 1 for a
    /// standard body (radius 0.37 m at scale 1), and its crystal's gem colour. Without content (a
    /// scripted feed) every role is a standard body in ice-blue crystal.
    /// </summary>
    public sealed class RoleLooks
    {
        public const double StandardRadiusM = 0.3704;
        public static readonly Color Ice = new Color(0.55f, 0.9f, 1f);

        readonly float[] _size;
        readonly Color[] _gem;

        public RoleLooks(GameContent content)
        {
            int n = content?.Roles.Length ?? 0;
            _size = new float[n];
            _gem = new Color[n];
            for (int i = 0; i < n; i++)
            {
                var role = content.Roles[i];
                var cast = Array.Find(content.Cast, c => c.Role == role.Id);
                _size[i] = (float)((cast?.Scale ?? 1) * role.RadiusM / StandardRadiusM);
                _gem[i] = cast != null && ColorUtility.TryParseHtmlString(cast.Crystal, out var c) ? c : Ice;
            }
        }

        public float Size(int role) => role >= 0 && role < _size.Length ? _size[role] : 1f;

        public Color Gem(int role) => role >= 0 && role < _gem.Length ? _gem[role] : Ice;
    }
}
