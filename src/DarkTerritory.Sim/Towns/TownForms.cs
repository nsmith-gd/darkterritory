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
        if (!forms.Enabled || !forms.ByTrade.TryGetValue(trade, out var weights) && !forms.ByTrade.TryGetValue("any", out weights))
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
        TownDistrict District(DistrictKind kind, string id, IEnumerable<(double U, double D)> outline, double level = 0, bool named = true)
        {
            var d = new TownDistrict(kind, id, named ? Name(id) : "", Clip([.. outline.Select(x => P(x.U, x.D))]), level);
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
            for (int i = _l.Districts.Count - 1; i >= 0; i--)
                if (_l.Districts[i].Holds(s, d))
                    return (_l.Districts[i].Kind, _l.Districts[i].Level);
            return (DistrictKind.Quarter, 0);
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
        void Lots(double ua, double da, double ub, double db, double wayWidth, int sides, double depth = 0, bool yards = false, double frontMul = 1)
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
                    double front = _lots.Range(_f.Lot[0], _f.Lot[1]) * frontMul;
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
        void Blocks(int side, double first, double every, double reach, double u0, double u1, bool yards = true)
        {
            double width = _t.Walled.Width;
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
                Lane(u, side > 0 ? from : -reach, side > 0 ? reach : -from, _t.Walled.LaneWidth);
            }
            for (double off = first; off <= reach - every / 2; off += every)
                Street(side * off, u0, u1, width);
            for (double off = first; off <= reach - every / 2; off += every)
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
            // Behind the square: the lanes, the green across from the square, and the head above them with its light.
            Green(_t.Walled.First + _t.Walled.Width / 2, _t.Walled.First + _t.Walled.Every - _t.Walled.Width / 2);
            District(DistrictKind.Terrace, "head", Blob(-L * 0.5, -wl + 5, 70, 32, 0.12, 18, "head"), _f.TerraceLevel);
            Landmark("lighthouse", -L * 0.5, -wl - 10, 0, 1);
            Landmark("chapel", -L * 0.5 + 40, -wl + 14, 0, 1);
            Way([(-L * 0.5 - 60, -wl + 30), (-L * 0.5 - 30, -wl + 8), (-L * 0.5 + 20, -wl + 6)], 4, levels: [0, _f.TerraceLevel, _f.TerraceLevel]);
            Blocks(-1, _t.Walled.First, _t.Walled.Every, wl - 45, -L + 20, -8);
            District(DistrictKind.Quarter, "lanes", Rect(-L + 40, -L * 0.6, -wl * 0.4, -wl * 0.8), named: true);
            // A row of net lofts and stores along the wharf's back, fronting it.
            Lots(-L + 100, quay - 2, -L * 0.55 - 4, quay - 2, 0, 2, 7, frontMul: 0.8);
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
            double pu = -L * 0.46, pd = -wl * 0.55, rim = Math.Min(wl * 0.45, L * 0.24);
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
            Blocks(1, 84, 28, wr - 14, -L + 20, -20);
            Blocks(-1, 34, 26, 62, -L + 20, -L * 0.62 - 10);
            District(DistrictKind.Quarter, "founders", Rect(pu - 60, pu + 60, -wl + 10, -wl + 40));
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
            // Above the line: the square's green, then the terraces up to the chapel hill and the high ward.
            Green(34, 56);
            District(DistrictKind.Terrace, "chapel", Blob(-L * 0.72, -wl * 0.72, 62, 36, 0.08, 18, "chapel"), _f.TerraceLevel);
            District(DistrictKind.Terrace, "high", Blob(-L * 0.35, -wl * 0.78, 58, 34, 0.08, 18, "high"), _f.TerraceLevel * 1.6);
            Landmark("chapel", -L * 0.72, -wl * 0.72 - 6, 0, 1);
            Landmark("watchtower", -L * 0.35 - 30, -wl * 0.78 - 14, 0, 1);
            Way([(-L * 0.72 + 50, -wl * 0.45), (-L * 0.72 + 30, -wl * 0.6), (-L * 0.72 + 10, -wl * 0.66)], 4, levels: [0, _f.TerraceLevel, _f.TerraceLevel]);
            Way([(-L * 0.35 + 50, -wl * 0.5), (-L * 0.35 + 30, -wl * 0.66), (-L * 0.35 + 10, -wl * 0.72)], 4, levels: [0, _f.TerraceLevel * 1.6, _f.TerraceLevel * 1.6]);
            RingLots(-L * 0.72, -wl * 0.72, 22, 4, outside: true);
            RingLots(-L * 0.35, -wl * 0.78, 20, 4, outside: true);
            Works(-L * 0.3, -30, 12, 24, 1, "tidewater");
            Blocks(-1, 34, 26, wl * 0.5, -L + 20, -10);
            // The hills' own roads, round under their edges.
            Way([(-L + 30, -wl * 0.5 - 20), (-24, -wl * 0.5 - 20)], 5);
            Lots(-L + 30, -wl * 0.5 - 20, -24, -wl * 0.5 - 20, 5, 3);
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
                    _l.Lots.Add(new Lot(Math.Round(cs, 3), Math.Round(cd, 3), drng.Chance(0.5) ? 1 : -1, 12, drng.Range(-0.3, 0.3), null, null, _f.WaterLevel - 0.6)
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
            Blocks(-1, 34, 24, wl - 18, -L * 0.45 + 10, -10);
            Lots(-L + 30, wr * 0.48, -L * 0.8, wr * 0.48, 3, 3, 0, false);
            Lots(-L * 0.1, wr * 0.55, -30, wr * 0.55, 3, 3, 0, false);
        }

        public Layout Finish()
        {
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
