using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using Champions.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Champions.Client.UI
{
    /// <summary>
    /// A white line-art icon (design.md, Cards): the strokes of a committed SVG in Content/icons/, on its viewBox,
    /// flattened to polylines. The subset read: `path` (M L H V C Q A Z, absolute and relative), `circle`, `ellipse`,
    /// `line` and `polyline`, each stroked with its own or the root's `stroke-width` and its `opacity`; fills are not
    /// drawn.
    /// </summary>
    public sealed class Icon
    {
        public struct Stroke
        {
            public Vector2[] Points;
            public bool Closed;
            public float Width, Opacity;
        }

        public string Name;
        public Vector2 Size;
        public readonly List<Stroke> Strokes = new List<Stroke>();

        const int CurveSteps = 16;
        const float ArcStepRad = 8 * Mathf.Deg2Rad;

        /// <summary>Content/icons/<paramref name="name"/>.svg in the content folder.</summary>
        public static Icon Load(string contentDir, string name)
        {
            string path = Path.Combine(contentDir, "icons", name + ".svg");
            try { return Parse(File.ReadAllText(path), name); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is XmlException || e is FormatException)
            {
                throw new ContentException($"icons/{name}.svg: can't read {path}: {e.Message}", e);
            }
        }

        public static Icon Parse(string svg, string name)
        {
            var doc = new XmlDocument();
            doc.LoadXml(svg);
            var root = doc.DocumentElement;
            if (root == null || root.LocalName != "svg") throw new FormatException("no svg element");
            var box = Numbers(root.GetAttribute("viewBox"));
            if (box.Count != 4) throw new FormatException("the svg needs a viewBox");
            var icon = new Icon { Name = name, Size = new Vector2(box[2], box[3]) };
            float width = Attribute(root, "stroke-width", 1);
            foreach (XmlNode node in root.ChildNodes)
                if (node is XmlElement e) icon.Read(e, new Vector2(box[0], box[1]), Attribute(e, "stroke-width", width), Attribute(e, "opacity", 1));
            if (icon.Strokes.Count == 0) throw new FormatException("no strokes");
            return icon;
        }

        void Read(XmlElement e, Vector2 origin, float width, float opacity)
        {
            void Add(List<Vector2> points, bool closed)
            {
                if (points.Count < 2) return;
                for (int i = 0; i < points.Count; i++) points[i] -= origin;
                Strokes.Add(new Stroke { Points = points.ToArray(), Closed = closed, Width = width, Opacity = opacity });
            }

            switch (e.LocalName)
            {
                case "path":
                    foreach (var (points, closed) in PathData(e.GetAttribute("d"))) Add(points, closed);
                    break;
                case "circle":
                {
                    float r = Attribute(e, "r", 0);
                    Add(Ellipse(new Vector2(Attribute(e, "cx", 0), Attribute(e, "cy", 0)), r, r), true);
                    break;
                }
                case "ellipse":
                    Add(Ellipse(new Vector2(Attribute(e, "cx", 0), Attribute(e, "cy", 0)), Attribute(e, "rx", 0), Attribute(e, "ry", 0)), true);
                    break;
                case "line":
                    Add(new List<Vector2> { new Vector2(Attribute(e, "x1", 0), Attribute(e, "y1", 0)), new Vector2(Attribute(e, "x2", 0), Attribute(e, "y2", 0)) }, false);
                    break;
                case "polyline":
                case "polygon":
                {
                    var n = Numbers(e.GetAttribute("points"));
                    var points = new List<Vector2>();
                    for (int i = 0; i + 1 < n.Count; i += 2) points.Add(new Vector2(n[i], n[i + 1]));
                    Add(points, e.LocalName == "polygon");
                    break;
                }
                default:
                    throw new FormatException($"an element the icons don't draw: {e.LocalName}");
            }
        }

        static float Attribute(XmlElement e, string name, float fallback) =>
            e.HasAttribute(name) ? float.Parse(e.GetAttribute(name), CultureInfo.InvariantCulture) : fallback;

        static List<Vector2> Ellipse(Vector2 c, float rx, float ry)
        {
            int n = Mathf.Max(12, Mathf.CeilToInt(2 * Mathf.PI / ArcStepRad));
            var points = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float a = 2 * Mathf.PI * i / n;
                points.Add(c + new Vector2(rx * Mathf.Cos(a), ry * Mathf.Sin(a)));
            }
            return points;
        }

        /// <summary>The numbers in a list (commas or spaces between, a sign or a second point starting the next).</summary>
        static List<float> Numbers(string s)
        {
            var list = new List<float>();
            int i = 0;
            while (i < s.Length)
            {
                if (!TryNumber(s, ref i, out float v)) { i++; continue; }
                list.Add(v);
            }
            return list;
        }

        static bool TryNumber(string s, ref int i, out float v)
        {
            v = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\n' || s[i] == '\t' || s[i] == '\r')) i++;
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool dot = false, digits = false;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' && !dot))
            {
                if (s[i] == '.') dot = true;
                else digits = true;
                i++;
            }
            if (digits && i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            if (!digits)
            {
                i = start;
                return false;
            }
            v = float.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>A path's subpaths, flattened: curves in <see cref="CurveSteps"/> segments, arcs every few degrees.</summary>
        static List<(List<Vector2> points, bool closed)> PathData(string d)
        {
            var paths = new List<(List<Vector2>, bool)>();
            List<Vector2> current = null;
            Vector2 pen = Vector2.zero, start = Vector2.zero;
            char command = ' ';
            int i = 0;

            float Next()
            {
                if (!TryNumber(d, ref i, out float v)) throw new FormatException($"path: a number expected at {i} in \"{d}\"");
                return v;
            }

            Vector2 Point(bool relative) => new Vector2(Next(), Next()) + (relative ? pen : Vector2.zero);

            void Begin(Vector2 p)
            {
                current = new List<Vector2> { p };
                paths.Add((current, false));
                pen = start = p;
            }

            void To(Vector2 p)
            {
                if (current == null) Begin(pen);
                current.Add(p);
                pen = p;
            }

            while (true)
            {
                while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\t' || d[i] == '\r')) i++;
                if (i >= d.Length) break;
                if (char.IsLetter(d[i])) command = d[i++];
                else if (command == ' ') throw new FormatException($"path: no command at {i} in \"{d}\"");
                bool rel = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        Begin(Point(rel));
                        command = rel ? 'l' : 'L'; // further pairs are lines
                        break;
                    case 'L': To(Point(rel)); break;
                    case 'H': To(new Vector2(Next() + (rel ? pen.x : 0), pen.y)); break;
                    case 'V': To(new Vector2(pen.x, Next() + (rel ? pen.y : 0))); break;
                    case 'Q':
                    {
                        Vector2 p0 = pen, c = Point(rel), p1 = Point(rel);
                        for (int k = 1; k <= CurveSteps; k++)
                        {
                            float t = (float)k / CurveSteps, u = 1 - t;
                            To(u * u * p0 + 2 * u * t * c + t * t * p1);
                        }
                        break;
                    }
                    case 'C':
                    {
                        Vector2 p0 = pen, c0 = Point(rel), c1 = Point(rel), p1 = Point(rel);
                        for (int k = 1; k <= CurveSteps; k++)
                        {
                            float t = (float)k / CurveSteps, u = 1 - t;
                            To(u * u * u * p0 + 3 * u * u * t * c0 + 3 * u * t * t * c1 + t * t * t * p1);
                        }
                        break;
                    }
                    case 'A':
                    {
                        float rx = Mathf.Abs(Next()), ry = Mathf.Abs(Next()), rotation = Next();
                        bool large = Next() != 0, sweep = Next() != 0;
                        Arc(pen, Point(rel), rx, ry, rotation, large, sweep, To);
                        break;
                    }
                    case 'Z':
                        if (current != null)
                        {
                            paths[paths.Count - 1] = (current, true);
                            current = null;
                        }
                        pen = start;
                        break;
                    default:
                        throw new FormatException($"path: a command the icons don't read: {command}");
                }
            }
            return paths;
        }

        /// <summary>An SVG arc from <paramref name="p0"/> to <paramref name="p1"/> (the spec's endpoint-to-centre conversion), flattened into <paramref name="to"/>.</summary>
        static void Arc(Vector2 p0, Vector2 p1, float rx, float ry, float rotationDeg, bool large, bool sweep, Action<Vector2> to)
        {
            if (rx < 1e-6f || ry < 1e-6f || (p0 - p1).sqrMagnitude < 1e-12f)
            {
                to(p1);
                return;
            }
            float phi = rotationDeg * Mathf.Deg2Rad, cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
            var h = (p0 - p1) / 2;
            var q = new Vector2(cos * h.x + sin * h.y, -sin * h.x + cos * h.y);
            float lambda = q.x * q.x / (rx * rx) + q.y * q.y / (ry * ry);
            if (lambda > 1)
            {
                rx *= Mathf.Sqrt(lambda);
                ry *= Mathf.Sqrt(lambda);
            }
            float num = rx * rx * ry * ry - rx * rx * q.y * q.y - ry * ry * q.x * q.x;
            float den = rx * rx * q.y * q.y + ry * ry * q.x * q.x;
            float k = Mathf.Sqrt(Mathf.Max(0, num / den)) * (large == sweep ? -1 : 1);
            var cq = new Vector2(k * rx * q.y / ry, -k * ry * q.x / rx);
            var c = new Vector2(cos * cq.x - sin * cq.y, sin * cq.x + cos * cq.y) + (p0 + p1) / 2;
            float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);
            float a0 = Angle(new Vector2((q.x - cq.x) / rx, (q.y - cq.y) / ry));
            float delta = Angle(new Vector2((-q.x - cq.x) / rx, (-q.y - cq.y) / ry)) - a0;
            if (sweep && delta < 0) delta += 2 * Mathf.PI;
            else if (!sweep && delta > 0) delta -= 2 * Mathf.PI;
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(delta) / ArcStepRad));
            for (int s = 1; s <= n; s++)
            {
                float a = a0 + delta * s / n;
                var e = new Vector2(rx * Mathf.Cos(a), ry * Mathf.Sin(a));
                to(s == n ? p1 : c + new Vector2(cos * e.x - sin * e.y, sin * e.x + cos * e.y));
            }
        }
    }

    /// <summary>Draws an <see cref="Icon"/> in white, scaled to fit its content box, with round caps and joins.</summary>
    public sealed class IconElement : VisualElement
    {
        Icon _icon;
        Color _colour = Color.white;

        public IconElement(Icon icon = null)
        {
            _icon = icon;
            AddToClassList("icon");
            pickingMode = PickingMode.Ignore; // part of its card, which takes the pointer
            generateVisualContent += Draw;
        }

        public Icon Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                MarkDirtyRepaint();
            }
        }

        public Color Colour
        {
            get => _colour;
            set
            {
                _colour = value;
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext context)
        {
            if (_icon == null) return;
            var box = contentRect;
            float scale = Mathf.Min(box.width / _icon.Size.x, box.height / _icon.Size.y);
            if (!(scale > 0)) return;
            var offset = box.position + (box.size - _icon.Size * scale) / 2;
            var p = context.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            foreach (var s in _icon.Strokes)
            {
                p.strokeColor = new Color(_colour.r, _colour.g, _colour.b, _colour.a * s.Opacity);
                p.lineWidth = s.Width * scale;
                p.BeginPath();
                p.MoveTo(offset + s.Points[0] * scale);
                for (int i = 1; i < s.Points.Length; i++) p.LineTo(offset + s.Points[i] * scale);
                if (s.Closed) p.ClosePath();
                p.Stroke();
            }
        }
    }
}
