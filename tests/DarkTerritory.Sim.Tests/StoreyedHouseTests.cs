using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Houses with floors (queue #710, ARCHITECTURE §8 note 1005; the director, 23 Oct 2026: "more complex houses that have
/// multiple levels, including basements ... windows that you can escape from ... And we should spawn fewer of them"). The
/// plain houses are built with an upstairs, a basement, or both; a crewmate climbs the flight and comes back down it, goes
/// down the hatch's ladder into the basement and climbs out of a cellar window onto the ground outside; the basement's finds
/// lie on its floor and are drawn worthier.
/// </summary>
[Collection(nameof(LineGenTests))]
public class StoreyedHouseTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly string Content = DataFile.FindContentRoot();
    static readonly LootTuning Loot = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));

    static (Route.Route Route, TrainOnLine Train) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 600);
        train.Walls = StopWalls.Of(route, train.Line, null, Tuning.Run.Walls);
        return (route, train);
    }

    static IEnumerable<(Route.RouteFeature F, StopLayout Stop, int Index, StopBuilding B)> Houses(Route.Route route, Func<StopBuilding, bool> which) =>
        route.Features.Where(f => f.Stop is not null).SelectMany(f => Enumerable.Range(0, f.Stop!.Buildings.Count)
            .Where(i => which(f.Stop!.Buildings[i]) && StopWalls.Walled(f.Stop!, i)).Select(i => (f, f.Stop!, i, f.Stop!.Buildings[i])));

    /// <summary>A point in a house's own frame, in the world, at a height over its ground.</summary>
    static Double3 At(TrainOnLine train, Route.RouteFeature f, StopBuilding b, double x, double y, double up) =>
        Run.Run.StopWorld(train.Line, f, StopWalls.InHouse(b, x, y), Run.Run.StopWorld(train.Line, f, b.Centre).Y - Run.Run.StopWorld(train.Line, f, StopWalls.InHouse(b, x, y)).Y + up);

    static PlayerState Standing(Double3 at) =>
        new() { Parent = PlayerState.World, Position = at, Surface = Surface.Ground, Health = P.Health, Kit = P.StartingKit, LineHint = 0 };

    /// <summary>Steps a crewmate with the same intent, facing a world point, for a while.</summary>
    static void Go(ref PlayerState s, TrainOnLine train, Double3 toward, double seconds, PlayerIntent intent)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            var d = toward - s.Position;
            if (s.Surface != Surface.Ladder && (d with { Y = 0 }).Length > 0.05)
                s.Yaw = Math.Atan2(-d.X, -d.Z);
            PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds);
        }
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("local:5")]
    [InlineData("deepTerritory:5")]
    public void TheVillagesBuildHousesWithFloorsAndABasementInEach(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        int storeyed = 0, upstairs = 0, basements = 0;
        foreach (var f in route.Features.Where(f => f.Stop is { } s && s.Buildings.Any(b => b.Kind == BuildingKind.House && b.Open)))
        {
            var stop = f.Stop!;
            var plain = stop.Buildings.Where(StopGenerator.Storeyed).ToList();
            foreach (var b in stop.Buildings)
                // Only an open plain house has floors.
                Assert.True(b.Form == HouseForm.Plain || StopGenerator.Storeyed(b), $"a {b.Kind} ({b.Shape}) built with floors");
            storeyed += plain.Count(b => b.Form != HouseForm.Plain);
            upstairs += plain.Count(b => b.Upstairs);
            basements += plain.Count(b => b.Basement);
            // A village with a plain house has a basement (stops.json village.forms.leastBasements).
            if (plain.Count > 0)
                Assert.Contains(plain, b => b.Basement);
            // The basement's and the upstairs' finds are on their floors.
            foreach (var c in stop.Containers.Where(c => c.Level != 0))
            {
                var b = stop.Buildings[c.Building];
                Assert.True(c.Level < 0 ? b.Basement : b.Upstairs, $"a find on floor {c.Level} of a {b.Form} house");
                Assert.Equal(StopWalls.FloorAt(b, c.Level), StopWalls.FindUp(stop, c));
            }
        }
        Assert.True(storeyed > 0 && upstairs > 0 && basements > 0, $"{spec}: {storeyed} storeyed, {upstairs} upstairs, {basements} basements");
    }

    [Fact]
    public void TheHousesAreTheSameOnEveryMachine()
    {
        var r1 = Routes.Generate(Content, "frontier:7", 6);
        var a = StopWalls.Of(r1, r1.Build(), null, Tuning.Run.Walls);
        var r2 = Routes.Generate(Content, "frontier:7", 6);
        var b = StopWalls.Of(r2, r2.Build(), null, Tuning.Run.Walls);
        Assert.Equal(a.All, b.All);
        Assert.Equal(a.Ladders, b.Ladders);
    }

    [Fact]
    public void UpTheStairsAndBackDown()
    {
        var (route, train) = Night("frontier:7");
        var (f, stop, i, b) = Houses(route, b => b.Upstairs).First();
        var (x0, dir, y0, y1) = StopWalls.Stair(b);
        double ym = (y0 + y1) / 2, f0 = StopWalls.FloorAt(b, 0), f1 = StopWalls.FloorAt(b, 1);
        // At the flight's foot on the ground floor, facing up it: walk on, and you're upstairs, past its top.
        var s = Standing(At(train, f, b, x0 - dir * 0.6, ym, f0));
        var top = At(train, f, b, x0 + dir * (StopWalls.Rises * StopWalls.Tread + 1.0), ym, f1);
        Go(ref s, train, top, 4, new PlayerIntent { MoveZ = 1 });
        double ground = Run.Run.StopWorld(train.Line, f, b.Centre).Y;
        Assert.True(Math.Abs(s.Position.Y - (ground + f1)) < 0.05 && s.Surface == Surface.Ground, $"upstairs at {s.Position.Y - ground:0.00} over the ground ({s.Surface})");
        // And back down it to the ground floor.
        var foot = At(train, f, b, x0 - dir * 0.6, ym, f0);
        Go(ref s, train, foot, 4, new PlayerIntent { MoveZ = 1 });
        Assert.True(Math.Abs(s.Position.Y - (ground + f0)) < 0.05, $"back down at {s.Position.Y - ground:0.00} over the ground");
    }

    [Fact]
    public void DownTheHatchAndOutACellarWindow()
    {
        var (route, train) = Night("frontier:7");
        var (f, stop, i, b) = Houses(route, b => b.Basement).First();
        double ground = Run.Run.StopWorld(train.Line, f, b.Centre).Y, f0 = StopWalls.FloorAt(b, 0), fb = StopWalls.FloorAt(b, -1);
        var (hx, hy, inX, inY) = StopWalls.Hatch(b);
        // On the boards beside the hatch: the ladder key takes hold of its ladder; down it and off its foot, on the basement's floor.
        var s = Standing(At(train, f, b, hx + inX * (StopWalls.HatchSize / 2 + 0.25), hy + inY * (StopWalls.HatchSize / 2 + 0.25), f0));
        Go(ref s, train, At(train, f, b, hx, hy, f0), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder });
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = -1 });
        Assert.True(Math.Abs(s.Position.Y - (ground + fb)) < 0.05 && s.Surface == Surface.Ground, $"down at {s.Position.Y - ground:0.00} over the ground ({s.Surface})");
        // Over to a cellar window and up its crates: out over the sill, on the ground outside the house.
        var (wx, wy, ox, oy) = StopWalls.CellarWindows(b).First();
        Go(ref s, train, At(train, f, b, wx, wy, fb), 6, new PlayerIntent { MoveZ = 1 });
        Go(ref s, train, At(train, f, b, wx + ox, wy + oy, fb), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder });
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = 1 });
        var l = Run.Run.StopWorld(train.Line, f, b.Centre);
        double away = Math.Abs(Vector(s.Position - l, b, train, f));
        Assert.True(s.Surface == Surface.Ground && Math.Abs(s.Position.Y - ground) < 0.4 && away > b.Width / 2,
            $"out at {s.Position.Y - ground:0.00} over the ground, {away:0.00} m across the house ({s.Surface})");
    }

    /// <summary>How far across a house (its own y) a world offset from its middle is.</summary>
    static double Vector(Double3 d, StopBuilding b, TrainOnLine train, Route.RouteFeature f)
    {
        var ey = (Run.Run.StopWorld(train.Line, f, StopWalls.InHouse(b, 0, 1)) - Run.Run.StopWorld(train.Line, f, StopWalls.InHouse(b, 0, 0))) with { Y = 0 };
        return Double3.Dot(d with { Y = 0 }, ey.Normalized);
    }

    [Fact]
    public void ALootDroppedInTheBasementRestsOnItsFloor()
    {
        var (route, train) = Night("frontier:7");
        var (f, stop, i, b) = Houses(route, b => b.Basement).First();
        double ground = Run.Run.StopWorld(train.Line, f, b.Centre).Y;
        var at = At(train, f, b, 0, 0, StopWalls.FloorAt(b, -1) + 0.5);
        Assert.Equal(ground + StopWalls.FloorAt(b, -1), train.Walls!.Under(at, ground), 6);
        // Over the ground floor's boards, the earth's as ever.
        Assert.Equal(ground, train.Walls.Under(At(train, f, b, 0, 0, StopWalls.FloorAt(b, 0) + 0.5), ground), 6);
    }

    [Fact]
    public void TheBasementsFindsAreDrawnWorthier()
    {
        // Drawn three times and the worthiest kept (loot.json levelDraws): over many nights, worth more than one draw is.
        double deep = 0, once = 0;
        int n = 0;
        var flat = Loot with { LevelDraws = [] };
        for (int seed = 1; seed <= 40; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 6);
            foreach (var (f, k) in route.Features.Select((f, k) => (f, k)).Where(x => x.f.Stop is not null))
            {
                var stop = f.Stop!;
                var cellars = stop.Containers.Where(c => c.Level < 0).Select(c => c.Index).ToHashSet();
                if (cellars.Count == 0)
                    continue;
                deep += StopLoot.Village(Loot, stop, route.Seed, k, 100).Where(x => cellars.Contains(x.Container)).Sum(x => x.Value);
                once += StopLoot.Village(flat, stop, route.Seed, k, 100).Where(x => cellars.Contains(x.Container)).Sum(x => x.Value);
                n += cellars.Count;
            }
        }
        Assert.True(n > 0 && deep > once * 1.1, $"{n} basement finds: {deep:0} drawn worthier, {once:0} drawn once");
    }
}
