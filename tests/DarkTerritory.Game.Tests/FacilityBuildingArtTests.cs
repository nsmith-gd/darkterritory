using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 notes 410, 420, 422, 427 and 461: the mine head's, the chemical works', the foundry's, the coaling tower's,
/// the wreck yard's, the military depot's and the switchyard's buildings modelled (facility_pieces winding_house, spoil_heap,
/// chem_works, pipe_rack, foundry_shed, coaling_tower, dead_boxcar, dead_gondola, loose_truck, yard_shed, nissen_hut,
/// wire_fence, powder_magazine, goods_shed) and laid out round what the sim does there.
/// </summary>
public class FacilityBuildingArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static Site SiteOf(string route, FacilityKind kind) => YardOf(route, kind).Site;

    static (Run Run, Sim.Rail.RailLine Line, Site Site) YardOf(string route, FacilityKind kind)
    {
        var night = Sim.LineGen.Routes.Generate(Content, route, 6);
        var line = night.Build();
        var run = new Run(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), night);
        run.EnableSites(DataFile.Load<FacilityTuning>(Path.Combine(Content, FacilityTuning.File)), line);
        return (run, line, run.Sites.First(s => s?.Feature.Facility == kind)!);
    }

    /// <summary>
    /// A down-the-spur site's buildings' frame, as GreyboxScene sets it (25 m short of the layout down the spur, 4 out on its
    /// side, +Z back along it): a world point in it (x out on the site's side), and a point of the kit's piece (x to the
    /// track's right, as the kit builds it for the side) back in the world.
    /// </summary>
    static (Func<Double3, Vector2> Local, Func<Vector3, Double3> World) FrameOf(Site site)
    {
        var foot = site.Track.Sample(site.Mid - 25);
        var right = Double3.Cross(foot.Tangent, Double3.Up).Normalized;
        var origin = foot.Position + right * (site.Side * 4.0);
        return (p => new((float)(Double3.Dot(p - origin, right) * site.Side), (float)-Double3.Dot(p - origin, foot.Tangent)),
            v => origin + right * v.X - foot.Tangent * v.Z + Double3.Up * v.Y);
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

    [Fact]
    public void TheWreckYardsDeadLieOffItsHeapsAndTheWinchsRunOnEitherSide()
    {
        // The heaps the crew work (facilities.json "wreck") and the winch's sled run are the sim's; the yard's older dead and
        // its loose trucks lie round them, whichever side of the spur the yard is on.
        var site = SiteOf("frontier:1", FacilityKind.WreckYard);
        Assert.True(site.Spur >= 0 && site.Heaps.Count > 0);
        var foot = site.Track.Sample(site.Mid - 25);
        var right = Double3.Cross(foot.Tangent, Double3.Up).Normalized;
        var origin = foot.Position + right * (site.Side * 4.0);
        Vector2 Local(Double3 p)
        {
            var d = p - origin;
            return new((float)(Double3.Dot(d, right) * site.Side), (float)-Double3.Dot(d, foot.Tangent));
        }
        var heaps = site.Heaps.Select(h => Local(h.Centre)).ToList();
        var sled = Local(site.SledFrom);
        foreach (int side in new[] { -1, 1 })
        {
            var piece = StructureKit.Facility(Look, FacilityKind.WreckYard, side);
            var standing = piece.Vertices.Where(v => v.Position.Y > 0.3f).Select(v => new Vector2(v.Position.X * side, v.Position.Z)).ToList();
            foreach (var h in heaps)
                Assert.True(standing.All(v => Vector2.Distance(v, h) > 4), $"side {side}: something within 4 m of the heap at {h}");
            Assert.True(standing.All(v => Math.Abs(v.Y - sled.Y) > 2.5f || v.X < 1 || v.X > sled.X + 2), $"side {side}: something on the sleds' run");
        }
    }

    [Fact]
    public void TheMilitaryDepotsHutsWireAndMagazineStandOffItsCratesAndTheWinchsRun()
    {
        // The depot's crates (the stack and the heavy ones the crew carry to the cars, facilities.json "crates") are stacked
        // in its wire's gate, by the watchtower, and the winch's sleds come in along the ground from SledFrom; nothing of the
        // depot stands on either, from either side of the spur.
        var (_, _, site) = YardOf("deepTerritory:3", FacilityKind.MilitaryDepot);
        Assert.True(site.Spur >= 0 && site.Has(ModuleKind.Crates) && site.Has(ModuleKind.Winch));
        var (local, _) = FrameOf(site);
        var crates = site.CrateStack.Concat(site.HeavyStack).Select(local).ToList();
        var sled = local(site.SledFrom);
        foreach (int side in new[] { -1, 1 })
        {
            var piece = StructureKit.Facility(Look, FacilityKind.MilitaryDepot, side);
            var standing = piece.Vertices.Where(v => v.Position.Y > 0.2f).Select(v => new Vector2(v.Position.X * side, v.Position.Z)).ToList();
            foreach (var c in crates)
                // (A heavy crate's 0.55 across, a light one less: the wire's cranks lean over to a metre off them, 1.8 m up.)
                Assert.True(standing.All(v => Vector2.Distance(v, c) > 0.9f), $"side {side}: something on the crate at {c}");
            Assert.True(standing.All(v => Math.Abs(v.Y - sled.Y) > 2.5f || v.X < 1 || v.X > sled.X + 2), $"side {side}: something on the sleds' run");
            // The wire's there, the modelled panels, and open at the gate the crates stand in.
            var wire = standing.Where(v => Math.Abs(v.X - 3.5f) < 0.6f).ToList();
            Assert.Contains(wire, v => v.Y < -20);
            Assert.Contains(wire, v => v.Y > 20);
            Assert.DoesNotContain(wire, v => v.Y is > -17.5f and < -6.5f);
        }
    }

    [Fact]
    public void TheSwitchyardsGoodsShedIsTheModelClearOfItsTracksAndItsOfficeLampIsLit()
    {
        // The office's lamp left burning (GDD §30 "dimly lit"): the bake's emissive mask, as the foundry's furnace is.
        var model = Ballast.Assets.ModelLoader.Load(Path.Combine(Content, "art/models/props/goods_shed.glb"));
        Assert.Contains(model.Materials, m => m.Glow >= 1);
        // The yard's tracks, where its cars stand to be fetched (note 187), and its own spur: the shed, its dock and its
        // canopy stand clear of all of them, on the yards of a few nights.
        foreach (var route in new[] { "frontier:1", "frontier:2", "frontier:4", "frontier:7" })
        {
            var (run, line, site) = YardOf(route, FacilityKind.Switchyard);
            var (_, world) = FrameOf(site);
            var rails = run.YardTracks(site.Index).Append(site.Spur).Distinct().SelectMany(b =>
            {
                var tr = line.Branches[b].Local;
                return Enumerable.Range(0, (int)(tr.Length / 2) + 1).Select(i => tr.Sample(Math.Min(tr.Length, i * 2.0)).Position);
            }).ToList();
            var piece = StructureKit.Facility(Look, FacilityKind.Switchyard, site.Side);
            Assert.True(piece.Vertices.Max(v => v.Position.Y) > 8, $"{route}: no shed's roof");
            var near = piece.Vertices.Where(v => v.Position.Y > 0.3f).Select(v => world(v.Position))
                .Count(p => rails.Any(s => Math.Abs(p.X - s.X) < 2.5 && Math.Abs(p.Z - s.Z) < 2.5));
            Assert.True(near == 0, $"{route}: {near} vertices within 2.5 m of a yard track");
        }
    }
}
