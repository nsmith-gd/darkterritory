using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Towns;

/// <summary>What in a town a crewmate is looking at, close enough to talk to, read or knock on: a person, the board, a
/// paper, a thing, one of the square's doors, a house's door (knocked at, or a house nobody lives in looked at).</summary>
public enum TownTargetKind : byte { Person, Board, Paper, Fixture, Door, House }

/// <summary>A thing in the town to use: its kind, and its index in the plan's people, papers, fixtures, buildings or houses.</summary>
public readonly record struct TownTarget(TownTargetKind Kind, int Index);

/// <summary>
/// A fortress town put down on its line (GDD §3.1; ARCHITECTURE §8 note 281): its plan in world space, the walls that
/// make it solid (App. F.1: "the world is solid"), and what a crewmate standing in it is looking at. Talking and reading
/// change nothing in the night, so nothing here is replicated: every machine builds the same town from the route, and
/// a card opened on one shows on that one alone.
/// </summary>
public sealed class Town
{
    readonly RailLine _line;

    public Town(TownPlan plan, TownTuning tuning, RailLine line, HouseLooks? looks = null)
    {
        Plan = plan;
        Tuning = tuning;
        Looks = looks ?? new HouseLooks();
        _line = line;
        Ground = TownGround.Of(plan);
        Walls = [.. BuildWalls()];
        Index();
        _behind = new double[plan.People.Count];
        Rounds = TownRounds.Plan(this);
    }

    /// <summary>
    /// The town at the end of <paramref name="route"/> (ARCHITECTURE §8 notes 600, 692): <paramref name="arrival"/>, built from
    /// the site the next night's departure will build it from (its gate <paramref name="gate"/> along its own frame, as this
    /// night's departure's is; <paramref name="last"/>, this night's departure town's custom, the one it won't share), so the
    /// same plan. It stands in <paramref name="line"/> the same way round as it will when the next night leaves it
    /// (<see cref="RailLine.From"/>, <see cref="Origin"/>): its way in from the last night (its rear wall's gate) at the
    /// terminus's gate, where the train comes in, and its way out to the next at the far end of its yard, so this night's
    /// line could run on through it into the next night's. Null for a silent settlement, a hand-laid line, or no town named.
    /// </summary>
    public static Town? AtTerminus(TownContent content, Route.Route route, RailLine line, double gate, IReadOnlyList<string> roster, string? last,
        TownAt? arrival)
    {
        if (arrival is null || !content.Tuning.Enabled || route.Plan?.Terminus is not { Silent: false } terminus)
            return null;
        var plan = TownGenerator.Generate(content, new TownSite(arrival.Name, arrival.Industry, gate, arrival.Seed, roster, last, arrival.Burnt));
        double origin = terminus.GateM - plan.Frame.Entry;
        var frame = line.From(origin, gate + FrameBeyond);
        return new Town(plan, content.Tuning, frame, content.Looks) { Origin = origin };
    }

    /// <summary>How far past its way out a terminus town's frame runs on (m), where the line does: far enough for anything
    /// placed off it. Not a design number.</summary>
    const double FrameBeyond = 100;

    /// <summary>
    /// The night's clock the townspeople's rounds run on (seconds; note 353): <see cref="DarkTerritory.Sim.World"/> sets
    /// it from its tick every step, so every machine has them in the same places.
    /// </summary>
    public double Clock { get; set; }

    /// <summary>Each person's round (by id), or null for somebody who keeps to their place.</summary>
    public IReadOnlyList<TownRound?> Rounds { get; }

    // Who's held on this machine (talked to: they stop for you), since when, and how far behind the clock each one's round
    // has fallen for it. Talking changes nothing in the night, so this is this machine's alone.
    readonly double[] _behind;
    int _held = -1;
    double _heldAt;

