using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T96 (playtest: "if I get off the train, it never stops for me to get back on"): the driver bot stops for a crewmate
/// left on the ground behind the train, sets back to them at yard speed, waits while they climb on, and goes on again.
/// </summary>
public class LeftBehindTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));

    [Fact]
    public void TheDriverStopsSetsBackForACrewmateLeftBehindAndGoesOnOnceTheyreAboard()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), Line, 5_000, Tuning.Boiler);
        train.Dynamics.Velocity = 12;
        var world = new World(train, Tuning.Combat);
        var bot = new ConductorBot(new CrewCalls(), 0);
        var driver = PlayerMotor.SpawnInCab(train, P);
        double at = train.Dynamics.RearDistance - 250;
        var them = PlayerMotor.SpawnOnGround(Line.Sample(RailLine.MainPath, at).Position + new Ballast.Double3(3, 0, 0), Line, at, P);
        var c = new TrainControls { Reverser = 1 };
        double slowest = double.MaxValue;
        void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                bot.Crewmates = [them];
                var intent = bot.Decide(driver, world, world.Tick, out _);
                if (CabControls.CanDrive(driver, train) && CabControls.Clears(c, train, CabControls.ReleasesBrake(intent, driver, train)))
                    c.Brake = 0;
                CabControls.Apply(ref c, intent, driver, train);
                world.BeginTick();
                world.CrewAct(ref driver, intent, 1);
                world.Step(c);
                PlayerMotor.Step(ref driver, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                slowest = Math.Min(slowest, train.Dynamics.Velocity);
            }
        }

        Run(150);
        Assert.True(slowest < -0.5, "it set back");
        Assert.InRange(train.Dynamics.RearDistance - at, -12, 25);
        Assert.True(train.Dynamics.Speed < 0.1, $"standing for them at {train.Dynamics.Speed:0.0} m/s");
        // They're on: forward again.
        them = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        Run(40);
        Assert.Equal(1, c.Reverser);
        Assert.True(train.Dynamics.Velocity > 3, $"under way again at {train.Dynamics.Velocity:0.0} m/s");
    }

    /// <summary>
    /// #613: the stand for someone left behind yields to the dawn, as a stop's wait for someone on the ground does (note 935):
    /// late for it (the reckoning that calls a stop's loading done), the driver goes on. frontier:4 seed 4's driver stood 120 s
    /// for a gunner stuck by a switch, and the dawn caught the train 6.6 km in. With the dawn far off it sets back as ever.
    /// </summary>
    /// <para>Note 995 (the director's playtest of 23 Oct, line 4: "I fell off the train. Is he going to stop for me? He's not
    /// going to stop for me"): only a bot is left to the dawn; someone playing is fetched whatever it.</para>
    [Theory]
    [InlineData(60, false, false)]
    [InlineData(400, true, true)]
    [InlineData(3000, false, true)]
    public void TheStandForSomeoneLeftBehindYieldsToTheDawn(double dawn, bool person, bool setsBack)
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7) with { DawnSeconds = dawn };
        var line = route.Build();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 4_000, Tuning.Boiler);
        train.Dynamics.Velocity = 12;
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, 600, authority: true);
        var bot = new ConductorBot(new CrewCalls(), 0);
        var driver = PlayerMotor.SpawnInCab(train, P);
        double at = train.Dynamics.RearDistance - 250;
        var them = PlayerMotor.SpawnOnGround(line.Sample(RailLine.MainPath, at).Position + new Ballast.Double3(3, 0, 0), line, at, P);
        var c = new TrainControls { Reverser = 1 };
        double slowest = double.MaxValue;
        // Out past the gate (the run's Yard phase holds the train for whoever's afoot, note 995's line 2).
        world.StepRun([driver, them]);
        for (int i = 0; i < 150 * SimConstants.TickRate; i++)
        {
            bot.Crewmates = [them];
            bot.Players = person ? [them] : [];
            var intent = bot.Decide(driver, world, world.Tick, out _);
            if (CabControls.CanDrive(driver, train) && CabControls.Clears(c, train, CabControls.ReleasesBrake(intent, driver, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, intent, driver, train);
            world.BeginTick();
            world.CrewAct(ref driver, intent, 1);
            world.Step(c);
            PlayerMotor.Step(ref driver, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            slowest = Math.Min(slowest, train.Dynamics.Velocity);
        }
        Assert.Equal(dawn < 1000, bot.Stops!.LateHere(world));
        if (setsBack)
            Assert.True(slowest < -0.5, $"never set back (slowest {slowest:0.0} m/s, at {train.Dynamics.Velocity:0.0} m/s, their gap {train.Dynamics.RearDistance - at:0} m)");
        else
        {
            Assert.True(slowest > 3, $"stopped for them (slowest {slowest:0.0} m/s)");
            Assert.True(train.Dynamics.Velocity > 3, $"going at {train.Dynamics.Velocity:0.0} m/s");
        }
    }
}
