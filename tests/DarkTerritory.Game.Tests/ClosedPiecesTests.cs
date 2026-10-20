using System.Text.RegularExpressions;
using Ballast;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The kit's buildings are closed (queue #596, ARCHITECTURE §8 note 869; the director's playtest of 19 Oct, line 7: "LD
/// assets next to tracks, houses and such aren't 3D they are missing sides of geo"). The renderer culls back faces, so a
/// side a piece leaves out, or one wound the wrong way, is a hole into it from that side: the modelled houses' gable ends
/// (tools/models town_houses.py) were wound in, and the gatehouse's left gate leaf was built inside out. Every building the
/// catalog has, a house, shed, hall, tower, gate or works, shows next to nothing of its inside from all round it
/// (<see cref="MeshOpenness"/>; `dt art closed` lists every piece).
/// </summary>
public partial class ClosedPiecesTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [GeneratedRegex("house|shed|hut|barn|cottage|gate|church|chapel|station|tower|mill|works|cabin|store|hall|warehouse|kiosk|signal|lockup|magazine|silo|elevator|foundry|slaughter|winding|shack|office|platform")]
    private static partial Regex Building();

    [Fact]
    public void EveryBuildingShowsNoneOfItsInsideFromOutside()
    {
        var look = Look.Load(Content);
        var train = DataFile.Load<DarkTerritory.Sim.Train.TrainTuning>(Path.Combine(Content, DarkTerritory.Sim.Train.TrainTuning.File));
        var pieces = ArtCatalog.Entries(look, train).Where(e => Building().IsMatch(e.Name)).ToList();
        Assert.True(pieces.Count >= 40, $"only {pieces.Count} buildings in the catalog");
        foreach (var e in pieces)
        {
            var (inside, hits, from) = MeshOpenness.SeenInside(e.Make());
            Assert.True(inside <= 0.02, $"{e.Name}: {inside:P0} of {hits} looks at it see into it (worst from {from.X:0.0}, {from.Z:0.0})");
        }
    }

    [Fact]
    public void ABoxInsideOutIsSeenInto()
    {
        // The measure itself: a box as the kit builds it is closed; given its corners the wrong way round, it's all inside.
        var k = new Kit(null);
        k.Box(new System.Numerics.Vector3(-1, 0, -1), new System.Numerics.Vector3(1, 2, 1));
        Assert.Equal(0, MeshOpenness.SeenInside(k.Build("box")).Inside, 3);
        var turned = new Kit(null);
        turned.Box(new System.Numerics.Vector3(1, 0, -1), new System.Numerics.Vector3(-1, 2, 1));
        Assert.True(MeshOpenness.SeenInside(turned.Build("turned")).Inside > 0.9);
    }
}
