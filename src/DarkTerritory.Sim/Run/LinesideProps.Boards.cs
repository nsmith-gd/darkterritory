using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// Where a lettered board stands beside the line (note 560), as the sim keeps its sight lines clear and the art draws it:
/// the board of <see cref="Type"/> reading <see cref="Text"/>, on <see cref="Edge"/> at <see cref="Along"/> (the plan put
/// it at <see cref="S"/>; it's moved back toward the train where it couldn't be seen there), on <see cref="Side"/>; its
/// <see cref="Face"/>, the middle of the face's bottom edge at <see cref="Bottom"/> (world), its face across
/// <see cref="Right"/> (the piece's +X, as the cab sees it) and turned <see cref="Toward"/> the oncoming train (its +Z);
/// its <see cref="Posts"/> (each a foot on the land, world, and how tall). <see cref="SeenM"/> of the
/// <see cref="ApproachM"/> it's read over sees it, in the lamp, past the land.
/// </summary>
public sealed record BoardSite(string Type, string Text, string Edge, double S, double Along, int Side, BoardFace Face, Double3 Bottom,
    Double3 Right, Double3 Toward, IReadOnlyList<(Double3 Foot, double Height)> Posts, double SeenM, double ApproachM)
{
    /// <summary>Where along its line the driver's eye sees it from (every sightStepM): the sight lines kept clear.</summary>
    public IReadOnlyList<double> SeenFrom { get; init; } = [];
    /// <summary>A tunnel's plate, on its portal: no posts, nothing cleared.</summary>
    public bool Plate => Type == "tunnelPlate";
    /// <summary>The middle of the face (world).</summary>
    public Double3 Centre => Bottom + Double3.Up * (Face.Height / 2);
    /// <summary>
    /// How many of the places it's read from (billboard.clearFromM in to readToM) have another board's face in front of it
    /// (the director, 13 Oct, `pt13-signs`: "Some signs are overlapping"): none, wherever it could stand so.
    /// </summary>
    public int BoardsInWay { get; init; }

    /// <summary>The points of the face a sight line must reach: its middle and its two lower corners (a bank hides those first).</summary>
    public IEnumerable<Double3> SightTargets()
    {
        yield return Centre;
        var up = Double3.Up * (0.2 * Face.Height);
        yield return Bottom + Right * (0.4 * Face.Width) + up;
        yield return Bottom - Right * (0.4 * Face.Width) + up;
    }

    /// <summary>
    /// Points over the whole face, five across and three up: another board's face covering any of them would stand on top
    /// of this one's words as the driver sees them (`pt13-signs`), though the middle and the lower corners showed past it.
    /// </summary>
    public IEnumerable<Double3> FaceGrid()
    {
        // Over its words (laid out in the middle of the face, its margin round them); the whole face for a stack's.
        int widest = Face.Lines.Count == 0 ? 0 : Face.Lines.Max(l => l.Length);
        double w = widest == 0 ? Face.Width : (widest * BoardLayout.Advance - 1) * Face.Pixel;
        double h = widest == 0 ? Face.Height : (Face.Lines.Count * BoardLayout.LineHeight - 2) * Face.Pixel;
        double up = (Face.Height - h) / 2;
        foreach (double v in GridUp)
            foreach (double u in GridAcross)
                yield return Bottom + Right * (u * w) + Double3.Up * (up + v * h);
    }

    static readonly double[] GridUp = [0, 0.5, 1], GridAcross = [-0.5, -0.25, 0, 0.25, 0.5];

    /// <summary>Where the driver's eye is checked from about <paramref name="a"/> for another board in the way: there and a third of a sight step either side.</summary>
    public static IEnumerable<double> AboutEye(double a, double step)
    {
        yield return a - step / 3;
        yield return a;
        yield return a + step / 3;
    }

    /// <summary>Whether the straight line from <paramref name="a"/> to <paramref name="b"/> passes through this face (its ends excluded).</summary>
    public bool Crosses(Double3 a, Double3 b)
    {
        var d = b - a;
        double den = Double3.Dot(d, Toward);
        if (Math.Abs(den) < 1e-9)
            return false;
        var c = Centre;
        double t = Double3.Dot(c - a, Toward) / den;
        if (t <= 1e-6 || t >= 1 - 1e-6)
            return false;
        var q = a + d * t - c;
        return Math.Abs(Double3.Dot(q, Right)) <= Face.Width / 2 && Math.Abs(q.Y) <= Face.Height / 2;
    }
}

public sealed partial class LinesideProps
{
    readonly object _boardsLock = new();
    Dictionary<PlanSign, BoardSite>? _planBoards;
    Dictionary<(string, long), BoardSite>? _sightBoards;
    SightCones? _cones;
    const double BuildingCell = 32;
    readonly Dictionary<(long, long), List<(double X, double Z, double R, double Top)>> _buildings = [];
    /// <summary>The boards stood so far (as <see cref="Stand"/> stands them): the next keeps out of their way.</summary>
    readonly List<BoardSite> _placed = [];

    /// <summary>The plan's lettered boards as they stand (note 560), by sign: none for a missing one or a dead signal.</summary>
    public IReadOnlyDictionary<PlanSign, BoardSite> PlanBoards
    {
        get
        {
            Stand();
            return _planBoards!;
        }
    }

    /// <summary>
    /// A board of the sim's (sight.json's, <see cref="Lineside.Boards(SightTuning, Route.Route)"/>) as it stands: the
    /// default tuning's site if it's one of those (its sight lines kept clear), stood afresh if the night's tuning moved it.
    /// </summary>
    public BoardSite SightBoard(Sign sign)
    {
        Stand();
        var (type, text) = SightLettering(sign);
        if (_sightBoards!.TryGetValue((type, (long)Math.Round(sign.Board * 10)), out var site) && site.Text == text)
            return site;
        return Site("main", _line, sign.Board, 1, type, text);
    }

