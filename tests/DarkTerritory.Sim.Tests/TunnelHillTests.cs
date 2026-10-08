using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The hill over a bore is the land the line goes through (ARCHITECTURE §8 note 433): the cutting's walls carried on in
/// over the bore, up into the hill, and never less than the bore's cover. It was a mound of tunnelCoverM whatever the land
/// round it, so a bore through a mountain had a trench cut over it, its walls 100 m high and open to the sky at the portal.
/// </summary>
[Collection(nameof(LineGenTests))]
public class TunnelHillTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Theory]
    [InlineData("deadLines:3")]
    [InlineData("deepTerritory:2")]
    [InlineData("deadLines:8")]
    [InlineData("deepTerritory:4")]
    public void TheHillOverABoreIsTheLandTheCuttingGoesInto(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var terrain = ((PlanConditions)line.Conditions!).Terrain;
        var rules = route.Plan!.Rules.Terrain;
        double Over(double s, double lateral)
        {
            var t = line.Sample(s);
            var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
            return terrain.Height(at.X, at.Z) - t.Position.Y;
        }
        int bores = 0;
        foreach (var st in route.Plan.Structures.Where(x => x.Edge == "main" && x.Type == StructureType.Tunnel))
        {
            bores++;
            for (double s = st.S0 + 1; s < st.S1 - 1; s += 5)
            {
                // Covered: the bore's crown well under the ground over it.
                Assert.True(Over(s, 0) > rules.BoreCrownM + 5, $"{spec} {st.Name}: {Over(s, 0):0.0} m over the rail {s - st.S0:0} m in");
                // And not cut down, anywhere across, below the cutting's walls at both its portals: the hill goes on up
                // from them (the land's own lie varies along the line, a sidehill's ridge falling away, so a share of the
                // lower; Hensley's trench was a quarter of both).
                foreach (double lateral in new[] { -60.0, -30, -15, 15, 30, 60 })
                {
                    double walls = Math.Min(Over(st.S0 - 1, lateral), Over(st.S1 + 1, lateral));
                    Assert.True(Over(s, lateral) > 0.6 * walls - 2,
                        $"{spec} {st.Name}: {Over(s, lateral):0.0} m {s - st.S0:0} m in and {lateral} m out, the cutting's walls {walls:0.0} at its portals");
                }
            }
            // The cutting runs right up to the face: just short of it the bed is at rail height.
            Assert.InRange(Over(st.S0 - 0.5, 0), -0.4, 0.1);
            Assert.InRange(Over(st.S1 + 0.5, 0), -0.4, 0.1);
        }
        Assert.True(bores > 0, $"{spec} has no tunnel");
    }
}
