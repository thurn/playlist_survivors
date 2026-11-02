using System;
using System.Collections.Generic;
using System.IO;
using Champions.Sim;
using UnityEngine;

namespace Champions.Render
{
    /// <summary>
    /// The palette: every colour the game draws, by role (rhythm_champions/Content/palette.json; the rules it keeps are
    /// docs/champions/style.md's The Palette, checked by the tools' test_palette.py). Read strictly: a missing, unknown
    /// or malformed field fails with its path. The enemy families' members are the palette's own names
    /// (<see cref="Role"/> `enemies.key.cyan`); presenters colour a monster by its type (<see cref="ForType"/>).
    /// </summary>
    public sealed class Palette
    {
        public const string File = "palette.json";

        /// <summary>A floor key (style.md, Zones): the grid's resting and passing colours and the structures'.</summary>
        public sealed class Zone
        {
            public string Name;
            /// <summary>"cool", "violet" or "warm".</summary>
            public string Family;
            public Color Line, Pulse, Fill, Edge;
        }

        public string Name;
        public Color VoidFloor, VoidFog, VoidBody;
        public Color HeroBody, HeroBlade, HeroTrail, HeroEdge, HeroHurt, HeroBreak;
        public Color BossBody, BossTelegraph, Warning, EnemyShot, Wall;
        /// <summary>The four zones in their order: the first is the key's, the warm one the dragon's.</summary>
        public Zone[] Zones;

        readonly Dictionary<string, Color> roles = new Dictionary<string, Color>();
        readonly Dictionary<string, Color> types = new Dictionary<string, Color>();

        /// <summary>The dragon's zone, the warm one.</summary>
        public int WarmZone => Array.FindIndex(Zones, z => z.Family == "warm");

        /// <summary>A colour by its role's path (`void.body`, `enemies.counter.amber`, `zones.teal.fill`).</summary>
        public Color Role(string path) =>
            roles.TryGetValue(path, out var c) ? c : throw new KeyNotFoundException($"{File}: no role {path}");

        /// <summary>A cast type's colour (enemies.json's id: `skeleton`, `dragon`).</summary>
        public Color ForType(string type) =>
            types.TryGetValue(type, out var c) ? c : throw new KeyNotFoundException($"{File}: no colour for the type {type}");

        /// <summary>Loads the palette from the content folder <paramref name="contentDir"/>.</summary>
        public static Palette Load(string contentDir)
        {
            string path = Path.Combine(contentDir, File);
            try
            {
                return Read(JsonValue.Parse(System.IO.File.ReadAllText(path)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                throw new ContentException($"{File}: can't read {path}: {e.Message}", e);
            }
        }

        public static Palette Read(JsonValue json)
        {
            var p = new Palette();
            var r = new ContentReader(json, File);
            p.Name = r.String("name");
            r.String("note");
            var v = r.Object("void");
            p.VoidFloor = p.Colour(v, "floor");
            p.VoidFog = p.Colour(v, "fog");
            p.VoidBody = p.Colour(v, "body");
            v.End();
            var h = r.Object("hero");
            p.HeroBody = p.Colour(h, "body");
            p.HeroBlade = p.Colour(h, "blade");
            p.HeroTrail = p.Colour(h, "trail");
            p.HeroEdge = p.Colour(h, "edge");
            p.HeroHurt = p.Colour(h, "hurt");
            p.HeroBreak = p.Colour(h, "break");
            h.End();
            var enemies = r.Object("enemies");
            foreach (string family in new[] { "key", "accent", "counter" })
            {
                var f = enemies.Object(family);
                foreach (string member in new List<string>(f.Keys())) p.Colour(f, member);
                f.End();
            }
            enemies.End();
            var b = r.Object("boss");
            p.BossBody = p.Colour(b, "body");
            p.BossTelegraph = p.Colour(b, "telegraph");
            b.End();
            p.Warning = p.Colour(r, "warning");
            p.EnemyShot = p.Colour(r, "enemyShot");
            p.Wall = p.Colour(r, "wall");
            p.Zones = r.Objects("zones", z =>
            {
                string name = z.String("name");
                var zone = new Zone { Name = name, Family = z.String("family") };
                zone.Line = p.Colour(z, "line", "zones." + name);
                zone.Pulse = p.Colour(z, "pulse", "zones." + name);
                zone.Fill = p.Colour(z, "fill", "zones." + name);
                zone.Edge = p.Colour(z, "edge", "zones." + name);
                return zone;
            });
            var t = r.Object("types");
            foreach (string type in new List<string>(t.Keys()))
            {
                string role = t.String(type);
                if (!p.roles.TryGetValue(role, out var c)) throw ContentReader.Fail(t.Child(type), $"{role} is not a role");
                p.types[type] = c;
            }
            t.End();
            r.End();
            return p;
        }

        /// <summary>Reads the `#rrggbb` field <paramref name="key"/> and keeps it as the role `prefix.key` (the
        /// reader's path past the file's name unless given).</summary>
        Color Colour(ContentReader r, string key, string prefix = null)
        {
            string hex = r.String(key);
            if (hex.Length != 7 || hex[0] != '#' || !ColorUtility.TryParseHtmlString(hex, out var c))
                throw ContentReader.Fail(r.Child(key), $"{hex} is not a #rrggbb colour");
            prefix ??= r.Path.Length > File.Length ? r.Path.Substring(File.Length + 1) : "";
            roles[prefix.Length > 0 ? prefix + "." + key : key] = c;
            return c;
        }
    }
}
