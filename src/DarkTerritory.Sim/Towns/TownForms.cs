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
    public static readonly string[] All = ["harbour", "dykeland", "quarry", "tidewater", "glasshouse", "furnace", "highline", "timber", "salt", "crag"];

    /// <summary>A breakwater's half-width, its round head's radius, and its walk's height over the yard (m; note 692).</summary>
    public const double MoleHalf = 4, MoleHead = 7, MoleLevel = 1.0;

    /// <summary>
    /// A house's lot (note 692): its middle (S along the line, D across), the side its back is to (as
    /// <see cref="TownHouse.Side"/>), its frontage, how far it's turned (<see cref="TownHouse.Turn"/>: its front faces its way),
    /// the yard's street edge and back line (signed D, for a lot square to the line; null for none), and its ground's level.
    /// </summary>
    public sealed record Lot(double S, double D, int Side, double Frontage, double Turn, double? Edge, double? Back, double Level);

    /// <summary>A form's ground plan, as <see cref="Make"/> lays it.</summary>
    public sealed class Layout
    {
        public string Form = "";
        public double Entry;
        /// <summary>How far past its wall it shapes the land (m; note 692's landforms).</summary>
        public double Margin;
        /// <summary>Whether its houses stand shoulder to shoulder, each as wide as its lot (its warren's rows).</summary>
        public bool Packed;
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
        /// <summary>The yard's walls (note 692): runs (along, across, along, across) behind each side's first row, open where a way goes through.</summary>
        public readonly List<(double S0, double D0, double S1, double D1)> Wards = [];
        /// <summary>Its landmarks (towns.json-free pieces the form stands: a lighthouse, a lift, a chapel), as works pieces are.</summary>
        public readonly List<(string Kind, string Name, double S, double D, double FaceS, double FaceD)> Landmarks = [];
        public TownGreen? Green;
        public TownWorks? Works;

        public TownBounds Bounds(double gate) => new(Entry, gate, Outline.Max(p => -p.D), Outline.Max(p => p.D), Streets, Lanes) { Outline = Outline, Gaps = Gaps, Timber = Timber };
        /// <summary>Whether its wall is a timber palisade, its towers of logs on legs (note 692: Timberholds).</summary>
        public bool Timber;
        /// <summary>Its wall's open stretches (a harbour's mouth) and its breakwaters (note 692).</summary>
        public readonly List<((double S, double D) A, (double S, double D) B)> Gaps = [];
        public readonly List<IReadOnlyList<(double S, double D)>> Moles = [];
    }

    /// <summary>Which form a town of <paramref name="trade"/> is (towns.json <c>forms.byTrade</c>); "" the street town.</summary>
    public static string Pick(FormTuning forms, string trade, Pcg32 rng)
    {
        // (A forced form first: one type to play or look at, whether or not the forms are in the trades' rotation.)
        if (forms.Force is { } force)
            return force;
        if (!forms.Enabled)
            return "";
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
            case "furnace": p.Furnace(); break;
            case "highline": p.Highline(); break;
            case "timber": p.Timber(); break;
            case "salt": p.Salt(); break;
            case "crag": p.Crag(); break;
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
        const double LotPad = 0.6, WallPad = 7, LineClear = 9.5, Cell = 1, BackPad = 0.35;
        /// <summary>The room a warren's band keeps over its two rows' depth (m): its streets' wander apart, a back alley.</summary>
        const double BandSlack = 1.2;
        /// <summary>A boardwalk's width, a stage's lip past its houses' backs, and a jetty's length out over the water (m).</summary>
        const double BoardwalkWidth = 2.5, StageLip = 0.6, JettyLength = 10;
        /// <summary>How far apart a drowned ward's boardwalks are (m: two stages' depth and a channel), and their planks' height over the yard (a step up from a causeway).</summary>
        const double BoroughEvery = 36, StageLevel = 0.6;
        /// <summary>A borough's clusters of houses along its walk, and the open water between them (m, from-to).</summary>
        static readonly double[] BoroughCluster = [16, 34], BoroughGap = [14, 30];
        /// <summary>A fen town's (note 692): its walks built up most of their length, a gap of reeds now and then.</summary>
        static readonly double[] FenCluster = [26, 52], FenGap = [6, 16];
        /// <summary>A stair street's rise over its run: a steep street's steps (a 24° flight), walked up a stride at a time.</summary>
        const double StairSlope = 0.45;
        /// <summary>
        /// A tall tier's lane at its retaining wall's foot, and its walk along the top (m; note 692, the director, 14 Oct: the
        /// hill towns "missing the elevation and tiering"): where a tier's step is too tall for a flight straight across its
        /// band, the flight climbs along the wall's foot in the lane, as the renders' walled wards have it.
        /// </summary>
        const double WallLane = 4;
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
            _l.Packed = f.Packed;
            _l.Timber = f.Palisade;
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
            // (Clipped to nothing by a small town's wall: a tier past it.)
            if (d.Outline.Count < 3)
                return d;
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
                if (x.Falloff <= 0 || x.Kind is DistrictKind.Water or DistrictKind.Deck or DistrictKind.Stage)
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

        /// <summary>A stair up to a hill's crown (note 692): from the ground its foot is on to <paramref name="to"/> at its second point, level after.</summary>
        void Climb(IReadOnlyList<(double U, double D)> pts, double to, double width = 4)
        {
            double from = GroundAt(_gate + pts[0].U, pts[0].D).Level;
            Way(pts, width, levels: [from, .. pts.Skip(1).Select(_ => to)]);
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
        /// A warren's streets (note 692; the director, 14 Oct, of the first pass: "they don't look like the references"):
        /// on one side of the line, from u0 to u1, street after street out from the yard's wall to <c>Far</c>, each
        /// wandering off straight, a row of houses either side of it shoulder to shoulder, the rows back to back. Each
        /// street's band a tier <c>Rise</c> over the last where the town climbs. <see cref="Tiers"/> lays its ground;
        /// <see cref="Rows"/>, once whatever stands over it (a hill, a ward) is laid too, its streets, stairs and houses.
        /// </summary>
        sealed record Warren(int Side, double U0, double U1, double Near, double Far, double Rise, double Pitch,
            List<double> Offs, List<(double A, double B)> Phase, string Stream)
        {
            /// <summary>Street k's middle at u (out from the line).</summary>
            /// <remarks>The streets wander together, as contours do (each its own amount, <c>Phase[k].B</c>), so a band between
            /// two keeps room for its two rows.</remarks>
            public double Street(int k, double u, double wander) =>
                Offs[k] + wander * Phase[k].B * (0.6 * DMath.Sin(u / 37 + Phase[0].A) + 0.4 * DMath.Sin(u / 17 + Phase[0].A * 1.7));

            /// <summary>The back line behind street k at u: halfway to the next street, the yard's wall before the first.</summary>
            public double Back(int k, double u, double wander) =>
                k < 0 ? Near : k + 1 < Offs.Count ? (Street(k, u, wander) + Street(k + 1, u, wander)) / 2 : Street(k, u, wander) + Pitch / 2;

            /// <summary>Street k's band's level.</summary>
            public double Level(int k) => Rise * k;
        }

        Warren Tiers(int side, double u0, double u1, double far, double rise, string stream)
        {
            var rng = _rngFor("form.warren." + stream);
            double w = _f.StreetWidth, depth = _f.LotDepth, pitch = w + 2 * (_f.Setback + depth) + BandSlack;
            // (A step too tall for a flight across its band: a lane at each wall's foot and a walk on its top, as the rings.)
            double gap = Math.Abs(rise) / StairSlope / 2 + w / 2 + 1 > pitch / 2 ? 2 * WallLane : 0;
            var offs = new List<double>();
            for (double c = _f.Near + _f.Setback + depth + w / 2 + BandSlack / 2; c <= far - w / 2; c += pitch + gap)
                offs.Add(c);
            var phase = offs.Select(_ => (rng.Range(0, Math.PI * 2), rng.Range(0.8, 1.2))).ToList();
            var wr = new Warren(side, u0, u1, _f.Near, far, rise, pitch, offs, phase, stream);
            // (Each band a tier over the last, or under it: a town stepping down to a cliff's edge, note 692.)
            if (rise != 0)
            {
                var us = Samples(u0 - 8, u1 + 8, 6);
                for (int k = 1; k < offs.Count; k++)
                {
                    var outline = us.Select(u => (u, side * wr.Back(k - 1, u, _f.Wander))).ToList();
                    for (int i = us.Count - 1; i >= 0; i--)
                        outline.Add((us[i], side * (k == offs.Count - 1 ? far + 80 : wr.Back(k, us[i], _f.Wander))));
                    // The first tier up carries the warren's name (world/towns.json forms' districts, by its stream).
                    District(DistrictKind.Terrace, k == 1 ? stream : "tier", outline, wr.Level(k), named: k == 1);
                }
                if (_l.Green is { } g && g.Side == side)
                    District(DistrictKind.Terrace, "green", Rect(g.S0 - _gate, g.S1 - _gate, side * g.Near, side * g.Far), 0, named: false);
            }
            return wr;
        }

        static List<double> Samples(double a, double b, double every)
        {
            int n = Math.Max(1, (int)Math.Ceiling((b - a) / every));
            return [.. Enumerable.Range(0, n + 1).Select(i => a + (b - a) * i / n)];
        }

        void Rows(Warren wr)
        {
            var rng = _rngFor("form.warren.rows." + wr.Stream);
            int side = wr.Side;
            double w = _f.StreetWidth, wander = _f.Wander;
            bool OnBand(double u, double d, double level) =>
                InWall(_gate + u, d) && EdgeDistance(_gate + u, d) >= WallPad && GroundAt(_gate + u, d) is var (kind, lv)
                && kind is not (DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard) && Math.Abs(lv - level) < 1e-6;
            // The stairs and lanes across them first (the rows keep off them): a flight up each back line where the town
            // climbs, never through the square or the works (a lane runs round a works), stopped where a hill stands over it.
            double q0 = _sq.S0 - _gate - 10, q1 = _sq.S1 - _gate + 10;
            bool along = Math.Abs(wr.Rise) / StairSlope / 2 + w / 2 + 1 > wr.Pitch / 2;
            int lane = 0;
            for (double u0 = wr.U0 + rng.Range(18, 36); u0 < wr.U1 - 14; u0 += rng.Range(36, 64))
            {
                if (side == _sq.Side && u0 > q0 && u0 < q1)
                    continue;
                if (_l.Works is { } wk && wk.Side == side && _gate + u0 > wk.S0 - 4 && _gate + u0 < wk.S1 + 4)
                    continue;
                // (A flight along a wall goes on from where it came up; they turn each way by turns.)
                double u = u0;
                int turn = lane++ % 2 == 0 ? 1 : -1;
                var pts = new List<(double U, double D)> { (u, side * (LineClear + 1)) };
                var levels = new List<double> { 0 };
                double run = Math.Abs(wr.Rise) / StairSlope / 2;
                for (int k = 0; k < wr.Offs.Count; k++)
                {
                    double back = wr.Back(k, u, wander), mid = wr.Street(k, u, wander);
                    if (!OnBand(u, side * mid, wr.Level(k)))
                        break;
                    pts.Add((u, side * mid));
                    levels.Add(wr.Level(k));
                    if (k + 1 == wr.Offs.Count || wr.Rise == 0)
                        continue;
                    if (along)
                    {
                        // Along the wall's foot on its lower side, the walk on its top the other: up (or down) the flight.
                        double lo = Math.Min(wr.Level(k), wr.Level(k + 1)), hi = Math.Max(wr.Level(k), wr.Level(k + 1));
                        double footSide = wr.Rise > 0 ? -1 : 1, need = (hi - lo) / StairSlope;
                        double Foot(double x) => wr.Back(k, x, wander) + footSide * WallLane / 2;
                        double Top(double x) => wr.Back(k, x, wander) - footSide * WallLane / 2;
                        var flight = new List<(double U, double D)>();
                        double x1 = u + turn * need;
                        for (int i = 0; i <= (int)Math.Ceiling(need / 1.5); i++)
                        {
                            double x = u + turn * Math.Min(need, i * 1.5);
                            flight.Add((x, side * Foot(x)));
                        }
                        if (!flight.All(f => OnBand(f.U, f.D, lo)) || !OnBand(x1, side * Top(x1), hi) || !OnBand(wr.Rise > 0 ? x1 : u, side * wr.Street(k + 1, wr.Rise > 0 ? x1 : u, wander), wr.Level(k + 1)))
                            break;
                        // Climbing out: the foot's on this street's side, the flight from here to x1, then over onto the top.
                        // Stepping down: over the top here first, then down the flight to x1 on the far side.
                        if (wr.Rise > 0)
                        {
                            for (int i = 0; i < flight.Count; i++)
                            {
                                pts.Add(flight[i]);
                                levels.Add(lo + (hi - lo) * Math.Min(1, i * 1.5 / need));
                            }
                            pts.Add((x1, side * Top(x1)));
                            levels.Add(hi);
                        }
                        else
                        {
                            pts.Add((u, side * Top(u)));
                            levels.Add(hi);
                            for (int i = 0; i < flight.Count; i++)
                            {
                                pts.Add(flight[i]);
                                levels.Add(hi - (hi - lo) * Math.Min(1, i * 1.5 / need));
                            }
                        }
                        u = x1;
                        continue;
                    }
                    if (!OnBand(u, side * (back + run + 1), wr.Level(k + 1)))
                        break;
                    pts.Add((u, side * (back - run)));
                    levels.Add(wr.Level(k));
                    pts.Add((u, side * (back + run)));
                    levels.Add(wr.Level(k + 1));
                }
                if (pts.Count > 2)
                    Way(pts, 3, levels: wr.Rise != 0 ? levels : null);
            }
            // Each street where it's on its own band, inside the wall, then its rows.
            var us = Samples(wr.U0, wr.U1, 8);
            for (int k = 0; k < wr.Offs.Count; k++)
            {
                var run = new List<(double U, double D)>();
                void Flush()
                {
                    if (run.Count >= 3)
                    {
                        Way([.. run], w);
                        LotsAlong(run, w, wr.Level(k));
                    }
                    run.Clear();
                }
                foreach (double u in us)
                {
                    double d = side * wr.Street(k, u, wander);
                    if (OnBand(u, d, wr.Level(k)) && Open(_gate + u, d))
                        run.Add((u, d));
                    else
                        Flush();
                }
                Flush();
            }
        }

        /// <summary>
        /// A warren wrapped round a hill (note 692; the director, 14 Oct, of Breakwater Town: "the reference image has a lot
        /// more of like an organic layout ... it feels very [griddy]"): rings of street round its crown at (u, d) on one side
        /// of the line, each a wandering ellipse (along the line <paramref name="aspect"/> times as long as across), a row of
        /// houses either side; each ring's band a tier <c>Rise</c> under the one inside it, down to the yard's level, the
        /// rings past that level; spokes up from the yard to the crown, kinked ring by ring, a flight across each back line.
        /// Returns the crown's level.
        /// </summary>
        double Rings(int side, double cu, double cd, double crown, double aspect, string stream, Func<double, double, bool>? keepOut = null)
        {
            var rng = _rngFor("form.rings." + stream);
            double w = _f.StreetWidth, pitch = w + 2 * (_f.Setback + _f.LotDepth) + BandSlack, wander = _f.Wander;
            // Where the yard's edge is nearest the crown, as the rings measure it; the rings reach the corners by the gates.
            double Rho(double u, double d) => Math.Sqrt((u - cu) * (u - cu) / (aspect * aspect) + (d - cd) * (d - cd));
            double yardRho = Math.Abs(side * _f.Near - cd), far = Math.Max(Rho(-_f.Length, side * _f.Near), Rho(0, side * _f.Near));
            // A step too tall for a flight across its band: a lane at each climbing wall's foot and a walk on its top, the
            // flight along the lane (`Along`).
            bool along = _f.Rise / StairSlope / 2 + w / 2 + 1 > pitch / 2;
            double gap = along ? 2 * WallLane : 0;
            var radii = new List<double>();
            for (double r = crown + _f.Setback + _f.LotDepth + w / 2 + BandSlack / 2 + gap / 2; r < far; r += pitch)
            {
                radii.Add(r);
                if (r + pitch + gap + pitch / 2 < yardRho)
                    r += gap;
            }
            // The rings that climb: those whose band is short of the yard's edge; the rest stand on the yard's level.
            int climbing = radii.Count(r => r + pitch / 2 < yardRho);
            double crownLevel = _f.Rise * climbing;
            double Level(int k) => _f.Rise * Math.Max(0, climbing - 1 - k);
            var phase = (A: rng.Range(0, Math.PI * 2), B: rng.Range(0, Math.PI * 2));
            var amount = radii.Select(_ => rng.Range(0.7, 1.3)).ToList();
            double Ring(int k, double a) => radii[k] + wander * amount[k] * (0.6 * DMath.Sin(a * 3 + phase.A) + 0.4 * DMath.Sin(a * 7 + phase.B));
            double Back(int k, double a) => k < 0 ? crown : k + 1 < radii.Count ? (Ring(k, a) + Ring(k + 1, a)) / 2 : Ring(k, a) + pitch / 2;
            (double U, double D) At(double rho, double a) => (cu + aspect * rho * DMath.Cos(a), cd + rho * DMath.Sin(a));
            // Kept on its side of the line, short of the yard.
            List<(double U, double D)> Ours(IEnumerable<(double U, double D)> poly) =>
                [.. poly.Select(p => (p.U, side < 0 ? Math.Min(p.D, -_f.Near + 0.5) : Math.Max(p.D, _f.Near - 0.5)))];
            // The tiers, outermost first, each inner one over it; then the crown.
            for (int k = climbing - 2; k >= 0; k--)
                District(DistrictKind.Terrace, k == climbing - 2 ? stream : "tier", Ours(Enumerable.Range(0, 72).Select(i => At(Back(k, i * Math.PI * 2 / 72), i * Math.PI * 2 / 72))),
                    Level(k), named: k == climbing - 2);
            District(DistrictKind.Terrace, "head", Ours(Enumerable.Range(0, 48).Select(i => At(crown, i * Math.PI * 2 / 48))), crownLevel);
            bool OnBand(double u, double d, double level) =>
                Math.Abs(d) >= _f.Near + 1 && Math.Sign(d) == side && keepOut?.Invoke(u, d) != true && InWall(_gate + u, d) && EdgeDistance(_gate + u, d) >= WallPad
                && GroundAt(_gate + u, d) is var (kind, lv) && kind is not (DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard)
                && Math.Abs(lv - level) < 1e-6;
            // The spokes: from the yard's edge up to the crown, a little off their line ring by ring, toward the yard's side.
            double toward = side < 0 ? Math.PI / 2 : -Math.PI / 2;
            int spoke = 0;
            for (double a0 = toward - 1.25 + rng.Range(0, 0.15); a0 < toward + 1.25; a0 += rng.Range(0.2, 0.32))
            {
                // (Its flights along the walls turn each way by turns.)
                int turn = spoke++ % 2 == 0 ? 1 : -1;
                double rho0 = Math.Abs((side * (LineClear + 1) - cd) / Math.Max(1e-6, DMath.Sin(a0)));
                // (From the yard's edge inside the wall: a spoke too far round to reach it there isn't laid.)
                if (At(rho0, a0) is var foot && (!InWall(_gate + foot.U, foot.D) || EdgeDistance(_gate + foot.U, foot.D) < WallPad))
                    continue;
                var pts = new List<(double U, double D)> { At(rho0, a0) };
                var levels = new List<double> { 0 };
                double run = _f.Rise / StairSlope / 2, a = a0;
                for (int k = radii.Count - 1; k >= 0; k--)
                {
                    a += rng.Range(-0.04, 0.04);
                    var mid = At(Ring(k, a), a);
                    if (Rho(mid.U, mid.D) >= rho0 - 2)
                        continue;
                    if (!OnBand(mid.U, mid.D, Level(k)))
                    {
                        if (pts.Count > 1)
                            break;
                        continue;
                    }
                    pts.Add(mid);
                    levels.Add(Level(k));
                    double inner = Back(k - 1, a), up = k > 0 ? Level(k - 1) : crownLevel;
                    if (Math.Abs(up - Level(k)) < 1e-6)
                        continue;
                    if (along)
                    {
                        // Out to the wall's foot, along it up the flight, over onto the walk on its top.
                        var flight = new List<(double U, double D)>();
                        double b = a, need = (up - Level(k)) / StairSlope, got = 0;
                        var at = At(Back(k - 1, b) + WallLane / 2, b);
                        flight.Add(at);
                        while (got < need)
                        {
                            b += turn * 1.5 / Math.Max(1, Back(k - 1, b) * aspect);
                            var next = At(Back(k - 1, b) + WallLane / 2, b);
                            got += Math.Sqrt((next.U - at.U) * (next.U - at.U) + (next.D - at.D) * (next.D - at.D));
                            flight.Add(at = next);
                        }
                        var onTop = At(Back(k - 1, b) - WallLane / 2, b);
                        if (!flight.All(f => OnBand(f.U, f.D, Level(k))) || !(OnBand(onTop.U, onTop.D, up) || k == 0 && GroundAt(_gate + onTop.U, onTop.D).Level == crownLevel))
                            break;
                        double walked = 0;
                        for (int i = 0; i < flight.Count; i++)
                        {
                            if (i > 0)
                                walked += Math.Sqrt((flight[i].U - flight[i - 1].U) * (flight[i].U - flight[i - 1].U) + (flight[i].D - flight[i - 1].D) * (flight[i].D - flight[i - 1].D));
                            pts.Add(flight[i]);
                            levels.Add(Level(k) + (up - Level(k)) * Math.Min(1, walked / need));
                        }
                        pts.Add(onTop);
                        levels.Add(up);
                        a = b;
                        continue;
                    }
                    var top = At(inner - run, a);
                    if (!OnBand(top.U, top.D, up) && !(k == 0 && GroundAt(_gate + top.U, top.D).Level == crownLevel))
                        break;
                    pts.Add(At(inner + run, a));
                    levels.Add(Level(k));
                    pts.Add(top);
                    levels.Add(up);
                }
                if (pts.Count > 2)
                    Way(pts, 3, levels: levels);
            }
            // Each ring where it's on its own band and open, then its rows.
            for (int k = 0; k < radii.Count; k++)
            {
                var run = new List<(double U, double D)>();
                void Flush()
                {
                    if (run.Count >= 3)
                    {
                        Way([.. run], w);
                        LotsAlong(run, w, Level(k));
                    }
                    run.Clear();
                }
                int n = (int)Math.Ceiling(Math.PI * 2 * radii[k] * aspect / 7);
                for (int i = 0; i <= n; i++)
                {
                    double a = i * Math.PI * 2 / n;
                    var p = At(Ring(k, a), a);
                    if (OnBand(p.U, p.D, Level(k)) && Open(_gate + p.U, p.D))
                        run.Add(p);
                    else
                        Flush();
                }
                Flush();
            }
            return crownLevel;
        }

        /// <summary>
        /// A breakwater (note 692; the director's "seawall and breakwater"): a stone mole out from the sea wall into the sea
        /// along its points (u, d), its top a walk <see cref="MoleLevel"/> over the yard's, a round head at its end with its
        /// light (<paramref name="light"/>, a landmark's kind, or none). Its ground is an outside district; the art draws it.
        /// </summary>
        void Mole(string id, IReadOnlyList<(double U, double D)> pts, string light)
        {
            _l.Moles.Add([.. pts.Select(p => P(p.U, p.D))]);
            District(DistrictKind.Quarter, id, [.. Offset(pts, MoleHalf), .. Enumerable.Reverse(Offset(pts, -MoleHalf))], MoleLevel, named: id == "breakwater", outside: true);
            District(DistrictKind.Quarter, id + "-head", Circle(pts[^1].U, pts[^1].D, MoleHead, MoleHead, 16), MoleLevel, named: false, outside: true);
            if (light.Length > 0)
                Landmark(light, pts[^1].U, pts[^1].D, pts[^1].U - pts[^2].U, pts[^1].D - pts[^2].D, pad: 0.3);
        }

        /// <summary>Whether a street may run here: not across the square, its green, or the works.</summary>
        bool Open(double s, double d) =>
            !(_sq.Holds(s, Math.Sign(d)) && Math.Abs(d) <= Math.Abs(_sq.WallD) + 2)
            && !(_l.Green is { } g && Math.Sign(d) == g.Side && s > g.S0 - 2 && s < g.S1 + 2 && Math.Abs(d) > g.Near - 2 && Math.Abs(d) < g.Far + 2)
            && !(_l.Works is { } wk && wk.Holds(s, d, 2));

        /// <summary>
        /// Lots either side of a street that bends (its points as u, d), fronting it all along, shoulder to shoulder where the
        /// form packs them (its lot pad then a hand's breadth): each as it comes along the street, turned to it there.
        /// </summary>
        void LotsAlong(IReadOnlyList<(double U, double D)> pts, double wayWidth, double level, double depth = 0, int sides = 3)
        {
            if (depth <= 0)
                depth = _f.LotDepth;
            double pad = _f.Packed ? 0.15 : LotPad, off = wayWidth / 2 + _f.Setback + depth / 2;
            foreach (int sigma in new[] { 1, -1 })
            {
                if ((sides & (sigma > 0 ? 1 : 2)) == 0)
                    continue;
                // The line the lots' middles stand on, out from the street's on this side: spaced along it, not the street's,
                // so a bend's inside row doesn't crowd (left of the way's direction is (−td, ts)).
                var line = new List<(double U, double D)>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var (a, b) = (pts[Math.Max(0, i - 1)], pts[Math.Min(pts.Count - 1, i + 1)]);
                    double tl = Math.Max(1e-9, Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.D - a.D) * (b.D - a.D)));
                    double ts = (b.U - a.U) / tl, td = (b.D - a.D) / tl;
                    line.Add((pts[i].U - td * sigma * off, pts[i].D + ts * sigma * off));
                }
                var cum = new List<double> { 0 };
                for (int i = 1; i < line.Count; i++)
                    cum.Add(cum[^1] + Math.Sqrt((line[i].U - line[i - 1].U) * (line[i].U - line[i - 1].U) + (line[i].D - line[i - 1].D) * (line[i].D - line[i - 1].D)));
                double len = cum[^1];
                (double U, double D, double Ts, double Td) At(double x)
                {
                    int i = 1;
                    while (i < cum.Count - 1 && cum[i] < x)
                        i++;
                    double seg = Math.Max(1e-9, cum[i] - cum[i - 1]), t = Math.Clamp((x - cum[i - 1]) / seg, 0, 1);
                    double ts = (line[i].U - line[i - 1].U) / seg, td = (line[i].D - line[i - 1].D) / seg;
                    return (line[i - 1].U + (line[i].U - line[i - 1].U) * t, line[i - 1].D + (line[i].D - line[i - 1].D) * t, ts, td);
                }
                for (double x = 0; ;)
                {
                    double front = _lots.Range(_f.Lot[0], _f.Lot[1]);
                    if (x + front > len)
                        break;
                    var (u, d, ts, td) = At(x + front / 2);
                    x += front;
                    double ns = -td * sigma, nd = ts * sigma;
                    double cs = _gate + u, cd = d;
                    // Its own ground only, short of the street's (taken) and the back line (the row behind's, a tier's edge).
                    double half = depth / 2 - BackPad;
                    if (!Box(cs, cd, ts, td, front / 2 - pad / 2, half, false))
                        continue;
                    var (kind, lv) = GroundAt(cs, cd);
                    if (kind is DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard || Math.Abs(lv - level) > 1e-6)
                        continue;
                    bool flat = true;
                    foreach (var (cu, cv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                        flat &= GroundAt(cs + ts * cu * front / 2 + ns * cv * half, cd + td * cu * front / 2 + nd * cv * half).Level == lv;
                    if (!flat)
                        continue;
                    Box(cs, cd, ts, td, front / 2 - pad / 2, half, true);
                    var (side, turn) = Facing(-ns, -nd);
                    _l.Lots.Add(new Lot(Math.Round(cs, 3), Math.Round(cd, 3), side, front, turn, null, null, lv));
                }
            }
        }

        /// <summary>A line's (u, d) stretch from <paramref name="from"/> to <paramref name="to"/> along it (m).</summary>
        static List<(double U, double D)> Slice(IReadOnlyList<(double U, double D)> pts, double from, double to)
        {
            var piece = new List<(double U, double D)>();
            double at = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var (a, b) = (pts[i - 1], pts[i]);
                double len = Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.D - a.D) * (b.D - a.D));
                (double U, double D) At(double x) => (a.U + (b.U - a.U) * (x - at) / Math.Max(1e-9, len), a.D + (b.D - a.D) * (x - at) / Math.Max(1e-9, len));
                double x0 = Math.Max(from, at), x1 = Math.Min(to, at + len);
                if (x1 > x0)
                {
                    if (piece.Count == 0)
                        piece.Add(At(x0));
                    piece.Add(At(x1));
                }
                at += len;
            }
            return piece;
        }

        /// <summary>A line (u, d) moved <paramref name="by"/> to its left (as you walk it from its first point; right if negative).</summary>
        static List<(double U, double D)> Offset(IReadOnlyList<(double U, double D)> pts, double by)
        {
            var line = new List<(double U, double D)>();
            for (int i = 0; i < pts.Count; i++)
            {
                var (a, b) = (pts[Math.Max(0, i - 1)], pts[Math.Min(pts.Count - 1, i + 1)]);
                double tl = Math.Max(1e-9, Math.Sqrt((b.U - a.U) * (b.U - a.U) + (b.D - a.D) * (b.D - a.D)));
                line.Add((pts[i].U - (b.D - a.D) / tl * by, pts[i].D + (b.U - a.U) / tl * by));
            }
            return line;
        }

        /// <summary>
        /// A boardwalk over the water (note 692; the director, 14 Oct: the drowned town's houses "on these like standing water
        /// platforms ... connected to ladders, ... roads, ... seawalls ... not just these isolated things on their own"; the fen
        /// town "sitting on this like stilts above the water"): planks on piles along its points at <paramref name="level"/>,
        /// and a stage built out either side of it (<paramref name="sides"/> as <see cref="LotsAlong"/>'s) bearing a row of
        /// houses fronting it. Where it ends in open water (<paramref name="jetty"/>: 1 its first end, 2 its last), a jetty
        /// on and a boat tied at its foot, how they come and go when the tide's in the streets.
        /// </summary>
        void Boardwalk(IReadOnlyList<(double U, double D)> pts, double level, int jetty = 0, int sides = 3, double width = BoardwalkWidth,
            IReadOnlyList<(double From, double To, int Sides)>? stages = null)
        {
            double reach = width / 2 + _f.Setback + _f.LotDepth + StageLip;
            Way(pts, width, deck: true, level: level);
            // Its stages: all along it, or where it's told (from, to along it, which sides), open water between.
            foreach (var (from, to, these) in stages ?? [(0, double.MaxValue, sides)])
            {
                var piece = Slice(pts, from, to);
                if (piece.Count < 2)
                    continue;
                foreach (int sigma in new[] { 1, -1 })
                    if ((these & (sigma > 0 ? 1 : 2)) != 0)
                    {
                        var near = Offset(piece, sigma * width / 2);
                        var far = Offset(piece, sigma * reach);
                        District(DistrictKind.Stage, "stage", [.. near, .. Enumerable.Reverse(far)], level, named: false);
                    }
                LotsAlong(piece, width, level, sides: these);
            }
            foreach (int end in new[] { 1, 2 })
            {
                if ((jetty & end) == 0)
                    continue;
                var (e, f) = end == 1 ? (pts[0], pts[1]) : (pts[^1], pts[^2]);
                double el = Math.Max(1e-9, Math.Sqrt((e.U - f.U) * (e.U - f.U) + (e.D - f.D) * (e.D - f.D)));
                double tu = (e.U - f.U) / el, td = (e.D - f.D) / el;
                Way([e, (e.U + tu * JettyLength, e.D + td * JettyLength)], width * 0.8, deck: true, level: level);
                // The boat alongside the jetty's end, its bow out the way the jetty points.
                Landmark("dory", e.U + tu * (JettyLength - 2) - td * (width / 2 + 1.2), e.D + td * (JettyLength - 2) + tu * (width / 2 + 1.2), tu, td, pad: 0.3);
            }
        }

        /// <summary>
        /// A crown of ground filled with houses (a hill's top, a ward): streets across it along the line, every pitch, rows
        /// either side, only where the ground is its level.
        /// </summary>
        void Fill(double u, double d, double ru, double rd, double level)
        {
            double w = _f.StreetWidth, pitch = w + 2 * (_f.Setback + _f.LotDepth) + BandSlack;
            for (double dd = d - rd + pitch / 2; dd <= d + rd - pitch / 2 + 1e-6; dd += pitch)
            {
                var run = new List<(double U, double D)>();
                void Flush()
                {
                    if (run.Count >= 2)
                    {
                        Way([.. run], w);
                        LotsAlong(run, w, level);
                    }
                    run.Clear();
                }
                foreach (double uu in Samples(u - ru, u + ru, 6))
                    if (InWall(_gate + uu, dd) && Open(_gate + uu, dd) && GroundAt(_gate + uu, dd) is var (_, lv) && Math.Abs(lv - level) < 1e-6 && EdgeDistance(_gate + uu, dd) >= WallPad)
                        run.Add((uu, dd));
                    else
                        Flush();
                Flush();
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
            Sea(Rect(-L - m - 40, m + 40, wr + 3, wr + m + 60), _f.WaterLevel, 5);
            Approach(16);
            // The harbour: from the wharf's edge out to the sea wall, its shore ragged, and its mouth through the sea wall to the
            // open sea (the director, 14 Oct: "the breakwater ... doesn't look like it actually leads out into an ocean").
            double quay = 30, edge = 48, mu0 = -L * 0.46, mu1 = -L * 0.4;
            var harbour = new List<(double, double)> { (-L + 95, edge), (-75, edge), (-55, wr - 18), (-L * 0.4, wr - 10), (-L + 70, wr - 14), (-L + 95, edge + 30) };
            District(DistrictKind.Water, "harbour", harbour, _f.WaterLevel);
            District(DistrictKind.Water, "mouth", Rect(mu0, mu1, wr - 24, wr + 2), _f.WaterLevel, named: false);
            District(DistrictKind.Water, "mouth", Rect(mu0, mu1, wr - 2, wr + 12), _f.WaterLevel, named: false, outside: true, depth: 5);
            _l.Gaps.Add((P(mu0, wr), P(mu1, wr)));
            // The breakwaters (TownPlan.Moles): the long one out from the mouth's far side, round across in front of it, its
            // light at its head; the short one from the near side, the entrance between them.
            Mole("breakwater", [(mu1 + 3, wr), (mu1 + 12, wr + 36), (mu1 - 6, wr + 70), (mu0 - 44, wr + 84)], "beacon");
            Mole("arm", [(mu0 - 3, wr), (mu0 - 14, wr + 34)], "");
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
            // Behind the square: the green across from it, and the town wrapped round the headland ring on ring up to the head
            // (the director, 14 Oct: "a lot more of like an organic layout"), its light and its chapel on the crown.
            Green(_t.Walled.First + _t.Walled.Width / 2, _t.Walled.First + _t.Walled.Every - _t.Walled.Width / 2);
            double head = Rings(-1, -L * 0.5, -wl + 5, 34, 1.9, "lanes");
            Landmark("lighthouse", -L * 0.5, -wl - 2, 0, 1);
            Landmark("chapel", -L * 0.5 + 20, -wl + 26, 0, 1);
            Fill(-L * 0.5, -wl + 5, 64, 34, head);
            // The wharf's side of the yard packed too (the director: "pack and wall both halves much harder"): net lofts and
            // stores in rows between the yard's wall and the quay, round the fish stores.
            Rows(Tiers(1, -L + 96, -40, quay + 8, 0, "wharfside"));
        }

        /// <summary>
        /// A dykeland town built out over its marsh (the director's "Fenfolk", 14 Oct: "a dock town that's sitting on this like
        /// stilts above the water ... built onto the water, and ... the houses in the town are all stilted ... walking these
        /// sort of wooden pathways ... they built it on a swampy marsh"; the Tantramar, Grand-Pré's aboiteaux): a palisade
        /// round salt marsh, the railway on its dyke through the middle, a web of boardwalks wandering out from the dyke
        /// between the reeds and joined across, every house on a stage along them; the market mound by the square and the
        /// chapel's, the only ground that never floods, ringed by stages; the watch on its own stage, the boat landing.
        /// </summary>
        public void Dykeland()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right, c = 50;
            Wall((-L, -wl + c), (-L + c, -wl), (-c, -wl), (0, -wl + c), (0, wr - c), (-c, wr), (-L + c, wr), (-L, wr - c));
            // The land it stands in (note 692): the tidal flats all round the palisade, waded at low water, the line in on its dyke.
            double m = _f.Margin;
            Sea(Rect(-L - m - 40, m + 40, -wl - m - 60, wr + m + 60), -0.6, 0.6);
            Approach(5);
            // The marsh everywhere inside, then the dyke the line runs on and the two mounds out of it.
            District(DistrictKind.Water, "marsh", _l.Outline.Select(x => (x.S - _gate, x.D)), _f.WaterLevel);
            District(DistrictKind.Quarter, "dyke", Rect(-L, 0, -16, 16), named: false);
            District(DistrictKind.Quarter, "market", Blob(-62, -50, 56, 30, 0.06, 16, "market"));
            Green(36, 58);
            District(DistrictKind.Quarter, "upper", Blob(-L * 0.72, -wl * 0.6, 30, 22, 0.1, 18, "upper"));
            Landmark("chapel", -L * 0.72, -wl * 0.6 - 4, 0, 1);
            District(DistrictKind.Quarter, "hay", Rect(-L + 26, -L + 114, 16, 44), named: false);
            Works(-L + 30, -L + 110, 18, 40, 1, "dykeland");
            // The watch on its own stage out in the reeds, a walk out to it.
            District(DistrictKind.Stage, "tower", Blob(-L * 0.18, wr * 0.72, 9, 9, 0.05, 10, "tower"), StageLevel, named: false);
            Landmark("watchtower", -L * 0.18, wr * 0.72, 0, -1);
            // The web: spines wandering out from the dyke (or the mounds' edges), joined across where they come near.
            var brng = _rngFor("form.walks");
            var spines = new List<(int Side, List<(double U, double D)> Pts)>();
            for (double u = -L + 52; u < -20; u += brng.Range(42, 58))
                foreach (int side in new[] { -1, 1 })
                {
                    double from = side < 0 && u > -126 ? 84 : 16, reach = (side < 0 ? wl : wr) - 22;
                    if (side > 0 && u > -L + 18 && u < -L + 122)
                        from = 46;
                    var pts = new List<(double U, double D)> { (u, side * from) };
                    double wander = 0;
                    for (int k = 1; k <= 4; k++)
                    {
                        wander += brng.Range(-9, 9);
                        pts.Add((u + wander, side * (from + (reach - from) * k / 4)));
                    }
                    spines.Add((side, pts));
                }
            foreach (var (side, pts) in spines)
                Boardwalk(pts, StageLevel, jetty: 2, stages: Clusters(Length(pts), ref brng, FenCluster, FenGap));
            // Across: between each spine and the next on its side, a walk at a couple of places out along them.
            foreach (int side in new[] { -1, 1 })
            {
                var mine = spines.Where(x => x.Side == side).OrderBy(x => x.Pts[0].U).ToList();
                for (int i = 1; i < mine.Count; i++)
                    foreach (double at in new[] { brng.Range(0.3, 0.45), brng.Range(0.65, 0.85) })
                    {
                        double d = side * (Math.Abs(mine[i].Pts[0].D) + (Math.Abs(mine[i].Pts[^1].D) - Math.Abs(mine[i].Pts[0].D)) * at);
                        if (Math.Abs(d) < Math.Abs(mine[i - 1].Pts[0].D) + 6)
                            continue;
                        double ua = UAt(mine[i - 1].Pts, d) + BoardwalkWidth, ub = UAt(mine[i].Pts, d) - BoardwalkWidth;
                        if (ub - ua < 26)
                            continue;
                        double mid = (ua + ub) / 2 + brng.Range(-6, 6), dk = d + side * brng.Range(-5, 5);
                        var pts = new List<(double U, double D)> { (ua, d), (mid, dk), (ub, d) };
                        Boardwalk(pts, StageLevel, stages: Clusters(Length(pts), ref brng, FenCluster, FenGap));
                    }
            }
            // Round the mounds: a boardwalk round each at its foot, a stage of houses outside it fronting the mound, open where
            // it would cross the dyke, the square or its green.
            foreach (var (u, d, ru, rd) in new[] { (-62.0, -50.0, 62.0, 35.0), (-L * 0.72, -wl * 0.6, 35.0, 27.0) })
            {
                var run = new List<(double U, double D)>();
                void Flush()
                {
                    if (run.Count >= 3)
                        Boardwalk([.. run], StageLevel, sides: 2);
                    run.Clear();
                }
                foreach (var (pu, pd) in Circle(u, d, ru, rd, 40).Append((u + ru, d)))
                    if (Math.Abs(pd) > 24 && Open(_gate + pu, pd) && InWall(_gate + pu, pd) && EdgeDistance(_gate + pu, pd) >= WallPad)
                        run.Add((pu, pd));
                    else
                        Flush();
                Flush();
            }
            // The boat landing on the far side, its punts.
            District(DistrictKind.Deck, "landing", Rect(-L * 0.62, -L * 0.62 + 50, wr - 40, wr - 26), StageLevel);
            Landmark("dory", -L * 0.62 + 20, wr - 22, 1, 0, pad: 0.3);
        }

        /// <summary>A line's length (m).</summary>
        static double Length(IReadOnlyList<(double U, double D)> pts) =>
            Enumerable.Range(1, pts.Count - 1).Sum(i => Math.Sqrt((pts[i].U - pts[i - 1].U) * (pts[i].U - pts[i - 1].U) + (pts[i].D - pts[i - 1].D) * (pts[i].D - pts[i - 1].D)));

        /// <summary>Where a line running out from the line (its d growing in size) is along it at <paramref name="d"/> out.</summary>
        static double UAt(IReadOnlyList<(double U, double D)> pts, double d)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                var (a, b) = (pts[i - 1], pts[i]);
                if ((d - a.D) * (d - b.D) <= 0 && Math.Abs(b.D - a.D) > 1e-9)
                    return a.U + (b.U - a.U) * (d - a.D) / (b.D - a.D);
            }
            return Math.Abs(d - pts[0].D) < Math.Abs(d - pts[^1].D) ? pts[0].U : pts[^1].U;
        }

        /// <summary>A walk's stages in clusters along it (from, to, which sides), the water between them (note 692).</summary>
        static List<(double, double, int)> Clusters(double len, ref Pcg32 rng, double[] cluster, double[] gap)
        {
            var stages = new List<(double, double, int)>();
            for (double x = rng.Range(4, 10); x < len - 12;)
            {
                double run = rng.Range(cluster[0], cluster[1]);
                stages.Add((x, Math.Min(len - 4, x + run), rng.Chance(0.55) ? 3 : rng.Chance(0.5) ? 1 : 2));
                x += run + rng.Range(gap[0], gap[1]);
            }
            return stages;
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
            Rows(Tiers(1, -L + 20, -20, wr + 20, 0, "workers"));
            // The rows round the pit on the square's side, up to the wall (they keep to the rim's level: the benches are the pit's).
            Rows(Tiers(-1, -L + 20, -10, wl + 20, 0, "rows"));
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
            var causeways = new List<(double U, double Far)>();
            for (int i = 0; i < 3; i++)
            {
                double u = -L * (0.25 + 0.25 * i) + crng.Range(-15, 15), far = wr * crng.Range(0.6, 0.8);
                causeways.Add((u, far));
                District(DistrictKind.Quarter, "causeway", Rect(u - 4, u + 4, 20, far), named: false);
                Way([(u, 20), (u, far)], 5);
                District(DistrictKind.Quarter, i == 1 ? "wharves" : "wharf", Blob(u, far + 10, 24, 14, 0.1, 12, "wharf" + i), named: i == 1);
                District(DistrictKind.Deck, "staging", Rect(u + 18, u + 24, far, far + 26), 0.8, named: false);
                Lots(u - 20, far + 12, u + 20, far + 12, 3, 3, 0, false);
            }
            // The boroughs built up out of the flood (the director, 14 Oct): rows of boardwalks across it from causeway to
            // causeway, a stage of houses either side of each; those out past the last causeway end in a jetty and a boat.
            Boroughs(water, causeways);
            // Above the line: the square's green, the blocks climbing tier on tier toward the hills (note 692), then the
            // chapel hill and the high ward over the top tier, a stair up to each from it, houses round their crowns.
            Green(34, 56);
            Works(-L * 0.3, -30, 12, 24, 1, "tidewater");
            var climb = Tiers(-1, -L + 20, -10, wl * 0.72, _f.Rise, "climb");
            double top = climb.Level(climb.Offs.Count - 1);
            // The hills over the top tier whatever it reached, the high ward over the chapel's.
            double chapel = Math.Max(_f.TerraceLevel, top + _f.Rise * 1.5), high = Math.Max(_f.TerraceLevel * 1.6, chapel + _f.Rise * 2);
            District(DistrictKind.Terrace, "chapel", Blob(-L * 0.72, -wl * 0.74, 62, 34, 0.08, 18, "chapel"), chapel);
            District(DistrictKind.Terrace, "high", Blob(-L * 0.35, -wl * 0.8, 58, 30, 0.08, 18, "high"), high);
            // Between the stair lanes that climb to the hills' feet, not on one.
            Landmark("chapel", -L * 0.72 - 30, -wl * 0.74 - 6, 0, 1);
            Landmark("watchtower", -L * 0.35, -wl * 0.8 - 12, 0, 1);
            Climb([(-L * 0.72 + 64, -wl * 0.56), (-L * 0.72 + 34, -wl * 0.64), (-L * 0.72 + 12, -wl * 0.68)], chapel);
            Climb([(-L * 0.35 + 64, -wl * 0.56), (-L * 0.35 + 30, -wl * 0.7), (-L * 0.35 + 10, -wl * 0.74)], high);
            Landmark("crane", -60, 22, 0, 1);
            Rows(climb);
            Fill(-L * 0.72, -wl * 0.74, 62, 34, chapel);
            Fill(-L * 0.35, -wl * 0.8, 58, 30, high);
        }

        /// <summary>
        /// The drowned ward's boroughs (note 692): between each pair of causeways (and past the outermost, to the flood's
        /// edge), a boardwalk every <see cref="BoroughEvery"/> out from the shore, kinked where the old street did, joined to
        /// the causeway at each end it reaches (a step down to its stone), a jetty and a boat where it ends in the water.
        /// </summary>
        void Boroughs(List<(double U, double D)> water, List<(double U, double Far)> causeways)
        {
            var rng = _rngFor("form.boroughs");
            var area = new TownDistrict(DistrictKind.Water, "", "", [.. water.Select(x => P(x.U, x.D))]);
            bool Wet(double u, double d) => area.Holds(_gate + u, d) && InWall(_gate + u, d) && EdgeDistance(_gate + u, d) >= WallPad;
            var stops = causeways.OrderBy(c => c.U).ToList();
            double u0 = water.Min(x => x.U), u1 = water.Max(x => x.U);
            var spans = new List<(double A, double? FarA, double B, double? FarB)>();
            spans.Add((u0, null, stops[0].U, stops[0].Far));
            for (int i = 1; i < stops.Count; i++)
                spans.Add((stops[i - 1].U, stops[i - 1].Far, stops[i].U, stops[i].Far));
            spans.Add((stops[^1].U, stops[^1].Far, u1, null));
            for (double d = water.Min(x => x.D) + 14; ; d += BoroughEvery)
            {
                foreach (var (a, farA, b, farB) in spans)
                {
                    // From the causeway's side where it reaches this far out, else from where the water starts.
                    bool fromA = farA is { } fa && fa > d + 3, toB = farB is { } fb && fb > d + 3;
                    double a0 = fromA ? a + 4 : a, b0 = toB ? b - 4 : b;
                    while (a0 < b0 && !fromA && !Wet(a0 + 4, d))
                        a0 += 2;
                    while (b0 > a0 && !toB && !Wet(b0 - 4, d))
                        b0 -= 2;
                    if (b0 - a0 < 28)
                        continue;
                    double mid = (a0 + b0) / 2 + rng.Range(-8, 8), dk = d + rng.Range(-3, 3);
                    var pts = new List<(double U, double D)> { (a0, d), (mid, dk), (b0, d) };
                    if (!Wet(mid, dk))
                        continue;
                    // Its houses in clusters along it, a stage on one side or both, the flood between them.
                    Boardwalk(pts, StageLevel, jetty: (fromA ? 0 : 1) | (toB ? 0 : 2), stages: Clusters(Length(pts), ref rng, BoroughCluster, BoroughGap));
                }
                if (d > water.Max(x => x.D))
                    break;
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
            Land("ridge", Rect(-L - m - 60, m + 60, -wl - m - 60, -wl - 60), 30, 90);
            Land("ridge", Rect(-L - m - 60, m + 60, wr + 60, wr + m + 60), 26, 90);
            // The great glasshouses (the director, 14 Oct: "the main greenhouse is a concept ... that will be interesting for
            // players to explore"): three long houses of glass across the line on the platform's side, end to end, walks
            // round them; the processing works by the way out.
            var (gs, gd, _) = TownFixtures.Size("greathouse");
            double gu0 = -L * 0.78, gdd = 16 + gd + 2;
            for (int i = 0; i < 3; i++)
            {
                double u = gu0 + gs + i * (2 * gs + 8);
                Landmark("greathouse", u, gdd, 0, -1, pad: 1.5);
                Way([(u - gs - 4, 16), (u - gs - 4, gdd + gd + 4)], 3);
            }
            Way([(gu0 - 4, gdd + gd + 4), (gu0 + 3 * (2 * gs + 8) - 4, gdd + gd + 4)], 3);
            District(DistrictKind.Quarter, "glass", Rect(gu0 - 6, gu0 + 3 * (2 * gs + 8), 14, gdd + gd + 6));
            Works(-L * 0.3, -40, 14, wr * 0.42, 1, "glasshouse");
            // Beds between the glass and the works, walks between them.
            double bu0 = gu0 + 3 * (2 * gs + 8) + 4, bu1 = -L * 0.3 - 6;
            for (double u = bu0; u + 20 < bu1; u += 26)
            {
                for (double d = 18; d + 22 < wr * 0.5; d += 26)
                    District(DistrictKind.Plots, "beds", Rect(u + 2, u + 24, d, d + 22), named: false);
                Way([(u, 16), (u, wr * 0.5)], 2.5);
            }
            // The reservoirs: round tanks past the glasshouses, walks round them.
            var rrng = _rngFor("form.reservoirs");
            for (int i = 0; i < 3; i++)
            {
                double u = gu0 + 30 + i * 70, d = gdd + gd + 30 + (i % 2) * 8, r = rrng.Range(14, 18);
                District(DistrictKind.Water, i == 1 ? "reservoir" : "tank", Circle(u, d, r, r, 20), _f.WaterLevel, named: i == 1);
                Way(Circle(u, d, r + 3, r + 3, 20).Append((u + r + 3, d)).ToList(), 2.5);
            }
            // The gardeners' rows by the square (the director: "mostly integrated growing plots"): every other street a row
            // of houses facing its beds across it, the beds' far side the next row's backs.
            Green(34, 56);
            var rows = Tiers(-1, -L * 0.62, -10, wl + 20, 0, "lanes");
            for (int k = 0; k < rows.Offs.Count; k++)
            {
                if (k % 2 == 0)
                    continue;
                for (double u = rows.U0; u < rows.U1; u += 12)
                {
                    double near = rows.Street(k, u + 6, _f.Wander) + _f.StreetWidth / 2 + 0.6, far = rows.Back(k, u + 6, _f.Wander) - 0.3;
                    if (far - near > 3 && Open(_gate + u, -near) && Open(_gate + u + 12, -far))
                        District(DistrictKind.Plots, "beds", Rect(u, Math.Min(u + 12, rows.U1), -near, -far), named: false);
                }
            }
            Rows(rows);
            // The open beds toward the way in, in plots with walks between, and the seed house among them.
            double pu0 = -L + 34, pu1 = -L * 0.62 - 8;
            for (double u = pu0; u < pu1 - 20; u += 32)
                for (double d = -22; d > -wl + 36; d -= 28)
                    District(DistrictKind.Plots, "beds", Rect(u + 2, u + 28, d - 2, d - 24), named: u == pu0 && d == -22);
            for (double u = pu0; u < pu1; u += 32)
                Way([(u, -16), (u, -wl + 20)], 2.5);
            Landmark("seedhouse", (pu0 + pu1) / 2, -wl + 26, 0, 1);
        }

        /// <summary>
        /// A dock basin on the platform's side (note 692): water from <paramref name="d0"/> out to the wall between u0 and u1,
        /// its mouth through the sea wall to the sea between mu0 and mu1 (the wall's run along <paramref name="wallD"/>), a
        /// quay along its near edge, piers out over it, a barge moored.
        /// </summary>
        void Docks(double u0, double u1, double d0, double wallD, double mu0, double mu1, string stream)
        {
            District(DistrictKind.Water, "docks", Rect(u0, u1, d0, wallD - 6), _f.WaterLevel);
            District(DistrictKind.Water, "mouth", Rect(mu0, mu1, wallD - 8, wallD + 2), _f.WaterLevel, named: false);
            District(DistrictKind.Water, "mouth", Rect(mu0, mu1, wallD - 2, wallD + 12), _f.WaterLevel, named: false, outside: true, depth: 5);
            _l.Gaps.Add((P(mu0, wallD), P(mu1, wallD)));
            District(DistrictKind.Quarter, "quay", Rect(u0, u1, d0 - 10, d0), named: false);
            Way([(u0 + 4, d0 - 5), (u1 - 4, d0 - 5)], 6);
            var rng = _rngFor("form.docks." + stream);
            for (double u = u0 + 18; u < u1 - 16; u += rng.Range(34, 50))
            {
                double len = Math.Min(rng.Range(30, 50), wallD - 14 - d0);
                District(DistrictKind.Deck, "pier", Rect(u - 3, u + 3, d0 - 1, d0 + len), 0.9, named: false);
                Way([(u, d0), (u, d0 + len)], 5, deck: true, level: 0.9);
            }
            Landmark("barge", (u0 + u1) / 2 + 8, d0 + (wallD - d0) * 0.55, 1, 0, pad: 0.5);
            Landmark("crane", u0 + 10, d0 - 4, 0, 1);
        }

        /// <summary>
        /// A foundry town on its sea crag (the director's "Furnace Republic", 14 Oct): the foundry and its stacks on the crown,
        /// the town in rings round it on the square's side, the slag works down in a pit by the way in, the ore yard on the
        /// platform's side, the docks in their basin under the sea wall, the sea all round under the rock.
        /// </summary>
        public void Furnace()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            Wall((-L, -wl * 0.7), (-L + 50, -wl), (-L * 0.55, -wl - 15), (-60, -wl), (0, -wl * 0.6), (0, wr * 0.45), (-45, wr), (-L + 45, wr), (-L, wr * 0.7));
            double m = _f.Margin;
            // The crag in its sea: the sea all round, the rock falling to it from under the wall, the line in on its causeway.
            Sea(Rect(-L - m - 40, m + 40, -wl - m - 60, wr + m + 60), _f.WaterLevel, 6);
            Land("crag", Blob(-L * 0.5, (wr - wl) / 2, L * 0.62, (wl + wr) * 0.62, 0.08, 28, "crag"), 0, 26);
            Approach(12);
            // The slag works down in their pit by the way in: the rings keep off it.
            double su = -L + 105, sd = -64;
            bool InPit(double u, double d) => (u - su) * (u - su) / (50.0 * 50) + (d - sd) * (d - sd) / (38.0 * 38) < 1.25;
            Green(34, 54);
            double crown = Rings(-1, -L * 0.42, -wl + 14, 30, 1.9, "quarters", InPit);
            District(DistrictKind.Terrace, "slag", Blob(su, sd, 46, 34, 0.08, 20, "slag"), -_f.TerraceLevel);
            Landmark("crane", su + 30, sd - 8, 1, 0);
            Landmark("lift", su - 20, sd + 22, 0, -1);
            // The foundry on the crown: its sheds and its stacks.
            Landmark("foundry", -L * 0.42 - 14, -wl + 22, 0, 1);
            Landmark("foundry", -L * 0.42 + 22, -wl + 26, 0, 1);
            for (int i = 0; i < 5; i++)
                Landmark("stack", -L * 0.42 - 34 + i * 15, -wl + 6 + (i % 2) * 8, 0, 1);
            _ = crown;
            // The platform's side: the ore yard (the works), the docks under the sea wall, the workers' rows by the way in.
            Works(-L * 0.5, -40, 14, 58, 1, "foundry");
            Docks(-L + 60, -L * 0.52 - 6, 66, wr, -L * 0.62, -L * 0.62 + 30, "furnace");
            Rows(Tiers(1, -L + 24, -L * 0.52 - 8, 56, 0, "workers"));
        }

        /// <summary>
        /// A clifftop town (the director's "Highline Settlement", 14 Oct): on the platform's side the town steps down tier by
        /// tier to the wall on the cliff's edge, the sea far below; on the square's side it climbs in rings to the citadel;
        /// the lookout out on its sea stack, a stone bridge to it from the cliff.
        /// </summary>
        public void Highline()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            Wall((-L, -wl * 0.8), (-L + 40, -wl), (-60, -wl), (0, -wl * 0.7), (0, wr * 0.6), (-30, wr), (-L + 30, wr), (-L, wr * 0.6));
            double m = _f.Margin;
            // Past the wall on the platform's side the cliff, straight down to the sea; behind the town the wooded hills.
            Land("cliff", Rect(-L - m - 40, m + 40, wr + 3, wr + m + 60), -_f.TerraceLevel * 2, 0, roll: 0.3);
            Sea(Rect(-L - m - 40, m + 40, wr + 26, wr + m + 60), -_f.TerraceLevel * 2 - 2, 4);
            // Behind the citadel the clifftop's land falls away to the woods below.
            Land("woods", Rect(-L - m - 40, m + 40, -wl - m - 60, -wl - 30), -_f.TerraceLevel, 30);
            Approach(10);
            Green(34, 54);
            // Down to the cliff, tier under tier.
            // (Short of the wall on the cliff: a walk along its top, nothing built over the drop.)
            // The processing sheds by the way out, the catch and the crops up from the docks, the lower ward's tiers under them.
            Works(-L * 0.44, -30, 14, 40, 1, "harbour");
            Rows(Tiers(1, -L + 22, -14, wr * 0.72, -_f.Rise, "lower"));
            // Up to the citadel.
            Rings(-1, -L * 0.4, -wl + 16, 36, 1.8, "upper");
            Landmark("chapel", -L * 0.4 - 8, -wl + 14, 0, 1);
            Landmark("keep", -L * 0.4 + 22, -wl + 24, 0, 1);
            // The lookout: a stone bridge out from the cliff's wall to its stack, its tower on it.
            Mole("lookout", [(-L * 0.78, wr), (-L * 0.8, wr + 26), (-L * 0.86, wr + 44)], "watchtower");
            Landmark("crane", -L * 0.3, wr - 14, 0, 1);
        }

        /// <summary>
        /// A logging town in its clearing (the director's "Timberholds", 14 Oct): a round timber palisade, its watchtowers on
        /// legs inside and out over the stumps of the kill zone, the log yards and the sawmill on the platform's side, the
        /// quarters round the market on the square's.
        /// </summary>
        public void Timber()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            // Round, but square across the line at its two gates.
            var round = new List<(double U, double D)> { (-L, -14) };
            for (int i = 1; i < 16; i++)
            {
                double a = Math.PI + i * Math.PI / 16;
                round.Add((-L / 2 + DMath.Cos(a) * L / 2 * 0.98, DMath.Sin(a) * wl));
            }
            round.Add((0, -14));
            round.Add((0, 14));
            for (int i = 1; i < 16; i++)
            {
                double a = i * Math.PI / 16;
                round.Add((-L / 2 + DMath.Cos(a) * L / 2 * 0.98, DMath.Sin(a) * wr));
            }
            round.Add((-L, 14));
            Wall([.. round]);
            double m = _f.Margin;
            // The holdfast on its knoll: the clearing falls away from under the palisade, the line in on its bank.
            Land("clearing", Rect(-L - m - 40, m + 40, -wl - m - 60, wr + m + 60), -_f.TerraceLevel, 40);
            Approach(14);
            Green(34, 52);
            // The quarters in rings round the market on the knoll's top, a terrace up.
            double market = Rings(-1, -L * 0.5, -wl * 0.42, 24, 1.7, "quarters");
            Fill(-L * 0.5, -wl * 0.42, 24, 18, market);
            // The log yards and the sawmill, the platform's side: stacks of logs in rows, the mill and its stacks by the way out.
            for (double u = -L * 0.82; u < -L * 0.42; u += 34)
                for (double d = 24; d < wr * 0.7; d += 22)
                {
                    District(DistrictKind.Yard, "logs", Rect(u, u + 28, d, d + 16), named: u < -L * 0.81 && d < 25);
                    Landmark("logs", u + 8, d + 8, 1, 0, pad: 0.3);
                    Landmark("logs", u + 20, d + 8, 1, 0, pad: 0.3);
                }
            Landmark("sawmill", -L * 0.3, 40, 0, -1);
            Landmark("sawmill", -L * 0.3 + 46, 52, 0, -1);
            Landmark("stack", -L * 0.3 + 18, 70, 0, -1);
            Landmark("stack", -L * 0.3 - 22, 66, 0, -1);
            Rows(Tiers(1, -L * 0.2, -14, wr + 20, 0, "millrows"));
            // The yard hands' rows past the log yards, toward the palisade.
            Rows(Tiers(1, -L * 0.86, -L * 0.42, wr + 20, 0, "skidrows"));
            // The watch: towers on legs in the town, and out in the kill zone past the palisade.
            Landmark("timbertower", -L * 0.5, -wl * 0.55, 0, 1);
            Landmark("timbertower", -L * 0.5, wr * 0.8, 0, -1);
            foreach (var (u, d) in new[] { (-L * 0.25, -wl - 30), (-L * 0.75, -wl - 26), (-L * 0.3, wr + 30), (-L * 0.7, wr + 28) })
                Landmark("timbertower", u, d, 0, Math.Sign(-d));
        }

        /// <summary>
        /// A salt town on its marsh (the director's "Saltwardens", 14 Oct): an eight-sided wall standing in the marsh, the salt
        /// pans in rows on the platform's side between their dykes, the windpumps that fill them, the cisterns and the
        /// processing sheds; on the square's side the lagoon the lower ward stands over on its stilts, the living quarters by
        /// the market.
        /// </summary>
        public void Salt()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right, c = Math.Min(L, wl + wr) * 0.2;
            Wall((-L, -wl + c), (-L + c, -wl), (-c, -wl), (0, -wl + c), (0, wr - c), (-c, wr), (-L + c, wr), (-L, wr - c));
            double m = _f.Margin;
            Sea(Rect(-L - m - 40, m + 40, -wl - m - 60, wr + m + 60), -0.6, 0.6);
            Approach(5);
            // The pans: shallow rectangles of brine in rows, a dyke round each, a walk between the rows.
            double pu0 = -L * 0.82, pu1 = -L * 0.38;
            for (double u = pu0; u + 26 < pu1; u += 30)
                for (double d = 22; d + 20 < wr * 0.8; d += 24)
                    District(DistrictKind.Water, "pans", Rect(u, u + 26, d, d + 20), -0.25, named: u == pu0 && d == 22, depth: 0.3);
            for (double d = 20; d < wr * 0.8; d += 24)
                Way([(pu0 - 2, d), (pu1, d)], 2.5);
            foreach (var (u, d) in new[] { (pu0 + 13, 20.0), (pu0 + 103, 20.0), (pu0 + 43, wr * 0.8 - 2), (pu1 - 17, wr * 0.8 - 2) })
                Landmark("windpump", u, d, 0, 1, pad: 0.3);
            // The cisterns: round, walked round, past the pans; the sheds by the way out.
            for (int i = 0; i < 3; i++)
            {
                double u = -L * 0.28 + i * 34 - 34, d = wr * 0.55 + (i % 2) * 18, r = 13;
                District(DistrictKind.Water, i == 1 ? "cisterns" : "cistern", Circle(u, d, r, r, 20), _f.WaterLevel, named: i == 1);
                Way(Circle(u, d, r + 3, r + 3, 20).Append((u + r + 3, d)).ToList(), 2.5);
            }
            Landmark("shed", -L * 0.2, 30, 0, -1);
            Landmark("shed", -L * 0.2 + 30, 30, 0, -1);
            Landmark("shed", -60, 30, 0, -1);
            // The square's side: the living quarters by the market, the lower ward on its stilts over the lagoon past them.
            Green(34, 54);
            Rows(Tiers(-1, -L * 0.42, -12, 72, 0, "living"));
            var lagoon = Blob(-L * 0.68, -wl * 0.58, L * 0.26, wl * 0.36, 0.06, 20, "lagoon").Select(x => (x.U, Math.Min(x.D, -24.0))).ToList();
            District(DistrictKind.Water, "lagoon", lagoon, _f.WaterLevel);
            var srng = _rngFor("form.salt.walks");
            for (double u = -L * 0.9; u < -L * 0.46; u += srng.Range(40, 56))
            {
                var pts = new List<(double U, double D)> { (u, -16), (u + srng.Range(-8, 8), -wl * 0.45), (u + srng.Range(-10, 10), -wl * 0.85) };
                Boardwalk(pts, StageLevel, jetty: 2, stages: Clusters(Length(pts), ref srng, FenCluster, FenGap));
            }
            Landmark("watchtower", -L * 0.5, -wl + 20, 0, 1);
        }

        /// <summary>
        /// A crag fortress (the director's "Craghold", 14 Oct): the town climbing its crag in walled terraces ring on ring to
        /// the keep and the citadel, the stone lift up its face; on the platform's side the lower ward over the docks in their
        /// inlet under the sea wall.
        /// </summary>
        public void Crag()
        {
            double L = _f.Length, wl = _f.Left, wr = _f.Right;
            Wall((-L, -wl * 0.75), (-L + 50, -wl), (-L * 0.5, -wl - 20), (-50, -wl), (0, -wl * 0.65), (0, wr * 0.5), (-40, wr), (-L + 40, wr), (-L, wr * 0.7));
            double m = _f.Margin;
            // The crag in the sea: the wards climb it inside the wall, the rock falls from under the wall to the water all round.
            Sea(Rect(-L - m - 40, m + 40, -wl - m - 60, wr + m + 60), _f.WaterLevel, 6);
            Land("crag", Blob(-L * 0.5, (wr - wl) / 2, L * 0.6, (wl + wr) * 0.6, 0.1, 28, "crag"), 0, 16);
            Approach(14);
            Green(34, 54);
            double crown = Rings(-1, -L * 0.52, -wl + 12, 34, 1.6, "terraces");
            Landmark("keep", -L * 0.52, -wl + 6, 0, 1);
            Landmark("chapel", -L * 0.52 + 26, -wl + 20, 0, 1);
            Fill(-L * 0.52, -wl + 12, 34, 22, crown);
            Landmark("lift", -L * 0.22, -46, 0, 1);
            // The lower ward over the docks.
            Docks(-L + 70, -L * 0.42, 70, wr, -L * 0.6, -L * 0.6 + 30, "crag");
            Rows(Tiers(1, -L + 24, -20, 60, 0, "lower"));
        }

        /// <summary>
        /// The yard's walls (note 692; the director, 14 Oct: "pack and wall both halves much harder"): along each side of the
        /// line just in front of the first row's backs, wherever there's a row behind it, open wherever a way goes through
        /// (a stair lane, the square's side, a works' gate): each half of the town its own ward over the yard.
        /// </summary>
        void Wards()
        {
            double half = Run.Fortresses.WardHalf;
            foreach (int side in new[] { -1, 1 })
            {
                double d = side * (_f.Near - half - 0.15);
                var behind = _l.Lots.Where(x => Math.Sign(x.D) == side && Math.Abs(x.D) < _f.Near + _f.LotDepth + 1).ToList();
                var through = _l.Ways.Where(w => w.Points.Any(p => Math.Sign(p.D) == side || Math.Abs(p.D) < 1)).ToList();
                bool Stands(double s) =>
                    InWall(s, d) && EdgeDistance(s, d) >= 3
                    && behind.Any(x => Math.Abs(x.S - s) <= x.Frontage / 2 + 0.6)
                    && !through.Any(w => Enumerable.Range(1, w.Points.Count - 1).Any(i => SegDist(s, d, w.Points[i - 1], w.Points[i]) <= w.Width / 2 + 1.2));
                double? open = null;
                double s0 = _l.Outline.Min(x => x.S), s1 = _l.Outline.Max(x => x.S);
                for (double s = Math.Ceiling(s0); s <= s1 + 1; s += 1)
                {
                    bool here = s <= s1 && Stands(s);
                    if (here && open is null)
                        open = s;
                    else if (!here && open is { } a)
                    {
                        if (s - 1 - a >= 5)
                            _l.Wards.Add((a - 0.5, d, s - 0.5, d));
                        open = null;
                    }
                }
            }
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
        /// built.
        /// </summary>
        void Walkable()
        {
            var start = (S: _gate - 60, D: 6.0);
            for (int round = 0; round <= StairRounds; round++)
            {
                var ground = TownGround.Build(_l.Bounds(_gate), _l.Districts, _l.Ways, 0);
                var walk = ground.Walk(start.S, start.D);
                var cut = _l.Lots.Where(x => Front(x) is var (fs, fd) && !walk(fs, fd)).ToList();
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
            var kept = _l.Lots.Where(x => !OnAWay(x) && !OnALandmark(x) && GroundAt(x.S, x.D) is var (kind, level)
                && kind is not (DistrictKind.Water or DistrictKind.Deck or DistrictKind.Plots or DistrictKind.Yard) && Math.Abs(level - x.Level) < 1e-6).ToList();
            _l.OffGround = _l.Lots.Count - _l.OnWays - _l.OnLandmarks - kept.Count;
            _l.Lots.Clear();
            _l.Lots.AddRange(kept);
            Walkable();
            // Nearest the square first: the town grows out from its heart (note 335). Lots that can hold an open house
            // (square to the line, on dry ground) are kept first among equals.
            double hs = (_sq.S0 + _sq.S1) / 2, hd = _sq.Side * 15;
            var sorted = _l.Lots.OrderBy(x => (x.S - hs) * (x.S - hs) + (x.D - hd) * (x.D - hd)).ToList();
            _l.Lots.Clear();
            _l.Lots.AddRange(sorted);
            if (_f.Packed)
                Wards();
            return _l;
        }
    }
}
