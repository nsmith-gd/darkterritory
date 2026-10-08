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
/// </summary>
public readonly record struct Fort(double Start, double End, double Gate, bool Platform, bool Lived, Towns.TownSquare? Square = null,
    Towns.TownBounds? Bounds = null);

/// <summary>A lived-in house inside a fortress's walls: its frontage along the line, its depth across, and its ridge.</summary>
public readonly record struct FortHouse(double Width, double Depth, double Ridge);

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
    /// <summary>A gun tower every 120 m of wall, its half-width over the plinth, and its height to the parapet's top.</summary>
    public const double TowerEvery = 120, TowerHalf = 2.7, TowerHeight = 14.4;
    /// <summary>The gatehouse's two towers, from the line out (each side), their half-depth along it, height; the arch between them over the line.</summary>
    public const double GateInner = 3.5, GateOuter = 8.5, GateHalf = 3.5, GateHeight = 14.9, ArchBottom = 7.8, ArchTop = 13;
    /// <summary>How far apart the houses inside the walls stand along the line, and how far out from it.</summary>
    public const double HouseEvery = 15, HouseOut = 10.9;
    /// <summary>The deepest a lived-in house is (front to back), so it fits between the line and the wall.</summary>
    public const double LivedDepth = 6;
    /// <summary>The seed of a lived-in house's sizes, by variant (TownKit.LivedHouse draws its look on from the same).</summary>
    public const int HouseSeed = 7001;

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
    /// Every solid a fortress stands (each an upright box turned to the line where it stands): the walls a bay at a time,
    /// the gun towers on them, the gatehouse's towers and arch, and the village's houses.
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
            foreach (var w in Round(town, line))
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
        if (fort.Gate >= 0 && fort.Gate <= line.Length)
        {
            double mid = (GateInner + GateOuter) / 2, half = (GateOuter - GateInner) / 2;
            foreach (int side in new[] { -1, 1 })
                yield return At(fort.Gate, side * mid, GateHalf, half, -3, GateHeight);
            // The arch over the line, clear above anyone on a car's roof.
            yield return At(fort.Gate, 0, GateHalf, GateInner, ArchBottom, ArchTop);
        }
    }

    /// <summary>
    /// A walled town's wall (queue #74, note 335): a bay at a time down each side from the rear wall to the gate, the front
    /// wall out from the gatehouse's towers to each corner, the rear wall across the line behind the yard's start (its gate
    /// shut), a tower at each corner and every <see cref="TowerEvery"/> down the sides. Each an upright box on the rail's
    /// height at the yard's start, which is level (linegen's fortress segment) and straight.
    /// </summary>
    public static IEnumerable<Wall> Round(Towns.TownBounds town, RailLine line)
    {
        var start = line.Sample(0);
        var tangent = new Double3(start.Tangent.X, 0, start.Tangent.Z).Normalized;
        var right = Double3.Cross(tangent, Double3.Up).Normalized;
        double y = start.Position.Y;
        // A point in the rail frame (the yard is straight, so behind its start too).
        Double3 P(double s, double d) => (start.Position + tangent * s + right * d) with { Y = 0 };
        Wall Along(double s0, double s1, double d) => new(P((s0 + s1) / 2, d), tangent, (s1 - s0) / 2 + 0.05, WallHalf, y - 3, y + WallHeight);
        Wall Across(double s, double d0, double d1) => new(P(s, (d0 + d1) / 2), right, (d1 - d0) / 2 + 0.05, WallHalf, y - 3, y + WallHeight);
        Wall Tower(double s, double d) => new(P(s, d), tangent, TowerHalf, TowerHalf, y - 3, y + TowerHeight);
        foreach (var (d, sd) in new[] { (-town.Left, -1), (town.Right, 1) })
        {
            for (double s = town.Rear; s < town.Gate; s += WallBay)
                yield return Along(s, Math.Min(s + WallBay, town.Gate), d);
            foreach (double s in new[] { town.Rear, town.Gate })
                yield return Tower(s, d);
            for (double s = town.Rear + TowerEvery; s < town.Gate - TowerEvery / 2; s += TowerEvery)
                yield return Tower(s, d);
            // The front wall from the gatehouse's tower out to the corner.
            double inner = sd * GateOuter;
            yield return Across(town.Gate, Math.Min(inner, d), Math.Max(inner, d));
        }
        // The rear wall, straight across, the line's way out the back shut.
        yield return Across(town.Rear, -town.Left, town.Right);
    }
}
