using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public class CouplingTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    const double Dt = SimConstants.TickSeconds;

    static TrainOnLine Train(int cars, double speed = 0, BoilerTuning? boiler = null)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000, boiler);
        train.Dynamics.Velocity = speed;
        return train;
    }

    static void Run(TrainOnLine train, double seconds, TrainControls controls)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            train.Step(Dt, controls);
    }

    static readonly TrainControls Forward = new() { Throttle = 1, Reverser = 1 };

    [Fact]
    public void CarsCutAtAStandAreParkedAndTheEngineCanLeaveThem()
    {
        var train = Train(6);
        Assert.True(train.Uncouple(3));
        Assert.Equal(2, train.Rakes.Count);
        Assert.Equal(3, train.Dynamics.Consist.CarCount);
        var parked = train.Rakes.Single(r => !r.Consist.HasEngine);
        Assert.True(parked.Handbrake);
        double parkedAt = parked.Distance;

        Run(train, 20, Forward);
        Assert.Equal(parkedAt, parked.Distance, 6);
        Assert.True(train.Dynamics.RearDistance - parked.Distance > 50);
    }

    [Fact]
    public void CarsCutAtSpeedRollFreeAndFallBehind()
    {
        // The Weight's counter (GDD App. A.3): cut the rear car and it's gone.
        var train = Train(6, speed: 14);
        train.Uncouple(5);
        var cut = train.Rakes.Single(r => !r.Consist.HasEngine);
        Assert.False(cut.Handbrake);
        Run(train, 30, Forward);
        Assert.True(cut.Speed > 10 && cut.Speed < 14, $"cut car at {cut.Speed} m/s");
        Assert.True(train.Dynamics.RearDistance - cut.Distance > 100);
    }

    [Fact]
    public void BackingGentlyIntoParkedCarsCouplesThem()
    {
        var train = Train(6);
        train.Uncouple(0);
        Assert.Equal(0, train.Dynamics.Consist.CarCount);
        Run(train, 8, Forward);
        Run(train, 5, new TrainControls { Brake = 1, Reverser = 1 });

        // Reverse at a crawl until they meet.
        for (int i = 0; i < SimConstants.TickRate * 120 && train.Rakes.Count > 1; i++)
        {
            var controls = new TrainControls { Throttle = train.Dynamics.Speed < 0.6 ? 0.25 : 0, Reverser = -1 };
            train.Step(Dt, controls);
        }
        Assert.Single(train.Rakes);
        Assert.Equal(6, train.Dynamics.Consist.CarCount);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        Assert.All(train.Vehicles, v => Assert.Equal(1, v.Integrity));
        Assert.Contains(train.ContactsThisTick, c => c.Coupled);
    }

    [Fact]
    public void RammingParkedCarsDamagesBothEndsAndDoesNotCouple()
    {
        var train = Train(4);
        train.Uncouple(0);
        Run(train, 10, Forward);
        Run(train, 30, new TrainControls { Brake = 1, Reverser = 1 });
        // Back into them at 4 m/s.
        train.Dynamics.Velocity = -4;
        for (int i = 0; i < SimConstants.TickRate * 60 && !train.ContactsThisTick.Any(); i++)
        {
            train.Dynamics.Velocity = -4;
            train.Step(Dt, new TrainControls { Reverser = -1 });
        }
        var hit = Assert.Single(train.ContactsThisTick);
        Assert.False(hit.Coupled);
        double expected = (4 - T.Couplings.SafeContactSpeed) * (4 - T.Couplings.SafeContactSpeed) * T.Couplings.DamagePerSpeedSquared;
        Assert.Equal(1 - expected, train.Vehicles[0].Integrity, 3);
        Assert.Equal(1 - expected, train.Vehicles[1].Integrity, 3);
        Assert.Equal(1, train.Vehicles[2].Integrity);
        Assert.True(train.Vehicles[4].CargoIntegrity < 1);
        Assert.Equal(2, train.Rakes.Count);
    }

    [Fact]
    public void AJustCutRakeDoesNotRecoupleWhereItStands()
    {
        var train = Train(6);
        train.Uncouple(2);
        Run(train, 5, default);
        Assert.Equal(2, train.Rakes.Count);
    }

    [Fact]
    public void APlayerOnACutCarRidesItAway()
    {
        var train = Train(6, speed: 14);
        var rider = PlayerMotor.SpawnOnRoof(train, 5, 0, P);
        train.Uncouple(4);
        var start = PlayerMotor.WorldPosition(rider, train);
        for (int i = 0; i < SimConstants.TickRate * 20; i++)
        {
            train.Step(Dt, Forward);
            PlayerMotor.Step(ref rider, default, train, P, T, Dt);
        }
        Assert.Equal(5, rider.Parent);
        Assert.Equal(Surface.Roof, rider.Surface);
        double moved = (PlayerMotor.WorldPosition(rider, train) - start).Length;
        Assert.True(moved > 200 && train.Dynamics.RearDistance - train.RakeOf(5).Distance > 30);
    }

    static PlayerState OnThePlate(double pitch) => new()
    {
        Parent = 3,
        Surface = Surface.Coupler,
        Position = new Double3(0, T.Geometry.CouplerHeight, T.Geometry.CarLength / 2 + 0.7),
        Health = 100,
        Pitch = pitch,
    };

    /// <summary>Seconds until the train's in two rakes, holding <paramref name="intent"/> (null: it never came apart in 10 s).</summary>
    static double? SecondsToCut(double speed, PlayerState p, PlayerIntent intent)
    {
        var train = Train(6, speed);
        var controls = speed > 0 ? Forward : default;
        for (int ticks = 1; ticks <= SimConstants.TickRate * 10; ticks++)
        {
            CrewActions.Apply(ref p, intent, train, Dt);
            train.Step(Dt, controls);
            if (train.Rakes.Count == 2)
            {
                Assert.Equal(3, train.Dynamics.Consist.CarCount);
                return ticks * Dt;
            }
        }
        return null;
    }

    static readonly PlayerIntent Uncouple = new() { Actions = PlayerActions.Uncouple };

    [Theory]
    [InlineData(0, 3.0)]
    [InlineData(14, 6.0)]
    public void HoldingUncoupleLookingDownAtThePlateCutsIt(double speed, double seconds)
    {
        // T91 playtest: its own key, held long, looking down at the coupler.
        Assert.Equal(seconds, speed > 0 ? T.Couplings.UncoupleUnderLoadSeconds : T.Couplings.UncoupleSeconds, 6);
        var cut = SecondsToCut(speed, OnThePlate(pitch: -1.3), Uncouple);
        Assert.NotNull(cut);
        Assert.InRange(cut.Value, seconds - 0.05, seconds + 0.05);
    }

    [Fact]
    public void UncoupleLookingAheadOrUseOnThePlateNeverCuts()
    {
        Assert.Null(SecondsToCut(0, OnThePlate(pitch: 0), Uncouple));
        Assert.Null(SecondsToCut(0, OnThePlate(pitch: -1.3), new PlayerIntent { Buttons = PlayerButtons.Use }));
        // Walking while holding it isn't working the coupling either.
        Assert.Null(SecondsToCut(0, OnThePlate(pitch: -1.3), Uncouple with { MoveZ = 1 }));
    }

    [Fact]
    public void TheBrakeWheelWindsAParkedRakesHandbrakesOff()
    {
        var train = Train(6);
        train.Uncouple(3);
        var parked = train.RakeOf(5);
        Assert.True(parked.Handbrake);
        var p = PlayerMotor.SpawnOnRoof(train, 5, T.Geometry.CarLength / 2 - 0.5, P);
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        for (int i = 0; i < SimConstants.TickRate * 3; i++)
            CrewActions.Apply(ref p, use, train, Dt);
        Assert.False(parked.Handbrake);
    }

    [Fact]
    public void OnlyCoupledCarsDrawHeatingSteam()
    {
        var boiler = Tuning.Boiler;
        var train = Train(10, boiler: boiler);
        double full = train.Boiler.SteamDemand(boiler, 0, train.Dynamics.Consist.CarCount);
        train.Uncouple(3);
        double cut = train.Boiler.SteamDemand(boiler, 0, train.Dynamics.Consist.CarCount);
        Assert.Equal(7 * boiler.HeatingPerCar, full - cut, 6);
    }

    [Fact]
    public void RakeStateRoundTripsThroughCaptureAndRestore()
    {
        var host = Train(6, speed: 10);
        host.Uncouple(2);
        host.Uncouple(4);
        Run(host, 10, Forward);
        host.Vehicles[3].Integrity = 0.4;

        var client = Train(6);
        client.Restore(host.Capture());
        Assert.Equal(host.Rakes.Count, client.Rakes.Count);
        for (int id = 0; id < host.Vehicles.Count; id++)
        {
            Assert.True((host.Cars[id].Centre - client.Cars[id].Centre).Length < 1e-9, $"vehicle {id}");
            Assert.Equal(host.Vehicles[id].Integrity, client.Vehicles[id].Integrity);
        }
        // Both keep simulating identically.
        Run(host, 5, Forward);
        Run(client, 5, Forward);
        for (int id = 0; id < host.Vehicles.Count; id++)
            Assert.True((host.Cars[id].Centre - client.Cars[id].Centre).Length < 1e-9);
    }
}
