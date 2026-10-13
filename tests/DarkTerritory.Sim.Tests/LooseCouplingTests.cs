using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A coupling working loose (notes 356 and 640; orchestrator.md §5.1 U2): a gap's pin knocks loose only from a cause the crew
/// can name (the director, the inbox, 12 Oct), a run-in or a knock, never on its own and never with a pack aboard; the wrench
/// in the gap tightens it; left alone the pin drops and the rake parts behind it, and the clerk says what loosed it.
/// </summary>
public class LooseCouplingTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly LooseTuning L = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;
    const double Dt = SimConstants.TickSeconds;
    static readonly TrainControls Forward = new() { Throttle = 1, Reverser = 1 };

    static World Night(double speed, int cars = 6, double radius = 0)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000, radius)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 5_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.Upkeep = new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = L };
        return world;
    }

    static readonly TrainControls Brake = new() { Brake = 1, Reverser = 1 };

    [Fact]
    public void RunningNeverWorksOneLooseOnItsOwnNotEvenOnABend()
    {
        // Note 640 (the director, the inbox, 12 Oct: "I don't like pins working loose on their own"): twenty minutes at speed
        // on a bend, and no pin's loose.
        var w = Night(15, radius: 400);
        for (int i = 0; i < 1200 * SimConstants.TickRate; i++)
            w.Step(Forward);
        Assert.DoesNotContain(w.Train.Vehicles, v => v.Loose > 0);
    }

    [Fact]
    public void AHardBrakeWorksOneLooseNeverTheEnginesNorTheLastCars()
    {
        // A run-in: the whole brake from 15 m/s, and one gap's pin jumps; the coupling behind a car with a car behind it,
        // never the engine's own (the whole train), and the last has none.
        var w = Night(15);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            w.Step(Brake);
        var loose = Assert.Single(w.Train.Vehicles, v => v.Loose > 0);
        var consist = w.Train.Dynamics.Consist;
        Assert.False(loose.IsEngine);
        Assert.InRange(consist.IndexOf(loose.Id), 1, consist.Vehicles.Count - 2);
        Assert.Equal(1, w.LooseCount.Came);
    }

    [Fact]
    public void AServiceBrakeAndAStopFromACrawlLooseNothing()
    {
        // The stop driver's braking (half the brake at most, StopDriver.BrakeRate) and the whole brake from a walking pace.
        var w = Night(15);
        for (int i = 0; i < 20 * SimConstants.TickRate; i++)
            w.Step(new TrainControls { Brake = 0.5, Reverser = 1 });
        var slow = Night(L.RunInAbove - 1);
        for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            slow.Step(Brake);
        Assert.DoesNotContain(w.Train.Vehicles, v => v.Loose > 0);
        Assert.DoesNotContain(slow.Train.Vehicles, v => v.Loose > 0);
    }

    /// <summary>
    /// The cars behind car 3 cut at a stand, the engine's rake drawn off them, then backed onto them at about
    /// <paramref name="speed"/> (as <c>CouplingTests</c>' set-back); true once coupled.
    /// </summary>
    static bool BackedOn(World w, double speed)
    {
        Assert.True(w.Train.Uncouple(3));
        // Drawn off slower than a run-in's speed, so the brake looses nothing.
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            w.Step(Forward);
        for (int i = 0; i < 6 * SimConstants.TickRate; i++)
            w.Step(Brake);
        for (int i = 0; i < 120 * SimConstants.TickRate && w.Train.TrainRakes > 1; i++)
            w.Step(new TrainControls { Throttle = w.Train.Dynamics.Speed < speed - 0.1 ? 0.25 : 0, Reverser = -1 });
        return w.Train.TrainRakes == 1;
    }

    [Fact]
    public void CoupledUpTooHardTheKnockWorksItsPinLooseAndTheClerkSaysSo()
    {
        var w = Night(0);
        Assert.True(BackedOn(w, 1.45), "never coupled");
        Assert.Equal(3, Assert.Single(w.Train.Vehicles, v => v.Loose > 0).Id);
        // Left, it parts; the report names the knock and who was driving, not the hand that last pulled that coupler.
        w.Attribution.PulledCoupler(4, 2);
        w.Train.Vehicles[3].Loose = L.PartAfter - 0.5;
        for (int i = 0; i < 2 * SimConstants.TickRate && w.Train.TrainRakes == 1; i++)
            w.Step(Forward);
        Assert.Equal(2, w.Train.TrainRakes);
        Assert.Equal(LooseCause.Knock, w.Attribution.LooseOf(4)?.Cause);
        Assert.Equal(-1, w.Attribution.CouplerPulledBy(4));
        // At the stop driver's crawl onto them (up to about 1 m/s), nothing.
        var gentle = Night(0);
        Assert.True(BackedOn(gentle, 1));
        Assert.DoesNotContain(gentle.Train.Vehicles, v => v.Loose > 0);
    }

    [Fact]
    public void WithAPackAboardNoneComesLooseAndALoosePinHoldsAsItDoesAtAStand()
    {
        // The director, the inbox, 12 Oct ("b, not while a pack is aboard") and 9 Oct ("a pin holds while the train stands").
        var train = Night(15).Train;
        var pins = new Couplings(L, 7);
        train.Dynamics.Velocity = 15;
        pins.Step(train, 4, quiet: false, packAboard: true);
        train.Dynamics.Velocity = 0; // a run-in's worth in one tick
        pins.Step(train, 4, quiet: false, packAboard: true);
        Assert.DoesNotContain(train.Vehicles, v => v.Loose > 0);
        train.Vehicles[2].Loose = L.PartAfter - 1;
        train.Dynamics.Velocity = 15;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            pins.Step(train, 4, quiet: false, packAboard: true);
        train.Dynamics.Velocity = 0;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            pins.Step(train, 4, quiet: false);
        Assert.Equal(L.PartAfter - 1, train.Vehicles[2].Loose, 1e-9);
        Assert.Equal(1, train.TrainRakes);
    }

    [Fact]
    public void LeftAloneThePinDropsAndTheRakePartsBehindIt()
    {
        var w = Night(12);
        w.Train.Vehicles[3].Loose = L.PartAfter - 1;
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
            w.Step(Forward);
        Assert.Equal(2, w.Train.TrainRakes);
        Assert.Equal(3, w.Train.Dynamics.Consist.Vehicles[^1].Id);
        Assert.Equal(0, w.Train.Vehicles[3].Loose);
        Assert.Equal(1, w.LooseCount.Parted);
        // Note 511: the report says so of the cars behind ("The coupling worked loose and parted."), nobody's hand on it.
        Assert.True(w.Attribution.Parted(4));
        Assert.False(w.Attribution.Parted(3));
        Assert.Equal(-1, w.Attribution.CouplerPulledBy(4));
    }

    static PlayerState InTheGap(TrainOnLine train, int car, bool wrenchInHand) => new()
    {
        Parent = car,
        Surface = Surface.Coupler,
        Position = new Double3(T.Geometry.PlateX, T.Geometry.CouplerHeight, T.Geometry.CarLength / 2 + 0.7),
        Health = 100,
        Kit = Kit.Of([Tool.Shovel, Tool.Wrench]),
        HeldSlot = (byte)(wrenchInHand ? 1 : 0),
    };

    [Fact]
    public void TheWrenchInTheGapTightensItAndNothingElseDoes()
    {
        var w = Night(12);
        var train = w.Train;
        train.Vehicles[2].Loose = 30;
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        // The shovel in hand: nothing.
        var p = InTheGap(train, 2, wrenchInHand: false);
        Assert.Equal(2, Couplings.Within(p, train, L));
        for (int i = 0; i < (L.TightenSeconds + 1) * SimConstants.TickRate; i++)
            CrewActions.Apply(ref p, use, train, Dt);
        Assert.True(train.Vehicles[2].Loose > 0);
        // The wrench: tight, in its time and not before.
        p = InTheGap(train, 2, wrenchInHand: true);
        for (int i = 0; i < (L.TightenSeconds - 0.5) * SimConstants.TickRate; i++)
            CrewActions.Apply(ref p, use, train, Dt);
        Assert.True(train.Vehicles[2].Loose > 0, "tightened before its time");
        for (int i = 0; i < SimConstants.TickRate; i++)
            CrewActions.Apply(ref p, use, train, Dt);
        Assert.Equal(0, train.Vehicles[2].Loose);
    }

    [Fact]
    public void NotFromTheRoofNorInsideTheCar()
    {
        var w = Night(12);
        w.Train.Vehicles[2].Loose = 30;
        var roof = PlayerMotor.SpawnOnRoof(w.Train, 2, T.Geometry.CarLength / 2 - 1.5, Tuning.Player);
        Assert.Null(Couplings.Within(roof, w.Train, L));
        var inside = new PlayerState
        {
            Parent = 2,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, T.Geometry.CarLength / 2 - 0.8),
            Surface = Surface.Deck,
            Health = 100,
        };
        Assert.True(PlayerMotor.Indoors(inside, w.Train));
        Assert.Null(Couplings.Within(inside, w.Train, L));
    }

    [Fact]
    public void ABotInTheGapPutsTheWrenchInHandAndTightensIt()
    {
        var w = Night(12);
        w.Train.Vehicles[2].Loose = 30;
        var p = InTheGap(w.Train, 2, wrenchInHand: false);
        Assert.Equal(2, Heed.Coupling(default, p, w).Select);
        p.HeldSlot = 1;
        Assert.True(Heed.Coupling(default, p, w).Has(PlayerButtons.Use));
        w.Train.Vehicles[2].Loose = 0;
        Assert.False(Heed.Coupling(default, p, w).Has(PlayerButtons.Use));
    }

    [Fact]
    public void AWalkerGoesDownIntoTheGapAndTightensALooseCoupling()
    {
        // As the hot box's walker (HotBoxTests): car 3's coupling comes loose 20 s in, and the walker goes along the roofs to
        // it, down the end ladder into the gap behind car 3, puts the wrench in hand and tightens it before the pin drops.
        double? cameAt = null, tightAt = null;
        var report = CrewOfTwoTests.Night(CrewOfTwoTests.OpenLine.Spec, 6, 100, null, start: CrewOfTwoTests.OpenLine.Start, bots: 3,
            upkeep: new UpkeepTuning { HotBox = new() { Enabled = false }, Lamp = new() { Enabled = false }, Coupling = L },
            each: w =>
            {
                if (cameAt is null && w.ElapsedSeconds >= 20)
                {
                    w.Train.Vehicles[3].Loose = Dt;
                    cameAt = w.ElapsedSeconds;
                }
                else if (cameAt is not null && tightAt is null && w.Train.Vehicles[3].Loose == 0)
                    tightAt = w.ElapsedSeconds;
            });
        Assert.NotNull(cameAt);
        Assert.True(tightAt is { } at && at - cameAt < L.PartAfter, $"came at {cameAt:0} s, tight at {tightAt?.ToString("0") ?? "never"} ({report.Deaths} died)");
    }

    [Fact]
    public void AChilledWalkerGoesToALooseCouplingBeforeGoingInToWarm()
    {
        // Note 511: on frontier:7's 4-bot nights the walkers were in the cars, getting warm or waiting for a mail bag, while
        // a pin worked loose behind them, and 11 of 21 parted, every car behind each lost. A loose coupling comes first: a
        // walker chilled enough to want warming, the cold not yet hurting, goes along to car 3, down into the gap behind it
        // and tightens the pin, and only then goes in. Without the call it went into car 2 to warm first.
        var w = Night(8);
        w.Upkeep = w.Upkeep! with { Coupling = L };
        var train = w.Train;
        const int loose = 3;
        var bot = new RoofWalkerBot(7, Tuning.Player.Cold);
        var s = new PlayerState
        {
            Parent = 2,
            Position = new Double3(0, T.Geometry.CarHeight, 0),
            Surface = Surface.Roof,
            Health = 100,
            Cold = Tuning.Player.Cold.OnsetSeconds * 0.8,
            Kit = Kit.Of([Tool.Shovel, Tool.Wrench]),
            LineHint = train.Cars[2].FrontDistance,
        };
        train.Vehicles[loose].Loose = Dt;
        double? tight = null;
        bool wentIn = false;
        for (uint tick = 0; tick < SimConstants.TickRate * L.PartAfter && tight is null; tick++)
        {
            w.BeginTick();
            var intent = bot.Decide(s, w, tick, out _);
            w.CrewAct(ref s, intent, 1);
            w.Step(Forward);
            PlayerMotor.Step(ref s, intent, train, Tuning.Player, T, Dt, applyLook: false);
            wentIn |= PlayerMotor.Indoors(s, train);
            if (train.Vehicles[loose].Loose == 0)
                tight = tick / (double)SimConstants.TickRate;
        }
        Assert.True(tight is not null, $"never tightened: {s.Surface} on {s.Parent} at {s.Position}, cold {s.Cold:0}");
        Assert.False(wentIn, $"went in to warm before tightening it ({tight:0} s)");
    }
}
