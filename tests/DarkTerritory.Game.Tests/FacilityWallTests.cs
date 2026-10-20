using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// What's drawn at a stop is what's walked into (note 758; the director, 14 Oct, `docs/playtest/2026-10-14.md` line 8: "We're
/// actually clipping a little bit through some of the ... things at the station"). A facility's buildings beyond its
/// machinery are the art's kit (StructureKit.Facility), not its stop's layout, and the coaling tower has no stop at all: none
/// had walls. Now each stands on the ground the art draws it on, at a crewmate's height, as the Sim's walls.
/// </summary>
public class FacilityWallTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    /// <summary>The kit's points at a crewmate's height that no wall holds (within a body's half-width), placed as GreyboxScene stands the kit.</summary>
    static (int Low, List<System.Numerics.Vector3> Open) Uncovered(PrototypeSession session, RouteFeature f)
    {
        var line = session.Train.Line;
        var walls = session.Train.Walls!;
        var spur = line.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe));
        RailLine on;
        double mid, side, push;
        if (spur is not null)
        {
            var site = session.World.Run!.Sites.FirstOrDefault(s => s?.Spur == spur.Index);
            (on, mid, side, push) = (spur.Local, (site?.Mid ?? spur.Local.Length - 45) - 25, spur.Side, 4);
        }
        else
            (on, mid, side, push) = (line, (f.Start + f.End) / 2, f.Side, 0);
        side = side == 0 ? 1 : side;
        var t = on.Sample(mid);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var at = t.Position + right * (side * push);
        var open = new List<System.Numerics.Vector3>();
        int low = 0;
        foreach (var v in StructureKit.Facility(Look, f.Facility, (int)side).Vertices)
        {
            if (v.Position.Y < 0.3f || v.Position.Y > 1.8f)
                continue;
            low++;
            var p = (at + right * v.Position.X - t.Tangent * v.Position.Z) with { Y = at.Y + v.Position.Y };
            if (!walls.Near(p).Any(w => w.ToLocal(p) is var l && Math.Abs(l.X) <= w.HalfLength + 0.35 && Math.Abs(l.Z) <= w.HalfWidth + 0.35))
                open.Add(v.Position);
        }
        return (low, open);
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deepTerritory:8")]
    public void AFacilitysBuildingsStandWhereTheyreDrawn(string spec)
    {
        var route = Sim.LineGen.Routes.Generate(Content, spec, 6);
        var session = new PrototypeSession(Content, route, 6, enemies: false);
        int checkedOn = 0;
        foreach (var f in route.Features.Where(f => f.Kind == FeatureKind.Facility && f.Facility != FacilityKind.CoalingTower))
        {
            var (low, open) = Uncovered(session, f);
            // All of it but where a module's worked inside it (StopWalls.Clear gives the ground round a work point way).
            Assert.True(open.Count <= low / 10, $"{spec} {f.Facility}: {open.Count} of {low} points at a crewmate's height walked through");
            checkedOn += low;
        }
        Assert.True(checkedOn > 100);
    }

    [Fact]
    public void TheCoalingTowersTrestleIsShutRoundAndOnlyItsHeapIsInside()
    {
        // deepTerritory:8 stands a tower (note 422). What a body could walk into is held; what's left is the coal spilled
        // inside its four faces, out of reach.
        var route = Sim.LineGen.Routes.Generate(Content, "deepTerritory:8", 6);
        var session = new PrototypeSession(Content, route, 6, enemies: false);
        var tower = route.Features.First(f => f.Kind == FeatureKind.Facility && f.Facility == FacilityKind.CoalingTower);
        var (low, open) = Uncovered(session, tower);
        // (Its posts, braces, footings and coal at a body's height: fewer since its ladder went, note 869.)
        Assert.True(low > 50, $"only {low} of its points at a crewmate's height");
        float side = tower.Side == 0 ? 1 : tower.Side;
        Assert.All(open, p => Assert.True(MathF.Abs(p.Z) < 4.2f && p.X * side > 3.8f && p.X * side < 10.2f, $"{p} is outside the trestle"));
    }
}
