using System;
using System.Collections.Generic;
using System.Numerics;

namespace Demo3D.Choreography
{
    public enum FootprintShape { None, Circle, Polygon }

    /// <summary>
    /// One kit piece's footprint on the ground (choreography.md, "Navigation around structures"):
    /// a circle or a convex polygon in the prefab's own frame at scale 1, ground (X, Y) being
    /// Unity's (x, z). Measured by the editor's FootprintMeasurer from the prefab's colliders (or
    /// meshes) cut 1 m above the ground, and keyed by the vendor GUID. A piece that does not block
    /// (grass, flowers, ferns, debris, anything small) never enters the obstacle map.
    /// </summary>
    public sealed class Footprint
    {
        public string Id;            // "kingdom/SM_Env_Tree_Round_01": pack and prefab name
        public string Guid;          // the prefab's vendor GUID, which the scene generator instantiates
        public bool Blocks;
        public FootprintShape Shape;
        public Vector2 Center;       // circles
        public float Radius;         // circles
        public Vector2[] Points = new Vector2[0];   // polygons, in order round the outline
        public float HeightM, CanopyRadiusM;

        /// <summary>The farthest the footprint reaches from the prefab's origin (0 with no shape).</summary>
        public float BoundingRadius
        {
            get
            {
                if (Shape == FootprintShape.Circle) return Center.Length() + Radius;
                float r = 0;
                foreach (Vector2 p in Points) r = Math.Max(r, p.Length());
                return r;
            }
        }

        /// <summary>The footprint's extent along the prefab's local X axis, and its middle across it (walls run along X).</summary>
        public void AlongX(out float minX, out float maxX, out float midY)
        {
            if (Shape == FootprintShape.Circle)
            {
                minX = Center.X - Radius; maxX = Center.X + Radius; midY = Center.Y;
                return;
            }
            minX = float.MaxValue; maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector2 p in Points)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
            if (Points.Length == 0) { minX = maxX = 0; minY = maxY = 0; }
            midY = (minY + maxY) / 2;
        }
    }

    /// <summary>The tracked footprint annotations (Data/footprints.json), by piece id.</summary>
    public sealed class FootprintTable
    {
        public Footprint[] All = new Footprint[0];
        readonly Dictionary<string, Footprint> byId = new Dictionary<string, Footprint>(StringComparer.Ordinal);

        public Footprint this[string id] => byId.TryGetValue(id, out Footprint f) ? f : null;

        public Footprint Require(string id) =>
            this[id] ?? throw new FormatException("footprints: no kit piece '" + id + "' (add it to the manifest with a 'kit: ...' use and run `demo3d unity MeasureFootprints`)");

        public static FootprintTable Parse(string json)
        {
            JsonValue root = JsonValue.Parse(json);
            JsonValue props = root["props"];
            var t = new FootprintTable();
            var list = new List<Footprint>();
            for (int i = 0; i < props.Count; i++)
            {
                JsonValue p = props[i];
                var f = new Footprint
                {
                    Id = p["id"].AsString(),
                    Guid = p["guid"].AsString(),
                    Blocks = p["blocks"].AsBool(false),
                    HeightM = p["heightM"].AsFloat(),
                    CanopyRadiusM = p["canopyRadiusM"].AsFloat(),
                };
                switch (p["shape"].AsString("none"))
                {
                    case "circle":
                        f.Shape = FootprintShape.Circle;
                        f.Center = Vec(p["center"]);
                        f.Radius = p["radius"].AsFloat();
                        break;
                    case "polygon":
                    {
                        f.Shape = FootprintShape.Polygon;
                        JsonValue pts = p["points"];
                        f.Points = new Vector2[pts.Count];
                        for (int k = 0; k < pts.Count; k++) f.Points[k] = Vec(pts[k]);
                        if (f.Points.Length < 3) throw new FormatException("footprints: '" + f.Id + "' has a polygon of " + f.Points.Length + " points");
                        break;
                    }
                    default:
                        f.Shape = FootprintShape.None;
                        f.Blocks = false;
                        break;
                }
                if (f.Id == null) throw new FormatException("footprints: a prop without an id");
                if (t.byId.ContainsKey(f.Id)) throw new FormatException("footprints: '" + f.Id + "' twice");
                t.byId[f.Id] = f;
                list.Add(f);
            }
            t.All = list.ToArray();
            return t;
        }

        static Vector2 Vec(JsonValue v) => v.Count >= 2 ? new Vector2(v[0].AsFloat(), v[1].AsFloat()) : Vector2.Zero;
    }
}