    /// <summary>Holds <paramref name="person"/> where they are (whoever's being talked to; −1 for nobody): their round
    /// waits for you and goes on from there after.</summary>
    public void Hold(int person)
    {
        if (person == _held)
            return;
        if (_held >= 0)
            _behind[_held] += Clock - _heldAt;
        _held = person;
        _heldAt = Clock;
    }

    /// <summary>How far into their own round somebody is (the clock, less the times they stopped to talk).</summary>
    double Own(int id) => (id == _held ? _heldAt : Clock) - _behind[id];

    (Double3 Centre, double Radius)[]? _reach;

    /// <summary>
    /// Where somebody's whole round lies (world: a circle on the level round their post, their stops and the ways between),
    /// so a view far from it needn't ask where they are now (note 479: a big town's people, every frame).
    /// </summary>
    public (Double3 Centre, double Radius) Reach(Townsperson p)
    {
        _reach ??= [.. Plan.People.Select(Bound)];
        return _reach[p.Id];
    }

    (Double3 Centre, double Radius) Bound(Townsperson p)
    {
        var points = new List<Double3> { World(p.S, p.D, p.Up) };
        if (Rounds[p.Id] is { } r)
            foreach (var stop in r.Stops)
            {
                points.Add(World(stop.S, stop.D, p.Up));
                points.AddRange(stop.Via.Select(v => World(v.S, v.D, p.Up)));
            }
        var centre = points.Aggregate((a, b) => a + b) * (1.0 / points.Count);
        return (centre, points.Max(q => (q - centre).Length));
    }

    /// <summary>
    /// The yard's fight (ARCHITECTURE §8 note 608; note 589): where along this town's own frame (its S) a creature brought in
    /// through the gate is being fought, while it is; null, none. The town's guards leave their posts for the yard and stand
    /// round it, firing (<see cref="OnTheYard"/>). Whatever draws the town sets it from what it sees: like who's held to
    /// talk, it moves nobody in the night.
    /// </summary>
    public double? Fight { get; set; }

    /// <summary>Who turns out to the yard's fight (note 608): the gate's keeper and the town's guards.</summary>
    public static bool Guards(Townsperson p) => p.Role is "gatekeeper" or "guard";

    // Out on the yard (note 608): this far out from the line either side, and this far apart along it.
    const double YardOut = 5.5, YardApart = 6;

    /// <summary>
    /// Where the <paramref name="k"/>th of the town's guards stands to the yard's fight at <paramref name="fight"/> (note 608):
    /// either side of the line in turn, the first pair abreast of it and the rest either way along it, facing it.
    /// </summary>
    public TownPose OnTheYard(int k, double fight, double up = 0)
    {
        int side = k % 2 == 0 ? 1 : -1;
        int pair = k / 2;
        double s = fight + (pair % 2 == 0 ? pair / 2 : -(pair + 1) / 2) * YardApart, d = side * YardOut;
        return new TownPose(World(s, d, up), Direction(s, fight - s, -d), "guard", false);
    }

    // Who's stopped to look at something (note 730: the bell rung, a crewmate's wave), by id: from when to when on the clock,
    // how far into their round they'd got (it waits for them, as for a talk), where they look and what they do. Kept on every
    // machine from the host's replicated happenings, alike; like a talk, it moves nobody in the night.
    readonly Dictionary<int, (double Start, double End, double Own, Double3 Look, string Act)> _reacting = [];

    /// <summary>
    /// Stops <paramref name="person"/> where they are on their round from <paramref name="start"/> on the clock for
    /// <paramref name="seconds"/>, looking at <paramref name="look"/> and doing <paramref name="act"/> (note 730: "idle" to
    /// stand and stare at the bell, "wave" to wave back). Their round waits for them. A second reason while they're stopped
    /// keeps them there longer, looking at the new one.
    /// </summary>
    public void React(int person, double start, double seconds, Double3 look, string act)
    {
        if (person < 0 || person >= _behind.Length || seconds <= 0)
            return;
        double end = start + seconds;
        if (_reacting.TryGetValue(person, out var was) && start < was.End)
        {
            if (end > was.End)
                _behind[person] += end - was.End;
            _reacting[person] = (was.Start, Math.Max(end, was.End), was.Own, look, act);
            return;
        }
        // Where their round had got to at the start (a start already past on this machine counts back to it).
        double own = Own(person) - Math.Max(0, Clock - start);
        _behind[person] += seconds;
        _reacting[person] = (start, end, own, look, act);
    }

