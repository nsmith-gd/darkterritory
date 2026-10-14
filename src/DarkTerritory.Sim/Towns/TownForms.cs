using Ballast;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// The town types' ground plans (queue #424, ARCHITECTURE §8 note 692; the director, 13 Oct 2026: "multiple different town
/// types ... different town types that give the world a bit more flavor and a bit more meaning", from the director's five
/// renders: a harbour on a headland, a fen town on stilts, a quarry town down its pit's benches, a half-drowned town, a
/// walled garden of glasshouses). Each is laid in the town's rail frame from its way out: <c>u</c> metres along the line
/// from the exit gate (negative inside), <c>d</c> across it, the square on the left (−D) by the way out and the home
/// platform on the right, as every town has them. A form gives the wall's outline with its two gates (in from the last
/// night at <c>u = −length</c>, out to the next at 0), its quarters, water, decks and terraces, its ways, its green and its
/// works, and the lots its houses stand on, nearest the square first. The households, open houses, yards and people are
/// the walled town's (<see cref="TownGenerator"/>). Drawn from the town's own seed alone, so the town arrived at and the
/// town left from are the same (note 591).
/// </summary>
public static class TownForms
{
    /// <summary>The forms there are, in the order towns.json names them ("" is the street town of note 335).</summary>
    public static readonly string[] All = ["harbour", "dykeland", "quarry", "tidewater", "glasshouse"];

    /// <summary>
    /// A house's lot (note 692): its middle (S along the line, D across), the side its back is to (as
    /// <see cref="TownHouse.Side"/>), its frontage, how far it's turned (<see cref="TownHouse.Turn"/>: its front faces its way),
    /// the yard's street edge and back line (signed D, for a lot square to the line; null for none), and its ground's level.
    /// </summary>
    public sealed record Lot(double S, double D, int Side, double Frontage, double Turn, double? Edge, double? Back, double Level)
    {
        /// <summary>A lost house standing in the water (a tidewater town's drowned ward): only ever one of the lost.</summary>
        public bool Drowned { get; init; }
    }

    /// <summary>A form's ground plan, as <see cref="Make"/> lays it.</summary>
    public sealed class Layout
    {
        public string Form = "";
        public double Entry;
        /// <summary>How far past its wall it shapes the land (m; note 692's landforms).</summary>
        public double Margin;
        /// <summary>What the walk did (note 692): the stairs it laid, and the lots it left cut off, unbuilt.</summary>
        public int Stairs, CutOff;
        /// <summary>The lots each check of the finished ground dropped (a way, a landmark, the ground under it), for `dt town map`.</summary>
        public int OnWays, OnLandmarks, OffGround;
        public readonly List<(double S, double D)> Outline = [];
        public readonly List<TownDistrict> Districts = [];
        public readonly List<TownWay> Ways = [];
        public readonly List<TownStreet> Streets = [];
        public readonly List<TownLane> Lanes = [];
        public readonly List<Lot> Lots = [];
        /// <summary>Its landmarks (towns.json-free pieces the form stands: a lighthouse, a lift, a chapel), as works pieces are.</summary>
        public readonly List<(string Kind, string Name, double S, double D, double FaceS, double FaceD)> Landmarks = [];
        public TownGreen? Green;
        public TownWorks? Works;

        public TownBounds Bounds(double gate) => new(Entry, gate, Outline.Max(p => -p.D), Outline.Max(p => p.D), Streets, Lanes) { Outline = Outline };
    }

    /// <summary>Which form a town of <paramref name="trade"/> is (towns.json <c>forms.byTrade</c>); "" the street town.</summary>
    public static string Pick(FormTuning forms, string trade, Pcg32 rng)
    {
        if (!forms.Enabled)
            return "";
        if (forms.Force is { } force)
            return force;
        if (!forms.ByTrade.TryGetValue(trade, out var weights) && !forms.ByTrade.TryGetValue("any", out weights))
            return "";
        var keys = weights.Keys.Order(StringComparer.Ordinal).ToList();
        double total = keys.Sum(k => Math.Max(0, weights[k])), roll = rng.NextDouble() * total;
        foreach (var k in keys)
            if ((roll -= Math.Max(0, weights[k])) < 0)
                return k;
        return keys.Count > 0 ? keys[^1] : "";
    }

    /// <summary>
    /// The ground plan of a <paramref name="form"/> town whose exit gate is at <paramref name="gate"/> along the line, its
    /// square <paramref name="square"/>; null for the street town (or a form towns.json doesn't shape).
    /// </summary>
    public static Layout? Make(TownTuning t, TownWriting w, string form, double gate, TownSquare square, Func<string, Pcg32> rngFor)
    {
        if (form.Length == 0 || !t.Forms.Shapes.TryGetValue(form, out var shape))
            return null;
        var p = new Planner(t, w, form, shape, gate, square, rngFor);
        switch (form)
        {
            case "harbour": p.Harbour(); break;
            case "dykeland": p.Dykeland(); break;
            case "quarry": p.Quarry(); break;
            case "tidewater": p.Tidewater(); break;
            case "glasshouse": p.Glasshouse(); break;
            default: return null;
        }
        return p.Finish();
    }

    /// <summary>
    /// Lays one form out. A 1 m grid over the town says where's taken (the wall's margin, the line and its platform, the
    /// square, ways, water, works, the houses already placed), so nothing laid later stands on anything laid before.
    /// </summary>
    sealed class Planner
    {
        // Not design numbers: room a house's lot keeps off the next lot, its way and the wall (m).
        const double LotPad = 0.6, WallPad = 7, LineClear = 9.5, Cell = 1;
        /// <summary>A stair street's rise over its run: a steep street's steps (a 24° flight), walked up a stride at a time.</summary>
        const double StairSlope = 0.45;
        readonly TownTuning _t;
        readonly TownWriting _w;
        readonly FormShape _f;
        readonly double _gate;
        readonly TownSquare _sq;
        readonly Func<string, Pcg32> _rngFor;
        readonly Layout _l = new();
        bool[] _taken = [];
        double _s0, _d0;
        int _ns, _nd;
        Pcg32 _lots;

