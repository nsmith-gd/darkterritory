using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T114 playtest ("collisions"): a village's houses are walls, and what was in them is put out on the step. T124 (build
/// 1121: "fort buildings have no collision"): a fortress's walls, towers, gatehouse and houses are too.
/// </summary>
public class StopWallsTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static (Route.Route Route, TrainOnLine Train) Night(string spec)
    {
        var route = Routes.Generate(DataFile.FindContentRoot(), spec, 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 600);
        train.Walls = StopWalls.Of(route, train.Line, Fortresses.Of(route, train.Line, 600, Tuning.Run.TerminusZone));
        return (route, train);
    }

    /// <summary>Within <paramref name="pad"/> of a wall's box (round its corners, as the motor keeps a crewmate off it).</summary>
    static bool InAnyWall(StopWalls walls, Double3 p, double pad) => walls.Near(p).Any(w =>
    {
        var l = w.ToLocal(p);
        double dx = Math.Max(0, Math.Abs(l.X) - w.HalfLength), dz = Math.Max(0, Math.Abs(l.Z) - w.HalfWidth);
        return dx * dx + dz * dz < pad * pad;
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

    /// <summary>A player on the ground at <paramref name="along"/>, <paramref name="across"/> out (right +), facing straight out.</summary>
    static PlayerState OnTheGround(TrainOnLine train, double along, double across)
    {
        var t = train.Line.Sample(along);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var s = PlayerMotor.SpawnOnGround(t.Position + right * across, train.Line, along, P);
        var outward = right * Math.Sign(across);
        s.Yaw = Math.Atan2(-outward.X, -outward.Z);
        return s;
    }

    /// <summary>How far a point is out from the line (right +), near <paramref name="hint"/> along it.</summary>
    static double Across(TrainOnLine train, double hint, Double3 p)
    {
        var t = train.Line.Sample(hint);
        for (int i = 0; i < 3; i++)
            t = train.Line.Sample(Math.Clamp(t.Distance + Double3.Dot(p - t.Position, t.Tangent), 0, train.Line.Length));
        return Double3.Dot(p - t.Position, Double3.Cross(t.Tangent, Double3.Up).Normalized);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void WalkingAtTheFortWallStopsAtIt(int side)
    {
        // T124: in the home fortress's yard, between two houses, straight out at the wall for five seconds.
        var (route, train) = Night("frontier:7");
        const double along = 307.5;
        var s = OnTheGround(train, along, side * 9);
        double face = Fortresses.WallOut - Fortresses.WallHalf;
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            Assert.False(InAnyWall(train.Walls!, s.Position, P.Radius - 0.02), $"inside a wall at {s.Position}, tick {i}");
        }
        Assert.InRange(Math.Abs(Across(train, along, s.Position)), face - P.Radius - 0.1, face - P.Radius + 0.02);

        // Without the fortress's solids (as before T124) the same walk goes straight through the wall.
        train.Walls = StopWalls.Of(route, train.Line);
        s = OnTheGround(train, along, side * 9);
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
        Assert.True(Math.Abs(Across(train, along, s.Position)) > Fortresses.WallOut + Fortresses.WallHalf);
    }

    [Fact]
    public void TheFortressesStandTheirWallsTowersGatehouseAndHousesOffTheTrack()
    {
        var (route, train) = Night("frontier:7");
        var forts = Fortresses.Of(route, train.Line, 600, Tuning.Run.TerminusZone);
        Assert.Equal(2, forts.Count);
        var home = forts[0];
        var solids = Fortresses.Solids(home, train.Line).ToList();
        double rail = train.Line.Sample(home.Gate).Position.Y;
        // Sixty bays of wall a side, five towers a side (0 to 480), the gatehouse's two towers and its arch, and houses.
        Assert.Equal(2 * 60, solids.Count(w => w.HalfWidth == Fortresses.WallHalf));
        Assert.Equal(2 * 5, solids.Count(w => w.HalfWidth == Fortresses.TowerHalf && w.HalfLength == Fortresses.TowerHalf));
        Assert.Single(solids, w => Math.Abs(w.Bottom - rail - Fortresses.ArchBottom) < 1e-9);
        Assert.True(solids.Count(w => w.HalfWidth >= 2.25 && w.HalfWidth <= Fortresses.LivedDepth / 2 && w.HalfLength != Fortresses.TowerHalf) > 20);
        // Nothing on the ground stands nearer the line than the gatehouse's passage (the arch is overhead).
        foreach (var w in solids.Where(w => w.Bottom < rail + Fortresses.ArchBottom - 1))
        {
            double near = Math.Abs(Across(train, home.Gate, w.Centre with { Y = rail })) - w.HalfWidth;
            Assert.True(near >= Fortresses.GateInner - 0.05, $"a solid {near:0.00} m off the line");
        }
        // And all of them among the walls the train carries.
        Assert.True(train.Walls!.All.Count >= solids.Count + Fortresses.Solids(forts[1], train.Line).Count());
    }
}
