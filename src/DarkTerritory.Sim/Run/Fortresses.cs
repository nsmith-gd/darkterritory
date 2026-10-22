using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// One fortress along the line (GDD §9: "lights, then walls, then gun towers"): its walls from <see cref="Start"/> to
/// <see cref="End"/>, its gatehouse over the line at <see cref="Gate"/>, the home fortress's platform behind its gate, and
/// the lived-in village between the line and the walls while the town still answers (T100; linegen plan §22.4). A town's
/// fortress (GDD §3.1; note 281) has its <see cref="Square"/>: the wall on that side steps back round it (the town's own
/// walls), and no tower stands in it; its houses are the town's own (Towns.TownHouse), so the village's aren't stood. A
/// walled town (queue #74, note 335) has its <see cref="Bounds"/>: the wall goes round the town, the line through its gate.
/// A terminus's town (notes 600, 692) stands in its own frame from <see cref="Origin"/> along the line, the same way round
/// as when the next night leaves it (<see cref="RailLine.From"/>): its <see cref="Bounds"/> are in that frame, its
/// <see cref="Square"/> in the line's, as every other fort's.
/// </summary>
public readonly record struct Fort(double Start, double End, double Gate, bool Platform, bool Lived, Towns.TownSquare? Square = null,
    Towns.TownBounds? Bounds = null, double? Origin = null)
{
    /// <summary>A town type's ground (note 692): its wall stands on it, from the lower side up past the higher.</summary>
    public Towns.TownGround? Ground { get; init; }

    /// <summary>A point beside the line (along it, out to its right) in the town's own frame: from <see cref="Origin"/> on.</summary>
    public (double Along, double Offset) Local(double along, double offset) => Origin is { } o ? (along - o, offset) : (along, offset);

    /// <summary>Whether a point beside the line is inside the fort's walled town, give or take <paramref name="pad"/>.</summary>
    public bool InTown(double along, double offset, double pad)
    {
        var (s, d) = Local(along, offset);
        return Bounds is { } town && town.Holds(s, d, pad);
    }
}

/// <summary>A lived-in house inside a fortress's walls: its frontage along the line, its depth across, and its ridge.</summary>
public readonly record struct FortHouse(double Width, double Depth, double Ridge);

/// <summary>
/// The fortress gates (note 847; the director's playtest of 16 Oct, line 26: "The gates to the fortress should close behind
/// me"), run.json <c>gates</c>: open while the train's going through, as its engine comes within <see cref="OpenAheadM"/> of
/// one, shut again once its last car's <see cref="ShutBehindM"/> past; the leaves take <see cref="SwingSeconds"/> to swing.
/// </summary>
public sealed record GateTuning(double OpenAheadM, double ShutBehindM, double SwingSeconds);