    /// <summary>What a sim's board reads (note 560): a posted speed's figure, a tunnel's LOW, the mail crane's MAIL and its side, the terminus's END.</summary>
    public static (string Type, string Text) SightLettering(Sign sign) => sign.Kind switch
    {
        SignKind.SpeedLimit => ("speedBoard", sign.LimitKmh.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        SignKind.LowClearance => ("restricted", "LOW"),
        SignKind.Drop => ("mailDrop", sign.Drop is { Side: < 0 } ? "< MAIL" : "MAIL >"),
        SignKind.Terminus => ("lineClosed", "END"),
        _ => ("restricted", "?"),
    };

    /// <summary>Whether something <paramref name="radius"/> round at a world point stands in a board's sight lines (note 560): the woods leave them clear.</summary>
    public bool InBoardsSight(double x, double z, double radius)
    {
        Stand();
        return _cones!.Holds(x, z, radius + _plan.Rules.Billboard.ClearOfM);
    }

    /// <summary>How much of a lineside thing stands in the way of a sight line: a tree's crown, a boulder's or a piece's reach.</summary>
    public double SightRadius(LinesideProp p) => p.Kind == LinesideKind.Tree
        ? Math.Max(p.Radius, _plan.Rules.Billboard.CrownShare * p.Height)
        : Extent(p);

    /// <summary>Whether a thing beside the line is kept out of the boards' sight (never a telegraph pole: the wires run on).</summary>
    bool Hidden(LinesideProp p)
    {
        if (p.Kind == LinesideKind.Pole)
            return false;
        var w = World(p.Along, p.Lateral);
        return InBoardsSight(w.X, w.Z, SightRadius(p));
    }

    double Ground(double x, double z) => _terrain.Height(x, z);

    /// <summary>Whether a board's post can stand at a world point beside <paramref name="edge"/> (<paramref name="along"/>, <paramref name="offset"/> on it).</summary>
    bool Footing(string edge, double along, double offset, Double3 w)
    {
        if (edge == "main")
            return OffTheLine(along, offset) && !OnStop(along, offset);
        if (_terrain.WaterAt(w.X, w.Z) is not null)
            return false;
        foreach (var n in _terrain.Nearby(w.X, w.Z, 12))
            if (Math.Abs(n.Lateral) < 4.5)
                return false;
        return true;
    }

    /// <summary>Whether a sight line's point is behind a stop's building (main-line stops; each a disc round it, as tall as a house).</summary>
    bool Shelter(Double3 w)
    {
        if (!_buildings.TryGetValue(((long)Math.Floor(w.X / BuildingCell), (long)Math.Floor(w.Z / BuildingCell)), out var near))
            return false;
        foreach (var (x, z, r, top) in near)
            if (w.Y < top && (w.X - x) * (w.X - x) + (w.Z - z) * (w.Z - z) < r * r)
                return true;
        return false;
    }

    /// <summary>Whether a branch is clear of bridges and tunnels at a distance along it (the main line's: <see cref="Clear"/>).</summary>
    bool ClearOn(string edge, double at)
    {
        if (edge == "main")
            return Clear(at);
        foreach (var st in _plan.Structures)
            if (st.Edge == edge && st.Type is StructureType.Tunnel or StructureType.Trestle or StructureType.Girder or StructureType.Truss or StructureType.Viaduct
                && at > st.S0 - 30 && at < st.S1 + 30)
                return false;
        return true;
    }

    /// <summary>A board stood beside this night's line (<see cref="Place"/>): its land, its footing and the stops' buildings this night's.</summary>
    BoardSite Site(string edge, RailLine line, double s, int side, string type, string text, BoardDef? def = null, BoardFace? face = null)
    {
        var r = _plan.Rules.Billboard;
        def ??= _plan.Rules.Boards.GetValueOrDefault(type);
        face ??= BoardLayout.Of(type, text, def, r);
        // The side every board stands on (billboard.side), whatever side the plan gave it; a plate's side is the way it faces.
        if (type != "tunnelPlate" && (def?.Side ?? 0) != 0)
            side = def!.Side;
        else if (type != "tunnelPlate" && r.Side != 0)
            side = r.Side;
        // (Clear of the boards already stood, on its side: one standing before another would hide it. And no face of theirs in
        // front of it while it's read, nor it in front of one of theirs: `pt13-signs`.)
        return Place(def, face, r, edge, line, s, side, type, text, Ground, Footing, Shelter, at => ClearOn(edge, at),
            // (A km post, fixed, takes only postClearM of ground: it's small and low, and the sight lines keep the boards off it.)
            (at, stand) => _placed.Any(p => p.Edge == edge && p.Side == stand
                && Math.Abs(p.Along - at) < (_plan.Rules.Boards.GetValueOrDefault(p.Type)?.Fixed == true ? r.PostClearM : r.StackM)),
            WayNear, site => Hides(site, r), SeesCached);
    }

    /// <summary>
    /// Whether a board already stood has its face between the driver's eye and a point, for a board standing at
    /// <paramref name="centre"/> read from as far as <paramref name="reach"/>: only the boards that near it are asked.
    /// </summary>
    Func<Double3, Double3, bool> WayNear(Double3 centre, double reach)
    {
        var near = _placed.Where(p => (p.Centre - centre).Length <= reach + p.Face.Width + p.Face.Height).ToArray();
        return (eye, target) =>
        {
            foreach (var p in near)
            {
                // (Only those near the sight line: a face is never wider than the line is long.)
                double size = (p.Face.Width + p.Face.Height) / 2;
                var c = p.Centre;
                if (c.X < Math.Min(eye.X, target.X) - size || c.X > Math.Max(eye.X, target.X) + size
                    || c.Z < Math.Min(eye.Z, target.Z) - size || c.Z > Math.Max(eye.Z, target.Z) + size)
                    continue;
                if (p.Crosses(eye, target))
                    return true;
            }
            return false;
        };
    }

    /// <summary>Whether a board standing at <paramref name="site"/> would come in front of one already stood further on, where that one's read.</summary>
    bool Hides(BoardSite site, BillboardRules r)
    {
        foreach (var p in _placed)
        {
            if (p.Edge != site.Edge || p.Along <= site.Along || p.Along - site.Along > r.ClearFromM || LineOf(p.Edge) is not { } line)
                continue;
            foreach (double seen in p.SeenFrom)
                foreach (double a in BoardSite.AboutEye(seen, r.SightStepM))
                {
                    if (a < 0 || a >= site.Along || p.Along - a > r.ClearFromM + 1e-9)
                        continue;
                    var eye = Eye(line, a, r);
                    foreach (var target in p.FaceGrid())
                        if (site.Crosses(eye, target))
                            return true;
                }
        }
        return false;
    }

    /// <summary>The land's and the buildings' answers to sight lines already asked (a board is stood at a spot more than once, hung alone and with its neighbours).</summary>
    readonly Dictionary<(long, long, long, long, long, long), bool> _sees = [];

    bool SeesCached(Double3 eye, Double3 target)
    {
        var key = ((long)Math.Round(eye.X * 100), (long)Math.Round(eye.Y * 100), (long)Math.Round(eye.Z * 100),
            (long)Math.Round(target.X * 100), (long)Math.Round(target.Y * 100), (long)Math.Round(target.Z * 100));
        if (!_sees.TryGetValue(key, out bool seen))
            _sees[key] = seen = Sees(eye, target, Ground, Shelter) && !ThroughTruss(eye, target);
        return seen;
    }

    /// <summary>
    /// The through trusses' sides (note 686), each a run of walls along the span either side of the track
    /// (billboard.trussHalfM off the centreline; the rail's height at each end): the lattice, at the glancing angles a board's
    /// read at, fills in. With each truss's extent on the ground, to ask only those a sight line comes near.
    /// </summary>
    List<(double MinX, double MinZ, double MaxX, double MaxZ, List<(Double3 A, Double3 B)> Walls)>? _trusses;

    /// <summary>Whether a sight line passes through a truss's side, chord to chord (the cab in the truss, or the line leaving it).</summary>
    bool ThroughTruss(Double3 eye, Double3 target)
    {
        var r = _plan.Rules.Billboard;
        if (_trusses is null)
        {
            _trusses = [];
            foreach (var st in _plan.Structures)
            {
                if (st.Type != StructureType.Truss || LineOf(st.Edge) is not { } line)
                    continue;
                var walls = new List<(Double3, Double3)>();
                int n = Math.Max(1, (int)Math.Ceiling((st.S1 - st.S0) / TrussWallM));
                foreach (int hand in new[] { -1, 1 })
                {
                    Double3 At(double s)
                    {
                        var t = line.Sample(Math.Clamp(s, 0, line.Length));
                        var across = Double3.Cross(new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized, Double3.Up).Normalized;
                        return t.Position + across * (hand * r.TrussHalfM);
                    }
                    for (int i = 0; i < n; i++)
                        walls.Add((At(st.S0 + (st.S1 - st.S0) * i / n), At(st.S0 + (st.S1 - st.S0) * (i + 1) / n)));
                }
                _trusses.Add((walls.Min(w => Math.Min(w.Item1.X, w.Item2.X)), walls.Min(w => Math.Min(w.Item1.Z, w.Item2.Z)),
                    walls.Max(w => Math.Max(w.Item1.X, w.Item2.X)), walls.Max(w => Math.Max(w.Item1.Z, w.Item2.Z)), walls));
            }
        }
        double loX = Math.Min(eye.X, target.X), hiX = Math.Max(eye.X, target.X), loZ = Math.Min(eye.Z, target.Z), hiZ = Math.Max(eye.Z, target.Z);
        foreach (var (minX, minZ, maxX, maxZ, walls) in _trusses)
        {
            if (hiX < minX || loX > maxX || hiZ < minZ || loZ > maxZ)
                continue;
            foreach (var (a, b) in walls)
            {
                double rx = target.X - eye.X, rz = target.Z - eye.Z, wx = b.X - a.X, wz = b.Z - a.Z;
                double den = rx * wz - rz * wx;
                if (Math.Abs(den) < 1e-12)
                    continue;
                double ex = a.X - eye.X, ez = a.Z - eye.Z;
                double t = (ex * wz - ez * wx) / den, u = (ex * rz - ez * rx) / den;
                if (t <= 1e-6 || t >= 1 - 1e-6 || u < 0 || u > 1)
                    continue;
                double h = eye.Y + t * (target.Y - eye.Y), rail = a.Y + u * (b.Y - a.Y);
                if (h >= rail + r.TrussLowM && h <= rail + r.TrussHighM)
                    return true;
            }
        }
        return false;
    }

    /// <summary>How long each of a truss side's walls is (m): a curve's span followed closely enough.</summary>
    const double TrussWallM = 5;

    RailLine? LineOf(string edge) => edge == "main" ? _line
        : _plan.Edge(edge).Branch is int b && b >= 0 && b < _line.Branches.Count ? _line.Branches[b].Local : null;

    /// <summary>The driver's eye (billboard.eyeM up, eyeAcrossM to the right of the line's middle) with the cab at <paramref name="a"/>.</summary>
    static Double3 Eye(RailLine line, double a, BillboardRules r)
    {
        var c = line.Sample(a);
        var dir = new Double3(c.Tangent.X, 0, c.Tangent.Z).Normalized;
        return c.Position + Double3.Up * r.EyeM + Double3.Cross(dir, Double3.Up).Normalized * r.EyeAcrossM;
    }

    /// <summary>
    /// Whether the cab's own frame (its middle post, the right front window's outer pillar: billboard.cabBlindDeg) stands
    /// between the driver and a point <paramref name="right"/> m to their right and <paramref name="ahead"/> m ahead.
    /// </summary>
    static bool CabHides(BillboardRules r, double right, double ahead)
    {
        double deg = DMath.Atan2(right, ahead) * 180 / DMath.PI;
        for (int i = 0; i + 1 < r.CabBlindDeg.Count; i += 2)
            if (deg >= r.CabBlindDeg[i] && deg <= r.CabBlindDeg[i + 1])
                return true;
        return false;
    }

    /// <summary>
    /// A run of boards close together on one side of the line (within billboard.stackM: a limit and what it's for, a resume
    /// board and its whistle) stood as one: hung one under another on the run's posts, the first on top, so none stands in
    /// front of another's sight line. Each board's own site; the posts are the lowest's.
    /// </summary>
    List<BoardSite> Stack(string edge, RailLine line, int side, IReadOnlyList<(string Type, string Text, double S)> boards)
    {
        var r = _plan.Rules.Billboard;
        var faces = boards.Select(b => BoardLayout.Of(b.Type, b.Text, _plan.Rules.Boards.GetValueOrDefault(b.Type), r)).ToList();
        if (boards.Count == 1)
            return [Site(edge, line, boards[0].S, side, boards[0].Type, boards[0].Text, face: faces[0])];
        double gap = r.StackGapM, width = faces.Max(f => f.Width), height = faces.Sum(f => f.Height) + gap * (faces.Count - 1);
        var def = (_plan.Rules.Boards.GetValueOrDefault(boards[0].Type) ?? new BoardDef("", 3.5, 1, 150, true)) with
        {
            VisibleM = boards.Max(b => _plan.Rules.Boards.GetValueOrDefault(b.Type)?.VisibleM ?? 150),
            Warns = boards.Any(b => _plan.Rules.Boards.GetValueOrDefault(b.Type)?.Warns == true),
        };
        var whole = Site(edge, line, boards[0].S, side, boards[0].Type, "", def, new BoardFace([], faces[0].Pixel, width, height, 0));
        var outward = whole.Right * whole.Side;
        var inner = whole.Bottom - outward * (width / 2);
        var sites = new List<BoardSite>();
        double top = whole.Bottom.Y + height;
        for (int i = 0; i < boards.Count; i++)
        {
            top -= faces[i].Height;
            var bottom = (inner + outward * (faces[i].Width / 2)) with { Y = top };
            sites.Add(whole with
            {
                Type = boards[i].Type,
                Text = boards[i].Text,
                S = boards[i].S,
                Face = faces[i],
                Bottom = bottom,
                Posts = i == boards.Count - 1 ? whole.Posts : [],
            });
            top -= gap;
        }
        return sites;
    }

    /// <summary>Stands every board of the plan's and the sim's (at the default sight tuning), once.</summary>
    void Stand()
    {
        lock (_boardsLock)
        {
            if (_cones is not null)
                return;
            var rules = _plan.Rules;
            // The stops' buildings, as discs round them, as tall as a house is (the sight test's only buildings).
            foreach (var f in _route.Features)
                foreach (var b in f.Stop?.Buildings ?? [])
                {
                    var w = World(f.Start + b.S, b.D);
                    double r = double.Hypot(b.Length, b.Width) / 2;
                    var disc = (w.X, w.Z, r, _terrain.Height(w.X, w.Z) + 9.0);
                    for (long i = (long)Math.Floor((w.X - r) / BuildingCell); i <= (long)Math.Floor((w.X + r) / BuildingCell); i++)
                        for (long j = (long)Math.Floor((w.Z - r) / BuildingCell); j <= (long)Math.Floor((w.Z + r) / BuildingCell); j++)
                        {
                            if (!_buildings.TryGetValue((i, j), out var list))
                                _buildings[(i, j)] = list = [];
                            list.Add(disc);
                        }
                }
            var plan = new Dictionary<PlanSign, BoardSite>();
            var cones = new SightCones();
            // A plate on its portal, and a fallen board down where it stood, alone; the standing ones in runs (Stack).
            var standing = new List<PlanSign>();
            foreach (var sign in _plan.Signage)
            {
                if (sign.State == SignState.Missing || sign.Type == "deadSignal" || LineOf(sign.Edge) is not { } line)
                    continue;
                if (sign.State == SignState.Intact && sign.Type != "tunnelPlate")
                    standing.Add(sign);
                else
                    plan[sign] = Site(sign.Edge, line, sign.S, sign.Side, sign.Type, sign.Text);
            }
            // The plan's standing boards and the sim's own (sight.json's, on the main line) stand together: a mail crane's
            // board by a limit's hangs with it, rather than one before the other.
            int SideOf(PlanSign sign) => rules.Boards.GetValueOrDefault(sign.Type)?.Side is int own && own != 0 ? own
                : rules.Billboard.Side != 0 ? rules.Billboard.Side : sign.Side;
            bool Warns(string type) => rules.Boards.GetValueOrDefault(type)?.Warns == true;
            bool Fixed(string type) => rules.Boards.GetValueOrDefault(type)?.Fixed == true;
            double Tall(string type, string text) => BoardLayout.Of(type, text, rules.Boards.GetValueOrDefault(type), rules.Billboard).Height;
            var requests = standing.Select(x => (Plan: (PlanSign?)x, x.Edge, Side: SideOf(x), x.S, x.Type, x.Text))
                .Concat(Lineside.Boards(new SightTuning(), _route).Select(x =>
                {
                    var (type, text) = SightLettering(x);
                    return (Plan: (PlanSign?)null, Edge: "main", Side: rules.Billboard.Side != 0 ? rules.Billboard.Side : 1, S: x.Board, Type: type, Text: text);
                }))
                // (The km posts first, where they must stand: the billboards keep clear of them. A warning over what stands
                // with it at the same spot: it's read first.)
                .OrderBy(x => Fixed(x.Type) ? 0 : 1).ThenBy(x => x.Edge, StringComparer.Ordinal).ThenBy(x => x.Side).ThenBy(x => x.S).ThenByDescending(x => Warns(x.Type))
                .ThenBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => x.Text, StringComparer.Ordinal).ToList();
            var sight = new Dictionary<(string, long), BoardSite>();
            _placed.Clear();
            // Close together on a side (within stackM), they're one board on one set of posts (StandRun); a km post stands alone.
            var together = Runs(requests, (run, b) => run[^1].Edge == b.Edge && run[^1].Side == b.Side && b.S - run[^1].S <= rules.Billboard.StackM
                && !Fixed(run[^1].Type) && !Fixed(b.Type));
            foreach (var whole in together)
            {
                // (Two boards saying the same thing hung together, the sim's limit and the plan's: the plan's isn't stood.)
                // And a plan board whose words already stand within its reading back down the line (a bridge's plate at each
                // end, both now facing the train coming) isn't stood again: the second would only stand in front of what's next.
                var run = whole.Where((x, i) => x.Plan is null || !whole.Where((y, j) => j != i && (y.Plan is null || j < i))
                    .Any(y => y.Type == x.Type && y.Text == x.Text)
                    && !_placed.Any(p => p.Edge == x.Edge && p.Type == x.Type && p.Text == x.Text
                        && x.S - p.S >= -1e-9 && x.S - p.S <= (rules.Boards.GetValueOrDefault(x.Type)?.VisibleM ?? 150))).ToList();
                if (run.Count == 0)
                    continue;
                var line = LineOf(run[0].Edge)!;
                var sites = StandRun(run, line);
                // A board moved to stand beside its twin (a bridge's far plate, moved back over the bridge to where it could
                // stand) isn't stood twice.
                if (run.All(x => x.Plan is not null) && sites.All(s => _placed.Any(p => p.Edge == s.Edge && p.Type == s.Type && p.Text == s.Text
                    && Math.Abs(p.Along - s.Along) <= rules.Billboard.StackM)))
                    continue;
                Record(run, sites, line);
            }

            // A run hung as one board (if it's no taller than stackMaxM), unless they're read better apart (the first a mail
            // board on a ledge round a hillside): then the first on its own and the rest as they're read best. Read: metres of
            // approach seen, less those another board stands in front of it.
            List<BoardSite> StandRun(List<(PlanSign? Plan, string Edge, int Side, double S, string Type, string Text)> run, RailLine line)
            {
                var r = rules.Billboard;
                double Score(BoardSite b) => b.SeenM - b.BoardsInWay * r.SightStepM;
                bool fits = run.Sum(x => Tall(x.Type, x.Text)) + r.StackGapM * (run.Count - 1) <= r.StackMaxM + 1e-9;
                var stack = run.Count == 1 || fits ? Stack(run[0].Edge, line, run[0].Side, [.. run.Select(x => (x.Type, x.Text, x.S))]) : null;
                if (stack is not null && (run.Count == 1 || stack[0].SeenM >= r.SeenShare * stack[0].ApproachM - 1e-9 && stack[0].BoardsInWay == 0))
                    return stack;
                // Apart: the first stood, then the rest; or the rest, then the first kept out of their way (a board seen
                // from nowhere near where it must stand mustn't stand in front of its neighbours either).
                int mark = _placed.Count;
                var first = Stack(run[0].Edge, line, run[0].Side, [(run[0].Type, run[0].Text, run[0].S)]);
                _placed.AddRange(first);
                var rest = StandRun(run[1..], line);
                _placed.RemoveRange(mark, _placed.Count - mark);
                double apart = Score(first[0]) + rest.Sum(Score);
                List<BoardSite> split = [.. first, .. rest];
                if (first[0].SeenM < r.SeenShare * first[0].ApproachM - 1e-9 || first[0].BoardsInWay > 0)
                {
                    var restFirst = StandRun(run[1..], line);
                    _placed.AddRange(restFirst);
                    var last = Stack(run[0].Edge, line, run[0].Side, [(run[0].Type, run[0].Text, run[0].S)]);
                    _placed.RemoveRange(mark, _placed.Count - mark);
                    if (Score(last[0]) + restFirst.Sum(Score) > apart)
                        (split, apart) = ([.. last, .. restFirst], Score(last[0]) + restFirst.Sum(Score));
                }
                return stack is null || apart > stack.Sum(Score) ? split : stack;
            }

            void Record(IReadOnlyList<(PlanSign? Plan, string Edge, int Side, double S, string Type, string Text)> run, List<BoardSite> sites, RailLine line)
            {
                for (int i = 0; i < run.Count; i++)
                    if (run[i].Plan is { To: { } to } && Distance(sites[i], to) is { } said)
                        sites[i] = said;
                _placed.AddRange(sites);
                for (int i = 0; i < run.Count; i++)
                {
                    if (run[i].Plan is { } sign)
                        plan[sign] = sites[i];
                    else
                        sight[(run[i].Type, (long)Math.Round(run[i].S * 10))] = sites[i];
                    cones.Add(line, sites[i], rules.Billboard);
                }
            }
            _planBoards = plan;
            _sightBoards = sight;
            _cones = cones;
        }
    }