    /// <summary>Whether <paramref name="person"/> is stopped looking at something now (note 730), and what they're doing.</summary>
    public string? Reacting(int person) =>
        _reacting.TryGetValue(person, out var r) && Clock >= r.Start && Clock < r.End ? r.Act : null;

    /// <summary>
    /// The council house's bell rope (note 730), at its foot: beside its door in a walled town, on the far side of the door
    /// from its lamp, against its front. Null in a town without a council house.
    /// </summary>
    public Double3? BellRope => Plan.Buildings.FirstOrDefault(b => b.Style == "council") is { } c
        ? World(c.S - RopeAlong, c.D - Math.Sign(c.D) * (c.Depth / 2 + RopeOut), 0) : null;

    // Where the rope hangs from the council house's door: along its front, and out from it (m). Not design numbers.
    const double RopeAlong = 2.1, RopeOut = 0.12;

    /// <summary>Where somebody is now on their round, which way they face, and what they're doing.</summary>
    public TownPose Now(Townsperson p)
    {
        if (Fight is { } fight && Guards(p))
            return OnTheYard(Plan.People.Count(q => q.Id < p.Id && Guards(q)), fight, p.Up);
        if (_reacting.TryGetValue(p.Id, out var r) && Clock >= r.Start && Clock < r.End)
        {
            var there = At(p, r.Own);
            var look = (r.Look - there.Feet) with { Y = 0 };
            return new TownPose(there.Feet, look.Length > 1e-6 ? look.Normalized : there.Facing, r.Act, false);
        }
        return At(p, Own(p.Id));
    }

    /// <summary>Where somebody is <paramref name="own"/> seconds into their round (<see cref="Now"/>, with the clock given).</summary>
    TownPose At(Townsperson p, double own)
    {
        var at = World(p.S, p.D, p.Up);
        if (Rounds[p.Id] is not { Stops.Count: > 0 } r)
            return new TownPose(at, Direction(p.S, p.FaceS, p.FaceD), p.Pose, false);
        int n = r.Stops.Count;
        double t = (own + r.Offset) % r.Period;
        if (t < 0)
            t += r.Period;
        int k = Math.Min(n - 1, (int)(t / r.Slot));
        var stop = r.Stops[k];
        var next = r.Stops[(k + 1) % n];
        // At the stop for the slot, then the walk on to the next by its via points, timed to arrive as the slot ends.
        var here = World(stop.S, stop.D, p.Up);
        var path = next.Via.Select(v => World(v.S, v.D, p.Up)).Append(World(next.S, next.D, p.Up)).ToList();
        double length = 0;
        var from = here;
        foreach (var b in path)
        {
            length += (b - from).Length;
            from = b;
        }
        double walked = (t - k * r.Slot - Math.Max(0, r.Slot - length / Tuning.Rounds.Walk)) * Tuning.Rounds.Walk;
        if (walked <= 0 || length < 1e-6)
            return new TownPose(here, Direction(stop.S, stop.FaceS, stop.FaceD), stop.Act, false);
        from = here;
        foreach (var b in path)
        {
            double len = (b - from).Length;
            if (walked < len && len > 1e-6)
                return new TownPose(from + (b - from) * (walked / len), ((b - from) with { Y = 0 }).Normalized, "walk", true);
            walked -= len;
            from = b;
        }
        return new TownPose(World(next.S, next.D, p.Up), Direction(next.S, next.FaceS, next.FaceD), next.Act, false);
    }

