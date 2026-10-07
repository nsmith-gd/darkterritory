using Ballast;
using DarkTerritory.Game.LineGen;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The cab's run map and the route card put a stop where the train stops for it, and the train where it is (the director,
/// 7 Oct: "markers on the map in the train dont seem to align properly with stops on the route"; ARCHITECTURE §8 note 318).
/// </summary>
public class MapMarkerTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheRunMapMarksEachStopWhereTheTrainStandsAtIt()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        double length = line.PathLength(RailLine.MainPath);
        var stops = route.Features.Where(f => f.Kind is FeatureKind.Facility or FeatureKind.Village).ToList();
        Assert.NotEmpty(stops);
        foreach (var f in stops)
        {
            var stop = f.Stop!;
            // A halt's platform beside the main line: the mark is on the line at the stop point, not at the zone's start.
            var mark = GreyboxScene.MapStop(line, f, length);
            var stands = line.Sample(RailLine.MainPath, f.Start + stop.StopPoint.S).Position;
            double off = Math.Sqrt((mark.X - stands.X) * (mark.X - stands.X) + (mark.Z - stands.Z) * (mark.Z - stands.Z));
            Assert.True(off <= Math.Abs(stop.StopPoint.D) + 1, $"{f.Kind} at {f.Start:0}: marked {off:0} m from where the train stands");
            var start = line.Sample(RailLine.MainPath, f.Start).Position;
            Assert.True(stop.StopPoint.S < 50 || Math.Sqrt((mark.X - start.X) * (mark.X - start.X) + (mark.Z - start.Z) * (mark.Z - start.Z)) > 50,
                "not at the zone's start");
        }
    }

    [Fact]
    public void TheRouteCardTicksEachPlaceWhereTheTrainsPencilWillBeThere()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        var plan = route.Plan!;
        var line = route.Build();
        Assert.True(plan.GateM > 100, "the outer gate is well out from the line's start");
        foreach (var l in plan.Landmarks.Where(m => m.Edge == "main"))
        {
            var row = plan.RouteCard.Timetable.First(r => r.Text == l.Name);
            // The pencil's at the train's main-line distance over the line's length; the tick at the row's kilometre post.
            double pencil = l.S0 / line.Length, tick = PlanHud.ProfileAt(plan, line, row.Km);
            Assert.True(Math.Abs(pencil - tick) * line.Length <= 60, $"{l.Name}: ticked {(tick - pencil) * line.Length:0} m from the train there");
        }
    }
}