    /// <summary>
    /// A distance board ("RENWICK 0.5 KM") stood off its plan's spot, to where it can be read, lettered with its own distance
    /// from there to <paramref name="to"/> (note 684: a "0.5 KM" stood 118 m on still said 0.5 km); null if it says so already.
    /// </summary>
    BoardSite? Distance(BoardSite site, double to)
    {
        int unit = site.Text.LastIndexOf(' ', site.Text.Length - " KM".Length - 1);
        if (!site.Text.EndsWith(" KM", StringComparison.Ordinal) || unit < 0)
            return null;
        // (Never less than the nearest tenth: a board's past nothing it counts to.)
        string text = $"{site.Text[..unit]} {Math.Max(0.1, (to - site.Along) / 1000):0.0} KM";
        if (text == site.Text)
            return null;
        var face = BoardLayout.Of(site.Type, text, _plan.Rules.Boards.GetValueOrDefault(site.Type), _plan.Rules.Billboard);
        return site with { Text = text, Face = face };
    }

    /// <summary>A sorted list cut into runs: each item joins the run so far if <paramref name="together"/> holds of them.</summary>
    static IEnumerable<List<T>> Runs<T>(IReadOnlyList<T> sorted, Func<List<T>, T, bool> together)
    {
        var run = new List<T>();
        foreach (var item in sorted)
        {
            if (run.Count > 0 && !together(run, item))
            {
                yield return run;
                run = [];
            }
            run.Add(item);
        }
        if (run.Count > 0)
            yield return run;
    }

