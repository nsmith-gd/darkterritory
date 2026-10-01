using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>T114 playtest ("collisions"): a village's houses are walls, and what was in them is put out on the step.</summary>
public class StopWallsTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static (Route.Route Route, TrainOnLine Train) Night(string spec)
    {
        var route = Routes.Generate(DataFile.FindContentRoot(), spec, 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 600);
        train.Walls = StopWalls.Of(route, train.Line);
        return (route, train);
    }

    static bool InAnyWall(StopWalls walls, Double3 p, double pad) => walls.Near(p).Any(w =>
    {
        var l = w.ToLocal(p);
        return Math.Abs(l.X) < w.HalfLength + pad && Math.Abs(l.Z) < w.HalfWidth + pad;
    });

    [Fact]
    public void WalkingAtAHouseStopsAtItsWall()
    {
        var (route, train) = Night("frontier:7");
        Assert.NotEmpty(train.Walls!.All);
        var (f, b) = route.Features.Where(x => x.Stop is not null)
            .SelectMany(x => x.Stop!.Buildings.Select((b, i) => (x, b, i)).Where(t => StopWalls.Walled(x.Stop!, t.i)).Select(t => (x, t.b)))
            .First();
        var centre = Run.Run.StopWorld(train.Line, f, b.Centre);
        // From 12 m out on the line's side of it, straight at its middle for ten seconds.
        var from = Run.Run.StopWorld(train.Line, f, new Pt(b.S, b.D - Math.Sign(b.D) * (Math.Max(b.Length, b.Width) / 2 + 12)));
        var s = PlayerMotor.SpawnOnGround(from, train.Line, f.Start + b.S, P);
        var d = centre - from;
        s.Yaw = Math.Atan2(-d.X, -d.Z);
        // Never inside it on the way (it would walk clean through the middle and out the far side without its walls).
        for (int i = 0; i < 10 * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            Assert.False(InAnyWall(train.Walls, s.Position, P.Radius - 0.02), $"inside a wall at {s.Position}, tick {i}");
        }
        Assert.True((s.Position - from).Length > 6, "it walked up to it");
    }

    [Theory]
    [InlineData("frontier:3")]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    public void EveryFindPutOutOnAStepIsClearOfTheWalls(string spec)
    {
        var (route, train) = Night(spec);
        int steps = 0;
        foreach (var f in route.Features.Where(x => x.Stop is not null))
            foreach (var c in f.Stop!.Containers.Where(c => c.Building >= 0 && StopWalls.Walled(f.Stop!, c.Building)))
            {
                var at = Run.Run.StopWorld(train.Line, f, StopWalls.Doorstep(f.Stop!.Buildings[c.Building], c.Index));
                Assert.False(InAnyWall(train.Walls!, at, 0.3), $"a find's step at {at} is in a wall");
                steps++;
            }
        Assert.True(steps > 0);
    }
}
