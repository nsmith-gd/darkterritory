using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 470 (queue #206; the director, 8 Oct 2026, on the test build: "When turning into a yard, derailment is way too
/// easy. Don't allow derailments when turning into and leaving a yard."): a bend on a yard's track (a facility's spur, its
/// turnout's S-curve off the main line included) neither derails a train nor puts the bend warning up, at any speed.
/// </summary>
public class YardTurnTests
{
    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:2")]
    public void TakingAYardsTurnoutOverItsBendSpeedNeitherWarnsNorDerails(string spec)
    {
        var route = Routes.Generate(DataFile.FindContentRoot(), spec, 6);
        var line = route.Build();
        var plan = route.Plan!;
        double a = plan.Rules.ADerail;
        // The sharpest bend on any yard's track, off the main line: where it was easiest to come off.
        var (spur, s, k) = line.Branches.Where(b => b.Kind == BranchKind.Spur)
            .SelectMany(b => Enumerable.Range(0, 400).Select(i => b.Toe + 0.5 * i).Where(x => x < b.End)
                .Select(x => (Spur: b, S: x, K: Math.Abs(line.Sample(b.Index, x).Curvature))))
            .Where(p => TrackRules.InYard(line, p.Spur.Index, p.S)).MaxBy(p => p.K);
        double derails = Math.Sqrt(a / k);
        Assert.True(derails < 15, $"{spec}: its yards' sharpest bend derails only over {derails:0.0} m/s");
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, s + 8, Tuning.Boiler);
        var state = train.Capture();
        train.Restore(state with { Rakes = [state.Rakes[0] with { Path = spur.Index, Velocity = derails + 5 }] });
        var world = new World(train, Tuning.Combat);
        for (int i = 0; i < (Tuning.Train.Overspeed.LeadSeconds + 2) * SimConstants.TickRate; i++)
            Assert.Null(TrackRules.Step(world, plan, SimConstants.TickSeconds));
        Assert.False(world.Derailed);
        Assert.False(TrackRules.Assess(train, plan.Rules, Tuning.Train.Overspeed).Warning);
    }
}
