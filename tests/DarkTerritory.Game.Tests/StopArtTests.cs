using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The generated stops as the art pass draws them (level-design P1-P14): the ground under a stop is level, since the sim
/// walks people out to its village at rail height; and the lineside cells round a stop build its buildings.
/// </summary>
public class StopArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    // The route generator's night and the line generator's (the game's default, PlanStops), whose ground is the plan's.
    static readonly Route Legacy = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
    static readonly Route Generated = DarkTerritory.Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);

    public static TheoryData<string> Nights => ["legacy", "generated"];

    static Route NightOf(string which) => which == "legacy" ? Legacy : Generated;

    [Theory]
    [MemberData(nameof(Nights))]
    public void TheGroundUnderAStopIsLevelOutToItsVillage(string which)
    {
        var night = NightOf(which);
        var stops = night.Features.Where(f => f.Stop is not null).ToList();
        Assert.NotEmpty(stops);
        foreach (var f in stops)
            foreach (var b in f.Stop!.Buildings)
            {
                double s = f.Start + b.S;
                Assert.Equal(1, WorldArt.Flat(night, s));
                // Past the formation's ditch the profile is rail height; the hills are levelled away.
                if (Math.Abs(b.D) > 6)
                    Assert.InRange(WorldArt.Ground(night, s, (float)b.D, 18), -0.01f, 0.01f);
            }
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void EveryBuildingOfAStopStandsInItsCell(string which)
    {
        var night = NightOf(which);
        var art = new WorldArt(Look.Load(Content));
        var line = night.Build();
        var yard = night.Features.First(f => f.Stop is { HasYard: true, HasVillage: true });
        var eye = line.Sample(yard.Start).Position;
        var mesh = new MeshBuilder();
        art.Lineside(mesh, line, night, eye, yard.Start - 250, yard.End + 250, 7, 18);
        var tall = mesh.Vertices.ToArray().Where(v => v.Position.Y > 2.5f).Select(v => v.Position).ToList();
        // (Not a shed a gantry's cut runs the length of: no roofed length is left of it, so the sim stands no walls there
        // and the art draws only the cut's floor, note 387.)
        bool cutAway(int i) => yard.Stop!.Buildings[i].Kind is BuildingKind.Shed or BuildingKind.Hero && !Sim.Run.StopWalls.Roofed(yard.Stop, i).Any();
        foreach (var b in yard.Stop!.Buildings.Where((b, i) => b.Kind != BuildingKind.Well && !cutAway(i)))
        {
            // Walls up over its footprint: the trees are kept off it, so what stands there is the building.
            var at = Sim.Run.Run.StopWorld(line, yard, b.Centre).RelativeTo(eye);
            double reach = double.Hypot(b.Length, b.Width) / 2 + 0.5;
            Assert.True(tall.Any(p => double.Hypot(p.X - at.X, p.Z - at.Z) <= reach), $"{b.Kind} at {b.S:0},{b.D:0} isn't drawn");
        }
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void AYardsPilesLieAboutItsBuildingsAndOffItsTracks(string which)
    {
        // Note 325's second slice: the railway's leavings round a stop's works buildings, never on a track (the main line
        // or a yard's) and close by the building they're dumped against.
        var night = NightOf(which);
        var line = night.Build();
        int total = 0;
        foreach (var f in night.Features.Where(f => f.Stop is { HasYard: true }))
            for (int i = 0; i < f.Stop!.Buildings.Count; i++)
            {
                var b = f.Stop.Buildings[i];
                var centre = Sim.Run.Run.StopWorld(line, f, b.Centre, 0);
                foreach (var p in WorldArt.YardPiles(line, night, f, i, 18))
                {
                    total++;
                    Assert.True(NearestTrack(line, p.World, f.Start + b.S) >= 3.1, $"{p.Kind} by {b.Kind} at {f.Start + b.S:0} is on a track");
                    double reach = double.Hypot(b.Length, b.Width) / 2 + 2;
                    Assert.True(double.Hypot(p.World.X - centre.X, p.World.Z - centre.Z) <= reach, $"{p.Kind} strays from its {b.Kind}");
                }
            }
        Assert.True(total >= 6, $"only {total} piles in the night's yards");
    }

    /// <summary>How far <paramref name="p"/> is from the nearest track centre: the main line for 300 m about it, every branch whole.</summary>
    static double NearestTrack(DarkTerritory.Sim.Rail.RailLine line, Double3 p, double near)
    {
        double best = double.MaxValue;
        void Try(Double3 q) => best = Math.Min(best, double.Hypot(q.X - p.X, q.Z - p.Z));
        for (double s = Math.Max(0, near - 300); s <= Math.Min(line.Length, near + 300); s += 0.5)
            Try(line.Sample(s).Position);
        foreach (var b in line.Branches)
            for (double s = 0; s <= b.Local.Length; s += 0.5)
                Try(b.Local.Sample(s).Position);
        return best;
    }
}
