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

    /// <summary>
    /// The Car Hugger nights' line: deadLines:2's, found at 7700 m by its trestle past the tunnel, was a 24 km line; on the
    /// 7 km nights of note 554 it's deadLines:2 again, with Holt Bridge over its river at 1.7 km.
    /// </summary>
    internal const string HuggerNight = "deadLines:2";

    /// <summary>
    /// Where the walker nights start (a crew of three out on open line, the stops behind and ahead of them far enough off):
    /// frontier:7 from 2.5 km on its 24 km line; on the 7 km nights of note 554 that's its first stop, and frontier:3 is
    /// past its coaling tower at 2.9 km with 1.5 km of line before its next.
    /// </summary>
    internal static readonly (string Spec, double Start) OpenLine = ("frontier:3", 2900);

    /// <summary>
    /// Where the Car Hugger nights start: 270 m short of the middle of the line's trestle, where it lurks (`CarHugger.Spot`:
    /// a bridge 250–800 m ahead). Taken from the line, not pinned: note 278's bends moved deadLines:2's trestle 23 m nearer,
    /// inside the 250, and the Car Hugger never came.
    /// </summary>
    internal static double ShortOfTheTrestle(int cars = 6)
    {
        var route = LineGen.Routes.Generate(Content, HuggerNight, cars);
        var trestle = route.Of(Route.FeatureKind.Bridge).First();
        return Math.Round((trestle.Start + trestle.End) / 2 - 270);
    }

    /// <summary>A crew (of two, unless said) on a route, as `dt harness --insist` runs it (the combination sweep's night by hand).</summary>
    /// <param name="cranes">The mail cranes up, as these nights were found (sight.json mailCranes on for the night: the mail is
    /// cut for now, note 803); false: none, every other board as it is.</param>
    /// <summary>The departure load these nights were found with (run.json departureLoad before note 575).</summary>
    internal const double AsFound = 0.5;

    internal static HarnessReport Night(string routeName, int cars, double seconds, EnemyKind? insist, double? start = null,
        Action<World>? each = null, int bots = 2, int seed = 1, bool cranes = true, Train.UpkeepTuning? upkeep = null,
        EnemyTuning? enemies = null)
    {
        var route = LineGen.Routes.Generate(Content, routeName, cars);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = start ?? Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, cars, 1).LengthMetres);
        return Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Observe = each is null ? null : (_, _, world) => each(world),
            Bots = bots,
            Cars = cars,
            Seconds = seconds,
            Seed = seed,
            Link = new Ballast.Net.LinkConditions(0.09, 0.02, 0.03),
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = enemies ?? Tuning.Enemies,
            Route = route,
            // The night these seeds were found on: the cars half full from the fortress, as they left until note 575 (the
            // train's weight is the night's timing). What's tested is what a crew does in it, not what the train leaves with.
            Run = Tuning.Run with { DepartureLoad = AsFound },
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            // As these nights were found, before a through truss was posted low clearance (note 1002: deadLines:2's bridge the Car
            // Hugger lurks at is one, and the friend hauling from the roof would be down off it).
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File)) is var sight && !cranes
                ? sight with { MailCranes = false, TrussClearance = false } : sight with { MailCranes = true, TrussClearance = false },
            YardLength = gate,
            Upkeep = upkeep,
            Holdouts = Tuning.Holdouts,
            Insist = insist is { } k ? [k] : null,
            Look = DataFile.Load<BalanceTuning>(Path.Combine(Content, BalanceTuning.File)).Combinations.Look,
        }, Tuning.Boiler);
    }

    [Fact]
    public void ACrewOfTwosGunnerLentToTheWinchGoesRoundTheTrainToItsHandle()
    {
        // Note 486: frontier:7's first stop (seed 2) has a winch whose handles are on the far side of the spur from where the
        // shunter came down. Lent to the pair, it walked straight across at its handle, into car 3's side and up its steps,
        // for the six minutes the driver cranked alone; the winch turns only with both handles held, and hauled nothing. By
        // the foot path round the cars, it gets there, and the sleds come in. (On the 7 km nights of note 554 frontier:7's
        // first stop is a coaling tower: frontier:1's, a wreck yard, has the winch.)
        var route = LineGen.Routes.Generate(Content, "frontier:1", 6);
        double gate = route.GateOr(Tuning.Route.YardLength);
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 2,
            Cars = 6,
            Seconds = 900,
            Seed = 2,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, 6, 1).LengthMetres),
            Combat = Tuning.Combat,
            Route = route,
            // The night these seeds were found on: the cars half full from the fortress, as they left until note 575 (the
            // train's weight is the night's timing). What's tested is what a crew does in it, not what the train leaves with.
            Run = Tuning.Run with { DepartureLoad = AsFound },
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
        // Done with it inside the 900 s, with the winch's sleds in. Without the way round, it isn't done in this time.
        var stop = report.Stops!.FirstOrDefault(s => s.Legs.ContainsKey("Loading"));
        Assert.True(stop is not null, $"the stop's not done by {report.Seconds} s");
        Assert.True(stop.SledsHauled >= 1, $"{stop.SledsHauled} sleds hauled in {stop.Legs["Loading"]:0} s of loading");
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
        var report = Night(HuggerNight, 6, 90, EnemyKind.CarHugger, start: ShortOfTheTrestle(),
            each: world => latched |= world.ActiveEnemies.Any(e => e is CarHugger { Latched: true }));
        Assert.True(latched, "the Car Hugger never latched on");
        Assert.False(report.Threats!.DeathsByCause.ContainsKey(nameof(DeathCause.Eaten)), string.Join(", ", report.Threats.DeathsByCause));
    }
}
