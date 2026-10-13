using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The lineside boards' lettering (ARCHITECTURE §8 note 560): the sim sizes a board's face for its words
/// (<see cref="BoardLayout"/>) in the HUD's font as the art letters it, so the face it keeps clear in front of is the face
/// that's drawn, and the words fit on it.
/// </summary>
public class SignKitTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    [Fact]
    public void TheSimLaysBoardsOutInTheHudsOwnFont()
    {
        var font = BitmapFont.Default;
        Assert.Equal(BoardLayout.GlyphWidth, font.Width);
        Assert.Equal(BoardLayout.GlyphHeight, font.Height);
        Assert.Equal(BoardLayout.Advance, font.Advance);
        Assert.Equal(BoardLayout.LineHeight, font.LineHeight);
    }

    [Theory]
    [InlineData("facility", "Voss Grain Elevator 2.0 KM")]
    [InlineData("stationName", "HENSLEY CROSSING")]
    [InlineData("speedBoard", "20")]
    [InlineData("lineClosed", "LINE CLOSED")]
    [InlineData("tunnelPlate", "Tunnel 2 - Fenwick Heath")]
    public void ABoardsWordsFitItsFaceAtTheirSize(string type, string text)
    {
        var signage = Look.Signage!;
        var def = signage.Boards[type];
        var face = BoardLayout.Of(type, text, def, signage.Billboard);
        // A distance keeps its unit; a long name goes over lines no longer than the board's.
        Assert.DoesNotContain(face.Lines, l => l == "KM");
        Assert.All(face.Lines, l => Assert.True(l.Length <= def.LineChars || !l.Contains(' '), l));
        if (type != "tunnelPlate")
            Assert.Equal(def.LetterM, face.Pixel * BoardLayout.GlyphHeight, 3);
        else
            Assert.True(face.Width <= def.WidthM + 1e-6, "a plate fits across its portal");
        var mesh = SignKit.Board(Look, type, face, 0.9f, 0.3f);
        // The lettering (in front of the enamel) within the face.
        var letters = mesh.Vertices.Where(v => v.Position.Z > 0.02f).ToList();
        Assert.NotEmpty(letters);
        Assert.All(letters, v =>
        {
            Assert.InRange(v.Position.X, -face.Width / 2 - 1e-3, face.Width / 2 + 1e-3);
            Assert.InRange(v.Position.Y, -1e-3, face.Height + 1e-3);
        });
        // Its paler paint is the reflective one: it shines in the lamp, the other far less.
        Assert.Contains(mesh.Vertices, v => v.Emissive > 0.85f);
        Assert.Contains(mesh.Vertices, v => v.Emissive is > 0 and < 0.5f);
    }
}