/// <summary>
/// Where a fortress's walls, gun towers, gatehouse and houses stand (T100; T124 "fort buildings have no collision, and gun
/// shots hit nothing"). The rules are the sim's, so every machine stands the same solids in the same places
/// (<see cref="StopWalls"/>): the art pass draws its pieces where these say (WorldFeatures.Fortress), and the sizes here are
/// the pieces' own (StructureKit.Wall, Tower, Gatehouse; TownKit.LivedHouse).
/// </summary>
public static class Fortresses
{
    /// <summary>How far out from the line the walls stand, both sides.</summary>
    public const double WallOut = 14.8;
    /// <summary>A wall piece's length along the line (one StructureKit.Wall), its half-thickness, and its height to the crenellations' tops.</summary>
    public const double WallBay = 10, WallHalf = 0.8, WallHeight = 8.9;
    /// <summary>The walk along the wall's top, behind its merlons (the art's, StructureKit.Wall), where the watch walks.</summary>
    public const double WallWalk = 8.0;
    /// <summary>A gun tower every 120 m of wall, its half-width over the plinth, and its height to the parapet's top.</summary>
    public const double TowerEvery = 120, TowerHalf = 2.7, TowerHeight = 14.4;
    /// <summary>
    /// A town type's turrets between its gun towers (note 692; the director's renders: a tower every few dozen metres), as
    /// far apart as this at most and no nearer a tower than <see cref="TurretClear"/>: the tower's piece, no gun on it.
    /// </summary>
    public const double TurretEvery = 45, TurretClear = 18;
    /// <summary>The yard's walls in a town type's warren (note 692; TownPlan.Wards): half-thick and high (the art's StructureKit.Wall at 0.7).</summary>
    public const double WardHalf = 0.6, WardHeight = 6.2;
    /// <summary>The gatehouse's two towers, from the line out (each side), their half-depth along it, height; the arch between them over the line.</summary>
    public const double GateInner = 3.5, GateOuter = 8.5, GateHalf = 3.5, GateHeight = 14.9, ArchBottom = 7.8, ArchTop = 13;
    /// <summary>
    /// A gate's iron leaves (note 847; StructureKit.GateLeaf): each hung from the passage's wall by <see cref="LeafHinge"/> out
    /// from the line, half <see cref="LeafHalf"/> thick, the two meeting over the line, up to the arch. In the left one (across
    /// the line, −D) the wicket, standing open from <see cref="WicketLo"/> to <see cref="WicketHi"/> and
    /// <see cref="WicketHeight"/> high: a crewmate left behind outside a shut gate can still come in (run.json forts: "A
    /// crewmate left behind near the gate can run back inside"). Not design numbers: the pieces'.
    /// </summary>
    public const double LeafHinge = 3.25, LeafHalf = 0.08, WicketLo = -1.4, WicketHi = -0.2, WicketHeight = 2.1;
    /// <summary>How far apart the houses inside the walls stand along the line, and how far out from it.</summary>
    public const double HouseEvery = 15, HouseOut = 10.9;
    /// <summary>The deepest a lived-in house is (front to back), so it fits between the line and the wall.</summary>
    public const double LivedDepth = 6;
    /// <summary>The seed of a lived-in house's sizes, by variant (TownKit.LivedHouse draws its look on from the same).</summary>
    public const int HouseSeed = 7001;

    /// <summary>
    /// The home platform's bays (StructureKit.PlatformBay; note 869): one every <see cref="PlatformBay"/> m along the line from
    /// <see cref="PlatformFrom"/> past the yard's start to <see cref="PlatformShort"/> short of its gate, right of the line. Its
    /// deck from <see cref="PlatformEdge"/> out to <see cref="PlatformOut"/>, <see cref="PlatformDeck"/> up; at each bay's start
    /// its canopy's two posts, <see cref="PostOut"/> out, <see cref="PostHalf"/> square and <see cref="PostTop"/> tall. Not
    /// design numbers: the kit's.
    /// </summary>
    public const double PlatformBay = 8, PlatformFrom = 60, PlatformShort = 30, PlatformEdge = 3.6, PlatformOut = 10, PlatformDeck = 0.25;
    public const double PostHalf = 0.13, PostTop = 4.2;
    public static readonly double[] PostOut = [5.2, 9.2];

    /// <summary>
    /// What stands on a platform bay of <paramref name="variant"/> (every third bay the same, by where it is: (s / 8) % 3), in
    /// the bay's frame (x out from the line, z up the line from the bay's start, negative): the bench (0) or the crates (1), each
    /// (x0, z0, x1, z1, top); none on the third. StructureKit.PlatformBay draws them from these.
    /// </summary>
    public static IReadOnlyList<(double X0, double Z0, double X1, double Z1, double Top)> OnThePlatformBay(int variant) => (variant % 3) switch
    {
        0 => [(8.6, -5.5, 9.1, -3.5, 1.2)],
        1 => [(7.5, -3.0, 8.1, -2.4, 1.35), (8.2, -3.1, 8.8, -2.5, 0.85)],
        _ => [],
    };

    /// <summary>The platform's bays' starts along the line, for a fortress from <paramref name="start"/> with its gate at <paramref name="gateAt"/>.</summary>
    public static IEnumerable<double> PlatformBays(double start, double gateAt)
    {
        double p0 = start + PlatformFrom, p1 = gateAt - PlatformShort;
        for (double s = Math.Floor(p0 / PlatformBay) * PlatformBay; s < p1; s += PlatformBay)
            yield return s;
    }

