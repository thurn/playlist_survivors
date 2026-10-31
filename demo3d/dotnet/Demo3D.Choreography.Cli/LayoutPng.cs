using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using PS.Choreography;

namespace Demo3D.Choreography.Cli
{
    /// <summary>
    /// A top-down map of a placement list as a PNG carrying the `ps-render` tag (TD6's evidence):
    /// forest dark, fighting ground lighter, blocking footprints by kind (walls sand, trees deep
    /// green, bushes green, rocks grey), soft pieces as specks, goals as orange rings, and
    /// optionally a flow field's cost in 5 m bands, monsters as red dots (each with its last beat's track) and the hero
    /// in white, and the camera's view as a yellow outline. North (+Y) is up.
    /// </summary>
    static class LayoutPng
    {
        public static void Write(string path, ArenaLayout layout, PlacementList list, float pxPerM,
            FlowField field = null, Vector2[] monsters = null, Vector2? hero = null, Vector2? centre = null, float spanM = 0,
            Vector2[] tracks = null, Vector2[] outline = null)
        {
            Vector2 c = centre ?? Vector2.Zero;
            float span = spanM > 0 ? spanM : Math.Max(list.Size.X, list.Size.Y);
            int n = (int)Math.Round(span * pxPerM);
            var img = new byte[n * n * 3];
            float x0 = c.X - span / 2, y1 = c.Y + span / 2;
            Vector2 P(int px, int py) => new Vector2(x0 + (px + 0.5f) / pxPerM, y1 - (py + 0.5f) / pxPerM);

            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    Vector2 p = P(px, py);
                    bool inside = Math.Abs(p.X) <= list.Size.X / 2 && Math.Abs(p.Y) <= list.Size.Y / 2;
                    AreaType? t = inside ? layout.Classify(p) : null;
                    float fd = layout.FightingDistance(p, out _);
                    (byte r, byte g, byte b) col = !inside ? ((byte)10, (byte)10, (byte)12)
                        : fd <= 0 ? ((byte)70, (byte)86, (byte)60)
                        : t == AreaType.Edge ? ((byte)44, (byte)60, (byte)40)
                        : ((byte)24, (byte)38, (byte)28);
                    if (field != null && inside)
                    {
                        float m = field.CostAt(p);
                        if (!float.IsInfinity(m) && ((int)(m / 5)) % 2 == 0) col = Mix(col, (110, 130, 200), 0.3f);
                    }
                    Set(img, n, px, py, col);
                }

            foreach (Placement pl in list.Items)
            {
                Footprint f = list.Props[pl.Prop];
                if (!f.Blocks)
                {
                    Dot(img, n, P, pl.Position, x0, y1, pxPerM, 0.2f, (120, 150, 100));
                    continue;
                }
                (byte, byte, byte) col = f.Id.Contains("Wall") || f.Id.Contains("Ruin") || f.Id.Contains("Fence") ? ((byte)214, (byte)190, (byte)140)
                    : f.Id.Contains("Tree") ? ((byte)8, (byte)70, (byte)30)
                    : f.Id.Contains("Bush") ? ((byte)50, (byte)120, (byte)60)
                    : ((byte)140, (byte)145, (byte)155);
                float reach = f.BoundingRadius * pl.Scale + 0.5f;
                int ax = (int)Math.Floor((pl.Position.X - reach - x0) * pxPerM), bx = (int)Math.Ceiling((pl.Position.X + reach - x0) * pxPerM);
                int ay = (int)Math.Floor((y1 - pl.Position.Y - reach) * pxPerM), by = (int)Math.Ceiling((y1 - pl.Position.Y + reach) * pxPerM);
                for (int py = Math.Max(0, ay); py <= Math.Min(n - 1, by); py++)
                    for (int px = Math.Max(0, ax); px <= Math.Min(n - 1, bx); px++)
                        if (Inside(f, pl, P(px, py))) Set(img, n, px, py, col);
            }
            foreach (GoalArea g in list.Goals) Ring(img, n, x0, y1, pxPerM, g.Center, g.Radius, (255, 170, 60));
            if (tracks != null) for (int i = 0; i < monsters.Length; i++) Line(img, n, x0, y1, pxPerM, tracks[i], monsters[i], (255, 210, 90));
            if (monsters != null) foreach (Vector2 m in monsters) Dot(img, n, P, m, x0, y1, pxPerM, 0.4f, (240, 60, 60));
            if (outline != null) for (int i = 0; i < outline.Length; i++) Line(img, n, x0, y1, pxPerM, outline[i], outline[(i + 1) % outline.Length], (250, 220, 80));
            if (hero.HasValue) Dot(img, n, P, hero.Value, x0, y1, pxPerM, 0.6f, (255, 255, 255));