    /// <summary>
    /// Stands a board (note 560): its face stood back <see cref="BillboardRules.InnerM"/> from the track and turned in to the
    /// cab, up over the land at its foot, and moved back toward the train (or, failing that, on past where the plan put it)
    /// until the driver sees it, in the lamp, over enough of its approach; never where a post would stand on the line's
    /// own ground (<paramref name="footing"/>) or by a bridge or tunnel (<paramref name="clear"/>). A tunnel's plate stays on
    /// its portal. Pure: the same board stands the same on every machine.
    /// </summary>
    /// <param name="def">Its kind (signage.json): how high it stands and how far off it's read.</param>
    /// <param name="face">Its face, as its words lay it out (<see cref="BoardLayout"/>).</param>
    /// <param name="ground">The land's height at a world point.</param>
    /// <param name="footing">Whether a post can stand at a world point (the edge, along it and out from it, and the point).</param>
    /// <param name="shelter">Whether a world point is behind a building.</param>
    /// <param name="clear">Whether the line is clear of bridges and tunnels at a distance along it.</param>
    /// <param name="taken">Whether a distance along it, on a side, is another board's already.</param>
    public static BoardSite Place(BoardDef? def, BoardFace face, BillboardRules r, string edge, RailLine line, double s, int side,
        string type, string text, Func<double, double, double> ground, Func<string, double, double, Double3, bool> footing,
        Func<Double3, bool> shelter, Func<double, bool> clear, Func<double, int, bool>? taken = null,
        Func<Double3, double, Func<Double3, Double3, bool>>? inTheWay = null, Func<BoardSite, bool>? hides = null,
        Func<Double3, Double3, bool>? sees = null)
    {
        if (type == "tunnelPlate")
        {
            // Over the arch, square on its portal, facing out of it (+1 the train coming in; −1 the far portal's).
            var t = line.Sample(Math.Clamp(s, 0, line.Length));
            var fwd = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
            var toward = fwd * -side;
            var right = Double3.Cross(Double3.Up, toward);
            double top = def?.HeightM ?? 11.2;
            return new BoardSite(type, text, edge, s, s, side, face, t.Position + Double3.Up * (top - face.Height), right, toward, [], 0, 0);
        }
        BoardSite? best = null;
        int steps = (int)Math.Floor(r.MaxBackM / Math.Max(1, r.StepBackM));
        bool Clear(double at) => at >= 0 && at <= line.Length && clear(at) && clear(at - face.Width) && clear(at + face.Width);
        // Where the plan put it, then the nearest spots either way, back toward the train before on past it (and never on
        // past it for a warning: it'd come too late); on its own side first, then (an alternate's track or a road along it,
        // a stop's ground, the land) the other. The first seen over enough of its approach with no other board in front of
        // it while it's read, and itself in front of none (`pt13-signs`); or else the best read (seen, less where one's in front).
        // (A fixed one, a km post, is only ever nudged a step or two either way to be seen: it marks its km.)
        bool keep = def?.Fixed == true;
        var offsets = new List<double> { 0 };
        for (int k = 1; k <= (keep ? Math.Min(steps, 2) : steps); k++)
        {
            offsets.Add(-k * r.StepBackM);
            if (def?.Warns != true || keep)
                offsets.Add(k * r.StepBackM);
        }
        // And where it's read on neither side by the line (a dead line alongside keeps its posts off one side, a rock wall's
        // curve hides the other: note 693), stood back past what's in the way, billboard.backInnerM from the track.
        foreach (double innerM in (ReadOnlySpan<double>)[r.InnerM, r.BackInnerM])
        {
            if (innerM != r.InnerM && (keep || def?.Side is int own && own != 0))
                break;
            foreach (int stand in (ReadOnlySpan<int>)[side, -side])
                foreach (double offset in offsets)
                {
                    double at = s + offset;
                    if (!Clear(at) || !keep && taken?.Invoke(at, stand) == true
                        || Stood(line, edge, at, stand, type, text, face, def, r, ground, footing, shelter, inTheWay, sees, innerM) is not { } site
                        || !keep && hides?.Invoke(site) == true)
                        continue;
                    site = site with { S = s };
                    if (site.SeenM >= r.SeenShare * site.ApproachM - 1e-9 && site.BoardsInWay == 0)
                        return site;
                    if (best is null || site.SeenM - site.BoardsInWay * r.SightStepM > best.SeenM - best.BoardsInWay * r.SightStepM)
                        best = site;
                }
        }
        if (best is not null)
            return best;
        // Nowhere to stand near it (a limit for a bend inside a tunnel): the first spot back down the line clear of tunnels
        // and bridges, never up on the hill over the bore.
        for (double at = s; at >= Math.Max(0, s - 1000); at -= r.StepBackM)
            if (Clear(at))
                return Stood(line, edge, at, side, type, text, face, def, r, ground, (_, _, _, _) => true, shelter)! with { S = s };
        return Stood(line, edge, Math.Clamp(s, 0, line.Length), side, type, text, face, def, r, ground, (_, _, _, _) => true, shelter)! with { S = s };
    }

