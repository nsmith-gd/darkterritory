using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD Part Eleven, open question 12: "At crew 2, which group-based enemies are still fair?" (note 305). Swept alone at
/// crews 2 and 4, two were not, both for what the bots did with only the driver to help: the Choir took the gunner from a
/// car whose door it had left open, and the gunner went down to club the Car Hugger with nobody to pull it from the mouth.
/// Each was half a crew of two, every night it came.
/// </summary>
public class CrewOfTwoTests
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>A crew of two on a route, as `dt harness --insist` runs it (the combination sweep's night by hand).</summary>
    static HarnessReport Night(string routeName, int cars, double seconds, EnemyKind insist, double? start = null,
        Action<World>? each = null)
    {
        var route = LineGen.Routes.Generate(Content, routeName, cars);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = start ?? Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, cars, 1).LengthMetres);
        return Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Observe = each is null ? null : (_, _, world) => each(world),
            Bots = 2,
            Cars = cars,
            Seconds = seconds,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0.09, 0.02, 0.03),
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
            Insist = [insist],
            Look = DataFile.Load<BalanceTuning>(Path.Combine(Content, BalanceTuning.File)).Combinations.Look,
        }, Tuning.Boiler);
    }

    [Fact]
    public void TheChoirFindsACrewOfTwosGunnerBehindAShutDoor()
    {
        // App. A.7: it seizes anyone outside or behind no shut door, and with the driver at the controls nobody can break it.
        // Gathered past halfway, the gunner gets indoors and shuts the doors first (it was catching the mail with the side
        // door open, and was seized there at 44 s).
        bool came = false;
        var report = Night("frontier:7", 10, 120, EnemyKind.Choir, each: world => came |= world.Choir.Present);
        Assert.True(came, "the Choir never came");
        Assert.False(report.Threats!.DeathsByCause.ContainsKey(nameof(DeathCause.Seized)), string.Join(", ", report.Threats.DeathsByCause));
        Assert.Equal(0, report.Deaths);
    }

    [Fact]
    public void ACrewOfTwosGunnerKeepsClearOfTheCarHuggersMouthAndLetsItTakeTheCar()
    {
        // App. A.3: whoever's in front of its mouth is swallowed, for friends to pull free, and one player can barely out-hit
        // it. With nobody near but the driver in the cab, the gunner went down to its car's platform to club it and was eaten
        // (deadLines:2, every crew-2 night). Now it goes up the train, clear of it.
        bool latched = false;
        var report = Night("deadLines:2", 6, 90, EnemyKind.CarHugger, start: 7700,
            each: world => latched |= world.ActiveEnemies.Any(e => e is CarHugger { Latched: true }));
        Assert.True(latched, "the Car Hugger never latched on");
        Assert.False(report.Threats!.DeathsByCause.ContainsKey(nameof(DeathCause.Eaten)), string.Join(", ", report.Threats.DeathsByCause));
    }
}