    // The walls by where they stand: an 8 m grid on the level, each wall in every cell it reaches, so a way across the
    // town looks only at the walls beside it (a big town has thousands).
    const double Cell = 8;
    readonly Dictionary<(int X, int Z), List<int>> _cells = [];
    int[] _seen = [];
    int _stamp;

    void Index()
    {
        _seen = new int[Walls.Count];
        for (int i = 0; i < Walls.Count; i++)
        {
            var w = Walls[i];
            double r = w.HalfLength + w.HalfWidth;
            for (int x = (int)Math.Floor((w.Centre.X - r) / Cell); x <= (int)Math.Floor((w.Centre.X + r) / Cell); x++)
                for (int z = (int)Math.Floor((w.Centre.Z - r) / Cell); z <= (int)Math.Floor((w.Centre.Z + r) / Cell); z++)
                {
                    if (!_cells.TryGetValue((x, z), out var list))
                        _cells[(x, z)] = list = [];
                    list.Add(i);
                }
        }
    }

    /// <summary>The walls in the cells round the level box from <paramref name="a"/> to <paramref name="b"/>, <paramref name="pad"/> wider, each once.</summary>
    List<Wall> Around(Double3 a, Double3 b, double pad)
    {
        var found = new List<Wall>();
        _stamp++;
        for (int x = (int)Math.Floor((Math.Min(a.X, b.X) - pad) / Cell); x <= (int)Math.Floor((Math.Max(a.X, b.X) + pad) / Cell); x++)
            for (int z = (int)Math.Floor((Math.Min(a.Z, b.Z) - pad) / Cell); z <= (int)Math.Floor((Math.Max(a.Z, b.Z) + pad) / Cell); z++)
                if (_cells.TryGetValue((x, z), out var list))
                    foreach (int i in list)
                        if (_seen[i] != _stamp)
                        {
                            _seen[i] = _stamp;
                            found.Add(Walls[i]);
                        }
        return found;
    }

    /// <summary>Whether a body can stand at <paramref name="feet"/>: no wall of the town's within a shoulder of it, knees to
    /// chest (<paramref name="indoors"/>: a narrower body, between the furniture).</summary>
    public bool Free(Double3 feet, bool indoors = false)
    {
        foreach (var w in Around(feet, feet, 0.5))
            foreach (double up in (double[])[0.4, 1.2])
                if (Holds(w, feet + Double3.Up * up, indoors ? 0.15 : 0.3))
                    return false;
        return true;
    }

    /// <summary>
    /// Whether a body walks clear from <paramref name="a"/> to <paramref name="b"/>: nothing solid of the town's across the
    /// way at the knees or the chest, a shoulder's width either side (<paramref name="indoors"/>: a narrower body, between
    /// the furniture), the walls that hold either end aside.
    /// </summary>
    public bool Clear(Double3 a, Double3 b, bool indoors = false)
    {
        var way = (b - a) with { Y = 0 };
        double len = way.Length;
        if (len < 1e-6)
            return true;
        var across = Double3.Cross(way * (1 / len), Double3.Up) * (indoors ? 0.15 : 0.3);
        var near = Around(a, b, 0.5);
        foreach (double up in (double[])[0.4, 1.2])
            foreach (double off in (double[])[-1, 0, 1])
            {
                var from = a + across * off + Double3.Up * up;
                var to = b + across * off + Double3.Up * up;
                foreach (var w in near)
                    if (!Holds(w, a + Double3.Up * up, 0.05) && !Holds(w, b + Double3.Up * up, 0.05) && Crosses(w, from, to))
                        return false;
            }
        return true;
    }

    public TownPlan Plan { get; }

    /// <summary>The frame it stands in: the line, or for a terminus's town the line turned round (note 600).</summary>
    public RailLine Line => _line;

    /// <summary>For a terminus's town (notes 600, 692), the main line's distance its frame starts at (<see cref="Line"/>'s 0),
    /// the same way round; null for one standing in the line's own frame.</summary>
    public double? Origin { get; init; }

