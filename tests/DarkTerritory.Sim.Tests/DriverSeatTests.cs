using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 1009 (the director's playtest of 23 Oct, line 7: "It's sort of annoying that the bot just like takes over being the
/// like driver right away. I feel like that's something that like should maybe default to the player"): at the gate, with
/// someone playing aboard, the bot driver leaves the controls to them for facilities.json crew.peopleSeatSeconds before it
/// drives out; a ping of the cab sends it at once; a bot crew alone, or the flag at 0, drives out as before.
/// </summary>
public class DriverSeatTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed record Seen(double MovedAt, bool Underway);

    /// <summary>
    /// frontier:7's train at its departure gate as the app stands it, a bot driver in the cab and someone (playing or a bot)
    /// up on a roof, for <paramref name="seconds"/>; <paramref name="pingAt"/>: they ping the cab then. When it first moved off.
    /// </summary>
    static Seen Gate(bool person, double seconds, double? pingAt = null, FacilityTuning? facilities = null)
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7);
        var line = route.Build();
        double gate = route.GateOr(Tuning.Route.YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, gate - Tuning.Run.DepartShortOfGateM, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true, facilities ?? FacilityTests.F);
        var calls = new CrewCalls();
        var bot = new ConductorBot(calls, 0) { Me = 1 };
        var driver = PlayerMotor.SpawnInCab(train, P);
        var them = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        if (!person)
            calls.Bot(2);
        var c = new TrainControls { Reverser = 1, Brake = 1 };
        double movedAt = double.NaN;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            if (pingAt is { } at && i == (int)(at * SimConstants.TickRate))
                world.Pings.Add(new PingEvent(1, world.Tick, 2, PingKind.Cab, 0, train.Frames[0].ToWorld(default)));
            bot.Crewmates = [them];
            bot.Players = person ? [them] : [];
            var intent = bot.Decide(driver, world, world.Tick, out _);
            if (CabControls.CanDrive(driver, train) && CabControls.Clears(c, train, CabControls.ReleasesBrake(intent, driver, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, intent, driver, train);
            world.BeginTick();
            world.CrewAct(ref driver, intent, 1);
            world.Step(c);
            world.StepRun([driver, them]);
            PlayerMotor.Step(ref driver, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            if (double.IsNaN(movedAt) && train.Dynamics.Speed > 0.5)
                movedAt = i * SimConstants.TickSeconds;
        }
        return new Seen(movedAt, world.Run!.Phase != RunPhase.Yard);
    }

    [Fact]
    public void AtTheGateTheBotLeavesTheControlsToSomeonePlayingBeforeItDrivesOut()
    {
        double seat = FacilityTests.F.Crew.PeopleSeatSeconds;
        Assert.True(seat > 0, "the game's default leaves the seat to the people");
        var seen = Gate(person: true, seat + 30);
        Assert.True(seen.MovedAt >= seat, $"drove out {seen.MovedAt:0.0} s in, with the seat theirs for {seat:0} s");
        Assert.True(seen.Underway, "never drove out once nobody took the controls");
    }

    [Fact]
    public void APingOfTheCabSendsTheBotDrivingAtOnce()
    {
        var seen = Gate(person: true, 30, pingAt: 5);
        Assert.InRange(seen.MovedAt, 5, 15);
    }

    [Theory]
    [InlineData(false, 60)]
    [InlineData(true, 0)]
    public void ABotCrewAloneOrTheFlagOffDrivesOutAsBefore(bool person, double seat)
    {
        var f = FacilityTests.F with { Crew = FacilityTests.F.Crew with { PeopleSeatSeconds = seat } };
        var seen = Gate(person, 20, facilities: f);
        Assert.InRange(seen.MovedAt, 0, 10);
    }
}
