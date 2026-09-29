using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Loose bodies on a moving train (GDD §33: thrown objects, cargo, ragdolls, bodies).</summary>
public class BodyTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    const double Dt = SimConstants.TickSeconds;

    static TrainOnLine Straight(double speed, int cars = 6)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(40_000)])), 2_000);
        train.Dynamics.Velocity = speed;
        return train;
    }

    static void Run(TrainOnLine train, Bodies bodies, double seconds, Func<int, PlayerState?>? players = null, Action? each = null)
    {
        double v = train.Dynamics.Velocity;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            train.Dynamics.Velocity = v;
            train.Step(Dt, default);
            each?.Invoke();
            bodies.Step(train, T, players ?? (_ => null));
        }
    }

    [Fact]
    public void ACrateOnARoofAtFullSpeedStaysWhereItWasPut()
    {
        var train = Straight(T.MaxSpeed);
        var bodies = new Bodies();
        var crate = bodies.SpawnCrate(train, 3, new Double3(0.4, T.Geometry.CarHeight, 2));
        Run(train, bodies, 10);
        Assert.Equal(3, crate.Parent);
        Assert.True(crate.Pbd.Asleep);
        Assert.InRange((crate.Centre - new Double3(0.4, T.Geometry.CarHeight + 0.35, 2)).Length, 0, 0.05);
    }

    [Fact]
    public void ItStaysOnThroughTheCurvesToo()
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 600);
        train.Dynamics.Velocity = 14;
        var bodies = new Bodies();
        var crate = bodies.SpawnCrate(train, 2, new Double3(0, T.Geometry.CarHeight, 0));
        Run(train, bodies, 60);
        Assert.Equal(2, crate.Parent);
        Assert.InRange(crate.Centre.X, -0.5, 0.5);
        Assert.InRange(crate.Centre.Z, -0.5, 0.5);
    }

    static PlayerState Carrier(TrainOnLine train, int car, double z, double yaw) =>
        PlayerMotor.SpawnOnRoof(train, car, z, P) with { Yaw = yaw };

    static void PickUpAndThrow(Bodies bodies, TrainOnLine train, PlayerState carrier)
    {
        Assert.True(bodies.Handle(carrier, new PlayerIntent { Buttons = PlayerButtons.Use }, 1, train));
        bodies.Handle(carrier, default, 1, train);
        bodies.Step(train, T, id => carrier);
        bodies.Handle(carrier, new PlayerIntent { Buttons = PlayerButtons.Throw }, 1, train);
    }

    [Fact]
    public void ThrownOffTheSideAtSpeedItLandsOnTheGroundBehindTheTrain()
    {
        var train = Straight(T.MaxSpeed);
        var bodies = new Bodies();
        var carrier = Carrier(train, 3, 0, yaw: -Math.PI / 2); // facing right
        var crate = bodies.SpawnCrate(train, 3, carrier.Position + new Double3(0.6, 0, 0));
        PickUpAndThrow(bodies, train, carrier);
        double thrownAt = train.Cars[3].FrontDistance;
        Run(train, bodies, 6);
        Assert.Equal(PlayerState.World, crate.Parent);
        // It carried the train's speed with it, then stopped on the ballast: behind the car it left.
        double along = RouteDistance(train.Line, crate.Centre);
        Assert.True(along > thrownAt, "it should have flown forward with the train's momentum first");
        Assert.True(along < train.Cars[3].FrontDistance - 20, "and been left behind once it landed");
        Assert.InRange(crate.Centre.Y, 0, 1);
    }

    static double RouteDistance(RailLine line, Double3 p)
    {
        double s = 0;
        for (int i = 0; i < 10; i++)
        {
            var t = line.Sample(s);
            s = Math.Clamp(t.Distance + Double3.Dot(p - t.Position, t.Tangent), 0, line.Length);
        }
        return s;
    }

    [Fact]
    public void ThrownForwardFromTheEndOfARoofItLandsOnTheNextCar()
    {
        var train = Straight(14);
        var bodies = new Bodies();
        var shape = train.Frames[3].Shape;
        var carrier = Carrier(train, 3, -shape.HalfLength + 1, yaw: 0); // at the front end, facing forward
        var crate = bodies.SpawnCrate(train, 3, carrier.Position + new Double3(0, 0, -0.6));
        PickUpAndThrow(bodies, train, carrier);
        Run(train, bodies, 4);
        Assert.Equal(2, crate.Parent);
        Assert.InRange(crate.Centre.Y, T.Geometry.CarHeight, T.Geometry.CarHeight + 0.5);
    }

    [Fact]
    public void ABodyStaysOnTheCarWhereItFellAndHoldsTogether()
    {
        // Spec C.1: bodies persist at the death location.
        var train = Straight(T.MaxSpeed);
        var bodies = new Bodies();
        var dead = PlayerMotor.SpawnOnRoof(train, 4, 0, P) with { Health = 0, Death = DeathCause.Mauled };
        var body = bodies.SpawnRagdoll(train, 7, dead);
        Run(train, bodies, 10);
        Assert.Equal(4, body.Parent);
        Assert.All(body.Pbd.Particles, p => Assert.True(p.Position.Y > T.Geometry.CarHeight - 0.05, $"a particle sank to {p.Position.Y}"));
        foreach (var c in body.Pbd.Constraints.Where(c => c.Stiffness >= 1))
        {
            double len = (body.Pbd.Particles[c.A].Position - body.Pbd.Particles[c.B].Position).Length;
            Assert.InRange(len, c.Length * 0.8, c.Length * 1.2);
        }
        Assert.True(bodies.HasRagdoll(7));
    }

    [Fact]
    public void YouCanCarryABodyAndPutItDown()
    {
        var train = Straight(10);
        var bodies = new Bodies();
        var dead = PlayerMotor.SpawnOnRoof(train, 4, 0, P) with { Health = 0 };
        var body = bodies.SpawnRagdoll(train, 7, dead);
        Run(train, bodies, 2);
        var carrier = PlayerMotor.SpawnOnRoof(train, 4, 1.0, P) with { Yaw = 0 }; // standing over it, facing forward
        Assert.True(bodies.Handle(carrier, new PlayerIntent { Buttons = PlayerButtons.Use }, 1, train));
        Assert.Equal(1, body.Carrier);
        // Walk it three metres forward along the roof.
        for (int i = 0; i < 30; i++)
        {
            carrier.Position += new Double3(0, 0, -0.1);
            bodies.Handle(carrier, default, 1, train);
            bodies.Step(train, T, _ => carrier);
        }
        Assert.InRange(body.Pbd.Particles[1].Position.Z, carrier.Position.Z - 1.0, carrier.Position.Z - 0.2);
        Assert.True(bodies.Handle(carrier, new PlayerIntent { Buttons = PlayerButtons.Use }, 1, train));
        Assert.Equal(-1, body.Carrier);
        Run(train, bodies, 3);
        Assert.Equal(4, body.Parent);
    }
}
