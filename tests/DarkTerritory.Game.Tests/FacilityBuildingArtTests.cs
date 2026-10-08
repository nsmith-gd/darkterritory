using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 notes 410, 420 and 422: the mine head's, the chemical works', the foundry's and the coaling tower's
/// buildings modelled (facility_pieces winding_house, spoil_heap, chem_works, pipe_rack, foundry_shed, coaling_tower) and
/// laid out round what the sim does there.
/// </summary>
public class FacilityBuildingArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static Site SiteOf(string route, FacilityKind kind)
    {
        var night = Sim.LineGen.Routes.Generate(Content, route, 6);
        var run = new Run(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), night);
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), night.Build());
        return run.Sites.First(s => s?.Feature.Facility == kind)!;
    }

    [Fact]
    public void TheMineHeadsBuildingsStandOffTheWinchsSledRunOnEitherSide()
    {
        // The winch's sleds come in from SledFrom to the track (GDD §18, facilities.json "winch"). Where GreyboxScene sets
        // the buildings' frame (25 m short of the layout down the spur, 4 out, +Z back along it), nothing of them (the
        // tip above all) may stand on that run, on whichever side of the spur they are.
        var site = SiteOf("deepTerritory:3", FacilityKind.MineHead);
        Assert.True(site.Spur >= 0);
        var foot = site.Track.Sample(site.Mid - 25);
        var right = Double3.Cross(foot.Tangent, Double3.Up).Normalized;
        var origin = foot.Position + right * (site.Side * 4.0);
        var d = site.SledFrom - origin;
        float z = (float)-Double3.Dot(d, foot.Tangent), far = (float)Math.Abs(Double3.Dot(d, right));
        Assert.InRange(far, 30, 60);
        foreach (int side in new[] { -1, 1 })
        {
            var piece = StructureKit.Facility(Look, FacilityKind.MineHead, side);
            var onRun = piece.Vertices.Count(v => Math.Abs(v.Position.Z - z) < 2.5f && v.Position.X * side is > 1 && v.Position.X * side < far + 2
                && v.Position.Y > 0.2f);
            Assert.True(onRun == 0, $"side {side}: {onRun} vertices on the sleds' run at z {z:0.0}, out to {far:0.0}");
        }
    }

    [Fact]
    public void TheChemicalWorksPipeRackRunsInBaysEndToEndAlongItsTanks()
    {
        // 12 m bays at 7 m out, their pipes 4.4-4.8 m up (the pieces are sunk 0.3), meeting at -12, 0 and 12 and ending at
        // ±24, from either side (the bays' own frames turn with the side, so they're set at s·z to land where they did).
        foreach (int side in new[] { -1, 1 })
        {
            var piece = StructureKit.Facility(Look, FacilityKind.ChemicalWorks, side);
            var pipes = piece.Vertices.Where(v => Math.Abs(v.Position.X * side - 7) < 1.2f && v.Position.Y is > 4.3f and < 5.0f)
                .Select(v => v.Position.Z).ToList();
            foreach (float joint in new[] { -24f, -12, 0, 12 })
                Assert.True(pipes.Any(p => Math.Abs(p - joint) < 0.7f), $"side {side}: no pipe at {joint} m");
            // The last bay's turned down short of 24.
            Assert.True(pipes.Max() is > 22.5f and < 24.5f, $"side {side}: the rack ends at {pipes.Max():0.0} m");
        }
    }
    [Fact]
    public void TheFoundrysCastingShedIsTheModelAndAFurnaceLightsItsWindows()
    {
        // Its windows glow at night as the kit's lit panels did (GDD §30 "dimly lit"): the bake's emissive mask, which the
        // cook flags on the layer's material (dt_glow).
        var model = Ballast.Assets.ModelLoader.Load(Path.Combine(Content, "art/models/props/foundry_shed.glb"));
        Assert.Contains(model.Materials, m => m.Glow >= 1);
        // And it's what stands there from either side: the stack behind it 40 m up, past the crane's yard.
        foreach (int side in new[] { -1, 1 })
        {
            var piece = StructureKit.Facility(Look, FacilityKind.Foundry, side);
            Assert.True(piece.Vertices.Max(v => v.Position.Y) > 39, $"side {side}: no stack");
        }
    }

    [Fact]
    public void TheCoalingTowersChuteHangsWhereTheSimPoursAndClearOfItsLever()
    {
        // GreyboxScene.Chute pours from 8.8 m up, 0.4 off the track on the tower's side, over the spout (Run.ChuteAt); the
        // lever stands 6 m along. The tower's frame is the feature's middle (on the main line, not pushed out).
        var night = Sim.LineGen.Routes.Generate(Content, "deepTerritory:8", 6);
        var line = night.Build();
        var run = new Run(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), night);
        var tower = night.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);
        var (_, lever) = run.ChuteAt(tower, line);
        var foot = line.Sample((tower.Start + tower.End) / 2);
        var right = Double3.Cross(foot.Tangent, Double3.Up).Normalized;
        var d = lever - foot.Position;
        var leverAt = new Vector3((float)Double3.Dot(d, right), (float)d.Y, (float)-Double3.Dot(d, foot.Tangent));
        var piece = StructureKit.Facility(Look, FacilityKind.CoalingTower, tower.Side);
        Assert.Contains(piece.Vertices, v => Math.Abs(v.Position.X * tower.Side - 0.4f) < 0.45f && v.Position.Y is > 8.3f and < 9.4f
            && Math.Abs(v.Position.Z) < 0.7f);
        Assert.DoesNotContain(piece.Vertices, v => Vector3.Distance(v.Position, leverAt) < 0.6f);
    }
}