    /// <summary>
    /// The line's fortresses: the home fortress's yard (the gate at its end, <paramref name="yardLength"/>), and the terminus's
    /// from its gate to the end of the line (the plan's gate, or <paramref name="terminusZone"/> and 200 m short of the end on a
    /// hand-laid route), dark and empty when the terminus has stopped answering.
    /// </summary>
    public static IReadOnlyList<Fort> Of(Route.Route route, RailLine line, double yardLength, double terminusZone)
    {
        double home = route.Plan?.Terminus.GateM ?? line.Length - terminusZone - 200;
        return
        [
            new Fort(0, yardLength, yardLength, Platform: true, Lived: true),
            new Fort(home, line.Length, home, Platform: false, Lived: route.Plan?.Terminus.Silent != true),
        ];
    }

    /// <summary>Along a fortress's line, clear of its gate: where its houses stand.</summary>
    public static bool InTheVillage(double s, double start, double end, double gateAt) =>
        s > start + 25 && s < end - 25 && Math.Abs(s - gateAt) > 30;

    /// <summary>The home fortress's platform, right of the line behind its gate, which no house stands on.</summary>
    public static bool OnThePlatform(double s, int side, double start, double gateAt, bool platform) =>
        platform && side > 0 && s > start + 50 && s < gateAt - 20;

    /// <summary>A town's square (note 281) on <paramref name="side"/> at <paramref name="s"/>, give or take <paramref name="pad"/>.</summary>
    public static bool InTheSquare(Towns.TownSquare? square, double s, int side, double pad) =>
        square is { } sq && sq.Side == side && s > sq.S0 - pad && s < sq.S1 + pad;

    /// <summary>
    /// Which house stands at <paramref name="s"/> (a multiple of <see cref="HouseEvery"/>) on <paramref name="side"/>, or
    /// null for a gap: one in five left empty, none on the platform, by the gate, or in a town's <paramref name="square"/>.
    /// </summary>
    public static int? HouseAt(double s, int side, double start, double end, double gateAt, bool platform, Towns.TownSquare? square = null)
    {
        if (!InTheVillage(s, start, end, gateAt) || InTheSquare(square, s, side, 5))
            return null;
        int h = (int)(s / HouseEvery) * 7 + (side > 0 ? 3 : 0);
        if (h % 5 == 0 || OnThePlatform(s, side, start, gateAt, platform))
            return null;
        return h % 6;
    }

    /// <summary>A lived-in house's frontage and depth, the first draws of its own seed (the art draws the rest of its look on from them).</summary>
    public static (double Width, double Depth) LivedSize(Random rng) =>
        (5 + rng.NextDouble() * 3, 4.5 + rng.NextDouble() * (LivedDepth - 4.5));

    /// <summary>
    /// A lived-in house by variant: its size, and its ridge (the eaves one storey up or two, as TownKit.House draws next from
    /// the same seed, and the gable's rise over them). A seeded draw, so alike on every machine.
    /// </summary>
    public static FortHouse House(int variant)
    {
        var rng = new Random(HouseSeed + variant);
        var (w, d) = LivedSize(rng);
        double eaves = rng.Next(2) == 0 ? 3.2 : 5.8;
        return new FortHouse(w, d, eaves + w * 0.45);
    }

    /// <summary>
    /// Note 847: whether the gate <paramref name="gate"/> along the main line stands open for the train, from its
    /// <paramref name="front"/> and <paramref name="rear"/> along it: from its engine coming up to it until its last car's
    /// through. Every other time it's shut: before the night's off, behind the train once it's through, and a town's other
    /// gate (the way in to the departure, the way on out of the arrival), which no train comes to tonight.
    /// </summary>
    public static bool Open(double gate, double front, double rear, GateTuning t) =>
        front >= gate - t.OpenAheadM && rear < gate + t.ShutBehindM;

    // A gate's key from where it stands along the main line, to the quarter metre (a departure town's way in is behind the
    // line's start, so offset), and back. Never 0.
    const int GateKeyOffset = 1 << 20;

    /// <summary>Note 847: the key of the gate <paramref name="along"/> the main line (its leaves' <see cref="Wall.Gate"/>).</summary>
    public static int GateKey(double along) => (int)Math.Round(along * 4) + GateKeyOffset;

    /// <summary>Note 847: where along the main line the gate of <paramref name="key"/> stands.</summary>
    public static double GateAlong(int key) => (key - GateKeyOffset) / 4.0;