        public Planner(TownTuning t, TownWriting w, string form, FormShape f, double gate, TownSquare square, Func<string, Pcg32> rngFor)
        {
            (_t, _w, _f, _gate, _sq, _rngFor) = (t, w, f, gate, square, rngFor);
            _l.Form = form;
            _l.Entry = gate - f.Length;
            _l.Margin = f.Margin;
            _lots = rngFor("form.lots");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A point <paramref name="u"/> from the exit gate, <paramref name="d"/> across.</summary>
        (double S, double D) P(double u, double d) => (_gate + u, d);

        string Name(string district) =>
            _w.Forms.TryGetValue(_l.Form, out var words) && words.Districts.TryGetValue(district, out var names) && names.Length > 0
                ? _rngFor("form.names." + district).Pick(names) : "";

        /// <summary>The wall's outline (u, d pairs round it, from the entry gate's left side clockwise as seen from above).</summary>
        void Wall(params (double U, double D)[] pts)
        {
            foreach (var (u, d) in pts)
                _l.Outline.Add(P(u, d));
            _s0 = _l.Outline.Min(x => x.S) - 2;
            _d0 = _l.Outline.Min(x => x.D) - 2;
            _ns = (int)Math.Ceiling((_l.Outline.Max(x => x.S) + 2 - _s0) / Cell);
            _nd = (int)Math.Ceiling((_l.Outline.Max(x => x.D) + 2 - _d0) / Cell);
            _taken = new bool[_ns * _nd];
            var wall = new TownDistrict(DistrictKind.Quarter, "", "", _l.Outline);
            // Outside the wall and its walk's margin is taken; so is the line and its platform, and the square.
            for (int i = 0; i < _ns; i++)
                for (int j = 0; j < _nd; j++)
                {
                    double s = _s0 + (i + 0.5) * Cell, d = _d0 + (j + 0.5) * Cell;
                    if (!wall.Holds(s, d) || EdgeDistance(s, d) < WallPad || Math.Abs(d) < LineClear
                        || _sq.Holds(s, Math.Sign(d)) && Math.Abs(d) <= Math.Abs(_sq.WallD) + 1)
                        _taken[i * _nd + j] = true;
                }
        }

        double EdgeDistance(double s, double d)
        {
            double best = double.MaxValue;
            for (int i = 0, j = _l.Outline.Count - 1; i < _l.Outline.Count; j = i++)
                best = Math.Min(best, SegDist(s, d, _l.Outline[j], _l.Outline[i]));
            return best;
        }

        static double SegDist(double s, double d, (double S, double D) a, (double S, double D) b)
        {
            double es = b.S - a.S, ed = b.D - a.D, len2 = es * es + ed * ed;
            double k = len2 < 1e-9 ? 0 : Math.Clamp(((s - a.S) * es + (d - a.D) * ed) / len2, 0, 1);
            double x = a.S + es * k - s, y = a.D + ed * k - d;
            return Math.Sqrt(x * x + y * y);
        }

        bool Taken(double s, double d)
        {
            int i = (int)Math.Floor((s - _s0) / Cell), j = (int)Math.Floor((d - _d0) / Cell);
            return i < 0 || j < 0 || i >= _ns || j >= _nd || _taken[i * _nd + j];
        }

        /// <summary>Marks (or asks after) an oriented box: middle, the unit axis its length runs along, half-length and half-width.</summary>
        bool Box(double cs, double cd, double as_, double ad, double hl, double hw, bool mark)
        {
            double r = Math.Sqrt(hl * hl + hw * hw);
            int i0 = Math.Max(0, (int)Math.Floor((cs - r - _s0) / Cell)), i1 = Math.Min(_ns - 1, (int)Math.Floor((cs + r - _s0) / Cell));
            int j0 = Math.Max(0, (int)Math.Floor((cd - r - _d0) / Cell)), j1 = Math.Min(_nd - 1, (int)Math.Floor((cd + r - _d0) / Cell));
            if (!mark && (cs - r < _s0 || cs + r > _s0 + _ns * Cell || cd - r < _d0 || cd + r > _d0 + _nd * Cell))
                return false;
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    double x = _s0 + (i + 0.5) * Cell - cs, y = _d0 + (j + 0.5) * Cell - cd;
                    double along = x * as_ + y * ad, across = -x * ad + y * as_;
                    if (Math.Abs(along) > hl || Math.Abs(across) > hw)
                        continue;
                    if (!mark && _taken[i * _nd + j])
                        return false;
                    if (mark)
                        _taken[i * _nd + j] = true;
                }
            return true;
        }

        void Take(TownDistrict d)
        {
            for (int i = 0; i < _ns; i++)
                for (int j = 0; j < _nd; j++)
                    if (d.Holds(_s0 + (i + 0.5) * Cell, _d0 + (j + 0.5) * Cell))
                        _taken[i * _nd + j] = true;
        }

        void TakeRect(double s0, double s1, double d0, double d1) =>
            Box((s0 + s1) / 2, (d0 + d1) / 2, 1, 0, Math.Abs(s1 - s0) / 2, Math.Abs(d1 - d0) / 2, true);

        /// <summary>A piece of ground: added to the plan, and taken where nothing may be built (water, decks, plots, yards).</summary>
        TownDistrict District(DistrictKind kind, string id, IEnumerable<(double U, double D)> outline, double level = 0, bool named = true,
            double falloff = 0, bool outside = false, double? depth = null)
        {
            List<(double S, double D)> pts = [.. outline.Select(x => P(x.U, x.D))];
            var d = new TownDistrict(kind, id, named ? Name(id) : "", outside ? pts : Clip(pts), level)
            { Depth = depth ?? _f.WaterDepth, Falloff = falloff, Outside = outside };
            _l.Districts.Add(d);
            // (Water isn't taken: a hummock or a causeway may be raised out of it later. A lot asks the ground it's on.)
            if (kind is DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard)
                Take(d);
            return d;
        }

        /// <summary>
        /// A district's outline kept inside the wall's (Sutherland–Hodgman, edge by edge of the wall's outline: exact for a
        /// convex wall, near enough by a harbour's headland).
        /// </summary>
        List<(double S, double D)> Clip(List<(double S, double D)> poly)
        {
            var o = _l.Outline;
            // Which way round the wall goes, so "inside" is the same side of every edge.
            double area = 0;
            for (int i = 0; i < o.Count; i++)
                area += o[i].S * o[(i + 1) % o.Count].D - o[(i + 1) % o.Count].S * o[i].D;
            double sense = Math.Sign(area);
            for (int i = 0; i < o.Count && poly.Count > 2; i++)
            {
                var (a, b) = (o[i], o[(i + 1) % o.Count]);
                double Side((double S, double D) p) => sense * ((b.S - a.S) * (p.D - a.D) - (b.D - a.D) * (p.S - a.S));
                var next = new List<(double S, double D)>();
                for (int k = 0; k < poly.Count; k++)
                {
                    var (p, q) = (poly[k], poly[(k + 1) % poly.Count]);
                    double sp = Side(p), sq = Side(q);
                    if (sp >= 0)
                        next.Add(p);
                    if (sp >= 0 != sq >= 0)
                    {
                        double t = sp / (sp - sq);
                        next.Add((p.S + (q.S - p.S) * t, p.D + (q.D - p.D) * t));
                    }
                }
                poly = next;
            }
            return poly;
        }

        static IEnumerable<(double U, double D)> Rect(double u0, double u1, double d0, double d1) => [(u0, d0), (u1, d0), (u1, d1), (u0, d1)];

        /// <summary>A rough ring round (u, d): its radius wanders by <paramref name="wobble"/>, the same each time for the same seed.</summary>
        IEnumerable<(double U, double D)> Blob(double u, double d, double ru, double rd, double wobble, int points, string stream)
        {
            var rng = _rngFor("form.blob." + stream);
            double phase = rng.Range(0, Math.PI * 2);
            var bumps = Enumerable.Range(0, 3).Select(_ => (rng.Range(-wobble, wobble), rng.Range(0, Math.PI * 2))).ToList();
            for (int i = 0; i < points; i++)
            {
                double a = phase + i * Math.PI * 2 / points, k = 1;
                for (int b = 0; b < bumps.Count; b++)
                    k += bumps[b].Item1 * DMath.Sin(a * (b + 2) + bumps[b].Item2);
                yield return (u + DMath.Cos(a) * ru * k, d + DMath.Sin(a) * rd * k);
            }
        }

