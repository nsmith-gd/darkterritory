using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 998 (the director's playtest of 23 Oct, line 21: "I see a tree clipping through one of the walls"): the woods beside
/// the line were kept out of a fort or a walled town by their trunks alone, 3 m of room, so a crown leaned in over the wall.
/// A tree's crown is kept clear of every fort's and town's wall now, as the sim stands the trees and the art draws them.
/// </summary>
public class TownWallTreesTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    /// <summary>A night with both its towns, as the app has it: its forts, their walls and the woods beside the line.</summary>
    static (World World, LinesideProps Side) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - Tuning.Run.DepartShortOfGateM);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, Identities));
        return (world, LinesideProps.Of(route, train.Line)!);
    }

    /// <summary>The standing trees whose crowns reach into a fort's or a town's wall, kept out by <paramref name="kept"/>.</summary>
    static int Through(World world, LinesideProps side, Func<LinesideProp, bool> kept)
    {
        var walls = world.Train.Walls!.All.Where(w => w.Fort && w.Top - w.Bottom > 2).ToList();
        int n = 0;
        foreach (var p in side.Props(0, world.Train.Line.Length).Where(p => p.Kind == LinesideKind.Tree && !kept(p)))
        {
            var foot = side.World(p.Along, p.Lateral) with { Y = p.Ground };
            double r = side.SightRadius(p);
            foreach (var w in walls)
            {
                if (foot.Y + p.Height < w.Bottom || foot.Y > w.Top)
                    continue;
                var l = w.ToLocal(foot);
                if (Math.Abs(l.X) < w.HalfLength + r && Math.Abs(l.Z) < w.HalfWidth + r)
                {
                    n++;
                    break;
                }
            }
        }
        return n;
    }

    [Fact]
    public void NoTreesCrownReachesInOverATownsWall()
    {
        int before = 0;
        foreach (var spec in new[] { "frontier:7", "local:509", "deadLines:3", "frontier:3" })
        {
            var (world, side) = Night(spec);
            var forts = world.Forts!;
            before += Through(world, side, p => LinesideProps.InsideAFort(forts, p.Along, p.Lateral));
            Assert.Equal(0, Through(world, side, p => side.InAFort(forts, p)));
        }
        // The trunk's room alone let some through: what the director saw.
        Assert.True(before > 0, "no crown reached a wall under the old rule either: the check isn't looking where it should");
    }
}