    /// <summary>
    /// Note 847: a shut gate's two leaves, across the passage at the gate from either wall to the other, less the wicket: as
    /// spans across the line (from, to) for <paramref name="place"/> to stand.
    /// </summary>
    static IEnumerable<Wall> Leaves(Func<double, double, Wall> place)
    {
        foreach (var (lo, hi) in new[] { (-GateInner, WicketLo), (WicketHi, GateInner) })
            yield return place((lo + hi) / 2, (hi - lo) / 2);
    }

    /// <summary>
    /// Every solid a fortress stands (each an upright box turned to the line where it stands): the walls a bay at a time,
    /// the gun towers on them, the gatehouse's towers and arch, the gate's leaves (note 847: standing only while it's shut,
    /// <see cref="StopWalls.OpenGates"/>), and the village's houses. A walled town's two gatehouses are its wall's (<see cref="Round"/>).
    /// </summary>
    public static IEnumerable<Wall> Solids(Fort fort, RailLine line)
    {
        double a = Math.Max(0, fort.Start), b = Math.Min(line.Length, fort.End);
        if (a >= b)
            yield break;
        // Upright, standing on the rail's height where it is (the art's pieces are set there), a few metres into the ground.
        Wall At(double s, double across, double halfLength, double halfWidth, double bottom, double top)
        {
            var t = line.Sample(Math.Clamp(s, 0, line.Length));
            var tangent = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
            var right = Double3.Cross(tangent, Double3.Up).Normalized;
            var at = t.Position + right * across;
            return new Wall(at with { Y = 0 }, tangent, halfLength, halfWidth, t.Position.Y + bottom, t.Position.Y + top);
        }
        if (fort.Bounds is { } town)
        {
            foreach (var w in Round(town, Anchor(line, fort.Origin ?? 0, town.Rear), fort.Ground, fort.Origin ?? 0))
                yield return w;
        }
        else
        {
            foreach (int side in new[] { -1, 1 })
            {
                // A wall piece runs from where it's set a bay up the line (its kit runs to −Z, which is up the line). On a town's
                // square's side, the bays stop at the square and start again past it, cut short where it does (WorldArt.WallRun).
                for (double s = Math.Floor(a / WallBay) * WallBay; s < b; s += WallBay)
                {
                    if (fort.Square is { } sq && sq.Side == side && s + WallBay > sq.S0 && s < sq.S1)
                    {
                        foreach (var (lo, hi) in new[] { (s, Math.Min(s + WallBay, sq.S0)), (Math.Max(s, sq.S1), s + WallBay) })
                            if (hi - lo >= 0.05)
                                yield return At((lo + hi) / 2, side * WallOut, (hi - lo) / 2 + 0.05, WallHalf, -3, WallHeight);
                        continue;
                    }
                    yield return At(s + WallBay / 2, side * WallOut, WallBay / 2 + 0.05, WallHalf, -3, WallHeight);
                }
                for (double s = Math.Ceiling(a / TowerEvery) * TowerEvery; s < b; s += TowerEvery)
                    if (!InTheSquare(fort.Square, s, side, 3))
                        yield return At(s, side * WallOut, TowerHalf, TowerHalf, -3, TowerHeight);
                // A town's houses are its own (Towns.Town's walls, note 281), not this village's.
                if (!fort.Lived || fort.Square is not null)
                    continue;
                for (double s = Math.Ceiling(a / HouseEvery) * HouseEvery; s < b; s += HouseEvery)
                    if (HouseAt(s, side, fort.Start, fort.End, fort.Gate, fort.Platform, fort.Square) is { } v)
                    {
                        // Its front to the line: its frontage along it, its depth across.
                        var house = House(v);
                        yield return At(s, side * HouseOut, house.Width / 2, house.Depth / 2, -3, house.Ridge);
                    }
            }
        }
        // The home platform (note 869; the director's playtest of 19 Oct line 2, "Collision boxes are off"): its deck, stepped
        // up onto, its canopy's posts and what stands on it, solid where StructureKit.PlatformBay draws them, not walked through.
        if (fort.Platform)
            foreach (double s in PlatformBays(fort.Start, fort.Gate))
            {
                if (s < a || s + PlatformBay > b)
                    continue;
                double deck = (PlatformEdge + PlatformOut) / 2;
                yield return At(s + PlatformBay / 2, deck, PlatformBay / 2, (PlatformOut - PlatformEdge) / 2, -3, PlatformDeck);
                foreach (double x in PostOut)
                    yield return At(s, x, PostHalf, PostHalf, -3, PostTop);
                foreach (var (x0, z0, x1, z1, top) in OnThePlatformBay((int)(s / PlatformBay)))
                    yield return At(s - (z0 + z1) / 2, (x0 + x1) / 2, (z1 - z0) / 2, (x1 - x0) / 2, -3, top);
            }
        if (fort.Bounds is null && fort.Gate >= 0 && fort.Gate <= line.Length)
        {
            double mid = (GateInner + GateOuter) / 2, half = (GateOuter - GateInner) / 2;
            foreach (int side in new[] { -1, 1 })
                yield return At(fort.Gate, side * mid, GateHalf, half, -3, GateHeight);
            // The arch over the line, clear above anyone on a car's roof.
            yield return At(fort.Gate, 0, GateHalf, GateInner, ArchBottom, ArchTop);
            // Its leaves, up to the arch (note 847).
            int key = GateKey(fort.Gate);
            foreach (var leaf in Leaves((d, h) => At(fort.Gate, d, LeafHalf, h, -3, ArchBottom) with { Gate = key }))
                yield return leaf;
        }
    }