        /// <summary>The level of the ground at a point: the last district laid that holds it (terraces nest inward), else 0.</summary>
        (DistrictKind Kind, double Level) GroundAt(double s, double d)
        {
            // Each district over those before it, as TownGround lays them; a landform eases in over its falloff.
            var kind = DistrictKind.Quarter;
            double level = 0;
            bool inside = InWall(s, d);
            foreach (var x in _l.Districts)
            {
                // (The land outside the wall stays outside it: the town's ground inside is its own.)
                if (x.Outside && inside)
                    continue;
                if (x.Holds(s, d))
                {
                    (kind, level) = (x.Kind, x.Level);
                    continue;
                }
                if (x.Falloff <= 0 || x.Kind is DistrictKind.Water or DistrictKind.Deck)
                    continue;
                double away = x.Distance(s, d);
                if (away < x.Falloff)
                {
                    double t = 1 - away / x.Falloff;
                    level += (x.Level - level) * t * t * (3 - 2 * t);
                }
            }
            return (kind, level);
        }

        TownDistrict? _wall;

        bool InWall(double s, double d) => (_wall ??= new TownDistrict(DistrictKind.Quarter, "", "", _l.Outline)).Holds(s, d);

        /// <summary>
        /// The land the town stands in (note 692; the director, 13 Oct: "integrating the layout of the town with the layout
        /// of the landscape"): a landform outside the wall, easing over <paramref name="falloff"/> into what's under it and,
        /// at the margin's edge, into the line plan's land. Never across the line's own way in and out (its approach is
        /// laid level last, <see cref="Approach"/>).
        /// </summary>
        void Land(string id, IEnumerable<(double U, double D)> outline, double level, double falloff, double roll = 1)
        {
            var d = District(DistrictKind.Quarter, id, outline, level, named: false, falloff: falloff, outside: true);
            // Its swells (forms.shapes roll, this landform's share of it): each landform its own, the same town the same.
            if (roll * _f.Roll > 0)
                _l.Districts[^1] = d with { Roll = roll * _f.Roll, RollSeed = _rngFor($"form.roll.{id}.{_l.Districts.Count}").NextUInt() };
        }

        /// <summary>The sea or the flats outside the wall (note 692): water easing to the line's land at the margin's edge.</summary>
        void Sea(IEnumerable<(double U, double D)> outline, double level, double depth) =>
            District(DistrictKind.Water, "sea", outline, level, named: false, outside: true, depth: depth);

        /// <summary>The line's way in and out across the margin, level with the yard: an embankment through the landform.</summary>
        void Approach(double falloff)
        {
            double m = _f.Margin + 4, L = _f.Length;
            Land("approach", Rect(-L - m, -L + 2, -9, 9), 0, falloff, roll: 0);
            Land("approach", Rect(-2, m, -9, 9), 0, falloff, roll: 0);
        }

        /// <summary>A way through the town (its points as u, d), taken on the grid as wide as it is and a little more.</summary>
        void Way(IReadOnlyList<(double U, double D)> pts, double width, bool deck = false, double level = 0, IReadOnlyList<double>? levels = null)
        {
            var w = new TownWay([.. pts.Select(x => P(x.U, x.D))], width, deck, level) { Levels = levels };
            _l.Ways.Add(w);
            for (int i = 1; i < w.Points.Count; i++)
            {
                var (a, b) = (w.Points[i - 1], w.Points[i]);
                double len = Math.Sqrt((b.S - a.S) * (b.S - a.S) + (b.D - a.D) * (b.D - a.D));
                if (len < 1e-6)
                    continue;
                Box((a.S + b.S) / 2, (a.D + b.D) / 2, (b.S - a.S) / len, (b.D - a.D) / len, len / 2 + width / 2, width / 2 + 0.5, true);
            }
        }

        /// <summary>A street beside the line (u0 to u1 from the exit gate, d across), as the walled town's are, and taken.</summary>
        void Street(double d, double u0, double u1, double width)
        {
            (u0, u1) = Inside(d, u0, u1);
            if (u1 - u0 < 20)
                return;
            _l.Streets.Add(new TownStreet(d, _gate + u0, _gate + u1, width));
            TakeRect(_gate + u0, _gate + u1, d - width / 2 - 0.5, d + width / 2 + 0.5);
        }

        /// <summary>The stretch of u0..u1 at <paramref name="d"/> across that's inside the wall by its walk's margin.</summary>
        (double U0, double U1) Inside(double d, double u0, double u1)
        {
            var wall = new TownDistrict(DistrictKind.Quarter, "", "", _l.Outline);
            bool In(double u) => wall.Holds(_gate + u, d) && EdgeDistance(_gate + u, d) >= WallPad;
            while (u0 < u1 && !In(u0))
                u0 += 1;
            while (u1 > u0 && !In(u1))
                u1 -= 1;
            return (u0, u1);
        }

        /// <summary>A lane across the line's streets at u from d0 to d1, and taken.</summary>
        void Lane(double u, double d0, double d1, double width)
        {
            // Out to the wall's walk at most, where a cut corner comes in.
            var wall = new TownDistrict(DistrictKind.Quarter, "", "", _l.Outline);
            while (d1 > d0 + 10 && (!wall.Holds(_gate + u, d1) || EdgeDistance(_gate + u, d1) < WallPad))
                d1 -= 1;
            while (d0 < d1 - 10 && (!wall.Holds(_gate + u, d0) || EdgeDistance(_gate + u, d0) < WallPad))
                d0 += 1;
            _l.Lanes.Add(new TownLane(_gate + u, d0, d1, width));
            TakeRect(_gate + u - width / 2 - 0.5, _gate + u + width / 2 + 0.5, d0, d1);
        }

