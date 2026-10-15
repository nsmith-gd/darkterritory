using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// What a fire leaves on a car's boards (note 763; the director, 13 Oct: "There's a weird brown circle leftover where fires
/// are"): burnt wood laid along the grain, not a flat disc.
/// </summary>
public class CharTests
{
    static readonly string Content = Environment.GetEnvironmentVariable("DT_CONTENT") is { Length: > 0 } c ? c : DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly Effects Fx = new(Look);
    // A floor cell 2 m along the car (Z) by 1.3 m across, just below and ahead of the eye.
    static readonly Vector3 At = new(0, -1.6f, -4), Along = new(0, 0, 1), Across = new(0.65f, 0, 0), Up = Vector3.UnitY;

    static MeshBuilder Char(float amount, int seed = 7)
    {
        var mesh = new MeshBuilder();
        Fx.Char(mesh, At, Along, Across, Up, amount, seed);
        return mesh;
    }

    [Fact]
    public void CharIsTheBurntWoodTextureLaidOverTheBoardsNotAFlatDisc()
    {
        int layer = Look.Layer("fx_char");
        Assert.True(layer >= 0, "the char's texture (tools/art/texgen/mat_fx.py fx_char)");
        var burnt = Char(1);
        // No solid of its own: alpha-blended decals of the char texture, which only ever darken what's under them.
        Assert.Equal(0, burnt.Vertices.Length);
        Assert.Empty(burnt.AdditiveFx);
        Assert.NotEmpty(burnt.AlphaFx);
        Assert.All(burnt.AlphaFx, v => Assert.Equal(layer, v.Layer));
        Assert.All(burnt.AlphaFx, v => Assert.True(v.Colour.X >= 0.99f && v.Colour.Y >= 0.99f && v.Colour.Z >= 0.99f, "untinted: the texture is soot's colour"));
        // Just off the boards, never through them.
        Assert.All(burnt.AlphaFx, v => Assert.InRange(v.Position.Y - At.Y, 0.005f, 0.05f));
    }

    [Fact]
    public void BurntOutRunsPastItsCellIntoTheNextAndAScorchStaysSmall()
    {
        static (float Along, float Across) Reach(MeshBuilder m) =>
            (m.AlphaFx.Max(v => MathF.Abs(v.Position.Z - At.Z)), m.AlphaFx.Max(v => MathF.Abs(v.Position.X - At.X)));
        var burnt = Char(1);
        var scorched = Char(0.15f);
        // Burnt out, it runs on past the cell's own edge (1 m along) into its neighbours', so cells run together.
        Assert.True(Reach(burnt).Along > 1.3f, $"reaches {Reach(burnt).Along} m along a 1 m half-cell");
        Assert.True(burnt.AlphaFx.Count > scorched.AlphaFx.Count, "more patches the more it burnt");
        Assert.True(Reach(scorched).Along < Reach(burnt).Along);
        // A scorch is fainter: its heart doesn't take the boards as a burnt-out cell's does.
        Assert.True(scorched.AlphaFx.Max(v => v.Colour.W) < burnt.AlphaFx.Max(v => v.Colour.W));
        // Barely burnt is nothing.
        Assert.Empty(Char(0.01f).AlphaFx);
    }

    [Fact]
    public void NoTwoCellsCharTheSame()
    {
        static string Shape(MeshBuilder m) => string.Join(",", m.AlphaFx.Select(v => $"{v.Position.X:F2}:{v.Position.Z:F2}:{v.Uv.X:F2}"));
        Assert.NotEqual(Shape(Char(0.8f, 1)), Shape(Char(0.8f, 2)));
        // The same cell draws the same every frame.
        Assert.Equal(Shape(Char(0.8f, 1)), Shape(Char(0.8f, 1)));
    }

    [Fact]
    public void TheStagedFireIsCutAsTheGameCutsItsCars()
    {
        // `dt screenshot --threats --view fire` and `--burnt car,s` stage their cells at enemies.json's cellSize: at the code's
        // default the counts differed and GreyboxScene.FireGrids drew neither the staged flames nor the char.
        var tuning = DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File));
        var fireTuning = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).CarFire;
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new Sim.Train.TrainOnLine(new Sim.Train.TrainDynamics(Sim.Train.Consist.Uniform(tuning, 6, 1)), line, 1200);
        var fire = Assert.Single(Staging.Threats(train, fireTuning: fireTuning).OfType<CarFire>());
        var grid = FireGrid.Of(train, fire.Attached, fireTuning.CellSize);
        Assert.NotNull(grid);
        Assert.Equal(grid.Count, fire.Heat.Length);
        Assert.Equal(grid.Count, train.Vehicles[fire.Attached].Char.Length);
        Staging.Charred(train, 2, fireTuning);
        var charred = train.Vehicles[2].Char;
        Assert.Equal(FireGrid.Of(train, 2, fireTuning.CellSize)!.Count, charred.Length);
        Assert.Contains(charred, c => c == (1 << CarFire.Bits) - 1);
        Assert.Contains(charred, c => c > 0 && c < 8);
    }
}
