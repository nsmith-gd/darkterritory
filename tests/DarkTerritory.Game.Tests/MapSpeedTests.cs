using Ballast;
using DarkTerritory.Game.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The director, 7 Oct (on the HUD overhaul, note 285): keep "max speed on rail sections on the map that can't handle top
/// speed without derailment". Every stretch of the main line the engine would come off at its top speed has its figure on
/// the map (PostedSpeeds: the route card's profile and the cab's run map ink the same list).
/// </summary>
public class MapSpeedTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Theory]
    [InlineData(RouteTier.Local, 3UL)]
    [InlineData(RouteTier.Frontier, 7UL)]
    [InlineData(RouteTier.DeadLines, 5UL)]
    [InlineData(RouteTier.DeepTerritory, 2UL)]
    public void EveryStretchThatDerailsUnderTopSpeedHasItsFigureOnTheMap(RouteTier tier, ulong seed)
    {
        var route = Sim.LineGen.Routes.Generate(Content, tier, seed, 6);
        var plan = route.Plan!;
        var line = route.Build();
        double top = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File)).MaxSpeed;
        var posted = PostedSpeeds.Of(plan, line, line.Length);
        // Each 5 m the engine would come off at full speed, and whether a figure on the map covers it (its posted
        // stretch, with the board's lead-in and the bend's run-out either side).
        var bare = new List<double>();
        for (double s = 0; s <= line.Length; s += 5)
        {
            double k = Math.Abs(line.Sample(RailLine.MainPath, s).Curvature);
            if (k > 1e-9 && Math.Sqrt(plan.Rules.ADerail / k) < top && !posted.Any(p => s >= p.S0 - 60 && s <= p.S1 + 60))
                bare.Add(s);
        }
        Assert.True(bare.Count == 0, $"{route.Name}: {bare.Count * 5} m that derail under {top * 3.6:0} km/h have no figure on the map, " +
            $"first at {string.Join(", ", bare.Take(8).Select(s => $"{s / 1000:0.00} km"))}");
        // And each figure is under the speed that'd have it off there.
        foreach (var (s0, s1, kmh, why) in posted.Where(p => p.Why is null))
        {
            double kMax = Enumerable.Range(0, (int)((s1 - s0) / 5) + 1).Max(i => Math.Abs(line.Sample(RailLine.MainPath, s0 + 5 * i).Curvature));
            Assert.True(kmh / 3.6 < Math.Sqrt(plan.Rules.ADerail / kMax) + 0.5, $"{route.Name}: the {kmh} km/h figure at {s0 / 1000:0.00} km is over its bend's derailing speed");
        }
    }
}
