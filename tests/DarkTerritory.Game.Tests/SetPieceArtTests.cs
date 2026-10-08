using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 note 398: GDD §18's set pieces drawn by the art pass where the sim lays them, moving as it says: the
/// elevator's loading bin over its spout, the steam lift's bin over its chute and its skip as far as it's wound, a lever
/// down while it's held, the hose stand, the slaughterhouse's pen and ramp.
/// </summary>
public class SetPieceArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly SceneArt Art = new(Look.Load(Content));

    static Site SiteOf(string route, FacilityKind kind)
    {
        var night = Sim.LineGen.Routes.Generate(Content, route, 6);
        var run = new Run(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), night);
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), night.Build());
        return run.Sites.First(s => s?.Feature.Facility == kind)!;
    }

    static IReadOnlyList<CarFrame> Cars() =>
        new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(5_000)])), 1_000).Frames;

    static List<MeshInstance> Drawn(Site site, Double3 eye)
    {
        var mesh = new MeshBuilder();
        Assert.True(Art.SetPieces(mesh, site, Cars(), eye, 0));
        return [.. mesh.Instances];
    }

    static Vector3 Origin(in MeshInstance i) => new(i.Model.M41, i.Model.M42, i.Model.M43);

    [Fact]
    public void TheElevatorsBinStandsOverItsSpoutAndItsLeverGoesDownWhilePouring()
    {
        var site = SiteOf("frontier:7", FacilityKind.GrainElevator);
        var eye = site.Spout;
        var bin = PropArt.Of(Look.Load(Content)).Get("spout_bin")!;
        var drawn = Drawn(site, eye);
        var placed = drawn.Single(i => i.Asset.Name == bin.Name);
        // Its origin on the ground under the sim's spout mouth.
        Assert.True(Vector3.Distance(Origin(placed), new Vector3(0, -5.2f, 0)) < 0.01f);
        float Lever(List<MeshInstance> d) => d.Single(i => i.Asset.Name.Contains("lever_handle")).Model.M12;
        float up = Lever(drawn);
        site.Mirror(new SiteState(true, 0, 0, false, false, 0) { Bin = 2, Pouring = true });
        float down = Lever(Drawn(site, eye));
        Assert.True(up > 0.3f && down < 0, $"the handle's rise {up:0.00} ready, {down:0.00} held");
    }

    [Fact]
    public void TheLiftsBinStandsOverItsChuteAndTheSkipRisesAsItsWound()
    {
        var site = SiteOf("deepTerritory:3", FacilityKind.MineHead);
        var eye = site.LiftChute;
        var drawn = Drawn(site, eye);
        Assert.True(Vector3.Distance(Origin(drawn.Single(i => i.Asset.Name.Contains("lift_works"))), new Vector3(0, -4.6f, 0)) < 0.01f);
        float low = Origin(drawn.Single(i => i.Asset.Name.Contains("ore_skip"))).Y;
        site.Mirror(new SiteState(true, 0, 0, false, false, 0) { Ore = 2, Wind = 0.8, Winding = true });
        float high = Origin(Drawn(site, eye).Single(i => i.Asset.Name.Contains("ore_skip"))).Y;
        Assert.InRange(high - low, 7.5f, 8.2f);
    }

    [Fact]
    public void TheHoseStandAndThePenAndRampAreTheArts()
    {
        var works = SiteOf("deepTerritory:2", FacilityKind.ChemicalWorks);
        var stand = Drawn(works, works.HoseStand).Single(i => i.Asset.Name.Contains("hose_stand"));
        Assert.True(Origin(stand).Length() < 0.01f);
        var slaughterhouse = SiteOf("frontier:3", FacilityKind.Slaughterhouse);
        var pen = Drawn(slaughterhouse, slaughterhouse.Pen);
        // Seven panels round the pen (open toward the track for the ramp), each out at its edge, and the ramp.
        var panels = pen.Where(i => i.Asset.Name.Contains("cattle_pen")).ToList();
        Assert.Equal(7, panels.Count);
        Assert.All(panels, p => Assert.InRange(Origin(p).Length(), 3.6f, 3.8f));
        Assert.Single(pen, i => i.Asset.Name.Contains("cattle_ramp"));
    }
}