        /// <summary>
        /// House lots along a straight stretch of way from a to b (u, d), on its left (<paramref name="sides"/> &amp; 1, as
        /// you walk from a to b) and right (&amp; 2), fronting it: frontages from the form's lot range, each that fits.
        /// </summary>
        void Lots(double ua, double da, double ub, double db, double wayWidth, int sides, double depth = 0, bool yards = false)
        {
            var (a, b) = (P(ua, da), P(ub, db));
            double len = Math.Sqrt((b.S - a.S) * (b.S - a.S) + (b.D - a.D) * (b.D - a.D));
            if (len < 4)
                return;
            double ts = (b.S - a.S) / len, td = (b.D - a.D) / len;
            if (depth <= 0)
                depth = _f.LotDepth;
            foreach (int sigma in new[] { 1, -1 })
            {
                if ((sides & (sigma > 0 ? 1 : 2)) == 0)
                    continue;
                // The normal on this side: left of the way's direction is (−td, ts).
                double ns = -td * sigma, nd = ts * sigma;
                double off = wayWidth / 2 + _f.Setback + depth / 2;
                for (double x = 0; ;)
                {
                    double front = _lots.Range(_f.Lot[0], _f.Lot[1]);
                    if (x + front > len)
                        break;
                    double cs = a.S + ts * (x + front / 2) + ns * off, cd = a.D + td * (x + front / 2) + nd * off;
                    x += front;
                    if (!Box(cs, cd, ts, td, front / 2 - LotPad / 2, depth / 2 + _f.Setback / 2, false))
                        continue;
                    var (kind, level) = GroundAt(cs, cd);
                    if (kind is DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard)
                        continue;
                    // Not straddling a terrace's edge: its corners on the same ground as its middle.
                    bool flat = true;
                    foreach (var (cu, cv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                        flat &= GroundAt(cs + ts * cu * front / 2 + ns * cv * depth / 2, cd + td * cu * front / 2 + nd * cv * depth / 2).Level == level;
                    if (!flat)
                        continue;
                    Box(cs, cd, ts, td, front / 2 - LotPad / 2, depth / 2 + _f.Setback / 2, true);
                    // Its front faces the way: back along the normal.
                    double fs = -ns, fd = -nd;
                    var (side, turn) = Facing(fs, fd);
                    bool square = Math.Abs(turn) < 1e-9;
                    double? edge = square && yards ? cd - nd * (depth / 2 + _f.Setback) : null;
                    double? back = square && yards ? cd + nd * depth / 2 : null;
                    _l.Lots.Add(new Lot(Math.Round(cs, 3), Math.Round(cd, 3), side, front, turn, edge, back, level));
                }
            }
        }

        /// <summary>
        /// The side and turn of a house whose front faces (fs, fd) in the rail frame (<see cref="TownHouse.Body"/>: a side +1
        /// house's front faces −D, turned by its turn).
        /// </summary>
        static (int Side, double Turn) Facing(double fs, double fd)
        {
            if (Math.Abs(fs) < 1e-9)
                return (fd < 0 ? 1 : -1, 0);
            double plus = DMath.Atan2(fs, -fd), minus = DMath.Atan2(-fs, fd);
            return Math.Abs(plus) <= Math.Abs(minus) ? (1, plus) : (-1, minus);
        }

        /// <summary>Lots round a ring (centre u, d; radius r to the way's middle), fronting it from outside (or inside).</summary>
        void RingLots(double u, double d, double r, double wayWidth, bool outside, double depth = 0, double from = 0, double to = Math.PI * 2)
        {
            if (depth <= 0)
                depth = _f.LotDepth;
            double off = r + (outside ? 1 : -1) * (wayWidth / 2 + _f.Setback + depth / 2);
            if (off <= depth)
                return;
            for (double a = from; a < to;)
            {
                double front = _lots.Range(_f.Lot[0], _f.Lot[1]);
                double step = front / off;
                double mid = a + step / 2;
                a += step;
                if (a > to)
                    break;
                double cs = _gate + u + DMath.Cos(mid) * off, cd = d + DMath.Sin(mid) * off;
                double ts = -DMath.Sin(mid), td = DMath.Cos(mid);
                if (!Box(cs, cd, ts, td, front / 2 - LotPad / 2, depth / 2 + _f.Setback / 2, false))
                    continue;
                var (kind, level) = GroundAt(cs, cd);
                if (kind is DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard)
                    continue;
                Box(cs, cd, ts, td, front / 2 - LotPad / 2, depth / 2 + _f.Setback / 2, true);
                // Its front to the ring's way: inward from outside, outward from inside.
                double fs = (outside ? -1 : 1) * DMath.Cos(mid), fd = (outside ? -1 : 1) * DMath.Sin(mid);
                var (side, turn) = Facing(fs, fd);
                _l.Lots.Add(new Lot(Math.Round(cs, 3), Math.Round(cd, 3), side, front, turn, null, null, level));
            }
        }

        /// <summary>A circle's points (u, d) at radius r, <paramref name="n"/> of them.</summary>
        static List<(double U, double D)> Circle(double u, double d, double ru, double rd, int n, double from = 0, double to = Math.PI * 2)
        {
            var pts = new List<(double, double)>();
            bool closed = to - from >= Math.PI * 2 - 1e-9;
            int count = closed ? n : n + 1;
            for (int i = 0; i < count; i++)
            {
                double a = from + (to - from) * i / n;
                pts.Add((u + DMath.Cos(a) * ru, d + DMath.Sin(a) * rd));
            }
            return pts;
        }

        void Landmark(string kind, double u, double d, double faceU, double faceD, double pad = 1.5)
        {
            var (hs, hd, _) = TownFixtures.Size(kind);
            _l.Landmarks.Add((kind, "", _gate + u, d, faceU, faceD));
            Box(_gate + u, d, 1, 0, hs + pad, hd + pad, true);
        }

        /// <summary>The green across the first street from the square (note 353), as the walled town has it.</summary>
        void Green(double near, double far)
        {
            _l.Green = new TownGreen(_sq.S0 + 4, _sq.S1 - 4, near, far, _sq.Side);
            TakeRect(_l.Green.S0, _l.Green.S1, _sq.Side * near, _sq.Side * far);
        }

        void Works(double u0, double u1, double near, double far, int side, string trade)
        {
            _l.Works = new TownWorks(_gate + u0, _gate + u1, near, far, side, trade);
            TakeRect(_gate + u0, _gate + u1, side * near, side * far);
        }

        /// <summary>
        /// The street blocks beside the line on one side (<paramref name="side"/>): streets every <paramref name="every"/>
        /// m from <paramref name="first"/> out to <paramref name="reach"/>, between u0 and u1, a row of lots either side of
        /// each, lanes across every so often (the walled town's pattern, note 335, kept to a quarter of the town).
        /// </summary>
        void Blocks(int side, double first, double every, double reach, double u0, double u1, bool yards = true, double rise = 0)
        {
            double width = _t.Walled.Width;
            var offs = new List<double>();
            for (double off = first; off <= reach - every / 2; off += every)
                offs.Add(off);
            // Terraced (note 692; the director: "the verticality that we're trying to add"): each street's band a tier over
            // the last, the first (the square's and the green's) the yard's level, a retaining wall along the back line
            // between two rows' yards, the lanes climbing them as stair streets.
            double Level(int k) => rise * k;
            if (rise > 0)
            {
                for (int k = 1; k < offs.Count; k++)
                {
                    double lo = offs[k] - every / 2, hi = k == offs.Count - 1 ? reach + 80 : offs[k] + every / 2;
                    District(DistrictKind.Terrace, "tier", Rect(u0 - 6, u1 + 6, side * lo, side * hi), Level(k), named: false);
                }
                if (_l.Green is { } g && g.Side == side)
                    District(DistrictKind.Terrace, "green", Rect(g.S0 - _gate, g.S1 - _gate, side * g.Near, side * g.Far), 0, named: false);
            }
            var lanes = new List<double>();
            for (double u = u0 + _lots.Range(_t.Walled.LaneEvery[0], _t.Walled.LaneEvery[1]) * 0.6; u < u1 - 30; u += _lots.Range(_t.Walled.LaneEvery[0], _t.Walled.LaneEvery[1]) * 0.7)
                lanes.Add(Math.Round(u, 1));
            // Never through the square or across the green beyond it (note 353).
            double q0 = _sq.S0 - _gate - 14, q1 = _sq.S1 - _gate + 14;
            foreach (double u in lanes)
            {
                if (side == _sq.Side && u > q0 && u < q1)
                    continue;
                // From the line, or from past the works where they're in the way (a lane runs round a works, not through it).
                double from = LineClear;
                if (_l.Works is { } wk && wk.Side == side && _gate + u > wk.S0 - 6 && _gate + u < wk.S1 + 6)
                    from = wk.Far + 1;
                if (rise <= 0)
                {
                    Lane(u, side > 0 ? from : -reach, side > 0 ? reach : -from, _t.Walled.LaneWidth);
                    continue;
                }
                // A stair street: level along each band, a flight across each back line (a stair's slope, StairSlope).
                var pts = new List<(double U, double D)> { (u, side * from) };
                var levels = new List<double> { 0 };
                double run = rise / StairSlope / 2;
                for (int k = 0; k + 1 < offs.Count; k++)
                {
                    double back = offs[k] + every / 2;
                    if (back - run <= from)
                        continue;
                    pts.Add((u, side * (back - run)));
                    levels.Add(Level(k));
                    pts.Add((u, side * (back + run)));
                    levels.Add(Level(k + 1));
                }
                pts.Add((u, side * reach));
                levels.Add(Level(offs.Count - 1));
                Way(pts, _t.Walled.LaneWidth, levels: levels);
            }
            foreach (double off in offs)
                Street(side * off, u0, u1, width);
            foreach (double off in offs)
            {
                double d = side * off;
                // Both rows front the street: the line-side row's lots on the street's line side, the far row's beyond.
                Lots(u0, d, u1, d, width, side > 0 ? 3 : 3, 0, yards);
            }
        }

        // ------------------------------------------------------------------ the forms

        /// <summary>
        /// A harbour town on its headland (the director's "Breakwater Town"; Lunenburg, Louisbourg): the harbour inside the
        /// sea wall on the platform's side, its wharf along the yard with the piers out over the water, the slip and the boat
        /// yard by the way in, the breakwater round the basin; the lanes behind the square climbing to the head with its
        /// lighthouse and chapel.
        /// </summary>
        public void Harbour()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            Wall((-L, -wl * 0.75), (-L + 60, -wl), (-L * 0.62, -wl), (-L * 0.5, -wl - 25), (-L * 0.36, -wl), (-70, -wl * 0.9), (0, -wl * 0.6),
                (0, wr * 0.35), (-50, wr), (-L + 40, wr), (-L, wr * 0.7));
            // The land it stands in (note 692): the headland climbing behind the town to its crag, the sea under the sea wall.
            double m = _f.Margin;
            Land("hill", Rect(-L - m - 40, m + 40, -wl - m - 60, -wl - 30), _f.TerraceLevel + 4, 28);
            Land("upland", Rect(-L - m - 40, m + 40, -wl - m - 60, -wl - 95), _f.TerraceLevel + 16, 60);
            Land("crag", Blob(-L * 0.5, -wl - 75, 120, 50, 0.15, 18, "crag"), _f.TerraceLevel + 28, 45);
            Sea(Rect(-L - m - 40, m + 40, wr + 3, wr + m + 60), -6, 5);
            Approach(16);
            // The harbour: from the wharf's edge out to the sea wall, its shore ragged.
            double quay = 30, edge = 48;
            var harbour = new List<(double, double)> { (-L + 95, edge), (-75, edge), (-55, wr - 18), (-L * 0.4, wr - 10), (-L + 70, wr - 14), (-L + 95, edge + 30) };
            District(DistrictKind.Water, "harbour", harbour, _f.WaterLevel);
            // The breakwater: a stone arm out from the sea wall's corner by the way out, round the basin's mouth.
            District(DistrictKind.Quarter, "breakwater", Rect(-L * 0.5, -60, wr - 34, wr - 22));
            Way([(-60, wr - 28), (-L * 0.5, wr - 28)], 6);
            Landmark("beacon", -L * 0.5 + 4, wr - 28, 1, 0);
            // The wharf along the town, and the fish stores between it and the platform (the works).
            District(DistrictKind.Quarter, "wharf", Rect(-L + 95, -40, quay, edge));
            Way([(-L + 100, (quay + edge) / 2 + 3), (-45, (quay + edge) / 2 + 3)], 7);
            Works(-L * 0.55, -36, 12, quay - 1, 1, "harbour");
            // The piers, out over the water, a T at the end of the longer ones.
            var prng = _rngFor("form.piers");
            for (double u = -L + 120; u < -95; u += prng.Range(55, 80))
            {
                double len = prng.Range(40, 75);
                District(DistrictKind.Deck, "pier", Rect(u - 3, u + 3, edge - 1, edge + len), 0.9, named: false);
                Way([(u, edge), (u, edge + len)], 5, deck: true, level: 0.9);
                if (len > 55)
                {
                    District(DistrictKind.Deck, "pier", Rect(u - 12, u + 12, edge + len - 6, edge + len), 0.9, named: false);
                    Way([(u - 11, edge + len - 3), (u + 11, edge + len - 3)], 6, deck: true, level: 0.9);
                }
            }
            // The slip and the boat yard by the way in: a hard sloping into the water, the sheds on it.
            District(DistrictKind.Yard, "slip", Rect(-L + 15, -L + 90, 18, wr * 0.6));
            Landmark("boatshed", -L + 52, 30, 0, 1);
            // Behind the square: the green across from it, the lanes climbing tier on tier up the headland (note 692), and the
            // head over them with its light and its chapel, a stair up to it from the top tier.
            Green(_t.Walled.First + _t.Walled.Width / 2, _t.Walled.First + _t.Walled.Every - _t.Walled.Width / 2);
            Blocks(-1, _t.Walled.First, 29, wl - 40, -L + 20, -8, rise: _f.Rise);
            double top = _f.Rise * 2;
            District(DistrictKind.Terrace, "head", Blob(-L * 0.5, -wl + 5, 70, 32, 0.12, 18, "head"), _f.TerraceLevel);
            Landmark("lighthouse", -L * 0.5, -wl - 10, 0, 1);
            Landmark("chapel", -L * 0.5 + 40, -wl + 14, 0, 1);
            Way([(-L * 0.5 - 75, -wl + 36), (-L * 0.5 - 40, -wl + 14), (-L * 0.5 + 10, -wl + 8)], 4, levels: [top, _f.TerraceLevel, _f.TerraceLevel]);
            District(DistrictKind.Quarter, "lanes", Rect(-L + 40, -L * 0.6, -wl * 0.4, -wl * 0.8), named: true);
            // A row of net lofts and stores along the wharf's back, fronting it.
            Lots(-L + 100, quay - 2, -L * 0.55 - 4, quay - 2, 0, 2, 7);
        }

        /// <summary>
        /// A dykeland town on the marsh (the director's "Fenfolk"; the Tantramar, Grand-Pré's aboiteaux): a palisade round
        /// salt marsh, the railway on its dyke through the middle, the houses on their hummocks along the boardwalks, the
        /// market hummock by the square, the chapel on the high hummock, the boat landing on the far side.
        /// </summary>
        public void Dykeland()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right, c = 50;
            Wall((-L, -wl + c), (-L + c, -wl), (-c, -wl), (0, -wl + c), (0, wr - c), (-c, wr), (-L + c, wr), (-L, wr - c));
            // The land it stands in (note 692): the tidal flats all round the palisade, waded at low water, the line in on its dyke.
            double m = _f.Margin;
            Sea(Rect(-L - m - 40, m + 40, -wl - m - 60, wr + m + 60), -0.6, 0.6);
            Approach(5);
            // The marsh everywhere inside, then the dyke the line runs on and the hummocks out of it.
            District(DistrictKind.Water, "marsh", _l.Outline.Select(x => (x.S - _gate, x.D)), _f.WaterLevel);
            District(DistrictKind.Quarter, "dyke", Rect(-L, 0, -16, 16), named: false);
            // The old dyke road behind the square, along the line, its houses on hummocks either side.
            District(DistrictKind.Quarter, "road", Rect(-L + 20, -20, -38, -26), named: false);
            District(DistrictKind.Quarter, "market", Blob(-62, -50, 56, 30, 0.06, 16, "market"));
            Green(36, 58);
            District(DistrictKind.Quarter, "upper", Blob(-L * 0.72, -wl * 0.6, 55, 38, 0.1, 18, "upper"));
            Landmark("chapel", -L * 0.72, -wl * 0.6 - 12, 0, 1);
            District(DistrictKind.Quarter, "lower", Blob(-L * 0.42, wr * 0.55, 48, 34, 0.1, 18, "lower"));
            District(DistrictKind.Quarter, "tower", Blob(-L * 0.18, wr * 0.72, 16, 16, 0.05, 10, "tower"), named: false);
            Landmark("watchtower", -L * 0.18, wr * 0.72, 0, -1);
            District(DistrictKind.Quarter, "hay", Rect(-L + 26, -L + 114, 16, 44), named: false);
            Works(-L + 30, -L + 110, 18, 40, 1, "dykeland");
            // The boardwalks: spines out from the dyke at each side, and walks between the hummocks.
            var spines = new List<(double U, int Side, double Reach)>();
            var brng = _rngFor("form.walks");
            for (double u = -L + 70; u < -20; u += brng.Range(70, 95))
                foreach (int side in new[] { -1, 1 })
                    spines.Add((Math.Round(u + brng.Range(-12, 12), 1), side, (side < 0 ? wl : wr) - 25));
            foreach (var (u, side, reach) in spines)
                if (!(side < 0 && u > -130))
                    Way([(u, side * 16), (u, side * reach)], 3, deck: true, level: 0.6);
            // Cross walks joining the spines' far halves, a dog-leg between each pair.
            foreach (int side in new[] { -1, 1 })
            {
                var mine = spines.Where(x => x.Side == side && !(side < 0 && x.U > -130)).OrderBy(x => x.U).ToList();
                for (int i = 1; i < mine.Count; i++)
                {
                    double da = side * brng.Range(45, 70), db = side * brng.Range(85, (side < 0 ? wl : wr) - 30);
                    double mid = (mine[i - 1].U + mine[i].U) / 2;
                    Way([(mine[i - 1].U, da), (mid, da), (mid, db), (mine[i].U, db)], 3, deck: true, level: 0.6);
                }
            }
            // The boat landing on the far side, its jetty out from the walk.
            District(DistrictKind.Deck, "landing", Rect(-L * 0.62, -L * 0.62 + 50, wr - 40, wr - 26), 0.6);
            // The houses: on their hummocks along every walk (a lot in the marsh is raised to stand on), and round the hummocks.
            foreach (var wy in _l.Ways.Where(x => x.Deck).ToList())
                for (int i = 1; i < wy.Points.Count; i++)
                    StiltLots(wy.Points[i - 1], wy.Points[i], wy.Width);
            Street(-32, -L + 20, -20, 6);
            StiltLots(P(-L + 20, -32), P(-20, -32), 6);
        }

