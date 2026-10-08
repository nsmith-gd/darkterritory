using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Bot nights that run hot (ARCHITECTURE §8 note 376; queue #113; GDD App. F.3, the director's playtest: "kept the train hot
/// the whole time and didn't stop for anything"): `dt harness --express` drives over the boards and the line's authority,
/// takes no stops, and brakes only for a bend it would come off on, so a bot night draws what a hot train draws.
/// </summary>
public class ExpressTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static (double Top, double MinAfterAway, double Km, bool Derailed) Night(double? express)
    {
        var route = LineGen.Routes.Generate(Content, "frontier:7", 6);
        double top = 0, minAway = double.MaxValue;
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 1,
            Cars = 6,
            Seconds = 420,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = Tuning.Run.DepartFrom(route.GateOr(Tuning.Route.YardLength), Train.Consist.Uniform(Tuning.Train, 6, 1).LengthMetres),
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)),
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Express = express,
            Observe = (tick, _, world) =>
            {
                double v = world.Train.Dynamics.Speed;
                top = Math.Max(top, v);
                if (tick > 90 * SimConstants.TickRate)
                    minAway = Math.Min(minAway, v);
            },
        }, Tuning.Boiler);
        return (top, minAway, report.TrainDistance / 1000, report.Threats?.Derailed ?? false);
    }

    [Fact]
    public void TheExpressDriverRunsOverTheBoardsTakesNoStopsAndStaysOnTheRails()
    {
        var hauling = Night(null);
        var hot = Night(21);
        // Frontier's line speed is 18 m/s (tiers.json): the hauling driver keeps under it; the express driver runs past it.
        Assert.True(hauling.Top < 18.5, $"hauling top {hauling.Top:0.0}");
        Assert.True(hot.Top > 20, $"hot top {hot.Top:0.0}");
        // Once away, it never stands (no stop worked), and it's further down the line than the hauling night.
        Assert.True(hot.MinAfterAway > 1, $"hot stood: {hot.MinAfterAway:0.0}");
        Assert.True(hot.Km > hauling.Km, $"hot {hot.Km:0.00} km, hauling {hauling.Km:0.00} km");
        Assert.False(hot.Derailed);
    }
}
