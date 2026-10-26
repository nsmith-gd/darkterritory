using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The towns' houses with floors (queue #725, ARCHITECTURE §8 note 1022; #710's villages carried to the towns, note 1005;
/// the director, 23 Oct 2026: "more complex houses that have multiple levels, including basements. Those basements should
/// get pretty dark. They should have windows that you can escape from"). An open house of two storeys has an upstairs up
/// a flight, most have a cellar under a hatch, a burnt one keeps its cellar, and a boarded one or two are left as they were
/// inside; a crewmate climbs the flight and comes back down it, goes down the hatch's ladder and back up, and climbs out
/// of the upstairs window onto the porch roof and drops into the yard.
/// </summary>
[Collection(nameof(LineGenTests))]
public class TownFloorsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly PlayerTuning P = Tuning.Player;

    static (TrainOnLine Train, Town Town) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, gate - 8);
        var town = new Town(TownGenerator.Generate(Towns, TownSite.Of(route, gate, [], Towns)), Towns.Tuning, line);
        var forts = Fortresses.Of(route, line, gate, Tuning.Run.TerminusZone);
        // As World.EnableTown stands them: the fortresses, the town's walls, and its houses' floors.
        train.Walls = StopWalls.Of(route, line, [forts[0] with { Square = town.Plan.Square }, .. forts.Skip(1)]);
        train.Walls.Add(town.Walls);
        var (cellars, ladders, hatches) = town.Floors();
        train.Walls.AddFloors(cellars, ladders, hatches);
        return (train, town);
    }

    static TownSite Site(int seed)
    {
        string[] industries = [.. Towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        return new TownSite($"Fort {Towns.Surnames[seed % Towns.Surnames.Count]}", industries[seed % industries.Length], 1100, (ulong)seed, [], null);
    }

    static PlayerState Standing(Double3 at) =>
        new() { Parent = PlayerState.World, Position = at, Surface = Surface.Ground, Health = P.Health, Kit = P.StartingKit, LineHint = 0 };

    /// <summary>Steps a crewmate with the same intent, facing a world point, for a while (or until <paramref name="until"/>).</summary>
    static void Go(ref PlayerState s, TrainOnLine train, Double3 toward, double seconds, PlayerIntent intent, Func<PlayerState, bool>? until = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate && until?.Invoke(s) != true; i++)
        {
            var d = toward - s.Position;
            if (s.Surface != Surface.Ladder && (d with { Y = 0 }).Length > 0.05)
                s.Yaw = Math.Atan2(-d.X, -d.Z);
            PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds);
        }
    }

    static bool Near(PlayerState s, Double3 at, double by = 0.35) => ((s.Position - at) with { Y = 0 }).Length < by;

    [Fact]
    public void TheTownsBuildHousesWithFloorsCellarsAndRoomsLeftAsTheyWere()
    {
        int upstairs = 0, cellars = 0, burnt = 0, left = 0, twoStorey = 0;
        for (int seed = 1; seed <= 24; seed++)
        {
            var plan = TownGenerator.Generate(Towns, Site(seed));
            Assert.True(plan.Houses.Count(h => h.Left is not null) <= Towns.Tuning.Floors.BoardedMost, "too many boarded houses left open");
            foreach (var h in plan.Houses)
            {
                var rooms = TownFloors.Rooms(h);
                // Only a house with rooms goes up, and only one of two storeys.
                if (h.Upstairs)
                    Assert.True(rooms is not null && TownFloors.CanGoUp(h), $"house {h.Id} ({h.Kind}, {h.Design.Storeys} storeys) built with an upstairs");
                if (h.Cellar)
                    Assert.True(rooms is not null || h.Kind == HouseKind.Burnt, $"a {h.Kind} house with a cellar");
                if (h.Left is not null)
                    Assert.True(h.Kind == HouseKind.Boarded && h.Layout is null && h.Turn == 0, $"house {h.Id} ({h.Kind}) left open");
                if (rooms is not null && h.Design.Storeys >= 2)
                    twoStorey++;
                upstairs += h.Upstairs ? 1 : 0;
                cellars += h.Cellar && rooms is not null ? 1 : 0;
                burnt += h.Cellar && rooms is null ? 1 : 0;
                left += h.Left is not null ? 1 : 0;
                if (rooms is null || !TownFloors.Floored(h))
                    continue;
                // Nothing solid of the ground floor's stands in the hatch or on the flight; nobody's kept standing over the hatch.
                var (hu, hv, _, _) = TownFloors.Hatch(h);
                double hs = TownFloors.HatchSize / 2;
                bool InHatch(double u, double v, double ru, double rv) => h.Cellar && Math.Abs(u - hu) < hs + ru && Math.Abs(v - hv) < hs + rv;
                var (u0, u1, v0, v1) = TownFloors.Stair(h);
                bool OnFlight(double u, double v, double ru, double rv) => h.Upstairs && Math.Abs(u - (u0 + u1) / 2) < Math.Abs(u0 - u1) / 2 + ru
                    && v + rv > v0 && v - rv < v1;
                foreach (var x in rooms.Things.Where(x => x.Solid))
                    Assert.False(InHatch(x.U, x.V, x.HalfU, x.HalfV) || OnFlight(x.U, x.V, x.HalfU, x.HalfV), $"house {h.Id}'s {x.Kind} in the hatch or on the flight");
                foreach (var x in rooms.Spots)
                    Assert.False(InHatch(x.U, x.V, 0.2, 0.2), $"house {h.Id}'s {x.Name} place over the hatch");
                // Upstairs, the bedrooms' furniture is inside the walls and clear of the well and the doorway.
                double w = h.Width / 2 - HouseLayout.Wall, pv = TownFloors.UpperPassV(h);
                foreach (var x in TownFloors.Furniture(h))
                {
                    Assert.True(Math.Abs(x.U) + x.HalfU <= w + 1e-6 && x.V - x.HalfV >= HouseLayout.Wall - 1e-6 && x.V + x.HalfV <= h.Depth - HouseLayout.Wall + 1e-6,
                        $"house {h.Id}'s {x.Kind} upstairs through a wall");
                    Assert.False(OnFlight(x.U, x.V, x.HalfU, x.HalfV + 0.1), $"house {h.Id}'s {x.Kind} over the well");
                    Assert.False(Math.Abs(x.U) < x.HalfU + 0.5 && Math.Abs(x.V - pv) < x.HalfV + HouseLayout.PassWidth / 2, $"house {h.Id}'s {x.Kind} in the doorway");
                }
            }
        }
        Assert.True(twoStorey > 0 && upstairs == (int)Math.Round(twoStorey * Towns.Tuning.Floors.Upstairs), $"{upstairs} upstairs of {twoStorey} two-storey houses with rooms");
        Assert.True(cellars > 0 && burnt > 0 && left > 0, $"{cellars} cellars, {burnt} burnt cellars, {left} boarded houses left open");
    }

    [Fact]
    public void TheFloorsAreTheSameOnEveryMachine()
    {
        var (a, ta) = Night("frontier:7");
        var (b, tb) = Night("frontier:7");
        Assert.Equal(a.Walls!.All, b.Walls!.All);
        Assert.Equal(a.Walls.Ladders, b.Walls.Ladders);
        Assert.Equal(JsonSerializer.Serialize(ta.Plan.Houses), JsonSerializer.Serialize(tb.Plan.Houses));
    }

    /// <summary>The first house with floors of the kind asked for, in the first of the nights that has one.</summary>
    static (TrainOnLine Train, Town Town, TownHouse House) With(Func<TownHouse, bool> which)
    {
        foreach (string spec in new[] { "frontier:7", "frontier:3", "local:3", "local:5", "frontier:9", "deepTerritory:5" })
        {
            var (train, town) = Night(spec);
            if (town.Plan.Houses.FirstOrDefault(which) is { } h)
                return (train, town, h);
        }
        throw new InvalidOperationException("no night has such a house");
    }

    [Fact]
    public void UpTheStairsAndBackDown()
    {
        var (train, town, h) = With(h => h.Upstairs && h.Layout is not null);
        var (u0, u1, v0, v1) = TownFloors.Stair(h);
        double um = (u0 + u1) / 2, floor = town.FloorOf(h);
        // At the flight's foot, facing up it: walk on, and you're upstairs on the landing past its top.
        var s = Standing(town.InHouse(h, um, v0 - 0.4, 0));
        Go(ref s, train, town.InHouse(h, um, h.Depth, TownFloors.Storey), 5, new PlayerIntent { MoveZ = 1 }, x => x.Position.Y > floor + TownFloors.Storey - 0.05 && Near(x, town.InHouse(h, um, v1 + 0.3, 0), 0.25));
        Assert.True(Math.Abs(s.Position.Y - (floor + TownFloors.Storey)) < 0.05 && s.Surface == Surface.Ground, $"upstairs at {s.Position.Y - floor:0.00} over the floor ({s.Surface})");
        // And back down it to the ground floor.
        Go(ref s, train, town.InHouse(h, um, 0, 0), 5, new PlayerIntent { MoveZ = 1 }, x => Near(x, town.InHouse(h, um, v0 - 0.45, 0)));
        Go(ref s, train, s.Position, 0.5, new PlayerIntent());
        Assert.True(Math.Abs(s.Position.Y - floor) < 0.05, $"back down at {s.Position.Y - floor:0.00} over the floor");
    }

    [Fact]
    public void DownTheHatchIntoTheDarkAndBackUp()
    {
        var (train, town, h) = With(h => h.Cellar && h.Layout is not null);
        var (hu, hv, inU, inV) = TownFloors.Hatch(h);
        double floor = town.FloorOf(h);
        // On the boards in front of the hatch: the ladder key takes hold of its ladder; down it and off its foot, on the cellar's floor.
        var s = Standing(town.InHouse(h, hu + inU * (TownFloors.HatchSize / 2 + 0.3), hv + inV * (TownFloors.HatchSize / 2 + 0.3), 0));
        Go(ref s, train, town.InHouse(h, hu, hv, 0), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder }, x => x.Surface == Surface.Ladder);
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = -1 });
        Assert.True(Math.Abs(s.Position.Y - (floor - TownFloors.Cellar)) < 0.05 && s.Surface == Surface.Ground,
            $"down at {s.Position.Y - floor:0.00} over the floor ({s.Surface})");
        // Walking about down there, the earth's over you: you stay on the cellar's floor, inside its walls.
        Go(ref s, train, town.InHouse(h, 0, h.Depth / 2, -TownFloors.Cellar), 3, new PlayerIntent { MoveZ = 1 });
        Assert.True(Math.Abs(s.Position.Y - (floor - TownFloors.Cellar)) < 0.05, $"in the cellar at {s.Position.Y - floor:0.00}");
        // And back to the ladder and up it, onto the boards.
        Go(ref s, train, town.InHouse(h, hu, hv, -TownFloors.Cellar), 4, new PlayerIntent { MoveZ = 1 }, x => Near(x, town.InHouse(h, hu, hv, 0), 0.5));
        Go(ref s, train, town.InHouse(h, hu, hv, 0), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder }, x => x.Surface == Surface.Ladder);
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = 1 }, x => x.Surface != Surface.Ladder);
        Go(ref s, train, s.Position, 0.5, new PlayerIntent());
        Assert.True(Math.Abs(s.Position.Y - floor) < 0.05 && s.Surface == Surface.Ground, $"back up at {s.Position.Y - floor:0.00} over the floor ({s.Surface})");
    }

    [Fact]
    public void AnyoneOnTheGroundFloorStaysOnItsBoards()
    {
        var (train, town, h) = With(h => h.Cellar && h.Layout is not null);
        double floor = town.FloorOf(h);
        // In at the door and across the kitchen: the boards hold you, not the cellar's floor.
        var l = h.Layout!;
        var s = Standing(town.InHouse(h, l.DoorU, -1.5, 0));
        Go(ref s, train, town.InHouse(h, l.DoorU, l.PassV, 0), 4, new PlayerIntent { MoveZ = 1 }, x => Near(x, town.InHouse(h, l.DoorU, l.PassV, 0)));
        Assert.True(Math.Abs(s.Position.Y - floor) < 0.05 && s.Surface == Surface.Ground, $"at {s.Position.Y - floor:0.00} over the floor ({s.Surface})");
    }

    [Fact]
    public void OutTheUpstairsWindowOntoThePorchRoofAndDownIntoTheYard()
    {
        var (train, town, h) = With(h => h.Upstairs && h.Layout is not null);
        double floor = town.FloorOf(h), wu = TownFloors.WindowU(h);
        // Upstairs on the landing, across through the partition's doorway to the window in the back bedroom.
        var (u0, u1, _, v1) = TownFloors.Stair(h);
        var s = Standing(town.InHouse(h, (u0 + u1) / 2, v1 + 0.3, TownFloors.Storey));
        double pv = TownFloors.UpperPassV(h);
        Go(ref s, train, town.InHouse(h, 0, pv, TownFloors.Storey), 4, new PlayerIntent { MoveZ = 1 }, x => Near(x, town.InHouse(h, 0, pv, 0), 0.3));
        Go(ref s, train, town.InHouse(h, wu, h.Depth - 0.6, TownFloors.Storey), 5, new PlayerIntent { MoveZ = 1 }, x => Near(x, town.InHouse(h, wu, h.Depth - 0.6, 0), 0.2));
        Assert.True(Math.Abs(s.Position.Y - (floor + TownFloors.Storey)) < 0.05, $"at the window {s.Position.Y - floor:0.00} over the floor");
        // Up to the sill and out over it: on the porch roof.
        Go(ref s, train, town.InHouse(h, wu, h.Depth + 1, TownFloors.Storey), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder }, x => x.Surface == Surface.Ladder);
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 3, new PlayerIntent { MoveZ = 1 }, x => x.Surface != Surface.Ladder);
        Go(ref s, train, s.Position, 1, new PlayerIntent());
        double roof = TownFloors.Storey + TownFloors.WindowSill - TownFloors.PorchUnder;
        Assert.True(Math.Abs(s.Position.Y - (floor + roof)) < 0.05 && s.Surface == Surface.Ground, $"out at {s.Position.Y - floor:0.00} over the floor ({s.Surface}), the roof's at {roof:0.00}");
        // Off its edge into the yard, unhurt (a drop under player.json's fallHurtFrom).
        var health = s.Health;
        Go(ref s, train, town.InHouse(h, wu, h.Depth + 4, roof), 2, new PlayerIntent { MoveZ = 1 }, x => x.Position.Y < floor + 0.3 && x.Surface == Surface.Ground);
        Go(ref s, train, s.Position, 0.5, new PlayerIntent());
        Assert.True(s.Position.Y < floor + 0.5 && s.Health == health, $"down at {s.Position.Y - floor:0.00} over the floor, health {s.Health} of {health}");
    }

    [Fact]
    public void ABurntHousesCellarIsReachedThroughItsFloorAndItsWallsStandKneeHigh()
    {
        var (train, town, h) = With(h => h.Cellar && h.Kind == HouseKind.Burnt);
        var (hu, hv, inU, inV) = TownFloors.Hatch(h);
        double floor = town.FloorOf(h);
        // In through where its door was, onto its boards, to the hole and down the ladder left in it.
        var s = Standing(town.InHouse(h, h.Design.DoorU, -1.2, 0));
        var by = town.InHouse(h, hu + inU * (TownFloors.Burnt / 2 + 0.3), hv + inV * (TownFloors.Burnt / 2 + 0.3), 0);
        Go(ref s, train, town.InHouse(h, h.Design.DoorU, 1.0, 0), 4, new PlayerIntent { MoveZ = 1 }, x => Near(x, town.InHouse(h, h.Design.DoorU, 1.0, 0)));
        Go(ref s, train, by, 4, new PlayerIntent { MoveZ = 1 }, x => Near(x, by, 0.2));
        Assert.True(Near(s, by, 0.3) && Math.Abs(s.Position.Y - floor) < 0.05, $"never reached the hole ({s.Position.Y - floor:0.00} over the floor)");
        Go(ref s, train, town.InHouse(h, hu, hv, 0), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder }, x => x.Surface == Surface.Ladder);
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = -1 });
        Assert.True(Math.Abs(s.Position.Y - (floor - TownFloors.Cellar)) < 0.05, $"down at {s.Position.Y - floor:0.00} over the floor");
    }
}
