using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's 23 Oct playtest, line 21 (note 1002): "I see a tree clipping through one of the walls." A tree beside a fort
/// or a walled town stands clear of its wall by its crown as the art draws it (<see cref="LinesideProp.Crown"/>), not only its
/// trunk, so no bough comes through the stone; the same night's seed, the same trees on every machine.
/// </summary>
[Collection(nameof(LineGenTests))]
public class TreesClearOfWallsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("frontier:3")]
    [InlineData("local:5")]
    [InlineData("deadLines:3")]
    public void NoTreesCrownReachesOverAWall(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - Tuning.Run.DepartShortOfGateM);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, Identities));
        var forts = world.Forts!;
        Assert.Contains(forts, f => f.Bounds is not null);
        var side = LinesideProps.Of(route, train.Line)!;
        var standing = side.Props(0, train.Line.Length).Where(p => p.Kind == LinesideKind.Tree && !LinesideProps.InsideAFort(forts, p.Along, p.Lateral, p.Crown)).ToList();
        Assert.NotEmpty(standing);
        foreach (var p in standing)
            foreach (var f in forts)
            {
                // Its crown nowhere over the fort's walls (WallOut either side, Start to End) or a walled town's.
                bool overFort = Math.Abs(p.Lateral) - p.Crown < Fortresses.WallOut + 1.7 && p.Along + p.Crown > f.Start - 2 && p.Along - p.Crown < f.End + 2;
                Assert.False(overFort, $"{spec}: a {p.Height:0} m {p.Species} at {p.Along:0}, {p.Lateral:0.0} reaches over the fort's wall ({f.Start:0}-{f.End:0})");
                Assert.False(f.InTown(p.Along, p.Lateral, 3 + p.Crown), $"{spec}: a {p.Height:0} m {p.Species} at {p.Along:0}, {p.Lateral:0.0} reaches over a walled town's wall");
            }
    }
}