        /// <summary>Lots along a boardwalk over the marsh, each raised on its own hummock (a pad of ground round its house).</summary>
        void StiltLots((double S, double D) a, (double S, double D) b, double width)
        {
            int before = _l.Lots.Count;
            // Lay them as on dry ground, ignoring the water, then raise a hummock under each.
            var water = _l.Districts.Where(x => x.Kind == DistrictKind.Water).ToList();
            foreach (var x in water)
                _l.Districts.Remove(x);
            Lots(a.S - _gate, a.D, b.S - _gate, b.D, width, 3, _f.LotDepth * 0.85);
            for (int i = 0; i < water.Count; i++)
                _l.Districts.Insert(i, water[i]);
            for (int i = before; i < _l.Lots.Count; i++)
            {
                var lot = _l.Lots[i];
                double r = lot.Frontage / 2 + 1.2;
                _l.Districts.Add(new TownDistrict(DistrictKind.Quarter, "hummock", "", [.. Circle(lot.S, lot.D, r, r, 10)]));
            }
        }

        /// <summary>
        /// A quarry town down its pit (the director's "Pitborn"; Springhill, the granite of Shelburne): the pit beside the
        /// square, its benches stepping down to the floor, a ramp winding down them, houses on every bench facing in; the
        /// founders' row on the rim's far side, the lift and the crane yard across the line, the workers' rows by the way in.
        /// </summary>
        public void Quarry()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            Wall((-L, -wl * 0.7), (-L + 70, -wl), (-90, -wl), (0, -wl * 0.6), (0, wr * 0.5), (-60, wr), (-L + 50, wr), (-L, wr * 0.6));
            // The land it stands in (note 692): a mesa, its walls on its edge, the ditch they cut round it to build them, the line
            // in on its embankment across the ditch.
            double m = _f.Margin;
            Land("ditch", Rect(-L - m - 60, m + 60, -wl - m - 60, wr + m + 60), -_f.TerraceLevel * 2.4, 0);
            Approach(26);
            double pu = -L * 0.46, pd = -wl * 0.5, rim = Math.Min(wl * 0.38, L * 0.22);
            int benches = _f.Benches;
            double step = rim / (benches + 1.5), drop = _f.TerraceLevel;
            // The pit: each bench a ring lower than the last, the floor at the bottom (nested: the deepest is laid last).
            for (int k = 0; k <= benches; k++)
            {
                double r = rim - k * step;
                District(DistrictKind.Terrace, k == benches ? "floor" : k == 0 ? "benches" : "bench", Blob(pu, pd, r, r * 0.92, 0.04, 28, "pit"),
                    -drop * (k + 1), named: k == 0 || k == benches);
            }
            // The ramp down: round the benches anticlockwise from the rim nearest the line, a bench lower each turn.
            var ramp = new List<(double, double)>();
            var levels = new List<double>();
            double a0 = Math.PI / 2;
            for (int k = 0; k <= benches; k++)
            {
                double r = rim - (k + 0.5) * step;
                for (int i = 0; i <= 6; i++)
                {
                    double a = a0 + (k * 7 + i) * 0.33;
                    ramp.Add((pu + DMath.Cos(a) * r, pd + DMath.Sin(a) * r * 0.92));
                    levels.Add(-drop * (k + i / 6.0));
                }
            }
            Way(ramp, 4, levels: levels);
            // Houses on each bench, fronting the drop (facing in), the founders' and the rim's facing in too.
            for (int k = 0; k < benches; k++)
                RingLots(pu, pd, rim - (k + 1) * step + step * 0.15, 2, outside: true, depth: step * 0.55);
            RingLots(pu, pd, rim + 2, 5, outside: true);
            Landmark("lift", pu + rim * 0.2, pd + rim * 0.95, 0, -1);
            Landmark("crane", pu - rim * 0.6, pd - rim * 0.1, 1, 0);
            // The square's green, the yard and works across the line (the lift's winding house, the tip), the workers' rows.
            Green(34, 54);
            Works(-L * 0.62, -40, 14, 70, 1, "coal");
            Blocks(1, 84, 29, wr - 14, -L + 20, -20);
            Blocks(-1, 34, 29, 66, -L + 20, -L * 0.62 - 10);
            // The founders' row on the rim's far side, between the pit and the wall.
            if (pd - rim - 4 > -wl + 14)
                District(DistrictKind.Quarter, "founders", Rect(pu - 50, pu + 50, -wl + 8, pd - rim - 4));
            District(DistrictKind.Quarter, "cranes", Rect(-L * 0.62, -40, 14, 70));
        }