    /// <summary>A board stood at <paramref name="at"/>, its sight measured; null if a post can't stand there.</summary>
    static BoardSite? Stood(RailLine line, string edge, double at, int side, string type, string text, BoardFace face, BoardDef? def, BillboardRules r,
        Func<double, double, double> ground, Func<string, double, double, Double3, bool> footing, Func<Double3, bool> shelter,
        Func<Double3, double, Func<Double3, Double3, bool>>? inTheWay = null, Func<Double3, Double3, bool>? sees = null, double? innerM = null)
    {
        sees ??= (eye, target) => Sees(eye, target, ground, shelter);
        double innerAt = innerM ?? r.InnerM;
        var t = line.Sample(at);
        var fwd = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var across = Double3.Cross(fwd, Double3.Up).Normalized;
        double turn = r.TowardDeg * DMath.PI / 180, cos = DMath.Cos(turn), sin = DMath.Sin(turn);
        // Facing back down the line at the train, turned in toward the track; its face runs out from the inner edge.
        var toward = (fwd * -cos - across * (side * sin)).Normalized;
        var right = Double3.Cross(Double3.Up, toward).Normalized;
        var outward = right * side;
        var inner = t.Position + across * (side * innerAt);
        double w = face.Width;
        // Its posts: one under a narrow board, two under a wide one, none nearer the track than postsFromM.
        double[] us = w > 3 ? [0.2 * w, 0.8 * w] : [0.5 * w];
        for (int i = 0; i < us.Length; i++)
            us[i] = Math.Min(w, Math.Max(us[i], (r.PostsFromM - innerAt) / Math.Max(0.1, cos)));
        var feet = new Double3[us.Length];
        double highest = double.MinValue;
        for (int i = 0; i < us.Length; i++)
        {
            var p = inner + outward * us[i];
            double g = ground(p.X, p.Z);
            feet[i] = p with { Y = g };
            if (!footing(edge, at - sin * us[i], side * (innerAt + cos * us[i]), p))
                return null;
            highest = Math.Max(highest, g);
        }
        foreach (double u in (ReadOnlySpan<double>)[0, 0.5 * w, w])
        {
            var p = inner + outward * u;
            highest = Math.Max(highest, ground(p.X, p.Z));
        }
        double bottomY = Math.Max(t.Position.Y + (def?.HeightM ?? 3.5), highest + (def?.OverGroundM > 0 ? def.OverGroundM : r.OverGroundM));
        var bottom = (inner + outward * (w / 2)) with { Y = bottomY };
        var posts = feet.Select(f => (f, bottomY + face.Height * 0.85 - f.Y)).ToArray();
        // How much of its approach sees it: the driver's eye every sightStepM from visibleM out to readToM short of it.
        var centre = bottom + Double3.Up * (face.Height / 2);
        double cone = DMath.Cos(r.LampConeDeg * DMath.PI / 180);
        double visible = def?.VisibleM ?? 150, seen = 0, approach = 0;
        int inWay = 0;
        var from = new List<double>();
        var site = new BoardSite(type, text, edge, at, at, side, face, bottom, right, toward, posts, 0, 0);
        var targets = site.SightTargets().ToArray();
        var grid = site.FaceGrid().ToArray();
        var way = inTheWay?.Invoke(centre, (def?.VisibleM ?? 150) + r.SightStepM);
        for (double d = visible; d >= r.ReadToM - 1e-9; d -= r.SightStepM)
        {
            double a = at - d;
            if (a < 0)
                continue;
            approach += r.SightStepM;
            var c = line.Sample(a);
            var lamp = c.Position + Double3.Up * r.EyeM;
            var to = centre - lamp;
            var flat = new Double3(to.X, 0, to.Z);
            var dir = new Double3(c.Tangent.X, 0, c.Tangent.Z).Normalized;
            // In the lamp's cone (it shines along the engine's heading), and the whole face past the land and the buildings
            // from the driver's eye: its middle and its two lower corners (a cutting's bank hides the low outer end first).
            if (Double3.Dot(flat.Normalized, dir) < cone)
                continue;
            var eye = Eye(line, a, r);
            // Nor the cab's own frame across any of its words (`pt13-signs`), nor another board's face: while it's read
            // (clearFromM in, from either side of here too), never. The land last: it's the dearest to ask.
            var eyeRight = Double3.Cross(dir, Double3.Up).Normalized;
            if (grid.Any(p => CabHides(r, Double3.Dot(p - eye, eyeRight), Double3.Dot(p - eye, dir))))
                continue;
            bool read = d <= r.ClearFromM + 1e-9;
            bool blocked = way is not null && (read
                ? BoardSite.AboutEye(a, r.SightStepM).Where(e => e >= 0).Select(e => Eye(line, e, r)).Any(e => grid.Any(p => way(e, p)))
                : grid.Any(p => way(eye, p)));
            if (blocked && !read)
                continue;
            if (targets.Any(p => !sees(eye, p)))
                continue;
            if (blocked)
            {
                inWay++;
                continue;
            }
            seen += r.SightStepM;
            from.Add(a);
        }
        return site with { SeenM = seen, ApproachM = approach, SeenFrom = from, BoardsInWay = inWay };
    }

