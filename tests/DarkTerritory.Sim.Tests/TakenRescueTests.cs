using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 1021 (the director's playtest of 23 Oct, line 5: "I've been captured ... the bots aren't coming to save me ... I got
/// captured by the guy that exists between cars ... A Whistler got me ... they didn't try to rescue me"): a crewmate carried
/// off the train by a Whistler, or taken in a car by a Climber, is gone for by the one bot nearest (crewBots.rescueWithin,
/// rescueFit), and freed; the train's stood for it.
/// </summary>
public class TakenRescueTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning Quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

    static PlayerState InGap(Night n, int car)
    {
        var s = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
        s.Position = CrewSense.GapLocal(n.Train, car) with { Y = 0.9 };
        s.Surface = Surface.Coupler;
        return s;
    }

    static List<(int Id, PlayerState State)> Others(Night n, int id) => [.. n.Crew.Where(c => c.Key != id).Select(c => (c.Key, c.Value))];

    /// <summary>A Whistler watching car 3's gap at a stand, and someone playing (id 1) alone in it: carried off.</summary>
    static (Night, Whistler) Carried(int cars)
    {
        var n = new Night(cars, speed: 0, enemies: Quiet);
        var w = n.World.AddEnemy(id => Whistler.InGap(id, n.Train, 3, E.Whistler));
        n.Run(E.Whistler.WhistleAfterStop + E.Whistler.WhistleSeconds + 1);
        Assert.Equal(SpinePhase.Commit, w.Phase);
        n.Crew[1] = InGap(n, 3);
        n.Run(0.5);
        Assert.Equal(SpinePhase.Grab, w.Phase);
        Assert.Equal(1, w.Holding);
        return (n, w);
    }

    [Fact]
    public void ACrewmateAWhistlerCarriesOffIsFreedByTheNearestBot()
    {
        // Two walker bots on the roofs: car 2's (by the gap), and car 7's, further off. The nearer goes down after them and
        // clubs the Whistler off; the other stays aboard (one goes, not the whole crew).
        var (n, w) = Carried(8);
        var calls = new CrewCalls();
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 7, 0, P);
        foreach (int id in new[] { 2, 3 })
        {
            calls.Bot(id);
            calls.Role(id, BotRole.Walker);
        }
        var legs = new Dictionary<int, RoofWalkerBot> { [2] = new(2) { Me = 2 }, [3] = new(3) { Me = 3 } };
        var hands = new Dictionary<int, StopHand> { [2] = new(StopJob.None, calls, 2), [3] = new(StopJob.None, calls, 3) };
        PlayerIntent Crew(int id) => id == 1 ? default
            : Heed.Taken(legs[id].Decide(n.Crew[id], n.World, n.World.Tick, out _), n.Crew[id], n.World, id, hands[id], Others(n, id), calls);
        bool thirdNamed = false;
        for (int i = 0; i < (E.Whistler.NestSeconds + 15) * SimConstants.TickRate && w.Phase == SpinePhase.Grab; i++)
        {
            n.Run(SimConstants.TickSeconds, Crew);
            thirdNamed |= calls.Rescue(n.World, Others(n, 3), 3, n.Crew[3]) is not null;
        }
        Assert.NotEqual(SpinePhase.Grab, w.Phase);
        Assert.True(n.Crew[1].Alive, $"eaten ({n.Crew[1].Death}); the bot by the gap is {n.Crew[2].Surface} on {n.Crew[2].Parent}");
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
        Assert.Equal(PlayerState.World, n.Crew[2].Parent);
        Assert.False(thirdNamed, "the far bot was sent too");
        n.AssertFair();
    }

    [Fact]
    public void NoBotGoesFromFurtherThanTheRescueReachOrTooHurt()
    {
        var (n, _) = Carried(8);
        var calls = new CrewCalls();
        calls.Bot(2);
        calls.Bot(3);
        // Car 2's walker is hurt worse than rescueFit; car 7's is fit but out of reach of the nest's run (moved far down the line).
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Health = E.CrewBots.RescueFit - 1 };
        n.Crew[3] = PlayerMotor.SpawnOnGround(n.Train.Frames[7].ToWorld(new Double3(0, 0, E.CrewBots.RescueWithin + 40)), n.Train.Line, n.Train.Dynamics.Distance, P);
        Assert.Null(calls.Rescue(n.World, Others(n, 2), 2, n.Crew[2]));
        Assert.Null(calls.Rescue(n.World, Others(n, 3), 3, n.Crew[3]));
        // Fit, it's the one.
        n.Crew[2] = n.Crew[2] with { Health = P.Health };
        Assert.NotNull(calls.Rescue(n.World, Others(n, 2), 2, n.Crew[2]));
        Assert.Null(calls.Rescue(n.World, Others(n, 3), 3, n.Crew[3]));
    }

    [Fact]
    public void TheLoneDriverStandsTheTrainAndGoesAfterACrewmateAWhistlerCarriesOff()
    {
        // The director's campaign night: the director and one bot, the driver. Taken at a stand, the driver doesn't drive off
        // from under them: it gets down and goes after them, and is back in the cab after.
        var (n, w) = Carried(6);
        var calls = new CrewCalls();
        calls.Bot(2);
        calls.Role(2, BotRole.Driver);
        var driver = new ConductorBot(calls, 0) { Me = 2 };
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P);
        bool moved = false, out_ = false;
        for (int i = 0; i < (E.Whistler.NestSeconds + 30) * SimConstants.TickRate && (w.Phase == SpinePhase.Grab || driver.OutToRescue); i++)
        {
            driver.Crew = Others(n, 2);
            driver.Crewmates = [n.Crew[1]];
            driver.Players = [n.Crew[1]];
            var intent = driver.Decide(n.Crew[2], n.World, n.World.Tick, out _);
            if (CabControls.Clears(n.Controls, n.Train, CabControls.ReleasesBrake(intent, n.Crew[2], n.Train)))
                n.Controls.Brake = 0;
            CabControls.Apply(ref n.Controls, intent, n.Crew[2], n.Train);
            n.Run(SimConstants.TickSeconds, id => id == 2 ? intent : default, holdSpeed: false);
            moved |= n.Train.Dynamics.Speed > 0.3;
            out_ |= n.Crew[2].Parent == PlayerState.World;
        }
        Assert.True(out_, "the driver never went");
        Assert.NotEqual(SpinePhase.Grab, w.Phase);
        Assert.True(n.Crew[1].Alive, $"eaten ({n.Crew[1].Death}); the driver's {n.Crew[2].Surface} on {n.Crew[2].Parent}");
        Assert.False(moved, "the train moved while they were taken");
        Assert.False(driver.OutToRescue, $"the driver's still out: {n.Crew[2].Surface} on {n.Crew[2].Parent}");
        Assert.True(PlayerMotor.InCab(n.Crew[2], n.Train));
    }

    [Fact]
    public void ACrewmateAClimberTakesInACarIsFreedByTheNearestBotOnTheRoofs()
    {
        // A Climber in car 3 with someone playing alone in there: it takes them. The walker on car 5's roof goes along the
        // roofs to over it: outnumbered (App. A.4), it lets go.
        // (Left where it is, the Climber takes them: ClimberTests.InsideItTakesWhoeverComesInAlone.)
        var everyKind = EveryKind.Enemies;
        var n = new Night(6, speed: 12, enemies: everyKind with { Director = everyKind.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } });
        n.Train.Vehicles[3].ToggleDoor(0); // left open (note 633)
        var c = n.World.AddEnemy(id => Climber.Pacing(id, n.Train, 3, 1, everyKind.Climbers));
        n.Run(everyKind.Climbers.PaceSeconds + everyKind.Climbers.ScrabbleSeconds + 20);
        Assert.True(c.Inside);
        Assert.Equal(3, c.Attached);
        var room = n.Train.Frames[3].Shape.Interior!.Value;
        var alone = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        alone.Position = room.Centre with { Y = room.Min.Y };
        alone.Surface = Surface.Deck;
        n.Crew[1] = alone;
        n.Run(everyKind.Climbers.BiteEvery + 0.2);
        Assert.Equal(SpinePhase.Grab, c.Phase);
        var calls = new CrewCalls();
        calls.Bot(2);
        calls.Role(2, BotRole.Walker);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 5, 0, P);
        var hand = new StopHand(StopJob.None, calls, 2);
        // Standing where it is unless it's sent (a walker's own legs patrol the roofs, and may pass over it anyway).
        PlayerIntent Crew(int id) => id == 1 ? default : Heed.Taken(default, n.Crew[id], n.World, id, hand, Others(n, id), calls);
        for (int i = 0; i < everyKind.Climbers.TakeSeconds * SimConstants.TickRate && c.Phase == SpinePhase.Grab; i++)
            n.Run(SimConstants.TickSeconds, Crew);
        Assert.NotEqual(SpinePhase.Grab, c.Phase);
        n.Run(0.2, Crew);
        Assert.True(n.Crew[1].Alive, $"taken ({n.Crew[1].Death}); the walker's {n.Crew[2].Surface} on car {n.Crew[2].Parent}");
        Assert.False(n.Crew[1].Has(PlayerFlags.Held));
    }
}
