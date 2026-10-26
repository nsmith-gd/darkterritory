using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The villages' L, cross and pair houses with floors (queue #725, ARCHITECTURE §8 note 1022; note 1005's "L, cross and paired
/// houses with floors"): their floors in their biggest part (each cottage of a pair), a flight up where one fits, a cellar
/// under a hatch; a crewmate climbs the flight and comes back down, goes down the hatch and back up; what's kept down
/// there or up there lies on its floor.
/// </summary>
[Collection(nameof(LineGenTests))]
public class PartFloorsTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly string Content = DataFile.FindContentRoot();
    static readonly string[] Nights = ["frontier:7", "local:5", "deepTerritory:5", "frontier:3", "local:3", "deadLines:3"];

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

    /// <summary>The first house of parts of the kind asked for, in the first of the nights that has one.</summary>
    static (Route.Route Route, TrainOnLine Train, Route.RouteFeature F, StopBuilding B) With(Func<StopBuilding, bool> which)
    {
        foreach (string spec in Nights)
        {
            var (route, train) = Night(spec);
            foreach (var (f, _, _, b) in Houses(route, which))
                return (route, train, f, b);
        }
        throw new InvalidOperationException("no night has such a house");
    }

    /// <summary>A point in a house's own frame, in the world, at a height over its ground (its middle's).</summary>
    static Double3 At(TrainOnLine train, Route.RouteFeature f, StopBuilding b, double x, double y, double up) =>
        Run.Run.StopWorld(train.Line, f, StopWalls.InHouse(b, x, y)) with { Y = Run.Run.StopWorld(train.Line, f, b.Centre).Y + up };

    static PlayerState Standing(Double3 at) =>
        new() { Parent = PlayerState.World, Position = at, Surface = Surface.Ground, Health = P.Health, Kit = P.StartingKit, LineHint = 0 };

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

    [Fact]
    public void TheVillagesBuildHousesOfPartsWithFloors()
    {
        int floored = 0, up = 0, down = 0, finds = 0;
        foreach (string spec in Nights)
        {
            var route = Routes.Generate(Content, spec, 6);
            foreach (var f in route.Features.Where(f => f.Stop is not null))
            {
                var stop = f.Stop!;
                foreach (var b in stop.Buildings.Where(StopWalls.PartFloored))
                {
                    floored++;
                    Assert.True(b.Parts.Count > 1 && b.Open, $"a {b.Shape} built with floors as a house of parts");
                    // Only what fits: an upstairs has a flight, a cellar a hatch.
                    Assert.Equal(b.Upstairs, StopWalls.PartUp(b));
                    Assert.Equal(b.Basement, StopWalls.PartDown(b));
                    up += b.Upstairs ? 1 : 0;
                    down += b.Basement ? 1 : 0;
                    // Its flight and its hatch inside its part, clear of each other and of the doorways' insides.
                    foreach (int part in StopWalls.FlooredParts(b))
                    {
                        var p = b.Parts[part];
                        if (StopWalls.PartHatch(b, part) is { } h)
                        {
                            Assert.InRange(h.X, p.X - p.Length / 2, p.X + p.Length / 2);
                            Assert.InRange(h.Y, p.Y - p.Width / 2, p.Y + p.Width / 2);
                            foreach (var (_, inside) in StopWalls.Doorways(b))
                                Assert.True(Math.Abs(inside.X - h.X) > StopWalls.HatchSize / 2 || Math.Abs(inside.Y - h.Y) > StopWalls.HatchSize / 2, "a hatch in a doorway");
                        }
                    }
                }
                // What's kept on another floor is in a house with that floor, and lies on its boards.
                foreach (var c in stop.Containers.Where(c => c.Level != 0 && stop.Buildings[c.Building].Parts.Count > 1))
                {
                    var b = stop.Buildings[c.Building];
                    Assert.True(c.Level < 0 ? StopWalls.PartDown(b) : StopWalls.PartUp(b), $"a find on floor {c.Level} of a {b.Shape} of {b.Form}");
                    Assert.Equal(StopWalls.PartFloorAt(c.Level), StopWalls.FindUp(stop, c));
                    finds++;
                }
            }
        }
        Assert.True(floored > 0 && up > 0 && down > 0 && finds > 0, $"{floored} houses of parts with floors, {up} upstairs, {down} cellars, {finds} finds on them");
    }

    [Fact]
    public void TheHousesOfPartsAreTheSameOnEveryMachine()
    {
        var (a, ta) = Night("frontier:7");
        var (b, tb) = Night("frontier:7");
        Assert.Equal(ta.Walls!.All, tb.Walls!.All);
        Assert.Equal(ta.Walls.Ladders, tb.Walls.Ladders);
        Assert.Equal(ta.Walls.Hatches, tb.Walls.Hatches);
    }

    [Fact]
    public void UpAHouseOfPartsFlightAndBackDown()
    {
        var (_, train, f, b) = With(StopWalls.PartUp);
        var fl = StopWalls.FlooredParts(b).Select(i => StopWalls.PartStair(b, i)).First(x => x is not null)!.Value;
        double ground = Run.Run.StopWorld(train.Line, f, b.Centre).Y, mid = (fl.Wall + fl.Open) / 2;
        var foot = fl.At(fl.Start - fl.Dir * 0.5, mid);
        var landing = fl.At(fl.End + fl.Dir * 0.35, mid);
        var s = Standing(At(train, f, b, foot.X, foot.Y, 0));
        Go(ref s, train, At(train, f, b, landing.X, landing.Y, StopWalls.Storey), 5, new PlayerIntent { MoveZ = 1 },
            x => x.Position.Y > ground + StopWalls.Storey - 0.05 && ((x.Position - At(train, f, b, landing.X, landing.Y, 0)) with { Y = 0 }).Length < 0.2);
        Assert.True(Math.Abs(s.Position.Y - (ground + StopWalls.Storey)) < 0.05 && s.Surface == Surface.Ground,
            $"upstairs at {s.Position.Y - ground:0.00} over the ground ({s.Surface}) in a {b.Shape}");
        var below = fl.At(fl.Start - fl.Dir * 0.6, mid);
        Go(ref s, train, At(train, f, b, below.X, below.Y, 0), 5, new PlayerIntent { MoveZ = 1 },
            x => ((x.Position - At(train, f, b, below.X, below.Y, 0)) with { Y = 0 }).Length < 0.3);
        Go(ref s, train, s.Position, 0.5, new PlayerIntent());
        Assert.True(Math.Abs(s.Position.Y - ground) < 0.05, $"back down at {s.Position.Y - ground:0.00} over the ground");
    }

    [Fact]
    public void DownAHouseOfPartsHatchAndBackUp()
    {
        var (_, train, f, b) = With(StopWalls.PartDown);
        var h = StopWalls.FlooredParts(b).Select(i => StopWalls.PartHatch(b, i)).First(x => x is not null)!.Value;
        double ground = Run.Run.StopWorld(train.Line, f, b.Centre).Y;
        double by = StopWalls.HatchSize / 2 + 0.3;
        var s = Standing(At(train, f, b, h.X + h.InX * by, h.Y + h.InY * by, 0));
        Go(ref s, train, At(train, f, b, h.X, h.Y, 0), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder }, x => x.Surface == Surface.Ladder);
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = -1 });
        Assert.True(Math.Abs(s.Position.Y - (ground - StopWalls.Cellar)) < 0.05 && s.Surface == Surface.Ground,
            $"down at {s.Position.Y - ground:0.00} over the ground ({s.Surface}) in a {b.Shape}");
        // A step off into the dark, and back to the ladder.
        var away = At(train, f, b, h.X - h.InX * 1.2, h.Y - h.InY * 1.2, -StopWalls.Cellar);
        Go(ref s, train, away, 2, new PlayerIntent { MoveZ = 1 }, x => ((x.Position - away) with { Y = 0 }).Length < 0.3);
        Go(ref s, train, At(train, f, b, h.X, h.Y, -StopWalls.Cellar), 3, new PlayerIntent { MoveZ = 1 },
            x => ((x.Position - At(train, f, b, h.X, h.Y, 0)) with { Y = 0 }).Length < 0.5);
        Go(ref s, train, At(train, f, b, h.X, h.Y, 0), 0.2, new PlayerIntent { Actions = PlayerActions.Ladder }, x => x.Surface == Surface.Ladder);
        Assert.Equal(Surface.Ladder, s.Surface);
        Go(ref s, train, s.Position, 4, new PlayerIntent { MoveZ = 1 }, x => x.Surface != Surface.Ladder);
        Go(ref s, train, s.Position, 0.5, new PlayerIntent());
        Assert.True(Math.Abs(s.Position.Y - ground) < 0.05 && s.Surface == Surface.Ground, $"back up at {s.Position.Y - ground:0.00} over the ground ({s.Surface})");
    }
}