            File.WriteAllBytes(path, Png(img, n, n, "demo3d choreography cli (TD6 layout map)"));
        }

        static (byte, byte, byte) Mix((byte r, byte g, byte b) a, (int r, int g, int b) o, float k) =>
            ((byte)(a.r + (o.r - a.r) * k), (byte)(a.g + (o.g - a.g) * k), (byte)(a.b + (o.b - a.b) * k));

        static void Set(byte[] img, int n, int px, int py, (byte r, byte g, byte b) c)
        {
            int i = (py * n + px) * 3;
            img[i] = c.r; img[i + 1] = c.g; img[i + 2] = c.b;
        }

        static bool Inside(Footprint f, Placement pl, Vector2 p)
        {
            if (f.Shape == FootprintShape.Circle) return Vector2.Distance(p, pl.ToWorld(f.Center)) <= f.Radius * pl.Scale;
            // Convex polygon: the same side of every edge.
            int sign = 0;
            for (int i = 0; i < f.Points.Length; i++)
            {
                Vector2 a = pl.ToWorld(f.Points[i]), b = pl.ToWorld(f.Points[(i + 1) % f.Points.Length]);
                float cr = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                int s = cr > 0 ? 1 : cr < 0 ? -1 : 0;
                if (s == 0) continue;
                if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }

        static void Dot(byte[] img, int n, Func<int, int, Vector2> P, Vector2 c, float x0, float y1, float pxPerM, float r, (byte, byte, byte) col)
        {
            int cx = (int)((c.X - x0) * pxPerM), cy = (int)((y1 - c.Y) * pxPerM);
            int rp = Math.Max(1, (int)Math.Ceiling(r * pxPerM));
            for (int py = cy - rp; py <= cy + rp; py++)
                for (int px = cx - rp; px <= cx + rp; px++)
                    if (px >= 0 && py >= 0 && px < n && py < n && Vector2.Distance(P(px, py), c) <= Math.Max(r, 0.5f / pxPerM)) Set(img, n, px, py, col);
        }

        static void Line(byte[] img, int n, float x0, float y1, float pxPerM, Vector2 a, Vector2 b, (byte, byte, byte) col)
        {
            int steps = (int)(Vector2.Distance(a, b) * pxPerM * 2) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
                int px = (int)((p.X - x0) * pxPerM), py = (int)((y1 - p.Y) * pxPerM);
                if (px >= 0 && py >= 0 && px < n && py < n) Set(img, n, px, py, col);
            }
        }

        static void Ring(byte[] img, int n, float x0, float y1, float pxPerM, Vector2 c, float r, (byte, byte, byte) col)
        {
            int steps = (int)(2 * Math.PI * r * pxPerM * 2) + 8;
            for (int i = 0; i < steps; i++)
            {
                double a = 2 * Math.PI * i / steps;
                int px = (int)((c.X + r * Math.Cos(a) - x0) * pxPerM), py = (int)((y1 - c.Y - r * Math.Sin(a)) * pxPerM);
                if (px >= 0 && py >= 0 && px < n && py < n) Set(img, n, px, py, col);
            }
        }

        /// <summary>An RGB PNG with a `ps-render` tEXt chunk right after IHDR (Demo3D.Runtime.RenderTag's format).</summary>
        static byte[] Png(byte[] rgb, int w, int h, string tool)
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            BE(ihdr, 0, (uint)w); BE(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 2;
            Chunk(ms, "IHDR", ihdr);
            Chunk(ms, "tEXt", Encoding.ASCII.GetBytes("ps-render\0" + tool));
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgb, y * w * 3, w * 3);
            }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(zs); }
            Chunk(ms, "IDAT", z.ToArray());
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            BE(len, 0, (uint)data.Length);
            s.Write(len);
            var td = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type).CopyTo(td, 0);
            data.CopyTo(td, 4);
            s.Write(td);
            var crc = new byte[4];
            BE(crc, 0, Crc(td));
            s.Write(crc);
        }

        static void BE(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

        static uint Crc(byte[] b)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte x in b)
            {
                c ^= x;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFF;
        }
    }
}
