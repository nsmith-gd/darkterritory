using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 619 (D1.5's case for queue #350): at frontier:12's first stop (seed 1, a switchyard), the two walkers in car 1 put
/// its lamp out for the Fire Flies with the lamp key pressed every other tick, the key a toggle and their view of the lamp a
/// snapshot behind: the presses still in flight lit it again, the flies came back to it every 30 to 40 s, and the walkers
/// (sat at car 1's side door for a mail bag the whole while, the train standing) never went to their parts. The 240 s the
/// driver holds for them ran out and the stop was given up.
/// </summary>
public class LampTurnTests
{
    [Fact]
    public void TwoHandsAtOneLampPressHalfASecondApartAndOnceARoundEach()
    {
        var pressed = new Dictionary<int, List<uint>> { [2] = [], [3] = [] };
        for (uint t = 0; t < 4 * SimConstants.TickRate; t++)
            foreach (var (me, ticks) in pressed)
                if (Heed.LampTurn(t, me))
                    ticks.Add(t);
        Assert.All(pressed.Values, ticks => Assert.Equal(2, ticks.Count));
        var all = pressed.Values.SelectMany(t => t).Order().ToList();
        Assert.All(all.Zip(all.Skip(1)), p => Assert.True(p.Second - p.First >= SimConstants.TickRate / 2, $"{p.First} and {p.Second}"));
    }

    [Fact]
    public void FrontierTwelvesFirstStopIsWorkedNotGivenUp()
    {
        var content = DataFile.FindContentRoot();
        const int cars = 10;
        var route = LineGen.Routes.Generate(content, "frontier:12", cars);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, cars, 1).LengthMetres);
        double? spurAt = null;
        // As `dt harness --route frontier:12 --bots 4 --enemies --upkeep --seed 1` hosts it: the lossy link is the lag.
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 4,
            Cars = cars,
            Seconds = 420,
            // In at the switch (off the main onto the yard's spur, path 0 on): the stop's being worked.
            Until = world => (spurAt ??= world.Train.Dynamics.Path >= 0 ? world.Run?.Seconds : null) is not null,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0.09, 0.02, 0.03),
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Upkeep = DataFile.Load<Train.UpkeepTuning>(Path.Combine(content, Train.UpkeepTuning.File)),
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<FacilityTuning>(Path.Combine(content, FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
        Assert.True(spurAt is not null, $"never in at the switch: {string.Join("; ", (report.Stops ?? []).Select(s => string.Join(", ", s.Legs.Select(l => $"{l.Key} {l.Value:0}"))))}");
    }
}