    /// <summary>How finely a sight line is asked of the land (m): finer than any rise that could hide a board this big.</summary>
    const double RayStepM = 12;

    /// <summary>Whether the land, or a building, stands between the eye and a point (every <see cref="RayStepM"/> along).</summary>
    static bool Sees(Double3 eye, Double3 target, Func<double, double, double> ground, Func<Double3, bool> shelter)
    {
        var d = target - eye;
        int n = Math.Max(1, (int)Math.Ceiling(d.Length / RayStepM));
        for (int i = 1; i < n; i++)
        {
            var p = eye + d * ((double)i / n);
            if (ground(p.X, p.Z) > p.Y - 0.3 || shelter(p))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The boards' sight lines on the ground (note 560): for each, the triangles between the driver's eye where it sees the
    /// board and its face's two ends, in a grid so a tree asks only those near it.
    /// </summary>
    sealed class SightCones
    {
        const double Cell = 32, Pad = 20;
        readonly List<(double Ax, double Az, double Bx, double Bz, double Cx, double Cz)> _tris = [];
        readonly Dictionary<(long, long), List<int>> _grid = [];

        public void Add(RailLine line, BoardSite site, BillboardRules r)
        {
            var inner = site.Bottom - site.Right * (site.Side * site.Face.Width / 2);
            var outer = site.Bottom + site.Right * (site.Side * site.Face.Width / 2);
            // From where the driver sees it, and between neighbouring such places.
            double? last = null;
            foreach (double a in site.SeenFrom)
            {
                var c = line.Sample(a).Position;
                Tri(c, inner, outer);
                if (last is { } l && a - l <= r.SightStepM + 1e-6)
                {
                    var p = line.Sample(l).Position;
                    Tri(p, c, inner);
                    Tri(p, c, outer);
                }
                last = a;
            }
            // And the ground under the face itself.
            Tri(inner, outer, inner - site.Toward * 2);
            Tri(inner, outer, outer - site.Toward * 2);
        }

        void Tri(Double3 a, Double3 b, Double3 c)
        {
            int i = _tris.Count;
            _tris.Add((a.X, a.Z, b.X, b.Z, c.X, c.Z));
            long x0 = (long)Math.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) - Pad) / Cell), x1 = (long)Math.Floor((Math.Max(a.X, Math.Max(b.X, c.X)) + Pad) / Cell);
            long z0 = (long)Math.Floor((Math.Min(a.Z, Math.Min(b.Z, c.Z)) - Pad) / Cell), z1 = (long)Math.Floor((Math.Max(a.Z, Math.Max(b.Z, c.Z)) + Pad) / Cell);
            for (long x = x0; x <= x1; x++)
                for (long z = z0; z <= z1; z++)
                {
                    if (!_grid.TryGetValue((x, z), out var list))
                        _grid[(x, z)] = list = [];
                    list.Add(i);
                }
        }