    /// <summary>
    /// A walled town's wall (queue #74, note 335): a bay at a time down each side from the rear wall to the gate, the front
    /// wall out from the gatehouse's towers to each corner, the rear wall across the line behind the yard's start (its gate
    /// shut), a tower at each corner and every <see cref="TowerEvery"/> down the sides. Each an upright box on the rail's
    /// height at the yard's start, which is level (linegen's fortress segment) and straight.
    /// </summary>
    public static IEnumerable<Wall> Round(Towns.TownBounds town, RailLine line) => Round(town, Anchor(line, 0, town.Rear));

    /// <summary>
    /// A walled town's straight frame (note 692; <see cref="Towns.Town.Anchor"/>): the rail sample at its frame's 0 (main
    /// <paramref name="origin"/>) on the straight through its way in (<paramref name="rear"/> along its frame).
    /// </summary>
    public static TrackSample Anchor(RailLine line, double origin, double rear)
    {
        double entry = Math.Max(0, rear);
        var t = line.Sample(Math.Clamp(origin + entry, 0, line.Length));
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        return t with { Position = t.Position - along * entry };
    }

    /// <summary>
    /// A walled town's wall (<see cref="Round(Towns.TownBounds, RailLine)"/>) from its frame's start, <paramref name="start"/>,
    /// which is <paramref name="origin"/> along the main line (a terminus's town, note 600; its gates' keys are the main line's).
    /// </summary>
    public static IEnumerable<Wall> Round(Towns.TownBounds town, TrackSample start, Towns.TownGround? ground = null, double origin = 0)
    {
        var tangent = new Double3(start.Tangent.X, 0, start.Tangent.Z).Normalized;
        var right = Double3.Cross(tangent, Double3.Up).Normalized;
        double y = start.Position.Y;
        // A point in the rail frame (the yard is straight, so behind its start too).
        Double3 P(double s, double d) => (start.Position + tangent * s + right * d) with { Y = 0 };
        // Where a town type's wall stands on its ground (note 692): from the lower of the ground either side of it, up to its
        // height over the higher (a wall on a terrace's edge or a cliff's).
        var (low, high) = Footing(town, ground);
        Wall Tower(double s, double d) => new(P(s, d), tangent, TowerHalf, TowerHalf, y + low(s, d) - 3, y + high(s, d) + TowerHeight);
        // Each run of wall a bay at a time, along its own way (a town type's wall goes round its outline, note 692).
        foreach (var (a, b) in Runs(town))
        {
            double es = b.S - a.S, ed = b.D - a.D, len = Math.Sqrt(es * es + ed * ed);
            if (len < 0.05)
                continue;
            var axis = (tangent * (es / len) + right * (ed / len)).Normalized;
            for (double x = 0; x < len; x += WallBay)
            {
                double x1 = Math.Min(len, x + WallBay), m = (x + x1) / 2 / len, ms = a.S + es * m, md = a.D + ed * m;
                yield return new Wall(P(ms, md), axis, (x1 - x) / 2 + 0.05, WallHalf, y + low(ms, md) - 3, y + high(ms, md) + WallHeight);
            }
        }
        foreach (var (s, d) in Towers(town).Concat(Turrets(town)))
            yield return Tower(s, d);
        // Its two gatehouses (note 692): at the way in from the last night and the way out to the next, each its two towers
        // either side of the line, the arch over it and its leaves up to the arch (note 847), keyed where it stands along the
        // main line.
        double mid = (GateInner + GateOuter) / 2, half = (GateOuter - GateInner) / 2;
        foreach (double at in new[] { town.Rear, town.Gate })
        {
            foreach (int side in new[] { -1, 1 })
                yield return new Wall(P(at, side * mid), tangent, GateHalf, half, y - 3, y + GateHeight);
            yield return new Wall(P(at, 0), tangent, GateHalf, GateInner, y + ArchBottom, y + ArchTop);
            int key = GateKey(origin + at);
            foreach (var leaf in Leaves((d, h) => new Wall(P(at, d), tangent, LeafHalf, h, y - 3, y + ArchBottom) { Gate = key }))
                yield return leaf;
        }
    }