    /// <summary>Where a distance along the main line is in this town's own frame (note 608; note 692).</summary>
    public double Along(double main) => Origin is { } o ? main - o : main;

    /// <summary>
    /// The town's straight frame (note 692): a rail sample at its frame's 0 on the straight through its way in, so a point
    /// is <c>Position + Tangent·s + Right·d</c>. The yard is straight from its way in on, so this is where
    /// <see cref="World"/> has things there; behind it (an arrival's approach) the line may bend away from it.
    /// </summary>
    public TrackSample Anchor()
    {
        double entry = Math.Max(0, Plan.Frame.Entry);
        var t = _line.Sample(entry);
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        return t with { Position = t.Position - along * entry };
    }

    /// <summary>How its houses look (content/world/houses.json's palettes): the art's, carried with the town.</summary>
    public HouseLooks Looks { get; }
    public TownTuning Tuning { get; }

    /// <summary>The square's walls, its buildings, the solid things in it and its people, as boxes nobody walks through.</summary>
    public IReadOnlyList<Wall> Walls { get; }

    /// <summary>Its ground (note 692): a town type's terraces, benches, decks and water; the yard's level everywhere else.</summary>
    public TownGround Ground { get; }

    /// <summary>
    /// Lays its ground over <paramref name="main"/>'s land (note 692), so what's stood on there is its terraces, decks and
    /// basins' beds: the world does it as it puts the town down, on every machine alike.
    /// </summary>
    public void Lay(RailLine main)
    {
        if (Ground.Flat || main.Conditions is not { } c || (c is Net.HazardConditions h ? h.Inner : c) is not LineGen.PlanConditions pc)
            return;
        var start = Anchor();
        pc.Terrain.Lay(Ground, start.Position, start.Tangent);
    }

    /// <summary>A point in the town's rail frame, in the world: along the line, out to the side, up off the ground (a town
    /// type's terrace or deck there, note 692).</summary>
    public Double3 World(double s, double d, double up = 0)
    {
        var t = _line.Sample(s);
        return t.Position + Right(t) * d + Double3.Up * (up + Ground.Level(s, d));
    }

    /// <summary>A direction in the rail frame (along the line, across it), level, in the world.</summary>
    public Double3 Direction(double s, double alongS, double acrossD)
    {
        var t = _line.Sample(s);
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        return (along * alongS + Right(t) * acrossD).Normalized;
    }

    static Double3 Right(TrackSample t) => Double3.Cross(t.Tangent, Double3.Up).Normalized;

    /// <summary>Where somebody's feet are now (on their round).</summary>
    public Double3 Feet(Townsperson p) => Now(p).Feet;

    /// <summary>The front of a building, at the middle of its face and a door's height.</summary>
    public Double3 Door(TownBuilding b)
    {
        int side = Math.Sign(b.D);
        return World(b.S, b.D - side * (b.Depth / 2 + 0.05), 1.2);
    }

    /// <summary>A shut house's front door, at a door's height: where its design puts it (an enclosed porch's, out in front),
    /// with its body as it's turned (note 490).</summary>
    public Double3 Door(TownHouse h)
    {
        var (s, d) = h.Body(h.Design.DoorU, h.DoorV - 0.05);
        return World(s, d, 1.2);
    }

    /// <summary>What a crewmate on the ground is looking at within reach, the least angle off the view's centre.</summary>
    public TownTarget? Target(in PlayerState p, double eyeHeight)
    {
        if (p.Parent != PlayerState.World || !p.Alive)
            return null;
        var eye = p.Position + Double3.Up * eyeHeight;
        double cp = DMath.Cos(p.Pitch);
        var view = new Double3(-DMath.Sin(p.Yaw) * cp, DMath.Sin(p.Pitch), -DMath.Cos(p.Yaw) * cp);
        return Target(eye, view);
    }