        /// <summary>Whether a disc of <paramref name="r"/> (at most <see cref="Pad"/>) at a point meets any sight triangle.</summary>
        public bool Holds(double x, double z, double r)
        {
            if (!_grid.TryGetValue(((long)Math.Floor(x / Cell), (long)Math.Floor(z / Cell)), out var list))
                return false;
            r = Math.Min(r, Pad);
            foreach (int i in list)
            {
                var (ax, az, bx, bz, cx, cz) = _tris[i];
                if (Inside(x, z, ax, az, bx, bz, cx, cz) || Near(x, z, ax, az, bx, bz, r) || Near(x, z, bx, bz, cx, cz, r) || Near(x, z, cx, cz, ax, az, r))
                    return true;
            }
            return false;
        }

        static bool Inside(double x, double z, double ax, double az, double bx, double bz, double cx, double cz)
        {
            double d1 = (x - bx) * (az - bz) - (ax - bx) * (z - bz);
            double d2 = (x - cx) * (bz - cz) - (bx - cx) * (z - cz);
            double d3 = (x - ax) * (cz - az) - (cx - ax) * (z - az);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static bool Near(double x, double z, double ax, double az, double bx, double bz, double r)
        {
            double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
            double t = len < 1e-12 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / len, 0, 1);
            double px = ax + dx * t - x, pz = az + dz * t - z;
            return px * px + pz * pz < r * r;
        }
    }
}
