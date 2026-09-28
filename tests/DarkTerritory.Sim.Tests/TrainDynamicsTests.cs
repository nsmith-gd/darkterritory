using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public class TrainDynamicsTests
{
    static readonly TrainTuning T = Tuning.Train;
    const double Dt = SimConstants.TickSeconds;

    [Fact]
    public void EmptyCarsAccelerateHarderThanLoaded()
    {
        var empty = new TrainDynamics(Consist.Uniform(T, 10, 0));
        var loaded = new TrainDynamics(Consist.Uniform(T, 10, 1));
        var full = new TrainControls { Throttle = 1, Reverser = 1 };
        empty.Step(Dt, full, TrackConditions.Flat);
        loaded.Step(Dt, full, TrackConditions.Flat);
        Assert.True(empty.Acceleration > loaded.Acceleration * 2);
    }

    [Fact]
    public void BrakesNeverReverseTheTrain()
    {
        var train = new TrainDynamics(Consist.Uniform(T, 3, 1)) { Velocity = 1 };
        for (int i = 0; i < 300; i++)
            train.Step(Dt, new TrainControls { Brake = 1, Reverser = 1 }, TrackConditions.Flat);
        Assert.Equal(0, train.Velocity);
    }

    [Fact]
    public void HeldBrakeStopsTrainRollingBackOnModestGrade()
    {
        var train = new TrainDynamics(Consist.Uniform(T, 3, 1));
        for (int i = 0; i < 300; i++)
            train.Step(Dt, new TrainControls { Brake = 1, Reverser = 1 }, new TrackConditions { GradePercent = 3, Traction = 1 });
        Assert.Equal(0, train.Velocity);
    }

    [Fact]
    public void UnbrakedTrainRollsBackDownAGrade()
    {
        var train = new TrainDynamics(Consist.Uniform(T, 3, 1));
        for (int i = 0; i < 90; i++)
            train.Step(Dt, default, new TrackConditions { GradePercent = 3, Traction = 1 });
        Assert.True(train.Velocity < -0.5);
    }

    [Fact]
    public void BrakesFadeOnDescentAndRecoverWhenReleased()
    {
        var train = new TrainDynamics(Consist.Uniform(T, 20, 1)) { Velocity = 15 };
        var descent = new TrackConditions { GradePercent = -2, Traction = 1 };
        for (int i = 0; i < SimConstants.TickRate * 10; i++)
            train.Step(Dt, new TrainControls { Brake = 1, Reverser = 1 }, descent);
        Assert.InRange(train.BrakeEfficiency, 0.91, 0.93); // 8% per 10s

        double faded = train.BrakeEfficiency;
        for (int i = 0; i < SimConstants.TickRate * 10; i++)
            train.Step(Dt, new TrainControls { Reverser = 1 }, descent);
        Assert.InRange(train.BrakeEfficiency - faded, 0.039, 0.041); // 4% per 10s
    }

    [Fact]
    public void GreaseKillsTractionAndBraking()
    {
        var dry = TrainScenarios.StopFrom(T, 6, 14);
        var train = new TrainDynamics(Consist.Uniform(T, 6, 1)) { Velocity = 14 };
        int ticks = 0;
        while (train.Speed > 0 && ticks++ < 100_000)
            train.Step(Dt, new TrainControls { Brake = 1, Reverser = 1 }, new TrackConditions { Traction = 0.2 });
        Assert.InRange(train.Distance / dry.Metres, 4.5, 5.5);
    }

    [Fact]
    public void JumpOffAndBoardingShareOneThreshold()
    {
        Assert.True(SpeedBands.CanBeCaughtOnFoot(T, T.SpeedBands.Yard));
        Assert.False(SpeedBands.JumpOffIsLethal(T, T.SpeedBands.Yard));
        Assert.True(SpeedBands.JumpOffIsLethal(T, T.SpeedBands.Slow));
        Assert.Equal(SpeedBand.Max, SpeedBands.Classify(T, T.MaxSpeed));
    }

    [Fact]
    public void UncouplingDropsMass()
    {
        var consist = Consist.Uniform(T, 5, 1);
        Assert.Equal(2, consist.UncoupleFrom(3));
        Assert.Equal(3, consist.CarCount);
        Assert.Equal(210, consist.MassTonnes);
    }
}
