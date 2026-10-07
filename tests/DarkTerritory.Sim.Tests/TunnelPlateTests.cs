using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Linegen plan §13.3: every callable place has a name, and a tunnel's is "Number + name, plate on both portals"
/// (ARCHITECTURE §8 note 295).
/// </summary>
public class TunnelPlateTests
{
    [Fact]
    public void EveryNamedTunnelHasItsPlateOnBothPortals()
    {
        var content = DataFile.FindContentRoot();
        int tunnels = 0;
        foreach (var spec in new[] { "frontier:7", "deadLines:2", "deepTerritory:2", "deepTerritory:5" })
        {
            var plan = Routes.Generate(content, spec, 10).Plan!;
            foreach (var st in plan.Structures.Where(s => s.Type == StructureType.Tunnel))
            {
                Assert.NotNull(st.Name);
                var plates = plan.Signage.Where(s => s.Type == "tunnelPlate" && s.For == st.Id).OrderBy(s => s.S).ToList();
                // One on the portal a train runs into going up the line, facing it; one on the far portal, facing the other way.
                Assert.Equal(2, plates.Count);
                Assert.All(plates, p => Assert.True(p.Edge == st.Edge && p.Text == st.Name && p.Required && p.State == SignState.Intact));
                Assert.Equal(st.S0, plates[0].S, 1);
                Assert.Equal(1, plates[0].Side);
                Assert.Equal(st.S1, plates[1].S, 1);
                Assert.Equal(-1, plates[1].Side);
                tunnels++;
            }
        }
        Assert.True(tunnels > 0, "no tunnel on any of the lines");
    }
}
