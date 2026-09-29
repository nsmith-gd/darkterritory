using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The water a generated line runs past (docs/design/maritime-rules.md): lakes that hold water beside the line, and
/// are crossed on a fill that keeps the formation at rail height; shores whose sea is wet past its edge; a dyked marsh
/// whose fields lie flat under the rail; a river alongside, under the rail. All of it plan data, the same every run.
/// </summary>
[Collection(nameof(LineGenTests))]
public class WatersideTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static (LinePlan Plan, Rail.RailLine Line, TerrainField Terrain) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        return (route.Plan!, line, ((PlanConditions)line.Conditions!).Terrain);
    }

    [Theory]
    [InlineData("local:5")]
    [InlineData("deadLines:1")]
    public void LakesHoldWaterAndACrossedOneIsCrossedOnTheFormation(string spec)
    {
        var (plan, line, terrain) = Night(spec);
        Assert.NotEmpty(plan.Lakes);
        foreach (var lake in plan.Lakes)
        {
            // Wet somewhere across it (a crossed lake's centre is under the fill).
            bool wet = false;
            for (double f = -0.8; f <= 0.8 && !wet; f += 0.2)
                wet |= terrain.WaterAt(lake.X + lake.Cos * f * lake.RadiusM * lake.Stretch, lake.Z + lake.Sin * f * lake.RadiusM * lake.Stretch) is not null;
            Assert.True(wet, $"{spec} {lake.Id} holds no water");
        }
        // Every metre of main line: the ballast either side is at rail height, lakes or not.
        for (double s = plan.GateM; s < plan.TerminusM; s += 20)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            if (terrain.WaterAt(t.Position.X + right.X * 20, t.Position.Z + right.Z * 20) is null
                && terrain.WaterAt(t.Position.X - right.X * 20, t.Position.Z - right.Z * 20) is null)
                continue;
            if (plan.Structures.Any(x => x.Edge == "main" && s > x.S0 - 10 && s < x.S1 + 10 && x.Type is StructureType.Truss or StructureType.Girder
                or StructureType.Trestle or StructureType.Viaduct or StructureType.Tunnel))
                continue;
            foreach (double l in new[] { -2.0, 2.0 })
                Assert.InRange(terrain.Height(t.Position.X + right.X * l, t.Position.Z + right.Z * l) - t.Position.Y, -0.35, 0.05);
        }
    }

    [Theory]
    [InlineData("local:3")]
    [InlineData("frontier:7")]
    [InlineData("frontier:2")]
    public void ShoresAreWetPastTheirEdgeAndDykedFieldsLieFlat(string spec)
    {
        var (plan, line, terrain) = Night(spec);
        Assert.NotEmpty(plan.Shores);
        var rules = plan.Rules.Terrain;
        foreach (var sh in plan.Shores)
            for (double s = sh.S0 + 100; s < sh.S1 - 100; s += 250)
            {
                var t = line.Sample(s);
                var sea = Double3.Cross(t.Tangent, Double3.Up).Normalized * sh.Side;
                // Just past the water's edge (the mud's, at low water), inside the corridor the terrain models and short of
                // where the sea's drowned drumlins come up as islands.
                double out_ = terrain.ShoreEdge(sh, s) + (sh.Kind == ShoreKind.Sea ? 15 : sh.FlatM + 15);
                if (sh.Kind != ShoreKind.River && out_ < rules.CorridorM)
                    Assert.True(terrain.WaterAt(t.Position.X + sea.X * out_, t.Position.Z + sea.Z * out_) is not null, $"{spec} {sh.Id} dry {out_:0} m out at {s:0}");
                if (sh.Kind == ShoreKind.Dyke)
                {
                    // The fields, landward and between the line and the dyke: the low bank's depth under the rail.
                    foreach (double l in new[] { -60.0, 25.0 })
                    {
                        double h = terrain.Height(t.Position.X + sea.X * l, t.Position.Z + sea.Z * l) - t.Position.Y;
                        if (plan.Structures.Any(x => x.Edge == "main" && s > x.S0 - 150 && s < x.S1 + 150))
                            continue;
                        Assert.InRange(h, -rules.Dykes.FieldsBelowRailM - 0.4, -rules.Dykes.FieldsBelowRailM + 0.4);
                    }
                }
            }
    }

    [Fact]
    public void TheWatersideIsPartOfThePlanAndTheSameEveryRun()
    {
        var a = Routes.Generate(Content, "deadLines:3", 6).Plan!;
        var b = Routes.Generate(Content, "deadLines:3", 6).Plan!;
        Assert.Equal(a.Lakes, b.Lakes);
        Assert.Equal(a.Shores, b.Shores);
        // And a client's copy round-trips it.
        var c = LinePlan.FromJson(a.ToJson());
        Assert.Equal(a.Lakes, c.Lakes);
        Assert.Equal(a.Shores, c.Shores);
        Assert.Contains(a.Shores, s => s.Kind == ShoreKind.River);
    }
}