    /// <summary>
    /// The lower and the higher of a town type's ground either side of its wall at a point on it (note 692), m over the yard:
    /// a step in from the wall toward the town's middle, and out from it. Zero both for a town on the yard's level.
    /// </summary>
    public static (Func<double, double, double> Low, Func<double, double, double> High) Footing(Towns.TownBounds town, Towns.TownGround? ground)
    {
        if (ground is null || ground.Flat)
            return ((_, _) => 0, (_, _) => 0);
        var o = Outline(town);
        double cs = o.Average(p => p.S), cd = o.Average(p => p.D);
        (double In, double Out) Sides(double s, double d)
        {
            double vs = cs - s, vd = cd - d, len = Math.Max(1e-6, Math.Sqrt(vs * vs + vd * vd));
            (vs, vd) = (vs / len, vd / len);
            const double step = 2.5;
            double inside = ground.Level(s + vs * step, d + vd * step), outside = ground.Bed(s - vs * step, d - vd * step);
            return (inside, outside);
        }
        return ((s, d) => Sides(s, d) is var x ? Math.Min(x.In, x.Out) : 0, (s, d) => Sides(s, d) is var x ? Math.Max(x.In, x.Out) : 0);
    }

    /// <summary>
    /// A walled town's wall as runs in its rail frame (along, across): round its outline (a town type's, note 692) or its
    /// rectangle, open where it crosses the line at its two gates, the way in from the last night (<see cref="Towns.TownBounds.Rear"/>)
    /// and the way out to the next (<see cref="Towns.TownBounds.Gate"/>), between the gatehouses' outer faces.
    /// </summary>
    public static IEnumerable<((double S, double D) A, (double S, double D) B)> Runs(Towns.TownBounds town)
    {
        var o = Outline(town);
        for (int i = 0; i < o.Count; i++)
        {
            var (a, b) = (o[i], o[(i + 1) % o.Count]);
            // A run across the line is open between the gatehouse's towers.
            if (a.D < 0 != b.D < 0 && Math.Abs(b.D - a.D) > 1e-9)
            {
                double k0 = (-GateOuter - a.D) / (b.D - a.D), k1 = (GateOuter - a.D) / (b.D - a.D);
                (k0, k1) = (Math.Min(k0, k1), Math.Max(k0, k1));
                (double, double) At(double k) => (a.S + (b.S - a.S) * k, a.D + (b.D - a.D) * k);
                if (k0 > 0)
                    foreach (var piece in Open(town, a, At(k0)))
                        yield return piece;
                if (k1 < 1)
                    foreach (var piece in Open(town, At(k1), b))
                        yield return piece;
                continue;
            }
            foreach (var piece in Open(town, a, b))
                yield return piece;
        }
    }

