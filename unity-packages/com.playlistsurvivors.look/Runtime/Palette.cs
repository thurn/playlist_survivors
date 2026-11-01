using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PS.Look
{
    /// <summary>
    /// A palette (neon-look.md, Palettes): colours by semantic role, never by object, from a JSON file in the package
    /// (Resources/Look/Palettes/NAME.json). A role is a top-level colour (<c>spark</c>) or a group's member
    /// (<c>hero.body</c>); <c>enemies</c> is the enemy hue slots and <c>zones</c> the named zone tints. Colours are
    /// sRGB hex. <see cref="SetShaderGlobals"/> gives every role to shaders.
    /// </summary>
    public sealed class Palette
    {
        /// <summary>The roles every palette sets (the doc's), besides at least one enemy hue.</summary>
        public static readonly string[] RequiredRoles =
        {
            "void.floor", "void.fog", "grid.line", "grid.pulse", "hero.body", "hero.blade", "hero.trail",
            "boss.body", "boss.telegraph", "shatter.gem", "shatter.core", "spark",
        };

        /// <summary>The most enemy hues and zones a palette holds: the length of their shader arrays.</summary>
        public const int MaxSlots = 8;

        public readonly string Name;
        /// <summary>The enemy hue slots, which monster type blocks map onto.</summary>
        public readonly Color[] Enemies;
        public readonly (string Name, Color Tint)[] Zones;
        readonly Dictionary<string, Color> _roles;

        Palette(string name, Dictionary<string, Color> roles, Color[] enemies, (string, Color)[] zones)
        {
            Name = name;
            _roles = roles;
            Enemies = enemies;
            Zones = zones;
        }

        /// <summary>Every role's name (<c>hero.body</c>), in file order.</summary>
        public IEnumerable<string> Roles => _roles.Keys;

        /// <summary>The colour of <paramref name="role"/> (<c>hero.body</c>).</summary>
        public Color Role(string role) =>
            _roles.TryGetValue(role, out var c) ? c : throw new KeyNotFoundException($"palette {Name}: no role {role}");

        public bool TryRole(string role, out Color colour) => _roles.TryGetValue(role, out colour);

        public Color VoidFloor => Role("void.floor");
        public Color VoidFog => Role("void.fog");
        public Color GridLine => Role("grid.line");
        public Color GridPulse => Role("grid.pulse");
        public Color HeroBody => Role("hero.body");
        public Color HeroBlade => Role("hero.blade");
        public Color HeroTrail => Role("hero.trail");
        public Color BossBody => Role("boss.body");
        public Color BossTelegraph => Role("boss.telegraph");
        public Color ShatterGem => Role("shatter.gem");
        public Color ShatterCore => Role("shatter.core");
        public Color Spark => Role("spark");

        /// <summary>
        /// The shader global a role sets: <c>_Look</c> and the role's parts capitalized (<c>hero.body</c>:
        /// <c>_LookHeroBody</c>). The enemy hues are <c>_LookEnemies[8]</c> with <c>_LookEnemyCount</c>, the zone
        /// tints <c>_LookZones[8]</c> with <c>_LookZoneCount</c>, all linear.
        /// </summary>
        public static string GlobalName(string role)
        {
            var name = "_Look";
            foreach (var part in role.Split('.'))
                name += char.ToUpperInvariant(part[0]) + part.Substring(1);
            return name;
        }

        static readonly int IdEnemies = Shader.PropertyToID("_LookEnemies"), IdEnemyCount = Shader.PropertyToID("_LookEnemyCount");
        static readonly int IdZones = Shader.PropertyToID("_LookZones"), IdZoneCount = Shader.PropertyToID("_LookZoneCount");

        /// <summary>Sets every role as a shader global (<see cref="GlobalName"/>).</summary>
        public void SetShaderGlobals()
        {
            foreach (var kv in _roles) Shader.SetGlobalColor(GlobalName(kv.Key), kv.Value); // linear in a linear project
            Shader.SetGlobalVectorArray(IdEnemies, Linear(Enemies));
            Shader.SetGlobalInt(IdEnemyCount, Enemies.Length);
            Shader.SetGlobalVectorArray(IdZones, Linear(Array.ConvertAll(Zones, z => z.Tint)));
            Shader.SetGlobalInt(IdZoneCount, Zones.Length);
        }

        /// <summary>The colours as linear vectors, padded to <see cref="MaxSlots"/>: Unity fixes a global array's length on its first set.</summary>
        static Vector4[] Linear(Color[] colours)
        {
            var v = new Vector4[MaxSlots];
            for (int i = 0; i < colours.Length; i++) v[i] = colours[i].linear;
            return v;
        }

        /// <summary>Parses a palette file; throws <see cref="FormatException"/> on a malformed one or a missing role.</summary>
        public static Palette Parse(string json)
        {
            var o = JObject.Parse(json);
            var name = (string)o["name"] ?? throw new FormatException("palette: no name");
            var roles = new Dictionary<string, Color>();
            Color[] enemies = null;
            var zones = new List<(string, Color)>();
            foreach (var kv in o)
            {
                string where = $"palette {name}: {kv.Key}";
                switch (kv.Key)
                {
                    case "name":
                    case "description":
                    case "note":
                        break;
                    case "enemies":
                        if (!(kv.Value is JArray hues) || hues.Count == 0 || hues.Count > MaxSlots)
                            throw new FormatException($"{where}: 1 to {MaxSlots} colours");
                        enemies = new Color[hues.Count];
                        for (int i = 0; i < hues.Count; i++) enemies[i] = Hex(hues[i], $"{where}[{i}]");
                        break;
                    case "zones":
                        if (!(kv.Value is JArray zs) || zs.Count > MaxSlots) throw new FormatException($"{where}: at most {MaxSlots} zones");
                        foreach (var z in zs)
                        {
                            var zoneName = (string)z["name"] ?? throw new FormatException($"{where}: a zone has no name");
                            zones.Add((zoneName, Hex(z["tint"], $"{where} {zoneName}")));
                        }
                        break;
                    default:
                        if (kv.Value is JObject group)
                            foreach (var member in group) roles[kv.Key + "." + member.Key] = Hex(member.Value, $"{where}.{member.Key}");
                        else roles[kv.Key] = Hex(kv.Value, where);
                        break;
                }
            }
            if (enemies == null) throw new FormatException($"palette {name}: no enemies");
            foreach (var role in RequiredRoles)
                if (!roles.ContainsKey(role)) throw new FormatException($"palette {name}: no {role}");
            return new Palette(name, roles, enemies, zones.ToArray());
        }

        static Color Hex(JToken token, string where)
        {
            var s = token?.Type == JTokenType.String ? (string)token : null;
            if (s == null || !s.StartsWith("#", StringComparison.Ordinal) || !ColorUtility.TryParseHtmlString(s, out var c))
                throw new FormatException($"{where}: {token} is not a #rrggbb colour");
            return c;
        }
    }
}
