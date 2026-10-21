using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public partial class StopCrewTests
{
    [Fact]
    public void AtTheCraneTheOperatorLetsGoOfTheControlsToRelieveADeadDriver()
    {
        // Note 955 (frontier:7, 8 crew, 12 cars, seed 6, note 933's missed dawn with no Car Hugger): Climbers killed the driver
        // at the Foundry's loading, and the relief (note 399) fell to the crane's operator. Its legs walked it to the train from
        // the controls, but the stick drives the crane while it has them: it stood at the crane till dawn, the fire out, the
        // train where the driver fell. It lets go of them first, as it does for trouble (note 609).
        var night = new Night(cars: 8, walkers: 0, modules: [ModuleKind.Crane, ModuleKind.Winch]);
        for (int i = 0; i < night.Bots.Count; i++)
            if (night.Bots[i] is RoofWalkerBot w)
                (w.Me, w.Calls) = (i + 1, night.Calls);
        var run = night.World.Run!;
        int at = -1;
        night.Until(() => at >= 0 || run.Departures > 0, 1500, () =>
            at = night.Crew.FindIndex(c => c.Alive && c.Has(PlayerFlags.Operating)));
        Assert.True(at > 0, "nobody took the crane's controls");
        night.Crew[0] = night.Crew[0] with { Health = 0, Death = DeathCause.Climbed };
        // The others go on with the loading: only the operator's free for the controls (the relief is whoever claims it).
        for (int i = 1; i < night.Bots.Count; i++)
            if (i != at && night.Bots[i] is RoofWalkerBot other)
                other.Calls = null;
        night.Until(() => PlayerMotor.InCab(night.Crew[at], night.Train), 120);
        var hand = Assert.IsType<RoofWalkerBot>(night.Bots[at]);
        Assert.True(PlayerMotor.InCab(night.Crew[at], night.Train),
            $"the operator ({hand.Job?.Doing}, relieving {hand.Relieving}, operating {night.Crew[at].Has(PlayerFlags.Operating)}) never reached the cab");
    }

    [Fact]
    public void TheReliefDriverDownASpurBacksOutSetsThePointsBackItselfAndGoesOn()
    {
        // Note 955: with the driver dead on the Foundry's spur, the relief (a fresh driver, note 399) backed off it as off a
        // dead line and stood short of the points for someone to set them back. Nobody came, and the driver's own way, down
        // to set them back itself (SetBackAlone), went one step: out of the cab, the walker's legs took it straight back in.
        // Here the walker's the only hand: the driver dead down a dead line, it takes over, backs out, sets the points back
        // itself, and drives on.
        var night = new Night(cars: 8, walkers: 1, hands: 0, deadLine: true);
        var walker = Assert.IsType<RoofWalkerBot>(night.Bots[1]);
        (walker.Me, walker.Calls) = (2, night.Calls);
        var train = night.Train;
        var toe = train.Line.Branches[night.Branch].Toe;
        night.World.SetSwitch(night.Branch, true);
        var state = train.Capture();
        train.Restore(state with { Rakes = [state.Rakes[0] with { Path = night.Branch, Distance = toe + 120, Velocity = 0 }] });
        night.Until(() => false, 5);
        night.Crew[0] = night.Crew[0] with { Health = 0, Death = DeathCause.Climbed };
        night.Until(() => train.OnMain && train.Dynamics.Distance > toe + 150, 900);
        var s = night.Crew[1];
        Assert.True(train.OnMain && train.Dynamics.Distance > toe + 150,
            $"stuck at {train.Dynamics.Distance:0} on {train.Dynamics.Path} (points for the dead line {train.Diverging(night.Branch)}); the relief {walker.Relieving} at {s.Parent}/{s.Surface}");
        Assert.True(s.Alive);
        Assert.True(PlayerMotor.InCab(s, train));
    }

    [Fact]
    public void TheReliefDriverTakesOnTheStopItsDriverDiedAt()
    {
        // Note 955 (the TBM's follow-up): the relief driver, a fresh driver, took the Foundry's spur for a dead line taken by
        // mistake and backed off it, leaving the crew at the loading to walk to the train where it stood (and ten minutes of
        // frontier:7 seed 10's dawn). Taking over down a stop's spur, it takes the stop on where its driver left it: the
        // loading, then out with the crew aboard, as the dead driver would have.
        var night = new Night(cars: 8, walkers: 1, modules: [ModuleKind.Crane, ModuleKind.Winch]);
        for (int i = 0; i < night.Bots.Count; i++)
            if (night.Bots[i] is RoofWalkerBot w)
                (w.Me, w.Calls) = (i + 1, night.Calls);
        var stops = night.Driver.Stops!;
        night.Until(() => stops.Doing == StopDriver.Leg.Loading, 900);
        Assert.Equal(StopDriver.Leg.Loading, stops.Doing);
        var plan = stops.Plan!;
        night.Until(() => false, 20);
        night.Crew[0] = night.Crew[0] with { Health = 0, Death = DeathCause.Climbed };
        var walkers = night.Bots.OfType<RoofWalkerBot>().ToList();
        StopDriver? relief = null;
        night.Until(() => (relief = walkers.FirstOrDefault(w => w.Relief?.Stops is not null)?.Relief!.Stops) is not null, 240);
        Assert.NotNull(relief);
        Assert.Equal(plan.Facility, relief.Plan?.Facility);
        bool backedOff = false;
        night.Until(() => relief.Doing == StopDriver.Leg.Cruise && night.Train.OnMain && night.Train.Dynamics.Distance > plan.Hold + 100, 900,
            () => backedOff |= relief.Doing is StopDriver.Leg.OffDeadLine or StopDriver.Leg.SetBack);
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {(c.Alive ? $"{c.Surface} on {c.Parent}" : "dead")}"));
        Assert.False(backedOff, "backed off the stop's spur as off a dead line");
        Assert.True(night.Train.OnMain && night.Train.Dynamics.Distance > plan.Hold + 100,
            $"never left: the relief {relief.Doing} at {night.Train.Dynamics.Distance:0} on {night.Train.Dynamics.Path}; crew {where}");
        Assert.All(night.Crew.Skip(1).Where(c => c.Alive), c => Assert.NotEqual(PlayerState.World, c.Parent));
        Assert.Contains(relief.Log, r => r.Facility == plan.Facility);
    }

    [Fact]
    public void TheShunterGoneForwardForTheControlsIsNoShunterToWaitFor()
    {
        // Note 955 (frontier:7 seed 10): the relief was the shunter, and still said so; its driving stood 150 s at the Foundry's
        // points (ShunterGiveUp) for a shunter to come and set them back, before it went down and did it itself. Gone forward
        // for the controls, it's heard as no part: the driving does the shunter's part alone, as a crew without one does.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(4000)])), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        var calls = new CrewCalls();
        var cab = PlayerMotor.SpawnInCab(train, P);
        calls.Say(0, StopJob.Driver, cab);
        var bot = new RoofWalkerBot(21, new StopHand(StopJob.Shunter, calls, 1)) { Me = 2, Calls = calls };
        var roof = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        bot.Decide(roof, world, 0, out _);
        Assert.True(calls.Has(StopJob.Shunter));
        calls.Say(0, StopJob.Driver, cab with { Health = 0, Death = DeathCause.Mauled });
        world.Tick++;
        bot.Decide(roof, world, 1, out _);
        Assert.True(bot.Relieving);
        world.Tick++;
        bot.Decide(roof, world, 2, out _);
        Assert.False(calls.Has(StopJob.Shunter));
    }
}