    public TownTarget? Target(Double3 eye, Double3 view)
    {
        var r = Tuning.Reach;
        double bestCos = DMath.Cos(r.LookDegrees * Math.PI / 180);
        TownTarget? best = null;
        foreach (var p in Plan.People)
            Consider(TownTargetKind.Person, p.Id, Feet(p) + Double3.Up * 1.45, r.Talk);
        foreach (var paper in Plan.Papers)
            if (!paper.OnBoard)
                Consider(TownTargetKind.Paper, paper.Id, World(paper.S, paper.D, paper.Height), r.Read);
        foreach (var f in Plan.Fixtures)
        {
            if (f.Kind == "board" && Plan.Papers.Any(x => x.OnBoard))
                Consider(TownTargetKind.Board, f.Id, World(f.S, f.D, 1.5), r.Read + f.SolidS);
            else if (f.Text.Length > 0)
                Consider(TownTargetKind.Fixture, f.Id, LookAt(f), r.Read + Math.Max(f.SolidS, f.SolidD));
        }
        for (int i = 0; i < Plan.Buildings.Count; i++)
            if (Plan.Buildings[i].Knock.Length > 0)
                Consider(TownTargetKind.Door, i, Door(Plan.Buildings[i]), r.Read + 0.5);
        foreach (var h in Plan.Houses)
            if (h.Kind != HouseKind.Open && h.Text.Length > 0)
                Consider(TownTargetKind.House, h.Id, Door(h), r.Read + 0.5);
        return best;

        // Not through a wall (an open house's own walls, a shut house, a stall): a wall that holds the thing itself (the
        // board's, a photograph's on a partition) doesn't count.
        void Consider(TownTargetKind kind, int index, Double3 at, double reach)
        {
            var to = at - eye;
            double d = to.Length;
            if (d > reach || d < 1e-6)
                return;
            double cos = Double3.Dot(view, to * (1 / d));
            if (cos < bestCos || !Seen(eye, at))
                return;
            bestCos = cos;
            best = new TownTarget(kind, index);
        }
    }

    /// <summary>Nothing of the town's in the way between the eye and a thing (the walls that hold it aside).</summary>
    public bool Seen(Double3 eye, Double3 at)
    {
        var to = at - eye;
        double d = to.Length;
        if (d < 0.35)
            return true;
        var end = eye + to * ((d - 0.3) / d);
        foreach (var w in Walls)
            if (!Holds(w, at, 0.06) && Crosses(w, eye, end))
                return false;
        return true;
    }

    static bool Holds(Wall w, Double3 p, double margin)
    {
        var l = w.ToLocal(p);
        return Math.Abs(l.X) <= w.HalfLength + margin && Math.Abs(l.Z) <= w.HalfWidth + margin && l.Y >= w.Bottom - margin && l.Y <= w.Top + margin;
    }

    /// <summary>Whether the segment from <paramref name="a"/> to <paramref name="b"/> passes through a wall's box (slabs, in its frame).</summary>
    static bool Crosses(Wall w, Double3 a, Double3 b)
    {
        var la = w.ToLocal(a);
        var lb = w.ToLocal(b);
        double t0 = 0, t1 = 1;
        bool Slab(double from, double to, double min, double max)
        {
            double dir = to - from;
            if (Math.Abs(dir) < 1e-12)
                return from >= min && from <= max;
            double ta = (min - from) / dir, tb = (max - from) / dir;
            if (ta > tb)
                (ta, tb) = (tb, ta);
            t0 = Math.Max(t0, ta);
            t1 = Math.Min(t1, tb);
            return t0 <= t1;
        }
        return Slab(la.X, lb.X, -w.HalfLength, w.HalfLength) && Slab(la.Z, lb.Z, -w.HalfWidth, w.HalfWidth) && Slab(la.Y, lb.Y, w.Bottom, w.Top);
    }

