using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The notice board drawn with what's posted on it (note 669; the director, 13 Oct: "we're going to want to have actual
/// like custom writing for this"): each sheet carries its own words, and the board stays a prop's size to draw.
/// </summary>
public class NoticeBoardArtTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());
    static readonly TownContent Towns = TownContent.Load(DataFile.FindContentRoot())!;

    [Fact]
    public void EachSheetCarriesItsOwnWordsAndTheBoardStaysAPropsSize()
    {
        var bare = SquareKit.Board(Look, []);
        // The longest notices any custom has, four of them, as full a board as a town puts up.
        var longest = Towns.Writing.Cultures.SelectMany(c => c.Board).OrderByDescending(n => n.Text.Length).Take(4).Select(n => (n.Title, n.Text)).ToList();
        var full = SquareKit.Board(Look, longest);
        var oneWord = SquareKit.Board(Look, [.. longest.Select(n => (n.Title, "Lost."))]);
        int Tris(MeshAsset m) => m.Vertices.Length / 3;
        // The words are drawn: a full sheet's ink is far more than a one-word one's.
        Assert.True(Tris(full) - Tris(bare) > 4 * (Tris(oneWord) - Tris(bare)) / 2, $"{Tris(full)} against {Tris(oneWord)}");
        Assert.True(Tris(full) <= ArtCatalog.LargeProp.MaxTriangles * 2, $"{Tris(full)} triangles for the board");
    }
}
