using System.Numerics;
using Ballast.Render;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The gantry's work lamp isn't the repairs' marker (note 901; the director, 16 Oct: "a yellow circle on it ... that's the
/// same thing we're using right now for telling the player that the player needs to do something in order to sort of like
/// save the rig"). Its glow was a bare amber disc hung over the hook, sat on the casting from the crane's seat; now an iron
/// hood with a white bulb under it.
/// </summary>
public class WorkLampTests
{
    static float Saturation(Vector3 c)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z)), min = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        return max <= 0 ? 0 : (max - min) / max;
    }

    [Fact]
    public void TheWorkLampsGlowIsWhiteNotTheRepairsAmber()
    {
        var mesh = new MeshBuilder();
        GreyboxScene.WorkLamp(mesh, new Vector3(0, 0, -6));
        Assert.NotEmpty(mesh.AdditiveFx);
        Assert.True(Saturation(Effects.CalloutAmber) > 0.7f, "the repairs' amber is the saturated one");
        foreach (var v in mesh.AdditiveFx)
            Assert.True(Saturation(new Vector3(v.Colour.X, v.Colour.Y, v.Colour.Z)) < 0.3f, $"the lamp glows {v.Colour}, near the repairs' amber");
    }

    [Fact]
    public void FromTheCranesSeatOverItTheHoodHidesTheBulb()
    {
        var lamp = new Vector3(0, -3, -1);
        var mesh = new MeshBuilder();
        GreyboxScene.WorkLamp(mesh, lamp);
        float bulb = mesh.AdditiveFx.Max(v => MathF.Abs(v.Position.X - lamp.X));
        var hood = mesh.Vertices.ToArray();
        Assert.NotEmpty(hood);
        // Opaque, over the bulb and wider than it on every side: looked down on, it's the shade that's seen.
        Assert.True(hood.Min(v => v.Position.Y) >= lamp.Y - 0.001f, "the hood hangs below the bulb");
        Assert.True(hood.Max(v => v.Position.X) - lamp.X > bulb && lamp.X - hood.Min(v => v.Position.X) > bulb);
        Assert.True(hood.Max(v => v.Position.Z) - lamp.Z > bulb && lamp.Z - hood.Min(v => v.Position.Z) > bulb);
        Assert.True(mesh.AdditiveFx.All(v => v.Position.Y < hood.Max(h => h.Position.Y)));
    }
}