    /// <summary>
    /// Where a thing is looked at: most of the way up it, but no higher than a head (a works' headframe, the flag: note 353),
    /// so a tall one is read from its foot like anything else.
    /// </summary>
    public Double3 LookAt(TownFixture f) => World(f.S, f.D, Math.Clamp(f.Height * 0.6, 0.3, LookTop));

    /// <summary>The highest a thing is looked at (m).</summary>
    const double LookTop = 2.4;

    /// <summary>Where a target is (for how far away it's got, and for the person turning to face you).</summary>
    public Double3 Where(TownTarget target) => target.Kind switch
    {
        TownTargetKind.Person => Feet(Plan.People[target.Index]) + Double3.Up * 1.45,
        TownTargetKind.Paper => World(Plan.Papers[target.Index].S, Plan.Papers[target.Index].D, Plan.Papers[target.Index].Height),
        TownTargetKind.Door => Door(Plan.Buildings[target.Index]),
        TownTargetKind.House => Door(Plan.Houses[target.Index]),
        _ => World(Plan.Fixtures[target.Index].S, Plan.Fixtures[target.Index].D, 1.5),
    };

    /// <summary>The notices on the board, in the order they're read.</summary>
    public IReadOnlyList<TownPaper> Notices => [.. Plan.Papers.Where(p => p.OnBoard)];

    IEnumerable<Wall> BuildWalls()
    {
        var sq = Plan.Square;
        int side = sq.Side;
        const double half = 0.8; // the fortress wall's own half-thickness (StructureKit.Wall: 1.6 m)
        // The square's far wall, and its two ends back to the line of the yard's walls (14.8 m out). A walled town's square
        // is open to its streets: the wall's round the town (Run.Fortresses.Round).
        if (Plan.Bounds is null)
        {
            yield return Box((sq.S0 + sq.S1) / 2, sq.WallD, (sq.S1 - sq.S0) / 2 + half, half, 9);
            double inner = side * 14.8, mid = (inner + sq.WallD) / 2, across = Math.Abs(sq.WallD - inner) / 2 + half;
            yield return Box(sq.S0, mid, half, across, 9);
            yield return Box(sq.S1, mid, half, across, 9);
        }
        foreach (var b in Plan.Buildings)
            yield return Box(b.S, b.D, b.Length / 2, b.Depth / 2, 9);
        foreach (var f in Plan.Fixtures)
            if (f.SolidS > 0 && f.SolidD > 0)
            {
                // A thing turned to face along the line (a stall against the square's end) is turned a quarter.
                bool turned = Math.Abs(f.FaceS) > Math.Abs(f.FaceD);
                // A heap (slag, coal) is a mound walked up, in layers (note 869), not a block stopping you at its foot.
                if (TownFixtures.Heap(f.Kind))
                    foreach (var (hs, hd, bottom, top) in TownFixtures.HeapLayers(f.SolidS, f.SolidD, f.Height, HeapStep))
                        yield return Box(f.S, f.D, turned ? hd : hs, turned ? hs : hd, top - bottom, up: bottom);
                else
                    yield return Box(f.S, f.D, turned ? f.SolidD : f.SolidS, turned ? f.SolidS : f.SolidD, Math.Max(1, f.Height));
            }
        // Not the people: they go about their rounds (note 353), so a wall of their own would be in a different place on
        // every machine that's stopped somebody to talk.
        foreach (var h in Plan.Houses)
            foreach (var w in HouseWalls(h))
                yield return w;
        // A town type's yard walls (note 692): each half its own ward over the yard.
        foreach (var (s0, d0, s1, _) in Plan.Wards)
            yield return Box((s0 + s1) / 2, d0, (s1 - s0) / 2, Run.Fortresses.WardHalf, Run.Fortresses.WardHeight);
        // A town type's ground (note 692): its terraces' retaining walls, the rails along its deep water's edges.
        foreach (var (s0, d0, s1, d1, bottom, top) in Ground.Edges())
        {
            var t = _line.Sample((s0 + s1) / 2);
            var at = t.Position + Right(t) * ((d0 + d1) / 2);
            var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
            bool acrossLine = Math.Abs(s1 - s0) < 1e-6;
            var axis = acrossLine ? Right(t) : along;
            double run = (acrossLine ? Math.Abs(d1 - d0) : Math.Abs(s1 - s0)) / 2;
            yield return new Wall(at with { Y = 0 }, axis, run + 0.05, EdgeHalf, t.Position.Y + bottom, t.Position.Y + top);
        }
    }

