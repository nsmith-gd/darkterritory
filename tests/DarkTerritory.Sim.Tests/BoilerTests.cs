using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Pins the boiler model to spec B.6 (burn, endurance) and B.5/C.2 (rebuild times at a standstill).</summary>
public class BoilerTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly BoilerTuning B = Tuning.Boiler;
    static readonly PlayerTuning P = Tuning.Player;

    [Theory]
    [InlineData(3, 20, 133)]
    [InlineData(10, 12, 80)]
    [InlineData(20, 8, 53)]
    public void BurnToHoldPressureMatchesSpecB6(int cars, double secondsPerUnit, double enduranceMinutes)
    {
        Assert.InRange(BoilerScenarios.HoldingSecondsPerUnit(B, cars), secondsPerUnit * 0.95, secondsPerUnit * 1.05);
        Assert.InRange(BoilerScenarios.TenderEnduranceMinutes(B, cars), enduranceMinutes * 0.95, enduranceMinutes * 1.05);
    }

    [Theory]
    [InlineData(3, 12, 20)]
    [InlineData(10, 12, 12)]
    [InlineData(20, 12, 8)]
    public void ADiligentFiremanHoldsTheWorkingBandAtFullThrottle(int cars, double minutes, double shovelAtMostEvery)
    {
        var run = BoilerScenarios.Run(T, B, P, cars, minutes * 60, throttle: 1);
        Assert.False(run.Ruptured);
        Assert.True(run.MinPressure >= B.WorkingBandMin, $"pressure fell to {run.MinPressure}");
        Assert.True(run.SecondsPerShovel <= shovelAtMostEvery + 1, $"{run.SecondsPerShovel} s per shovel");
        Assert.True(run.MeanSpeed > 18);
    }

    [Fact]
    public void AtTwentyCarsTheBoilerIsAFullTimePost()
    {
        // Spec B.6: a 1.2 s shovel every 8 s. Count the ticks the fireman spends with the shovel.
        var run = BoilerScenarios.Run(T, B, P, 20, 600, throttle: 1);
        Assert.InRange(run.SecondsPerShovel, 7, 9);
        Assert.True(run.FiremanDuty > 0.14, $"fireman busy only {run.FiremanDuty:P0} of the time");
    }

    [Theory]
    [InlineData(3, 6, 10)]
    [InlineData(10, 6, 20)]
    [InlineData(20, 6, 30)]
    public void RebuildingPressureAtAStandstillMatchesTheSpec(int cars, double minSeconds, double maxSeconds)
    {
        // Spec C.2, the director, 21 Oct (note 969): "for 3 cars the rebuild for any speed from 0 is about 8s, at a full 20 cars
        // the rebuild should be no longer than 30s" (it was 40 s at three cars and over three minutes at twenty).
        Assert.InRange(BoilerScenarios.RebuildSeconds(T, B, P, cars), minSeconds, maxSeconds);
    }

    [Fact]
    public void LongerTrainsNeverRebuildFaster()
    {
        // The heating every car draws still costs a standing engine: twenty cars are never quicker to fire up than three.
        Assert.True(BoilerScenarios.RebuildSeconds(T, B, P, 20) >= BoilerScenarios.RebuildSeconds(T, B, P, 3));
    }

    [Fact]
    public void NobodyOnTheShovelMeansTheTrainDiesAndTheFireGoesLow()
    {
        var fired = BoilerScenarios.Run(T, B, P, 10, 600, throttle: 1);
        var neglected = BoilerScenarios.Run(T, B, P, 10, 600, throttle: 1, fireAt: null);
        Assert.True(neglected.EndPressure < B.PowerFloor, $"pressure still {neglected.EndPressure}");
        Assert.True(neglected.MeanSpeed < fired.MeanSpeed - 2, $"neglected {neglected.MeanSpeed} vs fired {fired.MeanSpeed}");
    }

    static TrainOnLine Train(int cars)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 1_000, B);
    }

    [Fact]
    public void LowPressureMeansLowPower()
    {
        var b = Boiler.Fresh(B);
        b.Pressure = B.PowerFloor;
        Assert.Equal(0, b.PowerFactor(B));
        b.Pressure = B.WorkingBandMin;
        Assert.Equal(1, b.PowerFactor(B));
        b.Pressure = (B.PowerFloor + B.WorkingBandMin) / 2;
        Assert.Equal(0.5, b.PowerFactor(B), 6);
    }

    [Fact]
    public void OverfiredPastTheSafetyValveTheBoilerRuptures()
    {
        // T109 playtest ("if I red line for too long the boiler should rupture"): the valve lifts in the red and is heard,
        // but a fire kept full beats it, up to 100, and the spec's 20 s there ruptures it.
        var train = Train(3);
        train.Boiler.Pressure = 90;
        int ticks = 0, lifted = -1;
        for (; ticks < SimConstants.TickRate * 180 && !train.Boiler.Ruptured; ticks++)
        {
            train.Boiler.Shovel(B);
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            if (lifted < 0 && train.Boiler.SafetyValveLifting)
                lifted = ticks;
        }
        Assert.True(lifted >= 0);
        Assert.True(train.Boiler.Ruptured);
        // Long enough in the red to answer it (vent, or stop firing): more than the 20 s at 100, from the valve's first lift. (From
        // the lift since note 903: a shovelful's kick gets the gauge up to the red sooner, as it should; it's the valve that blows
        // the kicks off in the red, so the time there is what it was.)
        Assert.True((ticks - lifted) * SimConstants.TickSeconds > B.RuptureHoldSeconds + 2, $"ruptured {(ticks - lifted) * SimConstants.TickSeconds:0.0} s after the lift");
    }

    [Fact]
    public void VentedInTheRedTheBoilerHolds()
    {
        var train = Train(3);
        train.Boiler.Pressure = 90;
        for (int i = 0; i < SimConstants.TickRate * 180; i++)
        {
            train.Boiler.Shovel(B);
            if (train.Boiler.Pressure > B.Redline)
                train.Boiler.Venting = true;
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        }
        Assert.False(train.Boiler.Ruptured);
    }

    [Fact]
    public void RupturedTheTrainSheddsItsSpeedHardThenCoasts()
    {
        // T109: "speed should drop drastically and quickly and I should coast to a stop (unless I apply a brake)".
        var train = Train(3);
        train.Dynamics.Velocity = 18;
        train.Boiler.Ruptured = true;
        double t = 0;
        while (train.Dynamics.Speed > B.RuptureCoastBelow + 0.5)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            t += SimConstants.TickSeconds;
        }
        Assert.InRange(t, 0.8 * (18 - B.RuptureCoastBelow) / B.RuptureDecel, 1.3 * (18 - B.RuptureCoastBelow) / B.RuptureDecel);
        // Below it, a coast: still rolling a good while later.
        for (int i = 0; i < SimConstants.TickRate * 10; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        Assert.True(train.Dynamics.Speed > 1, $"stopped dead at {train.Dynamics.Speed:0.0} m/s");
    }

    [Fact]
    public void HeldAtMaximumForTwentySecondsTheBoilerRuptures()
    {
        // The Stoker adds heat the safety valve can't shed (GDD App. A.5: "vent, or the boiler goes").
        var train = Train(3);
        train.Boiler.Pressure = 99;
        train.Boiler.ExternalHeat = 10;
        int ticks = 0;
        while (!train.Boiler.Ruptured && ticks < SimConstants.TickRate * 60)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            ticks++;
        }
        Assert.True(train.Boiler.Ruptured);
        Assert.InRange(ticks * SimConstants.TickSeconds, B.RuptureHoldSeconds, B.RuptureHoldSeconds + 1);
        Assert.Equal(0, train.Boiler.PowerFactor(B));
    }

    [Fact]
    public void VentingBeatsTheStoker()
    {
        var train = Train(3);
        train.Boiler.Pressure = 99;
        train.Boiler.ExternalHeat = 3;
        for (int i = 0; i < SimConstants.TickRate * 60; i++)
        {
            train.Boiler.Venting = true;
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        }
        Assert.False(train.Boiler.Ruptured);
        Assert.True(train.Boiler.Pressure < 90);
    }

    [Fact]
    public void HoldingTheVentDumpsAWorkingBoilerToZero()
    {
        var run = BoilerScenarios.Run(T, B, P, 3, 30, throttle: 0, fireAt: null, startPressure: 90, startFirebox: 0, vent: true);
        Assert.Equal(0, run.EndPressure);
    }

    [Fact]
    public void ShovellingNeedsThePlayerInTheCabAtTheFirebox()
    {
        var train = Train(6);
        var inCab = PlayerMotor.SpawnInCab(train, P);
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        // Behind the fire door: the firebox is against the cab's front wall (note 280).
        inCab.Position = inCab.Position with { X = firebox.X + 0.15, Z = firebox.Z + 0.4 };
        var onRoof = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        train.Boiler.Firebox = 0;
        double tender = train.Boiler.Tender;
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        for (int i = 0; i < SimConstants.TickRate * 6; i++)
        {
            CrewActions.Apply(ref inCab, use, train, SimConstants.TickSeconds);
            CrewActions.Apply(ref onRoof, use, train, SimConstants.TickSeconds);
        }
        // 6 s at 1.2 s per shovelful: five units, all from the man in the cab.
        Assert.Equal(tender - 5, train.Boiler.Tender);
        Assert.Equal(5, train.Boiler.Fire, 6);
    }

    [Fact]
    public void LowFireIsTimedForTheHollow()
    {
        var train = Train(3);
        train.Boiler.Firebox = 0.5;
        for (int i = 0; i < SimConstants.TickRate * 50; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        Assert.True(train.Boiler.LowFireSeconds >= 45);
    }

    [Fact]
    public void AnEngineWhoseFireDiesSlowsWithItsSteam()
    {
        // Note 319, the director's test build (7 Oct 2026): with nobody firing, heat and pressure fell and the train held its
        // speed; at 0.01 m/s² it was still at 17 m/s four minutes on with the fire long out. Pressure sets the speed both ways.
        static List<(double V, double P)> Run(BoilerTuning b)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(40000)])), 500, b);
            var samples = new List<(double, double)>();
            for (int i = 0; i <= SimConstants.TickRate * 300; i++)
            {
                if (i % (SimConstants.TickRate * 30) == 0)
                    samples.Add((train.Dynamics.Velocity, train.Boiler.Pressure));
                train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
            }
            return samples;
        }
        var now = Run(Tuning.Boiler);
        var coasting = Run(Tuning.Boiler with { StarvedDecel = 0 });
        // In the working band, nothing changes.
        for (int i = 0; i < now.Count && coasting[i].P >= Tuning.Boiler.WorkingBandMin; i++)
            Assert.Equal(coasting[i].V, now[i].V, 6);
        // The fire out (pressure gone by 240 s), it's slowing hard, and stopped by 300 s; coasting, it was still near 17 m/s.
        Assert.True(coasting[8].V > 16, $"coasting at 240 s: {coasting[8].V:0.0} m/s");
        Assert.True(now[8].V < 6, $"at 240 s: {now[8].V:0.0} m/s, pressure {now[8].P:0}");
        Assert.True(now[10].V < 0.5, $"at 300 s: {now[10].V:0.0} m/s");
    }

    [Fact]
    public void AShovelfulAnswersOnTheGaugeAndInThePullOneToThreeSecondsLater()
    {
        // The director's playtest of 13 Oct evening (line 19, note 903): "a shovel and one to three seconds later ... some sort of
        // reaction for the locomotive". Two trains alike, running at the speed their steam makes; one gets a shovelful.
        var fed = Train(8);
        var left = Train(8);
        foreach (var t in new[] { fed, left })
        {
            t.Boiler.Pressure = 62;
            t.Boiler.Firebox = 3;
            for (int i = 0; i < SimConstants.TickRate * 40; i++)
                t.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        }
        Assert.True(fed.Boiler.Shovel(B));
        double peakFlareAt = 0, peakFlare = 0, peakLook = 0, peakSurge = 0, gainAtHalf = 0, gainAtThree = 0, faster = 0;
        for (int i = 1; i <= SimConstants.TickRate * 3; i++)
        {
            fed.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            left.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            double at = i * SimConstants.TickSeconds;
            if (fed.Boiler.Flare(B) > peakFlare)
                (peakFlare, peakFlareAt, peakLook) = (fed.Boiler.Flare(B), at, B.FlareLook(fed.Boiler));
            peakSurge = Math.Max(peakSurge, fed.Dynamics.Acceleration - left.Dynamics.Acceleration);
            if (i == SimConstants.TickRate / 2)
                gainAtHalf = fed.Boiler.Pressure - left.Boiler.Pressure;
            gainAtThree = fed.Boiler.Pressure - left.Boiler.Pressure;
            faster = fed.Dynamics.Speed - left.Dynamics.Speed;
        }
        // Not at once: the coal catches first, and the flare's height is about a second after the stroke.
        Assert.InRange(peakFlareAt, 0.7, 2);
        Assert.True(gainAtHalf < gainAtThree * 0.3, $"{gainAtHalf:0.0} of {gainAtThree:0.0} in the first half second");
        // Then plainly on the gauge, and in the pull: a surge the driver feels, and speed made.
        Assert.True(gainAtThree >= 3.5, $"the gauge only {gainAtThree:0.0} up 3 s on");
        Assert.True(peakSurge >= 0.2, $"surge {peakSurge:0.00} m/s²");
        Assert.True(faster >= 0.4, $"only {faster:0.00} m/s faster 3 s on");
        // And the stack and the chuff see it: one shovelful at its height reads about 1.
        Assert.InRange(peakLook, 0.9, 1.1);
    }
}