        /// <summary>
        /// A tidewater town half drowned (the director's "Drowned Boroughs"; the Fundy tide that comes in faster than a man
        /// walks): the low ward on the platform's side under the water, its lost houses standing in it, causeways out to the
        /// wharves; the town above climbing in terraces to the chapel hill and the high ward with its watch.
        /// </summary>
        public void Tidewater()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            Wall((-L, -wl * 0.8), (-L + 40, -wl), (-L * 0.45, -wl - 10), (-40, -wl), (0, -wl * 0.65), (0, wr * 0.4), (-40, wr), (-L + 30, wr), (-L, wr * 0.8));
            // The land it stands in (note 692): the ridge the old town climbed, the tide outside the sea wall.
            double m = _f.Margin;
            Land("ridge", Rect(-L - m - 40, m + 40, -wl - m - 60, -wl - 25), _f.TerraceLevel + 8, 30);
            Land("ridge", Rect(-L - m - 40, m + 40, -wl - m - 60, -wl - 90), _f.TerraceLevel + 24, 60);
            Sea(Rect(-L - m - 40, m + 40, wr + 3, wr + m + 60), -4, 3.5);
            Approach(18);
            var water = Blob(-L * 0.5, wr * 0.62, L * 0.45, wr * 0.45, 0.07, 24, "drowned").Select(x => (x.U, Math.Max(x.D, 26.0))).ToList();
            District(DistrictKind.Water, "drowned", water, _f.WaterLevel);
            // The causeways out across the flood to the wharf islands, and the wharves' decks.
            var crng = _rngFor("form.causeways");
            for (int i = 0; i < 3; i++)
            {
                double u = -L * (0.25 + 0.25 * i) + crng.Range(-15, 15), far = wr * crng.Range(0.6, 0.8);
                District(DistrictKind.Quarter, "causeway", Rect(u - 4, u + 4, 20, far), named: false);
                Way([(u, 20), (u, far)], 5);
                District(DistrictKind.Quarter, i == 1 ? "wharves" : "wharf", Blob(u, far + 10, 24, 14, 0.1, 12, "wharf" + i), named: i == 1);
                District(DistrictKind.Deck, "staging", Rect(u + 18, u + 24, far, far + 26), 0.8, named: false);
                Lots(u - 20, far + 12, u + 20, far + 12, 3, 3, 0, false);
            }
            // The drowned houses: the lost, standing in the water where their street was.
            DrownedLots(water);
            // Above the line: the square's green, the blocks climbing tier on tier toward the hills (note 692), then the
            // chapel hill and the high ward over the top tier, a stair up to each from it, houses round their crowns.
            Green(34, 56);
            Works(-L * 0.3, -30, 12, 24, 1, "tidewater");
            Blocks(-1, 34, 29, wl * 0.72, -L + 20, -10, rise: _f.Rise);
            double top = _f.Rise * 2;
            District(DistrictKind.Terrace, "chapel", Blob(-L * 0.72, -wl * 0.74, 62, 34, 0.08, 18, "chapel"), _f.TerraceLevel);
            District(DistrictKind.Terrace, "high", Blob(-L * 0.35, -wl * 0.8, 58, 30, 0.08, 18, "high"), _f.TerraceLevel * 1.6);
            // Between the stair lanes that climb to the hills' feet, not on one.
            Landmark("chapel", -L * 0.72 - 30, -wl * 0.74 - 6, 0, 1);
            Landmark("watchtower", -L * 0.35, -wl * 0.8 - 12, 0, 1);
            Way([(-L * 0.72 + 64, -wl * 0.56), (-L * 0.72 + 34, -wl * 0.64), (-L * 0.72 + 12, -wl * 0.68)], 4, levels: [top, _f.TerraceLevel, _f.TerraceLevel]);
            Way([(-L * 0.35 + 64, -wl * 0.56), (-L * 0.35 + 30, -wl * 0.7), (-L * 0.35 + 10, -wl * 0.74)], 4, levels: [top, _f.TerraceLevel * 1.6, _f.TerraceLevel * 1.6]);
            RingLots(-L * 0.72, -wl * 0.74, 22, 4, outside: true);
            RingLots(-L * 0.35, -wl * 0.8, 20, 4, outside: true);
            Landmark("crane", -60, 22, 0, 1);
        }

        void DrownedLots(List<(double U, double D)> water)
        {
            var area = new TownDistrict(DistrictKind.Water, "", "", [.. water.Select(x => P(x.U, x.D))]);
            var drng = _rngFor("form.drowned");
            double u0 = water.Min(x => x.U) + 10, u1 = water.Max(x => x.U) - 10;
            for (double d = 40; d < water.Max(x => x.D) - 10; d += 26)
                for (double u = u0; u < u1; u += drng.Range(12, 18))
                {
                    var (cs, cd) = P(u, d + drng.Range(-2, 2));
                    if (!area.Holds(cs, cd) || drng.Chance(0.35) || !Box(cs, cd, 1, 0, 5, 4, false))
                        continue;
                    Box(cs, cd, 1, 0, 5.5, 4.5, true);
                    _l.Lots.Add(new Lot(Math.Round(cs, 3), Math.Round(cd, 3), drng.Chance(0.5) ? 1 : -1, 12, drng.Range(-0.3, 0.3), null, null, _f.WaterLevel - _f.WaterDepth)
                    { Drowned = true });
                }
        }

        /// <summary>
        /// A glasshouse town (the director's "Glass Gardeners"; the Annapolis Valley's orchards gone under glass): an
        /// eight-sided wall round its gardens, the long glasshouses on the platform's side, the reservoirs beside them, the
        /// beds behind the square, the long houses of those who tend them, the seed house among its plots.
        /// </summary>
        public void Glasshouse()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right, c = Math.Min(L, wl + wr) * 0.22;
            Wall((-L, -wl + c), (-L + c, -wl), (-c, -wl), (0, -wl + c), (0, wr - c), (-c, wr), (-L + c, wr), (-L, wr - c));
            // The land it stands in (note 692): a valley, the glass in its bottom out of the wind, the ridges either side.
            double m = _f.Margin;
            Land("ridge", Rect(-L - m - 60, m + 60, -wl - m - 60, -wl - 30), 30, 70);
            Land("ridge", Rect(-L - m - 60, m + 60, wr + 30, wr + m + 60), 26, 70);
            // The reservoirs: round tanks of water past the glasshouses, walks round them.
            var rrng = _rngFor("form.reservoirs");
            for (int i = 0; i < 4; i++)
            {
                double u = -L * (0.22 + 0.18 * i), d = wr * (i % 2 == 0 ? 0.78 : 0.62), r = rrng.Range(16, 22);
                District(DistrictKind.Water, i == 1 ? "reservoir" : "tank", Circle(u, d, r, r, 20), _f.WaterLevel, named: i == 1);
                Way(Circle(u, d, r + 3, r + 3, 20).Append((u + r + 3, d)).ToList(), 2.5);
            }
            // The glasshouses: the long ranges across the line, end to end in rows with walks between, the works (the pump
            // tower, the stores) by the way out.
            double g0 = -L * 0.8, g1 = -L * 0.3;
            var (ghs, ghd, _) = TownFixtures.Size("glasshouse");
            for (double d = 16 + ghd; d + ghd < wr * 0.5; d += 2 * ghd + 4)
            {
                for (double u = g0 + ghs; u + ghs < g1; u += 2 * ghs)
                    Landmark("glasshouse", u, d, 0, -1, pad: 0.2);
                Way([(g0, d + ghd + 2), (g1, d + ghd + 2)], 3);
            }
            District(DistrictKind.Quarter, "glass", Rect(g0, g1, 14, wr * 0.5));
            Works(-L * 0.3 + 6, -40, 14, wr * 0.42, 1, "glasshouse");
            // The beds behind the square, in plots with walks between.
            double pu0 = -L + 40, pu1 = -L * 0.45;
            for (double u = pu0; u < pu1 - 20; u += 34)
                for (double d = -40; d > -wl + 40; d -= 30)
                    District(DistrictKind.Plots, "beds", Rect(u + 2, u + 30, d - 2, d - 26), named: u == pu0 && d == -40);
            for (double u = pu0; u < pu1; u += 34)
                Way([(u, -16), (u, -wl + 20)], 2.5);
            Landmark("seedhouse", (pu0 + pu1) / 2, -wl + 26, 0, 1);
            // The long houses (rows of the same house, the gardeners'), and the square's green.
            Green(34, 56);
            Blocks(-1, 34, 29, wl - 18, -L * 0.45 + 10, -10);
            Lots(-L + 30, wr * 0.48, -L * 0.8, wr * 0.48, 3, 3, 0, false);
            Lots(-L * 0.1, wr * 0.55, -30, wr * 0.55, 3, 3, 0, false);
        }

        /// <summary>Where a lot's front door is reached from: a step out from its front (its front faces its way).</summary>
        (double S, double D) Front(Lot x)
        {
            double sn = DMath.Sin(x.Turn), cs = DMath.Cos(x.Turn);
            var (fs, fd) = x.Side > 0 ? (sn, -cs) : (-sn, cs);
            double out_ = _f.LotDepth / 2 + _f.Setback / 2 + 1.2;
            return (x.S + fs * out_, x.D + fd * out_);
        }

        /// <summary>
        /// Every house reached on foot from the yard (note 692; the director, 13 Oct: "make sure that there's always walkable
        /// paths for the player"): the town's ground walked from the platform, steps no taller than a stride, no water too
        /// deep to wade. Where a house's front isn't reached, a stair is laid up the least step between the ground it's on
        /// and the ground that is, nearest it; once the stairs run out (<see cref="StairRounds"/>), a house still cut off isn't
        /// built. Drowned houses stand in the water and aren't walked to.
        /// </summary>
        void Walkable()
        {
            var start = (S: _gate - 60, D: 6.0);
            for (int round = 0; round <= StairRounds; round++)
            {
                var ground = TownGround.Build(_l.Bounds(_gate), _l.Districts, _l.Ways, 0);
                var walk = ground.Walk(start.S, start.D);
                var cut = _l.Lots.Where(x => !x.Drowned && Front(x) is var (fs, fd) && !walk(fs, fd)).ToList();
                if (cut.Count == 0)
                    return;
                if (round == StairRounds)
                {
                    _l.CutOff += _l.Lots.RemoveAll(x => cut.Contains(x));
                    return;
                }
                // The first cut off (nearest the square: the lots are in no order yet, so the first laid), its own ground's
                // reach, and the least step from it to ground the yard reaches, nearest the house.
                var (ls, ld) = Front(cut[0]);
                var mine = ground.Walk(ls, ld);
                (double S, double D, double Ds, double Dd, double From, double To, double Score)? best = null;
                for (double s = Math.Max(ground.S0, ls - 160) + 0.5; s < Math.Min(ground.S0 + ground.Ns, ls + 160); s += 1)
                    for (double d = Math.Max(ground.D0, ld - 160) + 0.5; d < Math.Min(ground.D0 + ground.Nd, ld + 160); d += 1)
                    {
                        if (!mine(s, d))
                            continue;
                        foreach (var (es, ed) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
                        {
                            if (!walk(s + es, d + ed) || ground.Water(s + es, d + ed) is not null)
                                continue;
                            double from = ground.Level(s, d), to = ground.Level(s + es, d + ed);
                            double score = Math.Abs(to - from) * 6 + Math.Sqrt((s - ls) * (s - ls) + (d - ld) * (d - ld));
                            if (best is null || score < best.Value.Score)
                                best = (s, d, es, ed, from, to, score);
                        }
                    }
                if (best is not { } b)
                {
                    // Nowhere to stair it to (an island of ground): its houses aren't built.
                    _l.CutOff += _l.Lots.RemoveAll(x => Front(x) is var (fs, fd) && mine(fs, fd));
                    continue;
                }
                double run = Math.Abs(b.To - b.From) / StairSlope + 3, ms = b.S + b.Ds / 2, md = b.D + b.Dd / 2;
                Way([(ms - b.Ds * run / 2 - _gate, md - b.Dd * run / 2), (ms + b.Ds * run / 2 - _gate, md + b.Dd * run / 2)], 3, levels: [b.From, b.To]);
                _l.Stairs++;
                bool OnTheStair(Lot x) => SegDist(x.S, x.D, _l.Ways[^1].Points[0], _l.Ways[^1].Points[1]) <= 3 / 2.0 + Math.Max(x.Frontage, _f.LotDepth) / 2;
                _l.Lots.RemoveAll(OnTheStair);
            }
        }

        /// <summary>How many stairs the walk may lay before it gives up on what's left cut off. Not a design number.</summary>
        const int StairRounds = 12;

        public Layout Finish()
        {
            // Every lot on its ground as the plan finally has it (a district laid after it may have raised or flooded it):
            // a lot on ground it can't stand on is dropped; a drowned one stands only in the water.
            // And none a way runs through (a way laid after it: its middle within the way's half-width and a step).
            bool OnAWay(Lot x) => _l.Ways.Any(w => Enumerable.Range(1, w.Points.Count - 1).Any(i => SegDist(x.S, x.D, w.Points[i - 1], w.Points[i]) <= w.Width / 2 + 1));
            // Nor one a landmark stands on (laid after it).
            bool OnALandmark(Lot x) => _l.Landmarks.Any(m => TownFixtures.Size(m.Kind) is var (hs, hd, _)
                && Math.Abs(x.S - m.S) < hs + x.Frontage / 2 && Math.Abs(x.D - m.D) < hd + _f.LotDepth / 2);
            _l.OnWays = _l.Lots.Count(OnAWay);
            _l.OnLandmarks = _l.Lots.Count(x => !OnAWay(x) && OnALandmark(x));
            var kept = _l.Lots.Where(x => !OnAWay(x) && !OnALandmark(x) && GroundAt(x.S, x.D) is var (kind, level) && (x.Drowned
                ? kind == DistrictKind.Water && Math.Abs(level - _f.WaterLevel) < 1e-6
                : kind is not (DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard) && Math.Abs(level - x.Level) < 1e-6)).ToList();
            _l.OffGround = _l.Lots.Count - _l.OnWays - _l.OnLandmarks - kept.Count;
            _l.Lots.Clear();
            _l.Lots.AddRange(kept);
            Walkable();
            // Nearest the square first: the town grows out from its heart (note 335). Lots that can hold an open house
            // (square to the line, on dry ground) are kept first among equals.
            double hs = (_sq.S0 + _sq.S1) / 2, hd = _sq.Side * 15;
            var sorted = _l.Lots.OrderBy(x => x.Drowned ? 1 : 0).ThenBy(x => (x.S - hs) * (x.S - hs) + (x.D - hd) * (x.D - hd)).ToList();
            _l.Lots.Clear();
            _l.Lots.AddRange(sorted);
            return _l;
        }
    }
}