    /// <summary>A retaining wall's or a rail's half-thickness (m): solid to a ball and a body, thin to the eye.</summary>
    const double EdgeHalf = 0.2;

    /// <summary>
    /// A house's walls: a shut one is a box (a burnt one's knee-high), an open one its own walls with the front door and
    /// the doorway through the partition left open, and its solid furniture (<see cref="HouseLayout"/>).
    /// </summary>
    IEnumerable<Wall> HouseWalls(TownHouse h)
    {
        // What stands of it shut: its block (a shut house's), its wing, its enclosed porch (HouseDesign), turned as its body
        // is (note 490); and its yard's fences, sheds, woodpiles and the rest (note 335), square to its lot.
        foreach (var (u0, u1, v0, v1, height) in h.Parts())
        {
            var (s, d) = h.Body((u0 + u1) / 2, (v0 + v1) / 2);
            yield return Box(s, d, (u1 - u0) / 2, (v1 - v0) / 2, height, turn: h.Turn);
        }
        foreach (var y in h.Yard.Where(y => y.Solid))
        {
            var (s, d) = h.Rail((y.U0 + y.U1) / 2, (y.V0 + y.V1) / 2);
            yield return Box(s, d, Math.Abs(y.U1 - y.U0) / 2, Math.Abs(y.V1 - y.V0) / 2, y.Height);
        }
        if (h.Layout is not { } l)
            yield break;
        const double t = HouseLayout.Wall, high = 4;
        double w = h.Width / 2, dp = h.Depth;
        Wall Part(double u0, double u1, double v0, double v1, double height = high)
        {
            var (s, d) = h.Rail((u0 + u1) / 2, (v0 + v1) / 2);
            return Box(s, d, (u1 - u0) / 2, (v1 - v0) / 2, height);
        }
        double door = HouseLayout.DoorWidth / 2, pass = HouseLayout.PassWidth / 2;
        yield return Part(-w, l.DoorU - door, 0, t);
        yield return Part(l.DoorU + door, w, 0, t);
        yield return Part(-w, w, dp - t, dp);
        yield return Part(-w, -w + t, 0, dp);
        yield return Part(w - t, w, 0, dp);
        yield return Part(-t / 2, t / 2, t, l.PassV - pass);
        yield return Part(-t / 2, t / 2, l.PassV + pass, dp - t);
        foreach (var x in l.Things)
            if (x.Solid)
                yield return Part(x.U - x.HalfU, x.U + x.HalfU, x.V - x.HalfV, x.V + x.HalfV, Math.Max(0.5, x.Height));
    }

    /// <summary>How deep a heap's layers are (m): under player.json stepUp (0.35), so a heap is walked up. Not a design number.</summary>
    const double HeapStep = 0.3;

    /// <summary>A box in the rail frame, square to the line (or <paramref name="turn"/> off it, radians: a turned house's
    /// body, note 490): half its length along it and across it, its height.</summary>
    Wall Box(double s, double d, double halfS, double halfD, double height, double up = 0, double turn = 0)
    {
        var t = _line.Sample(s);
        var at = t.Position + Right(t) * d;
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var axis = turn == 0 ? along : (along * DMath.Cos(turn) + Right(t) * DMath.Sin(turn)).Normalized;
        double level = Ground.Level(s, d);
        return new Wall(at with { Y = 0 }, axis, halfS, halfD, at.Y + level + up - 0.5, at.Y + level + up + height);
    }
}
