using System;
using System.Collections.Generic;
using Champions.Render;
using Champions.Render.Arena;
using Champions.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Champions.Client
{
    /// <summary>
    /// The Boss Rush arena as the player sees it (boss-rush.md, Arenas; design.md, The aim): the sim's
    /// <see cref="BossArena"/> drawn in the edge look through ArenaEdge.shader, and the camera framing it.
    /// - <b>Walls</b>: the reserved grey (palette.json's wall), a low band round the outline. An arena forms by drawing its
    ///   walls in round the outline and raising them over the form's beats, its props rising after; it retracts the same
    ///   way back. Everything shows on song time from the sim's events, so it trails the frontier as the rest does.
    /// - <b>Floor zones</b>: open zones a faint outline over the grid (a hazard's in the warning colour); a raised one a
    ///   grey block rising over a beat; a sunk one a void pit with its rim and a step inside it; a broken one a void hole.
    /// - <b>Props</b>: placeholders, the hero-breakable ones in the zone key's edge colour (grey stays the unbreakable's),
    ///   flashing when hit and gone on the impact frame of their crumbling, with a structure-break ring.
    /// - <b>Obstacles</b>: a grown wall grey, a web its spokes, lava the warning colour, each growing in from its centre.
    /// - <b>The act's kit</b> (boss-rush.md, Environments; <see cref="ArenaKit"/>): the arena's own, else a Boss Rush run's
    ///   act's. Its props draw as their designed pieces (<see cref="KitMeshes"/>), and so do the placeholders it dresses; its
    ///   floor treatment is scattered over open floor clear of the walls, props, zones and starts, faint lines in the key
    ///   over a dimmed fill; its backdrop's rings stand outside the walls where the outline faces away from the camera, a
    ///   dark skyline in the key, lower toward the flanks, rising with the props. All of it seeded by the arena's id, so an
    ///   arena always looks the same. Without the baked pieces it draws the placeholders and no floor or backdrop.
    /// - <b>Camera</b> (<see cref="Frame"/>): <see cref="ArenaFraming"/>'s frame, holding the hero, the boss and its live
    ///   telegraphs; let go when the arena has retracted.
    /// </summary>
    public sealed class ArenaPresenter : IDisposable
    {
        public const string MaterialPath = "Arena/ArenaEdge";
        /// <summary>The lines' intensity over the palette's colour (they bloom, as the structures' edges do).</summary>
        const float WallGain = 2.4f, ZoneGain = 1.6f, HazardGain = 2f;
        /// <summary>Above the floor so caps never fight the grid.</summary>
        const float CapM = 0.02f;
        /// <summary>A crumbling or a hit lights a prop this long.</summary>
        const double FlashS = 0.2;
        /// <summary>The kit floor's and backdrop rings' line gain (the props' is the material's 2.6), nearest ring first.</summary>
        const float FloorEdge = 1.1f, FloorFill = 0.6f;
        static readonly float[] BackdropEdge = { 1.05f, 0.7f, 0.5f };
        /// <summary>The starts' clearing for the floor's scatter (m).</summary>
        const double StartClearM = 2.5;

        static readonly int LineId = Shader.PropertyToID("_Line"), FillId = Shader.PropertyToID("_Fill"), AlphaId = Shader.PropertyToID("_Alpha"),
            RevealId = Shader.PropertyToID("_Reveal"), RiseId = Shader.PropertyToID("_Rise"), ZoneLineId = Shader.PropertyToID("_ZoneLine"),
            EdgeId = Shader.PropertyToID("_Edge");

        enum PartKind { Walls, Zone, Prop, Obstacle, Floor, Backdrop }

        sealed class Part
        {
            public PartKind Kind;
            public int Index;
            public Mesh Mesh;
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            public bool Translucent, Gone;
            public Vector2 Offset;
            public float Reveal = 1, Rise = 1;
        }

        /// <summary>A change the sim made, shown once the frame's song time reaches it.</summary>
        struct Change
        {
            public SimEvent Event;
        }

        readonly ArenaRules _rules;
        readonly BossRushRules _rush;
        readonly Palette _palette;
        readonly IWorldCommands _world;
        readonly WorldCamera _camera;
        readonly int _layer;
        readonly Material _opaque, _translucent;
        readonly ArenaMeshes _build = new ArenaMeshes();
        readonly List<Part> _parts = new List<Part>();
        readonly List<Change> _pending = new List<Change>();
        readonly List<(double X, double Y, double R)> _keep = new List<(double, double, double)>(16);
        readonly Stack<Mesh> _meshes = new Stack<Mesh>();
        ArenaFraming _framing;
        BossArena _arena;
        ArenaDef _def;
        ArenaKit _kit;
        string _rushKit;
        IReadOnlyDictionary<string, KitPiece> _pieces = NoPieces;
        static readonly IReadOnlyDictionary<string, KitPiece> NoPieces = new Dictionary<string, KitPiece>();
        double _ox, _oy, _formFrom = double.NaN, _formTo, _retractFrom = double.NaN, _retractTo;
        ArenaZoneState[] _shown = new ArenaZoneState[0];
        double[] _zoneFrom = new double[0], _zoneBeat = new double[0];
        double[] _propFlash = new double[0];

        /// <param name="rush">The Boss Rush's slots, so a fight's own arena takes its act's kit outside a run too (its review, its stills).</param>
        public ArenaPresenter(ArenaRules rules, Palette palette, IWorldCommands world, WorldCamera camera, int layer = 0, BossRushRules rush = null)
        {
            _rules = rules;
            _rush = rush;
            _palette = palette;
            _world = world;
            _camera = camera;
            _layer = layer;
            var shared = Resources.Load<Material>(MaterialPath) ?? throw new InvalidOperationException($"ArenaPresenter: no material at Resources/{MaterialPath}");
            _opaque = new Material(shared) { name = "Arena (opaque)" };
            _opaque.SetFloat(IdPass.Id, IdPass.Structure);
            _translucent = new Material(shared) { name = "Arena (translucent)", renderQueue = (int)RenderQueue.Transparent };
            _translucent.SetFloat("_SrcBlend", (float)BlendMode.One);
            _translucent.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            _translucent.SetFloat("_ZWrite", 0);
            _translucent.SetFloat(IdPass.Id, IdPass.Structure);
        }

        /// <summary>Is an arena drawn (forming, standing or retracting) at the latest frame?</summary>
        public bool Shown => _def != null;

        /// <summary>One settled event, in log order (the arena's own; the rest it ignores).</summary>
        public void Take(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.ArenaForm:
                case SimEventKind.ArenaRetract:
                case SimEventKind.ArenaZone:
                case SimEventKind.ArenaObstacle:
                case SimEventKind.ArenaProp:
                    int i = _pending.Count;
                    while (i > 0 && _pending[i - 1].Event.T > e.T) i--;
                    _pending.Insert(i, new Change { Event = e });
                    break;
            }
        }

        /// <summary>
        /// The frame at song time <paramref name="song"/>: the changes it reaches show, the parts animate and the camera
        /// frames the arena round the hero at <paramref name="hero"/> (the boss and the live telegraphs held in frame), then
        /// it draws for <paramref name="camera"/>.
        /// </summary>
        public void Present(double song, Simulation sim, Vector2 hero, Camera camera)
        {
            _arena = sim.Arena;
            var rush = sim.BossRush;
            _rushKit = rush != null ? rush.Rules.Acts[rush.Act].Kit : null;
            while (_pending.Count > 0 && _pending[0].Event.T <= song)
            {
                var e = _pending[0].Event;
                _pending.RemoveAt(0);
                Show(e);
            }
            if (_def != null && !double.IsNaN(_retractFrom) && song >= _retractTo) Clear();
            Animate(song);
            Frame(song, sim, hero, camera);
            Queue(camera);
        }

        void Show(in SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.ArenaForm:
                    Clear();
                    _def = _rules.Arenas[e.I0];
                    _kit = _rules.Kit(_def.Kit ?? _rushKit ?? SlotKit(_def.Id));
                    _pieces = _kit != null ? KitMeshes.Load(_kit.Id) : NoPieces;
                    _ox = e.D0;
                    _oy = e.D1;
                    _formFrom = e.D2;
                    _formTo = e.D3;
                    _retractFrom = double.NaN;
                    _shown = new ArenaZoneState[_def.Zones.Length];
                    _zoneFrom = new double[_def.Zones.Length];
                    _zoneBeat = new double[_def.Zones.Length];
                    for (int z = 0; z < _shown.Length; z++)
                    {
                        _shown[z] = _def.Zones[z].State;
                        _zoneFrom[z] = double.NegativeInfinity;
                    }
                    _propFlash = new double[_def.Props.Length];
                    for (int p = 0; p < _propFlash.Length; p++) _propFlash[p] = double.NegativeInfinity;
                    BuildWalls();
                    for (int z = 0; z < _shown.Length; z++) BuildZone(z);
                    for (int p = 0; p < _def.Props.Length; p++) BuildProp(p);
                    BuildFloor();
                    BuildBackdrop();
                    break;
                case SimEventKind.ArenaRetract:
                    _retractFrom = e.D0;
                    _retractTo = e.D1;
                    break;
                case SimEventKind.ArenaZone:
                    if (_def == null) break;
                    _shown[e.I0] = (ArenaZoneState)e.I1;
                    _zoneFrom[e.I0] = e.T;
                    _zoneBeat[e.I0] = e.D0;
                    BuildZone(e.I0);
                    break;
                case SimEventKind.ArenaObstacle:
                    if (_def == null || _arena == null || e.I0 >= _arena.ObstacleCount) break;
                    if (e.I2 == 1) BuildObstacle(e.I0, _arena.Obstacle(e.I0));
                    else Remove(PartKind.Obstacle, e.I0);
                    break;
                case SimEventKind.ArenaProp:
                    if (_def == null) break;
                    _propFlash[e.I0] = e.T;
                    if (e.I1 == 0)
                    {
                        Remove(PartKind.Prop, e.I0);
                        var p = _def.Props[e.I0];
                        _world?.Ring(RingKind.StructureBreak, new Vector2((float)(_ox + p.X), (float)(_oy + p.Y)), e.T, Color.white, 0.4f);
                    }
                    break;
            }
        }

        // ---- Parts ----

        Part Add(PartKind kind, int index, bool translucent)
        {
            var part = new Part { Kind = kind, Index = index, Translucent = translucent, Mesh = _meshes.Count > 0 ? _meshes.Pop() : new Mesh { name = "Arena " + kind } };
            _build.Into(part.Mesh);
            _parts.Add(part);
            return part;
        }

        void Remove(PartKind kind, int index)
        {
            for (int i = _parts.Count - 1; i >= 0; i--)
            {
                if (_parts[i].Kind != kind || _parts[i].Index != index) continue;
                _meshes.Push(_parts[i].Mesh);
                _parts.RemoveAt(i);
            }
        }

        void Clear()
        {
            foreach (var p in _parts) _meshes.Push(p.Mesh);
            _parts.Clear();
            _def = null;
            _kit = null;
            _pieces = NoPieces;
            _camera?.Unframe();
        }

        static Color Lin(Color c, float gain) => c.linear * gain;

        /// <param name="edge">The lines' gain where not the material's.</param>
        void Style(Part p, Color line, Color fill, float alpha, bool zoneLine = false, float? edge = null)
        {
            if (edge.HasValue) p.Block.SetFloat(EdgeId, edge.Value);
            p.Block.SetColor(LineId, line);
            p.Block.SetColor(FillId, fill);
            p.Block.SetFloat(AlphaId, alpha);
            p.Block.SetFloat(ZoneLineId, zoneLine ? 1 : 0);
        }

        void BuildWalls()
        {
            _build.Clear();
            _build.Walls(_def.Outline, _ox, _oy, (float)_rules.WallThicknessM, (float)_rules.WallHeightM);
            var p = Add(PartKind.Walls, 0, false);
            Style(p, Lin(_palette.Wall, WallGain), _palette.VoidBody.linear, 1);
        }

        void BuildZone(int z)
        {
            Remove(PartKind.Zone, z);
            var zone = _def.Zones[z];
            var state = _shown[z];
            _build.Clear();
            switch (state)
            {
                case ArenaZoneState.Open:
                {
                    _build.Cap(zone.Shape, _ox, _oy, CapM);
                    var p = Add(PartKind.Zone, z, true);
                    bool hazard = zone.Kind == ArenaZoneKind.Hazard;
                    Style(p, hazard ? Lin(_palette.Warning, HazardGain) : Lin(_palette.Wall, ZoneGain), hazard ? _palette.Warning.linear * 0.08f : Color.black, hazard ? 0.35f : 0);
                    break;
                }
                case ArenaZoneState.Raised:
                {
                    _build.Prism(zone.Shape, _ox, _oy, 0, (float)_rules.RaiseM);
                    var p = Add(PartKind.Zone, z, false);
                    Style(p, Lin(_palette.Wall, WallGain), _palette.VoidBody.linear, 1);
                    break;
                }
                default:
                {
                    // A pit or a hole: the void over the grid, its rim lit; a pit shows a step down inside it.
                    _build.Cap(zone.Shape, _ox, _oy, CapM);
                    var p = Add(PartKind.Zone, z, false);
                    bool broken = state == ArenaZoneState.Broken;
                    Style(p, Lin(_palette.Wall, broken ? ZoneGain * 0.6f : WallGain), _palette.VoidFloor.linear * 0.2f, 1);
                    if (!broken)
                    {
                        _build.Clear();
                        zone.Shape.Centre(out double cx, out double cy);
                        var inset = new ArenaShape { X = new double[zone.Shape.Count], Y = new double[zone.Shape.Count] };
                        for (int i = 0; i < inset.Count; i++)
                        {
                            inset.X[i] = cx + (zone.Shape.X[i] - cx) * 0.78;
                            inset.Y[i] = cy + (zone.Shape.Y[i] - cy) * 0.78 + 0.12 * _rules.SinkM;
                        }
                        _build.Cap(inset, _ox, _oy, CapM * 1.5f);
                        var step = Add(PartKind.Zone, z, true);
                        Style(step, Lin(_palette.Wall, ZoneGain), Color.black, 0);
                    }
                    break;
                }
            }
        }

        void BuildProp(int i)
        {
            var place = _def.Props[i];
            var kind = _rules.Props[place.Kind];
            double a = place.Degrees;
            _build.Clear();
            // Under a kit, the kind draws as its piece: its own, or the kit kind dressing a placeholder.
            var drawn = _rules.Props[_kit != null ? _kit.Dress[place.Kind] : place.Kind];
            if (_pieces.TryGetValue(drawn.Id, out var piece))
            {
                _build.Piece(piece, _ox + place.X, _oy + place.Y, a);
                var k = Add(PartKind.Prop, i, false);
                Style(k, Lin(_palette.Wall, WallGain), _palette.VoidBody.linear, 1, zoneLine: kind.Hits > 0);
                return;
            }
            var shape = kind.Shape == FootprintShape.Circle
                ? ArenaShape.Circle(0, 0, kind.Radius, kind.Radius < 0.5 ? 6 : 8, 22.5)
                : ArenaShape.Box(0, 0, 2 * kind.HalfX, 2 * kind.HalfY, a);
            _build.Prism(shape, _ox + place.X, _oy + place.Y, 0, (float)kind.HeightM);
            var p = Add(PartKind.Prop, i, false);
            // Grey is reserved for what can't be broken; the hero's breakable props take the zone key's edge colour.
            Style(p, Lin(_palette.Wall, WallGain), _palette.VoidBody.linear, 1, zoneLine: kind.Hits > 0);
        }

        /// <summary>The kit of the act whose boss fights in arena <paramref name="id"/> (not the stand-in every act shares), or null.</summary>
        string SlotKit(string id)
        {
            if (_rush == null || id == BossRushBoss.StandInArena) return null;
            foreach (var b in _rush.Bosses)
                if (b.Arena == id) return _rush.Acts[b.Act].Kit;
            return null;
        }

        /// <summary>A stable hash of the arena's id and two cells, for its scatter.</summary>
        static uint Hash(string id, int a, int b, int salt)
        {
            uint h = 2166136261;
            foreach (char ch in id) h = (h ^ ch) * 16777619;
            h = (h ^ (uint)a) * 16777619;
            h = (h ^ (uint)b) * 16777619;
            h = (h ^ (uint)salt) * 16777619;
            h ^= h >> 15;
            h *= 0x2c1b3c6d;
            h ^= h >> 12;
            return h;
        }

        static double Unit(uint h) => (h & 0xffffff) / (double)0x1000000;

        /// <summary>The kit's floor treatment: on a grid of cells over the arena, a share of them hold a piece somewhere in the cell, clear of everything solid.</summary>
        void BuildFloor()
        {
            if (_kit == null || _kit.FloorPieces.Length == 0) return;
            var pieces = new List<KitPiece>();
            foreach (var id in _kit.FloorPieces)
                if (_pieces.TryGetValue(id, out var p)) pieces.Add(p);
            if (pieces.Count == 0) return;
            _def.Outline.Bounds(out double bx, out double by, out double ex, out double ey);
            double cell = _kit.FloorCellM, clear = _kit.FloorClearM;
            int nx = (int)Math.Ceiling((ex - bx) / cell), ny = (int)Math.Ceiling((ey - by) / cell);
            _build.Clear();
            for (int gy = 0; gy < ny; gy++)
            for (int gx = 0; gx < nx; gx++)
            {
                if (Unit(Hash(_def.Id, gx, gy, 0)) >= _kit.FloorShare) continue;
                double x = bx + (gx + 0.15 + 0.7 * Unit(Hash(_def.Id, gx, gy, 1))) * cell;
                double y = by + (gy + 0.15 + 0.7 * Unit(Hash(_def.Id, gx, gy, 2))) * cell;
                var piece = pieces[(int)(Hash(_def.Id, gx, gy, 3) % (uint)pieces.Count)];
                double room = clear + piece.Reach * 0.5;
                if (!Clear(x, y, room)) continue;
                _build.Piece(piece, _ox + x, _oy + y, 360 * Unit(Hash(_def.Id, gx, gy, 4)));
            }
            if (_build.Empty) return;
            var part = Add(PartKind.Floor, 0, true);
            Style(part, Color.black, _palette.VoidBody.linear, FloorFill, zoneLine: true, edge: FloorEdge);
        }

        /// <summary>Is (x, y) open floor <paramref name="room"/> clear of the walls, the props, every zone (wherever a moving one goes) and the starts?</summary>
        bool Clear(double x, double y, double room)
        {
            var outline = _def.Outline;
            if (!outline.Contains(x, y) || outline.DistanceToOutline(x, y) < room) return false;
            foreach (var p in _def.Props)
            {
                double r = _rules.Props[p.Kind].Bound + room;
                if ((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y) < r * r) return false;
            }
            foreach (var z in _def.Zones)
            {
                int loop = Math.Max(1, z.PathX.Length);
                for (int k = 0; k < loop; k++)
                {
                    double zx = x - (z.Path ? z.PathX[k] : 0), zy = y - (z.Path ? z.PathY[k] : 0);
                    if (z.Shape.Contains(zx, zy) || z.Shape.DistanceToOutline(zx, zy) < room) return false;
                }
            }
            foreach (var (sx, sy) in new[] { (_def.HeroStartX, _def.HeroStartY), (_def.BossStartX, _def.BossStartY) })
                if ((x - sx) * (x - sx) + (y - sy) * (y - sy) < StartClearM * StartClearM) return false;
            return true;
        }

        /// <summary>
        /// The kit's backdrop: each ring's pieces at even steps round the outline, outside the walls by its gap, along the
        /// outline's tangent, only where the outline faces away from the camera (+y) at least the ring's facing; shorter
        /// toward the flanks, and a little taller or shorter piece by piece.
        /// </summary>
        void BuildBackdrop()
        {
            if (_kit == null) return;
            var o = _def.Outline;
            int n = o.Count;
            double sign = o.Area >= 0 ? 1 : -1, perimeter = 0;
            for (int i = 0; i < n; i++) perimeter += Leg(o, i);
            for (int r = 0; r < _kit.Backdrop.Length; r++)
            {
                var ring = _kit.Backdrop[r];
                var pieces = new List<KitPiece>();
                foreach (var id in ring.Pieces)
                    if (_pieces.TryGetValue(id, out var p)) pieces.Add(p);
                if (pieces.Count == 0) continue;
                _build.Clear();
                int count = Math.Max(1, (int)Math.Round(perimeter / ring.SpacingM));
                for (int k = 0; k < count; k++)
                {
                    double along = (k + 0.5 + 0.3 * (Unit(Hash(_def.Id, r, k, 5)) - 0.5)) * perimeter / count;
                    int i = 0;
                    while (i < n - 1 && along > Leg(o, i)) along -= Leg(o, i++);
                    int j = (i + 1) % n;
                    double dx = o.X[j] - o.X[i], dy = o.Y[j] - o.Y[i], len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
                    double tx = dx / len, ty = dy / len, nx = sign * ty, ny = -sign * tx;
                    if (ny < ring.Facing) continue;
                    var piece = pieces[(int)(Hash(_def.Id, r, k, 6) % (uint)pieces.Count)];
                    double t = Math.Min(1, (ny - ring.Facing) / 0.6);
                    float tall = (float)((0.55 + 0.45 * t * t * (3 - 2 * t)) * (0.85 + 0.3 * Unit(Hash(_def.Id, r, k, 7))));
                    double outward = _rules.WallThicknessM + ring.GapM + piece.Reach * 0.3;
                    double x = o.X[i] + tx * along + nx * outward, y = o.Y[i] + ty * along + ny * outward;
                    _build.Piece(piece, _ox + x, _oy + y, Math.Atan2(ty, tx) * 180 / Math.PI, tall);
                }
                if (_build.Empty) continue;
                var part = Add(PartKind.Backdrop, r, false);
                Style(part, Color.black, _palette.VoidBody.linear, 1, zoneLine: true, edge: BackdropEdge[Math.Min(r, BackdropEdge.Length - 1)]);
            }
        }

        static double Leg(ArenaShape o, int i)
        {
            int j = (i + 1) % o.Count;
            double dx = o.X[j] - o.X[i], dy = o.Y[j] - o.Y[i];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        void BuildObstacle(int id, ArenaObstacle o)
        {
            _build.Clear();
            Part p;
            switch (o.Kind)
            {
                case ArenaObstacleKind.Wall:
                    _build.Prism(o.Shape, 0, 0, 0, (float)_rules.RaiseM);
                    p = Add(PartKind.Obstacle, id, false);
                    Style(p, Lin(_palette.Wall, WallGain), _palette.VoidBody.linear, 1);
                    break;
                case ArenaObstacleKind.Web:
                    _build.Cap(o.Shape, 0, 0, CapM * 2, spokes: true);
                    p = Add(PartKind.Obstacle, id, true);
                    Style(p, Lin(_palette.Wall, ZoneGain), Color.black, 0);
                    break;
                default:
                    _build.Cap(o.Shape, 0, 0, CapM * 2);
                    p = Add(PartKind.Obstacle, id, true);
                    Style(p, Lin(_palette.Warning, HazardGain), _palette.Warning.linear * 0.25f, 0.6f);
                    break;
            }
            p.Reveal = 0;
        }

        // ---- Animation ----

        static float Smooth(double u)
        {
            float x = Mathf.Clamp01((float)u);
            return x * x * (3 - 2 * x);
        }

        static double Share(double t, double from, double to) => to <= from ? (t >= from ? 1 : 0) : (t - from) / (to - from);

        void Animate(double song)
        {
            if (_def == null) return;
            // Forming: the walls draw in round the outline over its first half and rise over all of it; props and raised
            // floor rise in its second half. Retracting runs it back.
            double form = Share(song, _formFrom, _formTo);
            if (!double.IsNaN(_retractFrom)) form = Math.Min(form, 1 - Share(song, _retractFrom, _retractTo));
            float reveal = Smooth(form * 2), rise = Smooth(form), late = Smooth(form * 2 - 1);
            foreach (var p in _parts)
            {
                p.Offset = Vector2.zero;
                switch (p.Kind)
                {
                    case PartKind.Walls:
                        p.Reveal = reveal;
                        p.Rise = rise;
                        break;
                    case PartKind.Zone:
                    {
                        float change = _zoneBeat[p.Index] > 0 ? Smooth(Share(song, _zoneFrom[p.Index], _zoneFrom[p.Index] + _zoneBeat[p.Index])) : 1;
                        p.Reveal = reveal;
                        p.Rise = late * (_shown[p.Index] == ArenaZoneState.Raised ? change : 1);
                        if (_def.Zones[p.Index].Path && _arena != null && _arena.Def == _def)
                        {
                            _arena.ZoneOffset(p.Index, out double zx, out double zy);
                            p.Offset = new Vector2((float)zx, (float)zy);
                        }
                        break;
                    }
                    case PartKind.Prop:
                    case PartKind.Floor:
                    case PartKind.Backdrop:
                        p.Reveal = 1;
                        p.Rise = late;
                        break;
                    case PartKind.Obstacle:
                    {
                        var o = _arena != null && p.Index < _arena.ObstacleCount ? _arena.Obstacle(p.Index) : null;
                        p.Reveal = 1;
                        p.Rise = Math.Min(late, o == null ? 1 : Smooth(Share(song, o.GrowFrom, o.GrowTo)));
                        break;
                    }
                }
                p.Block.SetFloat(RevealId, p.Reveal);
                p.Block.SetFloat(RiseId, Mathf.Max(1e-3f, p.Rise));
                if (p.Kind == PartKind.Prop)
                {
                    double since = song - _propFlash[p.Index];
                    float flash = since >= 0 && since < FlashS ? 1 - (float)(since / FlashS) : 0;
                    var kind = _rules.Props[_def.Props[p.Index].Kind];
                    p.Block.SetColor(LineId, Lin(_palette.Wall, WallGain * (1 + 1.5f * flash)));
                    p.Block.SetFloat(ZoneLineId, kind.Hits > 0 && flash <= 0 ? 1 : 0);
                }
            }
        }

        // ---- Camera ----

        void Frame(double song, Simulation sim, Vector2 hero, Camera camera)
        {
            if (_camera == null) return;
            if (_def == null || _arena == null || _arena.Def != _def)
            {
                _camera.Unframe();
                return;
            }
            float aspect = camera != null ? camera.aspect : 16f / 9f;
            if (_framing == null || Math.Abs(_framingAspect - aspect) > 1e-3f)
            {
                _framingAspect = aspect;
                _framing = new ArenaFraming(new CameraModel
                {
                    PitchDeg = WorldCamera.PitchDeg, FovDeg = WorldCamera.FovDeg, Aspect = aspect, DistanceM = WorldCamera.DistanceM,
                });
            }
            _keep.Clear();
            _keep.Add((hero.x, hero.y, 1.5));
            var bosses = sim.Bosses;
            if (bosses.InDuel)
            {
                var b = sim.Enemies[bosses.Id];
                _keep.Add((b.X, b.Y, b.Radius + 1));
                foreach (var m in bosses.Marks)
                {
                    m.Shape.Bounds(out double cx, out double cy, out double r);
                    _keep.Add((cx, cy, r));
                }
            }
            var lead = _camera.Lead;
            var f = _framing.Frame(_arena, hero.x + lead.x, hero.y + lead.y, _keep);
            _camera.Frame(new Vector2((float)f.FocusX, (float)f.FocusY), (float)f.Distance);
        }

        float _framingAspect;

        // ---- Drawing ----

        /// <summary>Queues the arena's draws for <paramref name="camera"/> (again, for the stills' second drawing of a frame).</summary>
        public void Queue(Camera camera)
        {
            foreach (var p in _parts)
            {
                if (p.Mesh.vertexCount == 0) continue;
                var rp = new RenderParams(p.Translucent ? _translucent : _opaque)
                {
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                    matProps = p.Block,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    camera = camera,
                    layer = camera != null ? camera.gameObject.layer : _layer,
                };
                Graphics.RenderMesh(rp, p.Mesh, 0, Matrix4x4.Translate(new Vector3(p.Offset.x, 0, p.Offset.y)));
            }
        }

        public void Dispose()
        {
            foreach (var p in _parts) Destroy(p.Mesh);
            foreach (var m in _meshes) Destroy(m);
            _parts.Clear();
            _meshes.Clear();
            Destroy(_opaque);
            Destroy(_translucent);
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
