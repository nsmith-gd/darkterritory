using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Explorable village interiors (GDD App. F.3, the director, 7 Oct: "the villages don't have explorable interiors ...
/// things that we can search through in order to find things"; ARCHITECTURE §8 note 326): the plain houses stand open,
/// you walk in at the door in the face toward the line, and the finds are inside. Every house: an L or a cross is one floor
/// in its outline's walls, a pair two cottages, a door each.
/// </summary>
[Collection(nameof(LineGenTests))]
public class OpenHouseTests
{
    static readonly string Content = DataFile.FindContentRoot();
    const double Crewmate = 0.3;

    /// <summary>
    /// Whether a crewmate can't stand at a point, walking on a floor <paramref name="up"/> over its ground: a wall's in the way
    /// between a step up off that floor and a head's height over it (a house of floors' slabs, steps and lintels aren't, note 1005).
    /// </summary>
    static bool Blocked(StopWalls walls, Double3 p, double up = 0) =>
        walls.Near(p).Any(w => Math.Abs(w.ToLocal(p).X) <= w.HalfLength + Crewmate && Math.Abs(w.ToLocal(p).Z) <= w.HalfWidth + Crewmate
            && w.Top > p.Y + up + 0.36 && w.Bottom < p.Y + up + 1.8);

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:7")]
    public void YouWalkInAtTheDoorAndTheFindsAreInside(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        int open = 0, composite = 0, pairs = 0, inside = 0, furniture = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                // Every village house stands open, whatever its shape; and the barns, outbuildings and goods sheds (note 417), and
                // a dead town's station (note 493), and a yard's powerhouse (note 509).
                Assert.Equal(b.Kind is BuildingKind.House or BuildingKind.Barn or BuildingKind.Outbuilding or BuildingKind.GoodsShed or BuildingKind.Station
                    or BuildingKind.Powerhouse, b.Open);
                if (!b.Open || !StopWalls.Walled(stop, i))
                    continue;
                open++;
                composite += StopWalls.Composite(b) ? 1 : 0;
                var doors = StopWalls.Doorways(b).ToList();
                // A unit a door: a pair of cottages has two.
                Assert.Equal(b.Shape == HouseShape.Pair ? 2 : 1, doors.Count);
                // And a house of floors a back door besides (note 1005).
                if (StopWalls.Storeyed(b) && StopWalls.BackDoor(b) is var (bx, by, bnx, bny))
                    doors.Add(((bx + bnx * 0.8, by + bny * 0.8), (bx - bnx * 1.2, by - bny * 1.2)));
                pairs += b.Shape == HouseShape.Pair ? 1 : 0;
                var walk = Reach(walls, line, f, b);
                // In at each door, from its step.
                foreach (var (o, n) in doors)
                {
                    Assert.True(walk.Reached(o.X, o.Y) && walk.Reached(n.X, n.Y), $"{spec} at {f.Start:0}: house {i}'s door is shut ({b.Shape})");
                    Assert.True(StopWalls.InParts(b, n.X, n.Y) && !StopWalls.InParts(b, o.X, o.Y), $"{spec} at {f.Start:0}: house {i}'s door isn't in its wall");
                }
                // And nowhere else: anywhere a crewmate goes from outside the house to inside it, it's at a door.
                foreach (var (x, y) in walk.Crossings)
                    Assert.True(doors.Any(d => Math.Abs(x - (d.Out.X + d.In.X) / 2) < 1.5 && Math.Abs(y - (d.Out.Y + d.In.Y) / 2) < 1.5),
                        $"{spec} at {f.Start:0}: house {i} ({b.Shape}) has a way in at ({x:0.0}, {y:0.0})");
                // (What's kept in the basement or upstairs is StoreyedHouseTests'.)
                foreach (var c in stop.Containers.Where(c => c.Building == i && c.Level == 0))
                {
                    // Inside, and a crewmate gets to just in front of it (within a search's reach, loot.json search.reach), round
                    // the furniture; a cupboard or a cabinet is solid.
                    inside++;
                    var (ix, iy) = StopWalls.InsideLocal(b, c);
                    var (kx, ky, fx, fy) = StopWalls.Kept(b, c);
                    Assert.True(StopWalls.InParts(b, ix, iy) && StopWalls.InParts(b, kx, ky), $"{spec} at {f.Start:0}: a {c.Kind} outside its house {i} ({b.Shape})");
                    Assert.True(walk.Reached(ix + fx * 0.45, iy + fy * 0.45), $"{spec} at {f.Start:0}: a {c.Kind} in house {i} ({b.Shape}) can't be got to");
                    if (c.Kind is ContainerKind.Cupboard or ContainerKind.Cabinet)
                    {
                        furniture++;
                        Assert.False(walk.Reached(kx, ky), $"{spec} at {f.Start:0}: walked through a {c.Kind}");
                    }
                }
            }
        }
        Assert.True(open > 0 && composite > 0 && pairs > 0 && inside > 0 && furniture > 0,
            $"{spec}: {open} open houses ({composite} of parts, {pairs} pairs), {inside} finds inside, {furniture} cupboards and cabinets");
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:7")]
    [InlineData("local:4")]
    [InlineData("deepTerritory:5")]
    public void TheBiggerHousesHaveABackRoomThroughADoorway(string spec)
    {
        // An L's wing and a long plain house's far end are rooms of their own (note 326's rooms): a wall between, stood
        // solid, with a doorway in it that's kept clear; you get into every room; the furniture never stands in another piece.
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        int ls = 0, plain = 0, one = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (!b.Open || !StopWalls.Walled(stop, i))
                    continue;
                int index = i;
                var kept = stop.Containers.Where(c => c.Building == index).ToList();
                var pieces = StopWalls.Furniture(b, kept).ToList();
                for (int p = 0; p < pieces.Count; p++)
                    for (int q = p + 1; q < pieces.Count; q++)
                        Assert.False(Math.Abs(pieces[p].X - pieces[q].X) < pieces[p].HalfX + pieces[q].HalfX && Math.Abs(pieces[p].Y - pieces[q].Y) < pieces[p].HalfY + pieces[q].HalfY,
                            $"{spec} at {f.Start:0}: house {i}'s {pieces[p].Kind} and {pieces[q].Kind} stand in each other ({b.Shape})");
                if (StopWalls.Partition(b) is not { } w)
                {
                    // Only a long plain house or an L is cut into rooms (a house of floors has its floors for rooms, note 1005).
                    Assert.False(b.Shape == HouseShape.L || b.Shape is HouseShape.Rect or HouseShape.Square && b.Length >= StopWalls.RoomsFrom && !StopWalls.Storeyed(b),
                        $"house {i} ({b.Shape}, {b.Length:0.0} m) is one room");
                    one++;
                    continue;
                }
                ls += b.Shape == HouseShape.L ? 1 : 0;
                plain += b.Shape == HouseShape.L ? 0 : 1;
                var walk = Reach(walls, line, f, b);
                // Through the doorway, both sides.
                foreach (var (a, c) in StopWalls.InnerDoorways(b))
                    Assert.True(walk.Reached(a.X, a.Y) && walk.Reached(c.X, c.Y), $"{spec} at {f.Start:0}: house {i}'s inner doorway is shut ({b.Shape})");
                // The wall's solid away from its doorway.
                var o = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 0));
                foreach (double along in new[] { (w.A + w.Door - StopWalls.DoorWidth / 2) / 2, (w.Door + StopWalls.DoorWidth / 2 + w.B) / 2 })
                {
                    var (x, y) = w.Point(along);
                    Assert.True(Blocked(walls, Run.Run.StopWorld(line, f, StopWalls.InHouse(b, x, y))), $"{spec} at {f.Start:0}: house {i}'s inner wall isn't there ({b.Shape})");
                }
                // Every room's floor is got to (somewhere in each of its cells).
                foreach (var (x0, y0, x1, y1) in StopWalls.Outline(b).Cells)
                {
                    bool any = false;
                    for (double x = x0 + 0.5; x < x1 - 0.4 && !any; x += 0.2)
                        for (double y = y0 + 0.5; y < y1 - 0.4 && !any; y += 0.2)
                            any = walk.Reached(x, y);
                    Assert.True(any, $"{spec} at {f.Start:0}: house {i} ({b.Shape}) has a room nobody gets into");
                }
            }
        }
        // (Most long plain houses are built with floors now, note 1005, and have none: an L always has its wing.)
        Assert.True(ls > 0 && one > 0, $"{spec}: {ls} Ls and {plain} plain houses with a back room, {one} of one room");
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:7")]
    public void SmallThingsLieOnTheFloorsAndAreTakenAtOnce(string spec)
    {
        // Note 738, the director's 14 Oct playtest: "I never find anything in some of these abandoned buildings ... smaller
        // bits of loot"; "I don't actually ever find loot like on the floor or anything. It's not really rewarding me for
        // being attentive." Every village has something lying on a floor: in a corner of a room, inside its walls and clear
        // of every doorway, not searched for, and small (less than what's kept in a cupboard).
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        var loot = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
        double perCar = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)).Economy.PerCar[StopLoot.TierKey(route.Tier)];
        Assert.Null(loot.Search!.Of(ContainerKind.Loose));
        int villages = 0, lying = 0;
        for (int i = 0; i < route.Features.Count; i++)
        {
            if (route.Features[i].Stop is not { HasVillage: true } stop)
                continue;
            bool open = stop.Buildings.Any(b => b is { Kind: BuildingKind.House, Open: true });
            var loose = stop.Containers.Where(c => c.Kind == ContainerKind.Loose).ToList();
            if (open)
            {
                villages++;
                Assert.NotEmpty(loose);
            }
            var finds = StopLoot.Village(loot, stop, route.Seed, i, perCar);
            // Smaller bits (note 738), with every kind worth what it is (note 718): what lies on a floor is worth no more than the
            // middle of what a village's cupboards, cellars and boards can keep.
            var keptWorths = loot.Kinds.Where(k => k.Key != "loose").SelectMany(k => k.Value.Items).Distinct()
                .Select(item => loot.WorthOf(item, perCar)).Order().ToList();
            double kept = keptWorths[keptWorths.Count / 2];
            foreach (var c in loose)
            {
                lying++;
                var b = stop.Buildings[c.Building];
                Assert.True(b is { Kind: BuildingKind.House, Open: true });
                var (x, y) = StopWalls.InsideLocal(b, c);
                // Inside its walls (where the walls are, a crewmate can't stand), and a metre and more from any way in.
                var at = Run.Run.StopWorld(line, route.Features[i], StopWalls.InHouse(b, x, y));
                Assert.False(Blocked(walls, at, StopWalls.Storeyed(b) ? StopWalls.FloorAt(b, 0) : 0), $"{spec}: a find in house {c.Building}'s wall ({b.Shape})");
                foreach (var (_, way) in StopWalls.Doorways(b))
                    Assert.True(Math.Sqrt((x - way.X) * (x - way.X) + (y - way.Y) * (y - way.Y)) >= 1.0, $"{spec}: a find in house {c.Building}'s doorway ({b.Shape})");
                var find = finds.Single(f => f.Container == c.Index);
                Assert.Contains(find.Item, loot.Of(ContainerKind.Loose).Items);
                Assert.True(find.Value <= kept, $"{spec}: {find.Item} worth {find.Value}, the middle of a kept find's {kept:0}");
            }
        }
        Assert.True(villages > 0 && lying >= villages, $"{spec}: {lying} things lying in {villages} villages");
    }

    [Fact]
    public void EveryOpenHouseIsRansackedTheSameOnEveryMachine()
    {
        // The director, 8 Oct: "furniture scattered about, like the place has been ransacked many times before". Every open
        // house has its heavy furniture against a wall and things underfoot, from the stop's seed (twice over, the same); the
        // heavy pieces are walls (the walk-in test goes round them), the nest's ground is clear, and the stop is laid as before.
        var a = Routes.Generate(Content, "frontier:7", 6);
        var b2 = Routes.Generate(Content, "frontier:7", 6);
        int houses = 0, heavy = 0, loose = 0, nests = 0;
        foreach (var (f, g) in a.Features.Zip(b2.Features).Where(x => x.First.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                if (!stop.Buildings[i].Open || !StopWalls.Walled(stop, i))
                    continue;
                houses++;
                var clutter = StopWalls.ClutterOf(stop, i);
                Assert.Equal(clutter, StopWalls.ClutterOf(g.Stop!, i));
                heavy += clutter.Count(c => c.Solid);
                loose += clutter.Count(c => !c.Solid);
                Assert.True(clutter.Count(c => !c.Solid) >= 2, $"house {i} at {f.Start:0} is hardly touched");
                foreach (var c in clutter)
                    Assert.True(StopWalls.InParts(stop.Buildings[i], c.X, c.Y), $"a {c.Kind} outside house {i}");
                if (StopWalls.Nest(stop, i) is { } nest)
                {
                    nests++;
                    Assert.True(StopWalls.InParts(stop.Buildings[i], nest.X, nest.Y), $"house {i}'s nest is outside it");
                    Assert.All(clutter.Where(c => c.Solid), c => Assert.True(Math.Abs(c.X - nest.X) > c.Box.HalfX + 1 || Math.Abs(c.Y - nest.Y) > c.Box.HalfY + 1));
                }
            }
        }
        Assert.True(houses > 0 && heavy >= houses && loose >= 3 * houses && nests > 0, $"{houses} houses: {heavy} heavy, {loose} loose, {nests} nests");
    }

    /// <summary>Where a crewmate gets to from outside a house, on a 10 cm grid in its own frame.</summary>
    [Theory]
    [InlineData("frontier:1")]
    [InlineData("deadLines:20")]
    [InlineData("local:10")]
    [InlineData("deepTerritory:1")]
    public void TheBarnsAndShedsStandOpenWithTheirFindsInside(string spec)
    {
        // Note 417: a barn, an outbuilding and a dead town's goods shed stand as their walls with one wide door toward the
        // line; you walk in by it and nowhere else, and a hayloft's or a workbench's find is inside against the back wall,
        // got to round the bench (which is solid).
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        int sheds = 0, finds = 0, benches = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (!StopWalls.OpenShed(b) || stop.Holdouts.Any(h => h.Building == i))
                    continue;
                sheds++;
                Assert.True(StopWalls.Shelled(stop, i) && !StopWalls.Walled(stop, i));
                var door = Assert.Single(StopWalls.Doors(stop, i, new Run.WallTuning()));
                // Its door looks toward the line.
                var outward = StopWalls.InHouse(b, 0, door.Side) - StopWalls.InHouse(b, 0, 0);
                Assert.True(Math.Sign(b.D) * outward.D <= 1e-9, $"{spec} at {f.Start:0}: {b.Kind} {i}'s door faces away from the line");
                var walk = Reach(walls, line, f, b);
                double y = door.Side * b.Width / 2;
                Assert.True(walk.Reached(0, y + door.Side * 0.6) && walk.Reached(0, y - door.Side * 0.6), $"{spec} at {f.Start:0}: {b.Kind} {i}'s door is shut");
                foreach (var (x, cy) in walk.Crossings)
                    Assert.True(Math.Abs(x) < door.Width / 2 + 0.5 && Math.Abs(cy - y) < 1.5, $"{spec} at {f.Start:0}: {b.Kind} {i} has a way in at ({x:0.0}, {cy:0.0})");
                foreach (var c in stop.Containers.Where(c => c.Building == i))
                {
                    finds++;
                    var (ix, iy) = StopWalls.InsideLocal(b, c);
                    var (kx, ky, fx, fy) = StopWalls.Kept(b, c);
                    Assert.True(StopWalls.InParts(b, ix, iy) && StopWalls.InParts(b, kx, ky), $"{spec} at {f.Start:0}: a {c.Kind} outside its {b.Kind} {i}");
                    Assert.True(walk.Reached(ix, iy), $"{spec} at {f.Start:0}: a {c.Kind} in {b.Kind} {i} can't be got to");
                    if (c.Kind == ContainerKind.Bench)
                    {
                        benches++;
                        Assert.False(walk.Reached(kx, ky), $"{spec} at {f.Start:0}: walked through a {c.Kind}");
                    }
                }
            }
        }
        Assert.True(sheds > 0 && finds > 0, $"{spec}: {sheds} open sheds, {finds} finds in them, {benches} benches");
    }

    [Theory]
    [InlineData("frontier:36")]
    [InlineData("deadLines:20")]
    [InlineData("deepTerritory:63")]
    public void ADeadTownsStationStandsOpenItsCounterSolidAndNothingKeptInIt(string spec)
    {
        // Note 493 (note 417's "not yet"): a dead town's station stands open as its goods shed does, walked into by its door
        // toward the line and nowhere else, its booking office's counter at the back wall solid. Nothing's kept in it (it's
        // inside the rail buffer, P13), and the dead town is still a stop (a find in it failed the stop's checks).
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        int stations = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (b.Kind != BuildingKind.Station)
                    continue;
                stations++;
                Assert.True(stop.Valid);
                Assert.True(StopWalls.OpenShed(b) && StopWalls.Shelled(stop, i) && !StopWalls.Walled(stop, i), $"{spec} at {f.Start:0}: station {i} is shut");
                Assert.DoesNotContain(stop.Containers, c => c.Building == i);
                var door = Assert.Single(StopWalls.Doors(stop, i, new Run.WallTuning()));
                var outward = StopWalls.InHouse(b, 0, door.Side) - StopWalls.InHouse(b, 0, 0);
                Assert.True(Math.Sign(b.D) * outward.D <= 1e-9, $"{spec} at {f.Start:0}: station {i}'s door faces away from the line");
                var walk = Reach(walls, line, f, b);
                double y = door.Side * b.Width / 2;
                Assert.True(walk.Reached(0, y - door.Side * 0.6) && walk.Reached(0, 0), $"{spec} at {f.Start:0}: station {i} can't be walked into");
                foreach (var (x, cy) in walk.Crossings)
                    Assert.True(Math.Abs(x) < door.Width / 2 + 0.5 && Math.Abs(cy - y) < 1.5, $"{spec} at {f.Start:0}: station {i} has a way in at ({x:0.0}, {cy:0.0})");
                var (kx, ky, _, _) = StopWalls.Kept(b, ContainerKind.Bench, 0);
                Assert.Single(StopWalls.Benches(stop, i));
                Assert.False(walk.Reached(kx, ky), $"{spec} at {f.Start:0}: walked through station {i}'s counter");
            }
        }
        Assert.True(stations > 0, $"{spec}: no dead town's station");
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deepTerritory:2")]
    [InlineData("deadLines:2")]
    public void AYardsPowerhouseStandsOpenItsSwitchboardInsideAndTheRestartIsHeldThere(string spec)
    {
        // Note 509 (note 417's "not yet"): a yard's powerhouse stands open, walked into by its door toward the line and
        // nowhere else; its switchboard and its engine stand at the back wall, solid; and the yard's power is restarted
        // standing at the switchboard (the site's Powerhouse), which is inside and got to.
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var walls = StopWalls.Of(route, line);
        var run = new Run.Run(Ballast.DataFile.Load<Run.RunTuning>(Path.Combine(Content, Run.RunTuning.File)), route);
        run.EnableSites(Ballast.DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)), line);
        int houses = 0;
        foreach (var f in route.Features.Where(f => f.Stop is { HasYard: true, Powerhouse: >= 0 }))
        {
            var stop = f.Stop!;
            int i = stop.Powerhouse;
            var b = stop.Buildings[i];
            houses++;
            Assert.True(StopWalls.OpenShed(b) && StopWalls.Shelled(stop, i) && !StopWalls.Walled(stop, i), $"{spec} at {f.Start:0}: the powerhouse is shut");
            var door = Assert.Single(StopWalls.Doors(stop, i, new Run.WallTuning()));
            var outward = StopWalls.InHouse(b, 0, door.Side) - StopWalls.InHouse(b, 0, 0);
            Assert.True(Math.Sign(b.D) * outward.D <= 1e-9, $"{spec} at {f.Start:0}: the powerhouse's door faces away from the line");
            var walk = Reach(walls, line, f, b);
            double y = door.Side * b.Width / 2;
            foreach (var (x, cy) in walk.Crossings)
                Assert.True(Math.Abs(x) < door.Width / 2 + 0.5 && Math.Abs(cy - y) < 1.5, $"{spec} at {f.Start:0}: the powerhouse has a way in at ({x:0.0}, {cy:0.0})");
            Assert.Equal(2, StopWalls.Benches(stop, i).Count());
            foreach (int k in new[] { 0, 1 })
            {
                var (kx, ky, _, _) = StopWalls.ShedKept(b, k);
                Assert.False(walk.Reached(kx, ky), $"{spec} at {f.Start:0}: walked through the powerhouse's {(k == 0 ? "switchboard" : "engine")}");
            }
            var (sx, sy) = StopWalls.Switchboard(b);
            Assert.True(StopWalls.InParts(b, sx, sy) && walk.Reached(sx, sy), $"{spec} at {f.Start:0}: the switchboard can't be got to");
            var site = Assert.Single(run.Sites, s => s?.Feature == f)!;
            var at = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, sx, sy));
            Assert.True(((Assert.NotNull(site.Powerhouse) - at) with { Y = 0 }).Length < 0.01, $"{spec} at {f.Start:0}: the restart isn't at the switchboard");
        }
        Assert.True(houses > 0, $"{spec}: no yard with a powerhouse");
    }

    sealed record Walk(double X0, double Y0, bool[,] Grid, List<(double X, double Y)> Crossings)
    {
        const double Step = 0.1;
        public bool Reached(double x, double y)
        {
            int i = (int)Math.Round((x - X0) / Step), j = (int)Math.Round((y - Y0) / Step);
            return i >= 0 && j >= 0 && i < Grid.GetLength(0) && j < Grid.GetLength(1) && Grid[i, j];
        }

        public static double StepOf => Step;
    }

    static Walk Reach(StopWalls walls, Rail.RailLine line, Route.RouteFeature f, StopBuilding b)
    {
        // The stop's zone is straight and level: the house's frame to the world's is affine.
        var o = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 0));
        var ex = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 1, 0)) - o;
        var ey = Run.Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 1)) - o;
        double step = Walk.StepOf, x0 = -b.Length / 2 - 1.5, y0 = -b.Width / 2 - 1.5;
        int nx = (int)((b.Length + 3) / step) + 1, ny = (int)((b.Width + 3) / step) + 1;
        var free = new bool[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                free[i, j] = !Blocked(walls, o + ex * (x0 + i * step) + ey * (y0 + j * step), StopWalls.Storeyed(b) ? StopWalls.FloorAt(b, 0) : 0);
        var reached = new bool[nx, ny];
        var queue = new Queue<(int, int)>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
                if ((i == 0 || j == 0 || i == nx - 1 || j == ny - 1) && free[i, j])
                {
                    reached[i, j] = true;
                    queue.Enqueue((i, j));
                }
        var crossings = new List<(double, double)>();
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            bool inHere = StopWalls.InParts(b, x0 + i * step, y0 + j * step);
            foreach (var (a, c) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
            {
                if (a < 0 || c < 0 || a >= nx || c >= ny || !free[a, c] || reached[a, c])
                    continue;
                reached[a, c] = true;
                queue.Enqueue((a, c));
                if (StopWalls.InParts(b, x0 + a * step, y0 + c * step) && !inHere)
                    crossings.Add((x0 + a * step, y0 + c * step));
            }
        }
        return new Walk(x0, y0, reached, crossings);
    }
}