    /// <summary>A run of wall less the stretches of it left open (<see cref="Towns.TownBounds.Gaps"/>: a harbour's mouth, note 692).</summary>
    static IEnumerable<((double S, double D) A, (double S, double D) B)> Open(Towns.TownBounds town, (double S, double D) a, (double S, double D) b)
    {
        double es = b.S - a.S, ed = b.D - a.D, len2 = es * es + ed * ed;
        var cuts = new List<(double K0, double K1)>();
        foreach (var (g0, g1) in town.Gaps)
        {
            // A gap on this run: both its ends on the run's line, within it.
            double Along((double S, double D) p) => len2 < 1e-9 ? 0 : ((p.S - a.S) * es + (p.D - a.D) * ed) / len2;
            double Off((double S, double D) p) => len2 < 1e-9 ? double.MaxValue : Math.Abs((p.S - a.S) * ed - (p.D - a.D) * es) / Math.Sqrt(len2);
            double k0 = Along(g0), k1 = Along(g1);
            if (Off(g0) > 0.5 || Off(g1) > 0.5 || Math.Max(k0, k1) <= 0 || Math.Min(k0, k1) >= 1)
                continue;
            cuts.Add((Math.Clamp(Math.Min(k0, k1), 0, 1), Math.Clamp(Math.Max(k0, k1), 0, 1)));
        }
        double at = 0;
        (double, double) P(double k) => (a.S + es * k, a.D + ed * k);
        foreach (var (k0, k1) in cuts.OrderBy(c => c.K0))
        {
            if (k0 > at)
                yield return (P(at), P(k0));
            at = Math.Max(at, k1);
        }
        if (at < 1)
            yield return (P(at), b);
    }

    /// <summary>A walled town's towers (rail frame): one at each corner of its wall, and every <see cref="TowerEvery"/> along its runs.</summary>
    public static IEnumerable<(double S, double D)> Towers(Towns.TownBounds town)
    {
        var o = Outline(town);
        for (int i = 0; i < o.Count; i++)
        {
            var (a, b) = (o[i], o[(i + 1) % o.Count]);
            yield return a;
            double len = Math.Sqrt((b.S - a.S) * (b.S - a.S) + (b.D - a.D) * (b.D - a.D));
            // Not on a run across the line (the gatehouses are its towers).
            if (a.D < 0 != b.D < 0)
                continue;
            int n = (int)Math.Floor(len / TowerEvery + 0.5);
            for (int k = 1; k < n; k++)
                yield return (a.S + (b.S - a.S) * k / n, a.D + (b.D - a.D) * k / n);
        }
    }

    /// <summary>
    /// A town type's turrets (rail frame; note 692): along each run of its wall between its towers, every
    /// <see cref="TurretEvery"/> at most, none nearer a tower or a gatehouse than <see cref="TurretClear"/>. The street town has none.
    /// </summary>
    public static IEnumerable<(double S, double D)> Turrets(Towns.TownBounds town)
    {
        if (town.Outline is not { Count: > 2 })
            yield break;
        // Either side of an opening (a harbour's mouth), its turrets.
        foreach (var (g0, g1) in town.Gaps)
        {
            yield return g0;
            yield return g1;
        }
        var towers = Towers(town).ToList();
        foreach (var (a, b) in Runs(town))
        {
            double len = Math.Sqrt((b.S - a.S) * (b.S - a.S) + (b.D - a.D) * (b.D - a.D));
            int n = (int)Math.Ceiling(len / TurretEvery);
            for (int k = 1; k < n; k++)
            {
                var p = (S: a.S + (b.S - a.S) * k / n, D: a.D + (b.D - a.D) * k / n);
                bool clear = towers.All(t => (t.S - p.S) * (t.S - p.S) + (t.D - p.D) * (t.D - p.D) >= TurretClear * TurretClear)
                    && Math.Abs(p.D) > GateOuter + TurretClear * 0.5;
                if (clear)
                    yield return p;
            }
        }
    }

    /// <summary>The wall's corners in order round it: a town type's outline, or the street town's rectangle (note 335).</summary>
    static IReadOnlyList<(double S, double D)> Outline(Towns.TownBounds town) => town.Outline is { Count: > 2 } o ? o :
        [(town.Rear, -town.Left), (town.Gate, -town.Left), (town.Gate, town.Right), (town.Rear, town.Right)];
}
