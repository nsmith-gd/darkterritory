using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The townsfolk keep to their ground (ARCHITECTURE §8 note 1057; note 692's handoff: "rounds walk straight between their
/// stops, so on a quarry or a hill town they may cross a bench's wall"): out of doors, every way a round walks, from a stop
/// through its approach to the next, steps no higher than a stride anywhere, in the town types that climb.
/// </summary>
public class TownRoundsGroundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static Town Night(string form, string spec)
    {
        var content = Towns with { Tuning = Towns.Tuning with { Forms = Towns.Tuning.Forms with { Force = form } } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return world.Town!;
    }

    [Theory]
    [InlineData("harbour", "frontier:7")]
    [InlineData("harbour", "local:5")]
    [InlineData("dykeland", "frontier:7")]
    [InlineData("dykeland", "local:5")]
    [InlineData("quarry", "frontier:7")]
    [InlineData("quarry", "local:5")]
    [InlineData("tidewater", "frontier:7")]
    [InlineData("tidewater", "local:5")]
    [InlineData("glasshouse", "frontier:7")]
    [InlineData("glasshouse", "local:5")]
    [InlineData("furnace", "frontier:7")]
    [InlineData("furnace", "local:5")]
    [InlineData("highline", "frontier:7")]
    [InlineData("highline", "local:5")]
    [InlineData("timber", "frontier:7")]
    [InlineData("timber", "local:5")]
    [InlineData("salt", "frontier:7")]
    [InlineData("salt", "local:5")]
    [InlineData("crag", "frontier:7")]
    [InlineData("crag", "local:5")]
    public void NobodysRoundStepsOverABenchOrATerracesWall(string form, string spec)
    {
        var town = Night(form, spec);
        var g = town.Ground;
        int ways = 0;
        var over = new List<string>();
        foreach (var p in town.Plan.People.Where(x => x.House < 0 && town.Rounds[x.Id] is not null))
        {
            var r = town.Rounds[p.Id]!;
            for (int k = 0; k < r.Stops.Count; k++)
            {
                var (from, to) = (r.Stops[k], r.Stops[(k + 1) % r.Stops.Count]);
                var points = new List<(double S, double D)> { (from.S, from.D) };
                points.AddRange(to.Via);
                points.Add((to.S, to.D));
                for (int i = 1; i < points.Count; i++)
                {
                    ways++;
                    if (!TownRounds.OnFoot(g, points[i - 1].S, points[i - 1].D, points[i].S, points[i].D))
                        over.Add($"{p.Role} {p.Id}: {points[i - 1].S:0.0},{points[i - 1].D:0.0} to {points[i].S:0.0},{points[i].D:0.0}");
                }
            }
        }
        Assert.True(ways > 50, $"only {ways} ways walked");
        Assert.True(over.Count == 0, $"{over.Count} of {ways} ways step a wall: {string.Join("; ", over.Take(5))}");
    }

    /// <summary>
    /// Street folk in front of a turned house (note 1057; note 692's handoff: "the street folk stand in front of their house
    /// (FrontD), not yet in front of a turned one"): a lamp-carrier out in front of their house, where it stands turned on
    /// a ring, stands before its turned front, looking out from it.
    /// </summary>
    [Theory]
    [InlineData("harbour", "frontier:7")]
    [InlineData("crag", "local:5")]
    public void TheStreetFolkFaceOutFromATurnedHousesFront(string form, string spec)
    {
        var town = Night(form, spec);
        int seen = 0;
        foreach (var p in town.Plan.People.Where(x => x.House < 0 && x.Pose == "lantern"))
        {
            // Their house: the one whose turned front they stand before.
            var h = town.Plan.Houses.Where(h => h.Turn != 0).OrderBy(h =>
            {
                var (fs, fd) = h.Body(0, -2.6);
                return Math.Abs(fs - p.S) + Math.Abs(fd - p.D);
            }).FirstOrDefault();
            if (h is null || h.Body(0, -2.6) is var (bs, bd) && Math.Abs(bs - p.S) + Math.Abs(bd - p.D) > 1.5)
                continue;
            seen++;
            var (os, od) = h.BodyFacing(0, -1);
            Assert.True(os * p.FaceS + od * p.FaceD > 0.99, $"{p.Role} {p.Id} faces {p.FaceS:0.00},{p.FaceD:0.00}, its house's front {os:0.00},{od:0.00}");
        }
        Assert.True(seen > 0, $"nobody out in front of a turned house in a {form} town");
    }
}
